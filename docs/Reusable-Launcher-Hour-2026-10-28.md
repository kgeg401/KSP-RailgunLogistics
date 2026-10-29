# Reusable launcher development, October 28, 2026

Scope: one hour starting 2026-10-29 01:02:32 UTC, focused on the experimental reusable launcher. Public baseline remains 0.3.2. Work below uses the private 0.3.3 candidate; the production game, server, universe and update channel are not deployment targets for this experiment.

## Changes

The Junior connector previously settled about 0.99 m sideways from the cradle, at 0.33 m/s and with its attitude aligned, then never passed the old 0.7 m spherical capture gate. The gate now measures along-guide and lateral distance separately. It requires at most 0.7 m along-guide error, 1.25 m lateral error, less than 1 m/s surface speed and less than one degree of attitude error. This is inside the existing 3 m running-guide envelope. Invalid measurements still reject capture; propulsion and guide force rules are unchanged. The flight detail shows the individual alignment measurements.

Native testing also exposed two development startup faults. Luna's initial blank-save selection could override an explicit isolated-test flag. An airborne cradle pod could be destroyed by native KSP while the distant receiver was initially focused. Development startup now selects the exact save named by its flag, refuses conflicting test modes or an active Luna connection, and focuses the original first pod for launch and return fixtures. A missing pod fails startup rather than silently selecting a different vessel.

A bounded Windows observer now binds to the exact game process and installation, detects premature exit, writes native snapshots, and requests a normal save-and-exit at completion or timeout. It never kills a process or edits vessel state. A separate read-only audit distinguishes an uninterrupted return from a saved-descent replay: it pins the initial native save hash, requires an intact initial Kerbin orbit, checks the original manifest and continuous simulation clock, and rejects resumed/interrupted evidence. Neither audit grants delivery or completed-flight credit.

## Native results

| Test | Result | Limits |
| --- | --- | --- |
| Junior cradle through native orbit insertion | Passed: approximately 347 x 349 km orbit, 29 original parts, 100 locked cargo units, original pod/order/connector IDs. Muzzle speed 2,060.6 m/s; separate circularization burn 667.5 m/s; peak normalized temperature 0.5755. | Isolated launch. No delivery, docking or return claim. |
| Recorded pre-entry checkpoint through home-pad recovery | Passed: native LANDED, 0.0144 m horizontal pad error, 0.230 degrees upright error, 0.00267 m/s, stable 5.12 s. All 29 original parts and 100 locked cargo units remained. About 140.57 liquid fuel remained. Peak normalized temperature 0.96194. | Starts from the recorded 90 km descent checkpoint, not from orbit. No cargo transfer, refit or completed-flight credit. |
| Junior pod save/reload in orbit | Passed: original pod, order, 29 part IDs and Junior connector retained; 100 locked cargo units and propulsion amounts retained; stable orbit remained. | Separate reload after the launch, not continuous launch-to-delivery evidence. |
| Clean final-descent reload | Passed: landed 0.204 m from home, upright 0.239 degrees, speed 0.00239 m/s, stable 5.12 s, original parts/cargo preserved. | Replay of the recorded sub-1 km checkpoint with separate diagnostics. Does not add an uninterrupted orbit return. |
| Uninterrupted original-orbit return | Incomplete: the game exited before entry; preserved as interrupted evidence. | Does not establish a successful complete return. |

Both successful flight audits checked normal native physics and thermal limits. Rail travel and coast used zero engine thrust and no RCS or propulsion consumption. Guide force ended at departure. Recovery fuel decreased without a refill. Test snapshots retained the physical pod; no orbit insertion, velocity override, replacement pod or resource restoration was used to obtain these results.

One earlier pre-entry attempt was interrupted when stopping a monitor also closed its attached game. The passing attempt launched the game and monitor in separate sessions and exited through the mod's normal save-and-exit command. Earlier startup failures and the interrupted orbit run remain in the private evidence folder; they are not counted as successes.

## Verification and evidence

The tested integrated client DLL SHA-256 is `dcc673c9b3caf7948d8c3892295ae883825d5ce898b008cf3b1e056ea4094864`. This fingerprint identifies the native-tested build, rather than a subsequent untested rebuild.

Private evidence root: `<private-evidence-root>`.

- `junior-capture03`: paired native save/ledger, launch telemetry, status trace, launch acceptance report and Unity log.
- `return10-entry-033`: exact pre-entry initial pair, descent checkpoints, paired native landed save/ledger, acceptance row, thermal/fuel trace, process completion summary and approach screenshot.
- `return08-full-033`: incomplete full-orbit attempt; never a passing full-return result.
- `junior-reload`: before/after native save and ledger, live status, inspected screenshot and 25-second native orbit video. The video shows the orbit after reload, not launch or delivery; it cannot replace the original launch trace.
- `return11-descent-reload`: native reload during final approach landed intact at 0.192 m from home with approximately 146 liquid fuel. Its strict audit rejected duplicate acceptance rows inherited from the previous lab run. This observational result is not a clean accepted second run; both rows remain preserved. A final-approach video shows part of this descent, with that scope captioned.
- `return12-clean-descent-reload`: repeated the same native final-descent checkpoint after archiving the old diagnostics. The strict landing audit passed all 11 checks; the game saved and exited normally. No duplicate rows were edited or discarded from the preceding evidence.

When starting a separate checkpoint replay, archive the previous lab diagnostics before launch. Restoring only the native save and fleet ledger does not reset an existing `LandingEvidence.tsv`; appending the same run ID from two branches correctly fails the one-acceptance check. Preserve the original logs rather than deleting duplicate rows to manufacture a pass.

Read-only commands, run from the source root:

```powershell
python sandbox/tests/audit_rail_flight.py <private-evidence-root>\junior-capture03
python sandbox/tests/audit_landing.py <private-evidence-root>\return10-entry-033
python -m unittest discover -s sandbox/tests -p test_return_audit.py
dotnet run --project sandbox/tests/SandboxTests.csproj -c Release
dotnet build src/Client/RailgunLogistics.csproj -c Release --nologo -p:NuGetAudit=false --ignore-failed-sources
```

The fleet suite passes 171 checks, shared-authority suite 69 checks, return-evidence rejection suite 7 tests, and the integrated client builds without warnings or errors. The return auditor also rejects a partial landing report whose remaining checks were never produced. Test-count totals are not substitutes for the native flight results above.

## Remaining acceptance work

The next flight must start from the original captured post-launch orbit and complete its whole return without loading a later checkpoint. Then repeat a full physical launch, rendezvous, docking, cargo transfer, undocking and home return under Luna authority, including a reconnect. The small remaining fuel reserve and high reentry temperature need repeated reliability testing. These experimental controls remain opt-in until that evidence exists.

This hour does not publish a release or install the development DLL into the production game/server. The shared save is preserved.
