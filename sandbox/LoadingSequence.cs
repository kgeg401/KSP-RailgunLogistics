using System;

namespace RailgunSandbox
{
    public enum LoadingStage { Aligning, Capturing, Clamping, Locking, Charging, Unlocking, Clearing, Ready }

    // Local mechanism interlock, separate from the flight ledger. A scene or
    // pilot change requires fresh measured capture; a saved timer cannot fire a pod.
    public sealed class LoadingSequence
    {
        public const double CaptureSeconds = 2, ClampSeconds = 3, LockSeconds = 2,
            ChargeSeconds = 4, UnlockSeconds = 1.5, ClearSeconds = 2;
        public const double Duration = CaptureSeconds + ClampSeconds + LockSeconds + ChargeSeconds + UnlockSeconds + ClearSeconds;
        double previous = double.NaN, elapsed;
        string order = "";
        bool hadCapture;
        public LoadingPose Pose { get; private set; } = LoadingPose.At(-1);
        public LoadingPose Update(string orderId, double now, bool captured)
        {
            if (string.IsNullOrEmpty(orderId) || !FlightRules.Finite(now)) {
                Reset(); return Pose;
            }
            double step = now - previous;
            if (order != orderId || !FlightRules.Finite(step) || step < 0 || step > .8 || !captured || !hadCapture) elapsed = 0;
            else elapsed += step;
            previous = now; order = orderId; hadCapture = captured;
            Pose = LoadingPose.At(captured ? elapsed : -1);
            return Pose;
        }
        public void Reset() { previous = double.NaN; elapsed = 0; order = ""; hadCapture = false; Pose = LoadingPose.At(-1); }
    }

    public sealed class LoadingPose
    {
        public LoadingStage Stage;
        public double Elapsed, Clamps, Pins, Charge;
        public bool LaunchClear => Stage == LoadingStage.Ready;
        public string Description => Stage == LoadingStage.Aligning ? "Gantry transporting pod to launch cradle" :
            Stage == LoadingStage.Capturing ? "Cradle alignment and motion check" :
            Stage == LoadingStage.Clamping ? "Hydraulic collars closing" :
            Stage == LoadingStage.Locking ? "Locking pins seating" :
            Stage == LoadingStage.Charging ? "Pins locked; accelerator charging" :
            Stage == LoadingStage.Unlocking ? "Charge ready; withdrawing locking pins" :
            Stage == LoadingStage.Clearing ? "Opening collars and clearing gantry" : "All loading hardware clear";
        static double Ramp(double time, double start, double duration) {
            double t = Math.Max(0, Math.Min(1, (time - start) / duration)); return t * t * (3 - 2 * t);
        }
        public static LoadingPose At(double seconds)
        {
            double capture = LoadingSequence.CaptureSeconds;
            double clamp = capture + LoadingSequence.ClampSeconds;
            double locked = clamp + LoadingSequence.LockSeconds;
            double charged = locked + LoadingSequence.ChargeSeconds;
            double unlocked = charged + LoadingSequence.UnlockSeconds;
            return new LoadingPose {
                Elapsed = Math.Max(0, seconds),
                Stage = seconds < 0 ? LoadingStage.Aligning : seconds < capture ? LoadingStage.Capturing :
                    seconds < clamp ? LoadingStage.Clamping : seconds < locked ? LoadingStage.Locking :
                    seconds < charged ? LoadingStage.Charging : seconds < unlocked ? LoadingStage.Unlocking :
                    seconds < LoadingSequence.Duration ? LoadingStage.Clearing : LoadingStage.Ready,
                Clamps = Ramp(seconds, capture, LoadingSequence.ClampSeconds) * (1 - Ramp(seconds, unlocked, LoadingSequence.ClearSeconds)),
                Pins = Ramp(seconds, clamp, LoadingSequence.LockSeconds) * (1 - Ramp(seconds, charged, LoadingSequence.UnlockSeconds)),
                Charge = Ramp(seconds, locked, LoadingSequence.ChargeSeconds)
            };
        }
    }
}
