using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace RailgunSandbox
{
    public static class FleetClock
    {
        // Never add an epoch-sized long directly to Unity's float clock: C#
        // promotes that sum to float and quantizes modern dates to 128 seconds.
        public static double Estimate(long serverSeconds, float realtime, float receivedAt) =>
            (double)serverSeconds + Math.Max(0.0, (double)realtime - receivedAt);
    }
    public static class FleetReconcile
    {
        // A poll or lease renewal contains the last committed flight state. It
        // must not reset the current pilot's continuous landing/stability timer.
        public static bool KeepLocal(SharedFleet before, SharedFleet after, FleetSlot slot, string player)
        {
            var prior = before?.Slots.SingleOrDefault(s => s.Pod.Number == slot.Pod.Number);
            return prior != null && before.Epoch == after.Epoch && prior.Pilot == player && slot.Pilot == player &&
                prior.Lease != "" && prior.Lease == slot.Lease && FleetWire.Digest(prior.Pod) == FleetWire.Digest(slot.Pod);
        }
    }
    public sealed class FleetSlot
    {
        public PodRecord Pod = new PodRecord();
        public long Revision;
        public string Pilot = "", Dispatcher = "", Lease = "", LastRequest = "", LastDigest = "", LastPlayer = "";
        public long LeaseUntil;
    }
    public sealed class SharedFleet
    {
        public int Version = 1, Subspace = int.MinValue;
        public double GameTime;
        public string Epoch = Guid.NewGuid().ToString("N");
        public List<FleetSlot> Slots = new List<FleetSlot>();
        public static SharedFleet New() => new SharedFleet { Slots = FleetState.New().Pods.Select(p => new FleetSlot { Pod = p }).ToList() };
        public void RestartSession()
        {
            Epoch = Guid.NewGuid().ToString("N");
            foreach (var slot in Slots) {
                // Upgrade a pre-handoff flight without changing its manifest,
                // native identity or progress. Earlier builds had only one pilot.
                if (slot.Dispatcher == "" && FlightRules.Busy(slot.Pod) && slot.Pod.Phase <= FlightPhase.Docking && slot.LastPlayer != "") slot.Dispatcher = slot.LastPlayer;
                slot.Pilot = slot.Lease = slot.LastRequest = slot.LastDigest = slot.LastPlayer = ""; slot.LeaseUntil = 0;
            }
        }
        public void Validate()
        {
            if (Version != 1 || !FlightRules.Finite(GameTime) || GameTime < 0 || !Guid.TryParseExact(Epoch, "N", out _) || Slots == null || Slots.Count != 2 ||
                Slots.Any(s => s == null || s.Revision < 0 || s.LeaseUntil < 0 || s.Pilot == null || s.Pilot.Length > 80 ||
                    (s.Lease != "" && !Guid.TryParseExact(s.Lease, "N", out _)))) throw new InvalidDataException("Invalid shared fleet authority.");
            new FleetState { Pods = Slots.Select(s => s.Pod).ToList() }.Validate();
        }
    }
    public sealed class FleetMessage
    {
        public string Kind = "", Epoch = "", Request = "", Lease = "", Error = "";
        public int Number;
        public long Revision, ServerNow;
        public PodRecord Pod;
        public SharedFleet Fleet;
        public string LoadingOrder = "";
        public double LoadingSeconds = -1, LoadingSampleUt = -1;
    }
    // Ephemeral presentation only: never changes fleet revisions, launch gates,
    // leases, physics or durable state. Late samples simply expire.
    public static class LoadingViewRules
    {
        public static bool Accept(SharedFleet state,string player,FleetMessage sample,FleetFacts facts,long now)
        {
            var slot=state?.Slots?.SingleOrDefault(s=>s.Pod.Number==sample?.Number);
            return slot!=null&&sample.Kind=="LoadingView"&&sample.Pod==null&&sample.Fleet==null&&sample.Epoch==state.Epoch&&
                slot.Pod.Phase==FlightPhase.Reserved&&sample.LoadingOrder==slot.Pod.OrderId&&slot.Pod.OrderId!=""&&
                slot.Pilot==player&&slot.Lease!=""&&sample.Lease==slot.Lease&&slot.LeaseUntil>now&&
                facts!=null&&facts.OwnsPodUpdate&&facts.Subspace==state.Subspace&&
                FlightRules.Finite(sample.LoadingSeconds)&&sample.LoadingSeconds>=-1&&sample.LoadingSeconds<=30&&
                FlightRules.Finite(sample.LoadingSampleUt)&&FlightRules.Finite(facts.GameTime)&&
                sample.LoadingSampleUt>=slot.Pod.PhaseStarted&&Math.Abs(sample.LoadingSampleUt-facts.GameTime)<=2&&
                sample.LoadingSeconds<=sample.LoadingSampleUt-slot.Pod.PhaseStarted+.25;
        }
    }
    // Facts are supplied by the server adapter from Luna's locks and durable vessel snapshots.
    // No network request can provide these flags.
    public sealed class FleetFacts
    {
        public int Subspace = int.MinValue;
        public bool ReceiverOwned, ReceiverReady, PodSnapshotVerified, PodOnPad, Docked, ReceiptDurable, LandedAtDepot, PodMissingConfirmed;
        public bool OwnsPodUpdate, PlacementSettled;
        public bool AbandonedTimeline, ForwardTimeline, OtherFlightPilotsAligned;
        public double GameTime;
    }
    public static class FleetWire
    {
        public const string Channel = "Kgeg4.RailgunReusable.v1";
        public const int MaxBytes = 131072;
        public static string Text<T>(T value)
        {
            using (var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture)) {
                new XmlSerializer(typeof(T)).Serialize(writer, value); return writer.ToString();
            }
        }
        public static T Read<T>(string text)
        {
            if (text == null || text.Length > MaxBytes) throw new InvalidDataException("Fleet message too large.");
            using (var source = new StringReader(text))
            using (var reader = XmlReader.Create(source, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBytes }))
                return (T)new XmlSerializer(typeof(T)).Deserialize(reader);
        }
        public static T Copy<T>(T value) => Read<T>(Text(value));
        public static byte[] Encode(FleetMessage value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(Text(value));
            if (bytes.Length > MaxBytes) throw new InvalidDataException("Fleet message too large."); return bytes;
        }
        public static FleetMessage Decode(byte[] bytes, int count)
        {
            if (bytes == null || count < 1 || count > MaxBytes || count > bytes.Length) throw new InvalidDataException("Invalid fleet packet length.");
            return Read<FleetMessage>(new UTF8Encoding(false, true).GetString(bytes, 0, count));
        }
        public static PodRecord NativeRecord(FleetSlot slot, FleetMessage request)
        {
            if (slot == null) return null;
            if (request.Pod != null && (request.Pod.VesselId != slot.Pod.VesselId || request.Pod.Number != slot.Pod.Number))
                throw new InvalidOperationException("A request cannot select a different physical pod.");
            return slot.Pod.Created ? slot.Pod : request.Pod ?? slot.Pod;
        }
        public static string Digest<T>(T value)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Text(value)))).Replace("-", "");
        }
    }
    public sealed class FleetAuthority
    {
        public SharedFleet State { get; private set; }
        readonly Action<SharedFleet> persist;
        bool faulted;
        public FleetAuthority(SharedFleet state, Action<SharedFleet> save) { state.Validate(); State = FleetWire.Copy(state); persist = save; }
        public SharedFleet Snapshot() => FleetWire.Copy(State);
        public SharedFleet Apply(string player, FleetMessage request, FleetFacts facts, long now)
        {
            if (faulted) throw new IOException("Fleet storage failed; restore its matching universe checkpoint.");
            if (string.IsNullOrWhiteSpace(player) || player.Length > 80 || request == null || facts == null || now < 0)
                throw new InvalidDataException("Invalid fleet request.");
            if (request.Pod != null && request.Kind != "Reserve" && request.Kind != "Commit") throw new InvalidOperationException("Unexpected pod proposal.");
            if (request.Kind == "List") return Snapshot();
            if (request.Epoch != State.Epoch || !Guid.TryParseExact(request.Request, "N", out _)) throw new InvalidOperationException("Stale fleet session.");
            var next = FleetWire.Copy(State);
            var slot = next.Slots.SingleOrDefault(s => s.Pod.Number == request.Number) ?? throw new InvalidOperationException("Unknown pod slot.");
            string digest = FleetWire.Digest(request);
            if (slot.LastRequest == request.Request) {
                if (slot.LastDigest != digest || slot.LastPlayer != player) throw new InvalidOperationException("Changed or foreign retry.");
                return Snapshot();
            }
            if (request.Revision != slot.Revision) throw new InvalidOperationException("Fleet changed; refresh before retrying.");
            bool resynchronize = request.Kind == "Resynchronize";
            if (resynchronize) {
                if (facts.Subspace < 0 || facts.Subspace == next.Subspace || !facts.AbandonedTimeline || !facts.ForwardTimeline || !facts.OtherFlightPilotsAligned ||
                    !FlightRules.Finite(facts.GameTime) || facts.GameTime < Math.Max(next.GameTime, next.Slots.Max(s => s.Pod.PhaseStarted)))
                    throw new InvalidOperationException("Return to 1x and synchronize the delivery pilots to the same Luna time before resuming.");
                if (!slot.Pod.Created || !facts.PodSnapshotVerified || !facts.OwnsPodUpdate || (slot.LeaseUntil > now && slot.Pilot != player))
                    throw new InvalidOperationException("Timeline recovery requires ownership of the original intact pod.");
                // Only move the ledger's coordination group. Native vessels,
                // manifests, receipts and flight clocks are never rewound.
                next.RestartSession();
                foreach (var entry in next.Slots) entry.Revision++;
                slot.Pilot = player; slot.Lease = Guid.NewGuid().ToString("N");
            }
            else if (facts.Subspace < 0 || (next.Subspace != int.MinValue && facts.Subspace != next.Subspace))
                throw new InvalidOperationException("Synchronize to the depot's Luna time before controlling a pod.");
            var old = slot.Pod;
            bool held = slot.Pilot == player && slot.Lease == request.Lease && slot.Lease != "" && slot.LeaseUntil > now;
            if (resynchronize) { }
            else if (request.Kind == "Acquire") {
                bool dockHandoff = (old.Phase == FlightPhase.Docking || old.Phase == FlightPhase.Unloading) && facts.Docked && facts.OwnsPodUpdate;
                if (slot.LeaseUntil > now && slot.Pilot != player && !dockHandoff) throw new InvalidOperationException("Another player controls this pod.");
                if (old.Created && !facts.OwnsPodUpdate) throw new InvalidOperationException("Acquire this vessel's Luna update lock first.");
                slot.Pilot = player; slot.Lease = Guid.NewGuid().ToString("N");
            } else if (request.Kind == "Reserve") {
                if (slot.LeaseUntil > now && slot.Pilot != player) throw new InvalidOperationException("Another player controls this pod.");
                if (!facts.ReceiverOwned || !facts.ReceiverReady || !facts.PodOnPad || !facts.PodSnapshotVerified || !facts.PlacementSettled)
                    throw new InvalidOperationException("Wait for the receiving ship, parked pod and their native snapshots to synchronize.");
                var proposed = request.Pod ?? throw new InvalidOperationException("Missing delivery manifest.");
                SameIdentity(old, proposed);
                var view = new FleetState { Pods = next.Slots.Select(s => s.Pod).ToList() };
                FlightRules.Reserve(view, old, proposed.TargetAnchor, proposed.TargetPort, proposed.TargetName, proposed.Resource,
                    proposed.CargoUnits, factsResourceCapacity(proposed.Resource), proposed.PhaseStarted);
                DockingPortPolicy.ValidateToken(proposed.DockingType);
                if (string.IsNullOrWhiteSpace(proposed.DockingHardwareConfig) || proposed.DockingHardwareConfig.Length > 32768)
                    throw new InvalidOperationException("Missing or oversized connector fitting.");
                old.DockingType = proposed.DockingType; old.DockingHardwareConfig = proposed.DockingHardwareConfig;
                old.RailHeading = proposed.RailHeading;
                slot.Pilot = player; slot.Lease = Guid.NewGuid().ToString("N");
                slot.Dispatcher = player;
            } else if (request.Kind == "Commit") {
                if (!held) throw new InvalidOperationException("Pod control lease expired or belongs to another player.");
                if (old.Created && !facts.OwnsPodUpdate) throw new InvalidOperationException("Wait for the native pod update lock.");
                var proposed = FleetWire.Copy(request.Pod ?? throw new InvalidOperationException("Missing flight state."));
                if (old.Phase == FlightPhase.Reserved && proposed.Phase == FlightPhase.RailLaunch &&
                    next.Slots.Any(s => s.Pod.Number != old.Number && FlightRules.Busy(s.Pod) && s.Pod.Phase != FlightPhase.Reserved))
                    throw new InvalidOperationException("Another pod is flying; this reservation must wait.");
                ValidateCommit(old, proposed, facts);
                slot.Pod = proposed;
            } else if (request.Kind == "Heartbeat") {
                if (!held) throw new InvalidOperationException("Pod control lease expired.");
            } else if (request.Kind == "ObserveMissing") {
                if (!old.Created || !facts.PodMissingConfirmed) throw new InvalidOperationException("Waiting for authoritative damage confirmation.");
                if (old.Phase != FlightPhase.Lost) { FlightRules.Advance(old, FlightPhase.Lost, old.PhaseStarted); old.Detail = "Native server confirmed the pod or original structural parts are missing."; }
            } else throw new InvalidOperationException("Unknown fleet operation.");
            next.Subspace = facts.Subspace;
            if (FlightRules.Finite(facts.GameTime) && facts.GameTime >= 0) next.GameTime = Math.Max(next.GameTime, facts.GameTime);
            slot.LeaseUntil = checked(now + 20); slot.Revision++;
            slot.LastRequest = request.Request; slot.LastDigest = digest; slot.LastPlayer = player;
            bool returned = request.Kind == "Commit" && old.Phase == FlightPhase.Refitting && slot.Pod.Phase == FlightPhase.Ready;
            bool parked = request.Kind == "Heartbeat" && slot.Pod.Created && slot.Pod.Phase == FlightPhase.Ready && facts.PlacementSettled && facts.PodOnPad;
            bool undocked = request.Kind == "Commit" && old.Phase == FlightPhase.Undocking && slot.Pod.Phase == FlightPhase.Returning;
            if (returned || parked || undocked || slot.Pod.Phase == FlightPhase.Lost) { slot.Pilot = slot.Lease = ""; slot.LeaseUntil = 0; }
            if (returned || slot.Pod.Phase == FlightPhase.Lost) slot.Dispatcher = "";
            next.Validate();
            // Commit storage before exposing or acknowledging a new revision. A failed
            // write never leaves a second in-memory authority that can grant more work.
            try { persist(FleetWire.Copy(next)); } catch { faulted = true; throw; }
            State = next; return Snapshot();
        }
        static double factsResourceCapacity(string resource) => resource == "MonoPropellant" ? 120 : resource == "LiquidFuel" ? 180 : resource == "Oxidizer" ? 220 : 0;
        static void SameIdentity(PodRecord old, PodRecord next)
        {
            if (old.Number != next.Number || old.VesselId != next.VesselId) throw new InvalidOperationException("Pod identity cannot change.");
            if (old.Created && (!next.Created || old.OriginalParts != next.OriginalParts || old.OwnPort != next.OwnPort ||
                !old.PartIds.SequenceEqual(next.PartIds))) throw new InvalidOperationException("Existing pod parts cannot be replaced or renumbered.");
        }
        static void ValidateCommit(PodRecord old, PodRecord next, FleetFacts facts)
        {
            SameIdentity(old, next);
            if (!FlightRules.Finite(next.PhaseStarted) || next.PhaseStarted < old.PhaseStarted || (next.Detail?.Length ?? 0) > 512)
                throw new InvalidOperationException("Invalid flight clock or status.");
            if (next.RecoveryTest || next.RailOnlyAscent || next.Phase == FlightPhase.RecoveryTestComplete)
                throw new InvalidOperationException("Private launch/recovery fixtures are not multiplayer gameplay.");
            if (old.Phase == FlightPhase.Lost || old.Phase == FlightPhase.Stranded) {
                if (FleetWire.Digest(old) != FleetWire.Digest(next)) throw new InvalidOperationException("Unavailable pods cannot be reset.");
            }
            if (!old.Created && next.Created) {
                if (!facts.OwnsPodUpdate || !facts.PodSnapshotVerified || next.PartIds.Count != next.OriginalParts || next.OriginalParts < 1 || next.OriginalParts > 128 ||
                    next.PartIds.Distinct().Count() != next.PartIds.Count || !next.PartIds.Contains(next.OwnPort) || next.Phase != FlightPhase.Ready)
                    throw new InvalidOperationException("Initial pod construction is not durable and intact.");
            } else if (next.Created && !facts.PodSnapshotVerified && next.Phase != FlightPhase.Lost)
                throw new InvalidOperationException("Wait for the native pod snapshot.");
            if (old.OrderId != next.OrderId || old.TargetPort != next.TargetPort || old.TargetAnchor != next.TargetAnchor || old.Resource != next.Resource ||
                old.CargoUnits != next.CargoUnits || old.DockingHardwareConfig != next.DockingHardwareConfig || old.DockingType != next.DockingType)
                if (!(old.Phase == FlightPhase.Refitting && next.Phase == FlightPhase.Ready && next.OrderId == "" &&
                      old.TargetPort == next.TargetPort && old.TargetAnchor == next.TargetAnchor && old.Resource == next.Resource && old.CargoUnits == next.CargoUnits &&
                      old.DockingHardwareConfig == next.DockingHardwareConfig && old.DockingType == next.DockingType))
                    throw new InvalidOperationException("An in-flight delivery manifest cannot change.");
            if (old.CargoTransferred && !next.CargoTransferred) throw new InvalidOperationException("Delivery receipts cannot be cleared.");
            if (!old.CargoTransferred && next.CargoTransferred && !facts.ReceiptDurable) throw new InvalidOperationException("Cargo requires a durable native docking receipt.");
            int expectedFlights = old.FlightsCompleted;
            if (next.Phase != old.Phase) {
                var check = FleetWire.Copy(old); check.CargoTransferred = next.CargoTransferred;
                FlightRules.Advance(check, next.Phase, next.PhaseStarted); expectedFlights = check.FlightsCompleted;
                if (next.LaunchedAt != check.LaunchedAt) throw new InvalidOperationException("Launch time must follow the native launch transition.");
                if (next.Phase == FlightPhase.Unloading && !facts.Docked) throw new InvalidOperationException("Unloading requires the assigned physical dock.");
                if (next.Phase == FlightPhase.Returning && facts.Docked) throw new InvalidOperationException("Return requires the native docking joint to separate first.");
                if ((next.Phase == FlightPhase.Refitting || next.Phase == FlightPhase.Ready) && !facts.LandedAtDepot)
                    throw new InvalidOperationException("Return and reuse require an intact native landing at the depot.");
                if (next.Phase == FlightPhase.Lost && !facts.PodMissingConfirmed) throw new InvalidOperationException("A transient sync gap is not a destroyed pod.");
            } else if (next.LaunchedAt != old.LaunchedAt || next.PhaseStarted != old.PhaseStarted) throw new InvalidOperationException("Phase and launch clocks cannot change without a transition.");
            if (next.FlightsCompleted != expectedFlights) throw new InvalidOperationException("Flight credit cannot be invented.");
        }
    }
}
