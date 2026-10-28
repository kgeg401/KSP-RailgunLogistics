using System;
using System.IO;
using System.Linq;
using RailgunSandbox;

int count = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }
void Reject(Action action, string label) { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } Check(rejected, label); }
Check(Math.Abs(FleetClock.Estimate(1793148673, 100.25f, 100f) - 1793148673.25) < .000001, "Epoch-sized lease clock preserves subsecond precision instead of rounding by 128 seconds");
Check(FleetClock.Estimate(1793148673, 99f, 100f) == 1793148673, "A scene clock regression cannot move server time backwards");
var fleet = SharedFleet.New();
foreach (var slot in fleet.Slots) { slot.Pod.Created = true; slot.Pod.PartIds.AddRange(new uint[] { (uint)(slot.Pod.Number * 10 + 1), (uint)(slot.Pod.Number * 10 + 2) }); slot.Pod.OwnPort = slot.Pod.PartIds[0]; slot.Pod.OriginalParts = 2; }
SharedFleet disk = null;
var authority = new FleetAuthority(fleet, state => disk = state);
var facts = new FleetFacts { Subspace = 2, ReceiverOwned = true, ReceiverReady = true, PodOnPad = true, PodSnapshotVerified = true, OwnsPodUpdate = true };
FleetMessage Reserve(int number, uint port = 77) {
    var snapshot = authority.Snapshot(); var slot = snapshot.Slots.Single(s => s.Pod.Number == number);
    var proposed = FleetWire.Copy(slot.Pod);
    var local = new FleetState { Pods = snapshot.Slots.Select(s => s.Pod.Number == number ? proposed : s.Pod).ToList() };
    FlightRules.Reserve(local, proposed, 66, port, "Orbital station", "MonoPropellant", 100, 120, 1000);
    proposed.DockingHardwareConfig = "captured native connector";
    return new FleetMessage { Kind = "Reserve", Epoch = snapshot.Epoch, Request = Guid.NewGuid().ToString("N"), Number = number, Revision = slot.Revision, Pod = proposed };
}
FleetMessage Commit(int number, Action<PodRecord> mutate) {
    var snapshot = authority.Snapshot(); var slot = snapshot.Slots.Single(s => s.Pod.Number == number); var next = FleetWire.Copy(slot.Pod); mutate(next);
    return new FleetMessage { Kind = "Commit", Epoch = snapshot.Epoch, Request = Guid.NewGuid().ToString("N"), Number = number, Revision = slot.Revision, Lease = slot.Lease, Pod = next };
}
void Advance(FlightPhase phase, double time) {
    var slot = authority.Snapshot().Slots[0];
    if (slot.Pilot == "") authority.Apply("Alice", new FleetMessage { Kind = "Acquire", Number = 1, Epoch = authority.State.Epoch, Revision = slot.Revision, Request = Guid.NewGuid().ToString("N") }, facts, 1001);
    authority.Apply("Alice", Commit(1, p => FlightRules.Advance(p, phase, time)), facts, 1001);
}
var a = Reserve(1); facts.ReceiverOwned = false;
Reject(() => authority.Apply("Alice", a, facts, 1000), "Unowned receiving ship cannot reserve a pod");
Check(authority.State.Slots[0].Revision == 0 && disk == null, "Rejected request changes neither authority nor disk");
facts.ReceiverOwned = true; var reserved = authority.Apply("Alice", a, facts, 1000);
Check(reserved.Slots[0].Pilot == "Alice" && reserved.Slots[0].Lease.Length == 32 && disk!.Slots[0].Pod.Phase == FlightPhase.Reserved, "Server commits ownership and reservation before acknowledgement");
var renewed = FleetWire.Copy(reserved); renewed.Slots[0].Revision++; renewed.Slots[0].LeaseUntil++;
Check(FleetReconcile.KeepLocal(reserved, renewed, renewed.Slots[0], "Alice"), "A lease-only acknowledgement preserves the pilot's continuous physics timers");
renewed.Slots[0].Pod.PhaseStarted++;
Check(!FleetReconcile.KeepLocal(reserved, renewed, renewed.Slots[0], "Alice"), "A changed authoritative flight state replaces local transient state");
renewed = FleetWire.Copy(reserved); renewed.Slots[0].Pilot = "Bob";
Check(!FleetReconcile.KeepLocal(reserved, renewed, renewed.Slots[0], "Alice"), "Ownership handoff never preserves the previous pilot's transient state");
renewed = FleetWire.Copy(reserved); renewed.RestartSession();
Check(!FleetReconcile.KeepLocal(reserved, renewed, renewed.Slots[0], "Alice"), "A new server session forces authoritative reconciliation");
var retry = authority.Apply("Alice", a, facts, 1001);
var malformedNative = FleetWire.Copy(a); malformedNative.Pod!.PartIds.Clear();
Check(FleetWire.NativeRecord(authority.State.Slots[0], malformedNative).PartIds.Count == 2, "Native damage checks use established server part IDs, never a client's proposed list");
malformedNative.Pod.VesselId = authority.State.Slots[1].Pod.VesselId;
Reject(() => FleetWire.NativeRecord(authority.State.Slots[0], malformedNative), "A request cannot redirect native verification to another pod");
var malformedMissing = FleetWire.Copy(a); malformedMissing.Kind = "ObserveMissing";
Reject(() => authority.Apply("Bob", malformedMissing, facts, 1001), "Missing-object reports cannot carry a fabricated pod proposal");
Check(retry.Slots[0].Revision == reserved.Slots[0].Revision, "Lost acknowledgement retry does not reserve a second pod");
Reject(() => authority.Apply("Bob", a, facts, 1001), "Other player cannot replay an accepted request");
var changedRetry = FleetWire.Copy(a); changedRetry.Pod!.CargoUnits = 99;
Reject(() => authority.Apply("Alice", changedRetry, facts, 1001), "Same request identity with changed cargo is rejected");
bool duplicateRejected = false; try { Reserve(2); } catch (InvalidOperationException) { duplicateRejected = true; }
Check(duplicateRejected, "Concurrent second reservation to the same port is rejected");
var b = Reserve(2, 88); authority.Apply("Bob", b, facts, 1001);
Check(authority.State.Slots.All(s => s.Pod.Phase == FlightPhase.Reserved) && authority.State.Slots.Select(s => s.Pilot).Distinct().Count() == 2,
    "Two players can reserve different pods and ports without overwriting each other");
