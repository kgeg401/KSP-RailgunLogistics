using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LmpClient.Systems.VesselCoupleSys;
using LmpCommon.Message.Data.Vessel;
using UnityEngine;

namespace RailgunSandbox
{
    // Installed Luna 0.29.2 calls its post-coupling camera callback even when
    // the coupling routine returned early for a locally owned receiver. Its
    // cached dominant Vessel can then be null or belong to an earlier event.
    // Guard that callback only for the assigned logistics docking-port pair.
    // Native coupling, ownership, snapshots, exceptions and cargo are unchanged.
    public static class LunaDockingCompatibility
    {
        sealed class Scope { public Scope Previous; public Guid Dominant; public bool Known; }
        [ThreadStatic] static Scope current;
        static bool installed;
        static FieldInfo dominant,activeDominant;
        public static void Install()
        {
            // Native receiving-owner test kept the peer connected but lost the
            // merged pod through a stale dominant snapshot. Camera guarding
            // alone is not a safe coupling repair. Keep it test-only until
            // full native snapshot/ownership reconciliation is validated.
            if(!Environment.GetCommandLineArgs().Contains("-railgun-luna-coupling-test"))return;
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
            installed=true;
        }
        static void Begin(Guid vessel,uint a,uint b,ref Scope state)
        {
            state=new Scope {Previous=current,Dominant=vessel};current=state;
            var fleet=NetworkFleet.Shared.Local;
            state.Known=NetworkFleet.IsMultiplayer&&NetworkFleet.Connected&&fleet!=null&&vessel!=Guid.Empty&&
                fleet.Pods.Any(p=>p.Created&&p.OrderId!=""&&(p.Phase==FlightPhase.Docking||p.Phase==FlightPhase.Unloading||p.Phase==FlightPhase.Undocking)&&
                    p.OwnPort!=0&&p.TargetPort!=0&&p.PartIds.Contains(p.OwnPort)&&
                    (a==p.OwnPort&&b==p.TargetPort||b==p.OwnPort&&a==p.TargetPort));
        }
        static void BeforeInstance(VesselCouple __instance,ref Scope __state)=>Begin(__instance.VesselId,__instance.PartFlightId,__instance.CoupledPartFlightId,ref __state);
        static void BeforeMessage(VesselCoupleMsgData __0,ref Scope __state)=>Begin(__0.VesselId,__0.PartFlightId,__0.CoupledPartFlightId,ref __state);
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
