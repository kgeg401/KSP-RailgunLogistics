using System;

namespace RailgunSandbox
{
    public struct PadCommand
    {
        public bool Active, SlowDescent;
        public double AccelerationX, AccelerationZ, MaximumDescentSpeed;
    }

    public static class TerminalPadGuidance
    {
        // Inputs are horizontal metres/metres per second in one local tangent frame.
        // Output is a bounded engine-attitude request, never an applied force or pose.
        public static PadCommand Compute(double errorX, double errorZ, double velocityX, double velocityZ,
            double clearance, double verticalSpeed, double gravity, double thrustAcceleration)
        {
            foreach (double value in new[] { errorX, errorZ, velocityX, velocityZ, clearance, verticalSpeed, gravity, thrustAcceleration })
                if (!FlightRules.Finite(value)) return default;
            double distance = Math.Sqrt(errorX * errorX + errorZ * errorZ);
            double speed = Math.Sqrt(velocityX * velocityX + velocityZ * velocityZ);
            if (clearance < 2 || clearance > 5000 || verticalSpeed > 20 ||
                distance > 20000 || speed > 250 || gravity <= 0 || thrustAcceleration < gravity * 1.1) return default;
            double brakingAcceleration = .85 * thrustAcceleration - gravity;
            if (verticalSpeed < -25 && (brakingAcceleration <= 0 || verticalSpeed * verticalSpeed / (2 * brakingAcceleration) > clearance * .65)) return default;
            double horizon = Math.Max(4, Math.Min(20, clearance / Math.Max(2, -verticalSpeed)));
            if (clearance < 1200) horizon = 6;
            double tiltDegrees = clearance < 10 ? 4 : clearance < 30 ? 8 : clearance < 300 ? 15 : 25;
            double limit = Math.Min(gravity * Math.Tan(tiltDegrees * Math.PI / 180), .3 * (thrustAcceleration - gravity));
            double desiredX = errorX / horizon, desiredZ = errorZ / horizon;
            double desiredMagnitude = Math.Sqrt(desiredX * desiredX + desiredZ * desiredZ);
            // Govern approach speed by real lateral stopping distance, rather
            // than crawling at 3-10 m/s while still kilometres from the pad.
            double speedLimit = clearance < 10 ? .5 : clearance < 30 ? 1 : Math.Max(.5, Math.Min(60, .65 * Math.Sqrt(2 * limit * Math.Max(0, distance - 3))));
            if (desiredMagnitude > speedLimit) { desiredX *= speedLimit / desiredMagnitude; desiredZ *= speedLimit / desiredMagnitude; }
            double ax = (desiredX - velocityX) / 2.5, az = (desiredZ - velocityZ) / 2.5;
            double acceleration = Math.Sqrt(ax * ax + az * az);
            if (acceleration > limit) { ax *= limit / acceleration; az *= limit / acceleration; }
            return new PadCommand {
                Active = true, AccelerationX = ax, AccelerationZ = az,
                SlowDescent = distance > 5 || speed > .5,
                MaximumDescentSpeed = clearance > 300 ? Math.Max(3, Math.Min(40, clearance / (distance / speedLimit + speed / Math.Max(.1, limit) + 15))) : clearance > 30 ? 2 : .6
            };
        }
    }
}
