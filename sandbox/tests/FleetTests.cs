using System;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using RailgunSandbox;

int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
void Reject(Action action, string message) { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } Check(rejected, message); }
void RejectInvalidFleet(Action action, string message) { bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; } Check(rejected, message); }
FleetState New() { var s = FleetState.New(); foreach (var p in s.Pods) { p.Created = true; p.OriginalParts = 20; } return s; }
void Book(FleetState s, int index = 0, uint port = 2) => FlightRules.Reserve(s, s.Pods[index], 1, port, "Moving station", "MonoPropellant", 100, 120, 1000);

var state = New(); state.Validate(); Check(state.Pods.Count == 2, "Two pods only");
Check(state.Pods.Select(p => p.VesselId).Distinct().Count() == 2, "Stable unique vessels");
Book(state); Reject(() => Book(state), "Reject busy pod");
Reject(() => Book(state, 1), "Do not send two pods to the same port");
Book(state, 1, 3); Check(state.Pods.All(p => p.Phase == FlightPhase.Reserved), "Both slots can be reserved");
Reject(() => FlightRules.Advance(state.Pods[0], FlightPhase.Ready, 2000), "A long wait cannot release a pod");
Reject(() => FlightRules.Advance(state.Pods[0], FlightPhase.Refitting, 2000), "No skipped return and landing");
var xml = new XmlSerializer(typeof(FleetState)); using var buffer = new MemoryStream();
xml.Serialize(buffer, state); buffer.Position = 0; var restored = (FleetState)xml.Deserialize(buffer)!; restored.Validate();
Check(restored.Pods[0].VesselId == state.Pods[0].VesselId && restored.Pods.All(FlightRules.Busy), "Reload retains reservation and exact identities");
var pod = restored.Pods[0]; double now = 1001;
foreach (var phase in new[] { FlightPhase.RailLaunch, FlightPhase.Ascent, FlightPhase.Rendezvous, FlightPhase.Docking, FlightPhase.Unloading })
    FlightRules.Advance(pod, phase, now++);
