# Changelog

## Unreleased reusable-pod prototype — 2026-10-27

- Added the separate single-player development source, build instructions and 122 checks under `sandbox/`.
- Added the native Frutiger Aero delivery menu with destination/port selection, cargo controls, selectable pod cards, compact mode and clearer disabled states.
- Added physical Junior/standard/Sr. docking hardware fitting with tested resizing and reload persistence.
- Documented Kerbin-orbit-only dispatch, paused rail-only launch work, remaining round-trip validation and the absence of multiplayer support for this prototype.
- Existing 0.2.3 release assets and Friends update channel are unchanged.

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
