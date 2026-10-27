# SEC-17 bounded deep reconnect and concurrent Direct scopes

This separate backend owner adds representative synthetic deep coverage for
Issue #690 beyond the existing sequential event/route catalogue:

`ConcurrentIndependentDirectScopeTransitionsReauthorizeEveryBoundedReconnectCycle`

The real Web entry point runs on Kestrel with migrated PostgreSQL, the actual
Outbox dispatcher and authenticated SignalR WebSockets. Two independent
Workspace-scoped Direct Conversations each have an authorized owner and member.
One scope comes from the existing public transport fixture; the other is a
disposable synthetic Workspace/Direct graph. No private held test is reused.

The source bounds the test to three reconnect cycles. Each cycle observes
positive real HTTP Message creation and live delivery before changing current
authority. Separate transactions update the first Workspace membership and
the second Direct participant read flag, each proves its row update while
uncommitted, then both wait at a preparation barrier. The coordinator releases
both and awaits both commits before sending any asserted post-commit event.

Every cycle checks the same member cookie's current Direct and Message HTTP
denial, generic subscription denial, a newly reconnected denied socket,
authorized owner HTTP and native delivery positives, and nonreceipt on live
established and reconnected transports. The old subscriptions remain intact
through the passive observation. Concurrent restoration then requires fresh
subscriptions and actual authorized Message delivery on every receiver. The
test validates persisted Message identity and received event type/schema,
Tenant, aggregate and Direct resource, using only typed in-memory transport
identity fields. Reusing an event identity with changed Tenant, aggregate or
resource fails closed and cannot overwrite its earlier identity. The native
owner also exercises bounded deterministic identity-drift mutations. The helper
clears those fields when disposed.

Authority changes are direct disposable fixture database updates. They qualify
current subscription/dispatch/HTTP authorization after commit, not management
endpoint authorization or authorization invalidation side effects. Successful
SubscribeUser invocations prove connection liveness only; actual newly delivered
owner and restored-member Message events supply the delivery positives.
This does not test an event racing the commits, impose a commit order, or claim
exactly-once, load/capacity, full producer/event coverage, target deployment,
product RLS or normative approval. Existing SEC-ARCH/Owner holds remain.

## Execution ownership and false-green controls

`.github/workflows/sec17-deep.yml` is a separate public backend-only owner.
It uses a pinned PostgreSQL 18.6 service, hosted runner, read-only contents
permission, no protected environment and no secret context. It builds its own
exact checkout, runs one exact fully qualified native test, and requires the
named TRX execution to be present, passed and complete. Missing, renamed,
skipped, failed, aborted, duplicated, stale or counter-inconsistent executions
fail the verifier. Selection absence cannot become an early-return pass.

The dedicated summary binds checkout/build stamp, source digest, native TRX
digest, six real compiled assemblies and their loaded copies, and run/attempt.
Its source cycle/scope parameters are not independent per-cycle telemetry.
Only the closed execution JSON is published. Raw TRX remains private to the
ephemeral hosted runner, whose job is bounded to 20 minutes and is discarded
afterward. Capture runs after failure too, publishes only finite outcomes,
counts/digests and fixed diagnostics, and returns nonzero unless the exact
native owner passed. Native test failure remains a failed workflow step;
selection/capture errors cannot replace it with success. No raw TestOutput,
ErrorInfo, response body or protected frame is copied into the summary.
Independent hosted provenance reconciliation remains required; the summary
keeps contract completion and trusted attestation UNVERIFIED.

`scripts/ci/test_sec17_deep_execution.py` exercises execution mutations and
actual synthetic Git/build/source/loaded-copy binding failures. These verifier
self-tests do not execute the product transport. The existing closed SEC-ARCH
catalogue receives only this exact method with cardinality one; its parser,
SignalR matrix rules and approval semantics remain unchanged.

Local execution requires a disposable PostgreSQL connection:

```bash
POSTGRES_TEST_CONNECTION_STRING='<disposable connection>' \
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj --configuration Release \
  --filter 'Scope=SecurityArchitectureDeep'
```

A local skip is not deep execution evidence. Hosted exact-candidate result and
subsequent Main qualification must be recorded from their own runs; no previous
Main result transfers. Implementation/source inspection alone is unexecuted.
