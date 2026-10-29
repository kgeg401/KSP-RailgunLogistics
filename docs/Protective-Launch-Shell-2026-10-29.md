# Protective launch shell engineering build

Requested behavior: enclose the existing stock-part reusable pod, launch under rail power, shed the shell in Kerbin's upper atmosphere near 50 km, then use the original engine for native circularization.

## Implementation

- The player's installed `fairingSize3` supplies the real fairing base, generated panels, colliders, mass, drag and stock `ModuleCargoBay` shielding. A clone configuration adds a native top-node decoupler. No Squad meshes/textures or private decompilation are redistributed.
- A temporary thirtieth part mounts behind the existing Mainsail. The original 29 reusable part IDs, cargo and docking-port identity remain the mission manifest. Panel jettison and aft-base separation do not consume an original pod part.
- The shell is 7 m across its cylindrical section; its height follows the assembled pod's source mounting positions with nose clearance. It is not a visual-only mesh.
- Separation requires the ascent phase, ascending flight, at least 50,000 m altitude, and 30 m beyond the guide muzzle. With the existing 100 km / 30 degree guide, the muzzle is near 56 km; rail clearance therefore determines the actual release altitude in this fixture.
- Native fairing deployment must be acknowledged before the native base decoupler fires. The engine remains locked while the base is attached or nearby, and for three seconds after clearance. Unexpected shell loss, missing release hardware or a failed separation blocks ignition.
- Circularization is calculated after shedding the shell's actual mass. The existing native maneuver execution, atmosphere restriction and 800 m/s budget remain in force. No powered-ascent fallback, velocity insertion or temperature override is added.
- The engineering scene widens rail spacing, loading collars, locking-pin placement and the hoist yoke to accommodate the shell. Loading artwork still does not apply forces or collisions.
- Shell identity, deployment acknowledgement and clearance timing persist in the local ledger. Existing shell-free saves retain their previous behavior. Crash recovery of a newly constructed pod excludes the temporary shell from its reusable manifest.

## Running and evidence

`-railgun-shell` selects the independent `Railgun-Shell-Test` save and the rail-only launch experiment. It requires an explicitly prepared engineering save; it does not create or replace an existing fleet. The test stops owned guidance on achieving a safe orbit.

The client records normal launch telemetry in `RailLaunchEvidence-v2.tsv`, per-step shell/shielding information in `LaunchShellEvidence.tsv`, and loaded-vessel shell diagnostics in the status file. `sandbox/tests/audit_launch_shell.py <evidence directory>` checks paired native save, ledger and traces. Native game files and captures remain in the private D: evidence directory.

## Acceptance boundary

Native tests and their measured outcomes are recorded below when complete. Compilation and the pure policy/persistence checks alone do not establish physical separation or orbit.

This feature is currently isolated from the normal/Luna release. It does not establish multiplayer shell synchronization, automatic replacement of spent shells between missions, or an uninterrupted delivery/docking/return/reuse cycle. The previously recorded parked-pod reload stability issue is still separate acceptance work.

## Native results

First uninterrupted native flight (`run01`, client SHA256 `e7196effa9bb21f3e4a35166ffc93061c89474f3ca1c216b3f786d050ac9f377`):

- 5,965 launch samples, 0.02 s native physics step, 1x time and normal damage/temperature limits.
- All 29 original parts shielded before launch. Full LF/Ox remained 5,760 / 7,040 until circularization; rail/coast thrust and RCS remained off.
- Stock panel deployment near 56,081 m, after muzzle clearance; native aft-base separation acknowledged and all 29 original part IDs retained.
- Peak normalized temperature 0.4903; rail peak speed approximately 2,120 m/s.
- Native 649.3 m/s circularization, starting near 390,289 m altitude. Final orbit approximately 389,768 x 391,272 m, with LF/Ox approximately 4,341.08 / 5,305.76.
- All 17 read-only native acceptance checks passed. This early build did not yet produce the detailed shell TSV; it used native launch trace, status history, paired save/ledger and rendered observation.

The final candidate (`6b7ac41`, client SHA256 `a401e44e443b9751c486817c9965d81f9c17b56a8793c02c025ff82891811b4c`) adds closed-shell shielding admission, detailed shell diagnostics, revised loading clearances and safer persistence handling. Its second uninterrupted native flight (`run02`) passed all **20 native acceptance checks**:

- 5,952 paired launch/shell samples; 29 original parts present throughout.
- Closed native fairing: 12 panel meshes, all 29 original parts shielded, approximately 92.372 tonnes total launch mass. After native shell/base release: approximately 81.975 tonnes.
- Separation near 56,112 m, with verified panel release, native base release, physical base clearance and the three-second ignition delay in order.
- Zero main-engine thrust/RCS and unchanged LF/Ox before circularization. First actual thrust near 388,821 m altitude; native plan approximately 649.8 m/s.
- Final stable orbit approximately **388,332 x 389,781 m**; LF/Ox remaining approximately **4,340.00 / 5,304.45**. No delivery or return credit was granted.
- Peak normalized temperature approximately 0.4880, normal 0.02 s physics and 1x time; no temperature, joint, crash, fuel or electricity cheats.

Pure checks: **363**; shared-fleet regression checks: **69**. Shared-fleet checks do not establish multiplayer shell flight. Both native tests were saved and closed cleanly. The stock managed game assembly hash matched the installed source game and the final isolated lab (`d9e42483f25ee80a9c11d6c1c0a0d29b4ec78c1e08d76c971b71580c9cce51e4`).

Private evidence: `D:\GAMES\KSP-Railgun-Previews\launch-shell-20261029`. The original CradleLab plugin, part configuration and settings have been restored and hash-verified. Final candidate play/testing uses the separate `ShellLab`; normal KSP, shared universe and public update channels remain unchanged.
