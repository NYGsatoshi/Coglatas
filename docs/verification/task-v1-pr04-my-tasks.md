# TASK-V1-PR04 My Tasks verification

## Baseline and authoritative sources

- Main start SHA: `2d2d410fdbdbd13cbf47b0a0b97df4e27fa86563`
- Implementation branch: `task/v1-pr04-my-tasks-acceptance`
- Final implementation HEAD: `b00ae6562a3946b47047464d109b139a20978c4d`
- Pull request: `#254`
- Merge commit on `main`: `36d15d0506c26a10af55d5c6204acfa9ec7a9c70`
- Main-start CI: run `30410498290`, success, head `2d2d410fdbdbd13cbf47b0a0b97df4e27fa86563`
- Main-start Code Quality: run `30410498299`, success, head `2d2d410fdbdbd13cbf47b0a0b97df4e27fa86563`
- Final-head CI: run `30426910856`, success, head `b00ae6562a3946b47047464d109b139a20978c4d`
- Final-head Code Quality: run `30426910873`, success, head `b00ae6562a3946b47047464d109b139a20978c4d`
- Final-head Real Backend Browser Smoke: run `30431717178`, success, head `b00ae6562a3946b47047464d109b139a20978c4d`
- Canonical repository source: `docs/TASK_V1_PR04.md`
- Supporting contracts: `docs/API_CONTRACTS.md`, `docs/SECURITY_MODEL.md`, `docs/DATABASE.md`,
  `docs/TESTING.md`, `docs/TASK_V1_PR02.md`, and the current Task command,
  authorization, Watch, migration, controller, Angular, and test sources
- External specification sources were used read-only for Task planning acceptance,
  API/realtime mapping, and stable ordering. Repository source/tests remain the
  implementation authority.
- This document synchronization is documentation-only. It records the accepted
  implementation HEAD and its merge into `main`; it does not alter product code,
  tests, migrations, or CI behavior.

## Implementation gap audit

### Complete in the starting implementation and retained

- Canonical `GET /api/me/tasks` and `GET /api/me/tasks/counts` routes
- Seven relationship views and the two explicit Workspace scopes
- Server paging DTOs and the project, stage, priority, blocked, search, and
  urgency filters
- `task_items`-rooted projection, active Workspace membership predicate,
  Project visibility predicate, and relationship predicates
- One batched label lookup for the current page
- Grouped Angular list at `/tasks`
- Realtime event coalescing and authorization-state row clearing
- Existing PR04 Task, collaborator, assignee, reviewer, creator, and sort indexes

### Partially complete in the starting implementation, now closed

- Multiple-Workspace users now always send the AppShell/session active Workspace ID
- Workspace changes clear protected rows/counts, reset page 1, cancel the prior
  request, and refetch the selected Workspace
- All six filters and server previous/next/page-size controls are connected to
  canonical HTTP query parameters
- Seven accessible relationship tabs show counts and support arrow/Home/End keys
- Search is debounced and stale responses are generation-guarded
- 401/403, invalid scope, safe not-found, network, and server errors have distinct
  safe frontend handling; 401/403 clear rows and counts
- Manual HTTP refresh remains available while SignalR is degraded

### Missing in the starting implementation, now added

- `Scope=TaskV1PR04` focused real-PostgreSQL projection, migration, SQL-count,
  cancellation, and browser-seed coverage
- HTTP contract coverage for sole/multiple Workspace selection, string enums,
  safe errors, Project visibility roles, cross-Tenant denial, and revocation
- A forward migration for the canonical effective-Watch predicate
- A real-backend PR04 Playwright scenario using ASP.NET Core, PostgreSQL,
  latest migrations, built Angular, cookie auth, CSRF, real My Tasks endpoints,
  and the authorization-state refresh path

### Incorrect in the starting implementation, now corrected

- Undefined relationship enums could fall through to an unfiltered projection
- Public PR04 enums were not consistently serialized as stable strings
- All `PlanningController` failures were flattened to a generic 400
- Priority text was ordered lexicographically instead of canonical severity
- `Today` overlapped `Overdue`; the seven-day calendar boundary was off by one
- Watching trusted legacy `IsWatching` rather than the effective-Watch formula
- Team Queue did not enforce Backlog/Todo eligibility
- quick-edit permission flags could overstate derived-field, deadline, or Claim
  commands
- active/deleted Workspace state was not included in the projection predicate
- Project filter preauthorization did not use the exact My Tasks visibility rule
- counts used thirteen separate count queries instead of two set-based queries
- Session Workspace discovery retained suspended memberships, so an
  authorization-state refresh could reselect a revoked Workspace
