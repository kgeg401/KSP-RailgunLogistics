# Railgun Logistics client updates

## Current shared build: 0.3.6 auto-warp alpha

The shared host and `channel/reusable.xml` now require the matching 0.3.6 build. Existing 0.3.0–0.3.2 reusable clients check at startup, download the verified update, and apply it after KSP closes. Start KSP, allow the download notice, quit normally, wait for the helper, then reopen before joining. Your saves, existing settings and other mods are preserved. The older 0.2.3 migration still uses the updater bridge first.

For a manual installation, use **RailgunLogistics-0.3.6.zip** from the [current release](https://github.com/kgeg401/KSP-RailgunLogistics/releases/tag/v0.3.6-preview). This standard package is different from the earlier manual AutoWarp-Preview ZIP, which intentionally contains DisableAutoUpdates.txt. If you installed that manual preview, restore your normal setup or remove that opt-out file before expecting automatic updates.

After updating, every connected player enables **Auto warp: On** in Railgun Logistics. Safe orbital coasts can accelerate to 100×; guidance waits at 1× until all players join the same native Luna timeline. Docking, launch/loading, entry and terminal landing do not auto warp. See [the final native validation](docs/Auto-Warp-Validation-2026-10-30.md) for scope and the unresolved complete-trip/docking gaps. The older release notes below are historical.

## Optional mechanical-loading preview

0.3.4 is a **manual opt-in preview**, separate from the pinned 0.3.2 server channel. Download RailgunLogistics-0.3.4.zip from its release, close KSP, back up GameData/RailgunLogistics, and copy the complete mod folder. Preserve Settings.xml and create DisableAutoUpdates.txt for the preview. Do not join the ordinary host with a mismatched DLL; use an isolated single-player sandbox or a separate matching test host. Restore the 0.3.2 backup before rejoining the existing server. The internal Update ZIP is for the update helper, not first installation. A parked pod failed a native scene-reload test; automatic promotion is held.

The reusable-pod client is the **0.3.2 multiplayer alpha**. It adds a delivery ETA range to the physical-pod menu. The older 0.2.3 assisted-docking download remains available separately. Check the release page and the pinned Friends channel for what has actually been published; a source commit or draft does not update a player.

## Requirements and first installation

Use Windows, KSP 1.12.5 build 3190, Luna Multiplayer 0.29.2, ModuleManager, MechJeb 2.14.3 and Harmony 2.2.1 from the shared server's mod pack. This small download contains our mod, not the game or third-party mod pack.

Close KSP, extract the ordinary **RailgunLogistics-0.3.2.zip**, and copy its **GameData/RailgunLogistics** folder into your game's GameData folder. Back up an existing copy first and preserve your Settings.xml. Copy the entire folder, including Parts and Updater. Keep your existing Luna client, saves and other mods. An older separate development addon at GameData/RailgunSandbox must be backed up outside GameData before installing the integrated client, or duplicate game modules would load.

Players do not run an installer, a command, or ApplyUpdate.exe. GitHub's **Code / Download ZIP** contains source rather than the playable mod. The ZIP named **RailgunLogistics-Update-0.3.2.zip** is the updater's internal payload, not the first-install download.

## Updating an existing client

Existing **0.3.0 / 0.3.1** clients receive 0.3.2 in one normal update cycle after channel promotion: start KSP, follow the downloaded-update notice, quit normally, then reopen. The delivery ETA is advisory and uses KSP universal time. Missing guidance or a usable trajectory shows calculating rather than a fabricated countdown. Cargo still transfers only after physical docking.

### From 0.2.3

Start KSP normally. The older updater reads **channel/friends.xml**, which installs the **0.2.5 updater bridge** while retaining the classic gameplay DLL. Follow its notice: quit normally, wait for the background helper, then start KSP again.

The newer updater reads **channel/reusable.xml**. It checks the required mod versions and downloads the integrated 0.3.2 client. Follow that second notice and quit/reopen once more. This one-time migration takes two update cycles; later compatible updates use the ordinary single cycle. The Luna host requires the matching gameplay DLL, so finish the client update before joining.

Missing or incompatible dependencies leave the installed client unchanged and produce a message. Install the shared server's required mod pack, then start KSP again. Updates download at the main menu and apply only after that game instance closes. The helper preserves settings and extra files, keeps backups outside GameData, verifies every archive entry and file hash, and does not stop KSP, replace Luna, alter saves or update the server.

## What the alpha implements

The server owns two persistent physical pods. The native delivery menu selects an exact receiver port and fits the corresponding stock Junior, standard or Sr. connector before departure. Flight code covers rail-assisted launch, powered ascent, rendezvous, stock docking, cargo transfer, undocking and powered home-pad recovery/refit. A pod is unavailable until its return is accepted; losses do not generate replacements. Both clients share reservations and flight state through Luna.

Native two-client testing has demonstrated shared fleet state, ownership handoff, actual connector fitting, rail launch, powered ascent and stable orbit. A complete consecutive multiplayer docking/delivery/return cycle remains unverified. The alpha is limited to stable Kerbin-orbit receivers, one foreground guided flight, the same Luna subspace and 1x time. It does not implement Minmus/interplanetary routing or server-side powered flight while its controlling clients are offline. Keep a matching Universe/fleet backup before testing.

The new rail-only and precision-pad controllers are isolated engineering experiments. Ordinary and Luna flights continue using rail assist plus powered ascent and the existing native landing controller. Two experimental orbital launches and a successful saved-descent landing replay do not establish multiplayer readiness. See [the checkpoint](docs/Launch-Landing-Checkpoint.md) for measured results and unresolved cases.

## Host publishing and diagnostics

The host installs the matching server plugin and mod-control entries while stopped, then restarts and verifies it before promoting either update channel. **Publish-Reusable.ps1** verifies the running host, both payloads and GitHub asset digests, and promotes both descriptors in one commit. A partial upload leaves the previous channels in place. Older clients receive only the compatible bridge, not a payload their old helper cannot preflight.

Update diagnostics are in **RailgunLogistics-Updates** beside GameData; each job has a result.txt. Backups are in **RailgunLogistics-Backups/autoupdate-***. To opt out, create **GameData/RailgunLogistics/DisableAutoUpdates.txt**. To roll back, close KSP and restore the mod backup with updates disabled. Server fleet state lives in **Universe/RailgunReusable/Fleet.xml** and must stay paired with its native vessel universe.

Original mod and updater code developed for Kgeg4 with OpenAI Codex; MIT license. Uses the player's installed KSP/Unity, Luna, ModuleManager, MechJeb and Harmony. No Squad assets, private saves, server credentials or decompiled game code are distributed.
