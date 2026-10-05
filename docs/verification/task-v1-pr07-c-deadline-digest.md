# TASK-V1-PR07-C Workspace deadline digest verification

## Status

The same-Tenant concurrency remediation starts from audit HEAD
`b6d10206c508b8acc57590f2c127f468f77cc3c0`. This record is not merge
authorization and does not relabel PR07-C as complete. It intentionally
records no final branch SHA, test count, workflow ID, or review/check result:
those facts must be collected from the immutable final HEAD and recorded in
the PR body after the required checks finish. PR #277 must remain Draft and
unmerged during this work.

PR07-C covers generation only. Current-authorized delayed dispatch/replay,
notification opening, SignalR route changes, Angular reconciliation, and final
PR07 operations/acceptance remain PR07-D/E. No evidence below proves those
later phases.

## Authority and identity

| Field | Value |
| --- | --- |
| Implementation repository | `NYGsatoshi/Coglatas` |
| Task | `TASK-V1-PR07-C` |
| Branch | `task/v1-pr07-c-deadline-digest` |
| Accepted branch base | `93b1c5e260e04c243ff84f7370aca4d869484087` |
| PR07-B pull request | `#275`, merged |
| PR07-B post-merge entry gate | Recorded green for same-SHA main CI, Code Quality, Documentation CI, and npm Security Audit in `docs/TASK_V1_PR07_PLAN.md` |
| Canonical specification commit | `8b90c8897367606473515d17d3696e458b2ee7b5` |
| Focused owner decision | `docs/decisions/task-v1-pr07-c-deadline-digest-decisions.md` (`Resolved`, 2026-08-03) |
| Migration | `20260803041347_AddTaskDeadlineDigestLedger` |
| Policy version | `1` |
| Same-Tenant-concurrency audit starting HEAD | `b6d10206c508b8acc57590f2c127f468f77cc3c0` |
| Final remediation HEAD | Not recorded in source; obtain from the final immutable branch head |
| Pull request | `#277` — Draft and unmerged; final branch SHA/check references are recorded in the PR body after push |
| Merge performed | No |

Current source, tests, root deployment configuration, and active documentation
remain implementation authority. The owner decision narrows current digest
relevance without changing PR07-B's immediate-recipient matrix.

## Separated state machines

The Transactional Outbox is not the digest scheduler.

| Concern | Durable authority | Terminal generation/delivery state |
| --- | --- | --- |
| Daily identity, due time, claim, retry, generation outcome | `task_deadline_digest_jobs` plus append-preserved `task_deadline_digest_attempts` | Digest `Succeeded` or `Failed` |
| Notification signal delivery/retry/dead-letter | Existing transactional Outbox | Existing Outbox delivery/dead-letter contract |

One job is unique by exactly:

```text
TenantId
WorkspaceId
UserId
LocalDate
PolicyVersion
```

The job stores current state, counts, scheduling/claim timestamps, bounded error
code, completion time, and optional Notification reference. The attempt table
preserves every automatic or operator claim. Claim owner, expiry, and random
token fence concurrent or expired workers; PostgreSQL due and expiry selection
uses deterministic order and `FOR UPDATE SKIP LOCKED`.

The automatic budget is exactly three. The third automatic failure or expiry
is terminal. An approved operator restart requires a terminal job, appends one
linked `OperatorRestart` attempt with `RequestedByUserId`, and writes the
generic `TaskDeadlineDigestRestarted` AuditLog record in the same transaction.
It permits one attempt and never resets `AutomaticAttemptCount` or rewrites the
earlier attempts as a new automatic series. There is no digest dead-letter
table.

## Workspace-local schedule and DST

The effective local time is the member's nullable override or the Workspace
default. Only exact 15-minute wall times from `00:00` through `23:45` are
valid. The Workspace timezone is primary, then the Tenant timezone, then UTC;
invalid identities increment aggregate diagnostics.

The ledger date is the current Workspace-local date. A nonexistent wall time
advances to the first valid instant after the DST gap. An ambiguous wall time
uses its first chronological UTC occurrence; the five-field unique identity
prevents a second row or claim during the fold. Generation re-resolves current
timezone and preference: a stale local-date identity succeeds with no visible
digest, while a current identity whose new due instant is still future is
deferred.

