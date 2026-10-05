# TASK-V1-PR07 notification, digest, Outbox, and realtime gap audit

Audit result: **NO-GO pending three canonical owner decisions.** No PR07 product behavior was implemented by this audit.

## Current implementation status — PR07-A draft branch

The NO-GO result and all baseline classifications below are retained historical
audit evidence for `491d17db3701b7fb26010db8c0590eac7d24bd78`; they are not
rewritten as if the audit had observed later work. The owner-decision gate was
subsequently resolved in Coglatas-Spec PR #62 at
`8b90c8897367606473515d17d3696e458b2ee7b5` and synchronized in
`docs/decisions/task-v1-pr07-owner-decisions.md` (status: Resolved).

Draft branch `task/v1-pr07-a-notification-foundation`, based on implementation
`main` `ca0f3fec26a78d4199fa834ce82509a6dfeda812`, now supplies the limited
PR07-A foundation: a nullable Notification logical key and filtered unique
index, race-safe create-or-get primitive, Workspace/member digest preference
state, the private current-user GET/PATCH contract, independent preference
concurrency, and a default-disabled `tasks.notificationsV1` registry key.

It does not change the audit's remaining PR07-B through PR07-E gaps: no Task
notification producer, deadline mutation/classification, digest ledger/worker,
new semantic Outbox event family, SignalR authorization/routing, notification
open endpoint, or Angular behavior is implemented. Focused current-branch
evidence is recorded separately in
`docs/verification/task-v1-pr07-a-foundation.md`; PostgreSQL evidence remains
conditional on `POSTGRES_TEST_CONNECTION_STRING` locally, while GitHub Actions
run `30711182611` has applied migrations and passed 505 backend tests. The
draft remains unmerged and is not PR07-B acceptance evidence by itself.

## Audit identity

| Item | Exact value |
| --- | --- |
| Implementation repository | `NYGsatoshi/Coglatas` |
| Fetched `origin/main` baseline | `491d17db3701b7fb26010db8c0590eac7d24bd78` |
| Specification repository | `NYGsatoshi/Coglatas-Spec` |
| Fetched specification `main` | `6e8e5c3651adeedc7a2709124e9af0fd927d35b5` |
| Audit branch | `audit/task-v1-pr07-gap-analysis` |
| Audit worktree | Separate clean worktree created from the fetched implementation baseline |
| Original checkout HEAD | `e6ae21c225796c06de3c81cd02195e5c77737d26` on `dotnetUpdate10.0.301` |
| Audit date | 2026-08-01 (Asia/Tokyo) |

The original checkout was 0 commits ahead and 26 commits behind `origin/main`. It contained a modified `qodana.yaml` and untracked `.coglatas-spec-source/`, `.idea/.idea.Coglatas/.idea/jsonSchemas.xml`, `.tools/`, and `scripts/ci/verify-dotnet-sdk.sh`. Those user-owned changes were not reset, deleted, staged, copied, or included. All inspection and documentation edits were made in the separate clean worktree.

### Baseline changes after PR #271

PR #271 merged as `b7c8b067b3e9e184d3f469aa7e1b9bc6995ead51`. The fetched baseline has two later commits:

| Commit | Subject | Files changed | PR07-sensitive effect |
| --- | --- | --- | --- |
| `8b84f0eceb6543e80919f9dd32e8d960208780d3` | Add ARM64 support and refactor test method bodies | `Coglatas.slnx`, `src/Coglatas.Application/Coglatas.Application.csproj`, `tests/Coglatas.Tests/Admin/AdminControllerTests.cs` | Solution/project configuration and an Admin test changed; no Task, Notification, Outbox, realtime, jobs, Workspace settings, or Angular notification/realtime code changed. |
| `491d17db3701b7fb26010db8c0590eac7d24bd78` | Merge branch main of the implementation repository | Merge only | No additional PR07-sensitive path beyond the preceding commit. |

No open pull request existed at audit time, so no open PR touched Task, Notification, Realtime, Outbox, jobs, or Workspace settings.

### Baseline CI evidence

All checks visible for exact implementation SHA `491d17db3701b7fb26010db8c0590eac7d24bd78` were complete:

- CI run `30700319724`: `frontend-test`, `build-test`, and `security-scan` succeeded.
- Code Quality run `30700319725`: Angular quality and Qodana Community/.NET succeeded; the additional Qodana Community for .NET result was neutral.
- npm Security Audit run `30700319729` succeeded.

This is observed existing evidence, not product verification performed by the documentation branch. PostgreSQL tests can report as passed while returning early when `POSTGRES_TEST_CONNECTION_STRING` is absent, and the audit did not infer that a named CI job exercised conditional PostgreSQL paths.

## Canonical sources and authority

The following files were read from specification SHA `6e8e5c3651adeedc7a2709124e9af0fd927d35b5`:

1. `01-core/15-workspace-task-messaging-owner-decision-resolution.md`
2. `01-core/12-task-work-management.md`
3. `01-core/11-task-work-planning-scope.md`
4. `01-core/realtime-event-catalog.md`
5. `01-core/14-workspace-task-messaging-realtime-addendum.md`
6. `01-core/signalr-group-authorization-matrix.md`
7. `01-core/outbox-delivery-contract.md`
8. `01-core/reconnect-catchup-sequence.md`
9. `01-core/22-audit-jobs-consistency.md`
10. `06-implementation-mapping/task-work-planning-api-realtime-contract.md`
11. `06-implementation-mapping/task-work-planning-owner-decision-api-addendum.md`
12. `03-acceptance/task-work-planning-owner-decision-acceptance.md`
13. `12-implementation-kickoff/task-v1-pr07-notifications-realtime-prompt.md`

All current specification Markdown was also searched for the required notification, digest, deadline, mention, Important, Blocked, Review, Outbox, SignalR, reconnect, preference, Workspace-timezone, open, deduplication, dead-letter, and replay terms. The authority order used here is owner decisions, core specification, API/realtime contract, acceptance criteria, PR07 prompt, historical documents, then code.

### Canonical conflicts and omissions

Three items remain unresolved and are recorded in `docs/decisions/task-v1-pr07-owner-decisions.md`:

1. The immediate categories are canonical, but the exact mandatory recipient sets for Blocked, returned/rejected Review, major hard-deadline change, and Important TaskComment are not defined. "Applicable Task users" and "affected users" are insufficient to implement a privacy-sensitive recipient policy.
2. A bounded digest-time granularity/range is required, but no current canonical file defines the allowed values.
3. `outbox-delivery-contract.md` requires ten automatic attempts and durable dead-letter/replay. `22-audit-jobs-consistency.md` limits MVP retry to three, makes JobLease and a dedicated DeadLetter mechanism Full-only, and does not explicitly name the Task deadline digest in its MVP job list. The PR07 prompt directs the digest to follow the existing job/Outbox claim, lease, retry, and dead-letter contracts, so the boundary must be clarified.

The event-name lists are not treated as an owner blocker. Higher-authority core/addendum event families take precedence over illustrative PR07 names when they have the same semantics. For example, stage changes should use `Projects.TaskWorkflowChanged.v1`, not introduce a duplicate `Projects.TaskStageChanged.v1` family.

## Current architecture

| Area | Current implementation | PR07 conclusion |
| --- | --- | --- |
| Notification persistence | `Notification` and `NotificationUserState` in `CommunicationEntities.cs`; `DbNotificationService` uses EF Core and the shared `AppDbContext`. | Reuse, but add a stable logical dedupe identity and target authorization. |
| Task commands | Canonical command paths are in `TaskCommandService` and `TaskSubresourceService`; compatibility assignment/dependency paths remain in `ProjectService`. | Integrate policy at the canonical mutation boundary and normalize compatibility routes; do not create a second Task service. |
| Transactional Outbox | `TransactionalOutbox`, `OutboxEventRepository`, Outbox EF configuration, `OutboxDispatcher`, replay service, and realtime health counters exist. | Reuse the envelope, transaction, claim, delivery, and metrics infrastructure; do not create a second realtime queue. |
| Realtime | One authenticated `AppHub`, server-authorized logical subscriptions, an in-memory registry, current-dispatch authorization, and one Angular `RealtimeFacade`. | Extend the catalog/authorization and retain one socket client. Polling remains fallback, not proof of SignalR routing. |
| Background jobs | The only `BackgroundService` found is `OutboxDispatcher`; no Hangfire, Quartz, generic `BackgroundJobRecord` worker, or digest scheduler is registered. | Add a bounded digest worker/ledger in a later PR; Outbox claim code is a pattern, not an existing general job system. |
| Workspace time | `Workspace.TimeZone` plus `TaskWorkspaceTimeZoneResolver` with Tenant fallback and UTC fallback. | Reuse for classification and digest scheduling. |
| Angular state | RightPanel owns notification HTTP state; Project/My Tasks/Task facades own their HTTP projections; `RealtimeFacade` owns transport/dedupe/version gates. | Extend existing owners and shared client; no feature-specific sockets. |

Active architecture/roadmap documents still describe background jobs and SignalR as absent or deferred. Current source, tests, and deployment composition take precedence; those active documents should be corrected in an implementation PR that changes the supported operational contract.

## Notification persistence and APIs

### Persisted fields

`Notification` currently stores `TenantId`, `UserId`, `NotificationType`, `Title`, `Body`, `RelatedEntityType`, `RelatedEntityId`, `IsRead`, `ReadAt`, `CreatedAt`, `DeletedAt`, and `StateVersion`. `NotificationUserState` stores a tenant/user monotonic `Version` and `UpdatedAt`.

There is no event ID, logical notification key, Workspace ID, Project ID, or database uniqueness constraint that represents a logical event/recipient pair.

### Routes and DTOs

| Route | Request | Response/current behavior |
| --- | --- | --- |
| `GET /api/notifications` | `NotificationListQuery` with bounded page/pageSize | `PagedResponse<NotificationListItemResponse>` |
| `GET /api/notifications/unread-count` | none | `NotificationUnreadCountResponse` |
| `PATCH /api/notifications/{notificationId}/read` | none | Marks only a notification belonging to the current user; emits read-state Outbox event. |
| `PATCH /api/notifications/read-all` | none | Marks the current user's visible notifications read. |
| `DELETE /api/notifications/{notificationId}` | none | Soft-deletes only the current user's notification. |
| `GET /api/communication/poll/notifications` | `CommunicationPollingQuery` | Reauthorizes supported Message/Conversation/Project targets and redacts/omits denied targets; Task targets are not supported and therefore become unavailable. The Angular RightPanel does not use this route. |

