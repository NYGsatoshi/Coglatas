# TASK-V1-PR07 sequential implementation plan

Status: PR07-A was accepted and merged as PR #274 at
`c5627eb09ecf19d66146eacdbc3e938c0a1c8563`. PR07-B was accepted and merged as
PR #275 at `93b1c5e260e04c243ff84f7370aca4d869484087`. PR07-C was merged and
accepted as PR #277; its merge commit is
`8d0b8b20551076ecd73ead06aced4b80c94749e7`. PR07-D is active on
`task/v1-pr07-d-authorized-delivery-angular-reconciliation` from that current
`main` commit. PR07-E remains blocked until PR07-D is merged and independently
accepted.

Implementation baseline audited: `491d17db3701b7fb26010db8c0590eac7d24bd78`

Audit-plan merge commit: `919762f707be94acc14320215256c0463d10bcbb`

Original canonical specification audited: `6e8e5c3651adeedc7a2709124e9af0fd927d35b5`

Canonical owner-decision resolution:

- AIPsiteNYGspec PR: `NYGsatoshi/AIPsiteNYGspec#62`
- Specification merge commit: `8b90c8897367606473515d17d3696e458b2ee7b5`
- Resolved decisions: `PR07-OWNER-001` through `PR07-OWNER-003`
- Implementation-repository decision record: `docs/decisions/task-v1-pr07-owner-decisions.md`

PR07-C owner-approved contract supplement:

- Approval date: `2026-08-03`
- Decision record: `docs/decisions/task-v1-pr07-c-deadline-digest-decisions.md`
- Resolved topics: current effective-Watch relevance, zero-candidate success,
  and operator-restart attempt accounting

## Objective

Implement canonical Task in-app immediate notifications, Workspace deadline-digest preferences and daily digests, transactional Outbox delivery, current-authorized SignalR delivery/opening, and Angular reconciliation using the repository's existing Task, Notification, Outbox, realtime, Workspace-timezone, and shared frontend state owners.

Implementation uses one sequential lane:

```text
PR07-A -> PR07-B -> PR07-C -> PR07-D -> PR07-E
```

No two phases may be developed on simultaneous branches that edit overlapping Task, Notification, Outbox, realtime, Workspace settings, migrations, or Angular state. Each phase begins from current `main` after the preceding phase is merged and accepted.

## Resolved canonical decisions

### Mandatory Task-notification recipients

| Event category | Mandatory recipients |
| --- | --- |
| Primary Assignee assigned | new Primary Assignee |
| Primary Assignee removed | previous Primary Assignee captured before relationship mutation |
| Reviewer assigned | new Reviewer |
| Valid direct mention | each valid directly mentioned user |
| Submitted for Review | current Reviewer |
| Returned or rejected from Review | current Primary Assignee |
| Task becomes Blocked | current Primary Assignee and current Reviewer |
| Major hard-deadline change | current Primary Assignee and current Reviewer |
| Important `TaskComment` | current Primary Assignee, current Reviewer, and current Collaborators |

Rules:

- mandatory recipients and Watch-derived recipients are evaluated independently;
- explicit unwatch suppresses only Watch-derived activity;
- actor self-notification is suppressed, including self-assignment and self-mention;
- previous relationship recipients are captured inside the business transaction before removal/replacement;
- overlapping relationships produce one visible Notification per recipient-specific logical event;
- direct mention plus Important on the same TaskComment uses one `TaskCommentSignificant` recipient union;
- current authorization is checked at intent creation, delayed/replayed dispatch, and target open;
- ordinary comments without a valid direct mention and without Important MUST NOT notify all Task participants;
- broad projection routing is not a mandatory Notification recipient policy.

### Deadline-digest preference

```text
GET   /api/me/workspaces/{workspaceId}/task-notification-preferences
PATCH /api/me/workspaces/{workspaceId}/task-notification-preferences
```

Contract:

- `deadlineDigestLocalTime`: nullable local time without timezone;
- allowed values: `00:00` through `23:45`, inclusive, at 15-minute granularity;
- Workspace default: `08:00` local time;
- null inherits the Workspace default;
- `effectiveDeadlineDigestLocalTime` is non-null;
- `workspaceTimeZoneId` is authoritative when deriving a due instant;
- browser timezone and Project settings are not authoritative;
- Project-specific digest time does not exist;
- invalid format/minute/range returns HTTP 400 `TASK_NOTIFICATION_PREFERENCE_INVALID_LOCAL_TIME` with no rounding or mutation;
- PATCH requires `expectedVersion`;
- an omitted, zero, negative, or stale numeric `expectedVersion` that binds to the request DTO returns HTTP 409 `TASK_NOTIFICATION_PREFERENCE_VERSION_CONFLICT`, does not mutate the stored preference, and exposes only safe retry/version metadata;
- malformed JSON or an incompatible JSON value type (for example, `"expectedVersion": "abc"`) is rejected before the service by the shared safe HTTP 400 model-validation response, without mutation, retry metadata, or protected-state disclosure;
- GET and successful PATCH return stored value, effective value, Workspace timezone identity, body version, and optionally a matching ETag.

