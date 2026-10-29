# 0.3.4 mechanical loading preview

- Added a traveling two-axis hoist, transport yoke, twin hydraulic collars, driven locking pins, charge indicators, work lights and original synthesized mechanical audio. Pod art remains the original installed KSP parts.
- Added a measured capture and 14.5-second locking/charging/clearance sequence before launch. Motion loss, a sample gap, new order or scene reset blocks inherited clearance. Existing crane forces and launch guidance remain responsible for actual movement.
- Corrected art attachment to the root probe rather than the physics center of mass.
- Added isolated daylight setup, loading replay/hold and camera controls for repeatable native verification. These are unavailable in ordinary and Luna saves.
- Kept the normal host and automatic channel on 0.3.2 after a native parked-pod scene-reload failure. Full consecutive multiplayer delivery/return remains unverified.

# Changelog

## 0.3.3 development — not released

- Replace the experimental pad controller's fixed 2 m/s descent below 300 m with a bounded descent rate based on clearance and time needed for horizontal correction. The last 30 m retains the slow contact approach.
- A replay from the failed uninterrupted 0.3.2 return's unchanged 944 m native checkpoint landed 0.720 m from the pad and passed the five-second stability gate with all original parts/cargo intact. A complete new return on this policy is still required; production channels stay on 0.3.2.
- Add two near-pad/far-miss policy checks; fleet checks now total 159. Missing touchdown evidence is reported as a failed acceptance rather than preventing the remaining read-only audit checks.

## 0.3.2 multiplayer alpha

- Advisory delivery ETA range in reusable pod cards and flight details, based on native encounter/docking guidance and KSP universal time. Invalidated estimates cannot complete cargo transfer.
- Watching a pod selects its menu card; the single-player pause menu hides the delivery panel and pauses mechanic updates.
- Isolated 100 km rail experiment: engines/RCS off through acceleration and coast, separate bounded circularization, improved lost-node handling, and versioned guide-force telemetry.
- Optional isolated precision-pad guidance uses native engine attitude/throttle, horizontal stopping distance and a strict physical touchdown/stability gate. It remains disabled for ordinary/Luna flights.
- Read-only launch and landing acceptance audits, including rejection of an intact touchdown outside the home-pad envelope.
- Existing updater/helper reused byte-for-byte. Matching host/client update is required. See the checkpoint for native evidence and limitations.

## 0.2.3 bundle (gameplay remains 0.2.0)

- Normal GameData mod ZIP: copy the mod folder and play; no installer or launcher.
- Bundled startup updater checks the Friends channel and applies verified updates after KSP closes.
- Uses the XML library included with KSP; archive processing runs in the bundled Windows helper, not KSP's limited Mono runtime.
- Supersedes the 0.2.1 setup release, whose startup parser required an unavailable runtime library. 0.2.2 was an unpublished intermediate build.

## 0.2.1 bundle (gameplay remains 0.2.0)

- Separate Windows updater checks the pinned Friends GitHub channel at KSP's main menu.
- Downloads and verifies in the background; a helper waits for game exit before replacing this mod folder.
- Keeps rollback backups, local mod settings, extra files, saves, and unrelated mods.
- Rejects wrong game/Luna builds, corrupt downloads, unlisted files, and unsafe ZIP paths.
- Server-approved release promotion checks the client SHA required by the running host.
- Native-tested physical-pod gameplay DLL is unchanged; server restart is not required for this bundle.

## 0.2.0

- Real persistent cargo vessels replace the cosmetic arrival capsule and instant tank fill.
- Assisted final approach uses physical acceleration; stock Clamp-O-Tron magnets capture and dock the pod.
- Cargo transfers from actual pod tanks only after docking. The empty pod stays attached for normal undocking.
- Orders identify a free size1 Clamp-O-Tron and use a stable pod ID with durable spawn acknowledgement.
- Luna protocol v2 checks the physical pod and saved docking receipt; matching clients/server are required.
- Native orbital sandbox and one-client Luna docking/cargo conservation passed. Two-client play and forced-crash recovery remain untested.

## 0.1.0

- Initial assisted countdown delivery with a cosmetic capsule, instant tank transfer and persistent receipts. Superseded by0.2.0 physical pods.