`NotificationListItemResponse` returns notification fields plus a computed `TargetRoute` and `StateVersion`. `DbNotificationService.BuildTargetRoute` maps a Task target to `/tasks/{id}`.

### Dedupe and isolation findings

`DbNotificationService.CreateAsync` performs an application query using recipient, type, related type/id, and trimmed title. It has no logical event key and no unique database constraint. Concurrent writers can create duplicates; a title change defeats the check; and a later distinct event with the same title and target can be suppressed indefinitely. Soft deletion changes the result again. This is not PR07 logical deduplication.

The list/read/delete queries are tenant-filtered by `AppDbContext` and explicitly user-filtered. `NotificationApplicationServiceTests.UserCannotReadAnotherUsersNotification` and `UnreadCountExcludesReadNotifications` prove the application boundary with a fake service. They do not prove database races, logical dedupe, target authorization, or cross-Workspace recipient policy.

`DbNotificationService.EnqueueCreatedAsync` currently serializes the stored title, body, target type/id, target route, and state version into `Notifications.NotificationCreated.v1`. It uses a user route, but delayed user-route delivery does not reauthorize the related Task. Historical payloads can therefore reveal data after access is revoked.

## Task mutation-source inventory

All canonical Task command saves go through `IUnitOfWork.SaveTaskCommandAsync`. `TaskCommandService.CommitAsync` advances aggregate version, writes an AuditLog, queues `Projects.TaskChanged.v1` through `IBusinessInvalidationPublisher`, and then saves once. An Outbox row queued before that save is transactional because it is tracked by the same `AppDbContext`; a method name alone was not counted as proof. Conditional PostgreSQL tests prove this boundary for representative mutations and rollback cases.

| Mutation source | Service/symbol and persisted state | Audit behavior | Current Notification / Outbox behavior | Rollback/test evidence | PR07 gap |
| --- | --- | --- | --- | --- | --- |
| Primary Assignee set/clear | `TaskCommandService.SetAssigneeAsync`; updates `TaskItem.PrimaryAssigneeUserId` and reconciles `WorkItemWatchState`. | `TaskAssigneeChanged`; no protected content. | Generic `Projects.TaskChanged.v1`; no visible notification. Affected users are computed after mutation, so the removed assignee is not included in the modern user invalidation. | `PrimaryAssigneeClearWithoutReviewerRemovesAutomaticSourceAndCommitsOneEvent`; `Prompt2CRepresentativeTaskMutationsKeepOutboxEnvelopeVersionsAligned`. | Add prior/new relationship capture, mandatory recipient intent, logical key, and safe semantic event. |
| Compatibility assignment add/update/delete | `ProjectService.AddAssignmentAsync`, `UpdateAssignmentAsync`, `DeleteAssignmentAsync`; persists `TaskAssignment` and Task version. | Task assignment action. | Calls `INotificationService` and queues Task invalidation before the same Task save. This is a legacy assignment role notification, not the canonical Primary Assignee policy. | `AssignmentRaceWithRealNotificationsCommitsWinnerSideEffectsOnly` proves the winning assignment, Notification, Notification state, and Notification Outbox row commit together on PostgreSQL. | Normalize or retire duplicate behavior so compatibility calls cannot create a second/wrong notification. |
| Reviewer set/clear | `TaskCommandService.SetReviewerAsync`; updates `ReviewerUserId` and Watch source. | `TaskReviewerChanged`. | Generic Task invalidation only. | Representative Outbox sequence test. | Reviewer-assigned notification and semantic event missing. |
| TaskComment create/update/delete | `TaskSubresourceService.CreateCommentAsync`, `UpdateCommentAsync`, `DeleteCommentAsync`; persists `TaskComment`, including `IsImportant`. | Action plus safe metadata such as Important flag; comment text is not put in AuditLog. | Validates direct mention IDs; emits generic Task invalidation only. No notification intent. | `Comment_UpdateDeleteAndLegacyAdapterRemainAtomicAndPrivate`, update/delete race tests. | Mention and Important recipient generation/dedupe missing. |
| Generic legacy comment service | `ProjectService.AddCommentAsync` can still create generic `Comment`. The public Task compatibility controller routes Task comments to `TaskSubresourceService`, bypassing this path. | Legacy audit. | `NotifyCommentAsync` addresses every Task assignment except author and supplies a comment-derived body; its related ID is the Project ID while declaring a Task source. | No test proves this code is unreachable from every non-controller caller. | Contract-dangerous notify-all/body behavior must be removed or guarded for Task targets. |
| Blocked state | `TaskCommandService.SetBlockedStateAsync`; changes `IsBlocked`/reason. | `TaskBlockedStateChanged`; reason presence rather than broad payload. | Generic Task invalidation only. | Task command unit coverage exists for shared commit mechanics, not recipient policy. | Mandatory notification absent; exact recipients need owner decision. |
| Review submit | `TaskCommandService.SubmitReviewAsync`; changes review/stage state. | `TaskReviewSubmitted`. | Generic Task invalidation only. | Review command tests cover workflow rules, not notification. | Current Reviewer notification missing. |
| Review accept/return | `AcceptReviewAsync` and `ReturnReviewAsync`; persist review outcome and stage changes. | Review outcome action; return reason remains on the entity and is not sent in the generic event. | Generic Task invalidation only. | Workflow unit tests; no notification evidence. | Returned/rejected notification missing; recipient set unresolved. |
| Manager override | `OverrideCompleteAsync`; persists completed state and an override record/reason. | `TaskReviewOverridden` records safe metadata. | Generic Task invalidation only; no specific event. | `ReviewOverrideUsesCanonicalDoneTransitionAndReturnsDoneStage` and rejection tests. | Add semantic invalidation without the override reason; no PR07 immediate category unless owner policy later adds one. |
| Hard deadline | `TaskItem.DeadlineAt` exists and is read by Task/My Tasks projections. No production request DTO assigns it. | None for mutation because no mutation exists. | None. | Only seed/read tests use the property. | Add a versioned server-authoritative mutation and classifier; keep Gantt planned dates separate. |
| Watch/explicit opt-out | `WatchAsync`, `UnwatchAsync`, and relationship reconciliation persist `WorkItemWatchState`. | `TaskWatchEnabled`, `TaskWatchOptOut`, and relationship actions. | Generic Task invalidation; private Watch values are not in the payload. | `WatchOptOutSurvivesAutomaticSourceReconciliationUntilManualRewatch`, `WatchSourcesNoOpsAndPrivacyAreActorSpecific`, race tests. | Reuse the effective state; do not let opt-out suppress mandatory notifications or broadcast private state. |
| Claim | `TaskCommandService.ClaimAsync`; atomically assigns the actor when currently eligible. | `TaskClaimed`. | Generic Task invalidation only. | Command logic exists; no PR07 semantic event/notification test. | Emit canonical semantic invalidation; assignment notification follows canonical assignment rule. |
| Dependency add/delete | `ProjectService.AddDependencyAsync`/`DeleteDependencyAsync`; persists `TaskDependency` and Task graph versions. | Dependency action. | Generic Task/Project invalidation. | PR05/PR06 PostgreSQL hosted tests cover transaction and graph versions. | Add or map `Projects.TaskDependencyChanged.v1`; no visible notification required. |
| Checklist create/update/delete/reorder | `TaskSubresourceService` checklist methods; persists `TaskChecklistItem` and Task version. | Checklist actions. | Generic Task invalidation. | Unit and PostgreSQL atomicity/race tests. | Map semantic invalidation without checklist text. |
| Label definition/association | `TaskSubresourceService` label methods; persists `ProjectTaskLabel`/`WorkItemLabel`. | Label actions. | Generic Task/Project invalidation. | Extensive PostgreSQL uniqueness/race/isolation tests. | Map label event without private or display-heavy payload. |
| Workflow/stage/cancel/reopen/delete/restore | `TaskCommandService` transition and lifecycle methods. | Named Task actions. | Generic Task invalidation; parent invalidation when derived state changes. | Unit and PostgreSQL parent/child transaction tests. | Add only distinct semantic events needed by canonical consumers; retain generic invalidation. |
| Membership revocation / Workspace archive | Workspace membership methods use `AuthorizationStateChangePublisher` and `RealtimeConnectionInvalidator`. `WorkspaceService.ArchiveAsync` and `AdminService.ArchiveWorkspaceAsync` archive without publishing an authorization-state event to affected members. | Authorization/membership/archive audit. | Member removal/status changes queue durable authorization state and invalidate connections; archive relies on later route/HTTP reauthorization. | Frontend mocked reconnect/clearing tests; no real SignalR membership-revocation/archive acceptance test. | Dispatch must recheck notification Task targets, and archive must cause prompt protected-state clearing rather than leave an already rendered view until a later request. |

## Requirement-to-implementation matrix

Statuses are mutually exclusive. "Implemented but unverified" means the relevant code exists but no test in the repository proves the full stated boundary.

Evidence keys used in each row resolve to these exact files and symbols:

