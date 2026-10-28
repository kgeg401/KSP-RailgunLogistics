# Reusable railgun sandbox

This contains the KSP physical-flight prototype and its new Luna shared-fleet integration. The friends build compiles these sources into `RailgunLogistics.dll`; the separate `RailgunSandbox.dll` remains a development-only build and must not be installed alongside the integrated DLL. An updater bridge migrates older friends clients to the approved reusable-fleet alpha.

## Luna integration status

The server persists exactly two fleet identities in `Universe/RailgunReusable/Fleet.xml`. Reservations, native vessel ownership, per-pod revisions, renewable pilot leases, physical docking receipts and home-pad return checks are enforced by the server. Network state and unacknowledged updates survive native scene changes. Background update locks are released cooperatively when another pilot receives a pod; active control locks are retained. A docked receiver owner can take over unloading, with the original dispatching player retained for the return handoff.

Both real clients have joined the isolated Luna server and observed the same fleet. Native tests have created and persisted both 29-part pods, fitted the actual selected Junior connector, and flown through rail launch, powered ascent and stable orbit. The packaged client also rejoined and resumed the same pod in rendezvous. Full consecutive multiplayer docking, delivery and return acceptance is still pending. A native interrupted staging test destroyed its airborne pod after reconnect; the shared fleet retained that loss rather than replacing it. This is not yet an unattended service.

The current multiplayer flight slice requires the same Luna subspace, 1x time, KSP 1.12.5 build 3190, Luna 0.29.2, ModuleManager, MechJeb 2.14.3 and Harmony 2.2.1. Only stable Kerbin-orbit receivers are implemented. Interplanetary delivery and server-side powered flight while all controlling clients are offline remain outside this build.

The updater migration first installs a compatible updater using the unchanged classic gameplay DLL. That updater follows a separate reusable-fleet channel and checks dependencies before staging the integrated client. Existing helpers can apply the bridge package without expanding their file permissions. The full payload still uses only the existing mod's `Plugins`, `Parts` and `Updater` paths. A release source commit or a draft release does not update either player's game.

## Delivery menu

The native flight menu uses a Frutiger Aero theme: glossy aqua framing, a globe emblem, pale glass cards, green availability badges and a single green delivery button. Follow **Destination → Supplies → Delivery pod**. Choose a ship, pick its actual docking port, enter the supply amount or use **Fill available**, then select one of the two pods. The menu explains unavailable routes, invalid amounts and ports already assigned to a delivery before enabling dispatch.

Each pod has its own status and **Watch pod** control. **Flight details** expands the underlying mission information; the header's minus button collapses the window to a small bar. The content area scrolls on shorter displays while the delivery action remains visible. Editing the amount retains the ship-control input lock until the field loses focus. These presentation changes retain the existing flight, cargo and connector-fitting rules, including the current Kerbin-orbit route restriction.

Active pod cards and flight details now include an advisory **delivery ETA range** when the native flight plan supplies a viable encounter or docking approach. It includes the final closing leg and docking uncertainty, uses KSP universal time, and invalidates on lost guidance, changed orders, clock rewind or an expired window. Before orbit insertion or a usable encounter plan, it explicitly says it is calculating. Arrival at the estimated time cannot complete an order; cargo still requires a real docking receipt. See [the current launch/landing checkpoint](../docs/Launch-Landing-Checkpoint.md) for evidence and remaining limits.

The isolated `-railgun-porttest` fixture accepts `ui-preview:default`, `ui-preview:ports`, `ui-preview:destinations`, `ui-preview:details`, `ui-preview:invalid`, `ui-preview:compact` and `ui-preview:collapsed` for visual checks without dispatching a pod. These commands are unavailable in the ordinary save.

## Request delivery at a docking port

In the reusable sandbox, right-click the exact receiving docking port and choose **Request pod delivery here**. The delivery panel retains that ship and part ID. Choose cargo/quantity and dispatch an available pod. Watching another vessel does not silently select another receiver; an occupied, closed, unloaded or missing selected port disables dispatch until the user selects another port.

