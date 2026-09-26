# Database

Last broad implementation audit: 2026-08-02. WPC-02A/B/D schema status
update: 2026-08-24. Issue #362 Message-thread schema update: 2026-08-28.

## Technology

- PostgreSQL.
- EF Core 10.
- Npgsql EF Core provider.
- One `AppDbContext` in `src/Coglatas.Infrastructure/Persistence/AppDbContext.cs`.

The runtime requires `ConnectionStrings:DefaultConnection`; infrastructure registration throws when it is absent.

## Schema sources of truth

Use these in order:

1. `AppDbContext` DbSets.
2. Entity classes under `src/Coglatas.Domain/Entities/`.
3. Fluent configurations under `Infrastructure/Persistence/Configurations/`.
4. `Infrastructure/Persistence/Migrations/AppDbContextModelSnapshot.cs`.
5. Applied database migration history in the target environment.

`docs/DATA_MODEL.md` is a human-readable field inventory, not the authoritative schema.

## Migration history

There are fifty-one timestamped EF migration classes in the current source,
from:

- `20260606135558_InitialCreate`
- through `20260829153230_AddConversationInboxLater`

Migration files live in `src/Coglatas.Infrastructure/Persistence/Migrations/`.

The application does not auto-migrate. `/health/ready` fails when pending migrations exist.

## Model groups

### Platform and tenancy

- Tenant, TenantSettings
- Plan, Subscription, UsageRecord
- TenantUser
- IdempotencyRecord
- ExportJob
- IntegrationAccount, WebhookEndpoint, ApiToken

### WPC canonical creation and lifecycle persistence

Migration `20260813100711_Wpc01WorkspaceCreateIdempotency` adds only
`idempotency_records`. The unique key is
`(TenantId, ActorUserId, Operation, KeyHash)`. The raw client identity and
request body are never stored; SHA-256 hashes retain retry identity and request
equivalence. A resource index supports safe reconciliation, and the actor
foreign key uses restricted deletion.

Migration `20260817023749_Wpc02BCapabilityGrantWorkspaceGeneral` adds persisted
Capability Grants plus the Conversation `DefaultKind`, unique partial indexes,
and shape constraints for `WorkspaceGeneral` and `ProjectGeneral`. Production
registers the persistence-backed `WorkspaceGeneral` initializer. The
idempotency claim, active Workspace, creator Owner membership, canonical
`WorkspaceGeneral` Conversation and creator participation, audit row, and
authorization Outbox rows commit in one PostgreSQL transaction. Failed
initialization or required Outbox staging rolls the claim and every business
effect back, so a later retry is not mistaken for a successful replay.
Idempotency records currently have no automatic expiration; they retain replay
identity indefinitely unless a separately approved retention operation is
added.

Migration `20260816041835_Wpc02AProjectVisibilityAndActivationProvenance` adds
nullable Project `Visibility` and explicit activation/recovery provenance.
Canonical creates persist `WorkspaceVisible`, `MembersOnly`, or `Restricted`;
pre-migration rows remain `NULL` and therefore explicitly unclassified instead
of receiving a guessed authorization policy. Existing rows receive
`ActivationState = LegacyUnknown`, while canonical creates use
`NeverActivated` and successful activation records `Activated`,
`ActivatedAtUtc`, and a positive `ActivationVersion`. `SuspendedFromStatus` and
`ArchivedFromStatus` retain recoverable lifecycle provenance, with database
constraints and the governance save interceptor rejecting inconsistent state.
Restore or resume succeeds only when the stored prior state is canonical and
consistent; ambiguous legacy state continues to fail closed with a non-mutating
`InvalidStateTransition`.

### Identity

- User, Session, Invite

### Message notification delivery preference

Migration `20260823054500_AddMessageNotificationPreference` adds the non-null
`tenant_users.MessageNotificationsEnabled` Boolean column with default `true`.
The additive default preserves existing Message notification behavior until a
user explicitly disables it for that Tenant membership.