- The durable realtime validator required RFC UUID version/variant bits even
  though the backend contract and deterministic Tenant IDs use canonical,
  non-empty .NET GUIDs
- Project list DTO authorization checks were run concurrently over the same
  scoped EF Core `DbContext`

### Hosted verification completed

- The protected hosted runner supplied the required `SYNCFUSION_LICENSE` secret
  without exposing it in source, bundle, logs, or artifacts.
- Final-head CI, Code Quality, and Real Backend Browser Smoke all completed
  successfully on the exact implementation HEAD `b00ae656...`.
- The hosted real-backend run used real ASP.NET Core, PostgreSQL latest
  migrations, built Angular, cookie authentication, CSRF, real My Tasks APIs,
  SignalR/authorization refresh paths, and synthetic test data.
- The hosted real-backend suite completed 4 passed, 0 failed, 0 skipped.
- Unresolved review threads were 0 before merge.
- PR #254 was merged into `main` as `36d15d0506c26a10af55d5c6204acfa9ec7a9c70`.

### Out of scope and unchanged

- PR05 Kanban
- Prompt 3
- Gantt
- notification finalization
- Workspace-local timezone materialization
- Project/browser timezone inference

## API and authorization contract

- List: `GET /api/me/tasks`
- Counts: `GET /api/me/tasks/counts`
- Views: `assigned`, `participating`, `reviews`, `created`, `watching`,
  `teamQueue`, `completed`
- Scopes: default `currentWorkspace`; explicit `allWorkspaces`
- A sole active Workspace may be selected server-side. Multiple active
  Workspaces require an explicit `workspaceId`.
- Resolution order is authentication/query validation, active Workspace scope,
  Project visibility, relationship, filters, and paging.
- Project visibility covers Project owner/member/group and active Workspace
  Owner/Admin/Adviser without bypassing active membership.
- Cross-Tenant Project IDs are safe 404; unavailable explicit Workspace IDs are
  safe 403; ambiguous current scope is safe 400.
- Model-binding validation uses safe ASP.NET Core ValidationProblemDetails.
  Application failures use a request-ID-bearing, redacted error envelope.
- Page metadata uses normalized page `>= 1` and page size `1..100`.
- Stable order is blocked, canonical priority severity, UTC deadline, planned
  end, updated timestamp, and Task ID.

## Relationships, urgency, and DTO

- Assigned uses `PrimaryAssigneeUserId`.
- Participating uses `WorkItemCollaborator`.
- Reviews uses `ReviewerUserId`.
- Created uses `CreatedByUserId`.
- Watching uses
  `IsManualWatch || (!IsExplicitOptOut && AutomaticSources != None)`.
- Team Queue is unassigned, targets a current group, and is Backlog/Todo.
- Completed is Done/completed plus any canonical current-user relationship.
- The projection remains rooted at `task_items`; it never uses
  `TaskAssignment` as its source and cannot duplicate rows within a view.
- UTC deadlines take precedence over date-only planned end. The groups are
  mutually exclusive `Overdue`, `Today`, `Next7Days`, `Later`, and
  `NoDeadline`. No Workspace/browser/Project timezone was introduced.
- The canonical projection includes Task/Tenant/Workspace/Project identity,
  workflow, priority, blocked/planning/deadline/progress fields, assignee/group/
  reviewer, labels/checklist, relationships, time group, version, permissions,
  and warnings.
- quick-edit permissions are computed in the backend. Unsupported deadline edit
  and uncertain permissions fail closed. Claim additionally requires current
  Project membership, matching the command service.
- Storage keys, paths, grant tokens/hashes, other users' private Watch intent,
  and authorization internals are not projected.

## PostgreSQL and query evidence

- PostgreSQL: `18.4` (`postgres:18-alpine`)
- Latest migration:
  `20260729010000_AddMyTasksEffectiveWatchIndex`
- Clean apply: passed from an empty ephemeral PostgreSQL database
- Upgrade: passed from `20260719071017_MyTasksProjectionIndexes` to latest
- Pending migrations: none
- Pending model changes: none
- Forward effective-Watch index:
  `IX_work_item_watch_states_effective_watch`
  on `(TenantId, UserId, TaskItemId)` with the canonical partial predicate
- List query count: 4 total (count, paged projection, one label batch, available
  Workspace metadata); no row-level relationship query and no legacy assignment query
- Counts query count: 3 total (all seven view counts in one `UNION ALL`, all five
  urgency counts in one `UNION ALL`, and available Workspace metadata)
