using System;

namespace RailgunSandbox
{
    // Initial Kerbin entry envelope, to be tuned against the native thermal/fuel traces.
    // Metres ASL and metres/second. This only tightens the native landing speed policy.
    public static class ReentryEnvelope
    {
        static readonly double[] Altitudes = { 0, 10000, 20000, 30000, 40000, 50000, 60000, 70000, 80000 };
        // Full-profile two-owner attempt07 reached ~2080 m/s at36km with the
        // retracted LT-2 skins already at .73 of their thermal limit. Two legs
        // then overheated before20km, despite normal retrograde control and
        // >2000 liquid fuel remaining. Brake before that observed corridor.
        // The first conservative revision survived entry at .389 peak heat,
        // but left insufficient terminal fuel. Retain earlier braking than07
        // while testing a modest relaxation against warm native replays.
        // Provisional until repeated native entry AND fuel-to-touchdown checks;
        // never substitute a cold fixture or add propellant to pass this policy.
        static readonly double[] Speeds = { 500, 800, 1200, 1550, 1850, 2100, 2400, 2800, 3200 };
        public static double SpeedCeiling(double altitude)
        {
            if (!FlightRules.Finite(altitude)) throw new InvalidOperationException("Invalid entry altitude.");
            if (altitude > Altitudes[Altitudes.Length - 1]) return double.MaxValue;
            if (altitude <= 0) return Speeds[0];
            for (int i = 1; i < Altitudes.Length; i++)
                if (altitude <= Altitudes[i]) {
                    double fraction = (altitude - Altitudes[i - 1]) / (Altitudes[i] - Altitudes[i - 1]);
                    return Speeds[i - 1] + fraction * (Speeds[i] - Speeds[i - 1]);
                }
            throw new InvalidOperationException("Entry envelope range error.");
        }
        public static double Limit(double nativeLimit, double altitude) => Math.Min(nativeLimit, SpeedCeiling(altitude));
        public static double TerminalSpeedCeiling(double clearance, double fullThrustAcceleration, double gravity)
        {
            if (!FlightRules.Finite(clearance) || !FlightRules.Finite(fullThrustAcceleration) || !FlightRules.Finite(gravity) || gravity < 0 || fullThrustAcceleration < 0)
                throw new InvalidOperationException("Invalid terminal braking state.");
            // Reserve acceleration and altitude for attitude/throttle response. Units are m, m/s2, m/s.
            double acceleration = Math.Max(0, .85 * fullThrustAcceleration - gravity);
            return Math.Max(.4, .8 * Math.Sqrt(2 * acceleration * Math.Max(0, clearance - 15)));
        }
    }
}
