using System;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using MuMech;
using UnityEngine;

namespace RailgunSandbox
{
    // A disconnected replay of captured native objects; never logistics credit.
    [KSPAddon(KSPAddon.Startup.MainMenu,true)]
    public sealed class RendezvousFixtureLab : MonoBehaviour
    {
        string folder,output; bool awaiting,finished,reviewed; double startUt,next; float startReal;
        void Awake()
        {
            var args=Environment.GetCommandLineArgs();
            if(!args.Contains("-railgun-reentry")||!args.Contains("-railgun-rendezvous-fixture")){enabled=false;return;}
            DontDestroyOnLoad(gameObject);folder=Path.Combine(KSPUtil.ApplicationRootPath,"saves","Railgun-Reentry-Test");
            GameEvents.onFlightReady.Add(Ready);
            File.WriteAllText(Path.Combine(folder,"RendezvousLab.boot"),DateTime.UtcNow.ToString("O"));
        }
        void Ready(){if(output!=null){awaiting=false;SandboxFlight.RendezvousFixtureReady=true;}}
        void OnDestroy(){if(folder!=null)GameEvents.onFlightReady.Remove(Ready);}
        void Update()
        {
            if(folder==null||finished)return;
            try {
                string command=Path.Combine(folder,"RendezvousLab.command");
                if(File.Exists(command)) {
                    var text=File.ReadAllText(command).Trim();File.Delete(command);
                    if(text=="quit"){Application.Quit();return;}
                    var words=text.Split(':');
                    if(output!=null||words.Length!=3||words[0]!="reload"||!Valid(words[1],"case-")||!Valid(words[2],"run-"))throw new InvalidDataException("Invalid rendezvous fixture request");
                    if(NetworkFleet.IsMultiplayer||NetworkFleet.Connected)throw new InvalidOperationException("Disconnected diagnostic only");
                    var game=GamePersistence.LoadGame(words[1],"Railgun-Reentry-Test",true,false);
                    if(game==null||game.Mode!=Game.Modes.SANDBOX)throw new InvalidDataException("Missing native sandbox fixture");
                    FleetState fleet;using(var f=File.OpenRead(Path.Combine(folder,words[1]+".xml")))fleet=(FleetState)new XmlSerializer(typeof(FleetState)).Deserialize(f);
                    fleet.Validate();var pod=fleet.Pods.Single(p=>p.Number==1);
                    if(pod.Phase!=FlightPhase.Rendezvous||pod.RecoveryTest||pod.CargoTransferred)throw new InvalidDataException("Original undelivered rendezvous required");
                    int index=-1;
                    foreach(var record in fleet.Pods) {
                        var native=game.flightState.protoVessels.Single(v=>v.protoPartSnapshots.Any(p=>p.modules.Cast<ProtoPartModuleSnapshot>().Any(m=>m.moduleName==nameof(RailgunFlightPod)&&m.moduleValues.GetValue("fleetId")==record.VesselId)));
                        if(!native.protoPartSnapshots.Where(p=>p.flightID!=record.LaunchShellPartId).Select(p=>p.flightID).OrderBy(x=>x).SequenceEqual(record.PartIds.OrderBy(x=>x)))throw new InvalidDataException("Original physical inventory mismatch");
                        if(record.Number==1)index=game.flightState.protoVessels.IndexOf(native);
                    }
                    output=Path.Combine(folder,"rendezvous-lab",words[2]);if(Directory.Exists(output))throw new IOException("Never overwrite evidence");Directory.CreateDirectory(output);
                    File.Copy(Path.Combine(folder,words[1]+".sfs"),Path.Combine(output,"initial.sfs"));File.Copy(Path.Combine(folder,words[1]+".xml"),Path.Combine(output,"initial.xml"));
                    File.Copy(Path.Combine(folder,words[1]+".xml"),Path.Combine(folder,"RailgunReusableFleet.xml"),true);
                    awaiting=true;startUt=game.flightState.universalTime;startReal=Time.realtimeSinceStartup;
                    HighLogic.SaveFolder="Railgun-Reentry-Test";HighLogic.CurrentGame=game;FlightDriver.StartAndFocusVessel(game,index);return;
                }
                if(output==null||awaiting||!FlightGlobals.ready||!HighLogic.LoadedSceneIsFlight||SandboxFlight.Instance==null)return;
                if(NetworkFleet.IsMultiplayer||NetworkFleet.Connected)throw new InvalidOperationException("Multiplayer connection invalidates fixture");
                var podNow=SandboxFlight.Instance.WarpFleet.Pods.Single(p=>p.Number==1);var v=StockPods.Vessel(podNow);
                if(v==null||!v.loaded||v.packed||v!=FlightGlobals.ActiveVessel)return;
                var core=v.GetMasterMechJeb();if(core==null)return;
                double now=Planetarium.GetUniversalTime();if(now<next)return;next=now+.1;
                if(!core.target.NormalTargetExists) {
                    if(now-startUt>15)Finish("InvalidFixture: native receiver target did not bind");
                    return;
                }
                var node=v.patchedConicSolver?.maneuverNodes.FirstOrDefault();
                var parts=v.parts.Where(p=>podNow.PartIds.Contains(p.flightID)).ToArray();if(parts.Length!=29)throw new InvalidDataException("Original subject lost parts");
                var cargo=parts.Single(p=>p.flightID==podNow.CargoPart).Resources.Cast<PartResource>().Single(r=>r.resourceName==podNow.Resource);
                if(cargo.amount!=100||cargo.flowState)throw new InvalidDataException("Locked cargo changed before delivery");
                string path=Path.Combine(output,"native.tsv");if(!File.Exists(path))File.WriteAllText(path,"ut\tphase\tdistance\trelativeSpeed\tPe\tAp\tnodeUt\tnodeDv\tplannedPe\tthrust\tfuel\toriginalParts\twarp\tdt\tcheats\n");
                File.AppendAllText(path,FormattableString.Invariant($"{now:R}\t{podNow.Phase}\t{core.target.Distance:R}\t{core.target.RelativeVelocity.magnitude:R}\t{v.orbit.PeA:R}\t{v.orbit.ApA:R}\t{node?.UT??double.NaN:R}\t{node?.DeltaV.magnitude??0:R}\t{node?.nextPatch?.PeA??double.NaN:R}\t{parts.SelectMany(p=>p.FindModulesImplementing<ModuleEngines>()).Sum(e=>e.finalThrust):R}\t{parts.Where(p=>p.flightID!=podNow.CargoPart).SelectMany(p=>p.Resources.Cast<PartResource>()).Where(r=>r.resourceName=="LiquidFuel").Sum(r=>r.amount):R}\t{parts.Length}\t{TimeWarp.CurrentRate:R}\t{TimeWarp.fixedDeltaTime:R}\t{CheatOptions.InfinitePropellant||CheatOptions.InfiniteElectricity||CheatOptions.IgnoreMaxTemperature||CheatOptions.UnbreakableJoints||CheatOptions.NoCrashDamage}\n"));
                string control=Path.Combine(output,"native-control.tsv");
                if(!File.Exists(control))File.WriteAllText(control,"ut\tnodeEnabled\tburnTriggered\tattitudeError\tremainingDv\toxidizer\tmono\tcargo\tcargoFlow\tmaxHeat\n");
                var propulsion=parts.Where(p=>p.flightID!=podNow.CargoPart).SelectMany(p=>p.Resources.Cast<PartResource>()).ToArray();
                File.AppendAllText(control,FormattableString.Invariant($"{now:R}\t{core.node.enabled}\t{core.node.burnTriggered}\t{core.attitude.attitudeAngleFromTarget():R}\t{node?.GetBurnVector(v.orbit).magnitude??0:R}\t{propulsion.Where(p=>p.resourceName=="Oxidizer").Sum(p=>p.amount):R}\t{propulsion.Where(p=>p.resourceName=="MonoPropellant").Sum(p=>p.amount):R}\t{cargo.amount:R}\t{cargo.flowState}\t{parts.Max(p=>Math.Max(p.temperature/p.maxTemp,p.skinTemperature/p.skinMaxTemp)):R}\n"));
                if(node!=null&&node.nextPatch!=null&&!reviewed&&!FlightRules.SafeOrbit(node.nextPatch.PeA,node.nextPatch.eccentricity,v.mainBody.atmosphereDepth)&&core.target.NormalTargetExists) {
                    // Same 15s native close-intercept planning lead, read-only prediction.
                    double at=now+15;var dv=OrbitalManeuverCalculator.DeltaVToMatchVelocities(v.orbit,at,core.target.TargetOrbit);var predicted=v.orbit.PerturbedOrbit(at,dv);
                    File.WriteAllText(Path.Combine(output,"velocity-match-prediction.txt"),FormattableString.Invariant($"ut={now:R}\nburnUt={at:R}\ndistance={core.target.Distance:R}\noriginalDv={node.DeltaV.magnitude:R}\noriginalPe={node.nextPatch.PeA:R}\nmatchDv={dv.magnitude:R}\nmatchPe={predicted.PeA:R}\nmatchEcc={predicted.eccentricity:R}\nsafeOrbit={FlightRules.SafeOrbit(predicted.PeA,predicted.eccentricity,v.mainBody.atmosphereDepth)}\nreadOnly=true\nfullLogisticsRoundTrip=false\n"));reviewed=true;
                    string curve=Path.Combine(output,"intercept-horizons.tsv");
                    File.WriteAllText(curve,"ut\tburnUt\tduration\tdv\tPe\tecc\tsafeOrbit\n");
                    // Bounded native prediction queries, not repeated flights
                    // or edits to a running trajectory. The original native
                    // short approach uses 100s; bracket up to one current orbit.
                    double maximum=Math.Max(100,v.orbit.period);
                    for(double duration=100;duration<=maximum;duration*=2) {
                        var intercept=OrbitalManeuverCalculator.DeltaVToInterceptAtTime(v.orbit,at,core.target.TargetOrbit,duration);
                        var orbit=v.orbit.PerturbedOrbit(at,intercept);
                        File.AppendAllText(curve,FormattableString.Invariant($"{now:R}\t{at:R}\t{duration:R}\t{intercept.magnitude:R}\t{orbit.PeA:R}\t{orbit.eccentricity:R}\t{FlightRules.SafeOrbit(orbit.PeA,orbit.eccentricity,v.mainBody.atmosphereDepth)}\n"));
                    }
                }
                if(RendezvousSafety.Held(podNow.VesselId,out var reason)){Finish("NativeSafetyHeld: "+reason);return;}
                if(podNow.Phase!=FlightPhase.Rendezvous){Finish("NativePhase: "+podNow.Phase);return;}
                if(now-startUt>600||Time.realtimeSinceStartup-startReal>720)Finish("BoundReached");
            }catch(Exception e){if(output!=null)File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());else File.WriteAllText(Path.Combine(folder,"RendezvousLab.error"),e.ToString());finished=true;Application.Quit();}
        }
        static bool Valid(string s,string prefix)=>s.StartsWith(prefix)&&s.Length<60&&s.All(c=>char.IsLetterOrDigit(c)||c=='-'||c=='_');
        void Finish(string outcome)
        {
            finished=true;
            GamePersistence.SaveGame("rendezvous-final",HighLogic.SaveFolder,SaveMode.OVERWRITE);
            File.Copy(Path.Combine(folder,"rendezvous-final.sfs"),Path.Combine(output,"final.sfs"));
            using(var stream=File.Create(Path.Combine(output,"final.xml")))new XmlSerializer(typeof(FleetState)).Serialize(stream,SandboxFlight.Instance.WarpFleet);
            File.WriteAllText(Path.Combine(output,"outcome.txt"),outcome+"\nfullLogisticsRoundTrip=false\n");Application.Quit();
        }
    }
}
