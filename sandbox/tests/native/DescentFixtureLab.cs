using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using MuMech;
using HarmonyLib;
using UnityEngine;

namespace RailgunSandbox
{
    // Explicit single-player engineering harness. Scene reloads restore a saved
    // native fixture; this never teleports, refuels or credits a running mission.
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class DescentFixtureLab : MonoBehaviour
    {
        string folder, run, output, mode="route";
        int controller = 3, errors;
        bool selecting;
        double nextSample;
        double controlStarted;
        string previousNativeStep;
        float nextPoll;
        static bool groupProbeInstalled;
        static string groupProbeOutput;
        static int groupProbeEvents;
        static bool reverseProbeInstalled;
        static readonly HashSet<uint> reversedPanels=new HashSet<uint>();
        sealed class SurfaceInput { public FlightCtrlState State;public float Pitch,Yaw; }
        void Awake()
        {
            var args = Environment.GetCommandLineArgs();
            if (!args.Contains("-railgun-reentry") || !args.Contains("-railgun-descent-fixture")) { enabled=false;return; }
            DontDestroyOnLoad(gameObject);
            folder=Path.Combine(KSPUtil.ApplicationRootPath,"saves","Railgun-Reentry-Test");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"LandingLab.boot.txt"),DateTime.UtcNow.ToString("O"));
        }
        void Update()
        {
            if (folder==null || errors>=3 || Time.realtimeSinceStartup<nextPoll)return;
            nextPoll=Time.realtimeSinceStartup+.1f;
            try {
                string command=Path.Combine(folder,"LandingLab.command");
                if (File.Exists(command)) {
                    string text=File.ReadAllText(command).Trim();File.Delete(command);
                    if(text=="quit") { Application.Quit();return; }
                    if(text=="snapshot") { GamePersistence.SaveGame("persistent",HighLogic.SaveFolder,SaveMode.OVERWRITE);return; }
                    var words=text.Split(':');
                    if((words.Length!=4&&words.Length!=5) || words[0]!="reload" || !ValidName(words[1],"case-") || !ValidName(words[3],"run-"))throw new InvalidDataException("Invalid private fixture command");
                    string requestedMode=words.Length==5?words[4]:"route";
                    if(requestedMode!="route"&&requestedMode!="route120"&&requestedMode!="route120-native"&&requestedMode!="route120-native-smooth"&&requestedMode!="route120-native-entry2000"&&requestedMode!="route120-native-entry2000-hold"&&requestedMode!="route120-native-entry2000-reserve"&&requestedMode!="route120-native-entry2000-replan"&&requestedMode!="route120-native-entry2000-vertical"&&requestedMode!="route120-native-entry2000-vertical-corridor"&&requestedMode!="pulse"&&requestedMode!="pulse-fins"&&requestedMode!="pulse-fins-reversed")throw new InvalidDataException("Unknown fixture control mode");
                    int selected=int.Parse(words[2],CultureInfo.InvariantCulture);
                    if(selected<0||selected>3)throw new InvalidDataException("Unknown installed native controller");
                    Reload(words[1],selected,words[3],requestedMode);return;
                }
                if(run==null || !HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready || SandboxFlight.Instance==null)return;
                if(NetworkFleet.Connected || NetworkFleet.IsMultiplayer || !SandboxFlight.ReentryTestSave)throw new InvalidOperationException("Fixture harness cannot run in multiplayer");
                var pod=SandboxFlight.Instance.WarpFleet?.Pods.FirstOrDefault(p=>p.Number==1);
                var vessel=pod==null?null:StockPods.Vessel(pod);
                if(vessel==null || vessel.packed || !vessel.loaded || vessel!=FlightGlobals.ActiveVessel)return;
                var core=vessel.GetMasterMechJeb();if(core==null)return;
                if(selecting) {
                    if(!core.landing.enabled)return; // Wait for the ordinary landing setup, including engine compatibility.
                    core.attitude.SetActiveController(controller);selecting=false;
                    RecoveryGuidance.SetIsolatedApproachSpeed(mode.StartsWith("route120",StringComparison.Ordinal)?120:60,mode.Contains("-native"),mode.EndsWith("-smooth",StringComparison.Ordinal),mode.Contains("-entry2000")?2000:0,mode.EndsWith("-hold",StringComparison.Ordinal)?5:1,mode.EndsWith("-reserve",StringComparison.Ordinal)?3:0,mode.EndsWith("-replan",StringComparison.Ordinal),mode.Contains("-vertical"),mode.EndsWith("-corridor",StringComparison.Ordinal));
                    if(!groupProbeInstalled) {
                        var setter=AccessTools.Method(vessel.ActionGroups.GetType(),"SetGroup",new[]{typeof(KSPActionGroup),typeof(bool)});
                        if(setter==null)throw new MissingMethodException("Native action-group setter missing");
                        new Harmony("local.railgun.fixture-rcs-setters").Patch(setter,prefix:new HarmonyMethod(typeof(DescentFixtureLab),nameof(GroupSet)));
                        groupProbeInstalled=true;
                    }
                    groupProbeOutput=output;groupProbeEvents=0;
                    controlStarted=Planetarium.GetUniversalTime();
                    if(mode.StartsWith("pulse",StringComparison.Ordinal)) {
                        core.landing.StopLanding();RecoveryGuidance.Remove(core.landing);
                        SandboxFlight.Instance.enabled=false;
                        core.thrust.users.Add(this);
                        File.AppendAllText(Path.Combine(output,"events.txt"),"pulse=true ordinaryLandingDisabled=true noLandingAcceptance=true\n");
                        if(mode=="pulse-fins"||mode=="pulse-fins-reversed") {
                            var panels=vessel.parts.Where(p=>pod.PartIds.Contains(p.flightID)).SelectMany(p=>p.FindModulesImplementing<ModuleAeroSurface>()).ToArray();
                            if(panels.Length!=4)throw new InvalidDataException("Expected four original native steering airbrakes");
                            foreach(var panel in panels) { panel.ignorePitch=false;panel.ignoreYaw=false; }
                            File.AppendAllText(Path.Combine(output,"events.txt"),"nativePitchYawEnabled=true nativeStockAuthorityUnchanged=true\n");
                            if(mode=="pulse-fins-reversed") {
                                if(!reverseProbeInstalled) {
                                    var update=AccessTools.DeclaredMethod(typeof(ModuleAeroSurface),"CtrlSurfaceUpdate",new[]{typeof(Vector3)});
                                    if(update?.GetMethodBody()==null)throw new MissingMethodException("Native surface-control update has no implementation");
                                    new Harmony("local.railgun.fixture-surface-wiring").Patch(update,prefix:new HarmonyMethod(typeof(DescentFixtureLab),nameof(ReverseSurface)),finalizer:new HarmonyMethod(typeof(DescentFixtureLab),nameof(RestoreSurface)));
                                    reverseProbeInstalled=true;
                                }
                                foreach(var panel in panels)reversedPanels.Add(panel.part.flightID);
                                File.AppendAllText(Path.Combine(output,"events.txt"),"nativeSurfacePitchYawInputsReversed=true physicalCurvesActuatorsUnchanged=true\n");
                            }
                        }
                    }
                    File.AppendAllText(Path.Combine(output,"events.txt"),FormattableString.Invariant($"nativeController={controller} ut={Planetarium.GetUniversalTime():R}\n"));
                    nextSample=0;
                }
                if(mode.StartsWith("pulse",StringComparison.Ordinal)) {
                    double elapsed=Planetarium.GetUniversalTime()-controlStarted;
                    var up=(vessel.GetWorldPos3D()-vessel.mainBody.position).normalized;
                    var north=Vector3d.Exclude(up,vessel.mainBody.GetWorldSurfacePosition(vessel.latitude+.01,vessel.longitude,vessel.altitude)-vessel.GetWorldPos3D()).normalized;
                    var east=Vector3d.Cross(up,north).normalized;
                    double tilt=elapsed<10?0:elapsed<30?25:-25;
                    core.thrust.tmode=MechJebModuleThrustController.TMode.KEEP_VERTICAL;core.thrust.trans_kill_h=false;core.thrust.trans_spd_act=-40;
                    core.attitude.attitudeTo((up*Math.Cos(tilt*Math.PI/180)+east*Math.Sin(tilt*Math.PI/180)).normalized,AttitudeReference.INERTIAL,this);
                    if(elapsed>55||vessel.altitude<300) {
                        File.AppendAllText(Path.Combine(output,"events.txt"),"boundedPulseComplete=true\n");
                        Application.Quit();return;
                    }
                }
                double now=Planetarium.GetUniversalTime();if(now<nextSample)return;nextSample=now+.1;
                Sample(pod,vessel,core,now);
            } catch(Exception e) {
                errors++;File.AppendAllText(Path.Combine(folder,"LandingLab.errors.txt"),e+"\n");
                Debug.LogError("[RailgunDescentFixture] "+e);
            }
        }
        static bool ValidName(string name,string prefix)=>name.StartsWith(prefix,StringComparison.Ordinal)&&name.Length<=64&&name.All(c=>char.IsLetterOrDigit(c)||c=='-'||c=='_');
        static void ReverseSurface(ModuleAeroSurface __instance,ref SurfaceInput __state)
        {
            if(!SandboxFlight.ReentryTestSave||!reversedPanels.Contains(__instance.part.flightID))return;
            var state=__instance.vessel.ctrlState;
            __state=new SurfaceInput {State=state,Pitch=state.pitch,Yaw=state.yaw};
            state.pitch=-state.pitch;state.yaw=-state.yaw;
        }
        static Exception RestoreSurface(Exception __exception,SurfaceInput __state)
        {
            if(__state!=null) {__state.State.pitch=__state.Pitch;__state.State.yaw=__state.Yaw;}
            return __exception;
        }
        static void GroupSet(object __instance,KSPActionGroup __0,bool __1)
        {
            if(__0!=KSPActionGroup.RCS||!__1||groupProbeOutput==null||groupProbeEvents>=5)return;
            var v=FlightGlobals.ActiveVessel;
            if(v==null||v.altitude<8000||!ReferenceEquals(v.ActionGroups,__instance)||v.ActionGroups[KSPActionGroup.RCS])return;
            groupProbeEvents++;
            File.AppendAllText(Path.Combine(groupProbeOutput,"rcs-enable-callers.txt"),FormattableString.Invariant($"ut={Planetarium.GetUniversalTime():R} altitude={v.altitude:R}\n")+Environment.StackTrace+"\n");
        }
        void Reload(string fixture,int selected,string label,string requestedMode)
        {
            reversedPanels.Clear();
            if(NetworkFleet.Connected||NetworkFleet.IsMultiplayer)throw new InvalidOperationException("Disconnect Luna before native fixture reload");
            var game=GamePersistence.LoadGame(fixture,"Railgun-Reentry-Test",true,false);
            if(game==null||game.Mode!=Game.Modes.SANDBOX)throw new InvalidDataException("Missing native sandbox fixture");
            var serializer=new XmlSerializer(typeof(FleetState));FleetState fleet;
            using(var stream=File.OpenRead(Path.Combine(folder,fixture+".xml")))fleet=(FleetState)serializer.Deserialize(stream);
            fleet.Validate();
            var first=fleet.Pods.Single(p=>p.Number==1);
            if(first.Phase!=FlightPhase.Landing||!first.RecoveryTest||string.IsNullOrEmpty(first.RecoveryTestRunId))throw new InvalidDataException("Fixture must explicitly be a diagnostic landing, without refit credit");
            var missingInactive=new List<int>();
            foreach(var pod in fleet.Pods) {
                var native=game.flightState.protoVessels.SingleOrDefault(v=>v.protoPartSnapshots.Any(p=>p.modules.Cast<ProtoPartModuleSnapshot>().Any(m=>m.moduleName==nameof(RailgunFlightPod)&&m.moduleValues.GetValue("fleetId")==pod.VesselId)));
                if(native==null&&pod.Number!=1&&pod.Phase==FlightPhase.Lost) {missingInactive.Add(pod.Number);continue;}
                if(native==null)throw new InvalidDataException("Required fixture pod "+pod.Number+" is missing; no replacement is generated");
                var actual=native.protoPartSnapshots.Select(p=>p.flightID).Where(id=>id!=pod.LaunchShellPartId).OrderBy(id=>id);
                if(!actual.SequenceEqual(pod.PartIds.OrderBy(id=>id))||pod.OriginalParts!=pod.PartIds.Count)throw new InvalidDataException("Fixture original part inventory mismatch");
            }
            var path=Path.Combine(folder,"landing-lab",label);
            if(Directory.Exists(path))throw new InvalidOperationException("Do not overwrite an experiment");
            Directory.CreateDirectory(path);output=path;run=label;mode=requestedMode;controller=selected;selecting=true;previousNativeStep=null;
            File.Copy(Path.Combine(folder,fixture+".sfs"),Path.Combine(output,"initial.sfs"));
            File.Copy(Path.Combine(folder,fixture+".xml"),Path.Combine(output,"initial.xml"));
            first.RecoveryTestRunId=Guid.NewGuid().ToString("N");
            File.WriteAllText(Path.Combine(output,"events.txt"),"diagnostic=true fullLogisticsRoundTrip=false\nfixture="+fixture+"\nmode="+mode+"\n");
            File.AppendAllText(Path.Combine(output,"events.txt"),"recoveryRunId="+first.RecoveryTestRunId+"\n");
            File.AppendAllText(Path.Combine(output,"events.txt"),"missingInactiveLostSlots="+string.Join(",",missingInactive)+" noFleetReuseCredit=true\n");
            using(var stream=File.Create(Path.Combine(output,"runtime-ledger.xml")))serializer.Serialize(stream,fleet);
            using(var stream=File.Create(Path.Combine(folder,"RailgunReusableFleet.xml")))serializer.Serialize(stream,fleet);
            int index=game.flightState.protoVessels.FindIndex(v=>v.protoPartSnapshots.Any(p=>p.modules.Cast<ProtoPartModuleSnapshot>().Any(m=>m.moduleName==nameof(RailgunFlightPod)&&m.moduleValues.GetValue("fleetId")==first.VesselId)));
            HighLogic.SaveFolder="Railgun-Reentry-Test";HighLogic.CurrentGame=game;
            FlightDriver.StartAndFocusVessel(game,index);
        }
        void Sample(PodRecord pod,Vessel v,MechJebCore c,double now)
        {
            string flags=Path.Combine(output,"physics-flags.tsv");
            if(!File.Exists(flags))File.WriteAllText(flags,"ut\tinfinitePropellant\tinfiniteElectricity\tignoreTemperature\tunbreakableJoints\tnoCrashDamage\n");
            File.AppendAllText(flags,FormattableString.Invariant($"{now:R}\t{CheatOptions.InfinitePropellant}\t{CheatOptions.InfiniteElectricity}\t{CheatOptions.IgnoreMaxTemperature}\t{CheatOptions.UnbreakableJoints}\t{CheatOptions.NoCrashDamage}\n"));
            var parts=v.parts.Where(p=>pod.PartIds.Contains(p.flightID)).ToArray();
            double Amount(string name)=>parts.SelectMany(p=>p.Resources.Cast<PartResource>()).Where(r=>r.resourceName==name).Sum(r=>r.amount);
            if(parts.Length!=pod.PartIds.Count) {
                // Preserve the physical failure before querying empty or damaged
                // inventories; an observer error must not obscure part loss.
                File.WriteAllText(Path.Combine(output,"native-part-loss.txt"),FormattableString.Invariant($"ut={now:R}\nrun={run}\nvessel={v.id}\nexpected={pod.PartIds.Count}\nactual={parts.Length}\nmissing={string.Join(",",pod.PartIds.Except(parts.Select(p=>p.flightID)))}\n"));
                throw new InvalidDataException("Original native subject lost parts; see native-part-loss.txt. No replacement or landing credit.");
            }
            string nativeStep=c.landing.CurrentStep?.GetType().FullName??"none";
            if(nativeStep!=previousNativeStep) {
                string transitions=Path.Combine(output,"native-ap-transitions.tsv");
                if(!File.Exists(transitions))File.WriteAllText(transitions,"ut\tstep\tpodPhase\taltitude\tclearance\tfuel\tmono\n");
                File.AppendAllText(transitions,FormattableString.Invariant($"{now:R}\t{nativeStep}\t{pod.Phase}\t{v.altitude:R}\t{c.vesselState.altitudeBottom:R}\t{Amount("LiquidFuel"):R}\t{Amount("MonoPropellant"):R}\n"));
                previousNativeStep=nativeStep;
            }
            var body=v.mainBody;var up=(v.GetWorldPos3D()-body.position).normalized;
            string nativeControl=Path.Combine(output,"native-control.tsv");
            if(!File.Exists(nativeControl))File.WriteAllText(nativeControl,"ut\tstep\tmode\tspeedTarget\ttargetThrottle\tpreviousThrottle\tkillHorizontal\tmaxAccel\tlimitedAccel\tforwardDotVelocity\tattitudeReference\n");
            File.AppendAllText(nativeControl,FormattableString.Invariant($"{now:R}\t{nativeStep}\t{c.thrust.tmode}\t{c.thrust.trans_spd_act:R}\t{c.thrust.targetThrottle:R}\t{c.thrust.trans_prev_thrust:R}\t{c.thrust.trans_kill_h}\t{c.vesselState.maxThrustAccel:R}\t{c.vesselState.limitedMaxThrustAccel:R}\t{Vector3d.Dot(c.vesselState.forward,c.vesselState.surfaceVelocity):R}\t{c.attitude.attitudeReference}\n"));
            var north=Vector3d.Exclude(up,body.GetWorldSurfacePosition(v.latitude+.01,v.longitude,v.altitude)-v.GetWorldPos3D()).normalized;
            var east=Vector3d.Cross(up,north).normalized;
            var fleet=SandboxFlight.Instance.WarpFleet;
            double lat=fleet.HomeLatitude+6/body.Radius*180/Math.PI;
            double lon=fleet.HomeLongitude+DepotModel.PadX(pod.Number)/(body.Radius*Math.Cos(lat*Math.PI/180))*180/Math.PI;
            var target=body.GetWorldSurfacePosition(lat,lon,v.altitude);
            var offset=Vector3d.Exclude(up,target-v.GetWorldPos3D());
            var a=c.attitude;var requested=a.attitudeGetReferenceRotation(a.attitudeReference)*a.attitudeTarget*Vector3.forward;
            var engines=parts.SelectMany(p=>p.FindModulesImplementing<ModuleEngines>()).ToArray();
            Vector3d thrustAxis=Vector3d.zero;
            foreach(var engine in engines)foreach(var transform in engine.thrustTransforms)thrustAxis-=(Vector3d)transform.forward;
            var panelPath=Path.Combine(output,"native-panels.tsv");
            if(!File.Exists(panelPath))File.WriteAllText(panelPath,"ut\tpart\tignorePitch\tignoreYaw\tdeploy\tactualDegrees\tauthorityPercent\tactuatorSpeed\n");
            foreach(var panel in parts.SelectMany(p=>p.FindModulesImplementing<ModuleAeroSurface>()))
                File.AppendAllText(panelPath,FormattableString.Invariant($"{now:R}\t{panel.part.flightID}\t{panel.ignorePitch}\t{panel.ignoreYaw}\t{panel.deploy}\t{FrozenAirbrake.NativeDeflection(panel):R}\t{panel.aeroAuthorityLimiter:R}\t{panel.actuatorSpeed:R}\n"));
            // Read-only native body-aerodynamics proxy. Surface-module lift and
            // joint forces are deliberately not represented as measured torque.
            Vector3d bodyDrag=Vector3d.zero,bodyLift=Vector3d.zero,dragMoment=Vector3d.zero,liftMoment=Vector3d.zero;
            foreach(var part in parts) {
                Vector3d drag=-(Vector3d)part.dragVectorDir*part.dragScalar;
                Vector3d lift=part.transform.TransformDirection(part.bodyLiftLocalVector);
                bodyDrag+=drag;bodyLift+=lift;
                dragMoment+=Vector3d.Cross((Vector3d)part.transform.TransformPoint(part.CoPOffset)-c.vesselState.CoM,drag);
                liftMoment+=Vector3d.Cross((Vector3d)part.transform.TransformPoint(part.bodyLiftLocalPosition)-c.vesselState.CoM,lift);
            }
            string forces=Path.Combine(output,"steering-physics.tsv");
            if(!File.Exists(forces))File.WriteAllText(forces,"ut\tactualEast\tactualNorth\trequestEast\trequestNorth\tangularX\tangularY\tangularZ\ttorqueX\ttorqueY\ttorqueZ\tmoiX\tmoiY\tmoiZ\tbodyDragKN\tbodyLiftKN\tdragMomentProxy\tliftMomentProxy\tqKpa\tmode\trcsEnabled\trcsAuto\n");
            File.AppendAllText(forces,FormattableString.Invariant($"{now:R}\t{Vector3d.Dot(v.ReferenceTransform.up,east):R}\t{Vector3d.Dot(v.ReferenceTransform.up,north):R}\t{Vector3d.Dot(requested,east):R}\t{Vector3d.Dot(requested,north):R}\t{v.angularVelocityD.x:R}\t{v.angularVelocityD.y:R}\t{v.angularVelocityD.z:R}\t{a.torque.x:R}\t{a.torque.y:R}\t{a.torque.z:R}\t{v.MOI.x:R}\t{v.MOI.y:R}\t{v.MOI.z:R}\t{bodyDrag.magnitude:R}\t{bodyLift.magnitude:R}\t{dragMoment.magnitude:R}\t{liftMoment.magnitude:R}\t{v.dynamicPressurekPa:R}\t{mode}\t{v.ActionGroups[KSPActionGroup.RCS]}\t{a.RCS_auto}\n"));
            string file=Path.Combine(output,"native.tsv");
            if(!File.Exists(file))File.WriteAllText(file,"ut\tphase\taltitude\tclearance\teastError\tnorthError\teastSpeed\tnorthSpeed\tverticalSpeed\tcontroller\tactualTilt\trequestedTilt\tphysicalError\tthrustAxisError\tpitch\tyaw\troll\tthrottle\tthrustKN\tmass\tfuel\toxidizer\tmono\toriginalParts\tlanded\tmaxHeat\twarp\tdt\tstatus\n");
            File.AppendAllText(file,FormattableString.Invariant($"{now:R}\t{pod.Phase}\t{v.altitude:R}\t{c.vesselState.altitudeBottom:R}\t{Vector3d.Dot(offset,east):R}\t{Vector3d.Dot(offset,north):R}\t{Vector3d.Dot(v.srf_velocity,east):R}\t{Vector3d.Dot(v.srf_velocity,north):R}\t{v.verticalSpeed:R}\t{a.activeController}\t{Vector3d.Angle(v.ReferenceTransform.up,up):R}\t{Vector3d.Angle(requested,up):R}\t{Vector3d.Angle(requested,v.ReferenceTransform.up):R}\t{Vector3d.Angle(thrustAxis,v.ReferenceTransform.up):R}\t{v.ctrlState.pitch:R}\t{v.ctrlState.yaw:R}\t{v.ctrlState.roll:R}\t{v.ctrlState.mainThrottle:R}\t{engines.Sum(e=>e.finalThrust):R}\t{v.totalMass:R}\t{Amount("LiquidFuel"):R}\t{Amount("Oxidizer"):R}\t{Amount("MonoPropellant"):R}\t{parts.Length}\t{v.Landed}\t{parts.Max(p=>Math.Max(p.temperature/p.maxTemp,p.skinTemperature/p.skinMaxTemp)):R}\t{TimeWarp.CurrentRate:R}\t{TimeWarp.fixedDeltaTime:R}\t{RecoveryGuidance.TerminalStatus(c.landing)}\n"));
            AtomicStatusFile.TryWrite(Path.Combine(folder,"LandingLab.status.txt"),FormattableString.Invariant($"run={run} ut={now:R} phase={pod.Phase} altitude={v.altitude:F1} fuel={Amount("LiquidFuel"):F1} controller={a.activeController} error={a.attitudeError:F2}\n"));
        }
    }
}
