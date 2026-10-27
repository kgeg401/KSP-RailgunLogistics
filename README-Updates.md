# Railgun Logistics: startup updates

For Windows, KSP 1.12.5 build 3190, Luna Multiplayer 0.29.2, and ModuleManager.
Bundle 0.2.1 adds a separate updater to the native-tested 0.2.0 physical-pod client.
The gameplay DLL and server protocol are unchanged. No server restart is needed for this bundle.

## One-time installation for both players

Download and extract `RailgunLogistics-0.2.1-Setup.zip`. Close KSP, then run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-Client.ps1 -KspPath 'D:\Games\Kerbal Space Program'
```

Use the real game directory, not a Steam shortcut. The installer verifies the game/Luna version,
backs up existing Railgun Logistics files, and preserves your mod settings, saves, and other mods.
It does not install KSP, Luna, ModuleManager, or the rest of your third-party mod pack.

Start KSP normally through Steam or its executable. At the main menu the updater checks the public
GitHub Friends channel once per game run. No GitHub account/token is required on your brother's PC.
If an update is available, it downloads in the background and shows an acknowledgement window.
Quit normally, wait a few seconds, and reopen KSP. An external helper waits until that KSP install
is closed, verifies checksums again, preserves local settings/extras, and swaps only this mod folder.
It does not close KSP for you or replace a DLL already loaded in the running game.

If GitHub is unavailable, the installed mod stays usable. Diagnostics/downloads are in
`RailgunLogistics-Updates` beside GameData. Each job's `result.txt` records installation or failure.
Backups are in `RailgunLogistics-Backups/autoupdate-*`. To opt out, create an empty
`GameData/RailgunLogistics/DisableAutoUpdates.txt`. To roll back, close KSP and restore one mod
backup folder only; a later startup will return to the pinned release unless updates are disabled.

## Host release procedure

The updater reads `channel/friends.xml` on the repository's main branch. It does NOT blindly follow
GitHub's latest release. The descriptor pins a versioned GitHub release ZIP, its SHA-256, supported
KSP/Luna versions, and hashes for each client file. ZIP paths outside the known mod files are rejected.
Trust is the configured GitHub repository over HTTPS; this is not an independently signed release system.
Never put private credentials, game assemblies, third-party mod packs, saves, or the server Universe on GitHub.

`Build-Updates.ps1` builds the updater/helper and packages 0.2.1 without rebuilding the gameplay DLL.
It deliberately reuses the existing tested 0.2.0 package. To release future gameplay changes, first
extend the build input to the new tested payload and bump its version; do not keep reusing the old ZIP.
Compile/test both client and server when gameplay changes require it. Close the server cleanly,
install the matching server/plugin and client hash requirement, restart, and THEN promote the new
Friends descriptor. Publishing a draft alone does not update either player's game.
The server is never automatically restarted or updated by a client updater.

Upload the client ZIP and setup ZIP to the matching GitHub release tag, then copy that build's
`friends.xml` to `channel/friends.xml` only when the running server accepts the pinned client SHA.
`Publish-Updates.ps1 -ServerPath '<your Luna server directory>'` previews this check. Add `-Publish`
after approving the release: it uploads/publishes the assets before promoting the descriptor. It
refuses to promote if server files changed since that server process started. The GitHub CLI needs
the host's publishing credentials; players' updaters never use or store those credentials.
The public source is a selected clean snapshot, excluding MODLOG, native recordings, sandbox work,
game references, saves, and backups. Build the original gameplay payload with local KSP/Luna
references; for reproducing the original update bundle, download its `RailgunLogistics-0.2.0.zip`
base package release asset into the source root before running `Build-Updates.ps1`.
All players must install this updater once; an older 0.2.0 installation cannot acquire it on its own.
The separate experimental sandbox/reusable-launcher work is not included.

## Credits and verification

Original mod/updater code developed for Kgeg4 with OpenAI Codex (GPT-6.1); MIT license.
Uses the installed KSP/Unity APIs, .NET Framework, Luna, ModuleManager, and stock assets on your own PC.
Build/test results and native startup verification are recorded in the project MODLOG.md.