- Captured SQL asserts the label batch, collaborator predicate, absence of
  `task_assignments`, and set-based counts.
- `pg_indexes` confirmed the primary-assignee, reviewer, creator, collaborator,
  tenant/Workspace sort, and effective-Watch index definitions. The small
  acceptance fixture was not used to justify hints or artificial indexes.

## Final local verification

All counts below exclude the two intentionally obsolete legacy-static Playwright
skips, which are not PR04 acceptance tests.

- `Scope=TaskV1PR04` run 1: 8 passed, 0 failed, 0 skipped
- `Scope=TaskV1PR04` run 2: 8 passed, 0 failed, 0 skipped
- `Scope=TaskV1PR03C`: 29 passed, 0 failed, 0 skipped
- `Scope=TaskV1Prompt2C`: 35 passed, 0 failed, 0 skipped
- `Scope=TaskV1Prompt2D`: 19 passed, 0 failed, 0 skipped
- `Category=PostgreSQLIntegration`: 66 passed, 0 failed, 0 skipped
- Full backend: 408 passed, 0 failed, 0 skipped
- Angular unit: 242 passed, 0 failed
- Angular production build: passed; pre-existing non-blocking size warnings remain
- Frontend architecture check and architecture tests: passed, 3/3
- Storybook static build: passed; pre-existing size warnings remain
- Mock Angular Playwright: 52 passed, 0 failed; 2 obsolete legacy-static tests skipped
- Local published real-backend diagnostic: 4 passed, 0 failed, 0 skipped
  (mandatory MVP0, Hub degraded, PR04, and PR03C)
- Protected hosted real-backend Compose: 4 passed, 0 failed, 0 skipped

## Realtime, concurrency, and protected changes

- `Projects.TaskChanged.v1`, `Projects.ProjectChanged.v1`, and
  `Security.AuthorizationStateChanged.v1` coalesce to HTTP refresh.
- Authorization-state invalidation cancels the active request and clears rows,
  counts, total count, message, error, and stale Workspace options before refetch.
- HTTP remains the source of truth and manual refresh remains available when the
  Hub is degraded.
- The shared heavy group remains `coglatas-self-hosted-frontend-heavy` with
  `cancel-in-progress: false` for Qodana, Code Quality frontend, CI frontend
  (Angular/Storybook/Playwright), and the manual real-backend Playwright job.
- Hosted heavy execution was serialized without overlap:
  Qodana -> CI frontend -> Code Quality frontend -> Real Backend.
- No timeout increase, unit retry, or frontend parallelism change was made.
- User-owned `qodana.yaml`, `.coglatas-spec-source/`, and `.tools/` were not staged or
  modified by the implementation work.

## Hosted gate and final verdict

### Historical candidate evidence

- Prior candidate CI: run `30419612749`, success
- Prior candidate Code Quality: run `30419612722`, success
- Prior candidate Real Backend Browser Smoke: run `30423352968`, failed because
  Project list authorization used concurrent operations on one scoped
  `DbContext` and the valid deterministic Tenant GUID was rejected before the
  authorization-state event reached the facade
- Those root causes were fixed by deterministic product changes and covered by
  focused unit, PostgreSQL, and real-browser tests. The failed candidate is
  retained as historical evidence only.

### Final implementation-head evidence

- CI: run `30426910856`, success, head
  `b00ae6562a3946b47047464d109b139a20978c4d`
- Code Quality: run `30426910873`, success, head
  `b00ae6562a3946b47047464d109b139a20978c4d`
- Real Backend Browser Smoke: run `30431717178`, success, head
  `b00ae6562a3946b47047464d109b139a20978c4d`
- Real Backend Browser Smoke artifact:
  `real-backend-browser-smoke-artifacts`, valid and tied to the same head
- Unresolved review threads before merge: 0
- PR #254 final state: merged
- Merge commit: `36d15d0506c26a10af55d5c6204acfa9ec7a9c70`

### Verdict

- PR03C: `Complete`
- Prompt 2 overall: `Complete`
- Main CI recovery: `Complete`
- PR04 acceptance: `Complete`
- PR04 merge: `Completed`
- PR05: `No-Go` until a separate kickoff audit confirms its authoritative scope
  and the latest `main` Gate
- Prompt 3: `No-Go` until its authoritative scope and prerequisites are established

This closure records the accepted PR04 implementation and its merge. A future
PR05 or Prompt 3 kickoff must independently re-read the latest `main` SHA and
its current hosted workflow state; this document does not pre-authorize either
next stage.
