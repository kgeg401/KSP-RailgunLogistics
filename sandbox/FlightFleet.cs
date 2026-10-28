using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RailgunSandbox
{
    public enum FlightPhase { Ready, Reserved, RailLaunch, Ascent, Rendezvous, Docking, Unloading, Undocking, Returning, Landing, Refitting, Lost, Stranded, RecoveryTestComplete }

    [Serializable]
    public class PodRecord
    {
        public int Number;
        public string VesselId, OrderId = "", TargetName = "", Resource = "MonoPropellant";
        public uint TargetAnchor, TargetPort, CargoPart, OwnPort;
        public List<uint> PartIds = new List<uint>();
        public bool Created, CargoTransferred, RecoveryTest;
        public bool RailOnlyAscent, CircularizationPlanned, CircularizationStarted;
        public double RailHeading = 90, CircularizationDeltaV, CircularizationHalfBurnTime;
        public string DockingType = "size1";
        public string DockingHardwareConfig = "";
        public string RecoveryTestRunId = "";
        public double CargoUnits, PhaseStarted, LaunchedAt, CradleHeight, StableSince = -1;
        public int OriginalParts, FlightsCompleted;
        public FlightPhase Phase = FlightPhase.Ready;
        public string Detail = "Parked at depot";
    }

    [Serializable]
    public class FleetState
    {
        public int Version = 1;
        public string HomeBody = "Kerbin";
        public double HomeLatitude = -0.04855, HomeLongitude = -74.612;
        public uint SelectedReceiverAnchor, SelectedReceiverPort;
        public List<PodRecord> Pods = new List<PodRecord>();
        public static FleetState New()
        {
            var state = new FleetState();
            for (int number = 1; number <= 2; number++)
                state.Pods.Add(new PodRecord { Number = number, VesselId = Guid.NewGuid().ToString("N") });
            return state;
        }
        public void Validate()
        {
            if (Version != 1 || Pods == null || Pods.Count != 2 ||
                !Pods.Select(p => p.Number).OrderBy(n => n).SequenceEqual(new[] { 1, 2 }) ||
                Pods.Select(p => p.VesselId).Distinct().Count() != 2 ||
                Pods.Any(p => !Guid.TryParseExact(p.VesselId, "N", out _) || !Enum.IsDefined(typeof(FlightPhase), p.Phase)) ||
                Pods.Any(p => !FlightRules.Finite(p.CargoUnits) || p.CargoUnits < 0 || !FlightRules.Finite(p.PhaseStarted)))
                throw new InvalidDataException("Invalid reusable-pod fleet. Restore the save; do not generate replacement pods.");
        }
    }

    public static class FlightRules
    {
        public static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
        public static bool SafeOrbit(double periapsis, double eccentricity, double atmosphere) => Finite(periapsis) && Finite(eccentricity) &&
            Finite(atmosphere) && atmosphere >= 0 && eccentricity >= 0 && eccentricity < 1 && periapsis > atmosphere + 3000;
        public static bool Busy(PodRecord p) => p.Phase != FlightPhase.Ready && p.Phase != FlightPhase.Lost && p.Phase != FlightPhase.Stranded && p.Phase != FlightPhase.RecoveryTestComplete;
        public static bool RequiresMainPropellant(FlightPhase phase) => phase == FlightPhase.Ascent || phase == FlightPhase.Rendezvous || phase == FlightPhase.Landing;
        // Native Mainsail starved with .0136 LF/.0163 Ox still reported in its tanks.
        // Do not mistake unusable frame-sized residue for a viable powered recovery.
        public static bool HasMainPropellant(double liquidFuel, double oxidizer) => Finite(liquidFuel) && Finite(oxidizer) && liquidFuel > .1 && oxidizer > .1;
        public static void BeginRecoveryTest(PodRecord p, bool isolatedTestSave, double now)
        {
            if (!isolatedTestSave || !p.Created || p.Phase != FlightPhase.Rendezvous || p.CargoTransferred || p.RecoveryTest)
                throw new InvalidOperationException("Return-only tests require a fresh isolated re-entry fixture in orbit. No delivery or refit will be credited.");
            if (!Finite(now) || now < p.PhaseStarted) throw new InvalidOperationException("Invalid recovery-test clock.");
            p.RecoveryTest = true;
            p.RecoveryTestRunId = Guid.NewGuid().ToString("N");
            Advance(p, FlightPhase.Landing, now);
        }
        public static void Reserve(FleetState state, PodRecord pod, uint anchor, uint port, string targetName,
            string resource, double units, double capacity, double now, bool railOnlyAscent = false)
        {
            state.Validate();
            if (!pod.Created || pod.Phase != FlightPhase.Ready) throw new InvalidOperationException("That pod has not returned and refitted.");
            if (anchor == 0 || port == 0 || string.IsNullOrWhiteSpace(targetName)) throw new InvalidOperationException("Choose a receiving ship and docking port.");
            if (!Finite(units) || units <= 0 || !Finite(capacity) || units > capacity ||
                !(resource == "MonoPropellant" || resource == "LiquidFuel" || resource == "Oxidizer"))
                throw new InvalidOperationException("Cargo must fit the stock tank and use supported propellant.");
            if (state.Pods.Any(p => Busy(p) && p.TargetPort == port)) throw new InvalidOperationException("A pod is already assigned to that port.");
            pod.OrderId = Guid.NewGuid().ToString("N"); pod.TargetAnchor = anchor; pod.TargetPort = port;
            pod.TargetName = targetName; pod.Resource = resource; pod.CargoUnits = units; pod.CargoTransferred = false;
            pod.LaunchedAt = 0; pod.CradleHeight = 0; pod.StableSince = -1;
            pod.RailOnlyAscent = railOnlyAscent; pod.CircularizationPlanned = false; pod.CircularizationStarted = false;
            pod.CircularizationDeltaV = 0; pod.CircularizationHalfBurnTime = 0;
            Advance(pod, FlightPhase.Reserved, now);
        }
        public static void Advance(PodRecord p, FlightPhase next, double now)
        {
            if (!Finite(now) || now < p.PhaseStarted) throw new InvalidOperationException("Flight clock moved backwards; reload the matching save.");
            bool allowed =
                p.Phase == FlightPhase.Ready && next == FlightPhase.Reserved ||
                p.Phase == FlightPhase.Reserved && next == FlightPhase.RailLaunch ||
                p.Phase == FlightPhase.RailLaunch && next == FlightPhase.Ascent ||
                p.Phase == FlightPhase.Ascent && next == FlightPhase.Rendezvous ||
                p.Phase == FlightPhase.Rendezvous && next == FlightPhase.Docking ||
                p.Phase == FlightPhase.Docking && next == FlightPhase.Unloading ||
                p.Phase == FlightPhase.Unloading && next == FlightPhase.Undocking && p.CargoTransferred ||
                p.Phase == FlightPhase.Undocking && next == FlightPhase.Returning ||
                p.Phase == FlightPhase.Returning && next == FlightPhase.Landing ||
                p.Phase == FlightPhase.Rendezvous && next == FlightPhase.Landing && p.RecoveryTest ||
                p.Phase == FlightPhase.Landing && next == FlightPhase.Refitting && !p.RecoveryTest ||
                p.Phase == FlightPhase.Landing && next == FlightPhase.RecoveryTestComplete && p.RecoveryTest ||
                p.Phase == FlightPhase.Refitting && next == FlightPhase.Ready ||
                p.Phase != FlightPhase.Lost && next == FlightPhase.Lost ||
                RequiresMainPropellant(p.Phase) && next == FlightPhase.Stranded;
            if (!allowed) throw new InvalidOperationException("Invalid flight transition " + p.Phase + " -> " + next);
            p.Phase = next; p.PhaseStarted = now; p.StableSince = -1;
            if (next == FlightPhase.RailLaunch) p.LaunchedAt = now;
            if (next == FlightPhase.Ready) { p.FlightsCompleted++; p.OrderId = ""; p.Detail = "Returned, inspected and refuelled"; }
        }
        public static bool DockingEvidence(bool ownDocked, bool reciprocalDocked, uint partner, uint requiredPort,
            bool sameVessel, bool anchorPresent) => ownDocked && reciprocalDocked && partner == requiredPort && requiredPort != 0 && sameVessel && anchorPresent;
        public static double[] CargoAllocation(double requested, double available, IList<double> amounts, IList<double> capacities)
        {
            if (!Finite(requested) || requested <= 0 || !Finite(available) || available < requested || amounts.Count != capacities.Count)
                throw new InvalidOperationException("Invalid physical cargo amount.");
            for (int i = 0; i < amounts.Count; i++)
                if (!Finite(amounts[i]) || amounts[i] < 0 || !Finite(capacities[i]) || capacities[i] < amounts[i])
                    throw new InvalidOperationException("Invalid receiver tank state.");
            double remaining = requested;
            var result = new double[amounts.Count];
            for (int i = 0; i < amounts.Count; i++) { result[i] = Math.Min(remaining, capacities[i] - amounts[i]); remaining -= result[i]; }
            return remaining > 1e-6 ? null : result;
        }
        public static bool LandingEvidence(bool nativeLanded, bool sameBody, double padDistance, double surfaceSpeed,
            int intactParts, int originalParts, double uprightDegrees = 0) => nativeLanded && sameBody && Finite(padDistance) && padDistance >= 0 && padDistance <= 25 &&
                Finite(surfaceSpeed) && surfaceSpeed >= 0 && surfaceSpeed <= 0.5 && intactParts >= originalParts && originalParts > 0 &&
                Finite(uprightDegrees) && uprightDegrees >= 0 && uprightDegrees <= 8;
        public static bool CanSettleWithoutFuel(bool homeBody, bool splashed, double clearance, double padDistance, double surfaceSpeed, double uprightDegrees) =>
            homeBody && !splashed && Finite(clearance) && clearance >= 0 && clearance <= 10 && Finite(padDistance) && padDistance >= 0 && padDistance <= 25 &&
            Finite(surfaceSpeed) && surfaceSpeed >= 0 && surfaceSpeed <= 5 && Finite(uprightDegrees) && uprightDegrees >= 0 && uprightDegrees <= 8;
        public static bool StableLanding(PodRecord p, bool evidence, double now)
        {
            if (!evidence) { p.StableSince = -1; return false; }
            if (p.StableSince < 0) p.StableSince = now;
            return now - p.StableSince >= 5;
        }
        public static string Duration(double seconds)
        {
            if (!Finite(seconds) || seconds < 0) return "Unknown";
            long value = checked((long)seconds);
            if (value >= 31557600) return (value / 31557600.0).ToString("F2") + " Earth years";
            if (value >= 86400) return (value / 86400) + "d " + (value % 86400 / 3600) + "h";
            return (value / 3600) + "h " + (value % 3600 / 60) + "m " + (value % 60) + "s";
        }
    }
}
