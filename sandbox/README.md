# Reusable railgun development prototype

This is the separate **single-player development prototype**, including the Frutiger Aero delivery menu and physically interchangeable docking connectors. It is not the multiplayer mod in the 0.2.3 release, and is not distributed through the Friends auto-update channel.

## Current features

- A glossy aqua flight menu with a globe emblem, Segoe UI, green availability badges and a single delivery action.
- **Destination → Supplies → Delivery pod**: select a ship, choose its actual docking port, enter cargo or use **Fill available**, then select one of two persistent pods.
- Pod status cards, Watch controls, collapsible flight details and a compact header. The main action remains visible while the content scrolls on shorter displays.
- Clear disabled states for invalid amounts, full receivers, unavailable pods, unsupported routes and a port already assigned to another delivery.
- Actual stock Junior, standard or Sr. connector fitting, including tested TweakScale resizing. The fitting uses native models, colliders and modules, preserving the existing pod and all its part IDs.
- Isolated delivery cargo and a persistent two-pod fleet. Lost or stranded pods are not silently replaced or made available by waiting.

## Important limits

This prototype is for engineering tests in a separate save. It is **not ready for unattended deliveries**.

- Dispatch currently supports independent receivers in stable **Kerbin orbit**. Minmus, other bodies and surface deliveries are not implemented.
- One pod can have foreground flight control at a time. Unattended background powered flight is unfinished.
- Travel uses KSP universal time at 1x while the simulation runs. Pausing or closing the game does not advance deliveries.
- Full delivery, undocking, powered return, landing and reuse with the current assembly still require complete sequence validation. Pad stability has had failures in earlier fixtures.
- The rail-only launch experiment is paused. Ordinary prototype dispatch retains the earlier vertical rail assist and powered ascent. A pure rail-only orbital launch is not established.
- Shielded, gendered, indexed and other unsupported docking hardware is rejected explicitly.
- The Friends multiplayer channel and released client/server protocol are unchanged.

## Requirements and build

Use Windows, the .NET 8 SDK, your own KSP 1.12.5 build 3190 installation, ModuleManager, MechJeb 2.14.3 and Harmony (`GameData/000_Harmony/0Harmony.dll`). The project references your installed game and mod assemblies; those assemblies and stock assets are not included here.

From the repository root:

```powershell
.\sandbox\Build-Sandbox.ps1 -KspRoot 'D:\Games\Kerbal Space Program'
```

The script builds the addon, runs the independent checks and stages **only** `sandbox/staged/GameData/RailgunSandbox`. It does not install or launch KSP. The code uses the installed KSP/Unity APIs and MechJeb; this is a native game addon, not a browser overlay.

## Manual engineering setup

1. Back up your saves and close KSP. Use a separate local KSP setup, not a Luna/shared save.
2. Copy the staged `RailgunSandbox` folder into that setup's `GameData` folder.
3. Create your own new single-player sandbox named exactly **Railgun-Reusable-Sandbox**. The controller activates only in this named save and the explicit development test saves.
4. Enter flight, open the prototype menu and use **Set up depot** to create its two original physical pods. Let their initial placement settle.
5. Use your own receiving vessel in stable Kerbin orbit, with a supported free docking port and unlocked cargo capacity. Select that vessel/port in the menu or right-click its port and choose **Request pod delivery here**.

The private developer installer and prepared saves are intentionally not included. Building the source does not recreate the developer's station, saved missions or test fixtures. Keep the part definitions installed while a save contains the prototype's custom guidance/cargo parts.

## Verification

The current source passes **122 independent checks**. Native KSP checks additionally verified:

- Physical Sr., resized Junior, unscaled Junior and standard connector fitting; resized fitting persisted through restart.
- Native model scale, colliders and mounting, with measured mounting gaps below 0.4 mm in these fixtures.
- The same two vessel identities, all 29 part IDs and unchanged propulsion fuel through the fitting tests.
- Menu collapse/reopen, actual port selection, resource selection, cargo typing, fill-to-capacity, pod selection and disabled states. Default and compact layouts were visually inspected without GUI layout exceptions.

Those checks do not establish a complete delivery/return flight, cross-size docking, multiplayer or interplanetary operation. Native captures, saves, workstation paths, decompiled API references and private fixture tooling are excluded from this repository.

## Developer controls

Write one command at a time into the current test save's `RailgunSandbox.command`. Commands are consumed and deleted; deletion alone is not completion acknowledgement. Inspect `RailgunSandbox.status.txt` and native state afterward.

General controls include `prepare`, `watch1`, `watch2`, `receiver`, `select-port:<part ID>`, `dispatch1`, `dispatch2`, `snapshot`, `inspect`, `pause-follow` and `save-and-exit`. Dispatch uses the selected receiving port and currently configured cargo. It never substitutes a different port when the saved selection is unavailable.

`-railgun-porttest` selects the separate `Railgun-Port-Selection-Test` fixture. Launch force is disabled and reserved orders stop after actual hardware/cargo fitting. The source supports this fixture but does not include a prepared save. In that fixture, `port-hardware-check` tests native meshes, colliders, mounting and identity; `port-test-release` releases only an unlaunched test reservation without flight or delivery credit. The older `port-coupler-selftest` tests compatibility metadata only.

Test-only UI views: `ui-preview:default`, `ui-preview:ports`, `ui-preview:destinations`, `ui-preview:details`, `ui-preview:invalid`, `ui-preview:compact` and `ui-preview:collapsed`. They do not dispatch a pod and are unavailable in the ordinary save.

## Persistence and credits

Back up `persistent.sfs` and `RailgunReusableFleet.xml` together. Restoring only one can mismatch mission history and physical vessels. Do not reset an order ledger to recover a lost pod.

Original code, facility geometry, globe and menu textures are AI-assisted work for Kgeg4 with OpenAI Codex (GPT-6.1), under the repository's MIT license. Fonts come from Windows at runtime. Stock pod meshes and colliders come from the player's installed KSP parts. MechJeb, Harmony and ModuleManager remain separate dependencies; no game binaries, models or third-party mod binaries are redistributed.