Reject(() => authority.Apply("Bob", Commit(1, p => p.Detail = "steal"), facts, 1001), "Other player cannot drive the leased pod");
var stale = Commit(1, p => p.Detail = "old");
authority.Apply("Alice", Commit(1, p => p.Detail = "new"), facts, 1001);
Reject(() => authority.Apply("Alice", stale, facts, 1001), "Delayed update cannot overwrite a newer revision");
facts.Subspace = 3;
Reject(() => authority.Apply("Alice", Commit(1, p => p.Detail = "future"), facts, 1001), "Different Luna timeline cannot drive this depot");
facts.Subspace = 2; facts.OwnsPodUpdate = false;
Reject(() => authority.Apply("Alice", Commit(1, p => p.Detail = "lock lost"), facts, 1001), "A lease does not bypass native vessel ownership");
facts.OwnsPodUpdate = true;
Reject(() => authority.Apply("Alice", Commit(1, p => p.PartIds[0] = 999), facts, 1001), "Structural identity cannot change during a flight");
Reject(() => authority.Apply("Alice", Commit(1, p => p.CargoUnits = 900), facts, 1001), "A reserved manifest cannot gain cargo");
Reject(() => authority.Apply("Alice", Commit(1, p => p.FlightsCompleted++), facts, 1001), "Client cannot invent completed flights");
Reject(() => authority.Apply("Alice", Commit(1, p => p.RailOnlyAscent = true), facts, 1001), "Paused rail-only fixture cannot be enabled in multiplayer");
Reject(() => authority.Apply("Alice", Commit(1, p => p.PhaseStarted++), facts, 1001), "Same-phase update cannot rewrite its start clock");
Advance(FlightPhase.RailLaunch, 1001);
Reject(() => authority.Apply("Bob", Commit(2, p => FlightRules.Advance(p, FlightPhase.RailLaunch, 1001)), facts, 1001), "Second reserved pod waits while the foreground flight is active");
Advance(FlightPhase.Ascent, 1002); Advance(FlightPhase.Rendezvous, 1003); Advance(FlightPhase.Docking, 1004);
var dockAuthority = new FleetAuthority(authority.Snapshot(), _ => { });
var dockSlot = dockAuthority.State.Slots[0];
var dockTakeover = new FleetMessage { Kind = "Acquire", Number = 1, Epoch = dockAuthority.State.Epoch, Revision = dockSlot.Revision, Request = Guid.NewGuid().ToString("N") };
Reject(() => dockAuthority.Apply("Bob", dockTakeover, facts, 1001), "Receiver cannot take the pilot's lease before a real dock");
facts.Docked = true; facts.OwnsPodUpdate = false;
Reject(() => dockAuthority.Apply("Bob", dockTakeover, facts, 1001), "Docking handoff still requires ownership of the merged native vessel");
facts.OwnsPodUpdate = true;
var received = dockAuthority.Apply("Bob", dockTakeover, facts, 1001);
Check(received.Slots[0].Pilot == "Bob" && received.Slots[0].Dispatcher == "Alice", "Actual receiver owner can accept unloading while preserving the original return pilot");
facts.Docked = false;
Reject(() => Advance(FlightPhase.Unloading, 1005), "Unloading cannot start from a proximity estimate");
facts.Docked = true; Advance(FlightPhase.Unloading, 1005);
Reject(() => authority.Apply("Alice", Commit(1, p => p.CargoTransferred = true), facts, 1001), "Cargo completion requires a durable vessel receipt");
facts.ReceiptDurable = true; authority.Apply("Alice", Commit(1, p => p.CargoTransferred = true), facts, 1001);
Reject(() => authority.Apply("Alice", Commit(1, p => p.CargoTransferred = false), facts, 1001), "Acknowledged delivery cannot be cleared and repeated");
Advance(FlightPhase.Undocking, 1006);
Reject(() => Advance(FlightPhase.Returning, 1007), "A still-attached pod cannot enter its return flight");
facts.Docked = false; Advance(FlightPhase.Returning, 1007);
Check(authority.State.Slots[0].Pilot == "" && authority.State.Slots[0].Dispatcher == "Alice", "Undocking releases the receiver's lease for the dispatching pilot to resume");
Advance(FlightPhase.Landing, 1008);
Reject(() => Advance(FlightPhase.Refitting, 1009), "Pod cannot refit before native home-pad landing");
facts.LandedAtDepot = true; Advance(FlightPhase.Refitting, 1009); Advance(FlightPhase.Ready, 1010);
Check(authority.State.Slots[0].Pod.FlightsCompleted == 1 && authority.State.Slots[0].Pod.OrderId == "", "Only the complete landed/refitted sequence releases and credits the pod");
Check(authority.State.Slots[0].Pilot == "" && authority.State.Slots[0].LeaseUntil == 0, "Returned pod becomes available to either player immediately");
Reject(() => authority.Apply("Alice", Commit(1, p => p.Detail = "expired"), facts, 2000), "Disconnected or stalled pilot loses write authority");
var handoffState = authority.Snapshot(); var handoff = new FleetMessage { Kind = "Acquire", Epoch = handoffState.Epoch, Request = Guid.NewGuid().ToString("N"), Number = 1, Revision = handoffState.Slots[0].Revision };
var taken = authority.Apply("Bob", handoff, facts, 2000);
Check(taken.Slots[0].Pilot == "Bob" && taken.Slots[0].Pod.FlightsCompleted == 1 && taken.Slots[0].Pod.VesselId == reserved.Slots[0].Pod.VesselId,
    "Expired lease takeover preserves the original pod and mission history");