### Digest generation versus Outbox delivery

| Responsibility | Profile |
| --- | --- |
| Digest generation | separate `TaskDeadlineDigestJob` ledger; at most 3 automatic attempts; terminal `Failed`; no separate digest dead-letter table; each audited operator restart adds one operator attempt without resetting the automatic-attempt budget |
| Visible Notification | recipient-owned persistence/read/open lifecycle; no scheduler semantics |
| Transactional Outbox delivery | dedicated claim/lease/retry/replay profile; at most 10 automatic attempts; terminal `DeadLetter`; capability-gated audited replay |

The digest ledger owns schedule, candidate identity, idempotency, claim, generation attempts, and terminal generation state. It is never an Outbox scheduler. The Outbox is never digest candidate/idempotency state. After successful digest generation creates an authorized visible Notification, its realtime signal is delivered through the existing Transactional Outbox.

For PR07-C, a Task is digest-relevant only through current effective Watch after
automatic-source reconciliation. Manual Watch qualifies, explicit opt-out
suppresses digest relevance, and neither mere visibility nor Team Queue
eligibility qualifies. A zero-candidate ledger unit reaches `Succeeded` without
creating a Notification or Outbox row. The focused decision record above is the
authority for these rules.

### Canonical realtime event boundary

PR07 uses only the current approved event families:

```text
Projects.TaskChanged.v1
Projects.TaskAssignmentChanged.v1
Projects.TaskWorkflowChanged.v1
Projects.TaskCommentChanged.v1
Projects.ProjectChanged.v1
Notifications.NotificationCreated.v1
Notifications.NotificationReadStateChanged.v1
Security.AuthorizationStateChanged.v1
```

Do not create `TaskDeadlineDigestReady`, `TaskPreferenceChanged`, or unapproved per-category Task semantic-event families. A new event family requires a separate canonical catalog amendment with exact schema, routing, authorization, and acceptance.

## Scope boundary

Included:

- immediate in-app Task notification policy;
- server-authoritative `DeadlineAt` major-change classification;
- per-user/per-Workspace digest preference and Workspace default;
- Workspace-local daily digest generation, idempotency, bounded claiming, and DST behavior;
- Notification logical identity and database-enforced dedupe;
- transactional approved events through the existing Outbox;
- current-authorized user/Project/Workspace routing and explicit Group-route non-use where not approved;
- current-authorized notification opening;
- Angular preference, notification, digest, and Task-view reconciliation through shared state owners;
- health, metrics, runbook, PostgreSQL/HTTP/SignalR/Real Backend evidence.

Excluded from every phase:

- PR06B large-project Gantt pagination/virtualization or changes to current 500/2,000 fail-closed limits;
- PR08 integration/cutover work;
- email or mobile push;
- ordinary-comment notify-all;
- Presence, Typing, calls, public Hub, or separate feature sockets;
- Project-specific digest time;
- automatic schedule movement, recurring/personal Tasks, or Calendar/Scheduler;
- Critical Path, Baseline, Resource Leveling, cross-Project dependencies/move;
- Messaging message-body delivery or reuse of Messaging payloads as Task notifications;
- broad admin UI, legal hold/discovery, or broad notification export.

## PR07-A — Contract foundation, preferences, and dedupe primitives

Status: Merged and accepted in PR #274 at
`c5627eb09ecf19d66146eacdbc3e938c0a1c8563`.

### Goal

Land additive persistence and exact preference/dedupe contracts without enabling Task notification generation or scheduled digest processing.

### Accepted implementation status

The historical audit baseline above remains historical evidence. PR07-A
implemented only the foundation, using resolved specification commit
`8b90c8897367606473515d17d3696e458b2ee7b5`, and is now present on `main` in
merge commit `c5627eb09ecf19d66146eacdbc3e938c0a1c8563`.

- migration `20260801171714_AddTaskNotificationPreferenceFoundation` adds the
  nullable logical key, filtered unique index, private preference/version
  state, and `08:00` Workspace defaults;
- `CreateOrGetByLogicalKeyAsync` is an explicit, PostgreSQL-authoritative
  Notification primitive; legacy creation remains unchanged and legacy null
  keys remain valid;