- **N-DOM** — `src/Coglatas.Domain/Entities/CommunicationEntities.cs`: `Notification`, `NotificationUserState`; `src/Coglatas.Domain/Enums/CoreEnums.cs`: `NotificationType`.
- **N-APP** — `src/Coglatas.Application/Notifications/NotificationApplicationService.cs`: `NotificationApplicationService`; `src/Coglatas.Application/Communication/CommunicationPollingService.cs`: `CommunicationPollingService`; `src/Coglatas.Web/Controllers/NotificationsController.cs`: `NotificationsController`.
- **N-INF** — `src/Coglatas.Infrastructure/Persistence/DbNotificationService.cs`: `DbNotificationService`; `src/Coglatas.Infrastructure/Persistence/Configurations/CommunicationConfigurations.cs`: notification configurations.
- **T-DOM** — `src/Coglatas.Domain/Entities/ProductionEntities.cs`: `TaskItem`, `WorkItemWatchState`; `src/Coglatas.Infrastructure/Persistence/Configurations/ProductionConfigurations.cs`: `TaskItemConfiguration`, `WorkItemWatchStateConfiguration`.
- **T-CMD** — `src/Coglatas.Application/Projects/TaskCommandService.cs`: `TaskCommandService`; `src/Coglatas.Application/Projects/TaskCommandDtos.cs`: Task command request/response records; `src/Coglatas.Web/Controllers/ProjectsController.cs`: Task command actions.
- **T-SUB** — `src/Coglatas.Application/Projects/TaskSubresourceService.cs`: `TaskSubresourceService`; `src/Coglatas.Web/Controllers/ProjectsController.cs`: TaskComment/checklist/label routes.
- **T-COMPAT** — `src/Coglatas.Application/Projects/ProjectService.cs`: `ProjectService`; `src/Coglatas.Web/Controllers/ProjectsController.cs`: compatibility assignment/comment/dependency routes.
- **T-TZ** — `src/Coglatas.Application/Projects/TaskDerivedValues.cs`: `TaskWorkspaceTimeZoneResolver`.
- **W** — `src/Coglatas.Domain/Entities/WorkspaceEntities.cs`: `Workspace`, `WorkspaceMember`; `src/Coglatas.Application/Workspaces/WorkspaceService.cs`: `WorkspaceService`; `src/Coglatas.Application/Admin/AdminService.cs`: `AdminService`.
- **O-APP** — `src/Coglatas.Application/Realtime/RealtimeContracts.cs`: catalog/envelope/contracts; `src/Coglatas.Application/Realtime/TransactionalOutbox.cs`: `TransactionalOutbox`; `src/Coglatas.Application/Realtime/BusinessInvalidationPublisher.cs`: `BusinessInvalidationPublisher`; `src/Coglatas.Application/Realtime/OutboxReplayService.cs`: `OutboxReplayService`.
- **O-INF** — `src/Coglatas.Infrastructure/Persistence/OutboxEventRepository.cs`: `OutboxEventRepository`; `src/Coglatas.Infrastructure/Persistence/Configurations/OutboxConfigurations.cs`: `OutboxEventConfiguration`.
- **RT-WEB** — `src/Coglatas.Web/Realtime/AppHub.cs`: `AppHub`; `src/Coglatas.Web/Realtime/HubSubscriptionAuthorizer.cs`: `HubSubscriptionAuthorizer`; `src/Coglatas.Web/Realtime/HubSubscriptionRegistry.cs`: `HubSubscriptionRegistry`; `src/Coglatas.Web/Realtime/RealtimeDispatchAuthorizer.cs`: `RealtimeDispatchAuthorizer`; `src/Coglatas.Web/Realtime/OutboxDispatcher.cs`: `OutboxDispatcher`; `src/Coglatas.Web/Program.cs`: `/health/realtime`.
- **RT-FE** — `frontend/src/app/core/realtime/realtime.facade.ts`: `RealtimeFacade`; `frontend/src/app/core/realtime/realtime.models.ts`: durable event models.
- **FE-N** — `frontend/src/app/shared/right-panel/right-panel.facade.ts`: `RightPanelFacade`; `frontend/src/app/shared/right-panel/notification-item/notification-item.component.ts`: `NotificationItemComponent`; `frontend/src/app/shared/right-panel/notifications-tab/notifications-tab.component.ts`: `NotificationsTabComponent`.
- **FE-T** — `frontend/src/app/features/projects/projects.facade.ts`: `ProjectsFacade`; `frontend/src/app/features/projects/my-tasks.facade.ts`: `MyTasksFacade`; `frontend/src/app/features/projects/project-detail.facade.ts`: `ProjectDetailFacade`.
- **TEST** — exact test files and names are listed in the test-evidence section below.