Reject(() => FlightRules.Advance(pod, FlightPhase.Undocking, now), "No departure before transfer receipt");
pod.CargoTransferred = true;
foreach (var phase in new[] { FlightPhase.Undocking, FlightPhase.Returning, FlightPhase.Landing }) FlightRules.Advance(pod, phase, now++);
Check(!FlightRules.LandingEvidence(false, true, 0, 0, 20, 20), "Near pad is not landed");
Check(!FlightRules.LandingEvidence(true, false, 0, 0, 20, 20), "Correct home body required");
Check(!FlightRules.LandingEvidence(true, true, 26, 0, 20, 20), "Landing beside another depot rejected");
Check(!FlightRules.LandingEvidence(true, true, 0, 4, 20, 20), "Fast contact is not successful landing");
Check(!FlightRules.LandingEvidence(true, true, 0, 0, 19, 20), "Damaged craft cannot refit automatically");
Check(!FlightRules.LandingEvidence(true, true, double.NaN, 0, 20, 20), "NaN landing evidence rejected");
Check(FlightRules.LandingEvidence(true, true, 4, .1, 20, 20), "Intact stable native landing accepted");
Check(!FlightRules.LandingEvidence(true, true, 4, .1, 20, 20, 20), "A tipped-over intact vehicle is not a recovered pod");
Check(!FlightRules.LandingEvidence(true, true, 4, .1, 20, 20, double.NaN), "Invalid attitude cannot establish recovery");
Check(FlightRules.LandingEvidence(true, true, 4, .1, 20, 20, 3), "Small upright touchdown angle accepted");
Check(FlightRules.CanSettleWithoutFuel(true, false, .1, 9, .6, 1), "An exhausted intact pod inches above its home pad may settle naturally");
Check(!FlightRules.CanSettleWithoutFuel(true, false, 20, 9, .6, 1), "Fuel exhaustion at unsafe drop height remains stranded");
Check(!FlightRules.CanSettleWithoutFuel(true, false, .1, 26, .6, 1) && !FlightRules.CanSettleWithoutFuel(true, true, .1, 9, .6, 1), "Settling exception cannot recover an off-pad or splashed pod");
Check(!FlightRules.CanSettleWithoutFuel(true, false, double.NaN, 9, .6, 1) && !FlightRules.CanSettleWithoutFuel(true, false, .1, 9, 6, 1), "Invalid or fast settling cannot bypass a failed flight");
Check(!FlightRules.StableLanding(pod, true, now), "Landing needs stability interval");
Check(!FlightRules.StableLanding(pod, true, now + 4.99), "No early landing completion");
Check(!FlightRules.StableLanding(pod, false, now + 5), "Bounce resets stability");
Check(!FlightRules.StableLanding(pod, true, now + 6), "Restart stability after bounce");
Check(FlightRules.StableLanding(pod, true, now + 11), "Stable touchdown can proceed");
FlightRules.Advance(pod, FlightPhase.Refitting, now + 12); Check(FlightRules.Busy(pod), "Refitting still occupies pod");
FlightRules.Advance(pod, FlightPhase.Ready, now + 13); Check(pod.FlightsCompleted == 1 && pod.OrderId == "", "Same pod returns to service");
Book(New());
Reject(() => FlightRules.Advance(pod, FlightPhase.Reserved, now - 100), "Backward time rejected");
Check(FlightRules.DockingEvidence(true, true, 2, 2, true, true), "Physical reciprocal dock accepted");
Check(!FlightRules.DockingEvidence(true, false, 2, 2, true, true), "One-sided dock rejected");
Check(!FlightRules.DockingEvidence(true, true, 4, 2, true, true), "Wrong port rejected");
Check(!FlightRules.DockingEvidence(true, true, 2, 2, false, true), "Uncoupled vessels rejected");
Check(!FlightRules.DockingEvidence(true, true, 2, 2, true, false), "Wrong receiving ship rejected");
foreach (var quantity in new[] { double.NaN, double.PositiveInfinity, -1, 0, 121 }) {
    var s = New(); Reject(() => FlightRules.Reserve(s, s.Pods[0], 1, 2, "Target", "MonoPropellant", quantity, 120, 10), "Invalid cargo rejected");
}
var lost = New().Pods[0]; FlightRules.Advance(lost, FlightPhase.Lost, 500);
Reject(() => FlightRules.Advance(lost, FlightPhase.Ready, 1e10), "Lost pod is never silently replaced");
Check(FlightRules.Duration(315576000).StartsWith("10.00"), "Ten-year trip is not clamped to days/minutes");
var allocation = FlightRules.CargoAllocation(100, 100, new double[] { 60, 0 }, new double[] { 100, 60 });
Check(allocation.SequenceEqual(new double[] { 40, 60 }) && allocation.Sum() == 100, "Physical transfer conserves cargo across tanks");
Check(FlightRules.CargoAllocation(100, 100, new double[] { 100 }, new double[] { 100 }) == null, "Full receiver cannot unload");
Check(FlightRules.CargoAllocation(100, 100, new double[] { 0 }, new double[] { 99 }) == null, "Do not partially unload before enough capacity exists");
Reject(() => FlightRules.CargoAllocation(100, 99, new double[] { 0 }, new double[] { 120 }), "No unloading missing physical cargo");
Reject(() => FlightRules.CargoAllocation(100, double.NaN, new double[] { 0 }, new double[] { 120 }), "NaN source cannot authorize delivery");
Reject(() => FlightRules.CargoAllocation(100, 100, new double[] { double.NaN }, new double[] { 120 }), "NaN receiver cannot authorize delivery");
Reject(() => FlightRules.CargoAllocation(100, 100, new double[] { 0 }, new double[] { double.PositiveInfinity }), "Infinite capacity rejected");
Reject(() => FlightRules.CargoAllocation(100, 100, new double[] { 121 }, new double[] { 120 }), "Overfull tank state rejected");
var strandedFleet = New(); Book(strandedFleet); var stranded = strandedFleet.Pods[0];
FlightRules.Advance(stranded, FlightPhase.RailLaunch, 1001); FlightRules.Advance(stranded, FlightPhase.Ascent, 1002);
FlightRules.Advance(stranded, FlightPhase.Stranded, 1003);
Check(!FlightRules.Busy(stranded), "Exhausted propulsion does not block the second pod's flight control");
Reject(() => FlightRules.Reserve(strandedFleet, stranded, 1, 2, "Target", "MonoPropellant", 100, 120, 2000), "Stranded pod cannot be booked again");
Reject(() => FlightRules.Advance(stranded, FlightPhase.Ready, 1e10), "Waiting cannot recover stranded pod");
using var strandedBuffer = new MemoryStream(); xml.Serialize(strandedBuffer, strandedFleet); strandedBuffer.Position = 0;
var strandedReload = (FleetState)xml.Deserialize(strandedBuffer)!; strandedReload.Validate();
Check(strandedReload.Pods[0].Phase == FlightPhase.Stranded && strandedReload.Pods[0].OrderId == stranded.OrderId, "Reload preserves stranded state and undelivered order");
FlightRules.Advance(stranded, FlightPhase.Lost, 1004); Check(stranded.Phase == FlightPhase.Lost, "Stranded pod destruction remains permanent");
Check(FlightRules.HasMainPropellant(1, 1) && !FlightRules.HasMainPropellant(0, 100) && !FlightRules.HasMainPropellant(100, 0), "Either exhausted propellant prevents powered flight");
Check(!FlightRules.HasMainPropellant(double.NaN, 1) && !FlightRules.HasMainPropellant(1, double.PositiveInfinity), "Invalid propellant telemetry rejected");
Check(!FlightRules.RequiresMainPropellant(FlightPhase.Docking) && !FlightRules.RequiresMainPropellant(FlightPhase.Unloading), "Main-engine exhaustion does not interrupt RCS docking or physical transfer");
var recoveryFleet = New(); Book(recoveryFleet); var recovery = recoveryFleet.Pods[0]; double recoveryNow = 1001;
foreach (var phase in new[] { FlightPhase.RailLaunch, FlightPhase.Ascent, FlightPhase.Rendezvous }) FlightRules.Advance(recovery, phase, recoveryNow++);
Reject(() => FlightRules.BeginRecoveryTest(recovery, false, 2000), "Production save cannot start a return-only test");
Reject(() => FlightRules.BeginRecoveryTest(recovery, true, 0), "Recovery test cannot rewind the flight clock");
Check(!recovery.RecoveryTest && recovery.Phase == FlightPhase.Rendezvous, "Rejected tests leave the original mission unchanged");
FlightRules.BeginRecoveryTest(recovery, true, 2000);
Check(Guid.TryParseExact(recovery.RecoveryTestRunId, "N", out _), "Each recovery receives a native-run identity so receipts cannot be reused across replayed fixtures");
Check(recovery.RecoveryTest && recovery.Phase == FlightPhase.Landing && !recovery.CargoTransferred && recovery.CargoUnits == 100, "Isolated recovery uses the real undelivered cargo, without a fake receipt");
Reject(() => FlightRules.Advance(recovery, FlightPhase.Refitting, 2001), "Return-only test cannot earn refuelling or mission credit");
using var recoveryBuffer = new MemoryStream(); xml.Serialize(recoveryBuffer, recoveryFleet); recoveryBuffer.Position = 0;
Check(((FleetState)xml.Deserialize(recoveryBuffer)!).Pods[0].RecoveryTest, "Reload preserves the return-only test marker");
FlightRules.Advance(recovery, FlightPhase.RecoveryTestComplete, 2002);
Check(!FlightRules.Busy(recovery) && recovery.FlightsCompleted == 0 && !recovery.CargoTransferred && recovery.OrderId.Length > 0, "Successful recovery test preserves undelivered order and credits zero logistics flights");
Reject(() => FlightRules.Advance(recovery, FlightPhase.Ready, 3000), "Completed test cannot silently re-enter normal service");
Reject(() => Book(recoveryFleet), "Completed test cannot be reserved for cargo");
Reject(() => FlightRules.BeginRecoveryTest(New().Pods[0], true, 1), "A ground pod is not an orbital recovery fixture");
Check(ReentryEnvelope.SpeedCeiling(23000) < 1850, "Entry remains below the observed 2100 m/s failure corridor while using the measured retracted-leg thermal margin");
Check(!FlightRules.HasMainPropellant(.013622751574748848, .016310945308701419), "Native flameout residue is not usable landing propellant");
Check(ReentryEnvelope.Limit(200, 40000) == 200, "Entry cap never relaxes stricter native braking");
Check(ReentryEnvelope.SpeedCeiling(100000) == double.MaxValue, "Orbital planning retains the native speed limit");
bool positiveEnvelope = true, monotoneEnvelope = true; double previousCap = 0;
for (double altitude = 0; altitude <= 80000; altitude += 25) {
    double cap = ReentryEnvelope.SpeedCeiling(altitude);
    positiveEnvelope &= FlightRules.Finite(cap) && cap > 0;
    monotoneEnvelope &= cap >= previousCap; previousCap = cap;
}
Check(positiveEnvelope, "Descent envelope stays finite and positive through the atmospheric domain");
Check(monotoneEnvelope, "Allowed entry speed never increases while descending through an envelope interval");
Check(ReentryEnvelope.Limit(0, -10) == 0, "Ground stopping request is preserved below the datum");
Reject(() => ReentryEnvelope.SpeedCeiling(double.NaN), "Unknown altitude is not a valid guidance input");
Reject(() => ReentryEnvelope.SpeedCeiling(double.PositiveInfinity), "Infinite altitude is not a valid guidance input");
Check(double.IsNaN(ReentryEnvelope.Limit(double.NaN, 20000)), "Native policy errors are not silently replaced by plausible guidance");
double terminalLimit = ReentryEnvelope.TerminalSpeedCeiling(1000, 60, 9.81);
Check(terminalLimit * terminalLimit / (2 * (.85 * 60 - 9.81)) < 985, "Terminal speed leaves a physical stopping-distance reserve");
Check(ReentryEnvelope.TerminalSpeedCeiling(500, 60, 9.81) < terminalLimit, "Approaching terrain tightens the braking target");
Check(ReentryEnvelope.TerminalSpeedCeiling(1000, 40, 9.81) < terminalLimit, "Lower thrust capability requires earlier braking");
Check(ReentryEnvelope.TerminalSpeedCeiling(1000, 60, 15) < terminalLimit, "Stronger gravity reduces the safe descent speed");
Check(ReentryEnvelope.TerminalSpeedCeiling(0, 60, 9.81) == .4, "Touchdown target permits contact instead of hovering indefinitely");
Check(ReentryEnvelope.TerminalSpeedCeiling(1000, 0, 9.81) == .4, "Unavailable propulsion does not produce a fictitious high-speed allowance");
Reject(() => ReentryEnvelope.TerminalSpeedCeiling(double.NaN, 60, 9.81), "Unknown terrain clearance cannot become plausible braking data");
Reject(() => ReentryEnvelope.TerminalSpeedCeiling(1000, -1, 9.81), "Negative available acceleration rejected");
Check(RailLaunch.OnGuide(0, 0) && RailLaunch.OnGuide(RailLaunch.Length - .01, 2), "Drive applies within the actual guide");
Check(!RailLaunch.OnGuide(RailLaunch.Length, 0) && !RailLaunch.OnGuide(RailLaunch.Length + 10, 0), "No launch force at or beyond the muzzle");
Check(!RailLaunch.OnGuide(0, -1), "Invalid negative lateral distance cannot establish rail capture");
Check(!RailLaunch.OnGuide(10, 4) && !RailLaunch.OnGuide(-5, 0), "No drive outside physical guide capture");
Check(!RailLaunch.OnGuide(double.NaN, 0), "Invalid rail position rejected");
Check(FlightRules.DevelopmentSave(new[] { "KSP_x64.exe", "-railgun-railonly" }) == "Railgun-RailOnly-Test", "Explicit rail-only startup does not depend on Luna's previously initialized save name");
Check(FlightRules.DevelopmentSave(new[] { "-railgun-reentry", "-railgun-pad-guidance" }) == "Railgun-Reentry-Test", "Recovery flags retain the isolated return fixture");
Check(FlightRules.DevelopmentSave(new[] { "KSP_x64.exe" }) == null, "Ordinary startup selects no development save");
Check(FlightRules.DevelopmentSave(new[] { "-railgun-porttest", "-railgun-porttest" }) == "Railgun-Port-Selection-Test", "Repeated identical startup mode is unambiguous");
Reject(() => FlightRules.DevelopmentSave(new[] { "-railgun-porttest", "-railgun-railonly" }), "Conflicting isolated modes cannot silently load another fixture");
Check(RailLaunch.Captured(.03685, .99017, .32769, .03), "Measured intact Junior cradle sample admits capture within the running guide");
Check(RailLaunch.Captured(-.7, 1.25, .99, .99), "Capture accepts its bounded position margins with safe speed and attitude");
Check(!RailLaunch.Captured(.71, .1, .1, .1) && !RailLaunch.Captured(-.71, .1, .1, .1), "Capture rejects both excessive along-guide offsets");
Check(!RailLaunch.Captured(0, 1.26, .1, .1) && !RailLaunch.Captured(0, -1, .1, .1), "Capture does not relax the running guide's lateral safety boundary");
Check(!RailLaunch.Captured(0, 0, 1, 0) && !RailLaunch.Captured(0, 0, -.1, 0), "Capture retains the native velocity interlock");
Check(!RailLaunch.Captured(0, 0, 0, 1) && !RailLaunch.Captured(0, 0, 0, -1), "Capture retains the native attitude interlock");
Check(!RailLaunch.Captured(double.NaN, 0, 0, 0) && !RailLaunch.Captured(0, double.NaN, 0, 0) &&
    !RailLaunch.Captured(0, 0, double.NaN, 0) && !RailLaunch.Captured(0, 0, 0, double.NaN), "Missing cradle measurements never admit a launch");
