using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RailgunSandbox
{
    public sealed class RailgunFlightPod : PartModule
    {
        [KSPField(isPersistant = true)] public string fleetId = "";
        [KSPField(isPersistant = true)] public int podNumber;
        [KSPField(isPersistant = true)] public string completedOrder = "";
        [KSPField(isPersistant = true)] public bool placementSettled;
        [KSPField(isPersistant = true)] public string couplerType = "size1";
        [KSPField(isPersistant = true)] public uint couplerPartId;
        [KSPField(isPersistant = true)] public string couplerHardwareKey = "";
        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (vessel == null || couplerPartId == 0 || !string.IsNullOrEmpty(couplerHardwareKey)) return;
            // A dock can reroot the combined vessel. Never select its first port,
            // which might be the receiver's connector after reload.
            var own = vessel.parts.FirstOrDefault(p => p.flightID == couplerPartId)?.FindModuleImplementing<ModuleDockingNode>();
            if (own != null) StockPods.SetCouplerType(own, couplerType);
        }
    }
    public sealed class RailgunCargoIsolation : PartModule
    {
        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            // Older development snapshots may predate the explicit cargo RESOURCE definitions.
            // Restore missing EMPTY capacity from the installed prefab; never refill an existing tank.
            if (part.partInfo.name == StockPods.FuelCargo) {
                foreach (var source in part.partInfo.partPrefab.Resources.Cast<PartResource>()) {
                    if ((source.resourceName != "LiquidFuel" && source.resourceName != "Oxidizer") ||
                        part.Resources.Cast<PartResource>().Any(r => r.resourceName == source.resourceName)) continue;
                    var node = new ConfigNode("RESOURCE"); node.AddValue("name", source.resourceName);
                    node.AddValue("amount", 0); node.AddValue("maxAmount", source.maxAmount); node.AddValue("flowState", false);
                    part.AddResource(node);
                    Debug.Log("[RailgunSandbox] Restored empty cargo capacity " + source.resourceName + " on part " + part.flightID);
                }
            }
            Isolate();
        }
        void FixedUpdate() { if (HighLogic.LoadedSceneIsFlight && part != null && part.vessel != null && part.vessel.loaded) Isolate(); }
        void Isolate() { foreach (PartResource r in part.Resources) { r.flowState = false; r.isTweakable = false; r.hideFlow = true; } }
    }
    public static class StockPods
    {
        public const string Probe = "krlReusableProbe";
        public const string FuelCargo = "krlReusableFuelCargo";
        public const string MonoCargo = "krlReusableMonoCargo";
        public static readonly string[] RequiredParts = { Probe, "dockingPort2", "advSasModule", "batteryBank", "RCSFuelTank", FuelCargo, MonoCargo,
            "largeAdapter2", "Rockomax64_BW", "liquidEngineMainsail_v2", "landingLeg1-2", "RCSBlock_v2", "rtg", "longAntenna", "airbrake1" };
        sealed class Spec
        {
            public string Name; public int Parent; public Vector3 Position; public Quaternion Rotation = Quaternion.identity;
            public readonly Dictionary<string, int> Nodes = new Dictionary<string, int>();
            public bool Surface;
        }
        // PartLoader.ParsePart converts underscores to periods. SanitizeInstanceName only strips clone suffixes.
        public static string Canonical(string name) => KSPUtil.SanitizeInstanceName(name).Replace('_', '.');
        public static string Missing()
        {
            var missing = RequiredParts.Where(n => PartLoader.getPartInfoByName(Canonical(n))?.partPrefab == null).ToList();
            foreach (var entry in new[] { Tuple.Create(MonoCargo, "MonoPropellant"), Tuple.Create(FuelCargo, "LiquidFuel"), Tuple.Create(FuelCargo, "Oxidizer") }) {
                var prefab = PartLoader.getPartInfoByName(Canonical(entry.Item1))?.partPrefab;
                if (prefab == null) continue;
                var tank = prefab.Resources.Cast<PartResource>().FirstOrDefault(r => r.resourceName == entry.Item2);
                if (tank == null || !FlightRules.Finite(tank.maxAmount) || tank.maxAmount <= 0)
                    missing.Add(entry.Item1 + ": missing " + entry.Item2 + " cargo capacity (check ModuleManager patches)");
            }
            return string.Join(", ", missing);
        }
        public static Part Prefab(string name) => PartLoader.getPartInfoByName(Canonical(name))?.partPrefab ?? throw new InvalidOperationException("Missing KSP part: " + name);
        static Vector3 Node(string name, string attach) => Prefab(name).attachNodes.First(n => n.id == attach).position;
        static List<Spec> Assembly()
        {
            var list = new List<Spec> { new Spec { Name = Probe, Parent = 0 } };
            Action<string, int, string, string> stack = (name, parent, parentNode, ownNode) => {
                int index = list.Count;
                list.Add(new Spec { Name = name, Parent = parent, Position = list[parent].Position + list[parent].Rotation * Node(list[parent].Name, parentNode) - Node(name, ownNode) });
                list[parent].Nodes[parentNode] = index; list[index].Nodes[ownNode] = parent;
            };
            stack("dockingPort2", 0, "top", "bottom");
            stack("advSasModule", 0, "bottom", "top");
            stack("batteryBank", 2, "bottom", "top");
            stack(MonoCargo, 3, "bottom", "top");
            stack(FuelCargo, 4, "bottom", "top");
            stack("RCSFuelTank", 5, "bottom", "top");
            // Native adapter has its SMALL node on the bottom: invert it below the 1.25 m head.
            stack("largeAdapter2", 6, "bottom", "bottom");
            list[7].Rotation = Quaternion.Euler(0, 0, 180);
            list[7].Position = list[6].Position + Node(list[6].Name, "bottom") - list[7].Rotation * Node(list[7].Name, "bottom");
            stack("Rockomax64_BW", 7, "top", "top");
            stack("Rockomax64_BW", 8, "bottom", "top");
            stack("liquidEngineMainsail_v2", 9, "bottom", "top");
            for (int i = 0; i < 4; i++) {
                float angle = 90 * i;
                var radial = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
                // The installed stock Kerbal X craft cants LT-2s -35 degrees about X.
                // Preserve that mount orientation and place the actual surface node on the 2.5 m shell.
                var legRotation = Quaternion.AngleAxis(angle, Vector3.up) * Quaternion.Euler(-35, 0, 0);
                var attachOffset = legRotation * Prefab("landingLeg1-2").srfAttachNode.position;
                float shellRadius = Prefab("Rockomax64_BW").srfAttachNode.position.magnitude;
                list.Add(new Spec { Name = "landingLeg1-2", Parent = 10, Surface = true,
                    Position = list[9].Position + Node(list[9].Name, "bottom") + new Vector3(0, -1.2f, 0) + radial * shellRadius - Vector3.Project(attachOffset, radial),
                    Rotation = legRotation });
                foreach (int parent in new[] { 6, 9 }) list.Add(new Spec { Name = "RCSBlock_v2", Parent = parent, Surface = true,
                    Position = list[parent].Position + radial * (parent == 9 ? 1.3f : .65f), Rotation = Quaternion.AngleAxis(angle, Vector3.up) });
            }
            for (int i = 0; i < 4; i++) {
                float angle = 90 * i;
                var radial = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
                // The stock attach normal points into the supporting tank. Local -Z
                // faces outward, so the native Flap opens away from the cylinder.
                var rotation = Quaternion.AngleAxis(angle + 180, Vector3.up);
                float radius = Prefab("Rockomax64_BW").srfAttachNode.position.magnitude;
                list.Add(new Spec { Name = "airbrake1", Parent = 8, Surface = true, Rotation = rotation,
                    Position = list[8].Position + Vector3.up * 1.5f + radial * radius - rotation * Prefab("airbrake1").srfAttachNode.position });
            }
            list.Add(new Spec { Name = "rtg", Parent = 6, Surface = true, Position = list[6].Position + Vector3.right * .7f, Rotation = Quaternion.Euler(0, 0, 90) });
            list.Add(new Spec { Name = "longAntenna", Parent = 0, Surface = true, Position = new Vector3(.5f, 0, 0), Rotation = Quaternion.Euler(0, 0, -90) });
            return list;
        }
        static ConfigNode PartNode(Spec spec, int index, PodRecord record)
        {
            var node = ProtoVessel.CreatePartNode(Canonical(spec.Name), ShipConstruction.GetUniqueFlightID(HighLogic.CurrentGame.flightState));
            foreach (PartModule module in Prefab(spec.Name).Modules) { var saved = new ConfigNode("MODULE"); module.Save(saved); node.AddNode(saved); }
            node.SetValue("parent", spec.Parent, true); node.SetValue("position", KSPUtil.WriteVector(spec.Position), true);
            node.SetValue("rotation", KSPUtil.WriteQuaternion(spec.Rotation), true);
            node.SetValue("srfN", spec.Surface ? "srfAttach, " + spec.Parent : "None, -1", true);
            node.SetValue("attm", spec.Surface ? 1 : 0, true);
            if (spec.Name == "landingLeg1-2") node.SetValue("autostrutMode", "ForceHeaviest", true);
            if (spec.Name == "airbrake1") {
                var brake = node.GetNodes("MODULE").Single(n => n.GetValue("name") == "ModuleAeroSurface");
                brake.SetValue("deploy", false, true); brake.SetValue("ignoreRoll", true, true);
            }
            node.RemoveValues("attN");
            foreach (var attach in Prefab(spec.Name).attachNodes)
                node.AddValue("attN", attach.id + ", " + (spec.Nodes.TryGetValue(attach.id, out int other) ? other : -1));
            if (index == 0) {
                var marker = node.GetNodes("MODULE").First(n => n.GetValue("name") == "RailgunFlightPod");
                marker.SetValue("fleetId", record.VesselId, true); marker.SetValue("podNumber", record.Number, true);
            }
            if (spec.Name == MonoCargo || spec.Name == FuelCargo)
                foreach (var resource in node.GetNodes("RESOURCE")) {
                    resource.SetValue("amount", 0, true); resource.SetValue("flowState", false, true);
                    resource.SetValue("isTweakable", false, true); resource.SetValue("hideFlow", true, true);
                }
            return node;
        }
        public static Vessel Spawn(PodRecord record, CelestialBody body, double latitude, double longitude, double terrainHeight, bool launchShell = false)
        {
            if (record.Created) throw new InvalidOperationException("A previously created pod cannot be replaced.");
            string missing = Missing(); if (missing.Length > 0) throw new InvalidOperationException("Missing stock-part assembly: " + missing);
            var specs = Assembly();
            int originalCount = specs.Count;
            if (launchShell) {
                var shell = new Spec { Name = LaunchShell.PartName, Parent = 10 };
                shell.Position = specs[10].Position + Node(specs[10].Name, "bottom") - Node(shell.Name, "top");
                shell.Nodes["top"] = 10; specs[10].Nodes["bottom"] = specs.Count;
                specs.Add(shell);
            }
            var parts = specs.Select((s, i) => PartNode(s, i, record)).ToArray();
            record.PartIds = parts.Take(originalCount).Select(n => uint.Parse(n.GetValue("uid"))).ToList();
            if (launchShell) {
                record.LaunchShellRequired = true;
                record.LaunchShellPartId = uint.Parse(parts.Last().GetValue("uid"));
                double top = specs.Take(originalCount).Max(s => s.Position.y + 2);
                LaunchShell.Profile(parts.Last(), top - specs.Last().Position.y);
            }
            record.OwnPort = record.PartIds[1];
            double height = -specs.Min(s => s.Position.y) + 2;
            var world = body.GetWorldSurfacePosition(latitude, longitude, terrainHeight + height);
            var orbit = new Orbit();
            orbit.UpdateFromStateVectors((world - body.position).xzy, body.getRFrmVel(world).xzy, body, Planetarium.GetUniversalTime());
            var node = ProtoVessel.CreateVesselNode("KRL Reusable Pod " + record.Number, VesselType.Probe, orbit, 0, parts);
            node.SetValue("pid", record.VesselId, true); node.SetValue("sit", "LANDED", true); node.SetValue("landed", true, true);
            // Native landed positioning applies hgt separately from surface altitude.
            node.SetValue("lat", latitude, true); node.SetValue("lon", longitude, true); node.SetValue("alt", terrainHeight, true);
            node.SetValue("hgt", height, true); node.SetValue("landedAt", "KRL Reusable Depot", true);
            var normal = body.GetRelSurfaceNVector(latitude, longitude);
            var rotation = Quaternion.LookRotation((Vector3)normal) * Quaternion.FromToRotation(Vector3.up, Vector3.forward);
            node.SetValue("rot", KSPUtil.WriteQuaternion(rotation), true); node.SetValue("nrm", KSPUtil.WriteVector(Vector3.up), true);
            var proto = HighLogic.CurrentGame.AddVessel(node);
            var vessel = proto.vesselRef ?? throw new InvalidOperationException("KSP did not create the stock-parts pod.");
            vessel.isPersistent = true; record.Created = true; record.OriginalParts = originalCount;
            Debug.Log("[RailgunSandbox] Created unique stock assembly " + record.Number + " / " + record.VesselId + " / parts " + specs.Count);
            return vessel;
        }
        public static RailgunFlightPod Marker(PodRecord record) => FlightGlobals.Vessels.Where(v => v != null && v.loaded)
            .SelectMany(v => v.parts).Select(p => p.FindModuleImplementing<RailgunFlightPod>()).FirstOrDefault(m => m?.fleetId == record.VesselId);
        public static void SetCouplerType(ModuleDockingNode own, string type)
        {
            // This single interface was admitted from the exact receiving node
            // when reserving. Runtime scaling can change it beyond the prefab;
            // it must also be restorable while that receiver is unloaded.
            DockingPortPolicy.ValidateToken(type);
            own.nodeType = type;
            own.nodeTypes = new HashSet<string>(new[] { type });
        }
        public static void ConfigureCoupler(PodRecord record, Vessel vessel)
        {
            var own = vessel.parts.FirstOrDefault(p => p.flightID == record.OwnPort)?.FindModuleImplementing<ModuleDockingNode>();
            var marker = Marker(record);
            if (own == null || marker == null) throw new InvalidOperationException("Load the pod's own docking coupler before configuring it.");
            string current = own.fsm?.CurrentState?.name ?? own.state ?? "";
            if (current != "Ready") throw new InvalidOperationException("The pod coupler must be free before it can be configured.");
            SetCouplerType(own, record.DockingType); marker.couplerType = record.DockingType; marker.couplerPartId = record.OwnPort;
        }
        public static Vessel Vessel(PodRecord record)
        {
            var marker = Marker(record); if (marker != null) return marker.vessel;
            return FlightGlobals.Vessels.FirstOrDefault(v => v != null && (v.id.ToString("N") == record.VesselId ||
                v.protoVessel?.protoPartSnapshots.Any(p => p.modules.Any(m => m.moduleName == "RailgunFlightPod" && m.moduleValues.GetValue("fleetId") == record.VesselId)) == true));
        }
        public static IEnumerable<Part> CargoParts(Vessel v) => v.parts.Where(p => p.partInfo.name == MonoCargo || p.partInfo.name == FuelCargo);
        public static bool OriginalPartsIntact(PodRecord record, Vessel vessel)
        {
            if (vessel == null || record.OriginalParts <= 0 || record.PartIds.Count != record.OriginalParts) return false;
            var actual = vessel.loaded ? vessel.parts.Select(part => part.flightID).ToArray() :
                vessel.protoVessel?.protoPartSnapshots.Select(part => part.flightID).ToArray();
            return actual != null && record.PartIds.All(actual.Contains);
        }
        public static bool PlacementSettled(PodRecord p)
        {
            var marker = Marker(p); if (marker != null) return marker.placementSettled;
            var vessel = Vessel(p);
            var saved = vessel?.protoVessel?.protoPartSnapshots.SelectMany(part => part.modules)
                .FirstOrDefault(m => m.moduleName == "RailgunFlightPod" && m.moduleValues.GetValue("fleetId") == p.VesselId);
            return saved != null && bool.TryParse(saved.moduleValues.GetValue("placementSettled"), out bool settled) && settled;
        }
        public static PartResource CargoTank(Vessel v, string resource) => CargoParts(v).SelectMany(p => p.Resources.Cast<PartResource>()).FirstOrDefault(r => r.resourceName == resource);
        public static double Capacity(string resource) => new[] { MonoCargo, FuelCargo }.Select(Prefab)
            .SelectMany(p => p.Resources.Cast<PartResource>()).Where(r => r.resourceName == resource).Sum(r => r.maxAmount);
    }
}
