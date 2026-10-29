using System;

namespace RailgunSandbox
{
    // Rail drive ends at the physical muzzle. Orbital correction is a separate
    // native maneuver; there is no in-flight launch force or velocity assignment.
    public static class RailLaunch
    {
        // Longer, lower-acceleration guide avoids the failed 40 km candidate's
        // exposed-part hypersonic heating while still releasing below orbit.
        // Experimental geometry: native thermal/flight acceptance is required.
        public const double Length = 100000;
        public const double ElevationDegrees = 30;
        public const double MuzzleSpeed = 2200;
        public const double MaximumCircularizationDeltaV = 800;

        // Native Junior fitting settled with 0.99 m lateral error and 0.33 m/s
        // surface speed. It was safely inside the 3 m drive envelope, but the
        // former 0.7 m spherical gate left the crane running indefinitely.
        // Keep tighter capture margins than the running guide and retain the
        // velocity/attitude interlocks; the drive still centres with real forces.
        public static bool Captured(double along, double lateral, double speed, double angle) =>
            OnGuide(along, lateral) && Math.Abs(along) <= .7 && lateral <= 1.25 &&
            FlightRules.Finite(speed) && speed >= 0 && speed < 1 &&
            FlightRules.Finite(angle) && angle >= 0 && angle < 1;

        public static bool OnGuide(double along, double lateral) =>
            FlightRules.Finite(along) && FlightRules.Finite(lateral) && along >= -2 && along < Length && lateral >= 0 && lateral <= 3;

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