- the two current-user preference routes enforce active membership,
  tenant/workspace isolation, exact quarter-hour values, inheritance, and
  version/ETag retry metadata without widening Workspace/member DTOs;
- the preference version is deliberately not an EF `WorkspaceMember`
  concurrency token. The repository's tenant/member/version-scoped conditional
  update remains the sole preference conflict authority, so unrelated Role or
  Status saves do not conflict with or overwrite a preference change;
- `tasks.notificationsV1` is registered centrally and remains disabled by
  default. It does not gate authorization, privacy, or dedupe;
- no Task producer, deadline classifier, digest ledger/worker, new semantic
  Outbox event family, SignalR route, notification-open endpoint, or Angular
  behavior is introduced.

The code-bearing candidate passed `HttpTenantIsolationTests` 31/31 and the
PostgreSQL-enabled `Scope=TaskV1PR07A` suite 11/11 with 0 skips using a
temporary PostgreSQL 18 container. The latter executes fresh/upgrade/Down
migration coverage, filtered unique-index and logical-key race coverage,
preference winner/loser/retry, and the Role/Status non-conflict regression.
`dotnet test Coglatas.slnx --no-restore --configuration Release -m:1` passed
507/507 with 0 failures/skips after applying migrations to that same isolated
CI-shaped database. EF reports no pending model changes, and the migration
script from `20260730120626_AddCanonicalGanttVersions` contains only this
foundation's additive columns and filtered index. The merge commit's
post-merge workflow runs passed: CI `30724803612`, Code Quality `30724803621`,
Documentation CI `30724803620`, and npm Security Audit `30724803615`.

### Included scope

- add a nullable bounded logical Notification key;
- add a unique filtered PostgreSQL constraint on `(TenantId, UserId, LogicalKey)` for non-null keys;
- extend `INotificationService` and `DbNotificationService` with a logical-key creation primitive that returns the existing authorized row on duplicate;
- retain legacy creation behavior for legacy callers until PR07-B normalizes producers;
- add nullable digest local time and an independent preference version to `WorkspaceMember`;
- add Workspace default digest local time (`08:00`) and an independent settings version to `Workspace`;
- implement the exact GET/PATCH current-user preference routes;
- return stored nullable value, effective inherited value, Workspace timezone identity, version, and optional ETag;
- enforce active Tenant/Workspace membership, privacy, quarter-hour validation, inheritance, and optimistic concurrency;
- add one centralized rollout key such as `tasks.notificationsV1`, default disabled, through the existing feature registry;
- update active API/operations documentation for the approved contracts.

### Explicit exclusions

- no Task notification producer;
- no `DeadlineAt` mutation/classifier;
- no digest ledger or worker;
- no semantic event emission;
- no SignalR routing change;
- no Angular preference/notification behavior.

### Expected components

- Domain: `CommunicationEntities.cs`, `WorkspaceEntities.cs`;
- Application: notification service contract, focused preference use case/DTOs, feature-key registry;
- Infrastructure: `DbNotificationService`, Workspace/communication EF configurations, repositories/DI;
- Web: thin current-user preference controller/use-case adapter;
- Tests: unit, hosted HTTP, migration, and conditional PostgreSQL evidence.

### Migration impact

One focused additive migration and snapshot update:

- nullable Notification logical key and filtered unique index;
- WorkspaceMember preference/time/version fields;
- Workspace default/time settings version with deterministic `08:00` backfill/default.

No digest ledger is added in PR07-A. Existing rows remain valid and legacy notification callers may leave the key null.

### Required tests

- fresh and upgrade migration, including existing legacy duplicate rows;
- concurrent same logical key creates/returns one visible Notification;
- distinct recipient/event/version/category creates a distinct row;
- soft-deleted logical row behavior is explicit and retry-safe;
- GET/PATCH active-member success, null inheritance, `00:00`, `23:45`, and every invalid class;
- omitted, zero, negative, and stale numeric expectedVersion values return the exact 409 contract without mutation; incompatible JSON value types use the shared safe HTTP 400 model-validation contract without mutation;
- another Workspace/Tenant/member denial with safe errors;
- revoked/expired membership denial;
- optimistic-concurrency winner/loser/retry;
- general Workspace/member DTOs do not expose private preference fields.

### Completion gate

PR07-A completes only when fresh/upgrade migration and PostgreSQL uniqueness/concurrency evidence pass, the two preference routes match the canonical contract, the feature remains disabled, and no Task notification/digest is generated.

## PR07-B — Immediate policy, hard-deadline classification, and transactional event production

