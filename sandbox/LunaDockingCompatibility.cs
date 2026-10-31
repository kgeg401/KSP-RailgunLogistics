using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using LmpClient.Systems.VesselCoupleSys;
using LmpClient.Systems.VesselUndockSys;
using LmpClient.Systems.VesselPositionSys;
using LmpClient.Extensions;
using LmpClient.Systems.Lock;
using LmpClient.Systems.SettingsSys;
using LmpClient.Systems.VesselRemoveSys;
using LmpClient.VesselUtilities;
using LmpCommon.Message.Data.Vessel;
using LmpCommon.Enums;
using LmpCommon.Message.Interface;
using UnityEngine;

namespace RailgunSandbox
{
    // Installed Luna 0.29.2 calls its post-coupling camera callback even when
    // the coupling routine returned early for a locally owned receiver. Its
    // cached dominant Vessel can then be null or belong to an earlier event.
    // Guard that callback only for the assigned logistics docking-port pair.
    // Replicate the pilot's actual native capture through stock docking events.
    // Luna's low-level DockToVessel omits both port FSMs.
    public static class LunaDockingCompatibility
    {
        sealed class Scope { public Scope Previous; public Guid Dominant, Weak; public bool Known; public PodRecord Pod; public double Age; }
        [ThreadStatic] static Scope current;
        static bool installed;
        public static bool Active => installed;
        sealed class DeferredRemoval { public Guid Vessel; public string Pod; public float Until; }
        static readonly Dictionary<Guid,DeferredRemoval> removals=new Dictionary<Guid,DeferredRemoval>();
        static FieldInfo dominant,activeDominant;
        public static void Install()
        {
            // Only connected shared-depot clients install this version-checked
            // adapter. Every callback still requires the exact assigned native
            // port pair; unrelated vessel docking follows the original code.
            if(!NetworkFleet.IsMultiplayer || !NetworkFleet.Connected)return;
            if(installed)return;
            var type=typeof(VesselCouple);
            dominant=AccessTools.Field(type,"_dominantVessel");
            activeDominant=AccessTools.Field(type,"_activeVesselIsDominantVessel");
            var after=AccessTools.Method(type,"AfterCouplingEvent");
            var instance=type.GetMethod(nameof(VesselCouple.ProcessCouple),Type.EmptyTypes);
            var message=type.GetMethod(nameof(VesselCouple.ProcessCouple),new[]{typeof(VesselCoupleMsgData)});
            if(dominant?.FieldType!=typeof(Vessel)||activeDominant?.FieldType!=typeof(bool)||after==null||instance==null||message==null)
                throw new InvalidOperationException("Installed Luna docking callback API requires a validated adapter");
            var harmony=new Harmony("local.railgun.luna-docking-camera");
            var end=new HarmonyMethod(typeof(LunaDockingCompatibility),nameof(End));
            harmony.Patch(instance,prefix:new HarmonyMethod(typeof(LunaDockingCompatibility),nameof(BeforeInstance)),finalizer:end);
            harmony.Patch(message,prefix:new HarmonyMethod(typeof(LunaDockingCompatibility),nameof(BeforeMessage)),finalizer:end);
            harmony.Patch(after,prefix:new HarmonyMethod(typeof(LunaDockingCompatibility),nameof(AfterPrefix)));
            harmony.Patch(AccessTools.Method(typeof(VesselRemoveMessageHandler),nameof(VesselRemoveMessageHandler.HandleMessage)),
                prefix:new HarmonyMethod(typeof(LunaDockingCompatibility),nameof(RemovalMessage)));
            harmony.Patch(AccessTools.Method(typeof(VesselUndock),nameof(VesselUndock.ProcessUndock)),
                prefix:new HarmonyMethod(typeof(LunaDockingCompatibility),nameof(BeforeUndock)));
            harmony.Patch(AccessTools.Method(typeof(VesselLoader),nameof(VesselLoader.LoadVessel),new[]{typeof(ProtoVessel),typeof(bool)}),
                prefix:new HarmonyMethod(typeof(LunaDockingCompatibility),nameof(ReceiptMetadata)));
            installed=true;
        }
        static void ReceiptMetadata(ProtoVessel __0)
        {
            if(__0==null||!NetworkFleet.Shared.Ready)return;
            var native=FlightGlobals.FindVessel(__0.vesselID);
            if(native==null||!native.loaded||native.state==Vessel.State.DEAD)return;
            var fleet=NetworkFleet.Shared.Local;if(fleet==null)return;
            foreach(var pod in fleet.Pods.Where(p=>p.Created&&NetworkFleet.Shared.DeliveryConfirmed(p))) {
                if(!StockPods.OriginalPartsIntact(pod,native)||!pod.PartIds.All(id=>__0.protoPartSnapshots.Any(p=>p.flightID==id)))continue;
                var source=__0.protoPartSnapshots.SelectMany(p=>p.modules).Where(m=>m.moduleName=="RailgunFlightPod"&&m.moduleValues.GetValue("fleetId")==pod.VesselId).ToArray();
                if(source.Length!=1||source[0].moduleValues.GetValue("completedOrder")!=pod.OrderId)continue;
                var sourceCargo=__0.protoPartSnapshots.FirstOrDefault(p=>p.flightID==pod.CargoPart)?.resources.FirstOrDefault(r=>r.resourceName==pod.Resource);
                var localCargo=native.parts.FirstOrDefault(p=>p.flightID==pod.CargoPart)?.Resources[pod.Resource];
                if(sourceCargo==null||localCargo==null||!LunaDockingPolicy.EmptyCargoSnapshot(NetworkFleet.Shared.DeliveryConfirmed(pod),
                    source[0].moduleValues.GetValue("completedOrder")==pod.OrderId,sourceCargo.amount,sourceCargo.flowState,
                    localCargo.amount,localCargo.flowState,pod.CargoUnits))continue;
                var marker=native.parts.Select(p=>p.FindModuleImplementing<RailgunFlightPod>()).FirstOrDefault(m=>m?.fleetId==pod.VesselId);
                if(marker==null)continue;
                bool changed=marker.completedOrder!=pod.OrderId||localCargo.amount!=0;
                if(localCargo.amount!=0)Debug.Log("[RailgunSandbox] Replicated confirmed EMPTY delivery tank from received native snapshot: previousLocal="+localCargo.amount.ToString("R",CultureInfo.InvariantCulture)+" received=0 / "+pod.OrderId);
                localCargo.amount=sourceCargo.amount;
                marker.completedOrder=pod.OrderId;
                // Same-part-count native snapshots skip module reload in this
                // Luna build. Copy ONLY our metadata from the received native
                // snapshot after the server confirmed the real drained tank and
                // receipt. Mirror ONLY that emptied locked cargo tank; an old
                // spectator resource packet must not resurrect delivered cargo.
                // No propulsion resource, pose, phase or flight credit changes.
                if(source[0].moduleValues.GetValue("departureOrder")==pod.OrderId&&
                    double.TryParse(source[0].moduleValues.GetValue("departurePortClearance"),NumberStyles.Float,CultureInfo.InvariantCulture,out var clearance)&&
                    FlightRules.Finite(clearance)&&clearance>=50) {
                    changed|=marker.departureOrder!=pod.OrderId||marker.departurePortClearance!=clearance;
                    marker.departureOrder=pod.OrderId;marker.departurePortClearance=clearance;
                }
                if(changed)Debug.Log("[RailgunSandbox] Replicated confirmed native delivery/departure metadata from received snapshot: "+pod.OrderId);
            }
        }
        static bool BeforeUndock(VesselUndock __instance)
        {
            if(!NetworkFleet.Shared.Ready)return true;
            var fleet=NetworkFleet.Shared.Local;
            var vessel=FlightGlobals.FindVessel(__instance.VesselId);
            if(fleet==null||vessel==null||!vessel.loaded||__instance.NewVesselId==Guid.Empty)return true;
            var pod=fleet.Pods.FirstOrDefault(p=>p.Created&&p.CargoTransferred&&p.OrderId!=""&&
                (p.Phase==FlightPhase.Unloading||p.Phase==FlightPhase.Undocking||p.Phase==FlightPhase.Returning)&&
                p.OwnPort==__instance.PartFlightId&&p.PartIds.All(id=>vessel.parts.Any(part=>part.flightID==id))&&
                vessel.parts.Any(part=>part.flightID==p.TargetAnchor));
            if(pod==null)return true;
            double age=LmpClient.Systems.TimeSync.TimeSyncSystem.UniversalTime-__instance.GameTime;
            if(!FlightRules.Finite(age)||age<-.1||age>5)return true;
            if(LockSystem.LockQuery.ControlLockBelongsToPlayer(vessel.id,SettingsSystem.CurrentSettings.PlayerName))return true;
            var own=vessel.parts.FirstOrDefault(p=>p.flightID==pod.OwnPort)?.FindModuleImplementing<ModuleDockingNode>();
            var target=own?.otherNode;
            if(target==null||target.part.flightID!=pod.TargetPort||target.otherNode!=own||target.vessel!=vessel||
                !PortState(own).StartsWith("Docked",StringComparison.Ordinal)||!PortState(target).StartsWith("Docked",StringComparison.Ordinal))return true;
            var system=VesselUndockSystem.Singleton;
            bool ignore=system.IgnoreEvents;Guid prior=system.ManuallyUndockingVesselId;
            try {
                system.IgnoreEvents=true;system.ManuallyUndockingVesselId=vessel.id;
                own.Undock();
                var separated=own.vessel;
                if(separated==target.vessel)throw new InvalidOperationException("Native assigned undock did not separate the original pod");
                separated.id=__instance.NewVesselId;
                LockSystem.Singleton.FireVesselLocksEvents(separated.id);
                separated.SetImmortal(true); // Same temporary replication protection as installed Luna.
                VesselPositionSystem.Singleton.ForceUpdateVesselPosition(separated.id);
                Debug.Log("[RailgunSandbox] Replicated assigned peer undock through stock port events: own="+PortState(own)+" target="+PortState(target)+" originalParts="+pod.PartIds.Count+" eventAge="+age.ToString("F3"));
                return false;
            } finally {system.IgnoreEvents=ignore;system.ManuallyUndockingVesselId=prior;}
        }
        static bool RemovalMessage(IServerMessageBase __0)
        {
            if(!(__0.Data is VesselRemoveMsgData message)||message.AddToKillList||!NetworkFleet.Shared.Ready)return true;
            if(removals.ContainsKey(message.VesselId))return false;
            var fleet=NetworkFleet.Shared.Local;
            var weak=FlightGlobals.FindVessel(message.VesselId);
            var receiver=FlightGlobals.ActiveVessel;
            if(fleet==null||weak==null||receiver==null||weak==receiver||!weak.loaded||!receiver.loaded)return true;
            string player=SettingsSystem.CurrentSettings.PlayerName;
            if(!LockSystem.LockQuery.ControlLockBelongsToPlayer(receiver.id,player))return true;
            var pod=fleet.Pods.FirstOrDefault(p=>p.Created&&p.OrderId!=""&&(p.Phase==FlightPhase.Docking||p.Phase==FlightPhase.Unloading)&&
                p.PartIds.Count==p.OriginalParts&&p.PartIds.All(id=>weak.parts.Any(part=>part.flightID==id))&&
                !receiver.parts.Any(part=>p.PartIds.Contains(part.flightID))&&
                receiver.parts.Any(part=>part.flightID==p.TargetAnchor)&&receiver.parts.Any(part=>part.flightID==p.TargetPort));
            if(pod==null)return true;
            // Luna queues future-UT coupling events, but applies removal packets
            // immediately. Retain this exact original weak vessel briefly so the
            // already-received native coupling can run before its source dies.
            // Real destruction (AddToKillList) is never deferred.
            removals.Add(message.VesselId,new DeferredRemoval {Vessel=message.VesselId,Pod=pod.VesselId,Until=Time.realtimeSinceStartup+5});
            Debug.Log("[RailgunSandbox] Deferred assigned pod removal for queued native coupling: "+pod.VesselId);
            return false;
        }
        public static void Pump()
        {
            if(!installed||removals.Count==0)return;
            if(!NetworkFleet.IsMultiplayer||!NetworkFleet.Connected){removals.Clear();return;}
            foreach(var pending in removals.Values.ToArray()) {
                // A completed native merge removes the old vessel object. Native
                // cleanup is then safe and still clears Luna's obsolete caches.
                bool absent=FlightGlobals.FindVessel(pending.Vessel)==null;
                if(!absent&&Time.realtimeSinceStartup<pending.Until)continue;
                removals.Remove(pending.Vessel);
                Debug.Log(absent?"[RailgunSandbox] Cleaning obsolete native coupled vessel cache: "+pending.Pod:
                    "[RailgunSandbox] Native coupling wait expired; applying original removal without delivery credit: "+pending.Pod);
                LmpClient.Base.System<VesselRemoveSystem>.Singleton.KillVessel(pending.Vessel,false,
                    absent?"Completed assigned native coupling cleanup":"Assigned native coupling removal wait expired");
            }
        }
        static void Begin(Guid vessel,Guid weak,uint a,uint b,double gameTime,CoupleTrigger trigger,ref Scope state)
        {
            state=new Scope {Previous=current,Dominant=vessel,Weak=weak,Age=LmpClient.Systems.TimeSync.TimeSyncSystem.UniversalTime-gameTime};current=state;
            var fleet=NetworkFleet.Shared.Local;
            if(NetworkFleet.IsMultiplayer&&NetworkFleet.Shared.Ready&&fleet!=null&&vessel!=Guid.Empty&&weak!=Guid.Empty&&weak!=vessel)
                state.Pod=fleet.Pods.FirstOrDefault(p=>p.Created&&p.OrderId!=""&&(p.Phase==FlightPhase.Docking||p.Phase==FlightPhase.Unloading||p.Phase==FlightPhase.Undocking)&&
                    p.OwnPort!=0&&p.TargetPort!=0&&p.PartIds.Contains(p.OwnPort)&&
                    (a==p.OwnPort&&b==p.TargetPort||b==p.OwnPort&&a==p.TargetPort));
            state.Known=state.Pod!=null&&trigger==CoupleTrigger.DockingNode;
        }
        static bool BeforeInstance(VesselCouple __instance,ref Scope __state,ref bool __result)
        {
            Begin(__instance.VesselId,__instance.CoupledVesselId,__instance.PartFlightId,__instance.CoupledPartFlightId,__instance.GameTime,__instance.Trigger,ref __state);
            return ProcessOrRetain(__state,ref __result);
        }
        static bool BeforeMessage(VesselCoupleMsgData __0,ref Scope __state,ref bool __result)
        {
            Begin(__0.VesselId,__0.CoupledVesselId,__0.PartFlightId,__0.CoupledPartFlightId,__0.GameTime,(CoupleTrigger)__0.Trigger,ref __state);
            return ProcessOrRetain(__state,ref __result);
        }
        static string PortState(ModuleDockingNode node)=>node?.fsm?.CurrentState?.name??node?.state??"";
        static bool ProcessOrRetain(Scope scope,ref bool result)
        {
            if(scope?.Known!=true)return true;
            var pod=scope.Pod;
            var vessel=StockPods.Vessel(pod);
            if(vessel==null||!vessel.loaded)return true;
            var own=vessel.parts.FirstOrDefault(p=>p.flightID==pod.OwnPort)?.FindModuleImplementing<ModuleDockingNode>();
            var target=vessel.parts.FirstOrDefault(p=>p.flightID==pod.TargetPort)?.FindModuleImplementing<ModuleDockingNode>();
            if(own!=null&&target!=null&&own.otherNode==target&&target.otherNode==own&&
                PortState(own)=="Docked (docker)"&&PortState(target)=="Docked (dockee)"&&
                vessel.parts.Any(p=>p.flightID==pod.TargetAnchor)&&pod.PartIds.All(id=>vessel.parts.Any(p=>p.flightID==id))) {
                // Both peers can observe the same real capture. A repeated
                // packet must not merge or retarget an already joined pair.
                Debug.Log("[RailgunSandbox] Assigned native dock already joined; acknowledged duplicate coupling without changing port state.");
                result=true;return false;
            }
            var receiver=FlightGlobals.ActiveVessel;
            if(receiver==null||receiver==vessel||!receiver.loaded||
                !receiver.parts.Any(p=>p.flightID==pod.TargetAnchor)||!receiver.parts.Any(p=>p.flightID==pod.TargetPort)||
                !pod.PartIds.All(id=>vessel.parts.Any(p=>p.flightID==id)))return true;
            string player=SettingsSystem.CurrentSettings.PlayerName;
            if(!LockSystem.LockQuery.ControlLockBelongsToPlayer(receiver.id,player))return true;
            target=receiver.parts.FirstOrDefault(p=>p.flightID==pod.TargetPort)?.FindModuleImplementing<ModuleDockingNode>();
            if(own==null||target==null||own.fsm==null||target.fsm==null||own.nodeTransform==null||target.nodeTransform==null||
                vessel.mainBody!=receiver.mainBody)return true;
            double gap=Vector3.Distance(own.nodeTransform.position,target.nodeTransform.position);
            double facing=Vector3.Dot(own.nodeTransform.forward,-target.nodeTransform.forward);
            double relative=(vessel.obt_velocity-receiver.obt_velocity).magnitude;
            if(!LunaDockingPolicy.CaptureEnvelope(scope.Age,gap,facing,relative)||
                !(PortState(own)=="Ready"||PortState(own)=="Acquire")||
                !(PortState(target)=="Ready"||PortState(target)=="Acquire (dockee)"))return true;
            // This is replication of an actual Luna capture notification for
            // this exact assigned pair, not independent receiver-side capture.
            // Stock OnNodeApproach sets partner IDs and native dominance; stock
            // OnCapture performs the joint, alignment, fuel links and both FSMs.
            // A bounded near-dock envelope rejects stale/foreign notifications.
            bool ignore=VesselCoupleSystem.Singleton.IgnoreEvents;
            try {
                VesselCoupleSystem.Singleton.IgnoreEvents=true;
                own.otherNode=target;target.otherNode=own;
                if(PortState(own)=="Ready")own.fsm.RunEvent(own.on_nodeApproach);
                if(PortState(target)=="Ready")target.fsm.RunEvent(target.on_nodeApproach);
                if(PortState(own)!="Acquire"||PortState(target)!="Acquire (dockee)")return true;
                own.fsm.RunEvent(own.on_capture);
                result=own.vessel==target.vessel&&PortState(own)=="Docked (docker)"&&PortState(target)=="Docked (dockee)";
                Debug.Log("[RailgunSandbox] Replicated assigned peer capture through stock docking events: joined="+result+" priorGap="+gap.ToString("F3")+" relativeSpeed="+relative.ToString("F3")+" eventAge="+scope.Age.ToString("F3"));
                return false;
            } finally {VesselCoupleSystem.Singleton.IgnoreEvents=ignore;}
        }
        static Exception End(Scope __state,Exception __exception) { if(__state!=null)current=__state.Previous;return __exception; }
        static bool AfterPrefix()
        {
            if(current?.Known!=true||!(bool)activeDominant.GetValue(null))return true;
            var vessel=dominant.GetValue(null) as Vessel;
            if(vessel!=null&&vessel.id==current.Dominant)return true;
            Debug.LogWarning("[RailgunSandbox] Skipped Luna's stale post-coupling camera callback for the assigned logistics dock; native coupling and ownership remain unchanged.");
            return false;
        }
    }
}
