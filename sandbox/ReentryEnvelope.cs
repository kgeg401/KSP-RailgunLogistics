using System;

namespace RailgunSandbox
{
    // Initial Kerbin entry envelope, to be tuned against the native thermal/fuel traces.
    // Metres ASL and metres/second. This only tightens the native landing speed policy.
    public static class ReentryEnvelope
    {
        static readonly double[] Altitudes = { 0, 10000, 20000, 30000, 40000, 50000, 60000, 70000, 80000 };
        // Retracted LT-2 entry measured only .43 of the native heat limit in attempt05.
        // The former thin-air braking corridor exhausted propulsion before touchdown.
        // Retain a cap below the observed 2100 m/s / 23 km failure, but use denser air
        // to dissipate energy before the terrain-aware terminal stopping envelope.
        static readonly double[] Speeds = { 800, 1100, 1750, 2000, 2250, 2400, 2700, 3000, 3200 };
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
