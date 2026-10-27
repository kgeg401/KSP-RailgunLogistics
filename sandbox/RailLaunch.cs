using System;

namespace RailgunSandbox
{
    // Rail drive ends at the physical muzzle. Orbital correction is a separate
    // native maneuver; there is no in-flight launch force or velocity assignment.
    public static class RailLaunch
    {
        public const double Length = 40000;
        public const double ElevationDegrees = 35;
        public const double MuzzleSpeed = 2400;
        public const double MaximumCircularizationDeltaV = 800;

        public static bool OnGuide(double along, double lateral) =>
            FlightRules.Finite(along) && FlightRules.Finite(lateral) && along >= -2 && along < Length && lateral <= 3;

        public static double DriveAcceleration(double speed, double dt, double gravityAlong)
        {
            if (!FlightRules.Finite(speed) || !FlightRules.Finite(dt) || dt <= 0 || !FlightRules.Finite(gravityAlong))
                throw new InvalidOperationException("Invalid native rail physics sample.");
            return Math.Max(0, Math.Min(MuzzleSpeed * MuzzleSpeed / (2 * Length) + gravityAlong,
                (MuzzleSpeed - speed) / dt));
        }

        public static bool BurnWindow(double altitude, double atmosphere, double timeToAp, double halfBurnTime) =>
            FlightRules.Finite(altitude) && FlightRules.Finite(atmosphere) && FlightRules.Finite(timeToAp) &&
            FlightRules.Finite(halfBurnTime) && halfBurnTime >= 0 && altitude > atmosphere + 1000 &&
            timeToAp >= -10 && timeToAp <= Math.Max(12, halfBurnTime + 5);

        public static bool EnginesLocked(PodRecord p) => p.Phase == FlightPhase.Reserved || p.Phase == FlightPhase.RailLaunch ||
            p.Phase == FlightPhase.Ascent && p.RailOnlyAscent && !p.CircularizationStarted;
    }
}
