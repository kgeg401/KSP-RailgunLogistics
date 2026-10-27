# Changelog

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