Status: accepted and merged as PR #275 at
`93b1c5e260e04c243ff84f7370aca4d869484087`; same-sha post-merge `main` checks
passed

### Goal

Generate every immediate Notification intent and approved Task invalidation/event inside the canonical business transaction using PR07-A logical identity.

### Relationship-target authorization remediation

Primary Assignee, Reviewer, and Collaborator are relationships, not merely
notification candidates. Before a canonical command or the compatibility
`Assignee`/`Reviewer`/`Support` adapter creates, changes, or maintains one,
the target must be an active non-deleted User with a retained ProjectMember row
and current Workspace/Project visibility through `CanViewProject`. A stale
ProjectMember row fails closed with the existing safe Task authorization
contract before any relationship, Watch, Audit, Notification, Outbox, or save
is staged. The target check does not apply to authorized relationship cleanup,
including clearing/removing revoked relationships and deleting a legacy
assignment. This is separate from mandatory notification-recipient policy.

### Current implementation status

- `TaskNotificationRecipientPolicy` is the single mandatory/Watch recipient
  expansion boundary; it applies actor suppression, dedupe, active-user and
  current Project authorization checks.
- Canonical Task assignment, Reviewer, Blocked, review, direct-mention,
  Important-comment, and hard-deadline mutations stage logical Notifications
  through one focused producer. The compatibility assignment adapter maps
  `Assignee`, `Reviewer`, and `Support` to Primary Assignee, Reviewer, and
  Collaborator and uses that same producer in the same transaction. It emits a
  semantic event and Notification only for an actual canonical relationship
  change, so canonical-first mirrors do not duplicate intent. New or
  changed-to legacy `Owner` rows and operations that would ambiguously change
  canonical state fail closed; historical same-role `Owner`
  maintenance/removal and mismatched-row removal stay non-canonical cleanup.
  The legacy ordinary-comment notify-all path is removed.
- Canonical Task detail PATCH accepts an omitted, explicit-null, or timestamp
  `deadlineAt`. The server classifies the persisted old/new values with the
  Workspace timezone; Gantt planned dates remain separate and client
  significance fields are rejected.
- Task mutation/relationship state, AuditLog, approved business Outbox rows,
  logical Notifications, and recipient-only Notification signal Outbox rows
  are staged before the command's one database save.
- Only the approved catalog families are used. Payloads are reference-only and
  exclude Task/comment/review/Watch/preference/display/license/secret data.
- `tasks.notificationsV1` remains disabled by default and gates only new PR07
  Task Notification intent production; it does not alter command
  authorization, and every enabled intent still passes privacy and dedupe
  enforcement.
- TaskComment PATCH is presence- and change-aware: a request with neither
  body nor `isImportant` returns `TASK_COMMENT_UPDATE_REQUIRED`; a supplied
  value equal to persisted state returns success without updating the comment
  or Task version and without audit, Outbox, or Notification work. A comment
  becomes Important only on `false -> true`; a body update evaluates mentions
  only when the normalized body changed. An already-Important body update
  never replays Important relationship recipients.
- During PR07-B, the accepted `Projects.TaskChanged.v1` compatibility
  invalidation retains `project:{projectId}` plus valid affected-user
  projection-invalidation routes. The new
  `Projects.TaskAssignmentChanged.v1` and `Projects.TaskCommentChanged.v1`
  events remain `project:{projectId}`-only until PR07-D adds their
  Task-specific dispatch/replay authorization. `Notifications.NotificationCreated.v1`
  remains the recipient-only minimal `user:{recipientUserId}` signal.
- TaskComment update and delete recheck the current parent Task/Project
  visibility and current comment authority before author-or-Manager evaluation.
  A historical author identity or residual Project/Group membership row never
  grants a mutation after Workspace access, Project visibility, or parent Task
  lifecycle has been lost.
- A `false -> true` Important-only TaskComment PATCH has one explicit
  communication-safety significance check. A body change uses the body check
  instead; a combined body/Important update is charged once, while no-op and
  de-emphasis updates add no new rate-limit check.
- Mention candidate display and direct mention validation both apply bounded
  per-user current `CanViewProject` checks after repository eligibility. A
  stale ProjectMember or GroupMember without current Workspace access is not
  displayed and causes the entire direct-mention mutation to fail with the
  existing generic eligibility error.

### Included scope

