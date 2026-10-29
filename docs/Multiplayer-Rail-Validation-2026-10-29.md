# Multiplayer rail update validation

Development source: 0.3.5. Testing used two native KSP 1.12.5 clients and a separate Luna 0.29.2 host on loopback. The friends server and its paired universe/fleet stayed untouched during acceptance. This is source publication, not promotion of the friends installation.

## Scope

The multiplayer path uses the existing 112 m vertical guide, rail assist and powered ascent. It now renders the detailed bare-pod rail variant, collars, hydraulic hardware and loading indicators. The long angled rail-only ascent, protective shell and shell-specific overhead transport remain explicit single-player engineering modes. Publishing their source does not enable them on Luna.

## Problems found and changes

The first automated flight load ran before Luna had finished populating the tracking-station vessel. The separate validation addon now waits for a stable native vessel before entering Flight. It is excluded from the normal gameplay build. Its explicit loopback commands can capture the game frame, inspect model meshes and native module snapshots, or exercise a tracking-station transition; they do not edit fuel, position, velocity or simulation time.

Leaving a multiplayer flight exposed another Unity teardown ordering case: carrier children and the loader root can already be destroyed before the parent cleanup runs. The carrier, loader and rail root now tolerate that order. The corrected native multiplayer exit produced zero exception blocks from this mod; unrelated installed-mod exceptions remain visible in the retained logs.

Dispatch also exposed stale client-side construction readiness after unloading the ground scene. Both physical pods were intact and the host's native snapshots recorded `placementSettled=True`, while an unloaded client rejected dispatch. A fresh connection read the correct flag. Multiplayer reservation now checks the host's current native placement evidence instead of rejecting on that stale local flag. The authority rejects an unsettled native pod even if the proposed client manifest is otherwise valid. Single-player retains its direct native placement check. Two regression checks verify rejection and unchanged storage; these brought authority coverage to 71 before the animation-relay checks below.

The first actual loading sequence exposed a separate observer problem: the pilot reached Charging while the second client's local physics-based reconstruction remained Aligning. Remote vessel interpolation is not reliable capture evidence. The pilot now relays short-lived, server-admitted animation samples using native game time. Admission requires the current order, epoch, unexpired pilot lease, native vessel ownership and the same subspace. Invalid, late or foreign samples are dropped. This path changes no fleet revision, durable flight state, control lease or physical launch gate. The observer renders these samples rather than independently deciding whether the remote pod has aligned. Missing samples expire; a new pilot or scene still needs fresh physical capture.

## Evidence so far

- Both clients connected to the host and shared the same two fleet identities.
- Native scene reload retained both original 29-part pods; the second client also loaded the original pod with 29 parts. Paired ledger comparison retained the vessel IDs and exact original part IDs.
- The native multiplayer rail mesh inspection reported finite, nondegenerate outward surfaces, pin engagement/withdrawal, and the 2.15 m bare-pod bore. It measures the rendered guide; it does not establish physical collisions for the artwork.
- Integrated client/server builds pass without warnings/errors. The fleet/loader suite has 2,762 checks and authority has 86, including view admission, independent launch gating and different rendering cadences. The classic logistics suite passed 34 checks; updater validation passed 32.
- The real update helper test waited for its exact synthetic game process while the other real KSP clients were running, then applied the candidate after exit. Settings, extra files and a save sentinel were preserved; backups were retained; a corrupt archive was rejected. The test now waits for helper startup under load instead of assuming its result file exists after one second.

The completed earlier [rail/loader checkpoint](Rail-Loader-Checkpoint-2026-10-29.md) remains the bounded evidence for the isolated protective-shell flights. It is not substituted for multiplayer flight evidence.

The readiness-fix client (`c813382b3446efbf9f92a23786212eda5cc0efc9a86707f9f8c6ac3b308f8263`) completed native multiplayer loading, physical Junior-port fitting, rail assist, powered ascent and a saved 111.956 x 112.163 km orbit with both original 29-part pod identities intact. The parked spare stayed available. This run exposed the observer-animation failure above and is preserved independently of the subsequent presentation fix. It awarded no cargo delivery or return credit.

## Final native build

The final integrated client SHA256 is `ab9ca6b3f5781ef10490c76fd82c9d8f5c51291e69f2b18df8f44a155f9fe9d7`; its server SHA256 is `420a0dbabce9445e78834782c6e0148f826f0b73b1095a5227d80584fdd7f165`. Both native clients connected using this exact gameplay binary and the matching host pin.

After a native tracking-station reload, the pilot's unloaded local snapshot again reported unsettled. Dispatch now succeeded against the host's settled native evidence. It retained the original identity while physically fitting the Junior connector, completing loading and rail assist, and using native powered ascent/circularization to reach a saved **111.976 x 112.142 km** orbit. Both original 29-part assemblies survived, the spare remained Ready, and neither delivery nor return credit was awarded. Continuous observations retained 1x time, 0.02-second physics steps and disabled resource/temperature/damage cheats.

Native transform samples show the leading collar moving from -2.8 m to 0 m on both clients, and the leading locking pin traversing the same 1.826 to 3.936 m range. The 37 pilot and 43 observer samples share one order/pilot. Comparing the one-second samples by interpolation gives maximum differences of 0.380 m for the collar and 0.327 m for the pin; this establishes the restored observer sequence, not frame-perfect animation parity. The actual native frames and full sample files are retained privately.

Both clients exited after saving. Their final logs contain zero exception blocks or error entries from this mod, including the tracking-station and exit cleanup paths. The isolated host completed its final backup and shutdown. Ctrl+C was ignored by one hidden test-host console; Ctrl+Break requested its native shutdown successfully, with the final completion message recorded. The control helper itself receives that console signal, so its exit code is not used as evidence of the server outcome.

The final drop-in package's updater checks and real helper integration passed again with the final gameplay payload. They do not establish a live update to another person's PC.

## Deployment boundary

The new source is suitable for reviewing and reproducing these bounded multiplayer tests. The existing friends host and automatic update channel remain on 0.3.2. Full consecutive multiplayer docking, cargo delivery and return remains a separate acceptance requirement; no scene replay or source-only test establishes it. The protective-shell and long-guide experiments remain restricted to their explicit local fixtures.

Private evidence is retained under `D:\GAMES\KSP-Railgun-Previews\multiplayer-rails-20261029`, including rejected attempts and pre-test client backups. Private native snapshots, logs, assemblies and game files are not part of the public source.
