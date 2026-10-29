# Launch and landing checkpoint

Work started 2026-10-28 03:33 UTC, with a three-hour checkpoint. Public baseline: 0.3.1.

Follow-up: [October 28 reusable-launcher development](Reusable-Launcher-Hour-2026-10-28.md) records the subsequent Junior launch fix, native launch/reload results and a longer pre-entry-to-pad recovery. The uninterrupted orbit return remains unverified.

## Delivery estimate

The reusable-pod menu shows a delivery estimate on each active pod card and in flight details. A native orbital encounter is projected through the final planned maneuver patch, then includes the rendezvous closing leg and a range for docking alignment/capture. Docking estimates use the installed controller's speed limit. The projection is display-only: reaching the estimate cannot transfer cargo, change phase, refuel or release a pod.

Numbers use KSP universal time. A pause does not count down on the wall clock. Missing flight authority, invalid trajectories, a changed order/phase, time rewind or an expired arrival window invalidate the countdown. Before orbit insertion, or while the controller has not yet planned a viable encounter, the menu explicitly says it is calculating. This is an advisory range, not an on-time guarantee or a substitute for a physical docking receipt.

## Experimental rail launch

The earlier 40 km / 35 degree / 2,400 m/s experiment destroyed exposed parts through aerodynamic heating. The current candidate uses a 100 km / 30 degree guide with a 2,200 m/s drive target, reducing the nominal acceleration from 72 to 24.2 m/s². Atmospheric drag still affects the attained muzzle speed. The guide supplies real forces only while the pod is on it; engines and RCS remain off through rail travel and coast. A separate native circularization maneuver is limited to 800 m/s.

This remains restricted to the isolated development flag. Ordinary and Luna reservations retain rail assist followed by powered ascent until multiplayer acceptance is established. The candidate must not be called a delivered multiplayer rail-only feature.

## Native evidence at the checkpoint

Two isolated launches reached approximately 263 x 267 km Kerbin orbits. Both retained all 29 original parts and 100 locked cargo units. Engines and RCS were off, with unchanged propulsion fuel, through rail travel and coast; the separate circularization correction was approximately 725 m/s. Peak normalized part temperature was approximately 0.434. Each left approximately 4,194 liquid-fuel units for subsequent flight. These are larger-connector launch results. An unscaled Junior connector test held at cradle alignment, approximately one metre from its target; it is an unresolved launch case.

The rendered menu showed a 2-5 minute delivery window in a prepared native near-receiver fixture. Physical docking occurred 231.86 seconds after the first available estimate, within that first 122.50-302.00 second window. Exactly 100 MonoPropellant transferred after real stock docking, then the pod physically undocked. A second trial on the exact released DLL observed transfer after 232.96 seconds, also within its first 122.65-302.30 second window. Physical cargo was empty with one matching receipt and no completed-flight credit. A 58-second pause retained the same game time and ETA window; the released build also passed a settled-pause check and visually hid its delivery panel. The receiver's initial orbit was authored near the real post-launch pod; these are not launch-to-delivery demonstrations.

The unchanged post-launch pod survived atmospheric entry with all parts and cargo. Default native recovery landed intact but 2.96 km short of home. Saved-descent replays exposed excessive fuel use during final correction. The optional experimental controller now takes ownership of the final-descent stage before stock guidance writes a conflicting throttle mode, preserving its throttle controller's state and governing horizontal travel by stopping distance.

One replay from the saved approximately 12 km descent completed native recovery **0.458 m from the home pad**, upright **0.297 degrees**, surface speed **0.003 m/s**, stable for **5.10 seconds**. All 29 original parts and 100 locked cargo units remained. It used its last propulsion on final approach. This return-only test awarded no delivery, refit or completed-flight credit. A read-only audit accepts this result and rejects the intact 2.96 km touchdown.

The uninterrupted 0.3.2 return started from a byte-identical copy of the original post-launch native save. It retained its original parts and cargo, used a monotonic 1x simulation clock and survived atmospheric entry, but **exhausted propulsion before touchdown**: approximately 3.16 m horizontally from the pad, 193 m above sea level (roughly 120 m above the pad), descending at 15.18 m/s. It correctly became Stranded and received no recovery, refit or flight credit. The saved-descent success therefore does not establish uninterrupted return reliability.

The final experiment is a 0.3.3 development candidate which avoids the fixed 2 m/s hover-like descent during near-pad correction below 300 m. It uses measured clearance, lateral travel/braking time and a 12 m/s ceiling, retaining the slow last 30 m. Its test started from the failed return's unchanged native approximately 944 m checkpoint, with approximately 804 liquid-fuel units. No native pose, resource, thermal or part-ID state was changed. This replay **passed**: touchdown 0.720 m from the pad, upright 0.189 degrees, speed 0.00136 m/s, stable for 5.08 seconds, with all 29 parts and locked cargo intact. The read-only acceptance audit passed. It cannot qualify a complete new return from orbit; that remains the next required test.

Published and installed 0.3.2 retains the frozen tested client. The 0.3.3 candidate is development source only; neither the host nor the update channel is promoted to it. Version 0.3.2 passes 157 fleet, 69 shared-authority, 34 classic-delivery and 32 updater checks, plus helper integration. The development fleet checks total 159. Package/source lint passed after verifying that the installed-game duplicate flags were our own tracked mod files and previous updater binaries. Every one of the 203 public universe files was unchanged by installation; both original public pod records were unchanged after host startup.

A complete multiplayer dock/deliver/return cycle, repeated landing reliability, every connector configuration and interplanetary routing remain unverified. The experimental rail and precision-pad controllers are not enabled in ordinary or Luna dispatches.

Private logs, native fixture saves, source fingerprints and diagnostics are kept outside the public source tree. Production universe/pods are not reset for testing.
