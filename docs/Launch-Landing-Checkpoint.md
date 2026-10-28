# Launch and landing checkpoint

Work started 2026-10-28 03:33 UTC, with a three-hour checkpoint. Public baseline: 0.3.1.

## Delivery estimate

The reusable-pod menu shows a delivery estimate on each active pod card and in flight details. A native orbital encounter is projected through the final planned maneuver patch, then includes the rendezvous closing leg and a range for docking alignment/capture. Docking estimates use the installed controller's speed limit. The projection is display-only: reaching the estimate cannot transfer cargo, change phase, refuel or release a pod.

Numbers use KSP universal time. A pause does not count down on the wall clock. Missing flight authority, invalid trajectories, a changed order/phase, time rewind or an expired arrival window invalidate the countdown. Before orbit insertion, or while the controller has not yet planned a viable encounter, the menu explicitly says it is calculating. This is an advisory range, not an on-time guarantee or a substitute for a physical docking receipt.

## Experimental rail launch

The earlier 40 km / 35 degree / 2,400 m/s experiment destroyed exposed parts through aerodynamic heating. The current candidate uses a 100 km / 30 degree guide with a 2,200 m/s drive target, reducing the nominal acceleration from 72 to 24.2 m/s². Atmospheric drag still affects the attained muzzle speed. The guide supplies real forces only while the pod is on it; engines and RCS remain off through rail travel and coast. A separate native circularization maneuver is limited to 800 m/s.

This remains restricted to the isolated development flag. Ordinary and Luna reservations retain rail assist followed by powered ascent until multiplayer acceptance is established. The candidate must not be called a delivered multiplayer rail-only feature.

## Native evidence at the checkpoint

Two isolated launches reached approximately 263 x 267 km Kerbin orbits. Both retained all 29 original parts and 100 locked cargo units. Engines and RCS were off, with unchanged propulsion fuel, through rail travel and coast; the separate circularization correction was approximately 725 m/s. Peak normalized part temperature was approximately 0.434. Each left approximately 4,194 liquid-fuel units for subsequent flight. These are larger-connector launch results. An unscaled Junior connector test held at cradle alignment, approximately one metre from its target; it is an unresolved launch case.

The rendered menu showed a 2-5 minute delivery window in a prepared native near-receiver fixture. Physical docking occurred 231.86 seconds after the first available estimate, within that first 122.50-302.00 second window. Exactly 100 MonoPropellant transferred after real stock docking, then the pod physically undocked. A 58-second pause retained the same game time and ETA window. The receiver's initial orbit was authored near the real post-launch pod; this is not a launch-to-delivery demonstration.

The unchanged post-launch pod survived atmospheric entry with all parts and cargo. Default native recovery landed intact but 2.96 km short of home. Saved-descent replays exposed excessive fuel use during final correction. The optional experimental controller now takes ownership of the final-descent stage before stock guidance writes a conflicting throttle mode, preserving its throttle controller's state and governing horizontal travel by stopping distance.

One replay from the saved approximately 12 km descent completed native recovery **0.458 m from the home pad**, upright **0.297 degrees**, surface speed **0.003 m/s**, stable for **5.10 seconds**. All 29 original parts and 100 locked cargo units remained. It used its last propulsion on final approach. This return-only test awarded no delivery, refit or completed-flight credit. A read-only audit accepts this result and rejects the intact 2.96 km touchdown.

An uninterrupted return from the original post-launch orbit on the frozen candidate is still running as this report is prepared. Its outcome must be reported separately; the saved-descent result alone does not establish uninterrupted entry-to-touchdown reliability.

A complete multiplayer dock/deliver/return cycle, repeated landing reliability, every connector configuration and interplanetary routing remain unverified. The experimental rail and precision-pad controllers are not enabled in ordinary or Luna dispatches.

Private logs, native fixture saves, source fingerprints and diagnostics are kept outside the public source tree. Production universe/pods are not reset for testing.
