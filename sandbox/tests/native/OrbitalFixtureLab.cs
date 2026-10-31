using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using HarmonyLib;
using MuMech;
using UnityEngine;

namespace RailgunSandbox
{
    // Separate validation addon: authentic saved ascent -> native stable orbit.
    // It never supplies velocity, fuel, an invented phase, or logistics credit.
    [KSPAddon(KSPAddon.Startup.MainMenu,true)]
    public sealed class OrbitalFixtureLab : MonoBehaviour
    {
        string folder,output,run;
        double startedUt,nextSample;
        float startedReal,nextPoll;
        bool finished,awaitingScene;
        int timingState=-1;
        bool preburnSaved,partialSaved,earlyBurnSaved;
        readonly XmlSerializer serializer=new XmlSerializer(typeof(FleetState));
        void Awake()
        {
            var args=Environment.GetCommandLineArgs();
            if(!args.Contains("-railgun-reentry")||!args.Contains("-railgun-orbit-fixture")){enabled=false;return;}
            DontDestroyOnLoad(gameObject);
            GameEvents.onFlightReady.Add(FlightReady);
            folder=Path.Combine(KSPUtil.ApplicationRootPath,"saves","Railgun-Reentry-Test");
            File.WriteAllText(Path.Combine(folder,"OrbitalLab.boot.txt"),DateTime.UtcNow.ToString("O"));
        }
        void FlightReady(){if(run!=null)awaitingScene=false;}
        void OnDestroy(){if(folder!=null)GameEvents.onFlightReady.Remove(FlightReady);}
        void Update()
        {
            if(folder==null||finished||Time.realtimeSinceStartup<nextPoll)return;
            nextPoll=Time.realtimeSinceStartup+.1f;
            try {
                string command=Path.Combine(folder,"OrbitalLab.command");
                if(File.Exists(command)) {
                    string text=File.ReadAllText(command).Trim();File.Delete(command);
                    if(text=="quit"){Application.Quit();return;}
                    var words=text.Split(':');
                    if(words.Length!=3||words[0]!="reload"||!Valid(words[1],"case-")||!Valid(words[2],"run-"))throw new InvalidDataException("Invalid orbital fixture request");
                    Reload(words[1],words[2]);return;
                }
                if(run==null||awaitingScene||!HighLogic.LoadedSceneIsFlight||!FlightGlobals.ready||SandboxFlight.Instance==null)return;
                if(NetworkFleet.Connected||NetworkFleet.IsMultiplayer||!SandboxFlight.ReentryTestSave)throw new InvalidOperationException("Orbital fixture requires disconnected private single-player save");
                var fleet=SandboxFlight.Instance.WarpFleet;
                var pod=fleet?.Pods.Single(p=>p.Number==1);var v=pod==null?null:StockPods.Vessel(pod);
                if(v==null||v.packed||!v.loaded||v!=FlightGlobals.ActiveVessel)return;
                var core=v.GetMasterMechJeb();if(core==null)return;
                double now=Planetarium.GetUniversalTime();
                if(now<nextSample)return;nextSample=now+.1;
                var parts=v.parts.Where(p=>pod.PartIds.Contains(p.flightID)).ToArray();
                if(parts.Length!=pod.PartIds.Count)throw new InvalidDataException("Original physical subject lost parts");
                Sample(pod,v,core,parts,now);
                if(pod.Phase==FlightPhase.Rendezvous) {
                    Finish(FlightRules.SafeOrbit(v.orbit.PeA,v.orbit.eccentricity,v.mainBody.atmosphereDepth)?"StableOrbit":"UnsafeRendezvous",pod,v,core);return;
                }
                if(pod.Phase==FlightPhase.Stranded||pod.Phase==FlightPhase.Lost){Finish("FlightFailed",pod,v,core);return;}
                if(pod.Phase!=FlightPhase.Ascent)throw new InvalidOperationException("Unexpected phase during bounded orbital segment: "+pod.Phase);
                if(now-startedUt>1000||Time.realtimeSinceStartup-startedReal>1100)Finish("BoundReached",pod,v,core);
            } catch(Exception e) {
                finished=true;File.AppendAllText(Path.Combine(folder,"OrbitalLab.errors.txt"),e+"\n");
                Debug.LogError("[RailgunOrbitalFixture] "+e);Application.Quit();
            }
        }
        static bool Valid(string s,string prefix)=>s.StartsWith(prefix,StringComparison.Ordinal)&&s.Length<=64&&s.All(c=>char.IsLetterOrDigit(c)||c=='-'||c=='_');
        void Reload(string fixture,string label)
        {
            if(run!=null)throw new InvalidOperationException("One orbital segment per process; preserve the original scene timeline");
            if(NetworkFleet.Connected||NetworkFleet.IsMultiplayer)throw new InvalidOperationException("Never reload an orbital fixture while connected to Luna");
            var game=GamePersistence.LoadGame(fixture,"Railgun-Reentry-Test",true,false);
            if(game==null||game.Mode!=Game.Modes.SANDBOX)throw new InvalidDataException("Native sandbox fixture missing");
            if(PartLoader.getPartInfoByName("krlLaunchShell")==null)throw new InvalidDataException("Native launch-shell part is not registered; reject setup before flight");
            FleetState fleet;using(var stream=File.OpenRead(Path.Combine(folder,fixture+".xml")))fleet=(FleetState)serializer.Deserialize(stream);
            fleet.Validate();var pod=fleet.Pods.Single(p=>p.Number==1);
            if(pod.Phase!=FlightPhase.Ascent||!pod.RailOnlyAscent||pod.RecoveryTest||pod.CargoTransferred||pod.FlightsCompleted!=0)throw new InvalidDataException("Untouched undelivered rail-ascent ledger required");
            foreach(var record in fleet.Pods) {
                var native=game.flightState.protoVessels.SingleOrDefault(v=>v.protoPartSnapshots.Any(p=>p.modules.Cast<ProtoPartModuleSnapshot>().Any(m=>m.moduleName==nameof(RailgunFlightPod)&&m.moduleValues.GetValue("fleetId")==record.VesselId)));
                if(native==null)throw new InvalidDataException("Missing original physical fixture pod");
                var ids=native.protoPartSnapshots.Select(p=>p.flightID).Where(id=>id!=record.LaunchShellPartId).OrderBy(id=>id);
                if(!ids.SequenceEqual(record.PartIds.OrderBy(id=>id)))throw new InvalidDataException("Original part identity mismatch");
            }
            output=Path.Combine(folder,"orbital-lab",label);if(Directory.Exists(output))throw new InvalidOperationException("Never overwrite orbital evidence");Directory.CreateDirectory(output);
            File.Copy(Path.Combine(folder,fixture+".sfs"),Path.Combine(output,"initial.sfs"));
            File.Copy(Path.Combine(folder,fixture+".xml"),Path.Combine(output,"initial.xml"));
            File.Copy(Path.Combine(folder,fixture+".xml"),Path.Combine(folder,"RailgunReusableFleet.xml"),true);
            File.WriteAllText(Path.Combine(output,"events.txt"),"diagnostic=true orbitalSegmentOnly=true fullLogisticsRoundTrip=false\nfixture="+fixture+"\nrun="+label+"\n");
            int index=game.flightState.protoVessels.FindIndex(v=>v.protoPartSnapshots.Any(p=>p.modules.Cast<ProtoPartModuleSnapshot>().Any(m=>m.moduleName==nameof(RailgunFlightPod)&&m.moduleValues.GetValue("fleetId")==pod.VesselId)));
            run=label;awaitingScene=true;startedUt=game.flightState.universalTime;startedReal=Time.realtimeSinceStartup;nextSample=0;timingState=-1;
            HighLogic.SaveFolder="Railgun-Reentry-Test";HighLogic.CurrentGame=game;FlightDriver.StartAndFocusVessel(game,index);
        }
        void Sample(PodRecord pod,Vessel v,MechJebCore core,Part[] parts,double now)
        {
            var engines=parts.SelectMany(p=>p.FindModulesImplementing<ModuleEngines>()).ToArray();
            var resources=parts.Where(p=>p.flightID!=pod.CargoPart).SelectMany(p=>p.Resources.Cast<PartResource>()).ToArray();
            double Amount(string name)=>resources.Where(r=>r.resourceName==name).Sum(r=>r.amount);
            var node=v.patchedConicSolver?.maneuverNodes.FirstOrDefault();
            var cargo=parts.Single(p=>p.flightID==pod.CargoPart).Resources.Cast<PartResource>().Single(r=>r.resourceName==pod.Resource);
            if(node!=null&&pod.CircularizationPlanned&&!pod.CircularizationStarted&&!preburnSaved&&node.UT-now<90&&node.UT-now>45) {
                Checkpoint("preburn",pod,core,node,now);preburnSaved=true;
            }
            if(node!=null&&pod.CircularizationStarted&&!earlyBurnSaved&&engines.Sum(e=>e.finalThrust)>.05&&
                !FlightRules.SafeOrbit(v.orbit.PeA,v.orbit.eccentricity,v.mainBody.atmosphereDepth)&&
                node.GetBurnVector(v.orbit).magnitude>pod.CircularizationDeltaV*.55&&
                node.GetBurnVector(v.orbit).magnitude<pod.CircularizationDeltaV*.65) {
                Checkpoint("partial-early",pod,core,node,now);earlyBurnSaved=true;
            }
            if(node!=null&&pod.CircularizationStarted&&!partialSaved&&engines.Sum(e=>e.finalThrust)>.05&&
                FlightRules.SafeOrbit(v.orbit.PeA,v.orbit.eccentricity,v.mainBody.atmosphereDepth)&&
                node.GetBurnVector(v.orbit).magnitude>5&&node.GetBurnVector(v.orbit).magnitude<pod.CircularizationDeltaV*.35) {
                Checkpoint("partial-safe",pod,core,node,now);partialSaved=true;
            }
            string file=Path.Combine(output,"native.tsv");
            if(!File.Exists(file))File.WriteAllText(file,"ut\tphase\taltitude\tperiapsis\tapoapsis\teccentricity\tnodeCount\tnodeUt\tplannedDv\tremainingDv\tattitudeError\tnodeEnabled\tburnTriggered\tthrustKN\tfuel\toxidizer\tmono\toriginalParts\trcs\twarp\tdt\tmaxHeat\tinfinitePropellant\tinfiniteElectricity\tignoreTemperature\tunbreakableJoints\tnoCrashDamage\tcircularizationStarted\tshellPanelsReleased\tshellBaseReleased\tshellClearUt\tcargoAmount\tcargoFlow\n");
            File.AppendAllText(file,FormattableString.Invariant($"{now:R}\t{pod.Phase}\t{v.altitude:R}\t{v.orbit.PeA:R}\t{v.orbit.ApA:R}\t{v.orbit.eccentricity:R}\t{v.patchedConicSolver?.maneuverNodes.Count??0}\t{node?.UT??double.NaN:R}\t{node?.DeltaV.magnitude??0:R}\t{(node==null?0:node.GetBurnVector(v.orbit).magnitude):R}\t{core.attitude.attitudeAngleFromTarget():R}\t{core.node.enabled}\t{core.node.burnTriggered}\t{engines.Sum(e=>e.finalThrust):R}\t{Amount("LiquidFuel"):R}\t{Amount("Oxidizer"):R}\t{Amount("MonoPropellant"):R}\t{parts.Length}\t{v.ActionGroups[KSPActionGroup.RCS]}\t{TimeWarp.CurrentRate:R}\t{TimeWarp.fixedDeltaTime:R}\t{parts.Max(p=>Math.Max(p.temperature/p.maxTemp,p.skinTemperature/p.skinMaxTemp)):R}\t{CheatOptions.InfinitePropellant}\t{CheatOptions.InfiniteElectricity}\t{CheatOptions.IgnoreMaxTemperature}\t{CheatOptions.UnbreakableJoints}\t{CheatOptions.NoCrashDamage}\t{pod.CircularizationStarted}\t{pod.LaunchShellPanelsReleased}\t{pod.LaunchShellBaseReleased}\t{pod.LaunchShellClearAt:R}\t{cargo.amount:R}\t{cargo.flowState}\n"));
            int state=node==null?0:core.node.enabled?2:1;
            if(node!=null&&state!=timingState) {
                var method=AccessTools.Method(typeof(MechJebModuleNodeExecutor),"BurnTime",new[]{typeof(double),typeof(double).MakeByRefType(),typeof(double).MakeByRefType()});
                if(method==null||method.ReturnType!=typeof(double))throw new MissingMethodException("Pinned native BurnTime API unavailable");
                object[] args={node.DeltaV.magnitude,0d,0d};double duration=(double)method.Invoke(core.node,args);
                File.AppendAllText(Path.Combine(output,"native-burn-estimates.tsv"),FormattableString.Invariant($"{now:R}\t{state}\t{engines.Count(e=>e.EngineIgnited)}\t{duration:R}\t{(double)args[1]:R}\t{(double)args[2]:R}\t{pod.CircularizationHalfBurnTime:R}\n"));
            }
            timingState=state;
            AtomicStatusFile.TryWrite(Path.Combine(folder,"OrbitalLab.status.txt"),FormattableString.Invariant($"run={run} ut={now:R} phase={pod.Phase} altitude={v.altitude:F1} Pe={v.orbit.PeA:F1} fuel={Amount("LiquidFuel"):F1}\n"));
        }
        void Checkpoint(string name,PodRecord pod,MechJebCore core,ManeuverNode node,double now)
        {
            string target=Path.Combine(output,"checkpoint-"+name);Directory.CreateDirectory(target);
            string save="orbital-"+name;
            if(string.IsNullOrEmpty(GamePersistence.SaveGame(save,HighLogic.SaveFolder,SaveMode.OVERWRITE)))throw new IOException("Native checkpoint save failed");
            File.Copy(Path.Combine(folder,save+".sfs"),Path.Combine(target,"snapshot.sfs"));
            using(var stream=File.Create(Path.Combine(target,"fleet.xml")))serializer.Serialize(stream,SandboxFlight.Instance.WarpFleet);
            File.WriteAllText(Path.Combine(target,"capture.txt"),FormattableString.Invariant($"ut={now:R}\nphase={pod.Phase}\nnodeUt={node.UT:R}\nremainingDv={node.GetBurnVector(core.vessel.orbit).magnitude:R}\nexecutorEnabled={core.node.enabled}\nfullLogisticsRoundTrip=false\n"));
        }
        void Finish(string result,PodRecord pod,Vessel v,MechJebCore core)
        {
            finished=true;
            // Stop the bounded test before ordinary rendezvous can deliver cargo.
            var owner=SandboxFlight.Instance;owner.enabled=false;
            core.GetComputerModule<MechJebModuleRendezvousAutopilot>().users.Remove(owner);
            core.node.Abort();core.thrust.ThrustOff();
            GamePersistence.SaveGame("orbital-final",HighLogic.SaveFolder,SaveMode.OVERWRITE);
            File.Copy(Path.Combine(folder,"orbital-final.sfs"),Path.Combine(output,"final.sfs"));
            using(var stream=File.Create(Path.Combine(output,"final.xml")))serializer.Serialize(stream,owner.WarpFleet);
            File.WriteAllText(Path.Combine(output,"outcome.txt"),FormattableString.Invariant($"result={result}\nphase={pod.Phase}\nut={Planetarium.GetUniversalTime():R}\nperiapsis={v.orbit.PeA:R}\neccentricity={v.orbit.eccentricity:R}\nfullLogisticsRoundTrip=false\n"));
            Application.Quit();
        }
    }
}
