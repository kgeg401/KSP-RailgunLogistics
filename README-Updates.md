# Railgun Logistics: copy into GameData

Download **RailgunLogistics-0.2.3.zip** from the GitHub release. Close KSP, unzip it, and copy the included **RailgunLogistics** folder into your existing **Kerbal Space Program/GameData** folder. The result should be `GameData/RailgunLogistics/Plugins/RailgunLogistics.dll`, with the `Parts` and `Updater` folders alongside `Plugins`. Copy the entire mod folder, not just its DLL. If our mod is already present, back up that folder first and merge the new folder into it. Keep your existing Settings.xml if you changed the default delivery timings.

That is the whole installation. **No installer, launcher, new multiplayer client, or GitHub login.** Keep your existing KSP, Luna Multiplayer, ModuleManager, saves, and other mods. Requirements: Windows, KSP 1.12.5 build 3190, Luna Multiplayer 0.29.2, and ModuleManager.

Start KSP normally, including through Steam. At the main menu, our mod checks the public Friends update channel. Approved updates download automatically in the background. If an update is ready, KSP shows a notice: quit normally, wait a few seconds, then reopen KSP. Our bundled background helper applies the update after the game closes because KSP cannot reload running DLLs. You never need to run the helper yourself. Downloads are checked before staging; every ZIP entry and file hash is checked again before installation. The mod automatically backs up replaced files and preserves settings/extras, saves, server data, and other mods. It does not close KSP, install software into Windows, or restart/update the server.

The mod ZIP is approximately 50 KB. `RailgunLogistics-Update-0.2.3.zip` is the internal updater payload; download the ordinary **RailgunLogistics-0.2.3.zip** for initial use. GitHub's **Code / Download ZIP** is source code, not the playable mod download.

If GitHub is unavailable, the installed mod stays usable. Diagnostics/downloads are in `RailgunLogistics-Updates` beside GameData. Each job's `result.txt` records installation or failure. Backups are in `RailgunLogistics-Backups/autoupdate-*`. To opt out, create an empty `GameData/RailgunLogistics/DisableAutoUpdates.txt`. To roll back, close KSP and restore only the mod backup folder; startup returns to the pinned release unless updates are disabled.

## Host release procedure

The mod reads `channel/friends.xml` on this repository's main branch. The descriptor pins a versioned GitHub release ZIP, its SHA-256, supported KSP/Luna versions, and hashes for each mod file. It follows the host-approved channel rather than blindly following the latest release. Trust is control of this GitHub repository over HTTPS, not separate code signing. Never publish credentials, game assemblies, third-party mod packs, saves, or the server Universe.

`Build-Updates.ps1` builds the updater/helper and creates the normal GameData mod ZIP and automatic update payload. It currently reuses the tested 0.2.0 gameplay package byte-for-byte: only the bundled updater changed. Gameplay/server protocol remains 0.2.0/v2, so this server needs no restart. To release future gameplay changes, first point the build at the new tested gameplay payload and bump the version. For server-dependent changes, install/restart/verify the matching server and required mod DLL hash, then promote the Friends descriptor. A draft alone updates nobody's mod.

`Publish-Updates.ps1 -ServerPath '<your Luna server directory>'` previews compatibility checks. Add `-Publish` after approving publication. It verifies the running host's required gameplay hash and uploaded release hashes before promoting the descriptor. Publishing credentials stay on the host and are never bundled. Players do not run PowerShell scripts.

For reproducing the existing gameplay bundle, obtain `RailgunLogistics-0.2.0.zip` from the older release's assets and place it in the source root. Build against your own KSP/Luna references. Source excludes private logs, recordings, sandbox experiments, game references, saves, and backups.

## Credits and verification

Original mod/updater code developed for Kgeg4 with OpenAI Codex (GPT-6.1); MIT license. Uses the installed KSP/Unity APIs, .NET Framework, Luna, ModuleManager, and stock assets on your PC.

26 updater checks cover archive/file integrity and malicious/invalid manifests. Windows integration checks process waiting, after-exit application, backups, preserved settings/extras/save sentinel, and corrupt-update refusal. Native KSP checks are required in addition to .NET build tests: KSP's bundled Mono lacks System.Xml.Linq and System.IO.Compression; the in-game updater uses System.Xml and leaves archive extraction to its automatically invoked Windows helper.
