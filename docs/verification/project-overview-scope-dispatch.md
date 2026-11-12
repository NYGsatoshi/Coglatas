# Project overview follow-up request scope

`ProjectsFacade.loadProjects` already rejected results from an old authorization
generation or Workspace before applying state. Its preceding `switchMap` could
nevertheless dispatch each old Project's Task request before that final check.
Deferred Workspace/Realtime effects leave a response-delivery interval in which
the current Workspace has changed but teardown has not canceled the inventory.

The existing generation/Workspace predicate is now also checked before the
follow-up request stage. A stale inventory completes without dispatching Task
reads. The final apply/error checks remain unchanged. A same-Workspace label
refresh still performs the authorized reads and applies the result. Backend
authorization, routes, errors, session semantics, budgets and browser error
assertions are unchanged; no 401 allowlist is widened.

Test-only commit `002a30d90124d54bdc9049dfeca89d407c570915` ran against unchanged
Main843 product source. Both Workspace-clear/replacement cases failed because
one old-scope Task read was issued; the same-Workspace positive passed. Original
log SHA256:
`80a386a524031d28a1bcab66af240e5c4d5bafd0adcb1c21b5afe22888919e39`.
An earlier harness attempt stopped before tests because its dependency mount
was outside a `node_modules` ancestor, preventing hoisted `semver` resolution.
That preparation error is retained separately. Correcting only the external
mount path executes the original unchanged test/product source.

Fixed executable commit `ddb975953477016b389523697f6f51f38ce905f0` passed all
93 tests across the four owning Project/My Tasks files, with no failure or skip.
Original log SHA256:
`55e34e3c93a103b4c304ae07b72f1f2e7d8ea681e6f4aaf16d49be24316e2d20`.
The new three cases and all pre-existing test files/assertions are unchanged.
Execution used a Linux LF Git clone, Node24.22.2, two-CPU affinity and 3GiB,
with the previously qualified frontend dependency volume mounted read-only;
current package manifests/lock bytes match that qualified installation.
These service unit tests do not activate a license or establish browser/backend
integration. Licensed verification remains in `syncfusion-licensed-build`.

Main843's first P0 case94 failed on three unexpected Project Task401 console
errors around logout. Original job127713866264/run42162845236/attempt1 and
artifact13477936942 are retained. This observation prompted the investigation;
the deterministic unit cases prove the request-dispatch gap. They do not alone
prove complete causality for that historical run or for earlier MSG-01/Thread
timeouts or the Audit flake. Those causes remain unknown until qualified by
their own evidence. Protected exact-candidate P0, current required checks,
original official PR Gate, independent review and resulting-Main qualification
remain separate requirements. No Owner promotion or Release GO is implied.