Check(RailLaunch.DriveAcceleration(RailLaunch.MuzzleSpeed, .02, 3) == 0, "No acceleration above configured muzzle speed");
Check(RailLaunch.DriveAcceleration(RailLaunch.MuzzleSpeed - 1, .02, 3) <= 50, "Last physics step cannot exceed speed budget");
Reject(() => RailLaunch.DriveAcceleration(0, 0, 3), "Zero physics timestep rejected");
Reject(() => RailLaunch.DriveAcceleration(0, double.NaN, 3), "Invalid physics timestep rejected");
Check(!RailLaunch.BurnWindow(60000, 70000, 5, 4), "Circularization cannot ignite inside atmosphere");
Check(!RailLaunch.BurnWindow(110000, 70000, 100, 4), "No powered ascent while coasting toward apoapsis");
Check(RailLaunch.BurnWindow(110000, 70000, 8, 4), "Native burn may begin near apoapsis in vacuum");
Check(!RailLaunch.BurnWindow(110000, 70000, -50, 4), "Missed burn window rejected");
Check(!RailLaunch.BurnWindow(double.NaN, 70000, 5, 4), "Invalid altitude cannot unlock thrust");
var launchPod = New().Pods[0]; launchPod.RailOnlyAscent = true; launchPod.Phase = FlightPhase.RailLaunch;
Check(RailLaunch.EnginesLocked(launchPod), "Engines interlocked during rail acceleration");
launchPod.Phase = FlightPhase.Ascent;
Check(RailLaunch.EnginesLocked(launchPod), "Engines interlocked during coast");
launchPod.CircularizationPlanned = true;
Check(RailLaunch.EnginesLocked(launchPod), "Planning a maneuver does not permit an early burn");
launchPod.CircularizationStarted = true;
Check(!RailLaunch.EnginesLocked(launchPod), "Only the admitted orbital correction unlocks engines");
Check(DockingPortPolicy.SelectType("size0", false, false) == "size0", "Junior target admits Junior coupling");
Check(DockingPortPolicy.SelectType("size1", false, false) == "size1", "Standard target admits standard coupling");
Check(DockingPortPolicy.SelectType("size2", false, false) == "size2", "Sr target admits Sr coupling");
Check(DockingPortPolicy.SelectType("other, size2", false, false) == "size2", "Multiple-type native ports use a real shared type");
Check(DockingPortPolicy.SelectType("size0,size1,size2", false, false) == "size1", "Universal receivers use a deterministic compatible type");
Check(DockingPortPolicy.SelectType("size1p5", false, false, new[] { "size1", "size1p5" }) == "size1p5", "Installed Junior interface overrides are read rather than guessed from the stock name");
Reject(() => DockingPortPolicy.SelectType("size0", false, false, new[] { "size1p5" }), "A missing stock interface is not invented when installed definitions differ");
Reject(() => DockingPortPolicy.ValidateToken("size1,size2"), "Persisted adapter stores one admitted interface, not an unvalidated wildcard");
Reject(() => DockingPortPolicy.ValidateToken(""), "Empty native interface cannot be restored");
Reject(() => DockingPortPolicy.ValidateToken("size1\n"), "Malformed native interface cannot be restored");
Reject(() => DockingPortPolicy.SelectType("customPort", false, false), "Unknown mod connector is not silently treated as standard");
Reject(() => DockingPortPolicy.SelectType(null, false, false), "Missing connector type rejected");
Reject(() => DockingPortPolicy.SelectType("size1", true, false), "Gendered ports cannot be coerced into ungendered coupling");
Reject(() => DockingPortPolicy.SelectType("size1", false, true), "Indexed ports cannot be coerced into an incompatible rotation");
foreach (var hardware in new[] { "dockingPort2", "dockingPort3", "dockingPortLarge" }) DockingPortPolicy.ValidateHardware(hardware);
Reject(() => DockingPortPolicy.ValidateHardware("dockingPort1"), "Shielded models cannot silently use an unshielded imitation");
Reject(() => DockingPortPolicy.ValidateHardware("universalModPort"), "Unsupported actual hardware cannot be replaced by metadata spoofing");
var selectedFleet = New(); selectedFleet.SelectedReceiverAnchor = 123; selectedFleet.SelectedReceiverPort = 456;
selectedFleet.Pods[0].DockingHardwareConfig = "native hardware and scaler configuration";
Book(selectedFleet, 0, 456);
Check(selectedFleet.Pods[0].TargetPort == 456, "Reservation retains exact requested port id");
Check(!selectedFleet.Pods[0].RailOnlyAscent, "Paused experimental rail launch is not enabled in ordinary reservations");
using var selectedBuffer = new MemoryStream(); xml.Serialize(selectedBuffer, selectedFleet); selectedBuffer.Position = 0;
var selectedReload = (FleetState)xml.Deserialize(selectedBuffer)!;
Check(selectedReload.SelectedReceiverAnchor == 123 && selectedReload.SelectedReceiverPort == 456 && selectedReload.Pods[0].TargetPort == 456,
    "Exact selected receiver and order survive paired-state reload");
