using System;

namespace RailgunSandbox
{
    public static class RendezvousPolicy
    {
        public const double MaximumCorrection = 500;
        // Native two-owner return replays exhausted a ~3km/s starting budget;
        // the earlier1.5km/s floor did not protect the actual recovery envelope.
        // This provisional Kerbin floor prevents spending it on another phasing
        // correction. It is not evidence of a successful landing or other routes.
        public const double ReturnReserve = 3000;
        public static string RecoveryAttemptKey(string vessel,string order)
        {
            // Returning the original pod permits a NEW authenticated order;
            // retrying one delivery must not replenish its recovery budget.
            return Guid.TryParseExact(vessel,"N",out var nativeVessel)&&Guid.TryParseExact(order,"N",out var nativeOrder)?
                nativeVessel.ToString("N")+"/"+nativeOrder.ToString("N"):null;
        }
        public static double AvailableDeltaV(double wetMass, double liquid, double oxidizer, double liquidRatio,
            double oxidizerRatio, double liquidDensity, double oxidizerDensity, double isp, double gravity)
        {
            double[] values = { wetMass, liquid, oxidizer, liquidRatio, oxidizerRatio, liquidDensity, oxidizerDensity, isp, gravity };
            foreach (double value in values) if (!FlightRules.Finite(value) || value <= 0) return double.NaN;
            double mixture = Math.Min(liquid / liquidRatio, oxidizer / oxidizerRatio);
            double propellantMass = mixture * (liquidRatio * liquidDensity + oxidizerRatio * oxidizerDensity);
            if (!FlightRules.Finite(propellantMass) || propellantMass <= 0 || propellantMass >= wetMass) return double.NaN;
            return isp * gravity * Math.Log(wetMass / (wetMass - propellantMass));
        }
        public static string Reject(double planned, double remaining, double available, double periapsis, double eccentricity, double atmosphere)
        {
            if (!FlightRules.Finite(planned) || !FlightRules.Finite(remaining) || planned < 0 || remaining < 0 ||
                !FlightRules.Finite(available) || available < 0) return "Rendezvous held: invalid native maneuver or propulsion budget";
            double required = Math.Max(planned, remaining);
            if (required > MaximumCorrection) return "Rendezvous held: planned correction exceeds the 500 m/s operating limit";
            if (available - required < ReturnReserve) return "Rendezvous held: maneuver would spend the home-return fuel reserve";
            if (!FlightRules.SafeOrbit(periapsis, eccentricity, atmosphere)) return "Rendezvous held: planned orbit intersects the recovery safety boundary";
            return null;
        }
    }
}