| ID | Canonical requirement | Status | Exact implementation evidence | Existing route/event/table and test evidence | Confirmed gap / security impact | Dependency | Phase |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R01 | Persist/list/read/delete in-app notifications. | Implemented and contract-complete | **N-DOM, N-APP, N-INF:** `Notification`, `NotificationUserState`, `NotificationApplicationService`, `DbNotificationService`, `NotificationsController`. | `notifications`, `notification_user_states`; five `/api/notifications` routes; two application tests. | None for basic recipient-owned lifecycle. | Existing infrastructure. | Reuse |
| R02 | Tenant and recipient isolation for notification lifecycle. | Implemented and contract-complete | **N-INF:** `AppDbContext` tenant filters plus user predicates in `DbNotificationService`. | Direct notification routes; `UserCannotReadAnotherUsersNotification`. | Does not prove target authorization or recipient generation. | R01. | Reuse |
| R03 | One visible notification per logical event/recipient. | Partially implemented | **N-INF:** `DbNotificationService.CreateAsync` heuristic query. | No logical field or unique constraint; no race test. | Retry/concurrency can duplicate, while unrelated events can be suppressed. | New logical key. | PR07-A |
| R04 | Authorize a notification target when opened. | Missing | **N-APP, FE-N:** no open application service/endpoint; `RightPanelFacade.displayNotificationTarget` uses the stored route. | RightPanel uses stored `targetRoute` and marks read separately. | Revoked/deleted targets are not checked before navigation; stored display data may already be visible. | Target resolver/API. | PR07-D |
| R05 | Primary Assignee assigned/removed immediate intent. | Missing | **T-CMD:** `TaskCommandService.SetAssigneeAsync` has no notification dependency. | Legacy assignment notification is a different compatibility contract. | New/previous assignee policy absent; removed user also drops from modern affected-user set. | Owner policy, R03. | PR07-B |
| R06 | Reviewer assigned immediate intent. | Missing | **T-CMD:** `TaskCommandService.SetReviewerAsync` emits generic invalidation only. | No notification/test. | Required recipient is not notified. | R03. | PR07-B |
| R07 | Valid direct-mention notification with non-disclosure. | Partially implemented | **T-SUB:** `TaskSubresourceService.ValidateMentionTargetsAsync` parses GUID mentions and validates current eligible Project users. | Mention candidates and comment routes; comment atomic/privacy tests. | No notification intent/dedupe; whole-request validation exists but notification non-disclosure is untested. | R03, recipient service. | PR07-B |
| R08 | Becomes Blocked immediate intent. | Missing | **T-CMD:** `TaskCommandService.SetBlockedStateAsync` persists/audits and emits generic invalidation only. | `Projects.TaskChanged.v1`. | No visible notification; recipient set unresolved. | PR07-OWNER-001. | PR07-B |
| R09 | Submitted for Review immediate intent. | Partially implemented | **T-CMD:** `TaskCommandService.SubmitReviewAsync` has the mutation, current reviewer relation, audit, transaction, and invalidation. | Review routes; command tests. | Visible notification and logical key absent. | R03. | PR07-B |
| R10 | Returned/rejected Review immediate intent. | Partially implemented | **T-CMD:** `TaskCommandService.ReturnReviewAsync` has the return mutation/audit/invalidation. | `/review/return`; no notification test. | Visible notification absent; exact recipient set unresolved. | PR07-OWNER-001, R03. | PR07-B |
| R11 | Major hard-deadline immediate intent. | Missing | **T-CMD:** Task request DTOs and `TaskCommandService` have no production DeadlineAt mutation/classifier. | None. | Entire event source absent. | R18-R20, PR07-OWNER-001. | PR07-B |
| R12 | Important TaskComment immediate intent. | Missing | **T-SUB:** `TaskSubresourceService.CreateCommentAsync`/`UpdateCommentAsync` persist `IsImportant`; no recipient generation. | Comment DTO/routes and PostgreSQL tests. | "Applicable Task users" unresolved; no notification/dedupe. | PR07-OWNER-001, R03. | PR07-B |
| R13 | Ordinary comments do not notify all participants. | Implemented with contract conflict | **T-SUB, T-COMPAT:** public Task adapter uses `TaskSubresourceService` and sends no notification; retained `ProjectService.NotifyCommentAsync` still performs assignment-wide body-bearing notification if invoked. | Public generic `/api/comments` Task branch bypasses legacy service; no reachability guard test. | A retained application path contradicts the contract and could leak comment-derived data if reused. | Compatibility cleanup. | PR07-B |
| R14 | Watch opt-out affects only Watch-derived activity. | Partially implemented | **T-CMD, T-DOM:** `WorkItemWatchState` and Task command reconciliation persist automatic sources/manual/opt-out. | Watch routes and PostgreSQL privacy/race tests. | No notification policy consumes effective Watch state or separates mandatory recipients. | Recipient policy. | PR07-B |
| R15 | Suppress redundant actor self-notification. | Partially implemented | **N-INF:** `DbNotificationService.CreateManyAsync` skips `actorUserId`. | Not all creation paths use `CreateManyAsync`; no category tests. | Inconsistent actor behavior. | Central recipient service. | PR07-B |
| R16 | Reauthorize at intent creation, delayed dispatch, and open. | Implemented with contract conflict | **T-CMD, RT-WEB, N-APP:** mutations authorize the actor; `RealtimeDispatchAuthorizer` authorizes the route; `CommunicationPollingService.AuthorizeNotificationTargetAsync` handles limited types. | User-route dispatch checks user/session only; Task open absent. | Historical membership can deliver a stored Task notification payload after revocation. | Target-aware authorizer/open API. | PR07-D |
| R17 | Persist the distinct hard deadline and query it efficiently. | Implemented and contract-complete | **T-DOM:** `TaskItem.DeadlineAt`; `TaskItemConfiguration` indexes Project/Deadline and tenant/workspace urgency. | Task/My Tasks DTO projections and read tests. | No mutation/classifier. | Existing Task model. | Reuse |
| R18 | Versioned production mutation for `DeadlineAt`. | Missing | **T-CMD:** `TaskUpdateDetailsRequest` and schedule request omit it; `ProjectsController.UpdateTask` has no other deadline input. | No route writes the field. | Client cannot set a canonical hard deadline. | API DTO/use case. | PR07-B |
| R19 | Server classifies Added/Removed/ShiftAtLeast24Hours/CrossedUrgencyBoundary/None. | Missing | **T-CMD, T-TZ:** no classifier or call site exists in Task commands/derived values. | None. | No authoritative classification; no notification source. | R18, R20. | PR07-B |
| R20 | Use Workspace timezone with Tenant/UTC fallback. | Implemented and contract-complete | **T-TZ:** `TaskWorkspaceTimeZoneResolver`. | Five resolver tests cover precedence, invalid/cross-tenant inputs, and cancellation. | DST use in digest/classifier remains untested. | Existing resolver. | Reuse |
| R21 | Deadline boundary and retry tests. | Missing | **TEST:** `TaskWorkspaceTimeZoneResolverTests` exists, but no classifier test or production classifier exists. | No null, 23h59m, 24h, local-midnight, Today/Overdue, planned-only, or retry tests. | Regression/privacy risk. | R18-R20. | PR07-B |
| R22 | Required preference GET/PATCH routes. | Missing | **W, N-APP:** no matching controller/service in Workspace/current-user/notification owners. | No equivalent route. | User cannot set/inherit Workspace preference. | Preference model. | PR07-A |
| R23 | Private per-user/per-Workspace nullable preference with version/ETag. | Missing | **W:** `WorkspaceMember` has the natural unique ownership key but no preference/version fields. | No table/fields. | No durable preference or concurrency control. | Migration. | PR07-A |
| R24 | Workspace default `08:00` local setting. | Missing | **W:** `Workspace` has `TimeZone`, no digest default; Workspace service DTOs expose no equivalent. | No Workspace settings API/UI field. | Inheritance cannot be resolved. | Migration/settings API. | PR07-A |
| R25 | Documented bounded digest-time granularity/range. | Owner decision required | **W:** no code setting; specification search found no canonical numeric rule. | Requirement says bounded only. | API/UI/scheduler could disagree or round silently. | PR07-OWNER-002. | Gate |
| R26 | Digest groups: 3 days, 1 day, today, overdue. | Missing | **T-CMD, W:** no digest builder/query in Task or Workspace application owners. | None. | Daily product behavior absent. | R22-R25. | PR07-C |
| R27 | Per-user/per-Workspace, DST-safe scheduler. | Missing | **RT-WEB:** `Program.cs` registers no job runner beyond `OutboxDispatcher`. | No digest worker. | Multi-Workspace and DST delivery absent. | Job contract decision. | PR07-C |
| R28 | Digest idempotency by user/workspace/local date/policy version. | Missing | **W, N-DOM:** no digest ledger/key in current persistence owners. | None. | Retry could create duplicate visible digests. | New ledger and R03. | PR07-C |
| R29 | Bounded paging, failure isolation, retry/dead-letter, observability. | Missing | **RT-WEB, O-INF:** no generic job model/worker; only Outbox delivery has claim/retry/diagnostics. | `OPERATIONS.md` says background-job health is incomplete. | One failure could poison an ad hoc scan; no operational state. | PR07-OWNER-003. | PR07-C/E |
| R30 | Durable Outbox envelope and persistence fields. | Implemented and contract-complete | **O-APP, O-INF:** `OutboxEvent`, `DurableEventEnvelope`, `TransactionalOutbox`, `OutboxEventConfiguration`. | `outbox_events`; schema/aggregate/routing/attempt/lock/dead-letter fields. | Payload validator does not understand all PR07-sensitive field semantics. | Existing Outbox. | Reuse |
| R31 | Business mutation and Outbox row share one transaction. | Implemented and contract-complete | **T-CMD, O-APP, O-INF:** publishers add to the same scoped `AppDbContext`; `TaskCommandService.CommitAsync` saves once. | Multiple conditional PostgreSQL atomicity/rollback tests, including real notification winner. | Only existing generic events are proven; absent PR07 producers are not. | Existing UoW. | Reuse/test in B |
| R32 | Complete PR07 semantic event catalog. | Partially implemented | **O-APP:** `RealtimeEventCatalog` has 11 events, only generic `Projects.TaskChanged.v1` for Tasks. | All canonical Task commands use generic invalidation. | Specific Watch/claim/comment/review/label/deadline/digest/preference families absent. | Catalog mapping. | PR07-B/C/D |
| R33 | Claim/lease/retry/dead-letter follows canonical delivery policy. | Implemented with contract conflict | **O-INF, RT-WEB:** `OutboxEventRepository.ClaimDueAsync`, `OutboxDispatcher` backoff, ten-attempt dead letter. | Two repository tests cover identity and stale lock recovery. | Dedicated Outbox matches its contract, but generic MVP job rules conflict; digest has no implementation. | PR07-OWNER-003. | Gate/C/E |
| R34 | Replay is authorized against current access. | Implemented with contract conflict | **O-APP, O-INF, RT-WEB:** `OutboxReplayService` requires PlatformAdmin/current Tenant/reason; repository resets the same row; dispatcher republishes stored envelope. | No public replay endpoint or replay authorization test. | User-route Task/notification target access is not rechecked and payload is not redacted on replay. | Target-aware dispatch authorization. | PR07-D/E |
| R35 | Server-derived user/project/workspace logical subscriptions. | Implemented but unverified | **RT-WEB:** `AppHub`, `HubSubscriptionAuthorizer`, `HubSubscriptionRegistry`, `RealtimeDispatchAuthorizer`. | Frontend transport tests are mocked; no backend Hub/dispatcher integration test. | Source implements current auth, but the real connection boundary is unproved. | Existing Hub. | PR07-D/E |
| R36 | Authorized `group:{groupId}` route when used. | Missing | **O-APP, RT-WEB:** `RealtimeSubscriptionType` has User/Tenant/Workspace/Conversation/Project only; `AppHub` has no Group method. | No Hub method/group naming/authorization. | A future group route must not be guessed by the client. PR07 can avoid it by resolving recipients to users, but the canonical matrix route itself is absent. | Group authorization or explicit non-use. | PR07-D |
| R37 | Revocation stops subsequent protected delivery. | Partially implemented | **W, O-APP, RT-WEB:** membership changes publish authorization state and invalidate connections; dispatcher rechecks route access. Workspace archive methods do not notify affected users. | Frontend clear/refetch tests; no real membership revocation/archive SignalR test. | User-route Task targets are not reauthorized, and archive can leave already-rendered protected state until later HTTP/reconnect. | R16. | PR07-D/E |
| R38 | Broad routes contain minimal metadata and no private state. | Partially implemented | **O-APP, N-INF:** `BusinessInvalidationPublisher.TaskChangedAsync` sends IDs, version, change, changed fields, and refetch hint; `DbNotificationService.EnqueueCreatedAsync` sends stored display fields on a user route. | Generic Task event is invalidation-only. | NotificationCreated stores/sends full title/body on user route; Outbox property-name validation does not ban comment/review/title fields by semantic context. | Event-specific payload validators. | PR07-B/D |
| R39 | Reconnect reauthorizes, catches up by HTTP, then becomes connected. | Implemented and contract-complete | **RT-FE:** `RealtimeFacade` re-subscribes logical targets and runs catch-up callbacks before Connected. | Unit tests cover denied owners, catch-up order, clearing, and degraded mode. | Backend real transport evidence remains for PR07-E. | Existing shared client. | Reuse |
| R40 | Shared client dedupes event IDs and rejects stale versions. | Implemented and contract-complete | **RT-FE:** `RealtimeFacade` bounded 256-event ID set, aggregate versions, feature stale guards. | Unit tests cover duplicate/unknown schema and stale versions. | Notification logical dedupe is separate and absent. | Existing client. | Reuse |
| R41 | Bounded coalescing and active-edit conflict behavior on all affected surfaces. | Partially implemented | **FE-T:** `ProjectsFacade`, `MyTasksFacade`, and `ProjectDetailFacade` coalesce refreshes; Task, Kanban, and Gantt preserve/queue active edits. | Extensive Angular facade tests. | RightPanel has no logical notification dedupe test; all PR07 semantic event types are unknown. | Catalog/UI integration. | PR07-D |
| R42 | RightPanel immediate/digest display produces one visible state change. | Partially implemented | **FE-N, RT-FE:** `RightPanelFacade` owns list/unread state and consumes current notification events through `RealtimeFacade`. | Component tests cover safe routing/text and mark-read failure, not realtime notification events. | No digest model, logical-key dedupe, revoked/deleted UX, or HTTP-plus-event proof. | R03/R04. | PR07-D |
| R43 | Workspace-specific preference UI. | Missing | **FE-N, FE-T:** no Angular preference model/client/control in current notification/Workspace-facing state owners. | None. | User cannot view inherited/effective time or update it. | R22-R25. | PR07-D |
| R44 | Authorization clearing, degraded mode, and manual HTTP refresh remain operational. | Implemented and contract-complete | **RT-FE, FE-N, FE-T:** feature facades clear protected projections; My Tasks/Kanban/Gantt expose retry/degraded state. | Angular tests cover denial, revoked in-flight responses, reconnect, and degraded HTTP use. | Must extend tests for new notification/preference state. | Existing facades. | Reuse/PR07-D |
| R45 | Backend evidence for PR07 policy and isolation. | Partially implemented | **TEST:** Task command/concurrency, Watch, timezone, Outbox repository, and basic notification test files listed below exist. | Exact test names inventoried below. | No immediate-category, logical-dedupe, open, digest, or dispatch-auth tests. | All implementation phases. | PR07-A-E |
| R46 | Real Backend two-user and SignalR acceptance. | Missing | **TEST, RT-WEB:** no PR07 browser/backend suite exercises `AppHub`/`OutboxDispatcher`. | Existing Real Backend infrastructure is not PR07 evidence. | End-to-end authorization/retry/reconnect behavior is unproved. | PR07-D. | PR07-E |
| R47 | Digest job, DST, and observability tests. | Missing | **RT-WEB, TEST:** no digest job code or corresponding test file. | None. | Operational safety is unproved. | PR07-C. | PR07-C/E |
| R48 | Exact mandatory recipient policy. | Owner decision required | **T-CMD, T-SUB:** mutation sources/relationships exist; canonical specification, not code, omits the full recipient matrix. | No code can decide the product set. | Over-notification leaks; under-notification misses mandatory action. | PR07-OWNER-001. | Gate |
| R49 | Definitive job/Outbox delivery profile. | Owner decision required | **O-INF, RT-WEB:** current Outbox code uses the dedicated ten-attempt profile; canonical specification leaves the digest/generic-job boundary incompatible/ambiguous. | Current Outbox uses ten attempts; no digest job. | Implementers cannot choose one contract silently. | PR07-OWNER-003. | Gate |

### Status count

| Status | Count |
| --- | ---: |
| Implemented and contract-complete | 9 |
| Implemented but unverified | 1 |
| Partially implemented | 12 |
| Implemented with contract conflict | 4 |
| Missing | 20 |
| Owner decision required | 3 |
| **Total** | **49** |

