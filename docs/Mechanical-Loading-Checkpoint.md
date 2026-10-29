# Mechanical loading preview - 29 October 2026

The new loading machinery is authored procedural geometry surrounding the original installed stock-part pod. It is not a replacement rocket mesh. The controller continues to use the existing native-force crane guidance; visual meshes have no colliders and do not supply forces.

The gate requires continuous measured alignment, then capture (2 seconds), collar closure (3), pin engagement (2), charging (4), pin withdrawal (1.5) and collar/gantry clearance (2). Native game time drives the 14.5-second sequence; pauses cannot finish it. Motion loss, missing samples, changed order and a new scene require fresh capture. Physics remains at the original 1x timing. The charge display is presentation, not an electrical-resource simulation.

Native testing completed the sequence with the first pod's 29 parts intact and unchanged 5,760 LF / 7,040 Ox during loading, with zero engine throttle. Early rendering revealed a center-of-mass versus root-probe art offset; the presentation now uses the root probe without changing guidance coordinates. Final rendered acceptance and video receipts are recorded separately before publication.

A second parked pod broke after unloading/reloading the ground scene. The failed native save, ledger and logs remain preserved. This preview does not claim to fix parked-vessel stability, prove a full mission, or validate multiplayer animation synchronization. The ordinary 0.3.2 host and updater descriptors are deliberately retained. The 0.3.4 download requires opt-in installation in an isolated test setup.

The pure checks cover phase ordering, motion loss during pin engagement, clock pause/regression, interrupted samples, new orders, scene resets and timing at different update cadences. Existing shared-fleet, classic delivery and updater checks are retained. These tests do not establish native flight by themselves.

The isolated demo controls are `demo-daylight` (native timewarp while no mission is active), `loading-hold`, `loading-cycle`, `loading-release`, `showcase-loading` and `showcase-locks`. A loading replay changes only its local mechanism gate; it does not rewind the vessel, change fuel or award delivery/refit credit. Daylight setup is completed before the loading take. A staged replay must not be represented as an uninterrupted complete flight.

Source, facility art and synthesized mechanism sounds are original AI-assisted work by Codex for Kgeg4. Owned KSP assets, private saves, game assemblies and native captures are excluded from the public source and mod download.