Check(selectedReload.Pods[0].DockingHardwareConfig == selectedFleet.Pods[0].DockingHardwareConfig, "Exact hardware fitting survives order reload");
launchPod.RailOnlyAscent = false; launchPod.CircularizationStarted = false;
Check(!RailLaunch.EnginesLocked(launchPod), "Normal powered ascent is not disabled by a paused experiment");
// ETA is advisory and must never credit a delivery or keep an obsolete countdown alive.
var etaPod = New().Pods[0];
Check(DeliveryEta.State(etaPod, true).Message == "No active delivery", "A parked pod has no delivery countdown");
etaPod.Phase = FlightPhase.Ascent;
Check(!DeliveryEta.State(etaPod, true).Available, "Ascent does not invent an orbital intercept time");
etaPod.Phase = FlightPhase.Rendezvous;
Check(DeliveryEta.State(etaPod, false).Message.Contains("paused"), "Loss of guidance invalidates ETA");
var eta = DeliveryEta.Encounter(1000, 1600, 100, 1, "Native planned encounter");
Check(eta.Available && eta.EarliestUt > 1600 && eta.LatestUt > eta.EarliestUt, "ETA includes approach and capture after the encounter");
Check(eta.Display(1000) == "Delivery ETA: ~12m–15m" && eta.Display(1060) == "Delivery ETA: ~11m–14m", "Encounter forecast counts down in KSP UT and includes docking uncertainty");
Check(eta.Display(999).Contains("recalculating") && eta.Display(eta.LatestUt + 1).Contains("recalculating"), "Rewind or missed window invalidates prediction");
Check(DeliveryEta.Encounter(1000, 999, 100, 1, "test").Available == false, "Past encounter cannot produce an ETA");
Check(!DeliveryEta.Encounter(1000, 1600, 5000, 1, "test").Available, "A distant fly-by is not a delivery encounter");
Check(!DeliveryEta.Encounter(1000, double.NaN, 100, 1, "test").Available, "Invalid orbital prediction has no countdown");
Check(!DeliveryEta.Encounter(1000, 1600, 100, 0, "test").Available, "Unlimited or missing docking-speed policy cannot fabricate a duration");
Check(DeliveryEta.Encounter(1000, 1600, 100, .1, "test").EarliestUt > eta.EarliestUt, "Slower configured docking increases predicted approach time");
Check(DeliveryEta.Rendezvous(1000, 1600, 20000, 10, 1, "test").EarliestUt > 3600, "A 20 km encounter includes native-speed intercept travel before docking");
Check(!DeliveryEta.Rendezvous(1000, 1600, double.NaN, 10, 1, "test").Available, "Invalid closest-approach miss invalidates ETA");
Check(!DeliveryEta.Rendezvous(1000, 1600, 20000, 0, 1, "test").Available, "Missing closing-speed policy cannot generate intercept ETA");
etaPod.CargoTransferred = true;
Check(DeliveryEta.State(etaPod, false).Message == "Supplies delivered", "Committed cargo receipt remains delivered during return or pause");
etaPod.CargoTransferred = false; etaPod.Phase = FlightPhase.Stranded;
Check(DeliveryEta.State(etaPod, true).Message.Contains("interrupted"), "Failed delivery stops the estimate");
Check(DeliveryEta.Short(315576000) == "10.0y", "Long real journeys are not capped to a short countdown");
Check(etaPod.Phase == FlightPhase.Stranded && !etaPod.CargoTransferred && etaPod.FlightsCompleted == 0, "ETA calculation cannot advance a mission or create delivery credit");
var padCommand = TerminalPadGuidance.Compute(100, 0, 0, 0, 100, -3, 9.81, 30);
Check(padCommand.Active && padCommand.AccelerationX > 0 && padCommand.AccelerationZ == 0 && padCommand.SlowDescent, "Terminal request points toward the assigned pad and allows time to align");
var padBraking = TerminalPadGuidance.Compute(0, 0, 5, -5, 100, -3, 9.81, 30);
Check(padBraking.AccelerationX < 0 && padBraking.AccelerationZ > 0, "Terminal correction opposes lateral drift instead of merely aiming at the pad");
var padLow = TerminalPadGuidance.Compute(100, 100, -10, -10, 5, -1, 9.81, 30);
Check(Math.Sqrt(padLow.AccelerationX * padLow.AccelerationX + padLow.AccelerationZ * padLow.AccelerationZ) <= 9.81 * Math.Tan(4 * Math.PI / 180) + 1e-9, "Final metres retain a four-degree tilt ceiling");
Check(!TerminalPadGuidance.Compute(1, 1, 0, 0, 8000, -3, 9.81, 30).Active, "Terminal stage cannot take over high-altitude entry");
Check(!TerminalPadGuidance.Compute(1, 1, 0, 0, 100, -200, 9.81, 30).Active, "Native emergency braking retains control of a fast descent");
Check(!TerminalPadGuidance.Compute(1, 1, 0, 0, 100, -3, 9.81, 9).Active, "Insufficient thrust reserve cannot request terminal steering");
Check(!TerminalPadGuidance.Compute(1, 1, 0, 0, double.NaN, -3, 9.81, 30).Active, "Invalid clearance cannot steer a live vessel");
Check(!TerminalPadGuidance.Compute(20001, 1, 0, 0, 100, -3, 9.81, 30).Active, "A gross landing miss is not hidden with an unlimited translation command");
var padApproach = TerminalPadGuidance.Compute(2000, 0, 20, 0, 2000, -50, 9.81, 30);
Check(padApproach.Active && padApproach.MaximumDescentSpeed > 2 && padApproach.MaximumDescentSpeed < 40, "Kilometre-scale final approach budgets descent time for real lateral travel");
Check(TerminalPadGuidance.Compute(4000, 0, 100, 0, 4000, -250, 9.81, 40).Active, "Early pad approach can brake a fast descent when measured thrust and clearance provide a stopping margin");
Check(!TerminalPadGuidance.Compute(4000, 0, 100, 0, 1000, -250, 9.81, 30).Active, "An inadequate vertical stopping margin leaves emergency braking in native control");
var padAligned = TerminalPadGuidance.Compute(1, 1, 0, 0, 10, -1, 9.81, 30);
var padNear = TerminalPadGuidance.Compute(31, 0, .7, 0, 240, -2, 9.81, 60);
Check(padNear.Active && padNear.SlowDescent && padNear.MaximumDescentSpeed > 8 && padNear.MaximumDescentSpeed <= 12, "Near-pad correction does not spend two minutes hovering 240 metres above touchdown");
var padFarLow = TerminalPadGuidance.Compute(1000, 0, 20, 0, 240, -2, 9.81, 60);
Check(padFarLow.MaximumDescentSpeed < padNear.MaximumDescentSpeed, "A large low-altitude miss keeps more time for lateral correction");
Check(padAligned.Active && !padAligned.SlowDescent, "An aligned settled approach retains native descent timing");
var loader = new LoadingSequence();
Check(loader.Update("order-a",100,false).Stage == LoadingStage.Aligning, "A remote pod cannot start closing the cradle");
Check(loader.Update("order-a",100.25,true).Stage == LoadingStage.Capturing, "First capture sample starts a fresh motion check");
for(int i=1;i<=57;i++) {
    var sample=loader.Update("order-a",100.25+i*.25,true);
    Check(!sample.LaunchClear, "No launch before the complete clearance sequence");
    Check(sample.Clamps>=0 && sample.Clamps<=1 && sample.Pins>=0 && sample.Pins<=1, "Actuator travel remains bounded");
    if(sample.Stage==LoadingStage.Locking || sample.Stage==LoadingStage.Charging || sample.Stage==LoadingStage.Unlocking)
        Check(sample.Clamps==1, "Collars remain closed while locking pins engage or retract");
}
var clear=loader.Update("order-a",100.25+LoadingSequence.Duration,true);
Check(clear.LaunchClear && clear.Clamps==0 && clear.Pins==0 && clear.Charge==1, "Launch is permitted only with collars open, pins withdrawn and charge ready");
Check(!loader.Update("order-b",115,true).LaunchClear, "A new order cannot inherit launch clearance");
for(int i=1;i<=24;i++)loader.Update("order-b",115+i*.25,true);
Check(loader.Pose.Stage==LoadingStage.Locking, "Sequence reaches measured pin engagement");
Check(loader.Update("order-b",121.25,false).Stage==LoadingStage.Aligning && loader.Pose.Pins==0, "Lost alignment cancels pin engagement and launch permission");
loader.Update("order-b",121.5,true);
Check(loader.Update("order-b",121.5,true).Elapsed==0, "A paused native clock cannot advance the mechanism");
loader.Update("order-b",121.75,true);
Check(loader.Update("order-b",130,true).Elapsed==0, "Missing capture samples cannot complete a loading animation in the background");
Check(loader.Update("order-b",129,true).Elapsed==0, "A backward clock requires a new capture window");
Check(loader.Update("order-b",double.NaN,true).Stage==LoadingStage.Aligning, "An invalid clock resets loading and blocks launch");
loader.Update("order-b",140,true);loader.Update("order-b",140.25,true);loader.Reset();
Check(loader.Pose.Stage==LoadingStage.Aligning && !loader.Pose.LaunchClear, "Scene reset cannot reuse a completed gate");
foreach(double rate in new[]{30.0,60.0,144.0}) {
    var timed=new LoadingSequence();double time=0;
    bool neverEarly=true;
    timed.Update("repeat",time,true);
    while(time+1/rate<LoadingSequence.Duration) {time+=1/rate;neverEarly &= !timed.Update("repeat",time,true).LaunchClear;}
    Check(neverEarly,"Render cadence cannot shorten the loading interlock");
    Check(timed.Update("repeat",LoadingSequence.Duration,true).LaunchClear,"Loading finishes at the same native game time for every update cadence");
}
Check(FlightRules.DevelopmentSave(new[] { "-railgun-shell" }) == "Railgun-Shell-Test", "Shell experiment uses its own save");
Reject(() => FlightRules.DevelopmentSave(new[] { "-railgun-shell", "-railgun-railonly" }), "Shell mode cannot silently select another fixture");
Check(!LaunchShellPolicy.MayOpen(FlightPhase.RailLaunch, 55000, 900, 110000), "Shell remains closed inside rail phase");
Check(!LaunchShellPolicy.MayOpen(FlightPhase.Ascent, 49999, 900, 110000), "Shell remains closed below requested 50 km");
Check(!LaunchShellPolicy.MayOpen(FlightPhase.Ascent, 55000, 900, RailLaunch.Length), "Shell waits for nose and panels to clear muzzle");
Check(!LaunchShellPolicy.MayOpen(FlightPhase.Ascent, 55000, -1, 110000), "Shell does not deploy on a failed descending launch");
Check(LaunchShellPolicy.MayOpen(FlightPhase.Ascent, 50000, 900, RailLaunch.Length + 30), "Shell deploys on ascending 50 km flight beyond rail");
foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity }) {
    Check(!LaunchShellPolicy.MayOpen(FlightPhase.Ascent, invalid, 900, 110000), "Invalid altitude cannot authorize jettison");
    Check(!LaunchShellPolicy.MayOpen(FlightPhase.Ascent, 55000, invalid, 110000), "Invalid ascent cannot authorize jettison");
    Check(!LaunchShellPolicy.MayOpen(FlightPhase.Ascent, 55000, 900, invalid), "Invalid rail position cannot authorize jettison");
    Check(!LaunchShellPolicy.MayBurn(true, false, 100, invalid), "Invalid clock cannot authorize ignition");
}
Check(!LaunchShellPolicy.MayBurn(true, true, 100, 200), "Attached base blocks ignition even after recorded clearance");
Check(!LaunchShellPolicy.MayBurn(true, false, -1, 200), "Missing shell is not successful separation evidence");
Check(!LaunchShellPolicy.MayBurn(true, false, 100, 102.99), "Engine waits full base clearance delay");
Check(!LaunchShellPolicy.MayBurn(true, false, 100, 99), "Backwards clock cannot bypass clearance");
Check(LaunchShellPolicy.MayBurn(true, false, 100, 103), "Engine clearance permits later native circularization");
Check(LaunchShellPolicy.MayBurn(false, true, -1, 0), "Existing shell-free missions retain original engine behavior");
var shellFleet = FleetState.New();
var shellPod = shellFleet.Pods[0]; shellPod.LaunchShellRequired = true;
RejectInvalidFleet(() => shellFleet.Validate(), "Shell-required save cannot omit its physical part identity");
shellPod.LaunchShellPartId = 42; shellFleet.Validate();
shellPod.LaunchShellBaseReleased = true;
RejectInvalidFleet(() => shellFleet.Validate(), "Base release without verified panel deployment is rejected");
shellPod.LaunchShellPanelsReleased = true;
RejectInvalidFleet(() => shellFleet.Validate(), "Deployment requires its recorded native command time");
shellPod.LaunchShellOpenedAt = 100; shellPod.LaunchShellClearAt = 99;
RejectInvalidFleet(() => shellFleet.Validate(), "Clearance cannot predate deployment");
shellPod.LaunchShellClearAt = 102;
using (var shellBuffer = new MemoryStream()) {
    xml.Serialize(shellBuffer, shellFleet); shellBuffer.Position = 0;
    var shellRestored = (FleetState)xml.Deserialize(shellBuffer)!; shellRestored.Validate();
    Check(shellRestored.Pods[0].LaunchShellPartId == 42 && shellRestored.Pods[0].LaunchShellPanelsReleased && shellRestored.Pods[0].LaunchShellBaseReleased && shellRestored.Pods[0].LaunchShellClearAt == 102,
        "Reload preserves physical shell identity and verified separation state");
}
var badShellBurn = new PodRecord { Phase = FlightPhase.Ascent, RailOnlyAscent = true, CircularizationStarted = true, LaunchShellRequired = true };
Check(RailLaunch.EnginesLocked(badShellBurn), "Stale circularization flag cannot ignite an attached closed shell");
var shellLoading=new LoadingSequence(true);
shellLoading.Update("shell-load",0,true);
for(int i=1;i<LoadingSequence.ShellDuration*20;i++) {
    double t=i*.05;var shellPose=shellLoading.Update("shell-load",t,true);
    Check(!shellPose.LaunchClear,"Shell carrier cannot authorize a shortened launch sequence");
    if(shellPose.CarrierGrip<1) Check(shellPose.Clamps==1 && shellPose.Pins==1 || shellPose.Stage>=LoadingStage.Unlocking,"Transport grip opens only after the cradle locks hold the shell");
    if(shellPose.Charge>0) Check(shellPose.CarrierRetraction==1 && shellPose.CarrierGrip==0,"Charging waits for the carrier to withdraw fully");
    if(shellPose.Stage==LoadingStage.ReleasingCarrier || shellPose.Stage==LoadingStage.RetractingCarrier)
        Check(shellPose.Pins==1 && shellPose.Clamps==1,"Rail locks retain the shell throughout carrier handover");
}
Check(shellLoading.Update("shell-load",LoadingSequence.ShellDuration,true).LaunchClear,"Complete shell sequence clears transport carrier, pins and collars");
Check(LoadingPose.At(LoadingSequence.Duration,true).LaunchClear==false,"Legacy loading duration cannot fire the larger shell sequence");
var route=new ShellLoadingRoute();double routeClock=0;
void TransferSample(double x,double y,double z,double speed=0,double angle=60,bool positioned=true) {route.Update("shell-route",routeClock,x,y,z,29,speed,angle,positioned);routeClock+=.02;}
for(int i=0;i<200;i++)TransferSample(-36,15,6,0,60,false);
Check(route.Stage==ShellTransferStage.Gripping && route.Grip==0,"Crane cannot lift before the carrier physically reaches the shell");
for(int i=0;i<105;i++)TransferSample(-36,15,6);
Check(route.Stage==ShellTransferStage.Lifting && route.X==-36 && route.Y==50 && route.Z==6,"Shell lifts vertically before traversing the depot");
for(int i=0;i<200;i++)TransferSample(-36,15,6);
Check(route.Stage==ShellTransferStage.Lifting,"Elapsed time cannot substitute for reaching the lift waypoint");
for(int i=0;i<35;i++)TransferSample(-36,50,6);
Check(route.Stage==ShellTransferStage.Traversing && !route.AlignToRail && route.X==0 && route.Y==50,"Bridge traverses at safe height with the shell upright");
for(int i=0;i<35;i++)TransferSample(0,50,0);
Check(route.Stage==ShellTransferStage.Tilting && route.AlignToRail,"Shell tilts only after reaching the overhead rail position");
for(int i=0;i<100;i++)TransferSample(0,50,0,0,60);
Check(route.Stage==ShellTransferStage.Tilting,"Position alone cannot complete the tilt stage");
for(int i=0;i<35;i++)TransferSample(0,50,0,0,.2);
Check(route.Stage==ShellTransferStage.Seating && route.Y==29,"Aligned shell lowers into the actual rail capture position");
for(int i=0;i<100;i++)TransferSample(0,29,0,2,.2);
Check(!route.Seated,"Fast shell motion cannot complete cradle seating");
for(int i=0;i<35;i++)TransferSample(0,29,0,0,.2);
Check(route.Seated,"Measured position, speed and attitude complete the shell transfer");
Reject(()=>route.Update("bad",double.NaN,0,0,0,29,0,0),"Invalid native telemetry cannot advance a shell transfer");
double GantryFootprintDistance(double degrees,double depth) {
    double angle=degrees*Math.PI/180,minimum=double.PositiveInfinity;
    foreach(int sx in new[]{-1,1})foreach(int sz in new[]{-1,1}) {
        var values=(from x in new[]{38.75,46.7} from z in new[]{depth-.7,depth+.7} select sx*x*Math.Cos(angle)-sz*z*Math.Sin(angle)).ToArray();
        double lo=values.Min(),hi=values.Max();minimum=Math.Min(minimum,lo<=0&&hi>=0?0:Math.Min(Math.Abs(lo),Math.Abs(hi)));
    }
    return minimum;
}
Check(GantryFootprintDistance(87.951063047091779,5)<3.85,"Original runway spacing reproduces the native tower intrusion");
foreach(double bore in new[]{2.15,3.85})for(int halfDegree=0;halfDegree<720;halfDegree++) {
    double angle=halfDegree*.5*Math.PI/180;
    double depth=GantrySafety.HalfDepth(Math.Sin(angle),Math.Cos(angle),bore,bore>3?9:7);
    Check(FlightRules.Finite(depth)&&depth>=7&&depth<=30&&GantryFootprintDistance(halfDegree*.5,depth)>=bore+.05,"Whole tower/brace footprint clears the heading sweep");
}
Check(GantrySafety.HalfDepth(0,0,2.15,7)==7,"Vertical bare guide keeps its standard runway spacing");
Reject(()=>GantrySafety.HalfDepth(double.NaN,1,3.85,9),"Invalid guide direction cannot configure the gantry");
foreach(double rate in new[]{30.0,60.0,144.0}) {
    bool same=true;double sampled=1000,seconds=7.25;
    for(int i=0;i<rate;i++) {
        double age=i/rate;var shown=LoadingPose.FromPilotSample(seconds,sampled,sampled+age);var expected=LoadingPose.At(seconds+age);
        same&=shown.Stage==expected.Stage&&Math.Abs(shown.Clamps-expected.Clamps)<1e-9&&Math.Abs(shown.Pins-expected.Pins)<1e-9&&Math.Abs(shown.Charge-expected.Charge)<1e-9;
    }
    Check(same,"Remote loading uses the pilot's native clock at every render cadence");
}
Check(LoadingPose.FromPilotSample(8,1000,1003).Stage==LoadingStage.Aligning,"A missing pilot sample expires instead of continuing the animation");
Check(LoadingPose.FromPilotSample(-1,1000,1000.1).Stage==LoadingStage.Aligning,"Lost native capture opens the remote loader");
Check(LoadingPose.FromPilotSample(8,1000,998).Stage==LoadingStage.Aligning,"A wrong native clock cannot advance remote loading");
var independentGate=new LoadingSequence();
Check(LoadingPose.FromPilotSample(14,1000,1001).LaunchClear&&!independentGate.Update("independent",1001,true).LaunchClear,"A complete remote animation never unlocks an independent physical launch gate");
Check(FlightRules.UndockingEvidence(true,true,"Disengage","Disengage",31,31,false,false),"Native separated Disengage ports may begin RCS clearance without waiting for otherNode to clear");
Check(FlightRules.UndockingEvidence(true,true,"Ready","",0,31,false,false),"A persisted already-separated native pod may resume departure");
Check(!FlightRules.UndockingEvidence(true,true,"Docked (docker)","Disengage",31,31,false,false),"A docked own port cannot depart");
Check(!FlightRules.UndockingEvidence(true,true,"Disengage","Docked (dockee)",31,31,false,false),"A still-docked reciprocal port cannot depart");
Check(!FlightRules.UndockingEvidence(true,true,"Disengage","Disengage",32,31,false,false),"An unrelated docking partner cannot satisfy departure evidence");
Check(!FlightRules.UndockingEvidence(true,true,"Disengage","Disengage",31,31,true,false)&&!FlightRules.UndockingEvidence(true,true,"Disengage","Disengage",31,31,false,true),"A shared vessel or retained receiver structure blocks departure");
Check(!FlightRules.UndockingEvidence(false,true,"Disengage","Disengage",31,31,false,false)&&!FlightRules.UndockingEvidence(true,false,"Ready","",0,31,false,false),"A missing receipt or missing own port cannot unlock return flight");
Check(AirbrakePolicy.Deploy(FlightPhase.Landing,false,50000,-200,2100,.8,.4),"Cool stock panels may deploy during real atmospheric entry");
Check(AirbrakePolicy.Deploy(FlightPhase.Landing,true,30000,-200,1600,20,.7)&&!AirbrakePolicy.Deploy(FlightPhase.Landing,false,30000,-200,1600,20,.7),"Thermal hysteresis prevents hot panels reopening immediately");
Check(!AirbrakePolicy.Deploy(FlightPhase.Landing,true,30000,-200,1600,20,.8),"Predicted exposed-panel heating folds the native airbrakes");
Check(!AirbrakePolicy.Deploy(FlightPhase.Ascent,false,50000,200,2100,.8,.4),"Ascent never opens recovery airbrakes");
Check(!AirbrakePolicy.Deploy(FlightPhase.Landing,true,1000,-20,200,20,.4),"The terminal landing envelope retracts the panels");
Check(AirbrakePolicy.Deploy(FlightPhase.Landing,true,30000,-200,1600,150,.4),"Dense-air braking uses native panel limits instead of an invented pressure cutoff");
Check(AirbrakePolicy.Deploy(FlightPhase.Landing,false,70000,-200,2100,0,.4),"Return preparation settles the stock flaps before deorbit prediction");
Check(!AirbrakePolicy.Deploy(FlightPhase.Landing,true,30000,-200,1600,20,double.NaN)&&!AirbrakePolicy.Deploy(FlightPhase.Landing,true,30000,-200,1600,20,double.PositiveInfinity),"Missing thermal trend fails closed");
Check(RangeBrakePolicy.Next(70,70,8000,.3)<70&&RangeBrakePolicy.Next(30,70,-8000,.3)>30,"Predicted undershoot reduces native braking; overshoot increases it");
Check(RangeBrakePolicy.Next(30,70,200,.3)==30,"Range deadband holds the native actuator command");
Check(RangeBrakePolicy.Next(0,70,8000,.8)==70,"Predicted body heating overrides range extension");
Check(RangeBrakePolicy.Next(0,70,8000,.3)==0&&RangeBrakePolicy.Next(70,70,-8000,.3)==70,"Range control never exceeds the native deployment span");
bool badRange=false;try { RangeBrakePolicy.Next(30,70,double.NaN,.3); } catch(ArgumentException) { badRange=true; }
Check(badRange,"Missing range evidence cannot become a fabricated actuator request");
var cubic=new AeroCurve(new[]{new AeroKnot(0,0,0,0),new AeroKnot(2,8,12,12),new AeroKnot(5,125,75,75)});
foreach(float x in new[]{0,.125f,.5f,1,1.5f,2,2.25f,3,4,5})Check(Math.Abs(cubic.Evaluate(x)-x*x*x)<.0001,"Frozen nonuniform Hermite curve preserves a known cubic");
Check(cubic.Evaluate(-1)==0&&cubic.Evaluate(6)==125,"Frozen native curve clamps beyond its captured keys");
var step=new AeroCurve(new[]{new AeroKnot(0,2,0,float.PositiveInfinity),new AeroKnot(1,7,float.PositiveInfinity,0)});
Check(step.Evaluate(.9f)==2&&step.Evaluate(1)==7,"Native step tangents retain their endpoint behavior");
bool badCurve=false;try { new AeroCurve(new[]{new AeroKnot(1,0,0,0),new AeroKnot(0,1,0,0)}); } catch(ArgumentException) { badCurve=true; }
Check(badCurve,"Unordered aerodynamic keys are rejected");
bool badInput=false;try { cubic.Evaluate(float.NaN); } catch(ArgumentException) { badInput=true; }
Check(badInput,"Invalid curve input cannot enter the worker forecast");
double approachTime=PoweredApproach.Plan(6800,5100,0,334,-314,0,9.65,71.6);
Check(FlightRules.Finite(approachTime),"Recorded early terminal state has a finite thrust-bounded intercept");
var approachStart=PoweredApproach.Command(6800,5100,0,334,-314,0,9.65,approachTime);
double axEnd=-6*6800/(approachTime*approachTime)+2*334/approachTime;
double ayEnd=6*5100/(approachTime*approachTime)+(2*-314-2)/approachTime+9.65;
double approachDuration=approachTime;
Check(Math.Abs(334*approachDuration+approachStart.X*approachDuration*approachDuration/2+(axEnd-approachStart.X)*approachDuration*approachDuration/6-6800)<1e-6,"Unclipped intercept integrates to the requested horizontal position");
Check(Math.Abs(5100-314*approachDuration+(approachStart.Y-9.65)*approachDuration*approachDuration/2+(ayEnd-approachStart.Y)*approachDuration*approachDuration/6)<1e-6,"Unclipped intercept integrates to ground height");
Check(Math.Abs(-314+(approachStart.Y-9.65)*approachDuration+(ayEnd-approachStart.Y)*approachDuration/2+.5)<1e-6,"Intercept terminal descent speed is half a metre per second");
Check(!FlightRules.Finite(PoweredApproach.Plan(6800,5100,0,334,-314,0,9.65,9)),"Insufficient thrust cannot produce an accepted powered intercept");
Console.WriteLine($"PASS {checks} fleet, docking evidence, landing, cargo, launch, loading and persistence checks. Native flight not established by these checks.");