## Immediate notification policy matrix

Every row below assumes current Tenant, Workspace, Project, and Task authorization at intent creation; current membership/target authorization again before delayed delivery; and notification-target authorization when opened. None of the current Task producers implements all three checks.

| Event | Actor | Mandatory recipient source | Optional Watch-derived source | Self behavior | Authorization checkpoints | Logical key candidate | Safe visible metadata | Forbidden payload | Current status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Primary Assignee assigned | Authorized Task editor or claimant | New Primary Assignee | Other effective watchers, only if general activity is enabled | Skip when actor becomes assignee | Validate relationship and current target at intent/dispatch/open | `task:{taskId}:v{taskVersion}:assignee-assigned:{recipientId}` | Generic assignment category, IDs/version, occurrence time | Restricted Task title, Watch state, request body | Missing on canonical route; legacy assignment route is not equivalent. |
| Primary Assignee removed | Authorized Task editor | Previous Primary Assignee captured before mutation | Remaining effective watchers if enabled | Skip actor | Prior recipient must be authorized at intent; dispatch/open may safely suppress after access loss | `task:{taskId}:v{taskVersion}:assignee-removed:{recipientId}` | Generic removal category, IDs/version | New relationship private fields or title | Missing; previous assignee is also absent from the modern post-mutation affected-user set. |
| Reviewer assigned | Authorized Task editor | New Reviewer | Other effective watchers if enabled | Skip actor | Current review/Task access at all three points | `task:{taskId}:v{taskVersion}:reviewer-assigned:{recipientId}` | Generic review assignment category | Review reason, restricted title | Missing. |
| Valid direct mention | Comment author | Each server-validated directly mentioned user | None required for the mention category | Skip author if self-mentioned | Mention eligibility at intent; Task access at dispatch/open | `task-comment:{commentId}:v{commentVersion}:mention:{recipientId}` | Generic mention category and IDs; fetch content after authorized open | TaskComment body, extracted text, restricted title | Mention validation exists; notification absent. |
| Task becomes Blocked | Authorized Task editor | **Owner decision required** | Effective watchers if enabled | Skip actor | Current Task visibility at all points | `task:{taskId}:v{taskVersion}:blocked:{recipientId}` | Generic blocked category and version | Block reason, restricted title | Mutation exists; notification absent. |
| Task submitted for Review | Submitter | Current Reviewer | Other effective watchers if enabled | Skip actor | Current Reviewer/Task access at all points | `task:{taskId}:v{taskVersion}:review-requested:{recipientId}` | Generic review-request category | Comment/review body or restricted title | Mutation exists; notification absent. |
| Task returned/rejected | Reviewer/authorized resolver | **Owner decision required**; current Primary Assignee is the minimum candidate | Effective watchers if enabled | Skip actor | Current relationship/Task access at all points | `task:{taskId}:v{taskVersion}:review-returned:{recipientId}` | Generic returned category | Return/rejection reason, restricted title | Mutation exists; notification absent. |
| Major hard-deadline change | Authorized Task editor | **Owner decision required** | Effective watchers if enabled | Skip actor | Current Task access at all points | `task:{taskId}:v{taskVersion}:deadline:{classification}:{recipientId}` | Classification, old/new timestamps only if approved for recipient-specific HTTP; generic broad signal | Planned dates presented as deadline, client `isMajor`, restricted title | Mutation/classifier/notification absent. |
| TaskComment marked Important | Comment author/editor | **Owner decision required**; direct mentions remain separately mandatory | Effective watchers if general activity is enabled | Skip actor | Comment-edit authority and Task visibility at all points | `task-comment:{commentId}:v{commentVersion}:important:{recipientId}` | Generic Important-comment category | TaskComment body, excerpt, restricted title, Watch state | `IsImportant` persists; notification absent. |
| Optional Watch-derived general activity | Task actor | Users whose effective Watch is true after explicit opt-out | The category itself is Watch-derived | Skip actor | Re-evaluate effective Watch plus Task access at intent/dispatch/open | `task:{taskId}:v{taskVersion}:watch-activity:{category}:{recipientId}` | Generic activity category | Watch source/opt-out state, protected content | Watch state exists; no producer. This category is optional and must not be confused with mandatory categories. |

The current canonical comment routes do not notify all participants for an ordinary comment. The retained legacy `ProjectService.NotifyCommentAsync` contradicts that rule if called for a Task and must not be reused. A generic Project invalidation is not counted as a user-visible Task notification.

## Major hard-deadline classification audit

The persisted hard-deadline field is `TaskItem.DeadlineAt` (`DateTimeOffset?`). It is distinct from `PlannedStartDate`, `PlannedEndDate`, legacy `StartDate`/`DueDate`, milestone date, and derived parent schedule. `TaskItemConfiguration` already has `(ProjectId, DeadlineAt)` and tenant/workspace urgency indexes.

No production request DTO or service method currently changes `DeadlineAt`. `TaskCommandService.UpdateScheduleAsync` owns only planned dates/milestone date, and its tests explicitly prove that boundary. Therefore:

- no server code loads persisted old/new DeadlineAt values for classification;
- no code returns `Added`, `Removed`, `ShiftAtLeast24Hours`, `CrossedUrgencyBoundary`, or `None`;
- no client field can currently force or suppress classification, because the mutation itself is absent;
- legacy `DueDate` change notifications in `ProjectService.NotifyTaskChangesAsync` are planned-date behavior and must not be repurposed as hard-deadline evidence;
- `TaskWorkspaceTimeZoneResolver` is the reusable timezone authority.

Required focused tests:

| Case | Expected assertion |
| --- | --- |
| `null` to value | `Added`; one logical intent per authorized recipient. |
| value to `null` | `Removed`. |
| 23h59m absolute shift | `None` unless it independently crosses the Workspace-local urgency boundary. |
| 24h absolute shift | `ShiftAtLeast24Hours`. |
| Same UTC date but Workspace-local midnight crossing | Use Workspace local calendar, not server/browser timezone. |
| Today to Overdue and future to Today | `CrossedUrgencyBoundary`. |
| Planned-end-only mutation | No Deadline classification or notification. |
| Stale version/save failure | No Notification or Outbox side effect commits. |
| Retried logical mutation | Database uniqueness leaves one visible notification per recipient. |
| Invalid Workspace zone | Tenant fallback, then UTC, matching the existing resolver contract. |

## Preference and digest audit

No route equivalent to the required preference GET/PATCH exists. No entity has `deadlineDigestLocalTime`, `defaultTaskDeadlineDigestLocalTime`, a digest policy version, or digest delivery identity. Angular has no preference UI.

### Recommended storage reuse

- Add nullable `TaskDeadlineDigestLocalTime` and `TaskNotificationPreferenceVersionNo` to `WorkspaceMember`. The row already has the canonical `(TenantId, WorkspaceId, UserId)` unique ownership and membership status. It must never be projected through general member DTOs.
- Add `DefaultTaskDeadlineDigestLocalTime` and an independent settings version to `Workspace`. Use `08:00` for existing rows during migration. Do not create a Project setting.
- Keep the existing `Workspace.TimeZone` and `TaskWorkspaceTimeZoneResolver`; do not introduce a second timezone source.
- Add a dedicated digest delivery/claim ledger because no generic job record exists. A row must be tenant/workspace/user owned and uniquely identify `(TenantId, WorkspaceId, UserId, LocalDate, DigestPolicyVersion)`.
- Reuse `Notification` plus its new logical key for the single visible digest, and reuse the transactional Outbox for the resulting user-specific refresh signal.

Membership must be active when reading/updating the preference, selecting a due ledger item, building candidates, creating the notification, dispatching its signal, and opening its target. Revocation should not require deleting the private preference fields; it prevents use. A later reactivation policy can retain the row's setting unless the owner specifies otherwise.

### Scheduler capability assessment

| Capability | Existing reusable component | Current adequacy |
| --- | --- | --- |
| Bounded paging | Notification and repository page-size patterns; Outbox claim cap of 100 | Pattern only; digest query/continuation absent. |
| Per-user/per-Workspace isolation | Tenant filters, `WorkspaceMember`, Workspace timezone resolver | Reusable ownership/auth inputs; scheduler absent. |
| Claim/lease | `OutboxEventRepository.ClaimDueAsync` with PostgreSQL `FOR UPDATE SKIP LOCKED` and lock token | Do not reuse the Outbox table for digest jobs; reuse the concurrency pattern in the digest ledger after owner clarification. |
| Retry/dead-letter | Outbox dispatcher/repository | Not a generic job system; canonical retry/terminal-state boundary is unresolved. |
| Observability | `/health/realtime`, `RealtimeDiagnostics`, repository counts | No digest due/running/succeeded/failed/lag/timezone metrics. |
| DST-safe scheduling | Workspace timezone resolver | No due-window algorithm or DST tests. |
| Failure isolation | Outbox processes one event at a time | No per-user/Workspace digest unit of work. |

The digest builder must page current authorized Tasks separately per user and Workspace and group only hard deadlines into 3 days, 1 day, today, and overdue. It must exclude deleted Tasks, archived/inaccessible Projects, completed/cancelled Tasks, stale memberships, and no-longer-relevant relationships. One user's failure must not roll back another user's ledger/notification.

## Outbox event catalog mapping

The current `RealtimeEventCatalog` supports 11 events: three Message events plus unread state, Notification created/read state, Task changed, Project changed, Announcement changed, File changed, and Authorization state changed. All current Task mutations use `Projects.TaskChanged.v1` with a minimal invalidation payload. The table below prefers an already canonical family rather than adding a synonym merely because the PR07 prompt contains an example.

