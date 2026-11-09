# Real-backend Compose cleanup exit propagation

## Historical repair record and current source boundary

This report records the #1601 cleanup-exit repair and its bounded helper tests.
Read the results and its final *fresh hosted qualification remains necessary*
statement as evidence from that repair, not a new run against today's Main.
At observed Main `54e63db2efc7030f3f08b9d85bb95e9ad94ab5ae`, the
[actual Compose adapter](../../tests/ui/run-real-backend-smoke-compose.mjs)
still treats unsuccessful project-scoped cleanup after a successful generic
scenario as failure, and the
[source-bound Node controls](../../tests/ui/real-backend-smoke-compose-helpers.node-test.mjs)
cover failure precedence and SIGINT/SIGTERM handling with synthetic child
events. The maintained operational entry is the
[Functional Compose harness](../ci/functional-compose-harness.md).
Source presence and synthetic controls do not prove actual Docker cleanup,
latest licensed browser execution or Pilot/Release authorization.

Issue [#1601](https://github.com/NYGsatoshi/Coglatas/issues/1601) corrects a
qualification false success: the generic Compose adapter previously returned a
successful scenario's zero exit even when its scoped `down` command failed.
The adapter now requires successful cleanup after a successful generic scenario.
An existing nonzero generic scenario or setup exit is preserved. The roster's
existing stricter cleanup-result normalization is unchanged, including rejection
of a missing result. SIGINT and SIGTERM retain exits 130 and 143 after the same
memoized cleanup. Cleanup commands, project selection and deadlines are unchanged.

The existing helper test suite executes the tracked adapter in a VM with
asynchronous child-event stubs. It covers generic and explicit MVP0 cleanup-only
failure, successful cleanup, original scenario/setup failures, unavailable
Compose, and both registered signal handlers with exactly one project-scoped
`down --volumes --remove-orphans`. These controls establish adapter behavior;
they do not prove real OS signal forwarding or absence of hosted Docker resources.
The unavailable-Compose control has no cleanup result and must remain a failure.
No artificial successful path with a missing generic cleanup result is claimed.

Fresh hosted qualification remains necessary. This repair does not assert a
historical hosted resource leak, change cleanup ownership policy, or grant Pilot
or Release authority.
