namespace RailgunSandbox
{
    public static class RecoveryRcsPolicy
    {
        // Keep the real maneuvering reserve for the powered terminal turn.
        // This selects stock attitude thrusters; it adds no fuel or torque.
        public static bool Enable(bool terminalSteering, bool alreadyEnabled, double clearance, double attitudeError, double usableMono)
        {
            if(!terminalSteering || !FlightRules.Finite(clearance) || !FlightRules.Finite(attitudeError) || !FlightRules.Finite(usableMono) ||
                clearance<0 || attitudeError<0 || usableMono<=.1 || clearance>5000)return false;
            return clearance<300 || attitudeError>(alreadyEnabled?3:8);
        }
    }
}
