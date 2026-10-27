using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace RailgunSandbox
{
    // Refit only the connector leaf in an unloaded, parked native vessel.
    // All other native snapshots, resources, part IDs and vessel identity remain untouched.
    public static class DockingHardware
    {
        public static string Capture(ModuleDockingNode target)
        {
            DockingPortPolicy.ValidateHardware(target.part.partInfo.name);
            if (target.gendered || target.snapRotation || target.part.FindModulesImplementing<ModuleDockingNode>().Count != 1)
                throw new InvalidOperationException("This docking hardware is not supported for automatic fitting.");
            var source = target.part;
            var bottom = source.attachNodes.SingleOrDefault(n => n.id == "bottom") ??
                throw new InvalidOperationException("The selected connector has no supported bottom mounting node.");
            var node = ProtoVessel.CreatePartNode(source.partInfo.name, 0);
            node.SetValue("persistentId", 0, true); // Template identity is never a new physical part identity.
            foreach (PartModule module in StockPods.Prefab(source.partInfo.name).Modules) {
                if (module == null) continue;
                var saved = new ConfigNode("MODULE"); module.Save(saved);
                // Same actual part, same native scaler and variant. Never copy the
                // receiver's docking FSM, partner IDs, resources or flight guidance.
                if (module.moduleName == "TweakScale" || module.moduleName == "ModulePartVariants") {
                    var live = source.Modules.Cast<PartModule>().FirstOrDefault(m => m != null && m.moduleName == module.moduleName);
                    if (live != null) { saved = new ConfigNode("MODULE"); live.Save(saved); }
                }
                node.AddNode(saved);
            }
            node.SetValue("mass", source.mass.ToString("R", CultureInfo.InvariantCulture), true);
            node.AddValue("mountBottom", KSPUtil.WriteVector(bottom.position));
            node.AddValue("modelScale", KSPUtil.WriteVector(source.transform.GetChild(0).localScale));
            return node.ToString();
        }

        static ConfigNode Template(PodRecord p)
        {
            var parsed = ConfigNode.Parse(p.DockingHardwareConfig);
            return parsed?.GetNode("PART") ?? (parsed?.name == "PART" ? parsed : null) ??
                throw new InvalidOperationException("The saved connector fitting is invalid; select the receiving port again.");
        }
        public static string Key(string config)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(config))).Replace("-", "");
        }
        public static bool NeedsRefit(PodRecord p, Vessel v)
        {
            if (string.IsNullOrEmpty(p.DockingHardwareConfig)) return false; // Existing in-flight orders keep their hardware.
            string fitted = v.loaded ? StockPods.Marker(p)?.couplerHardwareKey :
                v.protoVessel?.protoPartSnapshots.SelectMany(part => part.modules)
                    .FirstOrDefault(m => m.moduleName == "RailgunFlightPod")?.moduleValues.GetValue("couplerHardwareKey");
            return fitted != Key(p.DockingHardwareConfig);
        }
        public static void Refit(PodRecord p, Vessel v)
        {
            if (p.Phase != FlightPhase.Reserved || v.loaded || !v.Landed || !StockPods.OriginalPartsIntact(p, v))
                throw new InvalidOperationException("Connector fitting requires the intact reserved pod parked and unloaded at the depot.");
            var proto = v.protoVessel;
            int index = proto.protoPartSnapshots.FindIndex(part => part.flightID == p.OwnPort);
            if (index < 0 || index == proto.rootIndex || proto.protoPartSnapshots.Any(part => part.parentIdx == index))
                throw new InvalidOperationException("Only the pod's free connector leaf can be refitted.");
            var old = proto.protoPartSnapshots[index];
            var docking = old.modules.Single(m => m.moduleName == "ModuleDockingNode");
            if (docking.moduleValues.GetValue("state") != "Ready") throw new InvalidOperationException("Cannot refit an occupied connector.");
            var template = Template(p); DockingPortPolicy.ValidateHardware(template.GetValue("name"));
            var oldNode = new ConfigNode("PART"); old.Save(oldNode);
            foreach (string field in new[] { "uid", "cid", "mid", "persistentId", "launchID", "parent", "rotation", "mirror", "istg", "dstg", "temp", "skinTemp" })
                if (oldNode.HasValue(field)) template.SetValue(field, oldNode.GetValue(field), true);
            var parent = proto.protoPartSnapshots[old.parentIdx];
            var parentPrefab = StockPods.Prefab(parent.partName);
            var mount = parent.attachNodes.Single(n => n.partIdx == index);
            Vector3 parentMount = parentPrefab.attachNodes.Single(n => n.id == mount.id).position;
            var offset = KSPUtil.ParseVector3(template.GetValue("mountBottom"));
            template.RemoveValue("mountBottom"); template.RemoveValue("modelScale");
            template.SetValue("position", KSPUtil.WriteVector((Vector3)parent.position + parent.rotation * parentMount - old.rotation * offset), true);
            template.RemoveValues("attN"); template.SetValue("srfN", "None, -1", true);
            foreach (var node in StockPods.Prefab(template.GetValue("name")).attachNodes)
                template.AddValue("attN", node.id + ", " + (node.id == "bottom" ? old.parentIdx : -1));
            var replacement = new ProtoPartSnapshot(template, proto, HighLogic.CurrentGame);
            if (replacement.flightID != old.flightID || replacement.parentIdx != old.parentIdx)
                throw new InvalidOperationException("Connector refit would change the pod's identity or parent.");
            // Build and validate before committing. No vessel destruction or new pod is involved.
            var marker = proto.protoPartSnapshots.SelectMany(part => part.modules).Single(m => m.moduleName == "RailgunFlightPod");
            proto.protoPartSnapshots[index] = replacement;
            marker.moduleValues.SetValue("couplerHardwareKey", Key(p.DockingHardwareConfig), true);
            marker.moduleValues.SetValue("couplerPartId", p.OwnPort, true);
            marker.moduleValues.SetValue("couplerType", p.DockingType, true);
            Debug.Log("[RailgunSandbox] Physically refitted connector " + old.partName + " -> " + replacement.partName + " on existing pod " + p.Number + "; part ID " + p.OwnPort);
        }
        public static void ValidateLoaded(PodRecord p, Vessel v)
        {
            if (string.IsNullOrEmpty(p.DockingHardwareConfig)) { StockPods.ConfigureCoupler(p, v); return; }
            var template = Template(p);
            var part = v.parts.Single(n => n.flightID == p.OwnPort);
            var own = part.FindModuleImplementing<ModuleDockingNode>();
            if (NeedsRefit(p, v) || part.partInfo.name != template.GetValue("name") || own?.nodeTypes?.Contains(p.DockingType) != true)
                throw new InvalidOperationException("Fitted connector does not match the selected target; launch held.");
            if ((own.fsm?.CurrentState?.name ?? own.state) != "Ready")
                throw new InvalidOperationException("The fitted connector is occupied or unavailable; launch held.");
            var expected = KSPUtil.ParseVector3(template.GetValue("mountBottom"));
            if ((part.attachNodes.Single(n => n.id == "bottom").position - expected).magnitude > .002f)
                throw new InvalidOperationException("Fitted connector scale does not match the selected target; launch held.");
            var modelScale = KSPUtil.ParseVector3(template.GetValue("modelScale"));
            if ((part.transform.GetChild(0).localScale - modelScale).magnitude > .002f)
                throw new InvalidOperationException("Fitted connector model scale does not match the selected target; launch held.");
        }
    }
}