var restarted = FleetWire.Copy(taken); restarted.RestartSession();
var legacy = FleetWire.Copy(taken); legacy.Slots[1].Dispatcher = ""; legacy.RestartSession();
Check(legacy.Slots[1].Dispatcher == "Bob" && legacy.Slots[1].Pod.VesselId == taken.Slots[1].Pod.VesselId, "Pre-handoff flight upgrade retains its original pilot without replacing the pod");
Check(restarted.Epoch != taken.Epoch && restarted.Slots.All(s => s.Pilot == "" && s.Lease == "" && s.LastRequest == "") && restarted.Slots[0].Pod.VesselId == taken.Slots[0].Pod.VesselId && restarted.Slots[0].Pod.FlightsCompleted == 1,
    "Server restart invalidates sessions without replacing pods or flight history");
var afterRestart = new FleetAuthority(restarted, _ => { });
Reject(() => afterRestart.Apply("Bob", handoff, facts, 2000), "A pre-restart request cannot take over the new session");
facts.PlacementSettled = true;
var parkedSlot = authority.Snapshot().Slots[0];
var parkedRelease = new FleetMessage { Kind = "Heartbeat", Number = 1, Epoch = authority.State.Epoch, Request = Guid.NewGuid().ToString("N"), Revision = parkedSlot.Revision, Lease = parkedSlot.Lease };
var parked = authority.Apply("Bob", parkedRelease, facts, 2001);
Check(parked.Slots[0].Pilot == "" && parked.Slots[0].LeaseUntil == 0, "Settled unused pod releases its setup lease");
Check(authority.Apply("Bob", parkedRelease, facts, 2002).Slots[0].Revision == parked.Slots[0].Revision, "Retry after released setup lease remains idempotent");
facts.PlacementSettled = false;
var construction = new FleetAuthority(SharedFleet.New(), _ => { });
var empty = construction.State.Slots[0];
var createLease = construction.Apply("Alice", new FleetMessage { Kind = "Acquire", Number = 1, Epoch = construction.State.Epoch, Request = Guid.NewGuid().ToString("N"), Revision = 0 }, facts, 3000).Slots[0];
var createdPod = FleetWire.Copy(createLease.Pod); createdPod.Created = true; createdPod.PartIds.Add(12); createdPod.OwnPort = 12; createdPod.OriginalParts = 1;
var creation = new FleetMessage { Kind = "Commit", Number = 1, Epoch = construction.State.Epoch, Request = Guid.NewGuid().ToString("N"), Revision = createLease.Revision, Lease = createLease.Lease, Pod = createdPod };
facts.OwnsPodUpdate = false;
Reject(() => construction.Apply("Alice", creation, facts, 3001), "Initial creation also requires actual native vessel ownership");
facts.OwnsPodUpdate = true; construction.Apply("Alice", creation, facts, 3001);
facts.PodMissingConfirmed = false;
var missing = new FleetMessage { Kind = "ObserveMissing", Number = 1, Epoch = construction.State.Epoch, Request = Guid.NewGuid().ToString("N"), Revision = construction.State.Slots[0].Revision };
Reject(() => construction.Apply("Bob", missing, facts, 3002), "A client sync gap cannot declare another player's pod destroyed");
facts.PodMissingConfirmed = true;
Check(construction.Apply("Bob", missing, facts, 3025).Slots[0].Pod.Phase == FlightPhase.Lost, "Only authoritative sustained missing parts consume the original slot");
var tamperedSnapshot = authority.Snapshot(); tamperedSnapshot.Slots.Clear();
Check(authority.State.Slots.Count == 2, "A caller cannot mutate authority through a returned snapshot");
var packet = FleetWire.Encode(new FleetMessage { Kind = "State", Fleet = authority.Snapshot() });
Check(FleetWire.Decode(packet, packet.Length).Fleet!.Slots.Count == 2, "Network round-trip preserves the complete two-pod state");
bool oversized = false; try { FleetWire.Decode(new byte[FleetWire.MaxBytes + 1], FleetWire.MaxBytes + 1); } catch (InvalidDataException) { oversized = true; }
Check(oversized, "Oversized fleet packets are rejected before parsing");
bool dtd = false; try { FleetWire.Read<FleetMessage>("<!DOCTYPE a [<!ENTITY x SYSTEM 'file:///private'>]><FleetMessage>&x;</FleetMessage>"); } catch { dtd = true; }
Check(dtd, "External XML entities are disabled");
var failing = new FleetAuthority(fleet, _ => throw new IOException("disk unavailable"));
var failedRequest = FleetWire.Copy(a); failedRequest.Epoch = failing.State.Epoch;
bool failedWrite = false; try { failing.Apply("Alice", failedRequest, facts, 1000); } catch (IOException) { failedWrite = true; }
Check(failedWrite && failing.State.Slots[0].Revision == 0, "Failed storage never commits a volatile reservation");
bool stopped = false; try { failing.Apply("Alice", failedRequest, facts, 1001); } catch (IOException) { stopped = true; }
Check(stopped, "Storage failure stops subsequent grants");
Console.WriteLine($"PASS {count} shared-fleet authority checks. Native multiplayer flight is not established by these checks.");