Before departure, the pod receives the **actual matching stock Junior / standard / Sr. docking-port part**, including its native model, colliders, mass and modules. Dispatch captures the clicked receiver's stock hardware, TweakScale settings and stock variant rather than changing a standard connector's compatibility label. The exact native interface is still read from the live receiver: the resized Junior fixture uses `size1p5` instead of its prefab's `size0`.

The fitting replaces only the connector leaf in the parked, unloaded native vessel snapshot. The same two pods, vessel IDs, all 29 part IDs, propulsion fuel and exact destination are retained. KSP instantiates the selected part when the pod is loaded. Before launch, the addon checks its actual part name, native docking interface, mounting position, model scale and free state; a mismatch holds departure. The fitting survives reload independently of the receiver being loaded. Existing in-flight orders retain their previous hardware. Gendered, indexed, shielded and other unsupported connector designs are rejected explicitly.

Rail-only ascent remains restricted to its explicit experimental test save. Ordinary and Luna dispatches use the vertical rail assist and powered ascent. Successful isolated rail flights do not establish a multiplayer launch or a complete delivery/return cycle.

`-railgun-porttest` selects a separate `Railgun-Port-Selection-Test` fixture where launch force is disabled and Reserved stops after physical fitting and cargo configuration. `port-menu-selftest` exercises native port menu events; the older `port-coupler-selftest` checks compatibility metadata only. `port-hardware-check` verifies native meshes, colliders, mounting gap, identity and the selected fitting. `port-test-release` releases only an unlaunched isolated test order without delivery, return or refuel credit; it is unavailable in the ordinary sandbox.

The build passes 122 automated checks. Native tests verified actual Sr., resized Junior, unscaled Junior and standard fittings, plus restart persistence of the resized fitting. Mounting gaps were below 0.4 mm, all 29 part IDs and propulsion fuel were retained, and native Junior/standard/resized screenshots were inspected. Private evidence and the save audit are in `D:\GAMES\KSP-Railgun-Previews\port-hardware-20261027\validation.json`. These checks establish physical fitting and reload behavior; physical docking across sizes and a complete delivery flight with the new hardware remain untested. The ordinary user's sandbox save was not rewound or changed by these fixture tests.

## Experimental rail-only ascent — October 28

The `-railgun-railonly` experiment uses an angled electromagnetic guide, followed by an unpowered coast and a separate native MechJeb circularization maneuver. Its rocket-ascent autopilot is not started. Main engines and RCS are interlocked off during loading, rail acceleration and coast. Launch force is restricted to the guide and ends at the muzzle; there is no trajectory/velocity insertion after release.

The current engineering candidate has a **100 km guide at 30 degrees**, with a **2,200 m/s** drive target. Atmospheric drag affects attained speed. Two native flights left the guide at approximately **1,914 m/s / 56 km altitude**, then used approximately **725 m/s** of native circularization to reach **263 × 267 km** orbits. Both retained all 29 original parts, 100 locked cargo units and approximately 4,194 liquid-fuel units. Peak normalized part temperature was approximately 0.434. These are isolated launch results, not full logistics or multiplayer acceptance.

A native circularization plan is admitted only above the atmosphere, with a maximum **800 m/s** correction. Engines unlock near the saved maneuver's time according to the finite-burn duration estimate. Lost unexecuted nodes can be replanned from the actual orbit; a missing executed plan, missed window or failed guide departure stops the test explicitly. A safe actual orbit can resume without another ascent. No rocket-ascent fallback is selected.

The earlier 4 km / 20-degree / 2,700 m/s candidate failed natively: it reached 2,213 m/s with engines/RCS off and unchanged LF/Ox, but structural parts were destroyed before muzzle departure. The 40 km candidate also failed: its exposed RTG exceeded its temperature limit near 12 km altitude / 1,480 m/s, followed by the docking port. The longer guide reduces acceleration and heating without changing stock thermal limits or adding a fairing.

`-railgun-railonly` loads the separate `Railgun-RailOnly-Test` engineering save. That save stops owned guidance on reaching Rendezvous so the achieved orbit can be inspected before any rendezvous burn. It does not reset or respawn pods in ordinary play. `RailLaunchEvidence-v2.tsv` records rail position, speed, thrust, fuel, RCS, timestep, original assembly size, temperature, dynamic pressure and requested guide acceleration. Older runs use the original trace schema. `tests/audit_rail_flight.py` checks the paired native save, ledger and trace without modifying them.

