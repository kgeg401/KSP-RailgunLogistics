# Native delivery and return validation

This is an acceptance procedure, not a claim that the current candidate has
completed it. The shared friends server and its updater channel remain unchanged
while the private loopback tests run.

## Acceptance flight

Start a private Luna host on loopback port 8817 and two private native KSP
clients with the same pinned client DLL. Preserve the original two pod identities,
part IDs, home pads and unlocked receiver capacity. Freeze the candidate before
dispatch; record its DLL hash. Both slots must begin Ready. Use a declared initial
receiver orbit and the ordinary dispatch action. Do not reposition anything after
dispatch, reload intermediate saves, refill propulsion in flight, or advance time.

`sandbox/tests/observe_roundtrip.py --help` describes the collector. Supply the
exact private paths and three already running process IDs. `--dispatch` submits
one ordinary request from client A's receiving station. Without that flag the
collector is read-only. A resumed or edited close-approach fixture requires
`--diagnostic` and can never receive full-trip acceptance.

The private native addon is built separately from the gameplay DLL. It samples
actual vessels at approximately 0.25-second intervals and captures stock docking
states, part IDs, resources, heat, orientation, physics rate and native flap
rotations. Its cargo observer takes immediate before/after snapshots without
changing transfer arguments, results or exceptions. A separate observer captures
phase changes from actual received server messages, including their server
session identity. Those receipts can preserve a short committed phase missed
by filesystem polling; local proposed client state cannot substitute for them.
Do not distribute native
test snapshots, game assemblies or the private test addon as part of the mod.

## Required evidence

- Ordered Ready, Reserved, RailLaunch, Ascent, Rendezvous, Docking, Unloading,
  Undocking, Returning, Landing, Refitting and Ready stages.
- The same three native processes throughout; monotonic sampled native clocks,
  normal 1x time, 0.02-second physics and the recorded cheat flags disabled.
- The assigned reciprocal stock docking joint, original pod and receiver
  assemblies, and a conserved transfer from locked cargo into unlocked receiver
  tanks. A matching native receipt must persist with the emptied cargo tank.
- Stock undocking and separation into distinct native vessels. A retained
  `Disengage` partner pointer is not a physical docking joint: RCS must create
  clearance before the ports clear that pointer.
- All original pod parts and normal thermal limits throughout; no intermediate
  propulsion refill. Depots may refuel only after accepted native landing.
- Native touchdown at the original pad within 25 m, upright within 8 degrees and
  speed at most 0.5 m/s for the existing five-second stability requirement. The
  independent orientation capture must cover at least 4.5 seconds of that window
  without a sample gap over 0.8 seconds. The controller's acceptance record must
  still establish the complete five seconds.
- One completed flight, the original refitted pod physically home with empty
  locked cargo, and the unchanged spare still available.

Run `sandbox/tests/audit_roundtrip.py` on the completed collector bundle. An
individual delivery or landing audit cannot promote a diagnostic into a complete
trip. A successful single mission also does not establish a reliability rate or
interplanetary support.

Both native helper files must remain `network=Running`, and each flight-status
file must update within six seconds. A live process alone is not evidence that
its client remains connected. A peer disconnection is an explicit rejection,
even if the piloting client subsequently delivers or returns successfully.

The cargo conservation tolerance is 1e-6 resource units. Native snapshots can
store a double resource amount against a float32-quantized capacity; the capacity
bounds check admits at most one float32 ULP, without relaxing conservation or
allowing unrelated resource changes. This covers the observed unchanged native
82.0125 amount / 82.012496948242188 capacity pair.

## Candidate recovery controls

The existing four stock airbrakes previously stayed folded throughout return.
The new controller requests their normal native deployment symmetrically during
orbital return preparation and descent. It estimates heating across the stock
actuator response time and folds the panels before the exposed-temperature
envelope is exceeded. There is no invented dynamic-pressure cutoff. The return
waits for the actual flap pose to settle before starting the landing forecast.
It does not alter drag, physical forces, heat limits or actuator speed.

The installed MechJeb predictor omits ModuleAeroSurface internal drag/lift. A
scoped forecast adapter adds snapshots of those native curves and flap geometry
only for registered logistics returns. It retains native drag cubes and uses a
separate worker-safe instance pool. In the native force oracle, 3,072 folded/open
cases passed the documented tolerance of max(0.00005 kN, magnitude * 1e-5).
The integrated adapter comparison also checks cancellation of MechJeb's
downstream drag-cube pseudo-Reynolds multiplier for internal airbrake drag.
Maximum vector error in that comparison was 0.00109268 kN (about 1.09 N). These are force-model
comparisons; the synthetic forces were not applied to the vessel. KSP force
vectors already use kN and rigidbody masses use tonnes. This comparison does
not establish trajectory accuracy or full-trip success.

