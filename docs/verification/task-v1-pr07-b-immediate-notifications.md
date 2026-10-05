# TASK-V1-PR07-B immediate notifications verification

## Status

The PR07-B implementation candidate is present in the worktree. This
remediation preserves the TaskComment no-op protection, restores accepted
`Projects.TaskChanged.v1` affected-User projection invalidation, and keeps the
new Assignment/Comment semantic events Project-only. Exact-final-HEAD hosted
workflow evidence is maintained in the Draft PR body rather than a
self-referential source commit. This document must not be read as merge
authorization. PR07-B must remain an unmerged Draft PR.

The current authorization remediation adds a parent-Task/current-Project
recheck to canonical TaskComment update/delete before author-or-Manager
evaluation. It also applies one explicit safety check to an Important-only
`false -> true` mutation and filters both mention search and direct mention
validation through current `CanViewProject`. A denied mutation stages no Task,
comment, AuditLog, Outbox, Notification, or NotificationUserState change. The
exact final-HEAD counts, PostgreSQL execution evidence, and hosted workflow
IDs remain the Draft PR body's authoritative record; this document does not
self-reference a future commit.

The relationship-target remediation adds the shared
`TaskRelationshipTargetPolicy` for canonical Primary Assignee, Reviewer, and
Collaborator mutations and the legacy `Assignee`/`Reviewer`/`Support` adapter.
It requires a non-empty target ID, active non-deleted User, retained
ProjectMember row, and current `CanViewProject` access; current Workspace
membership and active Project state are therefore re-evaluated before a
relationship is saved. This does not change notification-recipient policy.
Suspending a WorkspaceMember while retaining the User and ProjectMember is the
required PostgreSQL reproduction: denied commands leave Task, relationship,
Watch, Notification, NotificationUserState, Audit, and Outbox state unchanged,
while authorized cleanup may remove a revoked relationship. The final
exact-HEAD PostgreSQL/full-suite counts, manifest count/matches, and hosted run
IDs remain in the Draft PR body to avoid a self-referential source commit.

## Authority and identity

