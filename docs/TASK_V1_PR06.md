# TASK-V1-PR06: Canonical Project Gantt Adapter

TASK-V1-PR06 upgrades the existing Project Detail Schedule tab from its
read-only compatibility projection to an authorized, versioned projection and
manual-edit surface over canonical WorkItems.

Status: PR #259 was merged on 2026-08-01 at merge commit
`d5de01cf303c914c2b390346575a22cadb8b4443`. The numerical capacity decision
was unresolved when the merge occurred and was approved only afterward as the
temporary PR06 full-snapshot contract. Detailed evidence belongs in
[`docs/verification/task-v1-pr06-gantt.md`](verification/task-v1-pr06-gantt.md).

## Post-merge status correction — 2026-08-01

### Historical merge-time state

PR #259 was merged before the numerical owner decision was formally recorded.
Its retained PR body and the historical sections below therefore correctly
show `Acceptance: Incomplete`, `Merge: No-Go`, `Merge performed: No`, and an
unresolved decision as the state immediately before the merge. This correction
does not rewrite that history.

### Post-merge owner decision

After the merge, the owner approved the existing implementation safeguards as
the temporary PR06 full-snapshot contract:

- maximum 500 combined canonical Task-kind WorkItems and Milestones;
- maximum 2,000 active dependencies whose endpoints are active canonical Tasks
  in the same Project;
- repository-standard typed HTTP 400 on overflow;
- fail closed, with no silent truncation, partial item set, partial dependency
  graph, or successful partial snapshot.

### Current contract

The server MUST reject the complete snapshot request when either temporary
limit is exceeded. It MUST NOT return a successful partial snapshot or silently
truncate items or dependencies. These are temporary safety limits for the PR06
full-snapshot implementation; they are not permanent Project capacity limits,
database storage limits, or general-availability scalability guarantees.

Current fields:

- PR #259: Merged
- Merge commit: `d5de01cf303c914c2b390346575a22cadb8b4443`
- PR06 temporary capacity decision: Resolved post-merge
- PR06B large-project support: Open / Deferred

### Follow-up scalability work