| Required semantic event | Canonical family to use | Current name/producer | Aggregate and routing | Payload/consumer | Transaction/auth/dedupe/replay gap | Classification |
| --- | --- | --- | --- | --- | --- | --- |
| Task changed | `Projects.TaskChanged.v1` | Same; `BusinessInvalidationPublisher.TaskChangedAsync` from Task services | Task; Project plus affected users | IDs, version, change, changed fields, refetch hint; Task/My Tasks/Project/Kanban/Gantt refetch | Transactional where queued before Task save; affected-user calculation needs prior relationships; user-route target auth missing on replay | Broad invalidation |
| Task stage changed | `Projects.TaskWorkflowChanged.v1` | Generic TaskChanged from transition/Review/Kanban | Task; Project plus affected users | Stage/workflow version metadata only | Catalog/producer/validator absent; use this higher-authority family instead of duplicate TaskStageChanged | Broad invalidation |
| Task blocked state changed | `Projects.TaskBlockedStateChanged.v1` where a distinct consumer needs it; otherwise TaskChanged plus NotificationCreated | Generic TaskChanged from `SetBlockedStateAsync` | Task; Project/affected users | Blocked boolean/version, never reason | Catalog and notification producer absent | Broad invalidation plus user-specific notification signal |
| Task relationship changed | `Projects.TaskAssignmentChanged.v1` for assignee/collaborator and `Projects.TaskReviewerChanged.v1` for reviewer | Generic TaskChanged | Task; Project plus old/new affected users | Relationship kind/change, IDs/version; no private Watch values | Prior user set and specific catalog absent | Broad invalidation |
| Task Watch changed | `Projects.TaskWatchChanged.v1` | Generic TaskChanged | Task; actor user route only unless shared Task projection also changed | Refetch private Watch state | Must not broadcast opt-out/source state; specific event absent | User-specific private invalidation, not broad content |
| Task claimed | `Projects.TaskClaimed.v1` | Generic TaskChanged from `ClaimAsync` | Task; Project plus claimant/affected users | IDs/version/category only | Specific event absent; assignment notification intent must share save | Broad invalidation plus user notification as policy requires |
| Task dependency changed | `Projects.TaskDependencyChanged.v1` | Generic Task/Project invalidation from `ProjectService` | Task/Project; Project route | Edge IDs/version/refetch hint, no display content | Specific catalog absent | Broad Project invalidation |
| Task Checklist changed | `Projects.TaskChecklistChanged.v1` | Generic TaskChanged from subresource service | Task; Project plus affected users | Task/version/refetch only; no checklist text | Specific catalog absent | Broad invalidation |
| TaskComment changed | `Projects.TaskCommentChanged.v1` (TASK-OPEN-005 is approved) | Generic TaskChanged | TaskComment or Task; Project plus affected users | Comment ID/version/change/refetch, never body | Specific catalog absent; mention/Important notification is separate | Broad invalidation |
| Task Review requested | `Projects.TaskReviewRequested.v1` | Generic TaskChanged | Task; Reviewer user plus Project invalidation as needed | IDs/version/state, no reason | Specific catalog and notification producer absent | User-specific notification signal plus broad invalidation |
| Task Review resolved | `Projects.TaskReviewResolved.v1` | Generic TaskChanged | Task; decided recipients plus Project | Outcome category/version; never return reason | Recipient decision and catalog absent | User-specific notification signal plus broad invalidation |
| Task Review overridden | `Projects.TaskReviewOverridden.v1` | Generic TaskChanged | Task; Project/affected users | Override occurrence/version only | Reason must remain out of payload; catalog absent | Broad invalidation, not a PR07 visible notification by default |
| Task Labels changed | `Projects.TaskLabelsChanged.v1` | Generic TaskChanged/ProjectChanged | Task/Project; Project | IDs/version/refetch only | Specific catalog absent | Broad invalidation |
| Task Deadline changed | `Projects.TaskDeadlineChanged.v1` | None | Task; Project plus decided recipients | Classification/version; timestamps only in authorized user response if approved | Mutation, classifier, catalog, notification absent | Broad invalidation plus user-specific notification signal |
| Task Workflow changed | `Projects.TaskWorkflowChanged.v1` | ProjectChanged/generic TaskChanged | Project/workflow; Project | Workflow/project versions/refetch | Specific catalog absent | Broad invalidation |
| Task deadline digest ready | `Notifications.TaskDeadlineDigestReady.v1`, followed by/reused with `Notifications.NotificationCreated.v1` for the visible row | None | Digest/Notification; user route | Notification/logical ID, state version, Workspace/filter identity; no Task list | Builder/ledger/catalog/auth absent | Internal job result and user-specific signal; never broad |
| Task notification preference changed | `Notifications.TaskPreferenceChanged.v1` only if another tab needs refetch; otherwise no broadcast | None | Preference; current user route only | Workspace ID and preference version, not the chosen time | Persistence/catalog absent | Private user invalidation or not broadcast |

`Notifications.NotificationCreated.v1` remains the user-specific "refresh the notification list" signal. It is not a substitute for the business event, and a business invalidation is not a visible notification. Messaging message-body delivery remains outside this Task payload design.

### Existing Outbox contract details

| Concern | Exact current implementation | Audit finding |
| --- | --- | --- |
| Envelope | `DurableEventEnvelope` / `OutboxEvent`: event ID, event type, schema version, aggregate type/ID/version, tenant, occurred time, payload, routing, correlation/causation. | Reusable and structurally adequate for PR07. Workspace/Project/recipient are routing targets rather than dedicated columns. |
| Transaction owner | `TransactionalOutbox.EnqueueAsync` calls the repository, which adds to the scoped `AppDbContext`; the application use case owns the eventual save. | Transactional only when the producer queues before the same successful save. The absent PR07 producers cannot be claimed transactional yet. |
| Claim/lease | `OutboxEventRepository.ClaimDueAsync`; PostgreSQL uses `FOR UPDATE SKIP LOCKED`; lock owner/token/timestamp are persisted. | Reusable delivery mechanism. Hard-coded stale recovery uses ten attempts. |
| Retry | `OutboxDispatcher` calculates exponential backoff with jitter and `RealtimeOptions.MaxAttempts` defaults to ten. | Matches dedicated Outbox contract, conflicts/overlaps generic MVP job guidance. |
| Dead letter | Same `outbox_events` row becomes `DeadLetter` and records `DeadLetteredAt`/safe error metadata. | Existing Outbox behavior; no digest job equivalent. |
| Replay | `OutboxReplayService` requires current Tenant plus PlatformAdmin, a bounded reason, supported schema, and an audit record; repository resets the original row. | No explicit capability beyond role, no exposed controller, no test, and no current Task-target authorization/redaction before stored payload delivery. |
| Deduplication | Event ID is unique; frontend dedupes event IDs. | At-least-once transport dedupe exists, but visible Notification logical dedupe does not. |
| Health/metrics | `/health/realtime`, `RealtimeDiagnostics`, and `GetDiagnosticsAsync` expose pending/retry/dead-letter/oldest/stale lock plus dispatch counters. | Reuse and extend for Task/digest signals. |
| Cleanup | Repository removes old delivered/dead-letter/cancelled rows. | Replay/dedupe retention for visible notifications must not depend on Outbox row retention. |

## Authorized SignalR audit

The server endpoint is `/hubs/app`. `AppHub` is authenticated and accepts logical subscription requests; group names are derived server-side. `HubSubscriptionRegistry` stores in-memory subscriptions and applies count/rate bounds. `RealtimeDispatchAuthorizer` validates the session and route at dispatch. The frontend has one SignalR transport behind `RealtimeFacade`.

| Route | Subscription authorization | Dispatch authorization | Revocation behavior | Payload sensitivity | Reconnect/HTTP behavior | Gap/proposal |
| --- | --- | --- | --- | --- | --- | --- |
| `user:{userId}` | Caller may subscribe only to the current authenticated user target after session/Tenant/feature checks. | Session is revalidated and target ID must equal subscription user. | Authorization-state handling removes registered groups/connections. | Intended for recipient-private invalidation. Current NotificationCreated contains full stored display fields. | Shared client re-subscribes current user, runs catch-up, then returns Connected. Notification list refetch is HTTP. | Add event-specific Task/notification target authorization at delayed dispatch; send only refetch-safe metadata. |
| `project:{projectId}` | `IProjectAuthorizationService.CanViewProject`. | Rechecks `CanViewProject`. | Current dispatch stops after access loss; authorization invalidation clears client state. | Current Task payload is minimal invalidation. | Project owner catch-up refetches Project/Kanban/Gantt and clears on denial. | Add real-backend revocation evidence and semantic event validators. |
| `workspace:{workspaceId}` | `IWorkspaceAuthorizationService.CanViewWorkspace`. | Rechecks Workspace; ProjectChanged on Workspace also rechecks Project, and FileChanged performs file authorization. | Workspace removal blocks route, assuming dispatch occurs after state change. | Workspace routes can fan out broadly, so Task/private fields are unsafe. | Reconnect reauthorizes stored logical subscription, then feature HTTP catch-up. | Generic Task/notification target authorization is not applied on Workspace route. Prefer Project/user routes for PR07. |
| `group:{groupId}` | Not implemented. | Not implemented. | Not implemented. | Group membership and target-group changes are sensitive. | Frontend deliberately does not guess group names. | Either add server-derived Group subscription with current Group membership checks or document that PR07 resolves Group recipients to user routes and never emits a Group target. |

Historical membership is not accepted as sufficient for Project/Workspace dispatch, but it is effectively sufficient for a stored Task notification sent to a still-valid user session because the Task target is not rechecked. Event delivery never grants HTTP access: Task detail endpoints reauthorize independently and safely return denied/not-found state. Polling/manual refresh remains operational but was not counted as authorized SignalR routing.

Private preference and Watch state, comment body, review override/return reason, restricted title, and digest Task list must never be placed on Project, Workspace, or Group routes. Current generic Task events meet the minimal-metadata rule; current NotificationCreated payload needs reduction/reauthorization before PR07 use.

## Notification-open audit

### Current flow

1. RightPanel fetches `GET /api/notifications` directly, including stored title/body and computed target route. It does not use the target-authorizing communication polling projection.
2. `notification-item` is an Angular `RouterLink`. Activating it navigates immediately and emits a mark-read action.
3. `RightPanelFacade.markNotificationRead` independently calls `PATCH /api/notifications/{id}/read`; that endpoint checks notification recipient ownership, not target access.
4. A Task route then calls the canonical Task detail HTTP endpoint, which reauthorizes current access and clears protected state on safe denial/not-found.

Consequently, read state changes on click even if the target later fails. Authorization is not checked before navigation, and previously stored title/body is displayed before the Task endpoint can deny access.

### Required design behavior

