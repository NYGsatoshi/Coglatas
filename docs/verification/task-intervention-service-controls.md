# Task execution intervention service controls

The focused service controls exercise the existing Stop and Correct Direction
commands without changing product behavior. They retain the distinction between
stopping a run and redirecting it into a new Accepted run from current saved
source policy and Research Plan provenance. The closed run's snapshot remains
immutable; no checkpoint resume or outbound execution is inferred.

The tests use the real application service, repositories, tenant query filters,
EF unit of work and transactional Outbox with an isolated EF InMemory database.
Current actor, Project manager authorization and the audit sink are controlled
test doubles. Two rejected-save outcomes are explicitly injected; they are not
database-generated concurrency conflicts.

The controls cover unavailable actor/task authority and foreign Tenant scope,
run-to-Task/Tenant/Workspace/Project matching, terminal-state rejection, the
three active states that can be stopped, current Project defaults and Task
overrides with matching or stale source documents, optional Research Plan
revision capture, immutable closed snapshots, successor identity and timestamps,
Task/run versions, activity, audit requests, persisted invalidation, and safe
failure after an injected save conflict. They assert denied calls produce no
save or Outbox writes and rejected saves do not return success.

Itemized closed and successor Run policies are inspected through the real
repository's pending documents immediately before the unit of work flushes
them. This checks full rule and scope/version staging, including stale-document
fallback; EF InMemory does not persist those JSON documents. Two Research Plan
revisions distinguish the current revision pointer from older history. Outbox
assertions inspect type, Tenant, Task/version, Project routing and refetch payload,
and correction audit/activity assertions bind actor and resource identities.
The full closed document is serialized before the command and compared with the
observed document, including all owner/scope/version fields. Reference identity
also remains unchanged: the real repository replaces a pending document object
on every upsert, so even an otherwise equal closed-Run restage is rejected by
these controls. Itemized file identities are synthetic canonical identifiers;
the controls do not validate their PostgreSQL inventory admission.

Run the focused native collector with:

```text
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj -c Release --filter FullyQualifiedName~Coglatas.Tests.Projects.TaskExecutionInterventionServiceTests --collect:"XPlat Code Coverage" -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
```

Original OpenCover, TRX and execution logs belong to exact-source external
evidence. Native covered-point counts are not a prediction of Main Overall
coverage; official Sonar import, source mapping and denominator must be measured
after any normal merge.

These controls do not establish PostgreSQL row locking, transaction atomicity,
Commit/Rollback, persisted audit delivery, real HTTP admission or deployed RLS.
The foundation and runtime/provider approval boundaries in
[the Issue #357 verification](p0-task-execution-scope-foundation.md) remain.
This test addition neither closes that issue nor promotes a provider, Web
execution, governance or release decision.