- add one central recipient-policy service implementing the exact mandatory matrix;
- capture pre-mutation previous-assignee identity before removal/replacement;
- resolve mandatory and Watch-derived recipients separately;
- apply actor suppression and recipient/logical-event dedupe centrally;
- integrate assignment/removal, Reviewer assignment, direct mention, Blocked, Review submit, Review return/rejection, major deadline, and Important TaskComment into canonical services;
- remove or guard compatibility paths that can double-notify or notify all Task participants;
- add versioned `DeadlineAt` mutation through the canonical Task update contract, not Gantt planning commands;
- classify `Added`, `Removed`, `ShiftAtLeast24Hours`, `CrossedUrgencyBoundary`, and `None` from persisted old/new values in Workspace timezone;
- persist business mutation, audit, Notification intent, approved business invalidation, and Notification signal Outbox rows atomically;
- use only approved catalog event names and metadata-safe payloads.

### Explicit exclusions

- no digest ledger/worker;
- no notification-open endpoint;
- no SignalR Hub, subscription, dispatcher, or dispatch-authorization change;
  the new Assignment and Comment Task semantic Outbox records remain
  project-only until PR07-D;
- no Angular production behavior changes; one My Tasks realtime regression test
  preserves the accepted HTTP-refetch contract;
- no email/push.

### Required tests

- every mandatory category and exact recipient set;
- previous-assignee capture and replacement producing correct removal/assignment events;
- actor skip, self-assignment, and self-mention;
- mandatory-versus-Watch independence;
- one visible Notification for overlapping relationships and retry;
- ordinary comment no-notify-all;
- direct mention/Important union and unauthorized mention non-disclosure;
- every deadline major/non-major boundary in Workspace timezone;
- stale version, authorization denial, audit failure, and database failure roll back Task/Notification/Outbox together;
- compatibility and canonical routes cannot double-notify;
- forbidden payload/log fields are absent.
- empty/same-value TaskComment PATCH has zero Task/comment version, audit,
  Outbox, Notification, and NotificationUserState delta;
- `false -> true` is the only Important-comment recipient trigger; unchanged
  Important comments only notify newly validated direct mentions;
- `Projects.TaskChanged.v1` preserves its Project target and deduplicated
  valid affected-User projection-invalidation targets; the new Assignment and
  Comment Task semantic events are exactly `project:{projectId}`; and
  `Notifications.NotificationCreated.v1` is exactly its recipient user route.

### Completion gate

PR07-B completes only with PostgreSQL transaction/dedupe/isolation evidence, exact event-catalog compliance, and the rollout key still disabled.

## PR07-C — Workspace deadline-digest ledger and worker

Status: active on `task/v1-pr07-c-deadline-digest`, based on accepted PR07-B
merge `93b1c5e260e04c243ff84f7370aca4d869484087`

### Goal

Build bounded, independently retryable daily user/Workspace digests without duplicating visible Notifications or using the Outbox as a scheduler.

### Included scope

- add a separate digest ledger with unique user/Workspace/local-date/policy-version identity;
- implement bounded due-row selection and atomic database-safe claiming;
- use Workspace-local preference/default time and a documented DST gap/fold policy;
- group eligible Tasks for 3 days, 1 day, today, and overdue;
- enforce a deterministic commit-time current-state fence: lock and recheck membership, Workspace/Project/Task visibility and lifecycle, current effective Watch/relationship state, and every persistent `tasks.notificationsV1` source before creation;
- acquire the original digest Job `FOR UPDATE` and claimed Attempt `FOR UPDATE` at transaction start, validate token/status/Tenant/User/Workspace/Job/trigger identity, and hold those rows while same-recipient work waits for the User lock;
- use PostgreSQL `FOR SHARE` for Tenant, TenantSettings, active Subscription, Plan, TenantUser, Workspace, WorkspaceMember, Project, Group, ProjectMember, GroupMember, Task, WorkflowStage, Watch, and Collaborator state so independent digest readers coexist; reserve `FOR UPDATE` for the recipient User and claimed digest Job/Attempt;
- preserve the fixed lock order Job -> Attempt -> Tenant -> TenantSettings -> active Subscription -> Plan -> recipient User -> TenantUser -> Workspace -> WorkspaceMember -> sorted Project/Group/membership rows -> sorted Task/WorkflowStage/Watch/Collaborator rows; keep expiry recovery on `FOR UPDATE SKIP LOCKED` so it skips an active queued same-recipient claim and relies on crash/rollback for normal recovery;
- protect absent TenantSettings/Subscription, WorkspaceMember, ProjectMember, GroupMember, Watch, and Collaborator rows with matching stable-parent shared/exclusive pivots (Tenant, Workspace, Project, Group, and Task respectively), rather than a digest-only advisory lock;
- include manual Watch, suppress explicit opt-out, and exclude mere visibility and Team Queue eligibility from digest relevance;
- isolate each user/Workspace unit so one failure does not poison the batch;
- make at most 3 automatic generation attempts, then terminal ledger `Failed`;
- make each audited operator restart one operator attempt without resetting the exhausted three-automatic-attempt budget, with no digest dead-letter table;
- complete a zero-candidate ledger unit as `Succeeded` without creating a visible Notification or Outbox row;
- create one authorized visible digest Notification and `Notifications.NotificationCreated.v1` Outbox signal after successful generation;
- keep complete candidate Task lists out of realtime payloads, ordinary logs, and ordinary audit metadata;
- expose metadata-safe due/running/succeeded/failed/restart metrics and health.