| Target case | Required result |
| --- | --- |
| Authorized active Task | Open endpoint verifies current Task visibility, preserves click-to-read behavior, and returns only the current authorized route/scope. |
| Deleted/no-longer-visible Task | Safe unavailable result with no title/Project/comment detail; do not navigate to a guessed route. |
| Archived/inaccessible Project | Same safe unavailable result; no existence distinction beyond the established 403/404 policy. |
| Revoked membership | Clear any protected client projection and return safe unavailable. |
| Moved-not-supported target | Treat as unavailable. Cross-Project Task move is not supported canonically. |
| Digest link | Return an authorized My Tasks route plus explicit Workspace/filter identity; do not embed the digest Task list in the notification event. |
| Unknown notification type | Non-clickable safe fallback; marking it read may retain the current click semantics. |

The recommended API is a recipient-owned notification-open use case, exposed as a single endpoint such as `POST /api/notifications/{notificationId}/open`, that returns an `Openable` or safe `Unavailable` result and performs the existing read transition in the same use case. The exact route name is a technical API choice; it must not return target display details on denial.

## Frontend reconciliation audit

| Surface | HTTP state owner | Realtime owner and current behavior | Version/dedupe/coalescing | Active edit | Authorization/degraded/manual behavior | PR07 gap |
| --- | --- | --- | --- | --- | --- | --- |
| Notifications RightPanel | `RightPanelFacade` directly calls notification APIs. | Subscribes to shared durable events and handles current notification families. | Uses notification `StateVersion`; global event-ID dedupe exists. No test proves HTTP-plus-event one-visible behavior. | Not applicable. | Authorization event clears/reloads; component renders text, not HTML; mark failure preserves unread count. | Use open endpoint, logical IDs, digest DTO/UX, safe revoked/deleted state, preference entry point. |
| Task Detail | `ProjectsFacade` owns canonical Task detail and sections. | Shared Task invalidation refetches active detail. | Rejects/handles versions at shared realtime layer; queues overview refresh. | Preserves Task-body editor and reports conflict; subresource states are not silently overwritten. | Clears protected Task state before reauthorization; safe 404 handling and manual retries. | Recognize semantic PR07 Task events without duplicating state changes. |
| My Tasks | `MyTasksFacade`. | Generic Task/Project/Auth events cause a 150 ms coalesced HTTP refetch. | Shared stale/event-ID gates; request generation cancels stale responses. | Row list has no inline Task-body editor. | Clears rows/counts before authorization refetch; degraded indicator and manual refresh. | Digest links must set explicit Workspace/time filters; add semantic families to the same coalesced path. |
| Project Detail/List | `ProjectDetailFacade` and `ProjectsFacade`. | Shared project subscriptions and HTTP catch-up. | Request generations discard older responses; refresh is bounded/coalesced. | Task detail preserves editor conflicts. | Denied reconnect clears Project projections before HTTP revalidation. | Add semantic event mappings without feature sockets. |
| Kanban | `ProjectDetailFacade`. | Generic Task/Project invalidation triggers authoritative snapshot refetch. | Ignores stale task/board versions and coalesces microtasks. | Queues reconciliation during menu/move and rolls back or refetches. | Reconnect denial clears board; HTTP remains usable in degraded mode. | Consume relevant semantic Task events through same queue. |
| Gantt | `ProjectDetailFacade`. | Generic Task/Project invalidation triggers canonical snapshot refetch. | Stale version rejection; one follow-up while HTTP request is in flight. | Queues during edit/command, preserves safe intent on conflict. | Permission denial clears snapshot; accessible HTTP forms/manual refresh remain. | PR06 temporary limits remain unchanged; no PR06B pagination/virtualization work is part of PR07. |

The frontend foundation already satisfies the one-shared-client constraint. PR07 must extend `RealtimeFacade` validators and existing feature handlers rather than add notification-, Kanban-, Gantt-, or Task-specific connections.

## Security and privacy findings

| Risk | Current control | Missing control | Required test | Severity if unfixed |
| --- | --- | --- | --- | --- |
| Cross-Tenant recipient generation | Tenant query filters and mutation authorization; Outbox rejects a mismatched current Tenant. | Central recipient policy that verifies every recipient is in the same Tenant/Workspace/Project before Notification creation. | Tenant A mutation cannot create a Tenant B notification/Outbox target, including guessed mention ID. | Critical |
| Cross-Workspace recipient generation | Task carries Workspace/Project ownership; mention validation uses Project eligibility. | Category-wide recipient resolver and digest query partitioned by Workspace. | Same user in two Workspaces receives only the correct Workspace event/digest. | High |
| Stale membership/archive delivery | Project/Workspace routes reauthorize at dispatcher; membership changes invalidate user connections. | Task-target authorization for user notification dispatch/replay/open, plus prompt authorization-state invalidation for Workspace archive paths in `WorkspaceService` and `AdminService`. | Revoke or archive after enqueue but before dispatch/open; no protected payload, route, or title is returned and already-rendered protected state clears. | High |
| Broad-route payload leakage | Current generic TaskChanged is minimal invalidation. | Event-specific schema validators/allowlists for all new semantic events. | Reject comment text, review reason, private preference/Watch fields, and restricted display fields from broad routes. | High |
| Comment-content leakage | Modern Task AuditLog omits comment text; current public Task comment path emits no notification. | Remove/guard legacy Task notify-all path; stop NotificationCreated from carrying content-bearing Task fields. | Ordinary/mention/Important events contain IDs/version/category only; logs and Outbox are content-free. | High |
| Restricted-title leakage | None on direct notification list; polling redacts some denied target types. | Direct notification projection/open reauthorization and metadata-safe stored/display contract. | Revoked user sees generic unavailable state and no prior restricted display value in response/event. | High |
| Review-reason leakage | Generic Task invalidation omits reasons; current review audit uses bounded safe metadata. | Explicit payload tests for review semantic events and visible notifications. | Outbox/log/SignalR assertions contain outcome/category only. | High |
| Private Watch/preference leakage | Watch event payload is currently only generic Task invalidation; Watch API is actor-specific. | User-only/no-broadcast semantic route and private DTO tests. | Other Project members cannot observe opt-out/source or digest time. | High |
| Replay authorization bypass | Replay requires current Tenant + PlatformAdmin and later route authorization. | Target-aware authorization/redaction for Task notification user routes; replay integration test. | Enqueue, revoke, dead-letter, replay: no delivery or protected stored payload. | High |
| Notification target enumeration | Recipient-owned notification mutations return generic failures; Task read uses safe denial/not-found. | Open endpoint with uniform unavailable response and no target details. | Other user's notification ID, deleted target ID, and revoked target are indistinguishable at the allowed policy boundary. | Medium |
| Duplicate visible notifications | Application heuristic and frontend event-ID dedupe. | Stable logical key plus database unique constraint; retry test. | Concurrent same event/recipient and Outbox replay leave one visible row/state change. | High |
| Protected values in logs | Task comment AuditLog omits text; review audit uses safe metadata; logging guidance exists. | PR07-specific structured logging allowlist and assertions for worker/dispatcher failures. | Failure/retry/timezone logs contain IDs/codes/counts only. | High |

## Data model and migration impact

| Category | Existing model to reuse | Required change | Uniqueness/ownership | Soft-delete and compatibility | Rollback implication |
| --- | --- | --- | --- | --- | --- |
| Notification logical identity | `Notification` | Add nullable bounded `LogicalKey` (or hash plus inspectable category fields) for new producers; add unique index. Keep legacy rows null. | Unique `(TenantId, UserId, LogicalKey)` where key is non-null. The key must include logical business event/category/recipient, not title. | Uniqueness should include soft-deleted rows so replay does not resurrect a deleted logical event. Existing heuristic may remain only for legacy producers until normalized. | Additive nullable column is deployment-safe; down migration loses dedupe identities and must not be used after new producers are enabled. |
| User/Workspace preference | `WorkspaceMember` | Add nullable `TaskDeadlineDigestLocalTime` and `TaskNotificationPreferenceVersionNo` concurrency token. | Existing unique `(TenantId, WorkspaceId, UserId)` owns the value. | Membership status, not soft deletion, controls use. Explicit DTO projection prevents private-state leakage. | Additive fields; rollback loses user choices. |
| Workspace default | `Workspace` | Add `DefaultTaskDeadlineDigestLocalTime` with `08:00` backfill/default and independent settings version. | Tenant/Workspace owned; no Project override. | Workspace soft-delete/archive makes it ineligible for dispatch. | Additive field; rollback loses configured defaults. |
| Digest delivery/idempotency | No reusable job table | New Task deadline digest ledger with local date, policy version, due/attempt/terminal state, safe error code, lock owner/token/time, notification/outbox references, timestamps. | Unique `(TenantId, WorkspaceId, UserId, LocalDate, DigestPolicyVersion)` plus due/claim indexes. | Retain according to operational policy; never soft-delete to allow a duplicate send. Current canonical documents do not define a new retention period. | Down migration is unsafe while worker enabled; disable/stop worker, drain/record state, then roll back. |
| Deadline classification | `TaskItem.DeadlineAt`, Task version, AuditLog, Outbox | No new classification table is required. Compute from persisted old/new values; return safe result and put classification in Audit/event metadata. | Task version/logical key supplies uniqueness. | Task soft-delete/archive excludes notification/digest. | No schema rollback. |
| Outbox catalog | Existing Outbox schema | Add catalog constants, payload validators, and producers; no new Outbox columns required. | Existing event ID/route validation. | Existing rows remain versioned; new schemas require backward-compatible dispatcher support during rollout. | Disable producers/consumers; retain rows until supported or safely terminal. |
| Task deadline query | Existing Task indexes | Review query plan; existing tenant/workspace/DeadlineAt index is likely reusable. Add only a focused filtered/composite index if PostgreSQL evidence shows it necessary. | Tenant/Workspace scoped. | Deleted/completed/cancelled predicates must be in query; do not guess an index before measuring. | Index removal only after worker disabled. |

Two focused migrations are recommended: PR07-A for notification/preference/Workspace settings, and PR07-C for the digest ledger (plus any measured digest query index). No migration belongs in this audit PR.

## Existing infrastructure that must be reused