Pure policy tests exercise both gap and fold. The PostgreSQL evidence set
includes integrated scheduler/upsert/claim cases for both directions:
`America/New_York` `2026-03-08 02:30` advances to the first valid `07:00Z`
instant and creates one identity, while both UTC occurrences of the
`2026-11-01 01:30` fold observe one ledger row/claim. Their final execution
status and counts remain pending for the immutable remediation HEAD.

Schedule upsert is write-idempotent. A PostgreSQL conflict updates a pending,
unattempted identity only when its calculated `ScheduledForUtc` or
`NextAttemptAt` differs; an identical poll affects zero rows and does not
change `UpdatedAt`. The fallback leaves the entity unchanged and does not save
in that case. Claimed or attempted jobs are not rewritten by schedule polling.
The scheduled diagnostic records only an inserted identity or meaningful
pending-schedule change, not every schedule candidate examined.

## Candidate and commit authorization

The four output categories are deadline in three Workspace-local days,
deadline in one local day, due today, and overdue. `Overdue` is strictly before
the captured current instant; an equal instant is due today.

Visibility is necessary but not sufficient. The approved relevance predicate
requires current effective Watch from a current manual Watch or current
Creator/Primary-Assignee/Collaborator/Reviewer source. Explicit opt-out
suppresses digest relevance. Visibility alone and Team Queue eligibility do
not qualify. Watch does not grant authorization.

Current Tenant/user, TenantUser, active Workspace membership, Workspace,
Project authorization and lifecycle, Task deletion/completion/cancellation,
Workflow terminal stage, and relationship source are all checked. The normal
candidate-page enumeration runs only inside its generation transaction;
bounded lock/rechecks validate each already enumerated page rather than
forming a discarded second enumeration. At transaction start, the repository
first acquires and validates the original claim fence: digest Job `FOR UPDATE`,
then claimed Attempt `FOR UPDATE`, with matching token/`Claimed` status,
Tenant/User/Workspace/Job/trigger identity. It then acquires the current-state
fence. It uses `FOR SHARE` for Tenant, TenantSettings, active Subscription(s),
their Plan source(s), TenantUser, Workspace, WorkspaceMember, Project, Group,
ProjectMember, GroupMember, Task, WorkflowStage, Watch, and Collaborator; it
uses `FOR UPDATE` for recipient User. The actual fixed order is Job -> Attempt
-> Tenant -> TenantSettings -> active Subscription -> Plan -> recipient User
-> TenantUser -> Workspace -> WorkspaceMember -> sorted Project/Group/
membership rows -> sorted Task/WorkflowStage/Watch/Collaborator rows. It
rechecks the current context and exact page predicate while those locks are
held.

`FOR SHARE` allows independent digest readers to coexist while conflicting
with ordinary PostgreSQL update/delete locks. Tenant and Workspace are not
exclusive digest fences. The User `FOR UPDATE` row is deliberately the only
recipient-wide serialization point, so same-user Workspace digests serialize
their Notification-state advance while different users in the same Tenant or
Workspace can progress together. The job and attempt retain exclusive
claim-token ownership fencing.

Absent optional rows have no row lock to protect. The writer and generator
therefore share stable parent pivots: Tenant for TenantSettings/Subscription,
Workspace for WorkspaceMember, Project for ProjectMember, Group for
GroupMember, and Task for Watch/Collaborator. A digest takes the pivot shared;
the matching writer takes it `FOR UPDATE` before it inserts, changes, or
deletes the child. The source fence covers TenantSettings, every active
Subscription, and the relevant Plan source(s), so a feature-disable mutation
cannot commit ahead of a stale digest. Its final feature read is no-tracking
after those locks, so a preflight-tracked source cannot be reused. No
digest-only advisory lock is used.