### Migration impact

One focused migration for the digest ledger and due/claim indexes. The
same-Tenant concurrency remediation changes lock SQL and writer fences only;
it adds no migration or digest-specific table. Add a Task deadline query index
only when PostgreSQL plan evidence demonstrates it is required.

### Required tests

- four digest groups and exact local-date boundaries;
- one user in multiple Workspaces/timezones;
- DST nonexistent/repeated local time and Workspace timezone change;
- restart without double-send or permanent skip;
- revoked/expired membership and deleted/archived/completed/cancelled/inaccessible Task exclusion;
- automatic/manual effective Watch, explicit opt-out, relationship-source reconciliation, mere-visibility exclusion, and Team Queue exclusion;
- zero-candidate `Succeeded` with no Notification or Outbox row;
- bounded pages and one-user failure isolation;
- concurrent workers, claim timeout/recovery, 3-attempt terminal Failed, and one audited operator attempt per restart without budget reset;
- provider-authoritative concurrency cases named
  `DifferentUsersInSameTenantGenerateConcurrently`,
  `DifferentUsersInSameWorkspaceDoNotShareExclusiveFence`,
  `DifferentWorkspacesInSameTenantDoNotShareExclusiveFence`, and
  `SlowFirstClaimDoesNotExpireLaterSameTenantClaims`; these must demonstrate
  post-fence/commit progress before an intentionally paused unrelated
  generator is released, not merely simultaneous task start;
- `SameRecipientStillSerializesNotificationStateVersion`, proving two
  same-user Workspace digests leave unique versions 1/2 with two
  Notifications, two recipient-only Outbox rows, and two successful jobs;
- `SlowFirstSameRecipientClaimDoesNotExpireQueuedClaim`,
  `SameRecipientWaitingClaimIsSkippedByExpiryScanner`, and
  `SameRecipientQueuedClaimKeepsAutomaticAttemptBudget`, proving that a
  second Workspace digest has locked its own Job/Attempt before waiting on the
  shared recipient User, expiry scanning skips it, and neither attempt budget
  nor claim token changes; and
- `ClaimLostBeforeTransactionFenceStagesNothing`, proving a worker that loses
  its token before the transaction-start claim fence creates no Notification,
  NotificationUserState, Outbox row, or success transition;
- `ConcurrentTenantMutationWaitsForGenerationFence`,
  `ConcurrentFeatureDisableWaitsOrPreventsDigestCommit`, and
  `MissingWatchRowOptOutInsertCannotBypassFence`, proving lifecycle/feature
  mutation fencing and the absent-Watch phantom policy without a stale visible
  result;
- post-final-evaluation membership revoke, Workspace/Project archive, Task completion, explicit Watch opt-out, and relationship-removal races, proving that the mutation waits behind the fence or the generator retries without committing stale state;
- feature-disable claim release that restores automatic claim counters, preserves one pending operator-restart attempt, fences an old token, and stages no Notification or Outbox row;
- identical schedule upsert no-op behavior, changed preference/timezone rescheduling of only pending unattempted rows, and diagnostics that count actual writes;
- one normal in-transaction candidate evaluation pass, bounded pages, and a fresh evaluation only after a fence/persistence retry;
- one visible Notification under ledger retry and Outbox replay;
- safe metrics/logs and query-count/plan evidence.

### Completion gate

PR07-C completes only with PostgreSQL evidence that at least two distinct-user
generations can progress in the same Tenant and the same Workspace, while the
same-recipient Notification-state critical section remains one-at-a-time and
each queued claim retains its own Job/Attempt lock. Tenant/feature/lifecycle
and absent-row mutations must conflict with the fence, and expiry scanning
must skip a same-recipient claim waiting on the User row rather than consume
its automatic attempt or token. It additionally requires current-state
fencing, schedule idempotency, bounded retry, DST evidence, and an
operator-readable health state. The feature remains disabled.

## PR07-D — Current-authorized delivery/opening and Angular reconciliation

