using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using HarmonyLib;
using LmpClient.Events;
using LmpClient.Network;
using LmpClient.Systems.ModApi;
using LmpClient.Systems.Warp;
using LmpCommon.Message.Data;
using MuMech;
using UnityEngine;

namespace RailgunSandbox
{
    [KSPAddon(KSPAddon.Startup.MainMenu,true)]
    public sealed class CoastWarpClient : MonoBehaviour
    {
        static CoastWarpClient instance;
        readonly ConcurrentQueue<CoastWarpMessage> inbox=new ConcurrentQueue<CoastWarpMessage>();
        readonly System.Collections.Generic.Dictionary<string,float> sentAt=new System.Collections.Generic.Dictionary<string,float>();
        CoastWarpMessage session=new CoastWarpMessage {Stage="Idle"};
        MechJebCore core;
        bool enabledOption,subscribed,warped,locallyStopping;
        float nextPresence,nextRequest,stageAt,lastMessage;
        double stopUt;
        int initialCoreId;
        string preference;
        public static bool Enabled=>instance?.enabledOption==true;
        public static string Status=>instance?.status??"Auto warp off";
        string status="Auto warp off";
        bool Leader=>session.Leader==NetworkFleet.Shared.Player;
        public static bool Holding=>instance!=null&&instance.session.Stage!="Idle"&&NetworkFleet.IsMultiplayer&&NetworkFleet.Connected&&instance.session.Members.Contains(NetworkFleet.Shared.Player);
        public static bool AllowsNativeWarp=>Holding&&instance.Leader&&instance.session.Stage=="Warping"&&!instance.locallyStopping;
        public static bool Autowarp(MechJebCore candidate)=>instance!=null&&candidate==instance.core&&
            (NetworkFleet.IsMultiplayer?AllowsNativeWarp:Enabled&&instance.LocalEligible(out _,out _,out _));
        void Awake()
        {
            if(instance!=null){Destroy(this);return;}instance=this;DontDestroyOnLoad(gameObject);
            preference=Path.Combine(KSPUtil.ApplicationRootPath,"GameData","RailgunLogistics","PluginData","FlightPreferences.cfg");
            var config=File.Exists(preference)?ConfigNode.Load(preference):null;enabledOption=config?.GetValue("autoWarp")=="True";
            var harmony=new Harmony("local.railgun.coast-warp");
            harmony.Patch(AccessTools.Method(typeof(MechJebModuleWarpController),"WarpRegularAtRate"),prefix:new HarmonyMethod(typeof(CoastWarpClient),nameof(CapRegular)));
            harmony.Patch(AccessTools.Method(typeof(MechJebModuleWarpController),"WarpToUT"),prefix:new HarmonyMethod(typeof(CoastWarpClient),nameof(CapTarget)));
            harmony.Patch(AccessTools.Method(typeof(MechJebCore),"OnFlyByWire"),postfix:new HarmonyMethod(typeof(CoastWarpClient),nameof(HoldControls)));
        }
        public static void SetEnabled(bool value)
        {
            if(instance==null)return;instance.enabledOption=value;
            Directory.CreateDirectory(Path.GetDirectoryName(instance.preference));var node=new ConfigNode();node.AddValue("autoWarp",value);node.Save(instance.preference);
            if(!value&&Holding)instance.Send("Cancel");
            if(!value&&instance.core!=null){instance.core.node.autowarp=false;instance.core.warp.MinimumWarp(true);}
            instance.nextPresence=0;
        }
        static void CapRegular(MechJebModuleWarpController __instance,ref float maxRate)
        {
            if(instance?.core?.warp!=__instance)return;
            maxRate=(float)Math.Min(maxRate,Autowarp(instance.core)?CoastWarpRules.MaximumRate:1);
        }
        static void CapTarget(MechJebModuleWarpController __instance,ref double UT,ref double maxRate)
        {
            if(instance?.core?.warp!=__instance)return;
            maxRate=Autowarp(instance.core)?Math.Min(maxRate<0?CoastWarpRules.MaximumRate:maxRate,CoastWarpRules.MaximumRate):1;
            if(instance.stopUt>0)UT=Math.Min(UT,instance.stopUt);
        }
        static void HoldControls(MechJebCore __instance,FlightCtrlState s)
        {
            if(!Holding||!instance.Leader||__instance!=instance.core)return;
            // The coast is unpowered. Keep native guidance objects alive while
            // Luna creates/synchronizes the new subspace, but apply no burn or RCS.
            s.mainThrottle=0;s.X=s.Y=s.Z=0;s.pitch=s.yaw=s.roll=0;
        }
        void Receive(string channel,byte[] bytes)
        {
            if(channel!=CoastWarpRules.Channel||bytes==null||bytes.Length>8192)return;
            try {inbox.Enqueue(FleetWire.Read<CoastWarpMessage>(System.Text.Encoding.UTF8.GetString(bytes)));}
            catch(Exception e){Debug.LogWarning("[RailgunCoastWarp] Invalid response: "+e.Message);}
        }
        void Send(string kind,CoastWarpMessage message=null)
        {
            if(!NetworkFleet.Connected)return;
            if(kind=="Ack"||kind=="Cancel"||kind=="Finished"||kind=="Done") {
                if(sentAt.TryGetValue(kind,out var before)&&Time.realtimeSinceStartup-before<.5f)return;
                sentAt[kind]=Time.realtimeSinceStartup;
            }
            var data=NetworkMain.CliMsgFactory.CreateNewMessageData<ModMsgData>();
            message=message??new CoastWarpMessage();message.Kind=kind;message.Epoch=NetworkFleet.Shared.CoastEpoch;message.Session=session.Session;
            byte[] bytes=System.Text.Encoding.UTF8.GetBytes(FleetWire.Text(message));
            data.ModName=CoastWarpRules.Channel;data.Data=bytes;data.NumBytes=bytes.Length;data.Reliable=true;data.Relay=false;
            ModApiSystem.Singleton.MessageSender.SendMessage(data);
        }
        static bool PassiveSafe()
        {
            var v=FlightGlobals.ActiveVessel;
            if(!HighLogic.LoadedSceneIsFlight||!FlightGlobals.ready||FlightDriver.Pause||v==null||v.state==Vessel.State.DEAD)return false;
            if(v.ctrlState.mainThrottle>.001||v.parts.SelectMany(p=>p.FindModulesImplementing<ModuleEngines>()).Any(e=>e.finalThrust>.05))return false;
            if(Math.Abs(v.ctrlState.X)>.05||Math.Abs(v.ctrlState.Y)>.05||Math.Abs(v.ctrlState.Z)>.05)return false;
            if(v.LandedOrSplashed)return v.srfSpeed<.1;
            return v.altitude>v.mainBody.atmosphereDepth+30000&&v.orbit.PeA>v.mainBody.atmosphereDepth+10000;
        }
        bool LocalEligible(out PodRecord pod,out MechJebCore selected,out double until)
        {
            pod=null;selected=null;until=0;
            var state=NetworkFleet.IsMultiplayer?NetworkFleet.Shared.Local:SandboxFlight.Instance?.WarpFleet;
            if(state==null||!enabledOption||!PassiveSafe())return false;
            pod=state.Pods.FirstOrDefault(p=>CoastWarpRules.PhaseAllowed(p.Phase)&&StockPods.Vessel(p)==FlightGlobals.ActiveVessel);
            if(pod==null)return false;
            selected=StockPods.Marker(pod)?.part.FindModuleImplementing<MechJebCore>();if(selected==null)return false;
            bool authority=!NetworkFleet.IsMultiplayer||NetworkFleet.Shared.CanAct(pod);
            var first=selected.vessel.patchedConicSolver?.maneuverNodes.FirstOrDefault();
            bool nativeCoast=selected.landing.CurrentStep is MuMech.Landing.DeorbitBurn;
            if(nativeCoast) {
                var triggered=AccessTools.Field(typeof(MuMech.Landing.DeorbitBurn),"deorbitBurnTriggered");
                nativeCoast=triggered!=null&&!(bool)triggered.GetValue(selected.landing.CurrentStep);until=600;
            } else if(selected.node.enabled&&!selected.node.burnTriggered&&first!=null) {
                // Use the installed executor's own burn duration, including
                // half-burn timing and engine spool-up, with a 30-second margin.
                var method=AccessTools.Method(typeof(MechJebModuleNodeExecutor),"BurnTime");
                object[] args={first.DeltaV.magnitude,0d,0d};method.Invoke(selected.node,args);
                until=first.UT-Planetarium.GetUniversalTime()-(double)args[1]-(double)args[2];nativeCoast=true;
            }
            bool near=pod.Phase==FlightPhase.Rendezvous&&(!selected.target.NormalTargetExists||selected.target.Distance<2000);
            return CoastWarpRules.Envelope(pod.Phase,true,authority,true,true,nativeCoast,near,until);
        }
        void Update()
        {
            try {Pump();}catch(Exception e){status="Auto warp paused: "+e.Message;if(core!=null){core.node.autowarp=false;core.warp.MinimumWarp(true);}Debug.LogWarning("[RailgunCoastWarp] "+e.Message);}
        }
        void Pump()
        {
            if(!subscribed&&ModApiEvent.onModMessageReceived!=null){ModApiEvent.onModMessageReceived.Add(Receive);subscribed=true;}
            if(!NetworkFleet.IsMultiplayer) {
                session=new CoastWarpMessage {Stage="Idle"};
                bool eligible=LocalEligible(out var p,out var native,out var until);
                if(native!=null)core=native;
                if(core!=null)core.node.autowarp=eligible;
                stopUt=eligible&&!(native.landing.CurrentStep is MuMech.Landing.DeorbitBurn)?Planetarium.GetUniversalTime()+until-30:0;
                status=!enabledOption?"Auto warp off":eligible?"Auto warp ready for orbital coast":"Holding 1× for active flight";
                if(!eligible&&core!=null&&TimeWarp.CurrentRate>1)core.warp.MinimumWarp(true);
                return;
            }
            if(!NetworkFleet.Connected){if(core!=null)core.warp.MinimumWarp(true);session=new CoastWarpMessage {Stage="Idle"};status="Waiting for Luna";return;}
            while(inbox.TryDequeue(out var response)) {
                if(response.Epoch!=""&&response.Epoch!=NetworkFleet.Shared.CoastEpoch)continue;
                bool changed=response.Session!=session.Session||response.Stage!=session.Stage;
                var previous=session;
                session=response;lastMessage=Time.realtimeSinceStartup;
                if(changed) {
                    stageAt=Time.realtimeSinceStartup;
                    if(session.Stage=="Preparing") {
                        warped=false;locallyStopping=false;
                        if(Leader) {var pod=NetworkFleet.Shared.Local?.Pods.FirstOrDefault(p=>p.Number==session.Number);if(pod!=null)core=StockPods.Marker(pod)?.part.FindModuleImplementing<MechJebCore>();}
                        initialCoreId=core!=null?core.GetInstanceID():0;
                        stopUt=session.StopUt;
                    }
                    if(session.Stage=="Idle") {
                        nextRequest=Time.realtimeSinceStartup+5;
                        if(previous.Leader==NetworkFleet.Shared.Player&&previous.Number>0) {
                            var pod=NetworkFleet.Shared.Local?.Pods.FirstOrDefault(p=>p.Number==previous.Number);
                            var current=pod==null?null:StockPods.Marker(pod)?.part.FindModuleImplementing<MechJebCore>();
                            if(current!=null&&current.GetInstanceID()!=initialCoreId)SandboxFlight.Instance?.RearmAfterWarp(previous.Number);
                            core=current;
                        }
                    }
                    Debug.Log("[RailgunCoastWarp] "+session.Stage+" / "+session.Session+" / "+session.Reason);
                }
            }
            bool safe=PassiveSafe();
            if(Time.realtimeSinceStartup>=nextPresence) {
                nextPresence=Time.realtimeSinceStartup+1;
                Send("Presence",new CoastWarpMessage {Enabled=enabledOption,Safe=safe,Subspace=WarpSystem.Singleton.CurrentSubspace});
            }
            status=session.Stage=="Idle"?enabledOption?session.Reason:"Auto warp off":session.Reason;
            if(session.Stage=="Idle") {
                if(core!=null)core.node.autowarp=false;
                if(Time.realtimeSinceStartup<nextRequest||!LocalEligible(out var pod,out var native,out var until))return;
                core=native;
                if(!NetworkFleet.Shared.PrepareCoastLease(pod))return;
                nextRequest=Time.realtimeSinceStartup+4;stopUt=!(native.landing.CurrentStep is MuMech.Landing.DeorbitBurn)?Planetarium.GetUniversalTime()+until-30:0;
                Send("Request",new CoastWarpMessage {Number=pod.Number,Subspace=WarpSystem.Singleton.CurrentSubspace,StopUt=stopUt>0?stopUt:Planetarium.GetUniversalTime()+1000});
                return;
            }
            if(session.Stage=="Preparing") {if(enabledOption&&safe)Send("Ack");else Send("Cancel");return;}
            if(!session.Members.Contains(NetworkFleet.Shared.Player))return;
            if((!enabledOption||!safe||Time.realtimeSinceStartup-lastMessage>3)&&session.Stage=="Warping")Send("Cancel");
            if(Leader) {
                if(core==null) {var pod=NetworkFleet.Shared.Local?.Pods.FirstOrDefault(p=>p.Number==session.Number);if(pod!=null)core=StockPods.Marker(pod)?.part.FindModuleImplementing<MechJebCore>();}
                if(core!=null)core.node.autowarp=AllowsNativeWarp;
                if(TimeWarp.CurrentRate>1)warped=true;
                bool end=session.Stage!="Warping"||Time.realtimeSinceStartup-stageAt>=7||!safe||
                    (stopUt>0&&Planetarium.GetUniversalTime()>=stopUt)||core?.node.burnTriggered==true||
                    core?.landing.CurrentStep is MuMech.Landing.DeorbitBurn&&(bool)AccessTools.Field(typeof(MuMech.Landing.DeorbitBurn),"deorbitBurnTriggered").GetValue(core.landing.CurrentStep);
                if(end) {locallyStopping=true;if(core!=null){core.node.autowarp=false;core.warp.MinimumWarp(true);}Send("Cancel");}
                if((end||warped)&&TimeWarp.CurrentRate==1&&WarpSystem.Singleton.CurrentSubspace>=0&&!WarpSystem.Singleton.WaitingSubspaceIdFromServer&&session.Stage!="Synchronizing")
                    Send("Finished",new CoastWarpMessage {Destination=WarpSystem.Singleton.CurrentSubspace});
                if(session.Stage=="Synchronizing"&&NetworkFleet.Shared.Ready&&NetworkFleet.Shared.LeaseValid(session.Number))Send("Done");
            } else if(session.Stage=="Synchronizing"&&session.Destination>=0&&WarpSystem.Singleton.CurrentSubspace!=session.Destination&&safe)
                WarpSystem.Singleton.SyncToSubspace(session.Destination);
        }
        void OnApplicationQuit(){if(subscribed)ModApiEvent.onModMessageReceived.Remove(Receive);}
    }
}
