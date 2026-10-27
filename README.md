# Railgun Logistics 0.2.0

**Download [RailgunLogistics-0.2.3.zip](https://github.com/kgeg401/KSP-RailgunLogistics/releases/download/v0.2.3/RailgunLogistics-0.2.3.zip).** Close KSP and copy the included `RailgunLogistics` folder into `GameData`. Launch KSP normally; the mod checks GitHub for approved updates and applies them after KSP closes. No installer, extra launcher, new Luna client, or GitHub login. See [copy-and-play instructions](README-Updates.md). Gameplay and server protocol remain the tested 0.2.0/v2 build.

Kerbin emergency supplies for KSP 1.12.5/build3190 and Luna Multiplayer0.29.2. Built for Kgeg4 with Codex; code and procedural visuals are AI-assisted original work. Requires the already-installed ModuleManager and Luna client. This package contains no KSP assets or game assemblies; its launcher references a stock model on your own install.

## Use

Fly the ship needing supplies in orbit, above the atmosphere. It needs a free **standard Clamp-O-Tron (size1)** docking port with a clear approach. Junior, Sr., gendered and rotation-snap ports are not supported in this version. Right-click a command pod/probe core and choose **Request railgun supplies**, or click the blue twin-rail toolbar icon. Select the port, enter fuel quantities (or use **Fill**), then **Dispatch from Kerbin**. Units are KSP resource units, not litres or tonnes.

Default timing is **30 seconds charging + 120 seconds transit at Kerbin**, or **30 seconds + 600 seconds at other bodies**, followed by the physical approach (about another minute in the orbital test). The countdown uses real elapsed seconds, independent of timewarp. The delivery board shows the shared queue. The receiving ship must be loaded and under the requesting player's control to receive cargo. If you switch ships or disconnect, the cargo waits. Return to the ship to finish delivery. It can follow the receiving command part through docking.

After the countdown, a **real persistent cargo vessel** arrives 60 m outside the selected port, with its own stock docking port, collider, mass and locked resource tanks. At **1x time**, assisted guidance brings it down the docking corridor; stock docking magnets perform capture and join the two vessels. **Cargo only transfers after that connection exists**, and is subtracted from the pod's tanks. The empty pod stays attached. Use the normal docking-port **Undock** action when you are finished with it.

Keep engines/RCS off, SAS on and the port clear during approach. An obstructed/occupied port or a full/locked receiver tank makes the delivery wait. Wait for the board to say **Docked and unloaded** before undocking. The insertion and approach are assisted; this does not simulate a launch from the surface or a fuel-limited interplanetary trajectory. The optional **KRL-6 Kerbin Railgun Logistics Terminal** still opens the same menu.

The current version is an emergency service with no fuel-stock or funds debit. The charging stage is a timer; it does not consume a physical launcher's electricity. Those are explicit gameplay choices for this first assisted version, not simulated launch physics.

Supported by default: LiquidFuel, Oxidizer, MonoPropellant, XenonGas, LqdHydrogen, LqdMethane, ArgonGas, Lithium and ElectricCharge. Only resources already present in the ship's unlocked tanks appear. Locked tanks remain locked. Full tanks never overfill; if the requested cargo no longer fits, the entire delivery waits until sufficient unlocked space is available. There is one pending dispatch per receiver. Cancel before transfer begins if necessary.

## Installation

Download the playable mod ZIP from Releases. Close KSP. Copy its **GameData/RailgunLogistics** into the KSP folder, merging GameData. Do this on **every player's PC**. No installer is required. It is a small custom-mod package, not the third-party mod pack. The GitHub source ZIP does not contain the compiled mod.

For the host, close the Luna server cleanly first, then copy **Server/Plugins/RailgunLogistics.Server.dll** into its Plugins folder. Run the included installer to also allow the new part and require matching client DLLs in mod control:

```powershell
.\Install.ps1 -KspPath 'D:\Games\Kerbal Space Program' -ServerPath 'D:\Servers\LunaMultiplayer'
```

Players only copy the mod folder; the script above is for initial server administration. Keep the existing KSP/Luna client. The updater backs up its own replaced files and preserves settings, saves, and unrelated mods. It does not download the game, install Luna, or change the server password, name, or mode. Back up saves/Universe separately before first use.

Single-player settings: `GameData/RailgunLogistics/Settings.xml`. Single-player order ledger: `saves/<save>/RailgunLogistics.xml`. Luna settings/ledger: `Universe/RailgunLogistics/Settings.xml` and `Orders.xml`, created on first server start. Change server settings while stopped. Ledger files have an atomic `.previous` revision; **do not delete/reset orders to fix an error**, as that discards delivery history. Keep these files with the corresponding save/universe backup.

## Persistence and multiplayer behavior

The server authorizes each dispatch and grant against the current control lock. Each order has a deterministic cargo-vessel ID and a persistent module on its physical pod. The pod snapshot must be durable before completion can be confirmed. Reconnecting finds that pod again; an acknowledged pod missing from the client waits for synchronization instead of creating replacement fuel. A receipt, emptied pod tanks, dock connection and receiver resources are saved together after unloading. Repeated grants inspect the receipt and do not unload twice. Storage errors stop ledger mutations instead of resetting state. Protocol v2 requires the updated client and server together; it does not fall back to the old instant-transfer mechanism.

Standard Luna vessel saving is still part of the persistence boundary. As with the existing game, deliberate save rollback or a restored mismatched universe/ledger is outside the duplicate-delivery guarantee. Two simultaneous real clients and crash recovery must be tested before treating the first release as established on the shared server.

## Verification status

- Client and server compile against the locally installed game/Luna assemblies, with zero build warnings/errors.
- 34 automated checks cover countdowns, cancellation, ownership, persistent pod identity/acknowledgement, duplicate packets, restart history, invalid quantities/resources, capacity limits, atomic saves and persistence failure.
- Isolated Luna server successfully loaded and registered the plugin on loopback UDP8817; no public listing.
- The previous0.1.0 instant-transfer implementation passed native one-client Luna delivery and restart tests. Those tests do not establish physical docking behavior in0.2.0.
- Native0.2.0 physical docking passed in an isolated orbital sandbox and on a Luna loopback server with one real client: separate71-part station and1-part pod became one72-part vessel through the stock docking event. The pod lost1,000mono, the station gained exactly1,000, its LiquidFuel stayed unchanged, both ports saved reciprocal Docked states, and the server committed one receipt asDelivered. The empty pod remained attached in orbit.
- A diagnostic reader briefly blocked an atomic ledger replacement during the single-player run. The depot stopped rather than granting more cargo; reloading the saved docked pod recovered the delivery once, without another pod or duplicate fuel. The uninterrupted Luna run passed normally. A forced process-crash test and two simultaneous real clients remain untested.
- The earlier0.1.0 clean lab-server restart retained its120-unit delivery. This is historical validation, separate from the0.2.0 docking test.
- Receiver patch/menu and shared server queue were exercised in KSP. Launcher rendering passed after adding the ReStock whitelist; see MODLOG.md for evidence. The terminal is a functional placeholder model, not finished artwork.
- `Railgun-Physical-Docking-Demo.mp4` shows the native Luna approach, stock docking and cargo handoff. Waiting time is explicitly shortened in the edit. The older videos demonstrate the superseded instant-transfer version.

## Build / source

Run `Build.ps1` with the .NET8 SDK. The client targets net472 and the server net6.0. `Directory.Build.props` contains the local assembly paths; override KspRoot/LunaRoot when building elsewhere. It compiles, runs the checks and stages `release/`. Do not distribute `references/`, `lab/`, game assemblies, personal saves or backups.

Disable/update only with KSP/server stopped. Moving a mod folder out does not revert fuel changes already received. Do not restore an old whole universe merely to remove this mod, because that would discard later progress. Keep the part definitions while any vessel still uses a launcher terminal or physical cargo pod.