Status: active on `task/v1-pr07-d-authorized-delivery-angular-reconciliation`.
PR07-C was merged and accepted as PR #277 at
`8d0b8b20551076ecd73ead06aced4b80c94749e7`.

### Goal

Make delayed/replayed delivery and notification opening current-authorized, then expose the behavior through existing Angular state owners and the single shared realtime client.

### Included scope

- extend `RealtimeDispatchAuthorizer` with event-specific Task/Notification target resolution;
- reauthorize current Tenant/Workspace/Project/Task access immediately before delayed/retried/replayed delivery;
- use recipient-only `user:{userId}` routing for visible Notifications;
- treat Project/Workspace/Group/My Tasks routes as projection invalidation only, not mandatory recipient sources;
- make Workspace archive paths trigger approved authorization-state invalidation for affected users;
- add a recipient-owned notification-open use case/endpoint that reauthorizes the current target and returns an authorized route or uniform safe unavailable result;
- handle active, deleted, archived, revoked, unsupported, unknown, and digest targets;
- retain one `RealtimeFacade` connection and extend validators, stale guards, dedupe, reconnect, and catch-up;
- add Workspace-specific preference UI using PR07-A APIs;
- extend RightPanel for digest display, authorized open outcomes, safe unavailable state, and HTTP-plus-event dedupe;
- map approved invalidations into Task Detail, My Tasks, Project Detail, Kanban, and Gantt coalescing/edit-preservation flows;
- clear protected Task/preference/notification state before reauthorization after logout, Tenant switch, membership loss, or authorization invalidation.

### Explicit exclusions

- no separate socket client;
- no Angular construction of SignalR group names;
- no PR06B pagination/virtualization;
- no new digest scheduling behavior;
- no broad payload display fields.

### Required tests

- intent creation followed by membership revoke/archive before dispatch/open;
- Outbox retry/dead-letter/replay after access loss;
- recipient-only Notification routing and explicit non-use of unapproved Group notification routing;
- no protected payload through broad routes;
- notification-open target matrix and read-state ordering;
- HTTP response plus duplicate/replayed event creates one visible result;
- stale versions, bounded coalescing, and multi-tab duplicate events;
- reconnect reauthorization, catch-up, denial, and protected-state clearing;
- open edits remain visible and are not silently overwritten;
- accessible preference/digest UI across narrow/touch and light/dark states.

### Completion gate

PR07-D completes only when real server routing authorization, notification opening, and all frontend state owners pass focused tests without separate sockets. The rollout key remains disabled pending PR07-E.

## PR07-E — Operations, Real Backend acceptance, and integration evidence

Status: blocked until PR07-D is merged and independently accepted

### Goal

Close operational and end-to-end evidence before PR07 enablement or PR08 entry.

### Included scope

- extend health/metrics for Task event failures, logical-dedupe suppression, digest due/running/succeeded/failed/restart, Outbox pending/retry/dead-letter age/count, per-Workspace lag, invalid timezone/preference, and authorization suppression;
- add metadata-safe structured logging and alert thresholds;
- complete bounded Outbox replay and digest restart runbooks using existing operator conventions;
- add Real Backend two-user flows for assignment/review/mention/Important/Blocked/deadline, Watch independence, digest/open, revocation, duplicate/stale/replay, and reconnect;
- run full relevant backend/frontend/UI validation and authoritative Linux screenshots where approved baselines change;
- update active architecture, testing, operations, and status documentation to verified behavior.

### Validation commands/evidence

- `dotnet test Coglatas.slnx`, with explicit PostgreSQL environment reporting;
- `npm --prefix frontend test`;
- `npm --prefix frontend run build`;
- focused Real Backend Compose flows;
- `npm run test:ui:angular:docker` when authoritative screenshot parity is relevant;
- health, failed-generation restart, stale-lock, Outbox dead-letter, and replay drills.

### Completion gate

PR07-E completes only when all evidence is source-linked, flags can be safely enabled/disabled without authorization or dedupe bypass, and no protected values appear in payloads, logs, audit metadata, errors, screenshots, or reports.

## Privacy and payload rules

Broad events, ordinary logs, ordinary audit metadata, and non-authorized inspection surfaces MUST NOT contain:

- TaskComment or Task description body;
- review reason;
- Watch source or explicit opt-out;
- digest preference time;
- complete digest Task list;
- recipient relationship set;
- restricted title/display fields;
- attachment content, storage paths, grants, or tokens;
- credentials or secrets;
- stack traces, SQL, raw exceptions, or authorization internals.

Recipient-specific presentation is allowed only after the required current-authorization checks and must fall back to a safe unavailable state.

## Migration strategy

Use two additive, focused migrations:

