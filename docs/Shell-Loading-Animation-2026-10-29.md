# Shell loading animation reconstruction

The protective shell needs a loader designed around its casing. Enlarging the former exposed-pod collars did not provide a convincing pickup, transfer, tilt or carrier handoff.

## Implemented sequence

The transport frame reads the actual native fairing profile. Two split grip bands contact the cylindrical section near its ends, away from the rail collar positions. A structural spine, hydraulic slides, cushions, lock indicators and twin hoist cables replace the small probe-head yoke. The crane bridge is raised to 84 m for shell clearance. Cable anchors stay on the upper side of the tilted shell so their vertical paths move away from its axis.

The native crane controller holds the real pod while the carrier approaches and grips, lifts it upright to 50 m above the depot, traverses over the rail, tilts it, then lowers it to the measured cradle height. Each waypoint requires actual position, speed and attitude evidence. A timer cannot substitute for reaching a waypoint.

After seating, a separate 21-second shell handoff checks capture, closes the rail collars, drives the locking pins, opens the transport carrier, withdraws it, charges the accelerator, retracts the locking pins and opens the collars. Charging waits for full carrier withdrawal. Launch requires all hardware clear plus the existing native closed-shell/shielding check. The ordinary exposed-pod loading sequence retains its original timing.

Facility meshes remain authored visual machinery with no colliders or vessel reparenting. Native translation is force-driven. The shell crane now also applies a distributed force couple and torque around the measured centre of mass; it does not assign a vessel position or use the former rotation shortcut during loading. Rail acceleration and stock fairing separation remain separate systems.

## Evidence and iteration

The first native take completed the transfer, lock handoff and rail release with all 30 parts intact and unchanged LF/Ox. Its stricter positional audit rejected the tilt: the former native `SetRotation` shortcut swung the casing around the upper probe, displacing its centre by about 5.2 m laterally and 4.1 m vertically. The failing audit and original take remain preserved. The tolerance was not widened.

The second candidate replaces that shortcut with the force-driven centre-of-mass rotation described above. Its native take passed all **16 loading/rail-release acceptance checks**, with 378 measured loading samples. The same 0.35 m lateral tilt bound that rejected the first take remains in force. Maximum measured tilt offsets were 0.2505 m in X and 0.0421 m in Z; tilt height remained between 50.0719 and 50.0749 m. The shell finished tilting with approximately 0.0081 degrees of attitude error. These raw numeric samples should not be interpreted as accuracy beyond native floating-origin measurement noise.

All 30 native parts were retained throughout loading and rail acceleration, with zero engine thrust/RCS and unchanged 5,760 LF / 7,040 Ox. The handoff first admitted launch at 21.24 s after measured seating/capture. Native rail departure was observed and saved. Recording continued into the separately tested atmospheric fairing release; the paired final save retains all 29 original pod IDs after the temporary shell was correctly jettisoned. This loading checkpoint does not claim another completed circularization, delivery, return or multiplayer round trip.

Headless route/handoff checks and all 69 shared-fleet regression checks passed. Shared-fleet checks cover the unchanged ordinary mode; they do not establish multiplayer shell loading. The final native test was saved and closed. Captioned preview footage uses the second take, with the game's own captured audio; original takes are preserved separately.

Private evidence root: `D:\GAMES\KSP-Railgun-Previews\shell-loading-20261029`. The existing successful orbital shell test is archived as `ShellLab/saves/Railgun-Shell-Orbit-20261029`. The first loading take is separately archived as `Railgun-Shell-Loading-Take01`. No shared universe or public update channel is modified by this work.

`ShellLoading-v2.tsv` records measured transfer stages, positions, carrier state, rail locks, charge, launch clearance and native part count. `sandbox/tests/audit_shell_loading.py` checks paired telemetry and the saved native part identities without changing them.