The preference store always predicates reads and updates on Tenant ID, user ID,
and active membership status. It is intentionally private current-user state;
it is not projected through general Tenant membership DTOs. Updating it also
updates the membership row's existing `UpdatedAt` audit timestamp. The Down
path removes only the new column; values written to it are not retained after
rollback.

### Issue #355 private Later state and inbox index

Migration `20260829153230_AddConversationInboxLater` adds the non-null Boolean
`conversation_members.IsLater` with default `false` and the query index
`(TenantId, UserId, IsLater)`. The default preserves every existing
participant as not deferred without changing read cursors, unread state,
mentions, mute, archive, or membership lifecycle.

Later is participant-private workflow state. It is stored on the same exact
current-user membership row as mute/archive and is never aggregated from
another participant. Inbox rows and all four category counts first compose the
existing recursive readable-Conversation relation, then apply unread,
recipient-owned Mention, or Later predicates. The Down path drops only this
index and column.

### Issue #368 participant-private saved Message state

Migration `20260829173340_AddMessageFollowUps` creates
`message_follow_ups`. Each row has the normal audited identity plus
`TenantId`, `UserId`, and `MessageId`; the unique index
`(TenantId, UserId, MessageId)` is the durable idempotency boundary. The paging
index `(TenantId, UserId, CreatedAt, Id)` supports newest-saved ordering. User
and Message foreign keys use restricted deletion.

Row presence is the complete saved/active state. Completing a follow-up hard
deletes only that private marker and never changes `ReadState`,
`ConversationMember.LastRead*`, unread/Mention state, or conversation-level
`IsLater`. Access revocation does not delete the marker; every list read joins
it back through the current readable-Conversation relation, so inaccessible
rows are absent from both results and totals. No reminder timestamp or delivery
state is stored. The Down path drops only `message_follow_ups`.

### Issue #362 same-Conversation Message threads

Migration `20260827154230_AddMessageThreadRootContext` adds nullable UUID
`messages.ThreadRootMessageId` as a restricted self-foreign key. Existing
Message rows remain `NULL`; there is no legacy backfill and no attempt to infer
an anchor from `ConversationType.Thread` or `ParentConversationId`.

`CK_messages_thread_root_not_self` rejects a Message that points to itself.
The application and repository additionally require a reply's current Tenant
and Conversation to match the authorized canonical root because a simple
self-foreign key cannot express that composite invariant. The deterministic
reply lookup index is
`(TenantId, ConversationId, ThreadRootMessageId, CreatedAt, Id)`; EF also adds
the ordinary single-column foreign-key index.

The main-timeline summary query ranks distinct participant display names with
PostgreSQL `ROW_NUMBER()` partitioned by root and applies the three-name limit
inside the provider query. No more than `root count * 3` participant rows are
materialized. This query still runs only after the Conversation read boundary.

The existing filtered unique index
`IX_messages_TenantId_ConversationId_AuthorUserId_ClientRequest~` (the exact
63-byte PostgreSQL name persisted by migration `20260718125541`) is the
concurrent idempotency boundary for Message writes. The Infrastructure commit
coordinator reconciles only an Npgsql unique violation naming that exact
constraint; PostgreSQL rolls back all staged audit, notification, and Outbox
rows before the losing context is cleared and reloads the committed winner.
Unrelated database exceptions are propagated. This repair does not rename the
live index or add a migration.

Deletion is restricted while replies reference a root. Current Message delete
operations retain rows as tombstones, so deleted replies remain ordered and
counted while their body and attachments are not projected. The main Message
query keeps a deleted root only when an explicitly same-Conversation reply
exists; the global Tenant filter and same-Conversation predicate prevent a
corrupt cross-scope link from making it visible. It remains a pinned bodyless
tombstone with its summary and ordering, but cannot receive new replies.
Ordinary deleted Messages with no replies remain omitted. The additive Down
path removes only the foreign key, indexes, check, and nullable column;
rollback discards thread links but does not remove Message rows.

### Organization and communication