1. PR07-A: Notification logical identity plus Workspace member/default preference state.
2. PR07-C: digest idempotency/claim ledger and only evidence-required query indexes.

Do not combine either migration with unrelated schema cleanup. Validate fresh and upgrade schemas. Deploy in this order:

```text
migration
-> backward-compatible code with feature disabled
-> worker/consumer compatibility
-> PR07-E evidence
-> explicit enablement decision
```

Never roll back the database while enabled code or in-flight workers depend on the added columns/tables.

## API strategy

- preserve existing Notification list/read/delete routes;
- add only the two canonical preference routes, one authorized open endpoint, and `DeadlineAt` in the existing canonical versioned Task mutation contract;
- keep controllers thin and place current user/Tenant/Workspace/Task checks in application use cases;
- use the existing typed error envelope and safe 403/404 behavior;
- never expose private preference state through general Workspace/member DTOs;
- never return protected target details from a denied/unavailable Notification open.

## Outbox and realtime strategy

- write Task Notification/event intents before the same transaction commit as the business mutation;
- rolled-back business changes leave no dispatchable Outbox row or visible Notification;
- retain at-least-once delivery and stable event-ID/client dedupe;
- enforce visible-Notification dedupe in PostgreSQL through recipient-specific logical identity;
- keep digest due/candidate state out of Outbox;
- keep approved broad Task events metadata-only and use HTTP refetch as authoritative reconciliation;
- use `Notifications.NotificationCreated.v1` for both immediate visible Notifications and successful digests;
- reauthorize at creation, dispatch/replay, and open;
- delivery never grants HTTP access.

## Frontend strategy

- keep `RealtimeFacade` as the only transport, reconnect, and catch-up owner;
- keep HTTP authoritative for RightPanel, Task Detail, My Tasks, Project Detail, Kanban, and Gantt;
- extend existing stale-version guards, refetch coalescing, edit preservation, authorization clearing, degraded indicator, and manual refresh;
- do not store the preference only in browser state;
- do not construct SignalR group names in feature code;
- do not expose hidden routes, Task titles, comment bodies, or notification targets after authorization loss.

## Feature flags and rollout

Add at most one centralized Task-notification rollout key through the existing registry. Default it off through PR07-A, PR07-B, PR07-C, and PR07-D. Existing `realtime.signalR` and `communication.transactional_outbox.enabled` continue to govern their infrastructure but never replace authorization, privacy, or dedupe.

PR07-E may recommend enablement. Actual integration/cutover remains PR08 scope.

## Rollback strategy

- disable new Task Notification/digest production first;
- never disable authorization, payload validation, or dedupe independently;
- stop new digest claims; a generator that observes feature disable after a
  claim must fenced-release it immediately rather than consume automatic budget
  or append another operator attempt;
- continue consuming already-written supported Outbox schemas during a compatibility window;
- preserve Notification logical keys and digest ledger rows to prevent duplicate visible results;
- use forward fixes for persisted-data defects;
- down migrations are safe only before feature use or with explicit data-loss handling;
- HTTP/manual refresh remains the functional fallback when SignalR is disabled or degraded.

## PR06B exclusion

PR06B issue #270 remains outside every PR07 phase. The current Gantt safety contract remains:

```text
500 combined canonical WorkItems and Milestones
2,000 active same-Project dependencies
typed HTTP 400 on overflow
fail closed
no silent truncation
no partial successful snapshot
```

No PR07 phase changes Gantt pagination, virtualization, adapter limits, or PR06 acceptance behavior.

## PR08 entry conditions

PR08 may begin only after PR07-E proves:

- all owner decisions are implemented exactly as canonicalized;
- immediate-recipient, actor-suppression, Watch-independence, dedupe, three-point authorization, and deadline-classifier tests pass;
- preference quarter-hour/inheritance/concurrency tests pass;
- digest idempotency, timezone, DST, 3-attempt Failed, restart, and worker health pass;
- Outbox 10-attempt DeadLetter, current-authorized replay, and observability pass;
- shared-client reconnect/catch-up and protected-state clearing pass on a real backend;
- preference/digest/open UI is accessible and Workspace-specific;
- PR07 flags have a documented safe enable/disable procedure;
- PR06B remains separate and no PR08 cutover code was pulled into PR07.

## Immediate next action

PR07-C may now proceed from
`93b1c5e260e04c243ff84f7370aca4d869484087` using its exact scope above and the
focused digest decision record. The rollout key remains default disabled.

PR07-C must not add frontend/open behavior, SignalR route changes, PR06B, PR08,
email/mobile push, Project-specific digest time, a digest dead-letter table, or
Outbox-as-scheduler behavior.