| Field | Value |
| --- | --- |
| Implementation repository | `NYGsatoshi/Coglatas` |
| Task | `TASK-V1-PR07-B` |
| Branch | `task/v1-pr07-b-immediate-notifications` |
| Accepted branch base / actual latest main at kickoff | `c5627eb09ecf19d66146eacdbc3e938c0a1c8563` |
| PR07-A pull request | [#274](https://github.com/NYGsatoshi/Coglatas/pull/274), merged |
| PR07-A merge commit | `c5627eb09ecf19d66146eacdbc3e938c0a1c8563` |
| Canonical specification repository | `NYGsatoshi/Coglatas-Spec` |
| Canonical specification PR | `#62`, merged |
| Canonical specification commit | `8b90c8897367606473515d17d3696e458b2ee7b5` |
| Owner-decision record | `docs/decisions/task-v1-pr07-owner-decisions.md` (`Resolved`) |
| PR07-B schema change | None; this work reuses the accepted PR07-A Notification logical-key and user-state schema |
| Code-bearing HEAD | The exact immutable SHA is recorded in the Draft PR body and check runs; this source document deliberately does not self-reference its own commit |
| Draft PR | The exact URL, Draft state, head SHA, and final check results are maintained in the Draft PR body; title is `TASK-V1-PR07-B: implement immediate Task notifications` |
| Merge performed | No |

The specification worktree was used only as a read-only authority reference.
Current source, tests, CI, and deployment configuration remain the
implementation authority. Archived documents are not implementation evidence.

## PR07-A entry gate

PR07-B code changes began only after PR07-A was merged and all four same-SHA
post-merge `main` workflows completed successfully.

| Workflow | Event | Status | Conclusion | Head SHA | Run |
| --- | --- | --- | --- | --- | --- |
| CI | `push` | `completed` | `success` | `c5627eb09ecf19d66146eacdbc3e938c0a1c8563` | [30724803612](https://github.com/NYGsatoshi/Coglatas/actions/runs/30724803612) |
| Code Quality | `push` | `completed` | `success` | `c5627eb09ecf19d66146eacdbc3e938c0a1c8563` | [30724803621](https://github.com/NYGsatoshi/Coglatas/actions/runs/30724803621) |
| Documentation CI | `push` | `completed` | `success` | `c5627eb09ecf19d66146eacdbc3e938c0a1c8563` | [30724803620](https://github.com/NYGsatoshi/Coglatas/actions/runs/30724803620) |
| npm Security Audit | `push` | `completed` | `success` | `c5627eb09ecf19d66146eacdbc3e938c0a1c8563` | [30724803615](https://github.com/NYGsatoshi/Coglatas/actions/runs/30724803615) |

These runs are the sequential PR07-B entry gate only. They are not evidence
for the future PR07-B final HEAD.

## Central recipient contract

`TaskNotificationRecipientPolicy` is the single application-layer authority
for expanding a Task notification event into recipients. Canonical Task and
Task-comment commands construct a semantic request; they do not independently
implement recipient expansion. `ProjectService` is retained as the existing
Task-assignment compatibility adapter and calls the same focused producer.

The adapter maps legacy `Assignee` to Primary Assignee, `Reviewer` to Reviewer,
and `Support` to Collaborator. It updates the legacy row and canonical
relationship, automatic Watch sources, child/parent versions, audit, generic
and assignment-semantic Outbox rows, Notification intent, and user refetch
signal in one save. It produces the semantic event and intent only when the
canonical relationship actually changes, so adding a mirror row after a
canonical command cannot create a second notification. New or changed-to
legacy `Owner` rows fail closed. A historical same-role `Owner` row may have
hours updated or be removed without becoming canonical. Multiple or mismatched
legacy rows fail before any operation that could ambiguously alter canonical
state; removing a mismatched historical row is allowed only as non-canonical
cleanup.

| Event | Mandatory recipients |
| --- | --- |
| Primary Assignee added | New Primary Assignee |
| Primary Assignee removed | Previous Primary Assignee captured before mutation |
| Primary Assignee replaced | Previous and new Primary Assignees, deduplicated into one logical event group |
| Reviewer assigned | New Reviewer |
| Direct mention | Validated directly mentioned users only |
| Review submitted | Current Reviewer |
| Review returned/rejected | Current Primary Assignee; the current command contract represents this as `Returned` |
| Task becomes Blocked | Current Primary Assignee and Reviewer |
| Major hard-deadline change | Current Primary Assignee and Reviewer |
| Important TaskComment | Current Primary Assignee, Reviewer, and Collaborators |

The policy applies these common rules after building the mandatory set:

- actor suppression, including self-assignment and self-mention;
- deterministic recipient dedupe;
- active-user filtering and current `CanViewProject` authorization for every
  candidate;
- mandatory and optional Watch-derived sets remain separate until their final
  union;
- Watch-derived recipients require effective `IsWatching` and are removed by
  explicit opt-out, while opt-out never suppresses a mandatory recipient;
- a comment containing both mentions and `Important` uses one
  `TaskCommentSignificant` logical group and one notification per recipient;
- an ordinary comment returns an empty result before Watch expansion. It
  cannot become a notify-all operation.

Invalid or unauthorized mentions fail safely at the Task-comment command
boundary without revealing which requested identity was invalid. Comment
create/update uses the validated mention set. Delete emits semantic
invalidation only. The legacy broad Task-comment notification helper is no
longer a producer. Compatibility Task-assignment routes use the canonical
relationship state and central producer; no-op mirrors retain only generic
invalidation, while actual canonical changes produce exactly one relationship
semantic and deduplicated recipient intent.

### TaskComment PATCH no-op boundary

The update service calculates `bodySpecified`, `bodyChanged`,
`importantSpecified`, `importantChanged`, and `becameImportant` before it
mutates a comment. A PATCH with no update field returns shared Task error
`TASK_COMMENT_UPDATE_REQUIRED` and stages nothing. A specified value equal to
the persisted normalized value returns a successful response without a save;
the Task/comment versions, `UpdatedAt`, AuditLog, Outbox, Notification, and
NotificationUserState are unchanged.

Only `false -> true` supplies `IsImportantComment=true` to the central
recipient policy. `true -> true` and `true -> false` create no
Important-comment recipient work. Mentions are validated only for an actually
changed supplied body. A body update on an already-Important comment can
produce recipient-private direct-mention intent only; it cannot replay the
Important relationship matrix. A direct mention combined with `false -> true`
remains one `TaskCommentSignificant` recipient union and logical event.

`TaskNotificationProducer` owns the stable category-to-presentation/logical
group mapping and stages one recipient-private intent for each final
recipient. Its logical key is based on Task identity, semantic source group,
and committed Task version; the PR07-A unique identity additionally includes
Tenant and recipient.

`tasks.notificationsV1` remains centrally registered and disabled by default.
When disabled it stops only the new PR07 Task Notification intents. It does
not bypass command authorization, semantic business invalidation, privacy
validation, or database dedupe.

## Server-authoritative `DeadlineAt` classification

The canonical versioned Task details update owns the optional hard-deadline
mutation. A presence-aware JSON value distinguishes an omitted member from an
explicit `null`; the Gantt planned-start/planned-end command has no
`DeadlineAt` member.

`TaskDeadlineChangeClassifier` uses the persisted old value, requested new
instant normalized to UTC, resolved Workspace timezone, and one server clock
instant. UTC normalization preserves the client-supplied instant and makes the
value safe for PostgreSQL `timestamptz`; classification still converts that
instant through the Workspace timezone for local urgency boundaries. It
applies this precedence:

1. `Added`: old null and new non-null;
2. `Removed`: old non-null and new null;
3. `ShiftAtLeast24Hours`: absolute instant shift greater than or equal to 24
   hours;
4. `CrossedUrgencyBoundary`: old and new instants occupy different
   Workspace-local `Overdue`, `Today`, or `Future` buckets;
5. `None`: none of the above.

`Overdue` means strictly before the captured server instant. A non-overdue
deadline whose Workspace-local calendar date is the current local date is
`Today`; later local dates are `Future`. Exactly 24 hours qualifies as a
shift, while 23 hours 59 minutes does not qualify as
`ShiftAtLeast24Hours`. Shift classification takes precedence when the same
change also crosses a boundary.

The safe classification name is recorded in Task audit metadata. The request
contract disallows unmapped members, including client attempts to supply
`isMajorDeadlineChange` or `deadlineChangeClassification`. Planning-date-only
changes do not invoke or mutate the hard-deadline contract.

## Approved semantic event mapping

The implementation follows the current canonical catalog rather than adding
the illustrative per-category synonyms from the task prompt.

| Mutation | Durable events staged in the business unit of work |
| --- | --- |
| Canonical Primary Assignee or Reviewer relationship | `Projects.TaskAssignmentChanged.v1` plus compatible `Projects.TaskChanged.v1`; recipient signals use `Notifications.NotificationCreated.v1` when an intent is created |
| Compatibility TaskAssignment adapter | Always compatible `Projects.TaskChanged.v1`; an actual mapped Primary Assignee, Reviewer, or Collaborator change also uses `Projects.TaskAssignmentChanged.v1`, and qualifying recipient changes use `Notifications.NotificationCreated.v1`. A canonical-first mirror or historical `Owner` maintenance/removal has no canonical relationship semantic or recipient signal |
| TaskComment create/update/delete | `Projects.TaskCommentChanged.v1` plus compatible `Projects.TaskChanged.v1`; significant create/update may also stage the recipient signal |
| Blocked, review, and hard-deadline changes | Compatible `Projects.TaskChanged.v1`; qualifying notification categories additionally stage the recipient signal |
| Existing Project-level invalidation where already required | `Projects.ProjectChanged.v1` |

`Projects.TaskWorkflowChanged.v1` is an approved catalog family for Workflow
Definition/configuration invalidation. It is not misused for an individual
Task review, Blocked, or deadline transition.

The implementation does not add these unapproved synonyms:

- `Projects.TaskRelationshipChanged.v1`;
- `Projects.TaskBlockedStateChanged.v1`;
- `Projects.TaskReviewRequested.v1`;
- `Projects.TaskReviewResolved.v1`;
- `Projects.TaskDeadlineChanged.v1`.

The retained general `Projects.TaskChanged.v1` remains the compatibility
invalidation hint. HTTP refetch remains authoritative.

### PR07-B routing boundary

`Projects.TaskChanged.v1` retains its accepted compatibility-invalidation
routes: exactly `project:{projectId}` plus each valid, deduplicated affected
User target. These User targets invalidate a user's My Tasks projection; they
are not Notification recipients and do not carry display data.

The new semantic events remain exactly `project:{projectId}` during PR07-B:

- `Projects.TaskAssignmentChanged.v1`; and
- `Projects.TaskCommentChanged.v1`.

Their compatible `affectedUserIds` argument is not converted to a routing
target until PR07-D supplies Task-specific dispatch/replay authorization. The
recipient-only
`Notifications.NotificationCreated.v1` signal remains exactly
`user:{notification.UserId}` with only `notificationId`, `stateVersion`, and
`requiresRefetch` in its payload.

## Transaction and dedupe design

`DbNotificationService.StageTaskByLogicalKeyAsync` stages, but does not save:

- the recipient Notification with safe generic presentation;
- the recipient's `NotificationUserState` version advance; and
- the recipient-only minimal Notification Outbox signal.

The Task command also stages its Task/relationship mutation, AuditLog, generic
and semantic business Outbox rows, and any automatic Watch reconciliation in
the same request-scoped `AppDbContext`. One production
`SaveTaskCommandAsync`/relational `SaveChangesAsync` commits or rolls back the
set as one PostgreSQL transaction. No SignalR send occurs before commit; only
durable Outbox rows are staged.

The Task optimistic-concurrency token prevents stale writers from publishing
partial records. The filtered PR07-A unique index over
`(TenantId, UserId, LogicalKey)` is the authoritative logical-intent dedupe
boundary. Same logical retries reuse the identity or become an idempotent
no-op; concurrent losing writers expose no committed Notification, state,
Outbox, audit, Watch, or Task mutation.

Focused PostgreSQL failure injection independently makes the Task row, audit
row, and Outbox row violate their real database length constraints. Every
scenario raises a database failure and verifies that all tracked Task,
relationship/Watch, Notification, NotificationUserState, Outbox, and AuditLog
writes remain at their pre-command counts.

## Privacy boundary

Task notification rows contain a generic category title, null body, and a
Task reference. Their `Notifications.NotificationCreated.v1` payload contains
only:

- `notificationId`;
- `stateVersion`;
- `requiresRefetch`.

Task semantic payloads contain only scope/aggregate identifiers, persisted
versions, a bounded change code, and `requiresRefetch`. Task-specific Outbox
validation and audit metadata filtering reject or remove forbidden broad
fields. Durable Task notification/event/log payloads must not contain comment
or description bodies, review/Blocked reasons, Watch state or opt-out,
private preferences, restricted titles/display text, attachment contents or
paths, credentials/secrets/tokens, or license material.

Current authorization is enforced when the intent is built. Later
dispatch/replay/open reauthorization and notification routing changes remain
outside PR07-B; the minimal signal deliberately requires a future authorized
refetch rather than carrying a private Task projection.

## Explicit exclusions

- digest ledger, worker, scheduling, and DST delivery behavior;
- notification-open endpoint;
- SignalR subscription authorization, routing, dispatcher, or hub changes;
- Angular production or other frontend behavior changes; one My Tasks facade
  regression test preserves the accepted HTTP-refetch contract;
- email and push delivery;
- PR06B and PR08;
- a new migration or notification schema beyond the accepted PR07-A
  foundation;
- merging the Draft PR.

## Focused evidence

| Check | Current result | Notes |
| --- | --- | --- |
| PR07-A same-SHA post-merge gate | Passed | CI `30724803612`, Code Quality `30724803621`, Documentation CI `30724803620`, and npm Security Audit `30724803615`; all `success` at `c5627eb09ecf19d66146eacdbc3e938c0a1c8563`. |
| Central policy/classifier/staging/semantic/privacy classes, Release | Passed: 60; failed/skipped: 0/0 | Covers exact recipient categories, actor/current-authorization/Watch behavior, both deadline shift directions and timezone boundaries, staged logical identity, minimal signals, approved event catalog/payloads, and audit privacy. |
| TaskComment no-op unit suite, Release | Passed: 14; failed/skipped: 0/0 | Covers empty and same-value PATCH zero mutation, `false -> true`, `true -> false`, body-only mention behavior on existing Important comments, and mention-plus-Important union. |
| Current-authorized TaskComment / Important safety / mention eligibility focused suites | Required for remediation final HEAD | Covers revoked-author PATCH/DELETE safe denial and zero delta, current author/Manager success, archived/deleted parent denial, single safety charge for combined body/Important updates, Important-only 429/retry/no delta, and stale Workspace/Project/Group mention exclusion. PostgreSQL execution remains mandatory and is not inferred from a skipped conditional run. |
| Task semantic routing suite, Release | Passed: 6; failed/skipped: 0/0 | Verifies `TaskChanged` retains exactly one Project route plus deduplicated valid affected-User projection-invalidation routes; the new Assignment/Comment events remain Project-only; Task payloads contain no restricted display fields. |
| My Tasks realtime coalescing regression, frontend unit | Passed: 1; failed/skipped: 0/0 | A loaded My Tasks facade handles `Projects.TaskChanged.v1` without an active Project detail view, refetches tasks and counts after the bounded debounce, and coalesces duplicate events to one request pair. |
| `dotnet test ... --configuration Release --filter "Scope=TaskV1PR07B"` with PostgreSQL 18 | Passed: 72; failed/skipped: 0/0 | Fresh local PostgreSQL 18 container; includes the added Important-comment no-op persistence regression and has no conditional skips. |
| PostgreSQL 18 focused atomicity suite, Release | Passed: 8; failed/skipped: 0/0 | Includes persisted `isImportant=true` and empty PATCH regression: Task/comment version, `UpdatedAt`, Notification, NotificationUserState, Outbox, and AuditLog remain unchanged after an existing Important notification. |
| PostgreSQL 18 compatibility concurrency subset, Release | Passed: 3; failed/skipped: 0/0 | Covers one compatibility writer winning, clean loser retry, atomic canonical mapping, canonical no-op retry, and composite role-change logical dedupe. |
| PostgreSQL migration apply for focused suite | Passed | Empty database migrated through the accepted PR07-A foundation; PR07-B adds no migration. |
| EF pending-model-change check, Release | Passed | Reports no model changes since the accepted PR07-A migration. |
| Full backend suite with PostgreSQL 18 | Passed: 632; failed/skipped: 0/0 | Both `POSTGRES_TEST_CONNECTION_STRING` and the application connection string targeted the same migrated database. |
| PR04-PR06 current-scope regression | Passed: 82; failed/skipped: 0/0 | Current remediation run used `Scope=TaskV1PR04|Scope=TaskV1PR05|Scope=TaskV1PR06` against PostgreSQL 18. |
| Task PR03C-PR06 scoped regressions | Passed: 146; failed/skipped: 0/0 | Includes the historical Prompt2C/Prompt2D supporting scopes carried by PR03C; they are not represented as a dedicated PR02 suite. |
| PR02-equivalent Task persistence/command/project/HTTP class regressions | Passed: 146; failed/skipped: 0/0 | PR02 has no dedicated `Scope=TaskV1PR02` trait, so the exact four-class filter below plus the full backend run is recorded explicitly. |
| HTTP tenant isolation | Passed: 32; failed/skipped: 0/0 | Runs the complete `HttpTenantIsolationTests` class, including the new hard-deadline contract. |
| Broad security boundary filter | Passed: 158; failed/skipped: 0/0 | Covers auth HTTP, exception privacy, file grants/storage/name safety, tenant isolation, pagination, seed isolation, and restricted student records. |
| Frontend unit/architecture/license/TypeScript/build | Passed | Unit: 42 files / 324 tests; architecture: 4/4; Syncfusion license guard: 4/4; TypeScript no-emit: passed; production and Storybook builds: passed with pre-existing size warnings. A chained Storybook attempt exhausted Node's default 2 GB heap; the isolated 4 GB command below passed. Inventory lint produced zero fatal parser/configuration errors. No Angular production source changed; one My Tasks facade regression test was added. |
| Local Windows Playwright diagnostic | Passed: 63; failed: 0; expected skipped: 3 | Diagnostic only; it is not Linux screenshot-baseline approval. |
| Pinned Linux Docker Playwright | Environment-blocked locally | Three MCR requests for `mcr.microsoft.com/playwright:v1.62.0-noble` ended with EOF before image build/test execution. Exact-final-HEAD hosted CI remains authoritative. |
| Dependency security reports | Completed | NuGet vulnerable-package scan: none. Root npm audit: 0. Active and legacy frontend lockfiles each report the same pre-existing 4 moderate / 2 high development-tool findings; repository npm Security Audit is report-only and lockfiles are outside PR07-B. |
| `git diff --check`, documentation integrity, and scope audit | Passed | No whitespace errors; all eight changed PR07-B documents passed strict UTF-8, NUL, and conflict-marker checks. The scoped file inventory excludes the pre-existing user-owned `qodana.yaml`, `.coglatas-spec-source/`, `.idea/`, `.tools/`, and `scripts/ci/verify-dotnet-sdk.sh` changes. |
| Draft PR exact-final-HEAD CI | Authoritative external evidence | CI, Code Quality, Documentation CI, npm Security Audit, and every required branch-protection check must be green at the immutable SHA recorded in the Draft PR body. |
| Draft PR body synchronization | Authoritative external record | The Draft PR body records exact commands, pass/fail/skip counts, environment limitations, final SHA, and run URLs without another self-referential source commit. |

### Exact local commands

The PostgreSQL value below was an isolated disposable PostgreSQL 18 connection
string. It is redacted from durable evidence.

```powershell
$env:POSTGRES_TEST_CONNECTION_STRING='<ephemeral PostgreSQL 18 connection string>'
$env:ConnectionStrings__DefaultConnection=$env:POSTGRES_TEST_CONNECTION_STRING

dotnet build Coglatas.slnx --configuration Release
dotnet ef database update --project src/Coglatas.Infrastructure --startup-project src/Coglatas.Web --configuration Release --no-build
dotnet ef migrations has-pending-model-changes --project src/Coglatas.Infrastructure --startup-project src/Coglatas.Web --configuration Release --no-build

dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~TaskNotificationRecipientPolicyTests|FullyQualifiedName~TaskDeadlineChangeClassifierTests|FullyQualifiedName~TaskNotificationProducerTests|FullyQualifiedName~DbNotificationTaskStagingTests|FullyQualifiedName~TaskSemanticRealtimeTests|FullyQualifiedName~DbAuditLoggerPrivacyTests"
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj --configuration Release --no-build --filter "Scope=TaskV1PR07B"
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~TaskV1Pr07BNotificationAtomicityPostgreSqlTests"
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~TaskV1CoreConcurrencyPostgreSqlTests.Compatibility"
dotnet test Coglatas.slnx --configuration Release --no-build
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj --configuration Release --no-build --filter "Scope=TaskV1PR03C|Scope=TaskV1PR04|Scope=TaskV1PR05|Scope=TaskV1PR06|Scope=TaskV1Prompt2C|Scope=TaskV1Prompt2D"
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~TaskV1PersistenceTests|FullyQualifiedName~TaskCommandServiceTests|FullyQualifiedName~ProjectServiceTests|FullyQualifiedName~HttpTenantIsolationTests"
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~HttpTenantIsolationTests"
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~AuthSecurityHttpTests|FullyQualifiedName~GlobalExceptionHandlingMiddlewareTests|FullyQualifiedName~FileDownloadGrantBoundaryTests|FullyQualifiedName~FileNameSanitizerTests|FullyQualifiedName~FileWorkspaceWorkflowTests|FullyQualifiedName~LocalFileStorageServiceTests|FullyQualifiedName~PaginationSafetyTests|FullyQualifiedName~AppDbContextSeedTests|FullyQualifiedName~StudentRecordRestrictedTests|FullyQualifiedName~HttpTenantIsolationTests|FullyQualifiedName~TenantIsolationSecurityTests"

npm.cmd --prefix frontend test
npm.cmd --prefix frontend run test:architecture
npm.cmd --prefix frontend run test:syncfusion-license
npm.cmd --prefix frontend exec tsc -- --noEmit -p tsconfig.app.json
npm.cmd --prefix frontend run build
$env:NODE_OPTIONS='--max-old-space-size=4096'
try { npm.cmd --prefix frontend run build-storybook } finally { Remove-Item Env:NODE_OPTIONS -ErrorAction SilentlyContinue }
npm.cmd run lint:frontend
npm.cmd run test:ui
npm.cmd run test:ui:angular:docker

dotnet list Coglatas.slnx package --vulnerable --include-transitive
npm.cmd audit --audit-level=critical
npm.cmd --prefix frontend audit --audit-level=critical
npm.cmd --prefix coglatas-frontend audit --audit-level=critical
git diff --check
```

No environment-unset conditional result is treated as PostgreSQL execution
evidence. Likewise, the successful PR07-A kickoff runs cannot substitute for
PR07-B exact-final-HEAD checks.

## Completion gate

Acceptance combines the source-backed local evidence above with the immutable
head SHA and hosted check URLs in the Draft PR body. Every exact-final-HEAD
check must be green. Even after those conditions are met, this task does not
authorize merging the PR.