- `src/Coglatas.Infrastructure/Persistence/DbNotificationService.cs` — notification row/state mutation and existing Notification Outbox signals; extend its contract instead of creating a second store.
- `src/Coglatas.Application/Notifications/NotificationApplicationService.cs` and `src/Coglatas.Web/Controllers/NotificationsController.cs` — recipient-owned notification lifecycle.
- `src/Coglatas.Application/Projects/TaskCommandService.cs` — canonical relationship/workflow/review/claim/deadline transaction boundary.
- `src/Coglatas.Application/Projects/TaskSubresourceService.cs` — canonical TaskComment/mention/Important, checklist, label, Watch/file boundary.
- `src/Coglatas.Application/Projects/TaskDerivedValues.cs` — `TaskWorkspaceTimeZoneResolver` Workspace/Tenant/UTC timezone precedence.
- `src/Coglatas.Application/Workspaces/WorkspaceService.cs` and `src/Coglatas.Application/Admin/AdminService.cs` — membership/archive mutation boundaries that must participate in current authorization-state invalidation.
- `src/Coglatas.Application/Realtime/TransactionalOutbox.cs` and `src/Coglatas.Application/Realtime/BusinessInvalidationPublisher.cs` — same-transaction enqueue and minimal invalidation patterns.
- `src/Coglatas.Infrastructure/Persistence/OutboxEventRepository.cs` and `src/Coglatas.Infrastructure/Persistence/Configurations/OutboxConfigurations.cs` — PostgreSQL claim/lock/retry storage and indexes.
- `src/Coglatas.Web/Realtime/OutboxDispatcher.cs`, `src/Coglatas.Web/Realtime/RealtimeDispatchAuthorizer.cs`, `src/Coglatas.Web/Realtime/HubSubscriptionAuthorizer.cs`, `src/Coglatas.Web/Realtime/HubSubscriptionRegistry.cs`, and `src/Coglatas.Web/Realtime/AppHub.cs` — authorized delivery pipeline.
- `src/Coglatas.Application/Realtime/OutboxReplayService.cs` and `src/Coglatas.Web/Program.cs` `/health/realtime` — operator replay/diagnostics foundation.
- `frontend/src/app/core/realtime/realtime.facade.ts` — the only client transport, event-ID dedupe, stale-version gates, logical subscription, and catch-up owner.
- `frontend/src/app/shared/right-panel/right-panel.facade.ts`, `frontend/src/app/features/projects/projects.facade.ts`, `frontend/src/app/features/projects/my-tasks.facade.ts`, and `frontend/src/app/features/projects/project-detail.facade.ts` — existing HTTP state/reconciliation owners.

The Outbox table must not become a digest candidate/job table. The communication polling route must not be described as SignalR. Notification persistence must not be described as recipient-policy implementation.

## Test and operational evidence inventory

### Observed existing evidence

| Test/file | What it actually proves | Limitation |
| --- | --- | --- |
| `NotificationApplicationServiceTests.UserCannotReadAnotherUsersNotification` | Application service asks for the current user's notification. | Fake repository; no EF/Tenant/race/target auth. |
| `NotificationApplicationServiceTests.UnreadCountExcludesReadNotifications` | Current unread-count service behavior. | No realtime/state-version race. |
| `CommunicationPollingServiceTests.AdminNonParticipantGetsSafePlaceholderForDmNotification`, `DmParticipantGetsSafeNotificationMetadataOnly`, and `StudentRecordAndFileNotificationTargetsFailClosed` | The separate polling projection reauthorizes/redacts supported notification targets and omits message body/private state. | RightPanel does not use polling; Task target authorization is not implemented there. |
| `OutboxEventRepositoryTests.ClaimFailureAndDeadLetterTransitionsPreserveEventIdentity` | In-memory status/identity transitions. | Not PostgreSQL locking or dispatcher authorization. |
| `OutboxEventRepositoryTests.StaleProcessingLockIsRecoveredWithoutDeletingPendingWork` | Stale lock recovery state transition. | In-memory provider, no competing worker. |
| `TaskWorkspaceTimeZoneResolverTests` (five named tests) | Workspace precedence, Tenant fallback, cross-Tenant ignore, UTC fallback, cancellation. | No DST scheduler/classifier behavior. |
| `TaskCommandServiceTests.GanttScheduleCommandOwnsOnlyPlannedDatesAndQueuesAtomicInvalidations` | Planned schedule mutation is separate from hard deadline and generic invalidation is queued. | Fake unit of work/publisher. |
| `TaskV1CoreConcurrencyPostgreSqlTests.AssignmentRaceWithRealNotificationsCommitsWinnerSideEffectsOnly` | Winning compatibility assignment, Notification, state version, and NotificationCreated Outbox row commit together. | Conditional PostgreSQL; legacy assignment policy, not canonical Primary Assignee notification. |
| `Prompt2CRepresentativeTaskMutationsKeepOutboxEnvelopeVersionsAligned` | Representative canonical Task changes have aligned Task/Audit/generic Outbox versions. | No semantic PR07 events/visible notification recipients. |
| `Comment_UpdateDeleteAndLegacyAdapterRemainAtomicAndPrivate` and comment race tests | Canonical comment row/version/audit/generic Outbox atomicity and AuditLog text privacy. | No mention/Important notification. |
| Watch PostgreSQL tests | Automatic sources, explicit opt-out, actor-specific read, uniqueness/race behavior. | No notification-policy consumption. |
| `realtime.facade.spec.ts` tests | One client, logical user subscription, reconnect reauthorization/catch-up order, denied owner handling, bounded event dedupe, stale rejection, clear/degraded behavior. | Mock transport; no server Hub/dispatcher. |
| `my-tasks.facade.spec.ts` authorization test | Protected rows/counts clear before refetch. | No semantic PR07 events or digest link. |
| `project-detail.facade.spec.ts` reconciliation tests | Stale/coalesced Gantt/Kanban refresh, active-edit queuing, reconnect denial clearing, degraded HTTP. | No real SignalR/backend. |
| `projects.facade.spec.ts` conflict/authorization tests | Task detail preserves edits and clears protected state on denial. | No notification-open flow. |
| `right-panel.component.spec.ts` | Known target mapping, unsupported non-clickable targets, text-only body rendering, mark-read failure behavior. | No realtime NotificationCreated test, logical dedupe, digest, or target reauthorization. |

`/health/realtime` exposes current Outbox state and counters. `OPERATIONS.md` has no PR07 replay/digest procedure and explicitly notes incomplete background-job health. No test found covers a real AppHub subscription, OutboxDispatcher-to-SignalR delivery, Task notification membership revocation, replay authorization, digest scheduling, timezone/DST delivery, or PR07 two-user browser flow.

### Future required implementation evidence

- PR07-A: fresh/upgrade migration tests; unique logical-key race; preference GET/PATCH membership, Tenant/Workspace isolation, inheritance, ETag/version conflict, and private DTO tests.
- PR07-B: every immediate category/recipient, actor skip, Watch independence, invalid mention non-disclosure, ordinary comment no notify-all, deadline boundaries, transaction rollback, and logical retry tests on PostgreSQL.
- PR07-C: candidate paging, four digest groups, membership/Task revalidation, multi-Workspace/timezone, DST gap/fold, failure isolation, three/ten-attempt policy after decision, terminal/restart, and idempotency race tests.
- PR07-D: Hub/dispatcher integration for user/project/workspace/group-or-explicit-non-use, delayed revoke/replay, notification-open active/deleted/archived/revoked/unknown/digest cases, frontend HTTP-plus-event dedupe, preference UI, and affected-surface reconciliation.
- PR07-E: Real Backend two-user flows, disconnect/reconnect/catch-up/manual refresh, revocation clearing, metadata-safe health/logging, accessibility, narrow/touch, and operational replay drill.

## Confirmed gaps and highest-risk findings

1. No current canonical Task mutation generates the complete PR07 immediate recipient policy; legacy assignment behavior is not equivalent.
2. No stable database-enforced logical notification key exists, so concurrent/retried production can duplicate visible notifications.
3. `DeadlineAt` is persisted/read but has no production mutation or major-change classifier. Legacy due/planned dates must not be substituted.
4. Preference routes, durable preference/default settings, digest builder, scheduler, ledger, and job observability are absent.
5. Delayed/replayed user-route Notification events do not reauthorize the related Task, while the stored realtime payload includes display fields; Workspace archive paths also do not proactively publish authorization-state invalidation to affected members.
6. Notification opening navigates/marks read without first authorizing the target; the Task endpoint only denies after navigation.
7. Only generic Task invalidation exists. The complete PR07 semantic catalog, event-specific safe payloads, and current-auth routing evidence are absent.
8. A retained legacy Task comment notification helper contradicts ordinary-comment no-notify-all and metadata-safety rules if invoked.
9. The repository has no general digest job system; Outbox delivery infrastructure must be reused for delivery, not misrepresented as the digest scheduler.
10. Real Hub/dispatcher/revocation/replay, digest/DST, and two-user PR07 acceptance evidence is absent.

## Owner decisions

The three exact blockers are in `docs/decisions/task-v1-pr07-owner-decisions.md`:

- `PR07-OWNER-001`: exact mandatory recipient sets;
- `PR07-OWNER-002`: supported digest local-time granularity/range;
- `PR07-OWNER-003`: generic BackgroundJob versus dedicated Outbox/digest delivery profile.

Technical details that do not require an owner decision:

- the digest candidate page size and maximum visible contents may be bounded configuration with explicit "more items" behavior and no silent data claim;
- the digest policy version is an application-owned constant persisted in the ledger/logical key and incremented only when eligibility/grouping semantics change;
- notification dedupe is event-identity based, not a time window, so the unique key remains even after soft deletion;
- digest notification retention follows the existing Notification retention/deletion contract unless a future retention policy changes it;
- the digest link can use existing My Tasks Workspace/time filters after the authorized open endpoint returns the current route.

## Go / No-Go

**NO-GO.** PR07-A's complete API contract cannot be reviewed until digest granularity is decided. PR07-B and PR07-C additionally depend on the recipient and delivery-profile decisions. Once the canonical specification records those answers, implementation can start in the single sequential lane defined by `docs/TASK_V1_PR07_PLAN.md`.

PR06B remains wholly outside this audit and every PR07 phase. The PR06 temporary limits remain unchanged: 500 combined canonical WorkItems/Milestones, 2,000 active same-Project dependencies, typed HTTP 400 on overflow, fail closed, no silent truncation, and no partial successful snapshot. PR08 integration/cutover remains outside PR07.
