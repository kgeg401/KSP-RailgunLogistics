using System;
using System.Collections.Generic;
using System.Linq;

namespace RailgunLogisticsServer
{
    public static class RefitSupplyPolicy
    {
        public sealed class Tank
        {
            public uint Part;
            public string PartName, Resource;
            public double Amount, Capacity;
            public bool Flow;
        }
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public static bool Ready(IEnumerable<Tank> source, IEnumerable<uint> originals, uint cargoPart)
        {
            if (source == null || originals == null || cargoPart == 0) return false;
            var ids = originals.ToArray(); var tanks = source.ToArray();
            if (ids.Length == 0 || ids.Contains(0u) || ids.Distinct().Count() != ids.Length || !ids.Contains(cargoPart) ||
                tanks.Length == 0 || tanks.Any(t => t == null || !ids.Contains(t.Part) || string.IsNullOrEmpty(t.Resource) ||
                    !Finite(t.Amount) || !Finite(t.Capacity) || t.Capacity < 0 || t.Amount < 0 || t.Amount > t.Capacity + 1e-6) ||
                tanks.GroupBy(t => new { t.Part, t.Resource }).Any(g => g.Count() != 1)) return false;
            Func<Tank, bool> cargo = t => t.PartName == "krlReusableMonoCargo" || t.PartName == "krlReusableFuelCargo";
            if (!tanks.Any(t => t.Part == cargoPart && cargo(t)) || tanks.Where(cargo).Any(t => t.Amount > 1e-6 || t.Flow)) return false;
            foreach (string name in new[] { "LiquidFuel", "Oxidizer", "MonoPropellant" }) {
                var supplies = tanks.Where(t => !cargo(t) && t.Resource == name).ToArray();
                if (supplies.Length == 0 || supplies.Any(t => !t.Flow || t.Capacity <= 0 || Math.Abs(t.Amount - t.Capacity) > 1e-6)) return false;
            }
            // Fuel refill is durable before Ready. Electrical generation remains
            // live; require usable charge rather than exact frame-level fullness.
            return tanks.Any(t => !cargo(t) && t.Resource == "ElectricCharge" && t.Flow && t.Amount > 0);
        }
    }
}
