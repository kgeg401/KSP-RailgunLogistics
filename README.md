# Railgun Logistics — 0.3.7 alpha

A two-pod logistics depot for **KSP 1.12.5 with Luna Multiplayer 0.29.2**. The pods physically launch, fly to a ship in Kerbin orbit, dock, deliver fuel, return, land and prepare for reuse.

Use the playable mod ZIP from [Releases](https://github.com/kgeg401/KSP-RailgunLogistics/releases). GitHub's source ZIP does not include the compiled mod. Every player and the host need the matching release; the published update channel determines which build is offered automatically.

## Using the depot

1. Enter flight on your receiving ship in stable Kerbin orbit.
2. Open **Railgun Logistics** with its toolbar icon, or right-click the receiving docking port and choose **Request railgun supplies**.
3. Select the exact port, fuel and amount, then send an available pod. On a new depot, use **Set up depot** once to create its two persistent pods.
4. Let the physical flight finish. Cargo transfers only after native docking. The pod then undocks, returns and refits before becoming available again.

**Close the panel completely with × or switch its toolbar icon off.** It starts hidden, can be reopened with the toolbar, and stays hidden across vessel changes after you close it. Hiding the panel does not cancel an active delivery.

For automatic coast warp, every connected player enables **Auto warp: On**. The clients agree on safe orbital coasts, slow down and rejoin the same Luna timeline before guidance continues. Launch, docking and atmospheric recovery remain at normal physics speed.

## What 0.3.7 changes

- Uses the verified native approach and vertical-capture return configuration in normal logistics flights.
- Enables the scoped Luna docking adapter without special test launch arguments.
- Keeps the physical shell, mechanical loading sequence, rail-only ascent and separate circularization burn.
- Waits for saved native fuel and empty cargo tanks before acknowledging a completed refit.
- Fits an older surviving pod with its first shell only after it physically returns to the depot. Original vessel IDs and delivery history are retained; lost pods are not regenerated.
- Adds a full menu close control and fixes the toolbar's off action.

## Installation and updates

Requirements: Windows, KSP 1.12.5/build 3190, Luna 0.29.2, ModuleManager, MechJeb 2.14.3 and Harmony 2.2.1 from your matching mod pack. The download contains this mod, not those dependencies or the game.

Close KSP and back up the existing mod folder. For a first install, copy **GameData/RailgunLogistics** from the standard release ZIP into the game's GameData folder. Preserve your existing Settings.xml. The ZIP whose name includes **Update** is the updater's internal payload.

The host also installs **Server/Plugins/RailgunLogistics.Server.dll** while the server is stopped and updates its required-client hash with the included installer:

```powershell
.\Install.ps1 -KspPath 'D:\Games\Kerbal Space Program' -ServerPath 'D:\Servers\LunaMultiplayer'
```

Existing supported clients check the published channel at startup. When an update is downloaded, quit KSP normally, wait for the helper, and reopen before joining the updated host. Updates preserve saves, local settings and unrelated mods. The host must be updated separately. See [update details](README-Updates.md).

## Scope and verification

This is an experimental **Kerbin-orbit** release. Minmus and interplanetary delivery are not implemented. Keep the controlling players connected while a pod is flying. Available pods, fuel, native docking and a successful physical return are required; a lost or fuel-starved fleet is not repaired by installing an update.

The 0.3.7 ordinary-configuration check completed one full native two-client launch, delivery, return and persisted refit, including coordinated coast warp. Touchdown was 2.93 m from the pad with all 29 original parts. The older-pod shell construction and menu controls also passed their native checks. See [validation and limits](docs/Release-0.3.7-Validation.md). One controlled route does not establish repeated reliability for every receiver.

## Building and licence

Use the .NET 8 SDK and your own installed game/Luna assemblies. Set `KspRoot` and `LunaRoot` when building. `Build.ps1` stages the custom client/server package. Game assemblies, third-party mod binaries, private saves and local evidence are not distributed.

Original mod and updater code developed for Kgeg4 with OpenAI Codex; MIT licence. The mod uses the player's installed KSP/Unity, Luna, ModuleManager, MechJeb and Harmony.

Earlier versions and validation reports remain in the [changelog](CHANGELOG.md) and Git history. Use the release notes and published update channel to identify the current build.
