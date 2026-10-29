using System;

namespace RailgunSandbox
{
    // Separation is altitude AND guide clearance, never elapsed flight time.
    public static class LaunchShellPolicy
    {
        public const double SeparationAltitude = 50000;
        public const double BaseClearanceSeconds = 3;
        public static bool MayOpen(FlightPhase phase, double altitude, double verticalSpeed, double alongRail) =>
            phase == FlightPhase.Ascent && FlightRules.Finite(altitude) && altitude >= SeparationAltitude &&
            FlightRules.Finite(verticalSpeed) && verticalSpeed > 0 && FlightRules.Finite(alongRail) && alongRail >= RailLaunch.Length + 30;
        public static bool MayBurn(bool required, bool shellAttached, double clearAt, double now) => !required ||
            !shellAttached && FlightRules.Finite(clearAt) && clearAt >= 0 && FlightRules.Finite(now) && now >= clearAt + BaseClearanceSeconds;
    }
}
