using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Server.System.Vessel.Classes;

namespace RailgunLogisticsServer
{
    public static class RefitSupplyEvidence
    {
        public static bool SavedReady(string text, IEnumerable<uint> originals, uint cargoPart, string fleetId, string completedOrder)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            try { return CurrentReady(new Vessel(text), originals, cargoPart, fleetId, completedOrder); }
            catch (Exception) { return false; } // Malformed/truncated native snapshots cannot acknowledge readiness.
        }
        public static bool CurrentReady(Vessel vessel, IEnumerable<uint> originals, uint cargoPart, string fleetId, string completedOrder)
        {
            if (vessel == null || originals == null || string.IsNullOrEmpty(fleetId) || string.IsNullOrEmpty(completedOrder)) return false;
            var ids = originals.ToArray(); var parts = vessel.Parts.GetAllValues().ToArray();
            var seen = new HashSet<uint>(); var tanks = new List<RefitSupplyPolicy.Tank>(); int receipts = 0;
            foreach (var part in parts) {
                if (!uint.TryParse(part.Fields.GetSingle("uid")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out uint uid) || !seen.Add(uid)) return false;
                if (!ids.Contains(uid)) continue;
                foreach (var marker in part.Modules.GetAll().Where(m => m.Key == "RailgunFlightPod")) {
                    if (marker.Value.GetValue("fleetId")?.Value != fleetId || marker.Value.GetValue("completedOrder")?.Value != completedOrder) return false;
                    receipts++;
                }
                foreach (var resource in part.Resources.GetAll()) {
                    var node = resource.Value;
                    if (!double.TryParse(node.GetValue("amount")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double amount) ||
                        !double.TryParse(node.GetValue("maxAmount")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double capacity) ||
                        !bool.TryParse(node.GetValue("flowState")?.Value, out bool flow)) return false;
                    tanks.Add(new RefitSupplyPolicy.Tank { Part = uid, PartName = part.Fields.GetSingle("name")?.Value,
                        Resource = resource.Key, Amount = amount, Capacity = capacity, Flow = flow });
                }
            }
            return receipts == 1 && ids.All(seen.Contains) && RefitSupplyPolicy.Ready(tanks, ids, cargoPart);
        }
    }
}
