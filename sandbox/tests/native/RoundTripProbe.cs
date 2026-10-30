using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using MuMech;

namespace RailgunSandbox
{
    // Read-only native evidence, compiled only into the loopback test addon.
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class RoundTripProbe : MonoBehaviour
    {
        readonly Dictionary<int,string> changes=new Dictionary<int,string>();
        float next;
        int snapshot,errors;
        static string Port(ModuleDockingNode node)=>node?.fsm?.CurrentState?.name??node?.state??"";
        void Update()
        {
            if(errors>=3||Time.realtimeSinceStartup<next)return;
            next=Time.realtimeSinceStartup+.25f;
            if(!Environment.GetCommandLineArgs().Any(s=>s=="-railgun-mp-test"||s=="-railgun-mp-test-b")||
                !NetworkFleet.Connected||!NetworkFleet.IsMultiplayer||!NetworkFleet.Shared.Ready||!FlightGlobals.ready)return;
            string directory=Path.Combine(KSPUtil.ApplicationRootPath,"Railgun-MP-Test","roundtrip");
            Directory.CreateDirectory(directory);
            try {
                foreach(var pod in NetworkFleet.Shared.Local.Pods.Where(p=>p.Created))Sample(pod,directory);
            } catch(Exception error) {
                errors++;File.AppendAllText(Path.Combine(directory,"probe-errors.txt"),error+"\n");
            }
        }
        void Sample(PodRecord pod,string directory)
        {
            var vessel=StockPods.Vessel(pod);if(vessel==null)return;
            var ids=new HashSet<uint>(pod.PartIds);
            var native=vessel.loaded?vessel.parts.Where(p=>ids.Contains(p.flightID)).ToArray():new Part[0];
            var own=native.FirstOrDefault(p=>p.flightID==pod.OwnPort)?.FindModuleImplementing<ModuleDockingNode>();
            var marker=native.Select(p=>p.FindModuleImplementing<RailgunFlightPod>()).FirstOrDefault(p=>p!=null&&p.fleetId==pod.VesselId);
            var receiver=FlightGlobals.Vessels.FirstOrDefault(v=>v!=null&&v.loaded&&v.parts.Any(p=>p.flightID==pod.TargetAnchor));
            var cargo=native.FirstOrDefault(p=>p.flightID==pod.CargoPart)?.Resources[pod.Resource];
            double liquid=Amount(native,"LiquidFuel"),oxidizer=Amount(native,"Oxidizer"),mono=Amount(native,"MonoPropellant");
            double received=receiver==null?double.NaN:receiver.parts.Where(p=>!ids.Contains(p.flightID)).SelectMany(p=>p.Resources.Cast<PartResource>())
                .Where(r=>r.resourceName==pod.Resource).Sum(r=>r.amount);
            double heat=native.Length==0?double.NaN:native.Max(p=>Math.Max(p.temperature/Math.Max(1,p.maxTemp),p.skinTemperature/Math.Max(1,p.skinMaxTemp)));
            double distance=receiver==null?double.NaN:(vessel.GetWorldPos3D()-receiver.GetWorldPos3D()).magnitude;
            double closing=receiver==null?double.NaN:(vessel.obt_velocity-receiver.obt_velocity).magnitude;
            foreach(var panel in native.SelectMany(p=>p.FindModulesImplementing<ModuleAeroSurface>())) {
                var flap=panel.part.FindModelTransform("Flap");
                if(flap==null)continue;
                string pathBrake=Path.Combine(directory,"airbrakes.tsv");
                if(!File.Exists(pathBrake))File.WriteAllText(pathBrake,"ut\tpod\tphase\tuid\tdeploy\tqx\tqy\tqz\tqw\tskinK\tskinLimitK\tnativeOwner\tpilotLease\n");
                var q=flap.localRotation;
                File.AppendAllText(pathBrake,FormattableString.Invariant($"{Planetarium.GetUniversalTime():R}\t{pod.Number}\t{pod.Phase}\t{panel.part.flightID}\t{panel.deploy}\t{q.x:R}\t{q.y:R}\t{q.z:R}\t{q.w:R}\t{panel.part.skinTemperature:R}\t{panel.part.skinMaxTemp:R}\t{NetworkFleet.Shared.NativeOwner(pod,false)}\t{NetworkFleet.Shared.LeaseValid(pod.Number)}\n"));
            }
            if(native.Length>0 && !vessel.packed) {
                string orientation=Path.Combine(directory,"orientation.tsv");
                if(!File.Exists(orientation))File.WriteAllText(orientation,"ut\tpod\tphase\tlanded\tbody\tuprightDegrees\tspeed\tlatitude\tlongitude\n");
                double upright=Vector3d.Angle(vessel.rootPart.transform.up,(vessel.GetWorldPos3D()-vessel.mainBody.position).normalized);
                File.AppendAllText(orientation,FormattableString.Invariant($"{Planetarium.GetUniversalTime():R}\t{pod.Number}\t{pod.Phase}\t{vessel.Landed}\t{vessel.mainBody.bodyName}\t{upright:R}\t{vessel.srfSpeed:R}\t{vessel.latitude:R}\t{vessel.longitude:R}\n"));
            }
            string path=Path.Combine(directory,"native.tsv");
            if(!File.Exists(path))File.WriteAllText(path,"ut\tpod\tphase\torder\tactive\tloaded\tpacked\tpilotLease\tnativeOwner\ttotalParts\toriginalParts\townPort\totherPort\totherState\tsameVessel\tcargo\treceiverResource\treceipt\tliquidFuel\toxidizer\tmono\theatRatio\tdistance\tclosingSpeed\tlanded\tlatitude\tlongitude\taltitude\tsurfaceSpeed\twarp\tfixedDt\n");
            File.AppendAllText(path,FormattableString.Invariant($"{Planetarium.GetUniversalTime():R}\t{pod.Number}\t{pod.Phase}\t{pod.OrderId}\t{vessel==FlightGlobals.ActiveVessel}\t{vessel.loaded}\t{vessel.packed}\t{NetworkFleet.Shared.LeaseValid(pod.Number)}\t{NetworkFleet.Shared.NativeOwner(pod,false)}\t{vessel.parts.Count}\t{native.Length}\t{Port(own)}\t{own?.otherNode?.part.flightID??0}\t{Port(own?.otherNode)}\t{receiver==vessel}\t{cargo?.amount??double.NaN:R}\t{received:R}\t{marker?.completedOrder??""}\t{liquid:R}\t{oxidizer:R}\t{mono:R}\t{heat:R}\t{distance:R}\t{closing:R}\t{vessel.Landed}\t{vessel.latitude:R}\t{vessel.longitude:R}\t{vessel.altitude:R}\t{vessel.srfSpeed:R}\t{TimeWarp.CurrentRate:R}\t{TimeWarp.fixedDeltaTime:R}\n"));
            var core=marker?.part.FindModuleImplementing<MuMech.MechJebCore>();
            if(core!=null && vessel.loaded && !vessel.packed) {
                var rz=core.GetComputerModule<MuMech.MechJebModuleRendezvousAutopilot>();
                var node=vessel.patchedConicSolver?.maneuverNodes.FirstOrDefault();
                var target=core.target.TargetOrbit;
                string guidance=Path.Combine(directory,"guidance.tsv");
                if(!File.Exists(guidance))File.WriteAllText(guidance,"ut\tpod\tphase\trendezvousEnabled\tnormalTarget\ttargetDistance\tstatus\tnodes\tnodeUT\tnodeDV\tnodeEnabled\tburnTriggered\taligned\tattitudeEnabled\tthrustEnabled\tcoreEnabled\tmaster\trelativeInclination\n");
                double targetDistance=double.NaN;
                try { if(core.target.Target?.GetTransform()!=null)targetDistance=core.target.Distance; } catch(NullReferenceException) { }
                File.AppendAllText(guidance,FormattableString.Invariant($"{Planetarium.GetUniversalTime():R}\t{pod.Number}\t{pod.Phase}\t{rz.enabled}\t{core.target.NormalTargetExists}\t{targetDistance:R}\t{rz.status}\t{vessel.patchedConicSolver?.maneuverNodes.Count??0}\t{node?.UT??double.NaN:R}\t{node?.DeltaV.magnitude??double.NaN:R}\t{core.node.enabled}\t{core.node.burnTriggered}\t{core.node.alignedForBurn}\t{core.attitude.enabled}\t{core.thrust.enabled}\t{core.enabled}\t{vessel.GetMasterMechJeb()==core}\t{(target==null?double.NaN:MuMech.OrbitExtensions.RelativeInclination(vessel.orbit,target)):R}\n"));
            }
            string key=pod.Phase+"/"+Port(own)+"/"+(own?.otherNode?.part.flightID??0)+"/"+(marker?.completedOrder??"")+"/"+Math.Round(cargo?.amount??-1,4);
            if(!changes.TryGetValue(pod.Number,out var previous)||key!=previous) {
                changes[pod.Number]=key;
                string take=Path.Combine(directory,DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff")+"-"+(snapshot++).ToString("D4")+"-pod"+pod.Number+"-"+pod.Phase);
                Directory.CreateDirectory(take);
                SaveVessel(vessel,Path.Combine(take,"pod.txt"));
                if(receiver!=null&&receiver!=vessel)SaveVessel(receiver,Path.Combine(take,"receiver.txt"));
                File.WriteAllText(Path.Combine(take,"fleet.xml"),FleetWire.Text(NetworkFleet.Shared.Local),System.Text.Encoding.Unicode);
                File.WriteAllText(Path.Combine(take,"ut.txt"),Planetarium.GetUniversalTime().ToString("R",System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        static double Amount(Part[] parts,string resource)=>parts.Length==0?double.NaN:parts
            .Where(p=>p.partInfo.name!=StockPods.MonoCargo&&p.partInfo.name!=StockPods.FuelCargo)
            .SelectMany(p=>p.Resources.Cast<PartResource>()).Where(r=>r.resourceName==resource).Sum(r=>r.amount);
        static void SaveVessel(Vessel vessel,string path)
        {
            var node=new ConfigNode("VESSEL");
            var proto=vessel.loaded?vessel.BackupVessel():vessel.protoVessel;
            if(proto==null)throw new InvalidOperationException("Native vessel has no capturable snapshot");
            proto.Save(node);File.WriteAllText(path,node.ToString());
        }
    }
}