- Workspace, WorkspaceMember
- Group, GroupMember
- Channel, ChannelMember
- Post, PostThread
- Conversation, ConversationMember, Message, MessageAttachment, MessageFollowUp, ReadState
- Announcement, AnnouncementRead, Notification
- TaskDeadlineDigestJob, TaskDeadlineDigestAttempt

### Events and forms

- ActivityEvent, EventAttendance
- InternalForm, FormQuestion, FormResponse, FormAnswer

### Projects and files

- Project, ProjectMember, Milestone
- TaskItem, TaskDependency, TaskAssignment
- TaskWorkflowDefinition and TaskWorkflowStage, including one Project Kanban
  display default and warning-only Stage WIP limits
- ActivityLog, Comment, Feedback
- Artifact, ArtifactVersion
- FileObject, Attachment, FileScanResult

### Issue #350 structured Task Brief

Migration `20260824220000_AddStructuredTaskBrief` adds three nullable
`character varying(4000)` columns to `task_items`:

- `BriefGoal`;
- `BriefDeliverable`; and
- `BriefConstraints`.

The migration is additive and performs no data backfill. In particular, it
does not reinterpret `TaskItem.Description` or `Project.Description`, and it
does not manufacture inherited values for existing Tasks. Null means the
Task-specific value is not set. The fields are ordinary Task-body data under
the existing Task version/concurrency and Tenant/Project authorization
boundaries.

No index is added: these nullable text fields are returned on authorized Task
detail and are not a list filter, join key, ordering key, or current Search
input. The Down path removes only the three new columns; values written to
them are not retained after rollback. Existing `Description` data is left
unchanged in both directions.

### Issue #357 Task execution source-scope foundation

Migration `20260825081645_AddTaskExecutionScopeFoundation` adds three purpose-
specific tables:

- `project_execution_scopes`: exactly one default two-boolean policy for every
  Project;
- `task_execution_scope_overrides`: an optional complete policy replacement for
  one Task; and
- `task_execution_runs`: append-only immutable policy snapshots for accepted
  run requests.

Existing Projects are backfilled with a disabled `WebEnabled` and disabled
`ProjectFilesEnabled` default at version 1. New Projects receive the same
default through the normal persistence boundary. The migration uses uniqueness,
foreign keys, and PostgreSQL guards to require the copied Tenant/Workspace/
Project scope to match the owning Project/Task. It prevents deletion of a
Project default and deletion or snapshot-field mutation of a Task execution
run. Lifecycle result fields remain deliberately mutable for a future approved
runtime.

These tables preserve policy metadata only: versions, booleans, origin,
requester, and timestamps. They contain no URLs, hosts, file identifiers or
names, source bytes, credentials, provider configuration, prompt, or output.
The migration creates no retrieval queue, worker, provider, or source-content
store.

### TASK-V1-PR05 Project Kanban

Migration `20260729140506_AddProjectKanbanDefaultSwimlane` adds the
non-null, string-converted `KanbanDefaultSwimlane` property to
`task_workflow_definitions`, defaulting existing definitions to `None`.

Kanban reuses canonical persistence:

- `TaskWorkflowStage.SortKey` is column display order.
- `TaskWorkflowStage.WipWarningLimit` is warning-only configuration.
- `TaskItem.WorkflowStageId` is card Stage.
- `TaskItem.SortKey` is stable card order.
- Task and Workflow Definition versions remain optimistic concurrency tokens.

No vendor board or card table exists. The board query uses the current
Tenant filter, Project scope, existing Stage/Task indexes, bounded card
projection, and one batched direct-child aggregate for canonical parent
summary values.

### TASK-V1-PR06 Canonical Project Gantt

Migration `20260730120626_AddCanonicalGanttVersions` adds:

- non-null `Project.VersionNo bigint`, default 1; and
- non-null `Milestone.VersionNo bigint`, default 1.

Both are EF optimistic concurrency tokens. Canonical Tasks and Workflow
Definitions already had version tokens; PR06 does not add another Task or
dependency version store.

The migration performs no Task progress or other domain-data update. Its
additive Down path removes only the two new version columns and preserves
existing Project, Milestone, Task, and dependency rows.

