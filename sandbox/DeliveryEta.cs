using System;
using System.Globalization;

namespace RailgunSandbox
{
    // Display-only projection. Never drives a maneuver, a receipt or a fleet transition.
    // Absolute KSP UT prevents a cached prediction counting down while simulation is paused.
    public sealed class DeliveryEstimate
    {
        public double SampleUt, EarliestUt, LatestUt;
        public string Basis, Message;
        public bool Available;

        public string Display(double now)
        {
            if (!Available) return Message;
            if (!FlightRules.Finite(now) || now < SampleUt || now > LatestUt)
                return "Delivery ETA: recalculating";
            double low = Math.Max(0, EarliestUt - now), high = Math.Max(0, LatestUt - now);
            return "Delivery ETA: ~" + DeliveryEta.Short(low) + "–" + DeliveryEta.Short(high);
        }
    }

    public static class DeliveryEta
    {
        public static DeliveryEstimate Unavailable(string message) => new DeliveryEstimate { Message = message, Basis = message };

        public static DeliveryEstimate State(PodRecord pod, bool guidanceReady)
        {
            if (pod.CargoTransferred) return Unavailable("Supplies delivered");
            if (pod.Phase == FlightPhase.Lost || pod.Phase == FlightPhase.Stranded)
                return Unavailable("Delivery interrupted · recovery required");
            if (!FlightRules.Busy(pod)) return Unavailable("No active delivery");
            if (!guidanceReady) return Unavailable("Delivery ETA paused · waiting for flight control");
            if (pod.Phase == FlightPhase.Unloading) return Unavailable("Docked · awaiting cargo transfer");
            if (pod.Phase >= FlightPhase.Undocking) return Unavailable("Return flight · no pending delivery");
            return Unavailable(pod.Phase <= FlightPhase.Ascent ? "Delivery ETA: calculating after orbit insertion" : "Delivery ETA: planning encounter");
        }

        public static DeliveryEstimate Encounter(double now, double encounterUt, double approachMetres, double dockingSpeed, string basis)
        {
            if (!FlightRules.Finite(now) || now < 0 || !FlightRules.Finite(encounterUt) || encounterUt < now ||
                !FlightRules.Finite(approachMetres) || approachMetres < 0 || approachMetres > 1000 ||
                !FlightRules.Finite(dockingSpeed) || dockingSpeed <= 0 || string.IsNullOrWhiteSpace(basis))
                return Unavailable("Delivery ETA: planning encounter");
            // MJ's configured docking speed is a ceiling. Alignment, braking and
            // capture add uncertainty; this is deliberately a range, not a promise.
            double approach = Math.Max(30, approachMetres / Math.Min(1, dockingSpeed));
            double transit = encounterUt - now;
            double low = encounterUt + approach + 3;
            double high = encounterUt + Math.Max(180, approach * 2) + Math.Max(60, transit * .15) + 3;
            if (!FlightRules.Finite(low) || !FlightRules.Finite(high)) return Unavailable("Delivery ETA: planning encounter");
            return new DeliveryEstimate { Available = true, SampleUt = now, EarliestUt = low, LatestUt = high, Basis = basis };
        }

        public static DeliveryEstimate Rendezvous(double now, double encounterUt, double separation, double maxClosingSpeed, double dockingSpeed, string basis)
        {
            if (!FlightRules.Finite(separation) || separation < 0 || !FlightRules.Finite(maxClosingSpeed) || maxClosingSpeed <= 0)
                return Unavailable("Delivery ETA: planning encounter");
            // Same distance-dependent intercept speed as native rendezvous guidance.
            // This extra leg is a forecast: it has not yet become a docking receipt.
            double intercept = separation <= 160 ? 0 : 15 + separation / Math.Max(.01, Math.Min(maxClosingSpeed, separation / 100));
            var estimate = Encounter(now, encounterUt, Math.Min(100, separation), dockingSpeed, basis);
            if (!estimate.Available || !FlightRules.Finite(estimate.LatestUt + intercept * 2)) return Unavailable("Delivery ETA: planning encounter");
            estimate.EarliestUt += intercept; estimate.LatestUt += intercept * 2;
            return estimate;
        }

        public static string Short(double seconds)
        {
            if (!FlightRules.Finite(seconds) || seconds < 0) return "unknown";
            // Keep user-facing ranges readable without long overflow or a 24-hour clamp.
            if (seconds < 60) return "<1m";
            if (seconds < 3600) return Math.Ceiling(seconds / 60).ToString("0", CultureInfo.InvariantCulture) + "m";
            if (seconds < 86400) return (seconds / 3600).ToString("0.0", CultureInfo.InvariantCulture) + "h";
            if (seconds < 31557600) return (seconds / 86400).ToString("0.0", CultureInfo.InvariantCulture) + "d";
            return (seconds / 31557600).ToString("0.0", CultureInfo.InvariantCulture) + "y";
        }
    }
}
