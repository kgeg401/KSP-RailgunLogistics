# Railgun Logistics client updates

## 0.3.7 integrated alpha

The normal client now includes the tested return configuration, the scoped Luna docking adapter, physical depot refit checks and a fully dismissible menu. It does not require engineering launch switches. One ordinary-configuration two-client launch-to-refit route passed; this remains an experimental Kerbin-orbit release. See [validation and limits](docs/Release-0.3.7-Validation.md).

Use the standard **RailgunLogistics-0.3.7.zip** from Releases for manual installation. Every player needs the matching GameData/RailgunLogistics folder, and the host needs the matching server plugin and client-hash requirement. Keep existing Settings.xml and unrelated mods.

An existing reusable client follows **channel/reusable.xml**. After the new build is promoted, start KSP, allow the update download, quit normally, wait for the helper, then reopen. The helper checks hashes and dependencies, keeps a backup, and never updates the server. Older classic clients retain their compatible updater-bridge migration.

The menu starts hidden. Use the toolbar to open it, **×** to close it entirely, or the toolbar's off action to hide it. Closing the panel leaves an active flight running. Right-clicking a receiving docking port explicitly opens the delivery menu again.

Existing fleet records and vessel IDs are retained. A surviving older bare pod receives a shell during a physical home-depot refit. Lost or stranded pods remain unavailable. An update does not reset the universe, discard an in-flight order or manufacture replacement fuel. Save/Universe backups must stay paired with the fleet ledger.

Update diagnostics: **RailgunLogistics-Updates** beside GameData. Replaced-file backups: **RailgunLogistics-Backups/autoupdate-***. Disable automatic updates with **GameData/RailgunLogistics/DisableAutoUpdates.txt** when deliberately rolling back a matched client/server pair.

## Older clients and rollback

The classic 0.2.3 updater first installs the compatible 0.2.5 updater bridge without changing its gameplay DLL. After quitting and reopening, that updater can fetch the current reusable client. This one-time migration takes two update cycles. Later supported reusable releases use one ordinary quit-and-reopen cycle.

A deliberately installed manual engineering preview may include DisableAutoUpdates.txt. That opt-out prevents automatic downloads. Keep it for a deliberate rollback; remove it only when you want to return to the shared published channel. Do not join a host with mismatched gameplay files.

The host installs the matching plugin and required-client hash while stopped, preserves its Universe and settings, then restarts. Publish-Reusable.ps1 checks the running host and immutable release asset digests before promoting both channels in one commit. A partial upload leaves the previous channel descriptors in place.

Earlier checkpoint reports describe their historical binaries and should not override the current installation instructions.