PR06 continues to reuse canonical persistence:

- `TaskItem.PlannedStartDate` and `TaskItem.PlannedEndDate` remain authoritative
  day-precision Task planning values. Maintained legacy `StartDate`/`DueDate`
  columns are synchronized for the flag-disabled compatibility view.
- `TaskItem.ProgressPercent`, Workflow Stage, Blocked state, hierarchy,
  priority, and primary-assignee relationship remain shared with List, My
  Tasks, and Kanban.
- Parent dates and progress are derived only from direct, non-deleted canonical
  Task-kind children and are never stored in a Gantt-owned parent row.
- Compatibility `Milestone.DueDate` is its zero-duration date. The nullable
  legacy column is retained; PR06 commands require a date and the snapshot
  warns on pre-existing null rows.
- `TaskDependency` remains the only dependency persistence. PR06 authors
  same-Project Finish-to-Start edges only and retains legacy non-FS rows as
  read-only warning inventory. Bounded dependency reads filter both ends to
  active same-Project canonical Task rows.
- `DeadlineAt` remains a separate UTC timestamp and is not updated by Gantt
  schedule commands.
- AuditLog and transactional Outbox rows share the same EF command save as
  schedule, progress, Milestone, and dependency mutations.

Terminal parent/child behavior is an Application invariant, not new schema:
Done/Cancelled parents reject new subtasks until reopened, terminal children
cannot reopen under a terminal parent, and parent Done requires terminal direct
Task children plus derived progress 100. All-cancelled children derive progress
0 and therefore cannot complete the parent as Done. Review override completion
uses that same guard; restore and delete of a child under a terminal parent are
also rejected until the parent is reopened.

Task lifecycle and dependency mutations that can change the visible Project
graph advance the shared `Project.VersionNo` in the same transaction. That
aggregate revision makes a concurrent Task-delete/dependency-add race fail as
an optimistic concurrency conflict. PostgreSQL regression coverage verifies
that no stale dependency edge is committed. Visible dependency rejections are
audited with metadata-safe reason codes and do not persist hidden-neighbor
metadata.

No Gantt-specific schedule, progress, calendar, vendor Task, or dependency
table was added. No PR06 index was added: the projection uses the existing
Tenant/Project/task/dependency relationships and bounds the graph before row
materialization.

The repository snapshot projection performs seven measured, deterministic,
set-based SQL commands:

1. Project identity/version;
2. Task count;
3. Milestone count;
4. Workflow version;
5. bounded Tasks with reference joins for Stage and primary assignee;
6. bounded Milestones; and
7. bounded same-Project dependencies.

This seven-command count is intentionally scoped to
`PlanningRepository.GetGanttAsync`. The authorized real Kestrel/PostgreSQL
snapshot is separately measured and asserted at exactly 24 commands total:
tenant resolution, cookie/session authorization, membership and timezone
lookups, plus the seven projection commands. The bounded query shape has no
row-per-item N+1 path. An item-overflow response stops after the two counts plus
Project lookup and does not load Task rows or the dependency graph. The
repository also rechecks the combined Task/Milestone row count after the
bounded reads, closing the PostgreSQL READ COMMITTED insert race between the
initial counts and materialization.
PR #259 was merged before its numerical limit decision was formally recorded.
On 2026-08-01, after the merge, the owner approved the existing safeguards as
the temporary PR06 full-snapshot contract: 500 combined canonical Task-kind
WorkItems and Milestones, consistently across snapshot, schedule, progress,
and dependency paths, and 2,000 active same-Project dependencies with active
canonical Task endpoints. Overflow is rejected with typed HTTP 400 and is
never silently truncated or returned as a successful partial snapshot.

