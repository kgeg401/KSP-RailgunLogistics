# Rail and loader development checkpoint

This local follow-up starts from `199b6e9`. It refines the launcher artwork and its connection to the protective-shell loader. The shared Luna game, release ZIPs and automatic update channel are outside this checkpoint.

## Changes

- Rounded continuous copper windings replace intersecting capped cylinders. Smooth tube normals, proper cap triangles and tangents remove the previous faceting and degenerate cap triangles.
- Detailed 32 m rail modules now contain 55,152 vertices, down from 92,320. A separate 23,120-vertex distant module omits small fasteners and winding detail. This is a vertex-count comparison, not a measured frame-rate improvement.
- Detail sections share meshes, use distance-based levels of detail, reuse a bounded pool and skip layout work when the viewer remains in the same section. Identical backbone spans share a mesh, partial end sections are cached once, and support rebuilds release their previous mesh.
- Continuous split collars, replaceable contact pads, fasteners, flanged hollow actuator cylinders, seals, hydraulic lines and profile-fitted carrier grips replace the rough segmented blocks. Locking pins align with the collar and withdraw into sockets outside the protected launch channel. Pin travel follows the normalized existing animation; the loading/launch interlocks and their timing are unchanged.
- The rail status strips follow the actual loading/charging/rail-driving state. The idle charge indicators reset instead of remaining lit after launch.
- The bare-pod visual bore increases to 2.15 m to accommodate the supported larger coupler and folded landing hardware. The shell bore remains 3.85 m.
- The gantry runway is wider and can move its sides, trolley wheels and cross-carriage to preserve clearance for the actual launch heading. Its safety calculation covers the full tower/diagonal footprint, rather than assuming an exactly eastward rail.

## Bugs found by native checks

The first surface-level clearance probe rejected an end housing that extended 0.10 m into the intended bore. Earlier vertex-only measurements missed the middle of that face. The housing moved outward; the acceptance bound was tightened to the full nominal bore, with 1 mm numerical allowance. The failed report is retained privately alongside the corrected run.

Inspection targets used to be children of the depot. KSP reparents its persistent camera pivot to a Transform target, so hiding or disposing the depot could disable or destroy the camera with it. The target now has an independent lifetime and releases the native camera before cleanup. Native camera reference-frame interpolation and pivot translation also caused incorrect framing; the inspection rig now uses the public immediate-mode and coordinate-positioning APIs and corrects only its owned pivot anchor. Moving orbital readback verified a centered target while the depot was inactive.

The first payload-width probe included runtime gizmos and effect geometry. The corrected probe uses KSP's own part-model hierarchy, bakes animated skins and explicitly excludes particle, Firefly and Waterfall shaders. It reports those exclusions rather than silently treating a plume as physical hull geometry.

A locked DLL replacement failed, but a non-terminating shell error allowed a second test process to start. Only that duplicate was stopped; the original test saved and closed normally. `sandbox/Start-AssetLab.ps1` now rejects any existing KSP process, stops on installation errors and verifies the installed DLL hash before starting one marked private lab. Hidden windows render internal images but are unavailable to Windows graphics capture; use its explicit `-Visible` option for footage. The launcher defaults to a read-only native snapshot, not pod construction.

The first complete launch-clear loader probe also rejected a tower at the actual 87.951-degree heading: its surface reached 2.628 m from the launch axis. The held take was saved without launching. Wider runway spacing and a heading-aware tower/brace calculation raised the measured minimum to 4.043 m, outside the 3.85 m protected bore. This checks the actual cleared pose in the first 128 m of the route; it is not a swept collision proof for every animation frame.

## Validation

