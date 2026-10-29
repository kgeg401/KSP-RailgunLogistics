using System;

namespace RailgunSandbox
{
    public enum ShellTransferStage { Gripping, Lifting, Traversing, Tilting, Seating, Docked }

    // Targets for the native crane controller. Each waypoint requires actual
    // position, speed and (when tilting) attitude evidence; time cannot move a pod.
    public sealed class ShellLoadingRoute
    {
        string order = "";
        double previous = double.NaN, dwell, startX, startY, startZ, lift;
        public ShellTransferStage Stage { get; private set; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public double Z { get; private set; }
        public double Grip { get; private set; }
        public bool AlignToRail => Stage >= ShellTransferStage.Tilting;
        public bool Seated => Stage == ShellTransferStage.Docked;
        public string Description => Stage == ShellTransferStage.Gripping ? "Transport carrier gripping shell bands" :
            Stage == ShellTransferStage.Lifting ? "Hoisting shell above the launch cradle" :
            Stage == ShellTransferStage.Traversing ? "Bridge trolley moving shell over the rail" :
            Stage == ShellTransferStage.Tilting ? "Carrier trunnions tilting shell to rail angle" :
            Stage == ShellTransferStage.Seating ? "Lowering shell into the magnetic cradle" : "Shell seated; checking rail locks";

        public void Update(string orderId, double now, double x, double y, double z, double cradleHeight, double speed, double angle, bool carrierPositioned = true)
        {
            if (string.IsNullOrEmpty(orderId) || !FlightRules.Finite(now) || !FlightRules.Finite(x) || !FlightRules.Finite(y) ||
                !FlightRules.Finite(z) || !FlightRules.Finite(cradleHeight) || cradleHeight <= 0 || !FlightRules.Finite(speed) || speed < 0 || !FlightRules.Finite(angle))
                throw new InvalidOperationException("Shell transfer requires valid native position, velocity and attitude measurements.");
            double step = now - previous;
            if (order != orderId) {
                order = orderId; Stage = ShellTransferStage.Gripping; dwell = 0;
                startX = x; startY = y; startZ = z; lift = Math.Max(50, y + 8); Grip = 0;
                step = 0;
            }
            previous = now;
            if (!FlightRules.Finite(step) || step < 0 || step > .8) { step = 0; dwell = 0; }
            SetTarget(cradleHeight);
            double error = Math.Sqrt((x-X)*(x-X) + (y-Y)*(y-Y) + (z-Z)*(z-Z));
            bool settled = error < .35 && speed < .3 && (!AlignToRail || angle < 1) && (Stage != ShellTransferStage.Gripping || carrierPositioned);
            dwell = settled ? dwell + step : 0;
            if (Stage == ShellTransferStage.Gripping) Grip = Math.Min(1, dwell / 2);
            else Grip = 1;
            if (Stage != ShellTransferStage.Docked && dwell >= (Stage == ShellTransferStage.Gripping ? 2 : .6)) {
                Stage++; dwell = 0; SetTarget(cradleHeight);
            }
        }
        void SetTarget(double cradleHeight)
        {
            X = Stage <= ShellTransferStage.Lifting ? startX : 0;
            Z = Stage <= ShellTransferStage.Lifting ? startZ : 0;
            Y = Stage == ShellTransferStage.Gripping ? startY : Stage <= ShellTransferStage.Tilting ? lift : cradleHeight;
        }
    }
}
