# Higher-fidelity accelerator rails

This records the first rail-art iteration. The subsequent [rail and loader checkpoint](Rail-Loader-Checkpoint-2026-10-29.md) supersedes its mesh counts and clearance measurements, fixes the camera and gantry issues, and adds a completed native shell-to-orbit test. The validation below remains the evidence for this earlier build.

## Implemented assets

The former two rectangular bars and sparse copper blocks are replaced by original Unity meshes authored in `sandbox/RailMesh.cs` and `sandbox/RailAssets.cs`. They include chamfered structural beams, copper conductors and windings, ceramic isolation, induction housings, cooling and power conduits, hex fasteners, electrical cabinets, grated service walks, guardrails, open loading hoops, triangulated trestles and bolted concrete foundations. Recessed cyan strips identify the acceleration channel. Original repeating micro-surface textures use the installed KSP shaders.

The shell variant has a 3.85 m nominal bore and a 150-degree upper loading aperture. The visible breech extends 24 m behind the acceleration origin to support the loaded casing. Rail roll places the aperture toward the local sky while preserving the original guide axis. Loader collars share that frame; their support feet and hydraulic rams have been moved outside the shell envelope. The idle cradle now uses the same measured mounting height and orientation as the reserved cradle.

## Runtime cost and boundaries

The 32 m detailed module has 92,320 vertices and seven material submeshes. Meshes and materials are shared; detailed sections near the viewer are pooled, with eight sections retained around the depot. Long sections use coarse continuous backbones. This avoids building the entire 100 km engineering guide at full detail. Full-length supports and distant detail are not a finished civil-engineering simulation. No frame-rate improvement or performance budget is claimed without a dedicated profile.

Rail artwork contains no colliders or rigidbodies. Launch force, acceleration origin, heading, elevation, loading timing, shell behavior and fuel rules retain their existing implementations. Native test diagnostics measured a minimum detailed-vertex radius of 3.8635993 m and zero rail colliders. This radial check is a geometry sanity check; it is not a swept collision proof for every moving loader component.

## Verification

Both the integrated client and standalone client compile. The standalone build passes 1,312 fleet/loading checks and 69 shared-fleet authority checks.

The completed native loading/rail test passed all 16 existing acceptance checks without changing tolerances. Its 379 loading samples cover grip, lift, traverse, tilt, seat and complete collar/carrier handoff. The first launch-clear sample was at 21.22 seconds after seating. All 30 parts and their saved identities survived; loading and rail acceleration used zero engine thrust and RCS, retaining 5,760 LiquidFuel and 7,040 Oxidizer. The pod departed the rail origin and was saved during acceleration. This verifies loading and early rail departure, not orbit insertion, a complete return, or a multiplayer logistics round trip.

The upgraded asset was loaded and inspected in the isolated `Railgun-Shell-Test` game. `RailInspection-2.png` is an in-engine asset close-up under an explicitly authored work light. `Rail-Trestles.png` is the actual game backbuffer with normal game lighting. These are different capture conditions, not a before/after comparison. Private images, diagnostics and paired native evidence are retained under `D:\GAMES\KSP-Railgun-Previews\rail-assets-20261029`.

This is a local development build. The shared Luna universe, public release and automatic update channel have not been promoted.

The 35-second native recording is preserved as `Rail-native-full.mp4`; the separate `Rail-Assets-Preview-Chat.mp4` keeps the full footage and game audio in a smaller captioned copy. A desktop screenshot helper failed, so visual checks used KSP's own camera/backbuffer plus the independently recorded native game window. Library upload and native Atlas attachment tools were unavailable; the preview has not been delivered to Atlas.
