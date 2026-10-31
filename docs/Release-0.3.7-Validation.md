# 0.3.7 release validation

Target: KSP 1.12.5/build 3190, Luna 0.29.2, MechJeb 2.14.3 and Harmony 2.2.1 on Windows.

## Release acceptance

The release check starts a new two-pod depot with ordinary gameplay guidance. A separate observation addon connects two native clients, selects vessels and records evidence. It does not enable the isolated guidance, intercept or docking experiments. The test must preserve both original physical vessels and record every launch, delivery, return and refit phase without restarting the flight.

The complete-route profile requires conserved cargo transfer after native docking, five seconds of stable native home landing with all original parts, a physically closed replacement shell and saved propulsion supplies before Ready. Coordinated coasts also require both players' consent, native slowdown, timeline handoff and continuous recorded clock coverage. Original strict 1x results remain separate. A staged landing or ascent fixture cannot satisfy complete-route acceptance.

Ordinary multiplayer native result: **passed one complete controlled route** in about 58 minutes. The same two clients and server completed rail launch, circularization, rendezvous, native docking, a conserved 100-unit monopropellant delivery, undocking, return and refit. All 29 original parts survived. Accepted touchdown was 2.93 m from the pad, with 5.18 seconds of stable native contact. The final server snapshot already contained full propulsion supplies and the new closed shell before Ready was accepted; no late persistence supplement was needed.

The rail segment independently recorded 382 native samples without propulsion fuel use and 916 thrust samples at zero main thrust/throttle. The subsequent circularization burn is a separate part of the flight.

## Other checks

- Menu: hidden startup, toolbar open, full close-button dismissal, toolbar reopen and toolbar hide verified in the native game. Closing releases the input lock and leaves flight updates running.
- Compatibility: a declared construction-only fixture passed first-shell fitting on an older landed pod. It retained the 29 original parts, order, zero completed-flight count and absent lost spare, and generated 12 native shell panels while still Refitting. It grants no delivery or whole-trip credit.
- Headless checks: 2,897 fleet/flight/persistence, 117 authority, 29 warp coordination, 34 core, 15 refit supply/native-parser, and 12 installer checks passed.
- Evidence readers: 34 common-clock and authority-chain tests passed. Missing handoffs, identity changes, unexplained clock jumps and powered warp are rejected. These synthetic tests grant no flight credit.

## Evidence profile clarification

The original strict 1x result and earlier coordinated-reader failure are retained. The earlier reader required a sampled Synchronizing state even for a cancelled attempt that never exceeded 1x or changed native timeline. One peer's brief state fell between telemetry samples. The current QPC v5 reader classifies such records only when both peers return to Idle at 1x on the unchanged source timeline with matching native clocks. They earn no acceleration or handoff credit, and every raw clock jump still requires independent evidence. Accelerated-session and physical acceptance rules are unchanged. The route v6 review passed 33 actual paired accelerated sessions plus the complete physical trip.

## Reproducing an evidence review

Private saves, game assemblies and captured evidence are not distributed. With your own complete observer bundle, use Python 3.10 or newer:

```powershell
python sandbox/tests/audit_roundtrip.py '<observer bundle>'
python sandbox/tests/review_coordinated_route.py '<observer bundle>'
python -m unittest discover -s sandbox/tests -p 'test_coordinated*.py'
```

The first command retains the strict 1x profile. The second writes a separately labelled coordinated-coast review and exits unsuccessfully if that profile is incomplete. Its `coordinatedNativeRouteVerified` field is distinct from the legacy `fullLogisticsRoundTrip` field. Optional linked late-persistence evidence is supported for historical captures; it must not hide an earlier failed snapshot.

## Limits

This remains a Kerbin-orbit alpha. One completed controlled route establishes one scenario, not repeated reliability or arbitrary receiver coverage. Return fuel margins can be small. This flight exhausted manoeuvring fuel during departure, briefly held, then cleared the receiver through actual separation drift and completed the return with the main engine and reaction wheels. A different depleted approach may remain held. Existing lost, stranded or fuel-starved pods are preserved rather than recreated by an update. The guarded recovery attempt budget is scoped to a vessel and authenticated order within a running client; restarting is not a persistence test of that budget. Remote-player internet latency and Minmus/interplanetary delivery are not established by a local two-client run.