These response limits do not constrain the number of records stored for a
Project. They are not permanent Project capacity limits, database storage
limits, or general-availability scalability guarantees. Large-project Gantt
delivery is deferred to
[`TASK-V1-PR06B` issue #270](https://github.com/NYGsatoshi/Coglatas/issues/270).

Latest-main code-bearing candidate
`1abce6c70d9f665b773d35f75d63c0d05a387cc8` repeated focused PostgreSQL 18.4
integration evidence: it applied the migration to an empty database, upgraded
the PR05 migration, migrated down additively, and reported no pending
migrations/model changes. The focused tests captured exact SQL in xUnit
evidence and asserted the seven-command repository projection and 24-command
authorized HTTP total, deterministic order/limits, cancellation, and no N+1 or
unbounded graph load. They also exercised the post-read count race, Project and
Milestone concurrency, and the Project-revision Task-delete/dependency-add
race. The fixture is intentionally small, so no `EXPLAIN` claim or index-plan
claim is made. Evidence is recorded in
`docs/verification/task-v1-pr06-gantt.md`; exact final-HEAD Hosted evidence is
pending the documentation commit.

### TASK-V1-PR07-A notification preference and logical-dedupe foundation

Migration `20260801171714_AddTaskNotificationPreferenceFoundation` is one
focused additive migration. It adds:

- nullable `notifications.LogicalKey` (`character varying(512)`), retaining
  null for every legacy row;
- unique filtered PostgreSQL index
  `IX_notifications_TenantId_UserId_LogicalKey` over
  `("TenantId", "UserId", "LogicalKey") WHERE "LogicalKey" IS NOT NULL`;
- nullable `workspace_members.TaskDeadlineDigestLocalTime` (`time without time
  zone`) and non-null `TaskNotificationPreferenceVersion bigint`, default 1;
- non-null `workspaces.DefaultTaskDeadlineDigestLocalTime` (`time without time
  zone`), default/backfilled to `08:00`, and non-null
  `TaskNotificationSettingsVersion bigint`, default 1.

The logical-key index deliberately does not include `DeletedAt`: a
soft-deleted notification retains its identity so a duplicate replay cannot
resurrect it. Legacy null-key notifications may coexist because the filtered
index does not apply to them. The per-member preference version is private
preference state and is separate from the Workspace settings version. It is not
an EF entity-wide concurrency token: the private preference repository is the
only concurrency authority and uses a tenant/member/version-scoped conditional
update. Therefore unrelated WorkspaceMember Role or Status saves neither
conflict with nor overwrite a concurrent preference update.

The Down migration removes only this filtered index and the five additive
columns. It preserves existing Notification, Workspace, and WorkspaceMember
rows, but—as with any column-removal rollback—values written only to the new
columns are not retained after rollback. No digest ledger, worker, or Task
notification-producer schema is added by this migration.

### TASK-V1-PR07-C Workspace deadline-digest ledger

Migration `20260803041347_AddTaskDeadlineDigestLedger` adds two tenant-owned
tables and no unrelated schema:

- `task_deadline_digest_jobs` is the durable generation identity and current
  state. Its unique index `IX_task_deadline_digest_jobs_identity` covers
  exactly `(TenantId, WorkspaceId, UserId, LocalDate, PolicyVersion)`.
- `task_deadline_digest_attempts` is the append-preserved automatic/operator
  attempt history. `(JobId, AttemptNumber)` is unique, and filtered unique
  index `IX_task_deadline_digest_attempts_one_active` permits only one
  `Pending` or `Claimed` attempt for a job.

The job records `Pending`, `Claimed`, `Succeeded`, or `Failed` plus
`AttemptCount`, `AutomaticAttemptCount`, monotonic `AttemptSequence`,
`ScheduledForUtc`, `NextAttemptAt`, claim owner/token/timestamps,
`CompletedAt`, a bounded `LastErrorCode`, and the optional resulting
`NotificationId`. Check constraints require coherent claim/completion fields,
a positive policy version, valid attempt counts, and exactly three automatic
attempts before terminal job failure. The Notification, Workspace, and User
foreign keys use `Restrict`.

Each claim creates or consumes an attempt row. The claim token is an optimistic
fence on job and attempt state: a worker holding an expired token cannot later
complete the reclaimed job. Expired claims finish their attempt as `Expired`;
an automatic job is returned to `Pending` only while its three-attempt budget
remains. PostgreSQL selection orders deterministically and uses `FOR UPDATE
SKIP LOCKED`, so concurrent workers can claim different due rows without
claiming one row twice.

Each generation transaction first locks its claimed Job `FOR UPDATE` and then
its claimed Attempt `FOR UPDATE`, before it reads current context or requests
the recipient User lock. It validates the original token and `Claimed` status
on both rows, the Job's Tenant/User/Workspace identity, and the Attempt's Job
and trigger identity. The Job lock stays held through commit; therefore the
same `FOR UPDATE SKIP LOCKED` expiry selector skips a live same-recipient
generation that is waiting for the User row. A process crash, connection loss,
or rollback releases that lock, after which normal expiry recovery may reclaim
the expired claim. No heartbeat, lease extension, or longer timeout is used.

Feature-disable release uses the same job-and-attempt token fence. It clears a
currently claimed automatic job back to `Pending`, restores its two automatic
claim counters, and completes that attempt as `Deferred`; it creates no Notification or
Outbox row. A claimed operator restart instead returns its existing audited
attempt to `Pending`, without appending another row or changing automatic
counts. A stale token is rejected by every later completion, defer, failure,
or release operation.

The automatic budget is exactly three. `AttemptCount` and
`AutomaticAttemptCount` reflect claims, except that a feature-disabled release
reverses only the just-claimed fenced automatic attempt. An operator restart
never resets the automatic budget. A Platform/System administrator's approved
restart of a terminal job adds a new `OperatorRestart` attempt linked
through `RestartedFromAttemptId`, records `RequestedByUserId`, increments the
monotonic sequence, and writes `TaskDeadlineDigestRestarted` to AuditLog in the
same transaction. It authorizes one operator attempt; failure or expiry of
that operator attempt is terminal. Earlier attempt rows and the three-count
automatic history remain intact. There is no independent dead-letter table.

Two focused partial indexes match the worker's scheduler queries:

- `IX_task_deadline_digest_jobs_due` on
  `(TenantId, NextAttemptAt, CreatedAt, Id)` for `Status = 'Pending'`;
- `IX_task_deadline_digest_jobs_claim_expiry` on
  `(TenantId, ClaimExpiresAt, Id)` for `Status = 'Claimed'`.

The schedule upsert updates an existing identity only when it is `Pending`, has
no prior claim/attempt sequence, and its calculated `ScheduledForUtc` or
`NextAttemptAt` differs. PostgreSQL expresses that rule in the `ON CONFLICT`
`DO UPDATE` predicate with `IS DISTINCT FROM`; a repeated identical upsert
affects zero rows and does not change `UpdatedAt`. The fallback leaves an
identical entity untouched and does not save. Consequently, scheduler
diagnostics count inserts or meaningful schedule changes, not each candidate
examined during a poll; claimed and attempted identities are not rewritten.

The conditional PostgreSQL suite captures `EXPLAIN (ANALYZE, BUFFERS)` output
and requires those exact indexes for due and expired-claim selection. Candidate
reads are asserted as one bounded SQL command per page, with a hard page-size
ceiling of 500 and deterministic `(DeadlineAt, Id)` order. No speculative
Task-deadline index is added: the current small fixture is not representative
plan evidence for such an index. Production-volume candidate plans remain an
explicit environment verification item.

Generation does not use the Outbox as a schedule table. Its one normal
candidate-page enumeration runs inside a short transaction; bounded
lock/recheck queries validate each already enumerated page rather than forming
a discarded second enumeration. Every generation transaction first acquires
the original claim ownership fence: digest Job `FOR UPDATE`, then claimed
Attempt `FOR UPDATE`, with token/status/Tenant/User/Workspace/Job/trigger
identity validation. Only then does the current-state fence lock Tenant,
TenantSettings, active Subscription(s), their Plan source(s), recipient User,
TenantUser, Workspace, WorkspaceMember, Project, Group, ProjectMember,
GroupMember, Task, WorkflowStage, Watch, and Collaborator. Those rows use
`FOR SHARE` except recipient User `FOR UPDATE`; multiple IDs of one kind are
locked in ascending ID order.

`FOR SHARE` was selected from PostgreSQL's row-lock compatibility matrix:
multiple digest readers coexist, while normal `UPDATE`/`DELETE` locks conflict
until the digest commits. Thus neither the Tenant nor Workspace is a
digest-wide exclusive fence. The recipient User row is deliberately exclusive
so same-user digests serialize before they advance `notification_user_states`;
different users in the same Tenant can still progress independently. The Job
and Attempt retain exclusive claim-token fencing so no two generators can
complete one claim. Their lock is held before any same-recipient User-lock
wait, so expiry scanning with `FOR UPDATE SKIP LOCKED` cannot consume a live
queued claim's automatic-attempt budget.

The feature source fence includes TenantSettings, every active Subscription,
and the Plan row(s) selected from those subscriptions before the final
`tasks.notificationsV1` evaluation. This prevents an enablement source change
from committing ahead of a stale visible digest. That final evaluation uses
no-tracking repository reads after the locks, so a preflight identity-map entry
cannot be reused as stale feature state.

Existing optional rows can be protected directly, but `FOR SHARE` cannot lock
an absent row. The matching writer paths therefore use stable parent pivots
before changing an optional child: Tenant for TenantSettings and Subscription,
Workspace for WorkspaceMember, Project for ProjectMember, Group for
GroupMember, and Task for Watch or Collaborator. The digest locks those same
parents shared; writers lock them `FOR UPDATE`. This parent-pivot contract
protects inserts as well as updates/deletes without advisory locks or schema
changes.

While the fence is held, generation rechecks current context and the exact
candidate predicate for the evaluated page. A later authorization/lifecycle
mutation waits for commit; an earlier change is detected and rolls the
transaction back before staging a visible result.

The generator recreates the full transaction, reacquires the claim fence, and
re-evaluates only for a detected current-state change, PostgreSQL
serialization/deadlock, or EF concurrency conflict, up to three attempts.
Infrastructure translates those provider failures to a safe application marker;
the internal retries neither add Notifications nor consume a new automatic
attempt. Claim loss stages nothing. Notification, Outbox signal, job
`Succeeded` transition, and optional `NotificationId` are saved and committed
together. A zero-candidate result commits only successful ledger completion;
it creates neither Notification nor Outbox row.

PR07-C also marks the existing `notification_user_states.Version` property as
an EF optimistic-concurrency token. This is model metadata over the existing
column and requires no new column or index in the focused migration. Every
Notification producer advances the same recipient-private sequence. If a
digest and immediate Task producer both load version 0 and try to commit
version 1, one update wins and the other transaction raises
`DbUpdateConcurrencyException` and rolls back its Notification/Outbox work. A
clean logical-key retry reuses the winner and commits the other intent as
version 2. PostgreSQL coverage verifies Notification and signal versions
`[1, 2]` with no lost state update or duplicate committed version.

For digest-to-digest races, each transaction acquires its own Job/Attempt
claim fence before the recipient User `FOR UPDATE` lock and holds all of them
through staging. The User lock serializes only that recipient's Notification
state, including two digest jobs for different Workspaces; it is not a
Tenant-wide lock. The EF token remains the cross-producer backstop for
immediate Task Notification work that does not share the digest's recipient
lock.

The migration Down path drops both digest tables. The earlier Notification,
preference, and Outbox schema remains, but all PR07-C ledger/attempt history is
lost. Operational rollback should normally leave this additive migration in
place; applying Down requires an explicit backup and acceptance of that loss.
The same-Tenant concurrency remediation changes lock SQL and mutation fences
only; it adds no migration and does not rewrite an existing migration.

### System and UI shell

- AuditLog, SecurityEvent, SystemSetting
- FeatureModule, PanelDefinition, UserLayout
- CommandDefinition, RadialMenuProfile, RadialMenuItem

## Tenant ownership

Tenant-owned records implement `ITenantEntity`.

`AppDbContext`:

- adds a required `TenantId`;
- adds a `TenantId` index;
- applies a global query filter;
- stamps new tenant-owned entities when `TenantId` is empty;
- rejects mismatched tenant writes;
- rejects normal tenant-owned writes when the current tenant is not active.

Platform scope bypasses filters. Any `IgnoreQueryFilters` usage must include an explicit tenant predicate or another reviewed platform boundary.

Known bypass locations include tenant repositories, tenant plans, integrations, and tenant export repositories. Re-audit them when changing tenancy behavior.

## Non-tenant tables

Some tables are intentionally platform/global, including:

- User
- Session
- Plan
- SystemSetting
- FeatureModule
- PanelDefinition
- CommandDefinition

Global tables can still reference tenant-owned tables. Review relationship and authorization implications before adding cross-scope queries.

## IDs, enums, timestamps, and deletion

- Primary IDs are GUIDs.
- Enums are generally stored as strings through Fluent API conversion.
- `AuditableEntity` provides created/updated timestamps.
- `SoftDeletableEntity` adds deletion timestamp, actor, and reason.
- Foreign-key delete behavior is mostly `Restrict` or `SetNull`.
- Application lifecycle operations often combine status changes with soft-delete metadata.

## Indexing

The model contains:

- unique tenant-scoped slugs;
- unique membership combinations;
- unique user email/normalized email;
- unique invite and API token hashes;
- tenant/status/time composite indexes;
- common lookup indexes for memberships, scopes, reads, dates, and lifecycle fields.

**Needs verification:** index effectiveness and query plans under realistic data volume. No performance benchmark suite was found.

## Seed data

Startup seed can create:

- a default tenant;
- `InternalPilot`, `SchoolPilot`, `Standard`, and `Enterprise` plans;
- optional UI-shell definitions.

It does not create users, memberships, or demo content.

## Search

Search queries relational tables directly with Npgsql `ILike` and membership predicates. There is no separate search index or full-text engine. Project-derived queries share the EF-translatable Project read scope. PostgreSQL Message Search constrains every matching Message by the shared readable-Conversation ID query, which composes Project scope with a recursive ancestry relation and 32-level fail-closed ceiling, before deterministic `CreatedAt DESC, Id ASC` ordering and the final 100-result bound. It does not materialize the caller's Conversation history or authorize an arbitrary pre-limit subset. Production Conversation pagination derives both items and `totalCount` from that same set-based recursive authorization relation; bounded record checks may restrict its anchor to requested IDs, while Search consumes the unrestricted queryable relation. Non-PostgreSQL providers retain the bounded fail-closed fallback.

PostgreSQL search tests exist but execute only when `POSTGRES_TEST_CONNECTION_STRING` is set.

## Exports, backup, and restore

Tenant export:

- creates an in-memory ZIP;
- includes selected metadata JSON;
- excludes password hashes, token hashes, secrets, and file bodies;
- records an `ExportJob`;
- has no import/restore path.

Operational recovery must back up both PostgreSQL and file storage. Tenant export is not a backup replacement.

## Migration workflow

```bash
dotnet tool restore
dotnet ef migrations add <Name> \
  --project src/Coglatas.Infrastructure \
  --startup-project src/Coglatas.Web
dotnet ef database update \
  --project src/Coglatas.Infrastructure \
  --startup-project src/Coglatas.Web
```

Before merging a migration:

1. Review generated SQL.
2. Verify every tenant-owned table implements `ITenantEntity`.
3. Verify tenant-scoped uniqueness/indexes.
4. Verify data backfills set `TenantId`.
5. Run migration and tenant-isolation tests against PostgreSQL.
6. Document destructive or operationally sensitive changes.

## Unknowns requiring environment evidence

- Largest tested dataset.
- Real migration duration and lock impact.
- Backup schedule and retention.
- Point-in-time recovery configuration.
- Successful database-plus-file restore drill.
- Production PostgreSQL version and extensions.