Thus an authorization/lifecycle mutation that arrives after fencing waits for
the permitted commit, while a mutation that already committed causes a
post-lock mismatch and the entire transaction is discarded before it can stage
a Notification, Outbox row, state-version advance, or `Succeeded` transition.
The generator recreates the transaction, reconfirms the same claim token,
reacquires all locks, and re-evaluates at most three times. PostgreSQL
serialization/deadlock and EF concurrency conflicts are classified in
Infrastructure and surfaced only as a safe application-level persistence
conflict marker. Internal retries do not consume another automatic attempt;
claim loss stages nothing. There is no pre-transaction throwaway candidate
evaluation.

The PostgreSQL candidate suite distinguishes all accepted Watch sources from
opt-out, visibility-only, Team Queue-only, and restricted-group unauthorized
rows. The required final-evaluation race cases are
`MembershipRevokedAfterFinalEvaluationCannotCommitDigest`,
`WorkspaceArchivedAfterFinalEvaluationCannotCommitDigest`,
`ProjectArchivedAfterFinalEvaluationCannotCommitDigest`,
`TaskCompletedAfterFinalEvaluationCannotCommitDigest`,
`WatchOptOutAfterFinalEvaluationCannotCommitDigest`, and
`RelationshipRemovedAfterFinalEvaluationCannotCommitDigest`. They must prove
that no stale Notification, Outbox row, or recipient state advance commits.

The same-Tenant PostgreSQL concurrency gates are
`DifferentUsersInSameTenantGenerateConcurrently`,
`DifferentUsersInSameWorkspaceDoNotShareExclusiveFence`,
`DifferentWorkspacesInSameTenantDoNotShareExclusiveFence`,
`SlowFirstClaimDoesNotExpireLaterSameTenantClaims`,
`SameRecipientStillSerializesNotificationStateVersion`,
`SlowFirstSameRecipientClaimDoesNotExpireQueuedClaim`,
`SameRecipientWaitingClaimIsSkippedByExpiryScanner`,
`SameRecipientQueuedClaimKeepsAutomaticAttemptBudget`, and
`ClaimLostBeforeTransactionFenceStagesNothing`,
`ConcurrentTenantMutationWaitsForGenerationFence`,
`ConcurrentFeatureDisableWaitsOrPreventsDigestCommit`, and
`MissingWatchRowOptOutInsertCannotBypassFence`. They use test-assembly-only
gates/interceptors around real generator/repository transactions. The first
three must show the later generator evaluates and commits before a paused
unrelated predecessor is released; the slow-claim case must show no irrelevant
expiry, extra automatic attempt, or claim loss; the same-recipient case must
show unique state versions. The new lease cases prove that A has Job/Attempt
and recipient User locks, B has completed its own Job/Attempt locks before it
requests that User row, post-expiry `FOR UPDATE SKIP LOCKED` probes return no
claim and leave B's Job/Attempt `Claimed` with its original token and one
automatic attempt, and releasing A lets both units succeed. Claim loss before
the transaction-start fence proves no Notification, recipient state, Outbox,
or success staging. The feature/phantom cases must show no stale Notification,
Outbox, or state update.

## Result, atomicity, and privacy

A non-empty final evaluation stages exactly one generic recipient Notification:

- numeric type `TaskDueSoon`;
- title `Task deadline digest`;
- null body;
- `TaskDeadlineDigest` job reference;
- stable logical key derived from Workspace, local date, and policy version.

The existing Notification unique identity adds Tenant and recipient. Its
recipient-only `Notifications.NotificationCreated.v1` signal contains only
`notificationId`, `stateVersion`, and `requiresRefetch`. It contains no Task
list, Task/Project title, comment/review/Blocked content, Watch/private
preference state, route, or relationship set.

The transaction-start Job/Attempt claim fence and the later recipient User
fence are held through the single save/commit. Notification,
NotificationUserState, minimal signal Outbox row, optional job
`NotificationId`, and `Succeeded` transition therefore share one atomic
outcome. The held Job lock makes the expiry scanner skip a live queued
same-recipient claim; crash or rollback releases it for normal recovery.
Concurrent same-user Workspace digests serialize through their common
recipient User `FOR UPDATE` fence, without treating the Outbox as a scheduler
or serializing unrelated Tenant recipients.