The previous physical diagnostic conserved all 100 cargo units at the assigned
native docking joint, then ran out of propulsion roughly 9 km from home. The
forecast adapter and complete delivery-return pad centering still require a
successful uninterrupted native mission. Neither the diagnostic close-approach
fixture nor any resumed descent is full acceptance.

## Docking callback investigation

The forecast diagnostic exposed a second-client disconnect in the installed
Luna 0.29.2 `VesselCouple.AfterCouplingEvent`. A locally owned receiver causes
the native coupling routine to return early; the subsequent camera callback
can dereference a null or stale dominant-vessel cache. The peer's process kept
running, but its last shared-flight status stopped at docking. The conserved
delivery remained real; uninterrupted multiplayer acceptance failed.

The candidate guard is scoped to the assigned logistics docking-port pair and
only skips an invalid post-coupling camera activation. It does not manufacture
a joint, force vessel ownership, grant cargo, or suppress native exceptions.
It also permits our flight controller to resume polling after a connection
loss and writes an explicit unavailable-fleet status. These changes require
native retesting, including actual receiving-player ownership handoff.

Related upstream research: [Luna docking synchronization draft #660](https://github.com/LunaMultiplayer/LunaMultiplayer/pull/660).
The draft also changes ownership and forced vessel reloads. Those broader
changes have not been applied to the gameplay candidate.

Follow-up native test: a same-pilot dock delivered exactly 100 units, undocked,
and kept both clients connected. It did not exercise receiving-player ownership.
A second test deliberately gave RG-Test-B the station's native control lock before
RG-Test-A joined and flew the pod. The guard executed on the receiving client and
prevented the previous callback exception. Both peers remained connected, but the
dominant-vessel snapshot failed to retain the merged pod and the server declared
the original pod missing before any delivery. The pilot still had the reciprocal
native joint, 100 combined parts and all 29 original pod parts, while the receiving
client/server omitted the pod. This was a synchronization failure, not an observed
physical destruction. **The camera guard alone is unsafe
as a complete repair.** It now requires `-railgun-luna-coupling-test` and is disabled
in normal use. Correct complete native snapshots and ownership reconciliation must
be validated together before enabling it. No public host or updater was changed.

## Further return diagnostics, October 29 evening

The complete uninterrupted trip remains **unverified**. The subsequent two-client
diagnostic again conserved exactly 100 MonoPropellant at the assigned reciprocal
Junior joint with all 29 original pod parts. It then ran out of propulsion at
3.7 km ASL, roughly 8.2 km short. The second client had already disconnected at
docking, so that run cannot establish uninterrupted multiplayer operation.

Two separate checks confirmed the forecast adapter is actually used: a warmed
whole-vessel builder captured all four airbrake models and passed all 3,072 force
comparisons; real return flights also recorded captures and evaluations under the
actual native vessel identity. This rules out an unused adapter in those tests.
It does not establish accuracy of predictions that assume different future controls.

An adaptive native deployment-angle experiment reduced a post-deorbit replay's
shortfall to about 3 km, but exhausted fuel. It uses the stock player deployment
slider within the original 70-degree range. It remains behind `-railgun-range-test`.

A separate cubic powered-intercept experiment tried to retain horizontal travel
instead of crawling toward a distant pad. Its analytical endpoint checks passed,
but native replays impacted short of home with fuel remaining. Read-only traces
recorded full pitch/yaw requests, zero incoming manual input, enabled actuation,
and persistent large attitude errors in dense air. A second attempt constrained
the initial turn and preserved thrust-axis projection; it also failed. Instantaneous
thrust-vector planning is therefore inadequate for this assembly and corridor.
This controller remains behind `-railgun-intercept-test`; it is **not** enabled for
ordinary deliveries. Neither replay awarded refit or completed-flight credit.

The next recovery work must characterize achievable steering under native
aerodynamic load and include that response in approach planning. Do not tune
entry caps repeatedly or promote a mathematical intercept into flight acceptance.
Trajectories 2.4.5 was inspected as a possible unpowered impact reference but was
not integrated and its settings were not changed.

The collector now rejects a peer whose native clock stalls for over six wall
seconds, even if its process stays alive and had previously accumulated enough
simulation time. Repeated old timestamps cannot satisfy continuity acceptance.