Paginated and virtualized large-project Gantt delivery is separate from this
corrective work and is tracked in
[`TASK-V1-PR06B` issue #270](https://github.com/NYGsatoshi/Coglatas/issues/270).
This correction does not add
pagination, infinite scrolling, or virtual scrolling.

Unless explicitly labelled current or post-merge, the candidate and Gate
entries below are preserved as historical merge-time evidence.

## Identity and authority

- Branch: `task/v1-pr06-gantt-adapter`
- Audit start PR HEAD:
  `e9519724506010e643e72837ea83aa9801f33194`
- Audit start main HEAD and actual latest main:
  `33c35cbc873fcdc78b75663d195ca120e2c01520`
- Latest incorporated main HEAD:
  `33c35cbc873fcdc78b75663d195ca120e2c01520`
- Current normal main merge commit:
  `1abce6c70d9f665b773d35f75d63c0d05a387cc8`
- Messaging scope cleanup commit:
  `e8bdf47754ca38b6f4d1b3a31c945ae07432f06f`
- Code-bearing candidate HEAD:
  `1abce6c70d9f665b773d35f75d63c0d05a387cc8`
- Inspection metadata fallback remediation:
  `8efa845dec5c553d5ff2107cf6edef7993141a8b`
- Backup Messaging patch SHA-256:
  `1099E128C2BBBE43D986C29427F82F2CBDB14371320FEC00E1B402DF628844DD`
- Ahead / behind: 29 / 0 before the final documentation commit
- Draft PR: `#259`, open and mergeable
- Final documentation HEAD: Pending
- Canonical specification revision:
  `20aa5a2e015ae8fb68e5ba2b257a416dfcad5c3f`
- Primary prompt:
  `docs/specs/aip-core-v4/12-implementation-kickoff/task-v1-pr06-gantt-adapter-prompt.md`
- Core authority:
  `01-core/12-task-work-management.md`,
  `01-core/11-task-work-planning-scope.md`,
  `01-core/04-permission-data-access-security.md`,
  `01-core/03-efcore-postgresql-design.md`, and
  `01-core/api-error-contract.md`
- Realtime/persistence authority:
  `01-core/realtime-event-catalog.md`,
  `01-core/signalr-group-authorization-matrix.md`,
  `01-core/outbox-delivery-contract.md`, and
  `01-core/reconnect-catchup-sequence.md`
- API mapping:
  `06-implementation-mapping/task-work-planning-api-realtime-contract.md`

The specification repository is read-only implementation input and is not
copied or staged into this repository.

## Existing seam and source of truth

PR06 retains:

- `GET /api/projects/{projectId}/gantt`;
- the existing Project Detail Schedule tab;
- canonical `TaskItem`/WorkItem schedule, progress, hierarchy, workflow,
  Blocked, assignment, and version state; and
- canonical `TaskDependency` persistence and PR02 dependency routes.

It does not introduce a Gantt Task table, dependency table, authoritative
date/progress copy, vendor model, parallel route, or parallel client store.

The existing separate `Milestone` persistence and routes may remain
compatibility surfaces. The Coglatas adapter still presents canonical Milestone
semantics: mandatory date, zero duration, progress 0 or 100, stable identity,
and current authorization. Compatibility records must not be copied into
vendor-owned rows or emitted twice as one logical item.

## Target snapshot

The existing route is upgraded in place to return a bounded, deterministic,
vendor-neutral projection:

```text
projectId, projectTitle
projectVersion, workflowVersion, calendarVersion where supported
workspaceTimeZone, workingCalendarSummary
scheduledItems[], unscheduledItems[], milestones[]
dependencies[], warnings[], permissions

item:
  taskId, kind, parentTaskId, milestoneId, title
  plannedStartDate, plannedEndDate, milestoneDate
  progressPercent, progressIsDerived
  workflowStageId, workflowStageName, stageCategory
  priority, isBlocked, primaryAssignee
  version/etag, scheduleEditPermissions, warnings[]

dependency:
  dependencyId, predecessorTaskId, successorTaskId
  type, editable, version where required, warnings[]
```

Projection rules:

- use each authorized canonical WorkItem once;
- derive parent planned bounds and progress from direct canonical
  `WorkItemKind.Task` children only and deny direct parent edits;
- show Milestones as required-date, zero-duration items;
- place undated Tasks in `unscheduledItems` without inferred dates;
- exclude or safely reject cross-Project edges;
- show legacy non-FS edges as read-only warning inventory; and
- use bounded, set-based PostgreSQL queries with deterministic ordering and
  cancellation propagation.

The post-merge owner decision approves a temporary maximum of 500
items counted consistently as canonical Task-kind WorkItems plus canonical
Milestones, and 2,000 active dependencies whose endpoints are active
same-Project canonical Tasks. The same count gate applies to snapshot,
schedule, progress, and dependency operations. Overflow returns a typed HTTP
400 error (`GANTT_ITEM_LIMIT_EXCEEDED` or
`GANTT_DEPENDENCY_LIMIT_EXCEEDED`) before returning a partial graph. It never
silently truncates. The projection rechecks the combined item count after its
bounded reads to close a count-then-read race. These limits are not permanent
Project or database capacity limits; large-project delivery is deferred to
`TASK-V1-PR06B`.

## Manual commands

| Operation | Route | Required contract |
| --- | --- | --- |
| Schedule | `PATCH /api/tasks/{taskId}/schedule` | Required-but-nullable `plannedStartDate` and `plannedEndDate` keys, `milestoneDate` where applicable, and required `expectedVersion`; owns schedule fields only. |
| Progress | `PATCH /api/tasks/{taskId}/progress` | Required integer 0-100 and required version; omission is rejected and only leaf Task progress is directly editable. |
| Dependency add | `POST /api/tasks/{taskId}/dependencies` | Reuse PR02 authority; required predecessor and canonical string `dependencyType: "FinishToStart"`; same Project only. |
| Dependency remove | `DELETE /api/tasks/{taskId}/dependencies/{dependencyId}` | Reuse PR02 authority; remove only the edge and never move dates. |

Schedule rejects end-before-start and direct parent override. Clearing both
Task dates is valid and makes the Task unscheduled. A Milestone date is required
and its duration remains zero. The maintained compatibility Milestone update
contract also requires a positive `expectedVersion`, preserves the date
invariant, and maps a stale aggregate to the same safe HTTP 409/refetch
behavior.

Progress remains consistent with Workflow Stage, Review, completion, Done=100,
parent derivation, and Milestone 0/100 rules. Dependency writes reject self,
duplicate, cycle, cross-Project, non-FS, lag/lead, deleted, unknown-neighbor,
revoked-membership, and unauthorized requests without disclosing hidden data.
Unknown dependency request members are rejected, so lag/lead cannot be smuggled
into an otherwise valid Finish-to-Start command. Bounded dependency reads
include only active canonical Task neighbors. Visible rejected dependency
attempts produce metadata-safe audit records without recording hidden-neighbor
details. Dependency and Task-lifecycle transactions advance the shared Project
revision so a concurrent Task deletion cannot race a dependency add into a
committed invalid edge.

Canonical terminal-parent invariants remain shared with Task Detail and Kanban:

- a Done/Cancelled parent rejects new subtasks until explicitly reopened;
- a terminal child cannot reopen while its parent remains terminal;
- completing a parent as Done requires every direct canonical Task child to be
  terminal and canonical derived progress to equal 100; and
- an all-cancelled child set derives progress 0 and cannot complete the parent
  as Done.

Milestones and any non-Task compatibility rows do not participate in parent
Task derivation. Review override completion uses the same parent completion
guard, and restoring or deleting a child beneath a terminal parent is rejected
until the parent is reopened.

Stale versions return HTTP 409 and trigger authoritative refetch. After that
refetch, the safe preserved edit intent is explicit: the actor can Retry it
against the latest version or Discard it. Dependency violations and
active-work missing-end conditions are non-blocking warnings; they never
cascade dates.

## Authorization and safe errors

Snapshot visibility follows current Tenant, Workspace membership, and Project
visibility. Viewers receive read-only data. Schedule edits require
`project.gantt.edit` or the canonical equivalent; progress and dependency
capabilities are also backend-projected and rechecked on every command.
Feature flags and hidden controls are never authorization.

Archived/deleted Projects, revoked membership, cross-Tenant IDs, cross-Project
IDs, and unknown neighbors follow the established safe rejection policy. PR06
separates 401, 403, safe 404, 409, and validation failures using the repository
error envelope:

```text
requestId
error.code, error.message, error.target, error.details
error.redactionApplied
```

Responses exclude stack traces, SQL, Tenant internals, hidden titles or
neighbors, and raw exceptions.

Request cancellation continues to propagate as cancellation rather than being
rewritten as a server failure. An unexpected snapshot exception returns a safe
HTTP 500 envelope with `GANTT_REQUEST_FAILED`; unexpected command failures use
their Gantt/dependency command code.

## Date, calendar, warnings, and atomicity

Planning dates use `DateOnly` day precision and the canonical Workspace timezone
resolver. The browser timezone does not reinterpret stored Project dates.
`DeadlineAt` remains a separate UTC timestamp and is never changed implicitly
by a Gantt edit.

The current system has Workspace timezone persistence/fallback but no complete
working-day or holiday service. PR06 returns only an available canonical
summary/version and does not fabricate holidays, weekends, or a school
timetable. This limitation remains explicit in final verification.

Warnings are vendor-neutral and separate from blocking API errors:

```text
code, message, severity, targetType, targetId, field, blocking=false
```

Required codes are `DEPENDENCY_VIOLATION`,
`MISSING_ACTIVE_PLANNED_END`, `PARENT_DERIVED`,
`LEGACY_DEPENDENCY_TYPE`, `MILESTONE_DATE_REQUIRED`, and `UNSCHEDULED`.
A warning alone does not reject an otherwise valid command. Dependency-date
warnings use derived parent dates where an edge references a parent Task.

Every successful schedule, progress, dependency-add, and dependency-remove
mutation records a metadata-safe audit entry and inserts Task/Project
invalidations into the transactional Outbox. Mutation, audit, and Outbox commit
in one transaction; a failed save leaves no partial persistence. HTTP remains
authoritative and SignalR carries invalidation/version hints only.

Migration `20260730120626_AddCanonicalGanttVersions` adds only optimistic
concurrency tokens to Project and canonical Milestone aggregates. It performs
no Task progress or other data normalization. The additive Down path removes
only those two new columns and preserves existing Project, Milestone, Task, and
dependency rows.

## Angular adapter, rollout, and realtime

`CoglatasGanttContract` is extended with vendor-neutral items, dependencies,
calendar, edit intents/results, warnings, permissions, busy/focus/feedback, and
adapter states. Syncfusion records, events, enums, selectors, DOM contracts, and
models do not enter feature code, and the Gantt vendor bundle remains lazy.

The existing Schedule tab uses `tasks.ganttV1` as a presentation rollout flag.
Disabled mode keeps the maintained read-only compatibility schedule/list on the
same route. The enabled workflow supplies:

- loading, ready, empty, permission-denied, error, conflict, rollback, and
  degraded states;
- pointer and accessible keyboard/form paths that emit the same edit intent;
- optimistic presentation, rollback, conflict refetch, safe intent retention,
  and logical focus restoration;
- visible hierarchy, warnings, Blocked, priority, and status without color-only
  meaning;
- focus-trapped forms where Escape/Cancel sends no command;
- a 320 px ordered schedule/Milestone/unscheduled mobile projection; and
- manual refresh and HTTP editing while SignalR is unavailable.

Realtime Task, Stage, Blocked, dependency, hierarchy, Milestone, workflow,
membership, and authorization events invalidate by version. Duplicate/stale
events are coalesced; an active edit is not silently overwritten. Reconnect
reauthorizes before refetch, and revocation clears protected state before a
stale response can restore it.

If the Project realtime subscription is denied during reconnect, the facade
synchronously clears both protected Kanban and Gantt projections, increments
authorization/load/request generations to invalidate every in-flight response,
and only then starts authoritative HTTP revalidation.

## Explicit non-goals

- PR07 Notifications, digest, or realtime finalization
- PR08 integrated cutover
- automatic scheduling or cascading date movement
- Critical Path, baseline comparison, or Resource Leveling
- automatic workload balancing
- cross-Project dependencies or Task movement
- SS, FF, or SF authoring; dependency lag or lead
- personal or recurring Tasks
- Calendar/Scheduler/school-timetable or attendance features
- multiple Boards, timesheet inference, or portfolio management
- changing PR04 My Tasks into a Gantt view
- changing package/license policy
- merging PR06

## Historical merge-time decision record

At implementation and merge time, the canonical sources required a bounded
snapshot but defined neither its numeric maximum nor overflow behavior. The
owner had not yet selected the maximum WorkItem/Milestone/dependency graph size
or the overflow behavior.

The provisional implementation uses 500 items counted as canonical Task-kind
WorkItems plus canonical Milestones and 2,000 active dependencies whose
predecessor and successor are active same-Project canonical Task endpoints. It
applies the bounds consistently to snapshot and every PR06 command path,
rejects overflow with a typed HTTP 400 response, and does not return a
truncated snapshot. These values and semantics are implementation safety
limits, not a canonical owner decision at merge time.

- Source: implementation safeguard; the canonical sources require only a
  bounded snapshot and provide no numeric value or overflow contract
- PR body/comments: no owner decision found
- Resolved: No
- DECISION REQUIRED: Yes

This is the historical merge-time record. The 2026-08-01 post-merge owner
decision above resolves the temporary PR06 contract prospectively without
changing this historical fact.

## Historical candidate evidence

The pre-remediation candidate
`ab9a260dd4517d34a2500d5e76369ba241b504ee` passed PR06 45/45, PR05
25/25, PR04 8/8, full backend 485/485, Angular 318/318, and mocked Playwright
63 passed with 3 expected skips. Its npm audit reported 18 findings. It and
the earlier Hosted candidates are retained in the detailed verification
ledger, but none is final Acceptance evidence after latest-main integration.
Code Quality attempt 1 was cancelled. In attempt 2, Qodana job `91089891268`
failed after runner shutdown and produced no artifacts, the PR Gate was
skipped, and Angular quality job `91090268692` succeeded; the workflow is
therefore not a pass.
Parent candidate `555379db03d076627f04083a43eb07fe7ffa23bc` later passed the
full local suites and Documentation CI, CI, Code Quality attempt 3, and npm
Security Audit; Qodana found 0 material PR06 and 0 material PR-introduced findings. Its
Real Backend run `30611459543` failed before login with six infrastructure
errors after an internal HSTS 307 upgraded `http://app:8080` to unsupported
`https://app:8080`. It executed 0 PR06 steps/commands and found 0
high-confidence secret matches. These results are historical after the
focused origin remediation.

The focused-origin candidate
`e0e87dd9b4933af8165e472cc02761db0ff3ab6e` passed Documentation CI
`30612005927`, CI `30612006065` attempt 2, Code Quality `30612006010`, and npm
Security Audit `30612006220`. Qodana again reported 0 error, 0 critical,
0 material PR06, and 0 material PR-introduced findings. Its Real Backend run
`30625754075`, job `91140507111`, was cancelled at step 0 and produced no
executable evidence, so it is not a Real Backend pass. These results are also
historical after latest-main merge `0b2d5fc` and lock-only fix `69cc6f0`.

Latest-main parent candidate
`69cc6f0943cfc9d3e2dab358edceb0fad0a0fea6` passed Documentation CI
`30626428426`, CI `30626428493` attempt 4, Code Quality `30626428491`
attempt 3, and npm Security Audit `30626428487`. Qodana reported 2,260
inventory findings (1,421 warning, 839 note, 0 error, 0 critical), 0 unresolved
model findings/failures, 0 material PR06 findings, and 0 material PR-introduced findings.
Its Real Backend run `30630832231`, job `91156526050`, reached the PR05/PR06
revocation paths but failed two evidence assertions: JUnit 6 total, 4 passed,
2 failed, 0 errors, 0 skipped. The PR05 assertion expected obsolete board-not-found text
after the UI had safely cleared protected state; the PR06 assertion had not
registered the exact safe `GET /api/projects/{projectId}` HTTP 400 denial for
console reconciliation. Artifact `8793522897` has digest
`sha256:79841bfa974edfee464256d5415165f53a606eab760e9c2c2887aaa95115033c`,
and high-confidence secret matches were 0. Test-only `f2d3805` now records and
validates that exact method/path/status, its safe redacted body, and the current
authorization-clear status. It neither treats arbitrary 400 responses as
expected nor adds a retry or timeout.

## Historical pre-2026-08-01 candidate status

The entries in this section preserve the earlier `69cc6f0`, `f2d3805`, and
`2fc5910` evidence ledger. They are not current final-remediation status after
latest-main merge `08056ee` and scope cleanup `e8bdf47`.

- Implementation: exact code-bearing candidate
  `2fc5910e772f427355529de6e500b093583872b6`; latest main
  `1739cfcc819174289d858cbacc255527f1ffa047` incorporated without conflict by
  normal merge `0b2d5fc1e99d441e278be1716b9fbb8baed96e90`, followed by lock-only `tar`
  fix `69cc6f0`, test-only Real Backend evidence remediation `f2d3805`, and
  Real Backend cache-remediation `2fc5910`; ahead/behind 22/0 before the final
  documentation commit
- HSTS-origin remediation inherited from `e0e87dd9`: the smoke host uses non-HSTS Compose alias
  `http://coglatas-backend:8080` with a fail-closed origin guard, focused test, and
  documentation. No timeout increase or retry was added. Runner helper tests
  passed 6/6; Node syntax, Compose config/alias, and diff checks passed.
- Toolchain: local Node `v24.13.0`, npm `11.6.2`, repository
  active `frontend/package.json` `packageManager` `npm@11.17.0` on the same
  major, and latest-main .NET test tooling `10.0.10`
- Exact product-code parent `69cc6f0` root `npm ci`: passed
- Exact product-code parent `69cc6f0` active-frontend
  `npm --prefix frontend ci`: passed
- Exact product-code parent `69cc6f0` Release build: passed
- Exact product-code parent `69cc6f0` migration: PostgreSQL 18.4 empty apply,
  PR05 upgrade, existing-data
  preservation, additive down, and migration list passed; pending migrations
  and pending model changes: none
- Exact product-code parent `69cc6f0` backend focused
  PostgreSQL/HTTP/service/controller tests:
  `Scope=TaskV1PR06` 49/49 passed, 0 failed, 0 skipped
- Exact product-code parent `69cc6f0` PostgreSQL query evidence: seven commands
  for the bounded repository
  projection and exactly 24 commands for the authorized real HTTP snapshot;
  exact SQL is emitted in xUnit evidence, with no N+1/unbounded graph load
- Exact product-code parent `69cc6f0` PR05 regression: 25/25 passed, 0 failed,
  0 skipped; PR04 regression: 8/8 passed, 0 failed, 0 skipped
- Exact product-code parent `69cc6f0` full backend: 494/494 passed, 0 failed,
  0 skipped
- Exact test-only candidate `f2d3805` PostgreSQL-enabled rerun: PR06 49/49,
  PR05 25/25, PR04 8/8, and full backend 494/494 passed with 0 failed and 0
  skipped. A preceding no-PostgreSQL run is not Acceptance evidence.
- Exact current candidate `2fc5910` PostgreSQL-enabled rerun after explicit
  empty-database migration apply: PR06 49/49, PR05 25/25, PR04 8/8, and full
  backend 494/494 passed with 0 failed and 0 skipped. Release build was 0
  warnings / 0 errors; the latest migration is
  `20260730120626_AddCanonicalGanttVersions`, and pending model changes are 0.
- Exact product-code parent `69cc6f0` package audit: latest main had
  active-frontend `tar` 7.5.19;
  lock-only commit `69cc6f0` restores 7.5.22 by changing only version,
  resolved URL, and integrity. Active-frontend audit is 19 findings (3 low,
  6 moderate, 10 high, 0 critical) versus latest-main 20 (3 low, 7 moderate,
  10 high, 0 critical); root audit is 0. No affected path contains Syncfusion,
  so PR06-introduced findings are 0. The 19 active findings are 5 direct
  (`@angular-devkit/build-angular`, `@angular/build`, `@angular/cli`,
  `@angular/compiler-cli`, and `@storybook/angular`) and 14 transitive. All
  report a `fixAvailable` path, but those paths include major or inconsistent
  downgrade candidates; no forced fix is authorized.
- Exact product-code parent `69cc6f0` Angular full suite: 42 files and 323/323
  tests passed; production build
  succeeded
- Exact product-code parent `69cc6f0` architecture: application check succeeded
  and Node architecture tests
  passed 4/4
- Exact product-code parent `69cc6f0` Syncfusion license policy: 4/4 passed
- Exact product-code parent `69cc6f0` bundle analysis: succeeded; Gantt remained
  lazy at approximately 5.42 MB
  and the initial bundle was 949.99 kB with its existing budget warning
- Exact product-code parent `69cc6f0` Storybook default: exit 134 after local
  2 GB heap exhaustion, not a pass
- Exact product-code parent `69cc6f0` Storybook 4 GB: passed with
  `NODE_OPTIONS=--max-old-space-size=4096`
- Exact product-code parent `69cc6f0` mocked Playwright: 63 passed, 0 failed,
  with 3 pre-existing explicitly expected skips; PR06 Schedule desktop/mobile
  subset 4/4 passed with no skips
- Historical `e0e87dd9` Hosted evidence: Documentation CI run `30612005927` /
  job `91096678127`, CI run `30612006065` attempt 2 / jobs `91138599652`,
  `91138599781`, and `91138599518`, Code Quality run `30612006010` /
  Qodana job `91096678715` / Angular quality job `91100159546`, and npm
  Security Audit run `30612006220` / job `91096678735` all succeeded
- Historical `e0e87dd9` Qodana triage: 2,260 inventory findings (1,421 warning, 839 note,
  0 error, 0 critical), short report 0, model unresolved/failures 0, material
  PR06 findings 0, and material PR-introduced findings 0. The remaining disposed
  closure/resource warnings are test-only; the multiple-enumeration,
  identical-ternary, and redundant-assignment findings are base findings, and
  `PlanningRepository` entries are non-material `Contains` notes.
- Historical `e0e87dd9` Real Backend: run `30625754075`, job `91140507111`,
  cancelled at step 0 with no executable evidence; not a pass
- Earlier `555379db` Real Backend: run `30611459543`, job `91094951966`,
  failed before login; JUnit 6 total, 0 passed, 0 assertion failures, 6 errors,
  0 skipped. Artifact `8785696348` digest
  `sha256:e5a102f3263a296f31ce2cf00853800d47d04d5b21a7816a69f32995031f092d`.
- Exact product-code parent `69cc6f0` Documentation CI: run `30626428426`, job
  `91142659554`, success
- Exact product-code parent `69cc6f0` npm Security Audit: run `30626428487`,
  job `91142659450`,
  success; artifact `8791530869`, digest
  `sha256:531d8a91339eda02af574db4bdab1c054c04ece33fcc3c725f40d1c90736af46`
- Exact product-code parent `69cc6f0` CI: run `30626428493` attempt 4 succeeded;
  build-test job `91146858583`, security-scan job `91146858952`, and
  frontend-test job `91146858537` all succeeded. Hosted backend was 494/494;
  Hosted frontend included Angular 323/323, production/license builds,
  raised-heap Storybook, and mocked Playwright 63 passed with 3 expected skips.
- Exact product-code parent `69cc6f0` Code Quality: run `30626428491` attempt 3
  succeeded; Qodana job `91149477336` and Angular quality job `91152690654`
  succeeded. Inventory was 2,260 findings (1,421 warning, 839 note, 0 error,
  0 critical), short report 0, model unresolved/failures 0, material PR06
  findings 0, and material PR-introduced findings 0.
- Exact product-code parent `69cc6f0` Real Backend: run `30630832231`, job
  `91156526050`, failed with JUnit 6 total, 4 passed, 2 failed, 0 errors,
  0 skipped.
  Artifact `8793522897`, digest
  `sha256:79841bfa974edfee464256d5415165f53a606eab760e9c2c2887aaa95115033c`;
  high-confidence secret matches 0. The two deterministic evidence mismatches
  are remediated by test-only `f2d3805` as described above.
- Exact `f2d3805` Documentation CI `30632549237` / job `91162128837`, CI
  `30632549234` / build-test `91162129484`, security-scan `91162129549`, and
  frontend-test `91163476861`, Code Quality `30632549238` / Qodana
  `91162129048` and Angular quality `91164686596`, and npm Security Audit
  `30632549183` / job `91162128341` all succeeded. Hosted backend was 494/494;
  Angular was 323/323, raised-heap Storybook succeeded, and mocked Playwright
  was 63 passed with 3 expected skips. Qodana remained 2,260 findings (1,421
  warning, 839 note, 0 error, 0 critical), short report 0, model
  unresolved/failures 0, and material PR06 findings 0. npm remained 19 active
  findings (3 low, 6 moderate, 10 high, 0 critical).
- First exact `f2d3805` Real Backend run `30632559051`, job `91162166864`, was
  cancelled before setup and produced no artifact. Fresh run `30634069147`,
  job `91167131007`, completed the smoke step with 6/6 scenarios passed, 0
  failed/skipped, all 30 PR06 steps recorded, and 0 high-confidence secret
  matches. Artifact `8794673197` has digest
  `sha256:660fbe4b8eafc0f967f4fa9ae7915f47b0a01951f16b3634d15d986e665ba814`.
  The smoke step and artifact upload succeeded, but the workflow ultimately
  timed out and was cancelled during setup-node post-cache upload. It remains
  historical executable evidence, not a Gate pass.
- Exact `2fc5910` local frontend: `npm --prefix frontend ci` passed; Angular 327/327 in 42 files,
  production build, architecture 4/4, license safeguards 4/4, lazy-bundle
  analysis, 4 GB Storybook, and mocked Playwright 63 passed plus 3 expected
  skips succeeded. Default 2 GB Storybook OOMed and is not a pass. Initial
  bundle was 949.99 kB / 177.69 kB transfer; Gantt remained lazy at 5.42 MB /
  721.84 kB transfer.
- Exact `2fc5910` Documentation CI `30637433566` / job `91178483260`, CI
  `30637433590` / build-test `91178483276`, security-scan `91178483339`, and
  frontend-test `91180641392`, Code Quality `30637433561` / Qodana
  `91178484006` and Angular quality `91183270978`, and npm Security Audit
  `30637433551` / job `91178483146` all succeeded. Hosted backend was 494/494,
  Angular was 327/327, Storybook succeeded, and mocked Playwright was 63 passed
  plus 3 expected skips. Qodana Critical/Error/material PR06/material PR-introduced were
  0/0/0/0. Active frontend npm audit was 19 (3 low, 6 moderate, 10 high,
  0 critical), Syncfusion/PR06 introduced 0.
- Exact `2fc5910` Real Backend run `30639800642`, job `91186533535`, completed
  success: JUnit 6/6, failed/errors/skipped 0, PR06 30 evidence steps and 9
  commands, API interception `none`, real cookie/CSRF/ASP.NET Core/PostgreSQL,
  schedule/progress/dependency/stale conflict/revocation/reload/degraded HTTP,
  and page errors 0. Artifact `8797054160`, digest
  `sha256:75315d1f961c6865fa2f25debbb23754b158ca2b6ea8e4a9995843b95b9398b8`;
  artifact Gitleaks matches 0.
- Scope contamination: commit `9f7b8f3` committed the pre-existing user-owned
  Messaging facade/test changes (334 added lines) together with documentation.
  The reconnect catch-up/authorization work is outside PR06/within prohibited
  PR07 scope. This remediation preserves it unchanged and records it as a
  Merge blocker rather than claiming it as PR06 work.
- Package lock: focused review passed; lockfile version 3 and latest-main
  compatible dependency updates are retained. The intentional PR delta is the
  Syncfusion family plus the lock-only `tar` 7.5.19-to-7.5.22 security fix; no
  unrelated downgrade, local path dependency, registry churn, or license
  material was found
- Numeric bounds: provisional 500 canonical Task-kind WorkItems plus
  Milestones and 2,000 active dependencies with active same-Project canonical
  Task endpoints, typed HTTP 400 and no truncation; source is an implementation
  safeguard because canonical authority says only bounded; owner decision was
  not found in the PR body/comments, `Resolved: No`, `DECISION REQUIRED: Yes`
- Post-documentation exact-final-HEAD Documentation CI, CI, Code Quality, npm
  Security Audit, and Real Backend reruns: Pending
- Exact current `2fc5910` review check: 0 unresolved threads;
  post-documentation final-HEAD review-thread recheck remains Pending
- Draft PR: #259 open and mergeable; Draft remains enabled
- TASK-V1-PR06 acceptance: Incomplete
- PR #259 Merge: No-Go; merge performed: No
- TASK-V1-PR07: No-Go pending PR06 merge and post-merge audit
- PR08: No-Go

## Historical final-remediation status and post-merge correction

- Repository / PR / branch: `NYGsatoshi/Coglatas`, PR #259,
  `task/v1-pr06-gantt-adapter`
- Audit start: PR HEAD `e9519724506010e643e72837ea83aa9801f33194`;
  main `33c35cbc873fcdc78b75663d195ca120e2c01520`
- Latest main: `33c35cbc873fcdc78b75663d195ca120e2c01520`, merged normally as
  `1abce6c70d9f665b773d35f75d63c0d05a387cc8`; the only conflicts were the
  active frontend package manifest and lockfile, resolved by retaining Gantt
  34.1.30 and main's Grid 34.1.33
- Main integration retained Angular 21/@angular-devkit architect compatibility,
  CI heavy queue v2 and `queue: max`, Qodana/manual-smoke queue v2,
  Compodoc 2.0.0, ESLint 10.8.0, globals 17.8.0, lockfile version 3, and
  latest test tooling
- Messaging cleanup: forward commit
  `e8bdf47754ca38b6f4d1b3a31c945ae07432f06f`; both prohibited Messaging
  files are absent from `origin/main...HEAD`
- Backup patch SHA-256:
  `1099E128C2BBBE43D986C29427F82F2CBDB14371320FEC00E1B402DF628844DD`
- Exact code-bearing HEAD:
  `1abce6c70d9f665b773d35f75d63c0d05a387cc8`; inspection metadata fallback
  remediation `8efa845dec5c553d5ff2107cf6edef7993141a8b`; ahead/behind 29/0 before
  this documentation commit
- Release build: passed, 0 warnings / 0 errors
- PostgreSQL 18.4: empty migration apply, PR05 upgrade/data preservation,
  additive Down, migration list, and pending-model check passed; latest
  migration `20260730120626_AddCanonicalGanttVersions`
- Backend: PR06 49/49, PR05 25/25, PR04 8/8, full backend 494/494;
  failed 0, skipped 0
- Frontend: root/active/inactive `npm ci` passed; the inspection workspace has
  no tracked lockfile, so its requested `npm ci` was structurally unavailable;
  the repository's no-lock install path and full inventory succeeded. Angular
  323/323 in 42 files;
  production build, architecture 4/4, Syncfusion license safeguards 4/4,
  bundle analysis, 4 GB Storybook, and mocked Playwright 63 passed with 3
  pre-existing expected skips succeeded
- Default Storybook: failed with the local approximately 2 GB JavaScript heap
  OOM and is not a pass; no timeout/retry change was made
- Bundle: initial 949.99 kB; Syncfusion Gantt 5.42 MB lazy chunk and absent
  from the initial bundle
- Toolchain: Node `v24.13.0`, npm `11.6.2`; Compodoc 2.0.0 executed, while
  its nested `@angular-devkit/core` 22.0.4 requires Node `^24.15.0`, so the
  repository Hosted Node 24 toolchain remains authoritative for that Gate
- npm audit: root 0; active 15 (3 low, 6 moderate, 6 high, 0 critical);
  inactive 8 (0 low, 5 moderate, 3 high, 0 critical); Syncfusion affected 0
- Exact-head attempt `5111784e72054db9501135888e72330672a8c975` is not a
  Code Quality pass: Qodana succeeded with 2,260 findings, 0 error, 0 critical,
  and short report 0, but Angular quality failed at the inspection install with
  `eslint@undefined` / `ERESOLVE`; downstream quality steps were skipped.
  Commit `8efa845` changes only the second install attempt to refresh online
  metadata. The equivalent local online install and inspection inventory pass.
- Historical numerical decision at merge: `UNRESOLVED`; preserved above and
  in the PR body
- Post-merge temporary decision: Resolved — 500 / 2,000 / typed HTTP 400,
  fail closed, no truncation, no partial graph
- PR #259: Merged at `d5de01cf303c914c2b390346575a22cadb8b4443`
- Large-project Gantt: Open / Deferred to `TASK-V1-PR06B`
- PR07 or later work must incorporate the latest corrected `main` before merge
  and does not inherit responsibility for PR06B unless the owner changes scope