- Integrated and standalone clients compile with zero warnings/errors. There are 2,755 fleet/loading/gantry checks and 69 shared-authority checks. The new gantry checks include 1,440 heading/guide-size combinations plus the original failing spacing, vertical guide and invalid input.
- The native geometry probe checks finite vertices, normals, UVs and tangents, nondegenerate outward triangles, both nominal guide bores, locking-pin engagement/retraction and support replacement. Its 40 repeated end-to-end cache samples stay at at most 14 sections and seven owned rail meshes, without growth after warmup. Two thousand same-view updates reuse the cached layout.
- Seven acceptance-reader regressions reject intruding faces, nonfinite radii, reversed surfaces, false cache claims, growing pools and truncated coverage. These are evidence-reader tests, not substitutes for running KSP.
- One complete shell flight with the first refined-hardware build passed all 16 existing loading checks and all 20 shell/ascent checks: 393.0 x 394.4 km orbit, a 648.2 m/s circularization plan, original 29 parts retained, shell separation at 56.2 km after muzzle clearance, zero engine/RCS fuel use before circularization and no delivery/refit credit. The native shell and three-second engine-clearance interlocks remain active.
- The revised gantry build's launch-clear geometry passed across 51,764 visible loader triangles, with 4.043 m minimum radial clearance. The actual folded installed pod, excluding the temporary shell and documented effects, measured 1.868 m maximum radial extent.
- Its subsequent native flight passed all 16 loading checks, 20 shell/ascent checks and 11 geometry/cache checks. The saved orbit is 389.516 x 391.046 km, with a 649.4 m/s circularization plan, shell separation at 56.268 km and peak temperature at 49.04% of its limit. Both reusable pods retain their original identities: the launched pod has all 29 original parts; the parked second pod has those 29 plus its temporary shell. Evidence is frozen in `native-gantry-fixed-orbit`.

The flight-validated integrated client SHA256 is `104ff4f4f0eff427f7a04f44aee8712215a034d6d4d10c6a5874d6a9f52ab121`. Final log review found a separate scene-teardown exception when Unity had already removed carrier children. A subsequent cleanup-only change adds Unity-null guards to `ShellCarrier.Dispose`; its build SHA256 is `964b778ee6147f8bd3920d3647130eb9641ede9a07690f4183e964a05397140a`. The two clients and their evidence remain distinct. The cleanup-only build does not inherit an unqualified claim of a separately repeated full flight.

That cleanup build passed a native orbit reload, moving-pod inspection and requested save/exit with zero exception blocks from this mod. The camera target remained centered, with 0.057 m eye-position error. The saved game and log confirm saving before teardown and process exit; no process exit code was collected. Other installed mods still emit their own startup warnings/exceptions, so this does not claim the whole installation has a clean log. Both builds compile without warnings/errors and pass the same fleet/authority checks. All 14 Python audit regressions pass.

## Preview and local build

The 35.27-second native-game preview shows the widened loading gantry, shell handoff, machined hardware and unpowered rail departure. It uses only captured game audio. Both the 1600 x 900 master and separate 1280 x 720 chat copy passed full decoding and visual review; the master is 12,599,114 bytes and the chat copy is 6,001,479 bytes. Full source footage and audio remain private alongside them.

The master was accepted by Google Drive as [Railgun Rails and Loader - Two Hour Checkpoint](https://drive.google.com/file/d/1QXy1qyNWvRtlofUe9nF5IfegZUGfytSB/view), with the stored size confirmed. No public access permissions were added. The local test patch is for an existing isolated installation; it is not a promoted multiplayer release or an automatic update.

Private evidence: `D:\GAMES\KSP-Railgun-Previews\rail-checkpoint-20261029`. The saved first orbit and pre-checkpoint test states are retained separately. Full logistics delivery/return, two-client shell operation, a full-length structural support model, and a dedicated CPU/GPU performance comparison remain unverified. Rail and loader artwork still have no physical collision bodies; surface checks describe the measured visual pose rather than a structural or electromagnetic simulation.

The original pre-checkpoint `persistent.sfs` and paired fleet ledger match their retained archive byte-for-byte. The original installed game assembly remains unchanged. All test launches, saves, private binary research, footage and patch builds stayed in the isolated D: lab; no server process, shared universe or update manifest was replaced.
