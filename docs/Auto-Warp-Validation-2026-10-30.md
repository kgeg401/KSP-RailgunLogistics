# Automatic orbital coast warp

Enable **Auto warp: On** in the Railgun Logistics menu. It is off by default and
saved per client. In Luna, every connected player must enable it and be safely
landed or in a stable orbit with engines and translation controls idle. Turning
it off stops further acceleration. All clients and the host need this build.

The pod uses the installed MechJeb executor's native coast/warp logic, capped at
100×. A manoeuvre-node coast retains the executor's half-burn and spool-up timing
plus a 30-second margin. The return controller can also fast-forward its native
high-deorbit waiting stage. Launch/loading, close rendezvous, docking, transfer,
undocking, atmospheric entry and terminal descent are ineligible. This is native
on-rails time warp, not a direct edit of universal time, positions or fuel.

Luna consensus uses a separate ephemeral channel. Fresh consent and matching
session acknowledgements are required from every authenticated player. The pilot
warps in a bounded coast pulse, then everyone joins the pilot's actual new native
subspace through Luna's normal safe-sync API. The existing fleet resynchronization
checks still require the original intact pod, native ownership, a forward clock
and abandonment of the old timeline. Guidance objects/nodes survive this handoff;
the pilot's engine/RCS control output is held at zero until synchronization ends.
The coordinator cannot create pods, change cargo, advance phases or grant a landing.

If consent expires, someone starts using propulsion, the player list changes or
the coast pulse expires, acceleration stops. A blocked native timeline sync stays
at 1× and reports that it is waiting. It does not force unsafe synchronization or
rewind another player's clock. Disconnect/join/reconnect combinations have not
received an exhaustive native test.

## Final native two-client pass

Tested on KSP 1.12.5 build 3190, Luna 0.29.2 and MechJeb 2.14.3, using a private
loopback host and two native clients. The fixture resumed an actual post-delivery
orbital checkpoint. Its initial server epoch was seeded before launch, and the
receiver name was changed for the private loader; the original native pod bytes,
orbit, propellant and delivery receipt were retained. It is not a fresh mission.

- With only the pilot opted in, both clients remained at 1× for the consent check.
- After the second player opted in, native time warp accelerated the pod's coast.
- The pilot returned to 1× and Luna created subspace 1. The other client joined
  that same subspace; Luna removed abandoned subspace 0.
- Both clients reported shared authority ready and resumed at 1×. Their measured
  sampled game clocks differed by about 0.18 seconds at the rejoin checkpoint.
- The pod retained all 29 original parts and its prior cargo-delivery state.
  Native fuel, temperature, joint and crash-damage cheats remained disabled.
- Both auto-warp switches were turned off after the successful coast and a further
  five seconds at 1× confirmed normal shared guidance. A native frame was inspected.

Frozen native client SHA256:
`5f410d147679f147f63e5080a87f1123890f76252b49d81f5624c07afd5a07a8`

Frozen native server SHA256:
`524f7dbd7b6b4fbde61d517a85b3fa6673a94491bc064eee564ca1684c24709d`

A preliminary pass also succeeded. Review then tightened the server's shared
authority locking and intact-snapshot check, and the short native validation was
repeated on the final server hash above. Both captures are retained privately.

Both assemblies compile without warnings/errors. The dedicated consensus/safety
suite passed 29 checks; the existing suites passed 2,801 fleet checks, 86 authority
checks and 39 evidence-reader regressions. Those checks do not replace native flight.

This pass verifies shared orbital coast acceleration, consent, slowdown and native
timeline recovery. It does not establish an uninterrupted delivery/return, receiving-
player docking repair, atmospheric landing reliability, or every manoeuvre type.
See [the complete-trip investigation](Full-RoundTrip-Validation.md) for those gaps.

The download is a matching-client/server preview. Publishing it does not replace
the running friends host or promote its automatic-update channel. The preview's
`DisableAutoUpdates.txt` prevents an installed stable updater from reverting the
manual preview. Restore the stable backup before joining an older pinned host.