## Earlier prototype evidence and continuing boundaries

The native scene has a launch facility and two persistent stock-part rockets. The first rocket physically left the rails and flew under its own engine, with delivery cargo isolated from propulsion. That first vehicle exhausted its propulsion before reaching orbit and was destroyed. It did not deliver cargo.

The Mk II assembly replaces its three FL-T800 tanks and Swivel with a stock adapter, two Jumbo-64 tanks and a Mainsail. Native testing reached a stable 111.99 x 112.11 km Kerbin orbit, with all 25 parts intact, 100 locked MonoPropellant cargo, and propulsion left. It entered rendezvous planning. Native docking/delivery, undocking, powered landing and reuse still require complete flight verification. Automated checks do not establish those results.

That orbital flight survived a native save/reload, preserving its parts and cargo. The parked second Mk II pod became unstable and broke apart on its landing legs during reload validation. Pad support/ground stability remains an open defect. Damage guards now compare original part IDs, including unloaded native snapshots, before dispatch; the fleet is not yet ready for unattended use.

Only a receiving ship in a stable Kerbin orbit is supported in this slice. One pod can have foreground flight control at a time; the second reservation waits. Interplanetary planning and unattended background powered flight remain unfinished. Flight uses KSP universal time at 1x while the simulation runs; closing or pausing the game does not advance deliveries.

## Build and local install

Run `Build-Sandbox.ps1`, then close KSP and run `Install-Sandbox.ps1`. The installer checks the staged source/DLL hashes and backs up the sandbox addon and its isolated save. It does not install game assets or change the Luna server.

The addon activates in the `Railgun-Reusable-Sandbox` sandbox save. An explicit `-railgun-demo` launch selects a separate `Railgun-Reusable-Demo` capture save instead. Normal saves do not activate this scene/controller. Native parts and the installed MechJeb 2.14.3 are required. The launcher model is original procedural artwork; pod meshes are loaded from installed KSP parts.

## Native test controls

The in-game panel can prepare the two original pods, watch one, and dispatch to a selected receiver's available standard docking port. Each pod must finish its initial construction placement before dispatch. A lost or stranded pod is not a new ready slot. No automatic replacement is generated.

For development without desktop input, write one command into the save's `RailgunSandbox.command` file. Commands are consumed and deleted: `prepare`, `watch1`, `watch2`, `receiver`, `dispatch1`, `dispatch2`, `snapshot`, `inspect`, `save-and-exit`, `pause-follow`.

`dispatch1`/`dispatch2` send 100 MonoPropellant to an available standard port on the active receiver. `RailgunSandbox.status.txt` reports actual native state and separates propulsion from cargo. `inspect` requires both pods loaded and creates offscreen scene images with additional inspection lighting; those images are not gameplay footage.

`showcase-wide`, `showcase-pod`, `showcase-flight` and `showcase-orbit` hide the interface and frame the real native cameras for recording. `showcase-ui` restores the interface. These commands change presentation only. The October 25 native flight video is saved under `D:\GAMES\KSP-Railgun-Previews\2026-10-25`: a 69-second captioned demo, a chat-sized copy, and the full uninterrupted native capture with process-only game audio. The video ends at stable orbit and does not demonstrate re-entry, docking or recovery.

## Persistence and failure

Keep `persistent.sfs` and `RailgunReusableFleet.xml` together when backing up or restoring. Native part IDs, cargo tanks and the transfer receipt are checked against the two stable fleet identities. Do not restore only one file of the pair.

Propulsion exhaustion during ascent, rendezvous or landing marks a pod stranded and stops its automatic flight control. It remains unavailable. Physical destruction marks it lost. A time delay or reload cannot release either slot.

Development fixture rewinds, when explicitly recorded, are engineering test resets rather than gameplay recovery. Captures, native saves, decompiled API evidence, fixture tooling and binaries stay under ignored `evidence/`, `staged/`, `bin/` and `obj/` directories.
