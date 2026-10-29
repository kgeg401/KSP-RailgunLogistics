using System;

namespace RailgunSandbox
{
    public enum LoadingStage { Aligning, Capturing, Clamping, Locking, ReleasingCarrier, RetractingCarrier, Charging, Unlocking, Clearing, Ready }

    // Local mechanism interlock, separate from the flight ledger. A scene or
    // pilot change requires fresh measured capture; a saved timer cannot fire a pod.
    public sealed class LoadingSequence
    {
        public const double CaptureSeconds = 2, ClampSeconds = 3, LockSeconds = 2,
            ChargeSeconds = 4, UnlockSeconds = 1.5, ClearSeconds = 2;
        public const double Duration = CaptureSeconds + ClampSeconds + LockSeconds + ChargeSeconds + UnlockSeconds + ClearSeconds;
        public const double ShellClampSeconds = 4, CarrierReleaseSeconds = 2.5, CarrierRetractSeconds = 3;
        public const double ShellDuration = Duration + ShellClampSeconds - ClampSeconds + CarrierReleaseSeconds + CarrierRetractSeconds;
        readonly bool shell;
        public LoadingSequence(bool shellMode = false) { shell = shellMode; Pose = LoadingPose.At(-1, shell); }
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
            Pose = LoadingPose.At(captured ? elapsed : -1, shell);
            return Pose;
        }
        public void Reset() { previous = double.NaN; elapsed = 0; order = ""; hadCapture = false; Pose = LoadingPose.At(-1, shell); }
    }

    public sealed class LoadingPose
    {
        public LoadingStage Stage;
        public double Elapsed, Clamps, Pins, Charge;
        public bool ShellMode;
        public double CarrierGrip, CarrierRetraction;
        public bool LaunchClear => Stage == LoadingStage.Ready && Clamps == 0 && Pins == 0 && Charge == 1 && (!ShellMode || CarrierGrip == 0 && CarrierRetraction == 1);
        // Read-only remote artwork. This never updates a LoadingSequence and
        // cannot supply the pilot's measured launch interlock.
        public static LoadingPose FromPilotSample(double seconds,double sampledAt,double now,bool shell=false)
        {
            if(!FlightRules.Finite(seconds)||!FlightRules.Finite(sampledAt)||!FlightRules.Finite(now)||
                seconds<0||sampledAt<0||now<sampledAt-1||now-sampledAt>2)return At(-1,shell);
            return At(seconds+Math.Max(0,now-sampledAt),shell);
        }
        public string Description => Stage == LoadingStage.Aligning ? "Gantry transporting pod to launch cradle" :
            Stage == LoadingStage.Capturing ? "Cradle alignment and motion check" :
            Stage == LoadingStage.Clamping ? "Hydraulic collars closing" :
            Stage == LoadingStage.Locking ? "Locking pins seating" :
            Stage == LoadingStage.ReleasingCarrier ? "Rail locks secured; opening transport carrier" :
            Stage == LoadingStage.RetractingCarrier ? "Retracting carrier clear of shell and rail" :
            Stage == LoadingStage.Charging ? "Pins locked; accelerator charging" :
            Stage == LoadingStage.Unlocking ? "Charge ready; withdrawing locking pins" :
            Stage == LoadingStage.Clearing ? "Opening collars and clearing gantry" : "All loading hardware clear";
        static double Ramp(double time, double start, double duration) {
            double t = Math.Max(0, Math.Min(1, (time - start) / duration)); return t * t * (3 - 2 * t);
        }
        public static LoadingPose At(double seconds, bool shellMode = false)
        {
            double capture = LoadingSequence.CaptureSeconds;
            double clampDuration = shellMode ? LoadingSequence.ShellClampSeconds : LoadingSequence.ClampSeconds;
            double clamp = capture + clampDuration;
            double locked = clamp + LoadingSequence.LockSeconds;
            double released = locked + (shellMode ? LoadingSequence.CarrierReleaseSeconds : 0);
            double retracted = released + (shellMode ? LoadingSequence.CarrierRetractSeconds : 0);
            double charged = retracted + LoadingSequence.ChargeSeconds;
            double unlocked = charged + LoadingSequence.UnlockSeconds;
            double duration = unlocked + LoadingSequence.ClearSeconds;
            return new LoadingPose {
                Elapsed = Math.Max(0, seconds),
                ShellMode = shellMode,
                Stage = seconds < 0 ? LoadingStage.Aligning : seconds < capture ? LoadingStage.Capturing :
                    seconds < clamp ? LoadingStage.Clamping : seconds < locked ? LoadingStage.Locking :
                    seconds < released ? LoadingStage.ReleasingCarrier : seconds < retracted ? LoadingStage.RetractingCarrier :
                    seconds < charged ? LoadingStage.Charging : seconds < unlocked ? LoadingStage.Unlocking :
                    seconds < duration ? LoadingStage.Clearing : LoadingStage.Ready,
                Clamps = Ramp(seconds, capture, clampDuration) * (1 - Ramp(seconds, unlocked, LoadingSequence.ClearSeconds)),
                Pins = Ramp(seconds, clamp, LoadingSequence.LockSeconds) * (1 - Ramp(seconds, charged, LoadingSequence.UnlockSeconds)),
                Charge = Ramp(seconds, retracted, LoadingSequence.ChargeSeconds),
                CarrierGrip = shellMode ? 1 - Ramp(seconds, locked, LoadingSequence.CarrierReleaseSeconds) : 0,
                CarrierRetraction = shellMode ? Ramp(seconds, released, LoadingSequence.CarrierRetractSeconds) : 1
            };
        }
    }
}