The existing `NotificationUserState.Version` is also an EF optimistic-
concurrency token. It protects the state version when a digest races an
immediate Task Notification that does not share the digest's recipient lock:
one unit of work commits and the other rolls back with
`DbUpdateConcurrencyException`. Digest processing maps that conflict to the
safe `DigestPersistenceConflict` retry path. A clean logical-key retry produces
exactly the two intended Notifications/signals at versions 1 and 2. The
PostgreSQL suites also prove that a post-save injected exception rolls the
whole digest transaction back and that a logical retry reuses an existing
Notification without a second signal or state advance.

A zero-candidate final evaluation is `Succeeded` with a null Notification
reference and creates no Notification or Outbox row. It is not a failure and
does not produce an empty visible digest.

## Worker and feature behavior

The worker is an in-process `BackgroundService` using public `RunOnceAsync` for
focused tests. Configured values are bounded to 100 Tenants/page, 500
schedules/page, 100 claims/batch, and 500 candidates/page. Default claim expiry
is 120 seconds and default failure retry delay is 60 seconds. Cancellation is
propagated through paging and database/generation calls. Every claim in the
bounded batch starts immediately in one `Task.WhenAll`, with its own DI and
Tenant scope. The claim-batch bound of 100 is an application fan-out bound, not
a database concurrency ceiling; `Task.WhenAll` does not by itself prove that
generators made concurrent PostgreSQL progress. The shared/exclusive fence is
the database contract that permits different recipients to progress while
serializing a same recipient. One Tenant cycle or one claimed-user generation
failure does not stop later Tenants/claims.

Worker logs pass no exception object or message. They use fixed templates and
safe bounded error codes only; focused tests reject Tenant, Workspace, user,
Task, job, and claim IDs. Diagnostics are aggregate counters without
high-cardinality labels.

`tasks.notificationsV1` remains default off. Because it is per Tenant, the
hosted worker still pages active Tenants before checking it. A disabled Tenant
performs no schedule upsert, claim, or generation. Disabling does not delete
ledger state or cancel delivery of already committed Outbox rows. If the flag
becomes disabled after a claim, the generator token-fenced release restores an
automatic claim to `Pending`, clears its claim state, restores both automatic
claim counters, and marks that attempt `Deferred`. A claimed operator restart returns
the same audited attempt to `Pending` without a new attempt row or automatic
budget change. The release creates no Notification or Outbox row; the released
token cannot later complete, defer, or fail the job. Re-enabling allows a fresh
claim and normal generation.

## Migration and query evidence

The focused migration adds only `task_deadline_digest_jobs` and
`task_deadline_digest_attempts`, their constraints, foreign keys, unique
identity/attempt indexes, and due/claim indexes. Fresh, upgrade, Down, and
re-upgrade paths are covered. Down removes both digest tables and therefore all
ledger history; it leaves PR07-A preferences, Notifications, Outbox, and Audit
schema intact. This remediation adds no migration and does not rewrite an
existing migration.

Focused partial indexes are:

- `IX_task_deadline_digest_jobs_due` for pending rows ordered by Tenant,
  `NextAttemptAt`, creation, and ID;
- `IX_task_deadline_digest_jobs_claim_expiry` for claimed rows ordered by
  Tenant, expiry, and ID.

The PostgreSQL 18 acceptance suite must capture `EXPLAIN (ANALYZE, BUFFERS)`
and show the due index directly and the claim-expiry index with incremental
sort. Candidate list pages clamp a requested `int.MaxValue` page size to 500 and preserve
deterministic `(DeadlineAt, Id)` order. Commit fencing performs additional
bounded lock/recheck operations for the evaluated page; it does not introduce
a discarded pre-transaction candidate pass. No optional Task-deadline index was
added because the small fixture is not representative PostgreSQL plan evidence
for that index. Production-volume candidate planning remains environment
evidence, not an inferred index recommendation.

## Required final evidence

No final evidence is recorded in this source file. A prior candidate's local
results are historical only and predate this same-Tenant concurrency
remediation; they must not be reused as a final SHA, count, manifest result, or
hosted-check result. Use a disposable PostgreSQL 18 database, redact connection
values, and record the following only after execution on the immutable final
head:

| Check | Required evidence, not yet recorded here |
| --- | --- |
| Release restore/build | 0 warnings and 0 errors on the final head. |
| `Scope=TaskV1PR07C` PostgreSQL acceptance and strict TRX manifest | No failed, skipped, aborted, or missing required test; record actual total and active/matched manifest counts from the final TRX. |
| `TaskV1Pr07CDeadlineDigestPostgreSqlTests` | Final PostgreSQL result, including claim/lease fencing. |
| `TaskV1Pr07CDigestCandidateAtomicityPostgreSqlTests` | Final PostgreSQL result, including all final-evaluation and new same-Tenant/phantom gates. |
| `TaskV1Pr07CNotificationVersionConcurrencyPostgreSqlTests` | Final PostgreSQL recipient-version result. |
| Full backend and EF pending-model check | Final result and exact model-drift output. |
| Frontend/unit/build/architecture/license/Storybook/Playwright | Final command results, with skipped status reported rather than hidden. |
| Hosted Draft PR checks and review state | Live final-HEAD workflow IDs, mergeability, behind-main, review threads, requested changes, and pending checks; retain Draft/unmerged. |
| Frontend/SignalR/open behavior | Excluded; PR07-D/E scope. |

### Commands

The connection values used by PostgreSQL runs are disposable and must remain
redacted from durable evidence.

```powershell
$env:POSTGRES_TEST_CONNECTION_STRING = '<disposable PostgreSQL 18 connection string>'
$env:ConnectionStrings__DefaultConnection = $env:POSTGRES_TEST_CONNECTION_STRING

dotnet restore Coglatas.slnx

dotnet build Coglatas.slnx `
  --configuration Release `
  --no-restore `
  --disable-build-servers `
  -m:1

dotnet ef database update `
  --project src/Coglatas.Infrastructure `
  --startup-project src/Coglatas.Web `
  --configuration Release `
  --no-build

dotnet ef migrations has-pending-model-changes `
  --project src/Coglatas.Infrastructure `
  --startup-project src/Coglatas.Web `
  --configuration Release `
  --no-build

dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj `
  --configuration Release `
  --no-build `
  --disable-build-servers `
  -m:1 `
  --filter "Scope=TaskV1PR07C" `
  --logger "trx;LogFileName=task-pr07c-acceptance.trx" `
  --results-directory artifacts/task-pr07c-test-results

bash scripts/ci/verify-trx-results.sh `
  artifacts/task-pr07c-test-results/task-pr07c-acceptance.trx `
  --minimum-total 1 `
  --required-tests scripts/ci/task-pr07c-required-tests.txt `
  --label "TASK-V1-PR07-C"

dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj `
  --configuration Release `
  --no-build `
  --disable-build-servers `
  -m:1 `
  --filter "FullyQualifiedName~TaskV1Pr07CDeadlineDigestPostgreSqlTests"

dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj `
  --configuration Release `
  --no-build `
  --disable-build-servers `
  -m:1 `
  --filter "FullyQualifiedName~TaskV1Pr07CDigestCandidateAtomicityPostgreSqlTests"

dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj `
  --configuration Release `
  --no-build `
  --disable-build-servers `
  -m:1 `
  --filter "FullyQualifiedName~TaskV1Pr07CNotificationVersionConcurrencyPostgreSqlTests"

dotnet test Coglatas.slnx `
  --configuration Release `
  --no-build `
  --disable-build-servers `
  -m:1
git diff --check
```

`POSTGRES_TEST_CONNECTION_STRING` is mandatory for every PostgreSQL claim.
Without it, local discovery skips those cases; CI fails when it is missing.
The focused provider runs above do not substitute for a final full backend run,
fresh/upgrade deployment evidence, pending-model check, or exact-head CI.

## Same-recipient claim-lease remediation evidence

Local provider verification on 2026-08-06 used an isolated PostgreSQL 18.4
container with the CI-equivalent base-database migration apply before the full
suite. The container had no mounted volume; it was removed after verification.
The relevant evidence is:

- `SlowFirstSameRecipientClaimDoesNotExpireQueuedClaim`,
  `SameRecipientWaitingClaimIsSkippedByExpiryScanner`, and
  `SameRecipientQueuedClaimKeepsAutomaticAttemptBudget` all passed. A first
  Workspace transaction held the recipient User lock, while the second
  Workspace transaction had already completed its own Job then Attempt locks
  and was waiting for that User row. One or three post-expiry scanner probes
  returned zero claims; during the probe the second Job and Attempt remained
  `Claimed`, its token was unchanged, it had one Attempt row, and both
  `AttemptCount` and `AutomaticAttemptCount` remained one.
- Releasing the first transaction let both Workspace Jobs and Attempts
  succeed. The combined outcome was two Notifications, two Outbox rows,
  Notification state versions `[1, 2]`, zero expired Attempts, zero
  same-recipient `ClaimLost` outcomes, and counts of one for both Jobs'
  `AttemptCount` and `AutomaticAttemptCount`.
- `ClaimLostBeforeTransactionFenceStagesNothing` passed: a stale token lost
  before the transaction-start fence produced no Notification,
  NotificationUserState, Outbox row, or `Succeeded` transition.
- The three required PostgreSQL fixtures passed without skips: deadline digest
  `23/23`, candidate/atomicity `27/27`, and Notification version concurrency
  `1/1`. The four new cases themselves passed `4/4`.
- The strict `Scope=TaskV1PR07C` TRX passed `106/106`, with no failed or
  skipped tests. Its required-test manifest was active and matched for all
  `76/76` names (the prior 72 retained plus four new exact test names).
- The Release build completed with zero warnings and zero errors. After the
  CI-equivalent migration apply, the full backend TRX passed `788/788`, with
  no failures or skips; EF reported no pending model changes.
- Frontend checks also completed: Angular unit tests `324/324`, production
  build, architecture checks, license checks, and Storybook build all passed.
  The Linux Docker Playwright smoke passed with 62 tests, three documented
  skips, and one desktop retry that passed after its initial timeout; no
  screenshot baseline was changed.

These are local provider and frontend results. The immutable final commit,
push, review state, and hosted workflow identifiers remain recorded on the
Draft PR after the final commit is published; no local result substitutes for
those exact-head hosted checks.

## Explicit exclusions

- Notification-open endpoint and read/navigation timing;
- delayed dispatch/replay current authorization;
- SignalR route/group/catalog changes;
- Angular preference, digest display/open, reconciliation, multi-tab,
  reconnect, degraded, active-edit, touch, or accessibility behavior;
- email and mobile push;
- PR06B and PR08;
- PR07-E alert thresholds, full runbook acceptance, broad admin UI, and final
  two-user real-backend acceptance;
- merge of the Draft PR.

## Current completion assessment

At this documentation point:

- Digest and Outbox state machines are separated in source.
- The source contract to verify uses a deterministic claim-first commit-time
  fence: transaction-start Job then Attempt `FOR UPDATE` with token/status/
  identity validation, shared Tenant/feature/authorization/lifecycle rows, an
  exclusive recipient User, stable-parent phantom pivots, post-lock
  re-evaluation, and at most three recreated transaction attempts. It holds
  the Job while same-recipient work waits for User, so expiry recovery's
  `FOR UPDATE SKIP LOCKED` path skips live work; it does not use a Tenant-wide
  or Workspace-wide exclusive digest fence.
- Feature-disable release, schedule write idempotency, and one normal
  in-transaction candidate evaluation are implemented contracts that require
  final provider evidence.
- Five-field identity, local scheduling/DST policy, exact three automatic
  attempts, append-preserved operator restart, zero-candidate success, generic
  Notification, minimal recipient-only Outbox signal, and privacy boundaries
  remain required preservation checks.
- The local Release, focused provider suites, full backend, pending-model,
  frontend gates, and manifest evidence above are complete. Exact-final-HEAD
  hosted checks, PR review state, and merge authorization must be recorded on
  the Draft PR before any separate merge decision.

Therefore this source record currently says:

```text
PR07-C Complete: No
Merge: No-Go
PR07-D: No-Go until PR07-C is merged and accepted
PR07-E: No-Go until PR07-D is merged and accepted
```

These labels must be updated only from exact final evidence. A future passing
run must not overwrite the historical limitations above without identifying
its immutable head and environment.
