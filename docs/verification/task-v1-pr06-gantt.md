# TASK-V1-PR06 Canonical Gantt implementation and verification ledger

This document records the implementation-start audit for TASK-V1-PR06. It is
also the living implementation and verification ledger. The initial
classification is retained as evidence that the specification/current-main
comparison happened before the first commit. Any item marked `Pending`,
`Not run`, `Unverified`, or qualified as pre-final-HEAD must not be treated as
passing.

## Post-merge correction — 2026-08-01

### Historical merge-time state

PR #259 was merged at
`d5de01cf303c914c2b390346575a22cadb8b4443` before the numerical owner
decision was formally recorded. The retained `Draft`, `Open`, `Acceptance:
Incomplete`, `Merge: No-Go`, `Merge performed: No`, and `UNRESOLVED` entries
below are historical merge-time evidence. They are not the current status and
have not been erased or rewritten.

### Post-merge owner decision

After the merge, the owner approved the existing implementation safeguards as
the temporary PR06 full-snapshot contract:

- maximum 500 combined canonical Task-kind WorkItems and Milestones;
- maximum 2,000 active dependencies with active canonical Task endpoints in
  the same Project;
- repository-standard typed HTTP 400 on overflow;
- fail closed, with no silent truncation, partial item set, partial dependency
  graph, or successful partial snapshot.

### Current contract

The server MUST reject the complete snapshot request when either temporary
limit is exceeded. It MUST NOT silently truncate items or dependencies and
MUST NOT return a successful partial snapshot. These values are temporary
safety limits for the PR06 full-snapshot implementation, not permanent Project
capacity limits, database storage limits, or general-availability scalability
guarantees.

### Follow-up scalability work

PR06 is merged and its temporary overflow contract is resolved post-merge.
Paginated and virtualized large-project Gantt support remains incomplete and
is tracked separately in
[`TASK-V1-PR06B` issue #270](https://github.com/NYGsatoshi/Coglatas/issues/270).

## Scope and stopping point

PR06 upgrades the existing Project Detail Schedule tab and
`GET /api/projects/{projectId}/gantt` around the shared canonical WorkItem
model. It must not replace that model with a Gantt-owned database, route, or
vendor model.

This work stops after the PR06 Go/No-Go report. It does not include:

- PR07 notification, digest, or realtime finalization
- PR08 integrated cutover
- merge of the PR06 pull request
- automatic scheduling or cascading date movement
- Critical Path, baseline comparison, resource leveling, or automatic
  workload balancing

Commit `9f7b8f3b3826ca7c4c1352cba253e3a2ea9827cc`, created while this remediation
was in progress, committed pre-existing user-owned Messaging reconnect and
authorization changes together with documentation. Forward cleanup commit
`e8bdf47754ca38b6f4d1b3a31c945ae07432f06f` restored both affected files to
actual `origin/main`; neither file remains in the PR diff. No PR07 behavior was
reimplemented in this PR.

## Start identity

| Field | Initial value |
| --- | --- |
| Repository | `NYGsatoshi/Coglatas` |
| Branch | `task/v1-pr06-gantt-adapter` |
| Main start SHA | `0a8a1f58b8365e9ffc54daafceca99864ae5f63f` |
| Branch start SHA | `0a8a1f58b8365e9ffc54daafceca99864ae5f63f` |
| PR05 final PR HEAD | `5817fefda7e6ff70fe617c4ebdbac87ace8fe5f9` |
| PR05 merge commit | `0a8a1f58b8365e9ffc54daafceca99864ae5f63f` |
| Canonical specification revision | `20aa5a2e015ae8fb68e5ba2b257a416dfcad5c3f` |
| PR06 prompt blob | `a18719099d363a02fc0e9ea877a1909ff7d29daf` |
| PR06 prompt introducing commit | `b4fd4d6301a9c0d3197d921118e25d7bf8833f73` |
| Historical implementation candidate HEAD | `ab9a260dd4517d34a2500d5e76369ba241b504ee` |
| Final documentation HEAD | Pending |
| PR number | `#259` |
| Draft PR | Open |
| Mergeable | Pending |
| Merged | No |
| Merge performed | No |

The specification worktree was inspected at the revision above. It remained a
reference source only and is not an intended PR06 change.

## Historical pre-2026-08-01 remediation identity

This table preserves the earlier `2fc5910` candidate audit. It is historical
and is not exact-final-HEAD evidence after the 2026-08-01 latest-main merge and
Messaging scope cleanup.

| Field | Actual value |
| --- | --- |
| Audit start PR HEAD | `3cf2e1dc8c1c94ce475fb8f52f5461391f69cbd1` |
| Audit start main HEAD | `4c36baf95d1b9f80cab9b28c236bbaa1cb490346` |
| Latest incorporated main HEAD | `1739cfcc819174289d858cbacc255527f1ffa047` |
| Main merge commits | `663b0f452093360b55d31d2c56b32cbeeb887a2f`, `7838173a318c5819353007b88c2bec52896d48bf`, `555379db03d076627f04083a43eb07fe7ffa23bc`, and current merge `0b2d5fc1e99d441e278be1716b9fbb8baed96e90` |
| Merge method | Normal `--no-ff` merge commits; no rebase, force-push, reset, or history rewrite |
| Current main merge conflicts | None; package/lock and test-tooling changes were audited after the merge |
| Code-bearing candidate HEAD | `2fc5910e772f427355529de6e500b093583872b6` |
| Product-code/package parent candidate | `69cc6f0943cfc9d3e2dab358edceb0fad0a0fea6` |
| Test-only Real Backend evidence remediation | `f2d3805466b2a9bce3e2a7bf8392069330a1d6fd` |
| Unrelated Messaging/documentation commit | `9f7b8f3b3826ca7c4c1352cba253e3a2ea9827cc` |
| Real Backend cache-remediation commit | `2fc5910e772f427355529de6e500b093583872b6` |
| Documentation-bearing final HEAD | Pending this ledger update |
| Ahead / behind | 22 / 0 before the final documentation commit |
| Draft PR | Yes |
| Mergeable | Yes |
| Merged | No |
| Merge performed | No |

The exact documentation-bearing commit cannot embed its own SHA or the run IDs
created after that commit without creating another documentation HEAD. The
post-documentation exact-HEAD SHA and run IDs will therefore be recorded in the
PR body and final Acceptance report without changing the source tree again.

## 2026-08-01 final Acceptance remediation

This is the current identity and local evidence record. The documentation commit
created from this ledger becomes the final HEAD; Hosted run IDs produced after
that commit are recorded in the PR body and final report without another
self-referential commit.

| Field | Current value |
| --- | --- |
| Repository / PR / branch | `NYGsatoshi/Coglatas` / `#259` / `task/v1-pr06-gantt-adapter` |
| Audit start PR HEAD | `e9519724506010e643e72837ea83aa9801f33194` |
| Audit start main / actual latest main | `33c35cbc873fcdc78b75663d195ca120e2c01520` |
| Main merge commit | `1abce6c70d9f665b773d35f75d63c0d05a387cc8` |
| Merge method / conflicts | Normal `--no-ff` merge / active frontend package manifest and lockfile; manually reconciled without unconditional ours/theirs |
| Scope cleanup / code-bearing HEAD | Messaging cleanup remains `e8bdf47754ca38b6f4d1b3a31c945ae07432f06f`; latest-main code-bearing HEAD is `1abce6c70d9f665b773d35f75d63c0d05a387cc8` |
| Inspection metadata fallback remediation | `8efa845dec5c553d5ff2107cf6edef7993141a8b` |
| Messaging backup patch SHA-256 | `1099E128C2BBBE43D986C29427F82F2CBDB14371320FEC00E1B402DF628844DD` |
| Messaging facade / test PR diff | None / none |
| Ahead / behind before documentation commit | 29 / 0 |
| Draft / mergeable / merged | Yes / Yes / No |
| Documentation-bearing final HEAD | Pending this ledger commit |

Main integration retained Angular 21 and `@angular-devkit/architect`
compatibility, CI heavy queue v2 and `queue: max`, manual-smoke and Qodana
queue v2, Compodoc 2.0.0, Grid 34.1.33, ESLint 10.8.0, globals 17.8.0,
lockfile version 3, latest-main security updates, and test tooling. The two
package conflicts were reconciled by retaining PR06 Gantt 34.1.30 and main's
Grid 34.1.33. No unrelated downgrade, registry churn, local path dependency,
or license material was introduced.

Exact code-bearing HEAD `1abce6c70d9f665b773d35f75d63c0d05a387cc8`
produced the following local evidence:

- .NET restore and Release build passed with 0 warnings and 0 errors.
- PostgreSQL 18.4 passed empty apply through
  `20260730120626_AddCanonicalGanttVersions`, PR05 upgrade with
  Project/Milestone/Task/dependency preservation and VersionNo initialization,
  additive Down coverage, migration list, and no pending model changes.
- Backend PR06 49/49, PR05 25/25, PR04 8/8, and full backend 494/494 passed;
  failed 0 and skipped 0.
- Root, active-frontend, and inactive-frontend `npm ci` passed. Actual
  `origin/main` tracks no inspection-workspace lockfile, so the requested
  `npm --prefix tools/frontend-inspections ci` cannot run; the repository's
  documented no-lock install and full inventory succeeded after online
  registry metadata resolved main's newly published packages. Angular passed
  323/323 in 42 files. Production build, architecture checks (4/4), Syncfusion
  license safeguards (4/4), bundle analysis, 4 GB Storybook, and mocked
  Playwright (63 passed, 3 pre-existing expected skips) succeeded.
- Default-heap Storybook failed with an approximately 2 GB JavaScript heap OOM
  and is not a pass. No timeout, retry, package downgrade, or test weakening was
  used. The Gantt vendor chunk remained lazy at 5.42 MB; the initial bundle was
  949.99 kB.
- Local Node was `v24.13.0` with npm `11.6.2`. Compodoc 2.0.0 executed, but its
  nested `@angular-devkit/core` 22.0.4 declares Node `^24.15.0`; the inactive
  install warning is recorded and the repository-specified Hosted Node 24
  toolchain remains the Acceptance Gate.
- npm audit totals were root 0; active frontend 15 (3 low, 6 moderate, 6 high,
  0 critical); inactive frontend 8 (0 low, 5 moderate, 3 high, 0 critical).
  Syncfusion-affected entries were 0 and no forced fix was run.

Owner decision input was explicitly unresolved at merge time:

| Decision | Owner input | Current implementation safeguard |
| --- | --- | --- |
| WorkItem/Milestone cap | `UNRESOLVED` | 500 combined canonical items |
| Dependency cap | `UNRESOLVED` | 2,000 active same-Project dependencies with active Task endpoints |
| Overflow behavior | `UNRESOLVED` | typed HTTP 400, fail closed, no truncation, no partial graph |
| Owner source | Current TASK-V1-PR06 final-remediation request | Canonical spec revision `20aa5a2e015ae8fb68e5ba2b257a416dfcad5c3f` supplies no numbers or overflow contract |
| Resolved / DECISION REQUIRED | No / Yes | Safeguards are not formalized as the product contract |

The post-merge owner decision recorded at the top of this ledger resolves the
temporary PR06 contract prospectively; it does not change this merge-time
record.

Exact-final-HEAD Documentation CI, CI, Code Quality, npm Security Audit,
licensed Real Backend Browser Smoke, artifact secret scan, review-thread
check, and PR-body synchronization remain pending until after this ledger is
committed and pushed.

Exact-head attempt `5111784e72054db9501135888e72330672a8c975` is retained as
diagnostic evidence, not a final Gate pass. Documentation CI `30673208377`, CI
`30673208386` (all three jobs), npm Security Audit `30673208382`, and licensed
Real Backend `30673224616` succeeded. Qodana job `91297960761` also succeeded
with 2,260 findings (1,421 warning, 839 note, 0 error, 0 critical), short
report 0, and model failures 0. Code Quality run `30673208389` failed because
Angular quality job `91305933712` repeated stale offline metadata resolution
for the lockfile-free inspection workspace (`eslint@undefined` / `ERESOLVE`),
then skipped its inventory/type/build/test steps. Focused commit `8efa845`
retains the existing cache-first attempt and makes only its second attempt
refresh online registry metadata; the equivalent local install and full
inventory passed. No dependency, lockfile, queue, Qodana-policy, or test change
was made. All Gates must rerun on the following documentation-bearing HEAD.

## Main post-PR05 Gate

All four required runs were completed `success` for the exact actual-main SHA
`0a8a1f58b8365e9ffc54daafceca99864ae5f63f`. These are kickoff gates only;
they are not evidence for the future PR06 final HEAD.

| Workflow | Event | Status | Conclusion | Head SHA | Run |
| --- | --- | --- | --- | --- | --- |
| Documentation CI | `push` | `completed` | `success` | `0a8a1f58b8365e9ffc54daafceca99864ae5f63f` | [30534541849](https://github.com/NYGsatoshi/Coglatas/actions/runs/30534541849) |
| CI | `push` | `completed` | `success` | `0a8a1f58b8365e9ffc54daafceca99864ae5f63f` | [30534541890](https://github.com/NYGsatoshi/Coglatas/actions/runs/30534541890) |
| Code Quality | `push` | `completed` | `success` | `0a8a1f58b8365e9ffc54daafceca99864ae5f63f` | [30534541948](https://github.com/NYGsatoshi/Coglatas/actions/runs/30534541948) |
| npm Security Audit | `push` | `completed` | `success` | `0a8a1f58b8365e9ffc54daafceca99864ae5f63f` | [30534542164](https://github.com/NYGsatoshi/Coglatas/actions/runs/30534542164) |

Initial kickoff result:

- TASK-V1-PR05 post-merge recovery: Complete
- TASK-V1-PR06 kickoff: Go

## Canonical sources audited

The implementation prompt at
`docs/specs/coglatas-core-v4/12-implementation-kickoff/task-v1-pr06-gantt-adapter-prompt.md`
in `NYGsatoshi/Coglatas-Spec` is the primary source.

The audit also covered:

- Task/work management and planning scope
- the closed Workspace Task Messaging owner decisions
- permission, data-access, and safe-error requirements
- EF Core/PostgreSQL design
- API error, realtime event, SignalR group, Outbox, and reconnect contracts
- Task/work planning API and owner-decision implementation mappings
- Task/work planning acceptance documents
- component adoption and frontend migration mappings
- implementation-side PR02 and PR05 task records and PR05 verification
- the current Planning application service, repository, controller, Project
  Detail facade, adapter contracts, feature flags, and realtime foundations

Where older event-name tables conflict with the newer catalog, the canonical
event family is `Projects.TaskChanged.v1` and `Projects.ProjectChanged.v1`.
All owner-decision items named by the PR06 prompt are closed; they are not
reopened here.

## Initial gap audit summary

### Complete

The following reusable foundations exist on the main start SHA:

- A shared canonical `TaskItem`/WorkItem source of truth exists. It includes
  WorkItem kind, parent relationship, canonical planned dates, progress,
  workflow stage, blocked state, primary assignee, and version fields.
- No separate Gantt-owned authoritative schedule or progress table exists.
- Canonical planning dates are represented as `DateOnly`; `DeadlineAt` remains
  a separate timestamp field.
- The shared parent-derivation utility can derive parent planned bounds and
  progress from canonical direct children.
- Workspace timezone persistence and the canonical
  `ITaskWorkspaceTimeZoneResolver` exist, with tenant timezone and UTC fallback.
- The existing route is `GET /api/projects/{projectId}/gantt`; no separate
  Gantt route is needed.
- Project Detail already owns the Schedule tab; no parallel route is needed.
- Canonical Task dependency persistence and existing add/remove routes exist.
  Their service already rejects non-FS authoring, self-dependency,
  cross-Project dependency, duplicate dependency, and cycles.
- Shared audit, Task invalidation, transactional command, feature-flag, and
  realtime/catch-up foundations exist and can be reused.

These foundation statements do not mean the PR06 projection, commands, UI, or
acceptance tests are complete.

### Partially complete

- The current Gantt snapshot performs project view authorization and returns
  project, milestone, task, assignment, and dependency data, but only through
  the legacy compact DTO.
- Deleted projects are filtered by the repository, but archived/read-only
  behavior is not expressed in the snapshot contract or Gantt command policy.
- The current repository projection is set-based. Its data path executes four
  queries when there are no Tasks and five when Tasks are present, because
  assignments are loaded in one batched query. This is a static code inventory,
  not an end-to-end measured query count.
- Task assignments are batched, and cancellation tokens are propagated through
  the current repository queries, but the result graph is unbounded.
- Canonical Task responses and PR02/PR05 commands already use version fields,
  but the Gantt snapshot does not expose the required project, workflow,
  calendar, Task, and dependency versions.
- The general `PATCH /api/tasks/{taskId}` Task-details command can currently
  write planned dates and progress with optimistic concurrency and shared
  parent validation. It owns unrelated fields as well, so it is not the
  schedule-only or progress-only PR06 contract.
- Existing Task commands use audit and transactional invalidation foundations.
  Existing dependency add/remove commands audit mutations, but their complete
  PR06 Outbox, safe-error, and version behavior is not established.
- A safe typed error envelope exists for newer Task commands, but the current
  Gantt endpoint and legacy dependency controller paths do not use it.
- The vendor-neutral adapter shell and adapter state vocabulary exist, but the
  Gantt contract contains only labels, milestones, a timezone string, and a
  global read-only flag.
- The existing semantic Schedule list is keyboard-readable, but it has no
  authorized editing forms, rollback/conflict workflow, dependency actions, or
  purpose-built mobile projection.
- Project Detail already has realtime invalidation, catch-up, stale-response,
  revocation-clear, and active-interaction patterns for PR05 Kanban. Those
  patterns are not wired to Schedule/Gantt.
- The central frontend feature-flag service exists, but `tasks.ganttV1` does
  not.

### Missing

- A bounded vendor-neutral Gantt snapshot contract with `scheduledItems`,
  `unscheduledItems`, milestones, dependencies, structured warnings, and
  permissions
- Project, workflow, and supported calendar version fields in the snapshot
- Workspace timezone and available working-calendar summary in the response
- Canonical WorkItem projection fields: kind, parent ID, canonical planned
  dates, milestone date, derived-progress marker, workflow-stage metadata,
  priority, blocked state, canonical primary assignee, version/etag, per-item
  schedule permissions, and warnings
- Explicit parent-derived bars and direct-edit denial
- Explicit unscheduled Task projection without guessed dates
- Milestone zero-duration projection and required-date enforcement
- Structured warning contracts for `DEPENDENCY_VIOLATION`,
  `MISSING_ACTIVE_PLANNED_END`, `PARENT_DERIVED`,
  `LEGACY_DEPENDENCY_TYPE`, `MILESTONE_DATE_REQUIRED`, and `UNSCHEDULED`
- Dependency editability, legacy non-FS read-only inventory, versions where
  required, and warning projection
- Dedicated `PATCH /api/tasks/{taskId}/schedule`
- A canonical schedule-only request with nullable dates and required
  `expectedVersion`
- Dedicated or safely reused `PATCH /api/tasks/{taskId}/progress` semantics
  that own only progress and enforce leaf/Milestone/completion rules
- Command responses that return non-blocking warnings without date cascade
- Gantt-specific authoritative-refetch results for stale conflicts
- Gantt command audit plus Task/Project invalidation in the same transaction
  for schedule, progress, and dependency mutations
- `tasks.ganttV1` rollout and maintained disabled compatibility projection
- A typed Project Detail Schedule view model; the current HTTP response is
  `unknown`
- Direct date editing, leaf progress editing, FS dependency add/remove,
  schedule clear, Milestone edit, and unscheduled movement
- Pointer and accessible form/grid alternatives feeding the same canonical
  edit intent
- Focus trap, cancel-without-command, logical focus restoration, and safe
  diagnostic retention
- Loading, ready, empty, permission-denied, error, conflict, rollback, and
  degraded behavior in the actual Schedule workflow
- Purpose-built 320 px mobile ordered schedule, Milestone, unscheduled,
  hierarchy, dependency, and warning sections
- Lazy Gantt vendor loading and PR06 architecture checks preventing vendor
  types and imports from leaking into feature code
- Schedule-specific realtime stale/dedup behavior, active-edit conflict,
  reconnect refetch, revocation clear, and degraded HTTP/manual refresh
- Focused `Scope=TaskV1PR06` backend tests
- PR06 Angular, adapter, architecture, accessibility, and mocked-browser tests
- A real Angular + ASP.NET Core + PostgreSQL + cookie/CSRF PR06 browser scenario
- PR06 final documentation, SQL/query evidence, hosted gates, Qodana triage,
  and review-thread evidence

### Incorrect

The following current behaviors conflict with the PR06 canonical target and
must be replaced only within PR06 scope:

- `PlanningController` maps ordinary Gantt failures to a generic HTTP 400
  instead of separating 401, 403, safe 404, 409, and validation errors.
- The snapshot uses legacy `startDate`/`dueDate` naming and omits canonical
  planning semantics.
- The current Gantt service computes "today" from UTC rather than the canonical
  Workspace timezone resolver.
- Milestone due date is nullable in the current snapshot, and the response does
  not state zero-duration semantics.
- Parent dates and progress are not projected from the shared derivation
  algorithm.
- Undated Tasks remain in one generic Task list instead of an explicit
  unscheduled section.
- The current Gantt Task DTO returns an assignee list instead of the canonical
  primary-assignee summary required by PR06.
- All dependency types are returned without editable/read-only classification
  or a `LEGACY_DEPENDENCY_TYPE` warning.
- The frontend parses the response from `unknown`, reduces Tasks to
  `{ id, label }`, displays `"project timezone unavailable from API"`, and
  hard-codes the Schedule projection as read-only.
- The current repository loads every matching project Task and dependency
  without a maximum result bound.
- Existing dependency controller paths use generic 400 mapping. The dependency
  delete route does not establish that the nested Task ID is part of the
  authorization/integrity decision.

### Unverified

These items require implementation or executable evidence before a status can
be assigned:

- exact archived-Project read-only and safe-rejection behavior
- revoked Workspace membership and authorization-revocation behavior
- cross-Tenant and cross-Project non-disclosure for every snapshot and command
- duplicate-row absence across canonical hierarchy and Milestone projections
- Project/workflow/calendar stale-version behavior
- complete leaf, parent, Stage, Review, Done, Cancelled, and Milestone progress
  rules under concurrent mutation
- atomic rollback of persistence, audit, and Outbox on a failed save
- preservation of `DeadlineAt` across every PR06 mutation
- generated PostgreSQL query count including authorization and timezone
  resolution
- generated SQL and any justified `EXPLAIN`
- empty migration apply, upgrade from PR05, additive down behavior, pending
  migrations, and pending model changes
- whether PR06 requires a new migration at all
- dark/light theme, density, reduced-motion, and 320 px behavior
- actual Gantt bundle laziness and initial-bundle impact
- exact final-HEAD local and hosted test results
- real-backend cookie-auth, CSRF, persistence, conflict, revocation, and
  SignalR-degraded behavior
- Qodana findings and unresolved review-thread count

No canonical working-day or holiday entity/service was found in the current
implementation. PR06 must return only the available canonical summary and
timezone information, must not invent holidays, and must record this limitation
in the final evidence.

### Out of scope

- PR07 Notifications, digest, or realtime finalization
- PR08 integrated cutover
- automatic scheduling or cascading movement of predecessor/successor dates
- Critical Path
- baseline snapshots or comparison
- Resource Leveling or automatic workload balancing
- cross-Project dependencies or Task movement
- SS, FF, or SF authoring
- dependency lag or lead
- personal or recurring Tasks
- Calendar/Scheduler/school-timetable or attendance behavior
- multiple Boards, timesheet inference, or portfolio management
- changing PR04 My Tasks into a Gantt view
- changing package/license policy

## Code-bearing candidate gap reconciliation

This section classifies exact code-bearing candidate
`2fc5910e772f427355529de6e500b093583872b6` after implementing the confirmed
PR06 deltas, correcting the internal real-backend smoke origin, incorporating
latest main, and retaining patched `tar` 7.5.22 with a lock-only follow-up. It
also includes test-only exact-safe-denial evidence remediation `f2d3805` after
the `69cc6f0` Real Backend candidate run and cache-only workflow remediation
`2fc5910`. Unrelated `9f7b8f3` Messaging contamination is not classified as
PR06 work. This section does not replace the kickoff classifications above or
claim final acceptance.

### Complete in the code-bearing candidate

- The existing `GET /api/projects/{projectId}/gantt` route returns one
  deterministic vendor-neutral projection over canonical Tasks, compatibility
  Milestones, and canonical `TaskDependency` records.
- The projection includes Project/Workflow versions, supported Calendar
  version (`null` when unsupported), actual Workspace timezone, explicit
  calendar limitations, scheduled/unscheduled collections, hierarchy,
  zero-duration Milestones, derived parent values, primary assignee summary,
  Stage/priority/Blocked state, warnings, row versions, and permissions.
- Parent derivation considers only direct, non-deleted canonical Task-kind
  children. Milestones and non-Task rows cannot turn a Task into a derived
  parent or affect its dates/progress.
- The repository rejects more than 500 items counted as canonical Task-kind
  WorkItems plus canonical Milestones before loading rows, and rejects more
  than 2,000 active same-Project dependencies whose endpoints are active
  canonical Tasks; it never emits a partial snapshot. The same item count gate
  is used by snapshot, schedule, progress, and dependency paths.
  It rechecks the combined bound after reading rows to close the
  count-then-read race. The owner approved those values after PR #259 merged as
  temporary PR06 full-snapshot safety limits.
- Schedule/progress commands own only their respective canonical fields,
  require optimistic versions, preserve `DeadlineAt`, allow Task schedule
  clear, enforce leaf/parent/Done/Cancelled/Milestone rules, and return
  non-blocking warnings without automatic date movement.
- The maintained compatibility Milestone update route requires a positive
  expected version, preserves the required-date invariant, and maps stale
  revisions to safe HTTP 409.
- Schedule JSON requires both nullable planned-date keys. Progress JSON rejects
  an omitted progress value. Dependency JSON requires predecessor and type,
  accepts canonical string `FinishToStart`, and rejects unknown members such as
  lag or lead.
- Existing PR02 dependency routes now require the successor version, author
  Finish-to-Start only, keep legacy non-FS rows read-only, reject unsafe
  neighbors/cycles/duplicates/self edges, and do not move dates. Bounded reads
  filter to active same-Project canonical Task endpoints, while parent-edge
  date warnings use canonical derived parent dates.
- Visible dependency rejections create metadata-safe audit records. Shared
  Project revision advancement makes a concurrent Task-delete/dependency-add
  race fail atomically; PostgreSQL evidence confirms no stale edge commits.
- Schedule, progress, dependency add/remove, audit entries, version advancement,
  and Task/Project Outbox invalidations share the command transaction. Failed
  saves clear tracked command state; PostgreSQL tests exercise atomic rollback.
- The shared terminal-parent invariant rejects subtask creation under a
  Done/Cancelled parent, rejects reopening a terminal child while its parent is
  terminal, and allows Done parent completion only when every direct canonical
  Task child is terminal and derived progress is 100. An all-cancelled child
  set derives 0 and is rejected. Review override completion applies the same
  guard, while child restore/delete beneath a terminal parent are rejected
  until the parent is reopened.
- PR06 routes map authentication, authorization, safe not-found, validation,
  and concurrency failures to typed safe envelopes rather than a generic 400.
- Request cancellation propagates. Unexpected snapshot exceptions return a
  safe HTTP 500 `GANTT_REQUEST_FAILED` envelope.
- `CoglatasGanttContract` and feature models are vendor-neutral. Syncfusion Gantt is
  lazy, and feature code contains no Syncfusion record/event/enum/selector
  contract.
- The existing Project Detail Schedule tab supplies permission-gated schedule,
  progress, dependency, clear/unscheduled, warning, accessible form, focus,
  rollback/conflict, mobile-list, manual-refresh, and compatibility-flag
  behavior without adding a route or parallel data store.
- After a 409 rollback and authoritative refetch, preserved safe intent is
  exposed through explicit Retry-against-latest and Discard actions.
- Schedule reconciliation treats HTTP as authoritative, coalesces stale or
  duplicate invalidations, queues refetch during an edit, reauthorizes after
  reconnect, clears protected state on revocation, and preserves HTTP actions
  in degraded realtime mode.
- A denied realtime Project subscription synchronously clears both Kanban and
  Gantt protected projections and advances authorization/load/request
  generations before authoritative HTTP revalidation, so stale in-flight
  responses cannot restore protected data.

### Partially complete

- The runtime has an authoritative Workspace timezone but no canonical
  working-day or holiday service. The response truthfully returns an empty
  `workingDays` collection, `holidaysAvailable=false`, `calendarVersion=null`,
  and limitations. It does not infer weekends or holidays.
- Backend, migration, Angular, architecture, license-policy, bundle,
  mocked-browser, and raised-heap Storybook commands have exact local evidence
  at product-code parent `69cc6f0943cfc9d3e2dab358edceb0fad0a0fea6`.
  Documentation CI, CI attempt 4, Code Quality attempt 3, and npm Security
  Audit also succeeded at that exact head. Its Real Backend run reached the
  PR05/PR06 revocation paths but failed two evidence assertions. Historical
  test-only candidate `f2d3805` remediates those exact assertions. Its
  PostgreSQL-enabled local regressions and ordinary Hosted Documentation CI,
  CI, Code Quality, and npm Security Audit all passed. A fresh licensed Real
  Backend run completed 6/6 scenarios and uploaded valid secret-clean evidence;
  its workflow ultimately timed out/cancelled during post-cache upload and is
  not a pass. Current `2fc5910` repeated the required local/Hosted gates and
  completed the licensed Real Backend workflow success. Default 2 GB local
  Storybook remains a separately recorded non-pass. The documentation-bearing
  exact-final-HEAD evidence is still required.
- Storybook succeeds only with an explicitly increased 4 GB Node heap in the
  local environment. The default command exited 134 after exhausting its 2 GB
  heap and is not counted as passing. Exact product-code parent source passed with
  4 GB; historical candidate `e0e87dd9` also passed the Hosted command.

### Missing

- Owner approval of the canonical numeric graph limits and overflow behavior
- Authorized forward correction for unrelated Messaging/PR07-scope changes
  committed by `9f7b8f3`
- Final documentation HEAD
- Licensed exact-final-HEAD real-backend browser evidence
- Post-documentation exact-final-HEAD CI, Code Quality, Documentation CI, npm
  audit, Real Backend artifact, and review-thread evidence

### Incorrect

- No known remaining PR06 contract divergence has been accepted as correct.
  Current review threads are 0, but the remaining executable Gates and final
  review recheck are pending, so this is not a final clean-review claim.

### Unverified

- Dark/light, density, and reduced-motion behavior beyond current unit/static
  browser assertions
- Post-documentation Qodana results and final unresolved review-thread count
- Every required Hosted Gate on the documentation-bearing exact final HEAD

### Out of scope

The intended kickoff out-of-scope list above remains unchanged. No PR08,
automatic scheduling, cascading movement, Critical Path, baseline, resource
leveling, cross-Project dependency, non-FS authoring, lag/lead, or Gantt-owned
persistence was added by this remediation. However, unrelated commit `9f7b8f3`
did add user-owned Messaging reconnect/authorization changes that fall within
the prohibited PR07 realtime-finalization scope. They are explicitly not
accepted as PR06 work and remain a branch-scope blocker.

## Detailed requirement matrix (historical main-start audit)

This matrix records the classification against the original main-start seam.
Its `Initial classification`, `Current-main evidence`, and requested PR06
delta columns are historical audit evidence, not the status of code-bearing
candidate `2fc5910e772f427355529de6e500b093583872b6`.

| Audit area | Initial classification | Current-main evidence | PR06 delta |
| --- | --- | --- | --- |
| Gantt snapshot | Partially complete | Existing authorized GET and compact DTO | Replace DTO/projection in place with bounded canonical contract |
| Project authorization | Partially complete | `CanViewProject` is invoked | Add typed permissions and command-side Gantt edit enforcement |
| Workspace membership | Unverified | Shared authorization foundation exists | Test active, revoked, and reauthorized actors |
| Project archive/read-only | Partially complete | Deleted rows are filtered | Define/test archive snapshot and mutation behavior |
| DTO versioning | Missing | Gantt DTO has no version fields | Project/workflow/calendar/row versions as supported |
| Workspace timezone | Partially complete | Workspace field and resolver exist | Use resolver and return explicit timezone |
| Working calendar | Unverified | No working-day/holiday service found | Return available summary only; do not fabricate |
| Task dates | Incorrect | Legacy start/due names in snapshot | Canonical `plannedStartDate`/`plannedEndDate` |
| Milestone dates | Incorrect | Nullable due date, no zero-duration marker | Required milestone date and zero duration |
| Parent derived dates | Partially complete | Shared calculator exists | Use it in snapshot; prohibit direct parent edit |
| Parent derived progress | Partially complete | Shared calculator exists | Project derived value and prohibit direct edit |
| Unscheduled Tasks | Missing | No separate list | Explicit deterministic unscheduled projection |
| FS dependencies | Partially complete | Persistence and core validation exist | Canonical version/error/Outbox/edit-result behavior |
| Legacy non-FS inventory | Missing | Returned without classification | Read-only plus structured warning |
| Structured warnings | Missing | No warning contract | Vendor-neutral warning collection and codes |
| Schedule permissions | Missing | No snapshot permission DTO | Project and per-item capabilities |
| Progress permissions | Missing | No Gantt permission DTO | Leaf/Milestone capabilities and backend enforcement |
| Optimistic concurrency | Partially complete | General Task command has version check | Focused command request/results and authoritative refetch |
| Audit | Partially complete | Shared Task/dependency audit foundations | Prove all PR06 mutations and rollback behavior |
| Outbox | Partially complete | Shared Task invalidation foundation exists | Prove Task/Project invalidations are transactional |
| HTTP error contract | Incorrect | Gantt/dependency paths collapse to 400 | Minimal PR06 typed safe envelope |
| Angular adapter | Partially complete | Vendor-neutral shell contract exists | Add canonical models/intents/results/state without vendor types |
| Accessible forms | Missing | Readable list only | Full keyboard/form parity with pointer actions |
| Mobile projection | Missing | No PR06 mobile workflow | Ordered lists and authorized actions at 320 px |
| Feature flag | Missing | Central flags exist; no Gantt flag | Add `tasks.ganttV1` as presentation rollout only |
| Realtime | Partially complete | PR05 patterns and infrastructure exist | Gantt invalidation/refetch/conflict/revocation handling |
| PostgreSQL query shape | Partially complete | Batched set queries, but unbounded | Bound graph; measure query count and capture SQL |
| Real Backend browser | Missing | No PR06 scenario found | Exact-final-HEAD real stack scenario without interception |

## Historical merge-time decision record

The canonical sources require the Gantt snapshot to be bounded and refer to a
canonical maximum, but the audited PR06 sources do not define:

1. the numeric maximum for WorkItems/milestones/dependencies, or
2. the required behavior when the maximum is exceeded (typed rejection,
   explicit truncation metadata, or pagination).

PR06 must not silently guess dates or silently truncate a graph. The owner must
select the numeric cap and overflow contract. This is the only independent
specification omission found in the initial audit; implementation of all other
confirmed requirements can continue.

Interim implementation:

- maximum 500 items counted as canonical Task-kind WorkItems plus canonical
  Milestones, consistently across snapshot, schedule, progress, and dependency
  paths;
- maximum 2,000 active dependencies whose predecessor and successor are
  active, same-Project canonical Task endpoints;
- typed HTTP 400 rejection (`GANTT_ITEM_LIMIT_EXCEEDED` or
  `GANTT_DEPENDENCY_LIMIT_EXCEEDED`) when either limit is exceeded; and
- no truncation, inferred dates, or partial graph.

At merge time these were conservative implementation safeguards, not an
owner-approved canonical decision. The 2026-08-01 post-merge decision at the
top of this ledger approves them prospectively as the temporary PR06 contract.

| Decision field | Current state |
| --- | --- |
| WorkItem/Milestone cap | 500 canonical Task-kind WorkItems plus canonical Milestones |
| Dependency cap | 2,000 active same-Project dependencies with active canonical Task endpoints |
| Overflow behavior | Typed HTTP 400, fail closed, no truncation or partial graph |
| Canonical source | Requires a bounded snapshot only; it supplies no numeric value or overflow contract |
| Implementation source | Conservative implementation safeguard pending owner approval at merge time |
| PR body/comments | No owner decision found at merge time |
| Resolved at merge time | No |
| DECISION REQUIRED at merge time | Yes |

Current resolution: resolved post-merge for the temporary PR06 full-snapshot
contract. This does not establish permanent Project or database limits.

## Implemented contract ledger

This section records source-audited behavior at current code-bearing candidate
`2fc5910e772f427355529de6e500b093583872b6`. Exact product/migration evidence
belongs to its product-code parent `69cc6f0`; the PostgreSQL-enabled PR06,
PR05, PR04, and full backend suites were also rerun successfully at exact
`f2d3805` and current `2fc5910`. Historical and current evidence are separated
below.

### Snapshot

- Endpoint: `GET /api/projects/{projectId}/gantt`
- Item bound: temporary 500 canonical Task-kind WorkItems plus canonical
  Milestones, consistently applied across snapshot and all PR06 command paths;
  typed rejection and no truncation
- Dependency bound: temporary 2,000 active same-Project dependencies whose
  endpoints are active canonical Tasks; typed rejection and no truncation
- Scheduled items: canonical Task items with at least one planned date
- Unscheduled items: canonical Task items with neither date and an `UNSCHEDULED`
  warning; no date inference
- Parent derived dates/progress: direct, non-deleted canonical Task-kind
  children only; `progressIsDerived=true`, `PARENT_DERIVED`, and direct edit
  denied
- Milestones: compatibility aggregate projected as canonical
  `kind=Milestone`, `milestoneDate`, 0/100 progress, zero-duration semantics,
  version, and missing-date warning
- Dependencies: same-Project canonical rows only; FS editable according to
  Project permission, legacy non-FS read-only; bounded reads require active
  canonical Task endpoints
- Calendar: actual resolved Workspace timezone; working days and holidays
  unavailable and explicitly limited; `calendarVersion=null`
- Warnings: `DEPENDENCY_VIOLATION`, `MISSING_ACTIVE_PLANNED_END`,
  `PARENT_DERIVED`, `LEGACY_DEPENDENCY_TYPE`,
  `MILESTONE_DATE_REQUIRED`, and `UNSCHEDULED`; every projected warning is
  non-blocking; dependency checks use derived dates for parent endpoints
- Permissions: top-level and per-item schedule/progress/dependency/clear/open
  capabilities derived server-side
- Versions: Project, Workflow, Task, Milestone, and successor/dependency command
  token; no independent working-calendar version exists

### Commands

- Schedule: `PATCH /api/tasks/{taskId}/schedule`; both
  `plannedStartDate`/`plannedEndDate` keys are required but nullable,
  `milestoneDate` is Milestone-only, and `expectedVersion` is required
- Progress: `PATCH /api/tasks/{taskId}/progress`; the progress member is
  required, integer 0-100, binary for a Milestone, with required
  `expectedVersion`; omission is rejected
- Dependency add: `POST /api/tasks/{successorTaskId}/dependencies`; request
  requires `predecessorTaskId`, canonical string
  `dependencyType="FinishToStart"`, and positive successor `expectedVersion`;
  unknown members, including lag/lead, are rejected
- Dependency remove:
  `DELETE /api/tasks/{successorTaskId}/dependencies/{dependencyId}?expectedVersion=...`
- Expected versions: positive and exact; stale versions map to HTTP 409 and the
  Angular flow performs authoritative refetch while preserving safe intent.
  Compatibility Milestone update also requires a positive version, enforces
  its date invariant, and maps stale revisions to 409
- Audit: metadata-safe schedule/progress/Milestone/dependency action records,
  plus reason-coded visible dependency rejections without hidden-neighbor
  metadata
- Outbox: canonical `Projects.TaskChanged.v1` and
  `Projects.ProjectChanged.v1` invalidations as applicable
- Atomic rollback: command state, audit, and Outbox use one EF save/transaction;
  failed-save tests verify no partial persistence

### Required no-cascade behavior

Dependency violations are warnings. Schedule/progress/dependency commands do
not move predecessor or successor dates automatically. The schedule command
updates only `PlannedStartDate`/`PlannedEndDate` and maintained compatibility
`StartDate`/`DueDate`; `DeadlineAt` is outside the command and PostgreSQL HTTP
tests verify that it remains unchanged.

### Parent and terminal invariants

- Only direct canonical Task-kind children participate in parent dates and
  progress; Milestones and non-Task rows are excluded.
- Done/Cancelled parents reject subtask creation until explicitly reopened.
- A terminal child cannot reopen to Backlog/Todo while its parent remains
  terminal.
- A parent can enter Done only when every direct canonical Task child is Done
  or Cancelled and the canonical derived progress is exactly 100.
- An all-cancelled child set stays derived with progress 0, so the parent Done
  transition is rejected.
- Review-override completion obeys the same parent guard. Restore and delete of
  a child beneath a terminal parent are rejected until the parent is reopened.

### Authorization and errors

- Viewer and Workspace ReadOnly actors receive read-only permissions and
  modifying commands are rejected server-side.
- Project Manager/Owner can edit Task/Milestone schedule and progress and
  manage FS dependencies. A contributor can edit only a Task they created or
  are primarily assigned to, subject to active Workspace membership.
- Archived/deleted Projects, deleted WorkItems, revoked membership, unknown
  neighbors, cross-Project IDs, and cross-Tenant IDs follow safe rejection and
  do not expose hidden titles or graph neighbors.
- PR06 endpoints use `401`, `403`, safe `404`, validation `400`, and
  concurrency `409`. The narrow safe JSON shape is:

```json
{
  "requestId": "trace-id",
  "error": {
    "code": "GANTT_STALE_VERSION",
    "message": "Work item has changed. Refetch and retry.",
    "target": null,
    "details": [],
    "redactionApplied": false
  }
}
```

This is a PR06-scoped alignment; it does not claim that the repository-wide
legacy error mismatch is resolved.

Request-aborted cancellation is rethrown so cancellation propagates rather
than becoming a false server error. An unexpected snapshot exception returns
HTTP 500 with `GANTT_REQUEST_FAILED`; unexpected Gantt/dependency command
exceptions use their safe command failure code.

### Date, timezone, and calendar

- Planning values are serialized as `DateOnly` `yyyy-MM-dd` values and remain
  day-precision in the Angular contract.
- The snapshot uses the canonical Workspace timezone resolver and returns the
  resolved IANA identifier. Browser-local `Date` parsing is not used for
  planning validation or display.
- The runtime has no canonical working-day/holiday service. It returns no
  fabricated working days or holidays and states both limitations.
- `DeadlineAt` remains a distinct UTC timestamp and is never inferred from or
  changed by a Gantt command.

## Historical candidate evidence

The results below remain useful diagnostic and implementation history, but
they are not Acceptance evidence for the latest-main or documentation-bearing
final HEAD.

### Pre-remediation local candidate

Candidate `ab9a260dd4517d34a2500d5e76369ba241b504ee` passed the Release
build, PR06 45/45, PR05 25/25, PR04 8/8, full backend 485/485, Angular
318/318, production/architecture/license/bundle checks, and mocked Playwright
63 passed with 3 explicitly expected skips. Its npm audit reported 18 findings
(3 low, 7 moderate, 8 high, 0 critical). Default Storybook exhausted the local
2 GB heap; the same source passed with a 4 GB heap. These results predate the
final Acceptance fixes and latest-main integration.

### Pre-final Hosted candidates

| Candidate | Evidence | Historical result |
| --- | --- | --- |
| `4d34b711163ef650a2a7b4e5c8ae02dc2c037ae2` | Real Backend Browser Smoke run `30556472660`, job `90918109556`; artifact ID `8765212822`, name `real-backend-browser-smoke-artifacts`, digest `sha256:b23c2d90c57ca22c2633b36ce7d4387b60485c1dd36736abea93a730925f2c17` | Failed after the revocation-denial wait reached 120 seconds; useful failure evidence only, not final Acceptance evidence |
| `e34e9b71be0e3720259b97879d9767b18828ec9b` | Documentation CI `30558447099`; CI `30558447304`; Code Quality `30558447119`; npm Security Audit `30558447301` | All four workflows succeeded, but the candidate was behind latest main and is not final evidence |
| `3cf2e1dc8c1c94ce475fb8f52f5461391f69cbd1` | Documentation CI `30599566118` | Succeeded |
| `3cf2e1dc8c1c94ce475fb8f52f5461391f69cbd1` | CI `30599566112` | Build/test and security succeeded; frontend failed during default-heap Storybook, so CI failed |
| `3cf2e1dc8c1c94ce475fb8f52f5461391f69cbd1` | Code Quality `30599566161` | Cancelled |
| `3cf2e1dc8c1c94ce475fb8f52f5461391f69cbd1` | npm Security Audit `30599566128` | Cancelled after the report step |
| `3cf2e1dc8c1c94ce475fb8f52f5461391f69cbd1` | Real Backend Browser Smoke `30599574065` | Cancelled before executable evidence |
| `69b9a48f96d6d888c2c68c30a4b7fd708264304f` | Documentation CI `30607219548`; npm Security Audit `30607219526` | Both succeeded, but this candidate predates the latest-main merge |
| `69b9a48f96d6d888c2c68c30a4b7fd708264304f` | CI `30607219550` | Build/test and security succeeded; frontend was cancelled, so CI is not a pass |
| `69b9a48f96d6d888c2c68c30a4b7fd708264304f` | Code Quality `30607219528` | Qodana was cancelled; Angular quality succeeded; workflow is not a pass |
| `69b9a48f96d6d888c2c68c30a4b7fd708264304f` | Real Backend Browser Smoke `30607225084` | Cancelled; no executable final evidence |
| `555379db03d076627f04083a43eb07fe7ffa23bc` | Documentation CI `30608340676`; CI `30608340662`; Code Quality `30608340670` attempt 3; npm Security Audit `30608340657` | All four workflows succeeded and Qodana triage found 0 material PR06 findings, but the later HSTS-origin remediation changed the code-bearing HEAD |
| `555379db03d076627f04083a43eb07fe7ffa23bc` | Real Backend Browser Smoke run `30611459543`, job `91094951966`; artifact ID `8785696348`, digest `sha256:e5a102f3263a296f31ce2cf00853800d47d04d5b21a7816a69f32995031f092d` | Failed before login: JUnit 6 total, 0 passed, 0 assertion failures, 6 errors, 0 skipped. Trace confirmed internal HSTS 307 from `http://app:8080` to `https://app:8080`, followed by `ERR_SSL_PROTOCOL_ERROR`. PR06 steps/commands executed: 0; high-confidence secret matches: 0. |
| `e0e87dd9b4933af8165e472cc02761db0ff3ab6e` | Documentation CI `30612005927`; CI `30612006065` attempt 2; Code Quality `30612006010`; npm Security Audit `30612006220` | All four workflows succeeded after the HSTS-origin fix. Qodana again found 0 error, 0 critical, 0 material PR06, and 0 material PR-introduced findings. Latest-main merge `0b2d5fc` later changed the code-bearing HEAD. |
| `e0e87dd9b4933af8165e472cc02761db0ff3ab6e` | Real Backend Browser Smoke run `30625754075`, job `91140507111` | Cancelled at step 0 with no setup steps or executable evidence; not a pass. |
| `69cc6f0943cfc9d3e2dab358edceb0fad0a0fea6` | Documentation CI `30626428426`; CI `30626428493` attempt 4; Code Quality `30626428491` attempt 3; npm Security Audit `30626428487` | All four ordinary Hosted workflows succeeded at exact head after latest-main merge `0b2d5fc` and lock-only `tar` fix `69cc6f0`. Qodana found 0 error, 0 critical, 0 material PR06, and 0 material PR-introduced findings. The later test-only Real Backend evidence remediation changed current HEAD. |
| `69cc6f0943cfc9d3e2dab358edceb0fad0a0fea6` | Real Backend Browser Smoke run `30630832231`, job `91156526050`; artifact ID `8793522897`, digest `sha256:79841bfa974edfee464256d5415165f53a606eab760e9c2c2887aaa95115033c` | Failed: JUnit 6 total, 4 passed, 2 failed, 0 errors, 0 skipped. PR05 used a stale UI-text expectation after the protected projection had safely cleared; PR06 had not registered the exact safe Project-detail HTTP 400 denial for console reconciliation. High-confidence secret matches: 0. This is diagnostic candidate evidence, not a pass. |

### Code Quality candidate attempt history

These attempts targeted now-historical parent candidate
`555379db03d076627f04083a43eb07fe7ffa23bc`. Attempt 3 later succeeded and its
artifacts are recorded below; none is evidence for later `69cc6f0` or
`f2d3805` candidates.

| Attempt | Historical result |
| --- | --- |
| Attempt 1 | Cancelled; not a pass |
| Attempt 2 | Qodana job `91089891268` failed after runner shutdown and produced no artifacts; the PR Gate step was skipped. Angular quality job `91090268692` succeeded, but the workflow is not a pass. |

## Verification evidence ledger

The earlier full local-suite and Hosted entries below were executed at
historical candidates `555379db03d076627f04083a43eb07fe7ffa23bc` and
`e0e87dd9b4933af8165e472cc02761db0ff3ab6e`. Current code-bearing candidate
`2fc5910e772f427355529de6e500b093583872b6` descends from latest-main and
lock-only `tar` parent `69cc6f0943cfc9d3e2dab358edceb0fad0a0fea6`, test-only
Real Backend assertion remediation `f2d3805`, unrelated Messaging/documentation
commit `9f7b8f3`, and the cache-only workflow remediation. Parent `69cc6f0` has exact local
evidence plus successful Documentation CI, CI attempt 4, Code Quality attempt
3, and npm Security Audit. Its Real Backend run is retained as a deterministic
4-pass/2-fail diagnostic. Historical `f2d3805` has exact PostgreSQL-enabled local
regressions and successful Documentation CI, CI, Code Quality, and npm Security
Audit evidence. Its fresh Real Backend smoke step passed 6/6 and produced a
valid secret-clean artifact, but the workflow timed out/cancelled during
post-cache upload and is not a pass. Current `2fc5910` repeated the local and
ordinary Hosted gates and completed the licensed Real Backend workflow success.

### HSTS-origin remediation retained by the current candidate

| Evidence | Status |
| --- | --- |
| Commit | `e0e87dd9b4933af8165e472cc02761db0ff3ab6e` (`fix(test): avoid HSTS upgrade in real backend smoke`) |
| Root cause addressed | Internal `http://app:8080` was HSTS-upgraded to unsupported `https://app:8080` before login |
| Fix | Use the non-HSTS Compose alias `http://coglatas-backend:8080` and add a fail-closed origin guard, focused test, and operator documentation |
| Timeout/retry behavior | No timeout increase or retry added |
| Runner helper tests | 6/6 passed |
| Node syntax check | Passed |
| Compose config / alias | Passed; `coglatas-backend` alias is present and the composed configuration is valid |
| Diff check | Passed |
| Current code-bearing head | `2fc5910e772f427355529de6e500b093583872b6` retains the origin and assertion remediations; its only direct delta from `9f7b8f3` removes unnecessary Real workflow npm cache configuration |

### Toolchain and package integration

| Evidence | Status |
| --- | --- |
| Local Node | `v24.13.0` |
| Local npm | `11.6.2` |
| Active frontend package manager | `frontend/package.json` declares `npm@11.17.0`; local npm is the same major used for lockfile validation |
| Latest-main .NET test tooling | Version `10.0.10` retained |
| Latest main integration | `1739cfcc819174289d858cbacc255527f1ffa047` merged normally as `0b2d5fc1e99d441e278be1716b9fbb8baed96e90`, with no conflicts |
| Root install | `npm ci` succeeded at exact `69cc6f0` |
| Active frontend install | `npm --prefix frontend ci` succeeded at exact `69cc6f0` |
| `tar` regression/fix | Latest main had active-frontend `tar` 7.5.19 after its compatible dependency update. Lock-only commit `69cc6f0` restores 7.5.22 by changing only version, resolved URL, and integrity. |
| Package/lock audit | Lockfile version 3 retained; latest-main compatible dependency/test-tooling updates retained. Relative to main, intentional package-lock changes are the required Syncfusion family plus the `tar` 7.5.19-to-7.5.22 fix; no unrelated downgrade, local path, registry churn, or license material. |
| Historical `f2d3805` delta | Test-only `tests/ui/real-backend-smoke.spec.ts`; no production, package, lockfile, migration, Qodana-profile, or license change |
| Unrelated `9f7b8f3` delta | The three documentation files plus user-owned `messaging.facade.ts` / `messaging-ui.spec.ts`; the Messaging reconnect/authorization work is outside PR06 and blocks Merge scope |
| Current `2fc5910` delta | Only `.github/workflows/real-backend-smoke.yml`: removes setup-node `cache: npm` and `cache-dependency-path`; no timeout, retry, assertion, production, package, lockfile, migration, Qodana-profile, or license change |

### PostgreSQL (exact product-code parent `69cc6f0`)

| Evidence | Status |
| --- | --- |
| PostgreSQL version | Exact product-code-parent integration evidence: PostgreSQL 18.4 in an ephemeral `postgres:18-alpine` container |
| Latest migration | `20260730120626_AddCanonicalGanttVersions` |
| Empty apply | Passed on PostgreSQL 18.4 and in `GanttVersionMigrationAppliesToEmptyAndPr05UpgradeAndRollsBackAdditively` |
| Upgrade from PR05 | Passed from `20260729140506_AddProjectKanbanDefaultSwimlane`; existing Project/Milestone data is preserved and versions initialize to 1 |
| Existing data | Passed; existing Project, Milestone, Task, and dependency rows were retained |
| Additive down | Passed: down to PR05 removes only the two new version columns and preserves existing rows. The migration performs no data normalization. |
| Pending migrations | None after applying through `20260730120626_AddCanonicalGanttVersions` |
| Pending model changes | `dotnet ef migrations has-pending-model-changes`: none |
| Measured query count | Seven commands for the bounded repository projection: Project, Task count, Milestone count, Workflow version, Tasks/Stage/assignee, Milestones, and dependencies. The authorized real Kestrel/PostgreSQL snapshot is asserted at exactly 24 commands total, including tenant resolution, cookie/session authorization, membership, timezone resolution, and those seven projection commands. Overflow exits before loading rows/dependencies. |
| Generated SQL / EXPLAIN | Exact SQL is captured and emitted in xUnit evidence. Deterministic `ORDER BY`, bounded `LIMIT`, active dependency endpoints, the post-read combined-count recheck, the seven-command repository projection, the 24-command authorized HTTP total, cancellation, and absence of row-per-item N+1 are asserted. No `EXPLAIN` was required for the small synthetic fixture, and no speculative index was added. |
| Concurrency races | PostgreSQL regressions cover Project/Milestone tokens, a row inserted between the count gate and bounded reads, and shared Project-revision rejection of a dependency add racing a predecessor Task deletion; no stale edge persists. |

Migration note: the Up migration adds only Project and Milestone `VersionNo`
optimistic concurrency tokens. It does not update Task progress or any other
domain data. Down removes only those columns and preserves existing rows.

### Backend (exact product-code parent `69cc6f0`, test-only `f2d3805`, and current `2fc5910`)

| Check | Status |
| --- | --- |
| `dotnet restore Coglatas.slnx` | Succeeded; all projects were up to date |
| Release build | Passed at exact candidate `69cc6f0943cfc9d3e2dab358edceb0fad0a0fea6` |
| `Scope=TaskV1PR06` | 49/49 passed, 0 failed, 0 skipped, with live PostgreSQL supplied |
| `Scope=TaskV1PR05` regression | 25/25 passed, 0 failed, 0 skipped |
| `Scope=TaskV1PR04` regression | 8/8 passed, 0 failed, 0 skipped |
| Exact `f2d3805` PostgreSQL rerun | PR06 49/49, PR05 25/25, PR04 8/8, and full backend 494/494 passed, all with 0 failed and 0 skipped. The preceding no-PostgreSQL run is not used for Acceptance. |
| Exact `2fc5910` PostgreSQL rerun | Empty migration apply succeeded; PR06 49/49, PR05 25/25, PR04 8/8, and full backend 494/494 passed, all with 0 failed and 0 skipped. Release build: 0 warnings / 0 errors. |
| Post-merge exact boundary regression | `TaskV1Pr06GanttHostedHttpTests.Snapshot_RealPipelineHonorsExactCombinedItemBoundariesWithoutPartialDto` and `Snapshot_RealPipelineHonorsExactActiveDependencyBoundariesWithoutPartialDto` passed 2/2 against ephemeral PostgreSQL 18. The complete `Scope=TaskV1PR06` suite then passed 51/51 with the CI-equivalent base-database migration. They prove 499/500 success and 501 typed 400, plus 1,999/2,000 success and 2,001 typed 400, with no snapshot DTO fields on overflow. |
| Full backend suite | 494/494 passed, 0 failed, 0 skipped at exact parent `69cc6f0943cfc9d3e2dab358edceb0fad0a0fea6`, exact test-only candidate `f2d3805466b2a9bce3e2a7bf8392069330a1d6fd`, exact current candidate `2fc5910e772f427355529de6e500b093583872b6`, and Hosted CI runs `30632549234` / `30637433590` |
| Unexpected skipped tests | 0 in every backend run above |

The focused PR06 evidence covers the canonical bounded/duplicate-free
snapshot, deterministic seven-command repository and 24-command authorized
HTTP query shapes, cancellation, migration paths, canonical Task-only parent
derivation and terminal parent/child transition/override/restore/delete
invariants, post-read bound checks,
Project/Milestone optimistic tokens, real Kestrel + Npgsql cookie/CSRF
commands, viewer/contributor/manager authorization, revoked/archived/deleted
and cross-scope rejection, schedule/progress/Milestone/dependency mutations,
stale versions, no-cascade/unchanged deadline, audit/Outbox, and failed-save
atomic rollback.

### Frontend (exact product-code parent `69cc6f0`)

| Check | Status |
| --- | --- |
| Root `npm ci` | Succeeded |
| `npm --prefix frontend ci` | Succeeded |
| Active-frontend audit | 19 known findings: 3 low, 6 moderate, 10 high, 0 critical; 5 direct and 14 transitive. Direct affected packages are `@angular-devkit/build-angular`, `@angular/build`, `@angular/cli`, `@angular/compiler-cli`, and `@storybook/angular`. Latest-main active frontend was 20 (3 low, 7 moderate, 10 high, 0 critical); no Syncfusion affected path. |
| Angular unit tests | 42 files and 323/323 tests passed, 0 failed |
| Production build | Succeeded |
| Architecture checks | Application architecture check succeeded; Node architecture tests 4/4 passed |
| Syncfusion license check | Policy tests 4/4 passed; no license key was written to source or logs |
| Bundle analysis | Succeeded. Gantt remains a lazy production chunk (approximately 5.42 MB); initial bundle was 949.99 kB with a budget warning. |
| Storybook default | Failed with exit 134 after exhausting the available 2 GB Node heap; not counted as a pass |
| Storybook 4 GB | Succeeded with `NODE_OPTIONS=--max-old-space-size=4096` |
| Storybook Hosted product-code parent | Succeeded in CI run `30626428493` attempt 4, frontend job `91146858537`, at exact parent `69cc6f0943cfc9d3e2dab358edceb0fad0a0fea6` |
| Storybook Hosted historical `f2d3805` candidate | Succeeded in CI run `30632549234`, frontend job `91163476861`; Angular 323/323 and mocked Playwright 63 passed with 3 expected skips also succeeded |
| Storybook Hosted current candidate | Succeeded in exact `2fc5910` CI run `30637433590`, frontend job `91180641392`; Angular 327/327 and mocked Playwright 63 passed with 3 expected skips also succeeded |
| Mocked Playwright | 63 passed, 0 failed, 3 pre-existing explicitly expected skips. Focused PR06 Schedule desktop/mobile scenarios: 4/4 passed, 0 skipped. Browser response gate: 2/2 passed. |
| Real-backend Playwright | Exact `2fc5910` run `30639800642`, job `91186533535`, completed workflow success: 6/6, 30 PR06 steps / 9 commands, 0 failed/errors/skipped, API interception `none`, page errors 0; documentation-bearing exact-final-HEAD rerun remains Pending. |

Mocked Playwright proves Angular behavior only. It does not replace real
ASP.NET Core/PostgreSQL/cookie/CSRF evidence.

The code-bearing hosted real-backend Gate proved, without API interception, two
real cookie-authenticated users, CSRF, canonical snapshot, schedule/progress/FS
dependency mutations, stale 409/refetch, revoked membership, reload
persistence, and SignalR-degraded HTTP fallback. The same Gate must rerun after
the documentation commit on the exact final PR HEAD.

### npm Security Audit (exact current candidate `2fc5910`)

| Evidence | Status |
| --- | --- |
| Candidate run / job | Run `30637433551`, job `91178483146`, success |
| Head SHA | `2fc5910e772f427355529de6e500b093583872b6` |
| Root total | 0 |
| Active frontend total | 19: 3 low, 6 moderate, 10 high, 0 critical |
| Inactive `coglatas-frontend` total | 12: 0 low, 5 moderate, 7 high, 0 critical; reported separately and not treated as the active UI |
| Latest-main comparison | Latest-main active frontend was 20: 3 low, 7 moderate, 10 high, 0 critical. The current lock-only `tar` 7.5.22 retention removes the one `tar` moderate finding. |
| PR06-introduced findings | 0; no Syncfusion Gantt package appears in an affected dependency path |
| Direct / transitive | 5 direct and 14 transitive active-frontend findings. Direct affected packages: `@angular-devkit/build-angular`, `@angular/build`, `@angular/cli`, `@angular/compiler-cli`, and `@storybook/angular`. |
| `fixAvailable` review | All 19 active findings report a fix path, but the proposed resolutions include major-version or inconsistent downgrade candidates and are not safe mechanical acceptance changes. |
| Artifact | ID `8793939576`, name `npm-audit-reports` |
| Artifact digest | `sha256:f9f3fd372d7626dc9d7ce0d17fdd7405918302abcbdd047c182400fbbf1c03fe` |
| Action | Retain the lock-only `tar` fix and existing report-only baseline; do not run `npm audit fix --force`, a major upgrade, or an inconsistent downgrade without owner approval |

### Realtime

| Behavior | Status |
| --- | --- |
| Stale-event rejection / duplicate coalescing | Implemented in the Schedule facade and covered by focused Angular tests; exact `2fc5910` Real Backend workflow completed success, with only the documentation-bearing rerun pending |
| Active-edit conflict | Queues authoritative reconciliation, preserves safe intent, refetches authoritative HTTP state, then exposes explicit Retry-against-latest and Discard actions; focused Angular and real-browser source assertions are present |
| Reconnect reauthorization and refetch | Project subscription catch-up refetches the authoritative HTTP snapshot; exact `2fc5910` Real Backend workflow completed success for revocation/refetch, with only the documentation-bearing rerun pending |
| Authorization-revocation clear | A denied Project subscription synchronously clears protected Kanban and Gantt state and increments authorization/load/request generations before refetch, preventing stale response restoration; focused tests passed |
| SignalR-degraded HTTP/manual refresh | Degraded state retains HTTP edit and manual refresh behavior; focused/static browser tests passed |

### Real Backend Browser Smoke

The root-cause-bearing historical failure at parent candidate
`555379db03d076627f04083a43eb07fe7ffa23bc` did not reach login or any PR06
command. The next run at focused-origin candidate `e0e87dd9` was cancelled at
step 0 and supplied no executable evidence. Latest-main parent `69cc6f0`
retained the origin fix and executed all six browser scenarios, but two
evidence assertions failed. Historical test-only candidate `f2d3805` records the
exact safe Project-detail denial and current protected-state status. Its first
rerun was cancelled before setup; a fresh run passed all executable smoke
checks and uploaded valid evidence, but the workflow ultimately timed out and
was cancelled during an unnecessary setup-node npm cache upload. Commit
`2fc5910` removes only that cache configuration. Its exact rerun completed
success, including post-job cleanup.

| Evidence | Status |
| --- | --- |
| Historical run / job / head | Run `30611459543`, job `91094951966`, exact head `555379db03d076627f04083a43eb07fe7ffa23bc`; failure |
| Historical JUnit | 6 total, 0 passed, 0 assertion failures, 6 errors, 0 skipped |
| Historical failure boundary | All errors occurred before login; trace confirmed internal HSTS 307 from `http://app:8080` to `https://app:8080`, then `ERR_SSL_PROTOCOL_ERROR` |
| Historical PR06 steps / commands | 0 / 0 |
| Historical artifact | ID `8785696348`; digest `sha256:e5a102f3263a296f31ce2cf00853800d47d04d5b21a7816a69f32995031f092d` |
| Historical high-confidence secret matches | 0 |
| Historical origin-fix rerun | Run `30625754075`, job `91140507111`, exact `e0e87dd9b4933af8165e472cc02761db0ff3ab6e`; cancelled at step 0 with no setup steps or executable evidence |
| Latest diagnostic run / job / head | Run `30630832231`, job `91156526050`, exact parent `69cc6f0943cfc9d3e2dab358edceb0fad0a0fea6`; failure |
| Latest diagnostic JUnit | 6 total, 4 passed, 2 failed, 0 errors, 0 skipped. MVP0 core, degraded Hub, PR04, and PR03C passed; PR05 and PR06 failed the evidence assertions below. |
| Latest diagnostic failures | PR05 expected stale `Project board not found` text after protected board state had safely cleared; PR06 did not register the exact safe `GET /api/projects/{projectId}` HTTP 400 denial as a scenario-specific expected response for browser-console reconciliation |
| Latest diagnostic artifact | ID `8793522897`, name `real-backend-browser-smoke-artifacts`, digest `sha256:79841bfa974edfee464256d5415165f53a606eab760e9c2c2887aaa95115033c` |
| Latest diagnostic secret-pattern matches | 0 high-confidence matches |
| Test-only remediation | Commit `f2d3805466b2a9bce3e2a7bf8392069330a1d6fd` observes and validates the Project-detail denial as exact `GET` + Project path + HTTP 400, checks the safe `BadRequest` / `Project not found.` contract and protected-value redaction, and asserts the current authorization-clear status. Scenario-specific expected failures must be observed exactly once; arbitrary 400 responses remain failures. No retry or timeout was added. |
| First `f2d3805` rerun / job / head | Run `30632559051`, job `91162166864`, exact `f2d3805466b2a9bce3e2a7bf8392069330a1d6fd`; infrastructure cancellation before setup, no artifact, not a pass |
| Fresh `f2d3805` rerun / job / head | Run `30634069147`, job `91167131007`, exact `f2d3805466b2a9bce3e2a7bf8392069330a1d6fd`; smoke step and artifact upload passed, but workflow ultimately timed out/cancelled during setup-node post-cache upload; not a Gate pass |
| Historical `f2d3805` artifact | ID `8794673197`, name `real-backend-browser-smoke-artifacts`, digest `sha256:660fbe4b8eafc0f967f4fa9ae7915f47b0a01951f16b3634d15d986e665ba814`; high-confidence secret matches 0 |
| Current workflow remediation | Commit `2fc5910e772f427355529de6e500b093583872b6` removes only setup-node npm cache settings; it does not change timeout, retries, assertions, or scenarios |
| Current run / job / head | Run `30639800642`, job `91186533535`, exact `2fc5910e772f427355529de6e500b093583872b6`; workflow and job completed `success` |
| Scenario count / passed / failed / skipped | 6 total, 6 passed, 0 failed, 0 errors, 0 skipped; PR06 recorded all 30 required evidence steps and 9 commands |
| API interception | `none`; the real Gantt API was not intercepted |
| Cookie authentication / real CSRF | Passed with real cookie authentication and real CSRF |
| Real ASP.NET Core / PostgreSQL | Passed against built Angular, real ASP.NET Core, and real PostgreSQL |
| Snapshot / schedule / progress | Passed; canonical snapshot plus schedule/progress commands and reload persistence were exercised |
| Dependency add/remove / no cascade | Passed; real FS add/remove and structured warning verified no successor auto-movement |
| Stale conflict / rollback / authoritative refetch | Passed; optimistic 409, rollback, authoritative refetch, and logical focus restoration were exercised |
| Membership revocation / stale-response fencing | Passed; protected state cleared before a held stale response and was not restored |
| Reload persistence / degraded HTTP fallback | Passed; persisted edits survived reload and manual HTTP refresh worked with Hub unavailable |
| Page errors | 0 |
| Current artifact ID / name / digest | ID `8797054160`, name `real-backend-browser-smoke-artifacts`, digest `sha256:75315d1f961c6865fa2f25debbb23754b158ca2b6ea8e4a9995843b95b9398b8` |
| Current secret-pattern matches | 0; downloaded artifact scanned with CI-identical Gitleaks v8.24.3 `--no-git --redact` |
| Current Gate conclusion | `success` at exact code-bearing head `2fc5910e772f427355529de6e500b093583872b6` |

### Historical full-gate candidate `555379db`

These runs are exact for historical parent candidate
`555379db03d076627f04083a43eb07fe7ffa23bc`. The successful quality gates and
Qodana triage remain useful historical evidence, but the Real Backend failure
led to code-bearing remediation `e0e87dd9b4933af8165e472cc02761db0ff3ab6e`.

| Gate | Run / job | Candidate status |
| --- | --- | --- |
| Documentation CI | Run `30608340676`, job `91085385363` | Success |
| CI | Run `30608340662` | Success |
| `build-test` | Job `91085385567` | Success; full backend 494/494 |
| `security-scan` | Job `91085385535` | Success |
| `frontend-test` | Job `91087317923` | Success; Angular 323/323, Hosted Storybook success, mocked Playwright 63 passed with 3 expected skips |
| npm Security Audit | Run `30608340657`, job `91085385191` | Success |
| Code Quality | Run `30608340670`, attempt 3 | Success at exact candidate `555379db03d076627f04083a43eb07fe7ffa23bc` |
| Qodana Community / .NET | Job `91091343250` | Success; full inventory and report/model artifacts triaged |
| Angular quality | Job `91094173439` | Success |
| Real Backend Browser Smoke | Run `30611459543`, job `91094951966` | Failed before login with six infrastructure-origin errors; PR06 steps/commands 0 |

### Historical focused-origin candidate `e0e87dd9`

These runs are exact for the focused HSTS-origin remediation candidate
`e0e87dd9b4933af8165e472cc02761db0ff3ab6e`. All ordinary Hosted gates
succeeded, but the Real Backend job was cancelled before setup. Latest-main
merge `0b2d5fc` later changed the code-bearing head.

| Gate | Run / job | Candidate status |
| --- | --- | --- |
| Documentation CI | Run `30612005927`, job `91096678127` | Success |
| CI | Run `30612006065`, attempt 2 | Success |
| `build-test` | Job `91138599652` | Success; full backend 494/494 |
| `security-scan` | Job `91138599781` | Success |
| `frontend-test` | Job `91138599518` | Success; Angular 323/323, Hosted Storybook success, mocked Playwright 63 passed with 3 expected skips |
| npm Security Audit | Run `30612006220`, job `91096678735` | Success |
| Code Quality | Run `30612006010` | Success |
| Qodana Community / .NET | Job `91096678715` | Success; 2,260 findings, 0 error, 0 critical, 0 material PR06, 0 material PR-introduced |
| Angular quality | Job `91100159546` | Success |
| Real Backend Browser Smoke | Run `30625754075`, job `91140507111` | Cancelled at step 0 with no executable evidence; not a pass |

### Latest-main product-code parent gates

All runs below target exact product-code/package parent
`69cc6f0943cfc9d3e2dab358edceb0fad0a0fea6`. Earlier cancelled attempts remain
historical; the final successful attempt is stated explicitly.

| Gate | Run / job | Parent-candidate status |
| --- | --- | --- |
| Documentation CI | Run `30626428426`, job `91142659554` | Success at exact head |
| CI attempts 1-3 | Run `30626428493` | Not passes: frontend jobs `91144806550`, `91145333396`, and `91146358216` were cancelled at step 0 while earlier reusable jobs had succeeded |
| CI attempt 4 | Run `30626428493`; build-test `91146858583`, security-scan `91146858952`, frontend-test `91146858537` | Success at exact head; backend 494/494, Angular 323/323, production/license builds, Hosted raised-heap Storybook, and mocked Playwright 63 passed with 3 expected skips |
| CI build-test artifact | ID `8791664257`, digest `sha256:0b5a71bb952293effe80cebf0da1ef15611e18dce8596fd86e2919e56ebfc92e` | Valid exact-parent artifact |
| CI security artifacts | Gitleaks `8791656244` / `sha256:8253ffcb58301a2f113138c0f7012e279ff66c131185be484b53ec4d45e354b5`; dependency report `8791671910` / `sha256:5d27e4109a4eaa4165b7715850fff3e34679e26b8de789beacbe0a0589059bfb`; Trivy `8791789324` / `sha256:152233aaa39e8d5da95465ae6cf7ae5a4f94577d3e9b105aab72cd9066e8b887` | Valid exact-parent artifacts |
| Code Quality attempts 1-2 | Run `30626428491` | Not passes: attempt 1 jobs were cancelled at step 0; attempt 2 Qodana `91147324934` was cancelled at step 0 while Angular quality `91147329233` succeeded |
| Code Quality attempt 3 | Run `30626428491`; Qodana `91149477336`, Angular quality `91152690654` | Success at exact head |
| npm Security Audit | Run `30626428487`, job `91142659450` | Success at exact head; artifact `8791530869`, digest `sha256:531d8a91339eda02af574db4bdab1c054c04ece33fcc3c725f40d1c90736af46` |
| Real Backend Browser Smoke | Run `30630832231`, job `91156526050` | Failure: 6 total, 4 passed, 2 failed, 0 skipped; diagnostic artifact `8793522897`, digest `sha256:79841bfa974edfee464256d5415165f53a606eab760e9c2c2887aaa95115033c`, secret matches 0 |

### Historical test-only remediation candidate gates

All runs below target historical test-only candidate
`f2d3805466b2a9bce3e2a7bf8392069330a1d6fd`. The four ordinary Hosted workflows
completed successfully. The first Real Backend run was cancelled before setup;
its fresh replacement passed all executable smoke checks and uploaded a valid
artifact, but the workflow ultimately timed out/cancelled during setup-node
post-cache upload and is not a Gate pass.

| Gate | Run | Current status |
| --- | --- | --- |
| Documentation CI | Run `30632549237`, job `91162128837` | Success at exact head |
| CI | Run `30632549234`; build-test `91162129484`, security-scan `91162129549`, frontend-test `91163476861` | Success at exact head; backend 494/494, Angular 323/323, raised-heap Storybook success, mocked Playwright 63 passed with 3 expected skips |
| CI build artifact | `8794073432` / `sha256:7816c5972cbac52201720108ba750ef7640a3f2f5d3dda754e3f1f45e8e29c0b` | Valid backend test-results artifact |
| CI security artifacts | Gitleaks `8793931371` / `sha256:c38e27ff6fb227561cc1df0f879cec955de3e24622bc1f7cee7e8e14d17d861c`; dependency `8793952689` / `sha256:a98d4033f00be1a272e022f79b7efae8e9de2e47f42677c1ed19b344a33c960e`; Trivy `8794015211` / `sha256:c00ade56a5c4c1028bdd418adb26db5190c8e9aa2af22faaea48b8d269c09a21` | Valid exact-head artifacts |
| Code Quality | Run `30632549238`; Qodana `91162129048`, Angular quality `91164686596` | Success at exact head; 2,260 findings (1,421 warning, 839 note, 0 error, 0 critical), short report 0, model unresolved/failures 0, material PR06 findings 0 |
| Code Quality artifacts | Full `8794078721` / `sha256:752afb3fad35c7a03e8f5de49e197f736fcdd3f82f3ccdbbb6007b0e8055d9cc`; report `8794203490` / `sha256:8a623a78a537b2fbeadbed544a0d36e54ad2e323999ba15bafeffaae3a2b4c27`; model `8794227440` / `sha256:248f5d3919c697a851fac19e13e5e498c2e543f02657a8d474a2824a5e4059a6`; frontend `8794475568` / `sha256:b5831329ac8db3b25ed674765a29e50a456b0a2173219dced29cddc1939e032f` | Valid exact-head artifacts |
| npm Security Audit | Run `30632549183`, job `91162128341`; artifact `8793939576` / `sha256:f9f3fd372d7626dc9d7ce0d17fdd7405918302abcbdd047c182400fbbf1c03fe` | Success at exact head; active frontend 19 (3 low, 6 moderate, 10 high, 0 critical), PR06-introduced 0 |
| First Real Backend Browser Smoke | Run `30632559051`, job `91162166864` | Cancelled before setup; no artifact; not a pass |
| Fresh Real Backend Browser Smoke | Run `30634069147`, job `91167131007` | Smoke step 6/6 passed with 30 PR06 steps, 0 failed/skipped; workflow overall cancelled during setup-node post-cache upload; not a pass |
| Fresh Real Backend artifact | `8794673197` / `sha256:660fbe4b8eafc0f967f4fa9ae7915f47b0a01951f16b3634d15d986e665ba814` | Valid; high-confidence secret matches 0 |

### Historical pre-latest-main code-bearing candidate gates

All results below target exact code-bearing candidate
`2fc5910e772f427355529de6e500b093583872b6`. Those local and Hosted technical gates
are green for the required commands. Default 2 GB local Storybook remains a
separately recorded OOM non-pass; the required 4 GB and Hosted Storybook builds
succeeded. This evidence predates latest-main merge `1abce6c` and is not final
Acceptance evidence.

| Gate | Run / job | Current status |
| --- | --- | --- |
| Local PostgreSQL/backend | Ephemeral PostgreSQL 18; exact `2fc5910` | Empty migration apply succeeded; PR06 49/49, PR05 25/25, PR04 8/8, full 494/494; 0 failed/skipped; pending model changes 0 |
| Local frontend | Exact `2fc5910` | `npm --prefix frontend ci`, Angular 327/327 in 42 files, production build, architecture 4/4, license safeguards 4/4, lazy bundle, 4 GB Storybook, and mocked Playwright 63 passed + 3 expected skips succeeded. Default 2 GB Storybook OOMed and is not a pass. |
| Documentation CI | Run `30637433566`, job `91178483260` | Success at exact head |
| CI | Run `30637433590`; build-test `91178483276`, security-scan `91178483339`, frontend-test `91180641392` | Success at exact head; backend 494/494, Angular 327/327, Hosted Storybook success, mocked Playwright 63 passed + 3 expected skips |
| CI backend artifact | `8796172867` / `sha256:f43869b43b857f4330bb7f4f51723bba24ecd9d50c1c779fbe4ab0c0a97f26c8` | Valid |
| CI security artifacts | Gitleaks `8795938446` / `sha256:4f8c7e403bfedffe7426717d2661cf0ba093a480434bebcfb8f92040bf180e6f`; dependency `8795949215` / `sha256:8c65be7523c5f4f104728be3a147fb126537e5620e3d41df41cf8b25e2c6d75f`; Trivy `8796019743` / `sha256:89d9a7388c7cca14beada8f896d93d4ce099c1d1bb1fef1ef5a1c6a71ed15f5a` | Valid; repository Gitleaks matches 0 |
| Code Quality | Run `30637433561`; Qodana `91178484006`, Angular quality `91183270978` | Success; 2,260 findings (1,421 warning, 839 note, 0 error, 0 critical), short report 0, model unresolved/failures 0, material PR06 0, material PR-introduced 0; Angular 327/327 |
| Code Quality artifacts | Full `8796327316` / `sha256:3e0c40fcf0caeb5bb8f8d8ad3c41b564a61502f8edc193a48912fc59d9b15c5d`; report `8796471351` / `sha256:cd4c4ab06552d7f5fab518f50f607bbfc00e4b4b0520e9f9e3caeca6c90bfbea`; model `8796506046` / `sha256:077bfe98533a2a8744740e0c112e6e9679a684fb76f79d2865689461ea2857c0`; frontend `8796818315` / `sha256:24eb0f0a20de07ade99ad3ac18b9c7a52e4f396ed21f13a980315fead9ae2269` | Valid |
| npm Security Audit | Run `30637433551`, job `91178483146`; artifact `8795930038` / `sha256:6922cac2d777d057555d60ace3f6b24630f05d9ad85fb901543ba13cfa89103d` | Success; active frontend 19 (3 low, 6 moderate, 10 high, 0 critical), latest-main delta -1 moderate, Syncfusion/PR06 introduced 0 |
| Real Backend Browser Smoke | Run `30639800642`, job `91186533535` | Success; JUnit 6/6, failed/errors/skipped 0, PR06 30 steps / 9 commands, API interception `none`, page errors 0 |
| Real Backend artifact | `8797054160` / `sha256:75315d1f961c6865fa2f25debbb23754b158ca2b6ea8e4a9995843b95b9398b8` | Valid; downloaded artifact Gitleaks matches 0 |

### Latest-main remediation code-bearing candidate gates

All local results below target exact code-bearing candidate
`1abce6c70d9f665b773d35f75d63c0d05a387cc8`. Hosted results for this commit
are diagnostic only because the following documentation commit becomes the
exact final HEAD.

| Gate | Result |
| --- | --- |
| Main integration | Actual latest main `33c35cbc873fcdc78b75663d195ca120e2c01520`; normal merge `1abce6c`; inspection metadata fallback `8efa845`; ahead/behind 29/0 before documentation |
| Scope | Messaging facade/test diff 0; PR07/PR08 contamination 0 |
| Release build | Passed; 0 warnings, 0 errors |
| PostgreSQL 18.4 | Empty apply through `20260730120626_AddCanonicalGanttVersions`; PR05 upgrade/data/VersionNo preservation and additive Down passed; pending model changes 0 |
| Backend | PR06 49/49, PR05 25/25, PR04 8/8, full backend 494/494; 0 failed, 0 skipped |
| Package install | Root, active frontend, and inactive frontend `npm ci` passed. The inspection workspace has no tracked lockfile, so its requested `npm ci` is unavailable; documented no-lock install and inventory succeeded. |
| Angular/frontend | Angular 323/323 in 42 files; production build; architecture 4/4; license safeguards 4/4; bundle analysis passed |
| Bundle | Initial 949.99 kB; Gantt 5.42 MB lazy chunk; absent from initial bundle |
| Storybook | Default approximately 2 GB heap OOM, not a pass; 4 GB diagnostic build passed; exact-final Hosted Storybook required |
| Playwright | 63 passed, 0 failed, 3 pre-existing expected skips, 0 unexpected skips |
| npm audit | Root 0; active 15 (3 low, 6 moderate, 6 high, 0 critical); inactive 8 (0 low, 5 moderate, 3 high, 0 critical); Syncfusion affected 0 |
| Qodana / Real Backend | Attempt `5111784`: Qodana job and Real Backend succeeded, but Code Quality workflow failed in Angular quality; not a final pass. Exact-final-HEAD reruns pending after documentation commit. |

### Post-documentation exact final-HEAD gates

| Gate | Status |
| --- | --- |
| Documentation CI | Pending on the documentation-bearing final HEAD |
| CI (`build-test`, `security-scan`, `frontend-test`) | Pending on the documentation-bearing final HEAD |
| Code Quality (Qodana and Angular quality) | Pending on the documentation-bearing final HEAD |
| npm Security Audit | Pending on the documentation-bearing final HEAD |
| Real Backend Browser Smoke | Pending on the documentation-bearing final HEAD |
| Real Backend artifact and secret scan | Pending |
| Exact final-HEAD match | Pending |

The successful main kickoff runs above must not be substituted for these final
PR06 runs. The exact documentation-bearing SHA and post-commit run IDs will be
recorded in the PR body and final report to avoid changing HEAD again merely to
embed self-referential evidence.

### Qodana candidates and review

The latest completed Qodana inventory below is exact for failed-workflow
candidate `5111784e72054db9501135888e72330672a8c975`. Its Qodana job passed,
but the separate Angular quality job failed, so Code Quality is not a pass.
The final documentation-bearing rerun after remediation `8efa845` remains
Pending.

| Evidence | Status |
| --- | --- |
| Latest attempted Code Quality candidate | Run `30673208389`, failure at exact `5111784e72054db9501135888e72330672a8c975`; not a pass |
| Latest completed Qodana / Angular jobs | Qodana `91297960761` success; Angular quality `91305933712` failure during inspection install |
| Full inventory | 2,260 findings: 1,421 warning, 839 note, 0 error, 0 critical |
| Qodana short report | 0 |
| Model validation | 0 unresolved findings and 0 model failures |
| Qodana Critical | 0 |
| Qodana Error | 0 |
| Material PR06 findings | 0 |
| Material PR-introduced findings | 0 |
| Latest full inventory artifact | ID `8810623145`; digest `sha256:9e15e61f054e30a0b2939e6e966805e650290d47b16a91c6e7bfccd062197ba1` |
| Latest report artifact | ID `8810773389`; digest `sha256:bf69e0967d03eb00c621ac49a382f38b0a9cc1f2ffe3c0230ae68665f017d37c` |
| Latest model artifact | ID `8810777568`; digest `sha256:1cfe6c0e4680901e0e075df2d83c345bde31355b2bb1815b94564b7d1a810d27` |
| Latest frontend inspection artifact | None; the inspection install failed before inventory execution, so this is not a Code Quality pass |
| Earlier candidate artifacts | `69cc6f0`: full `8792582726`, report `8792772245`, model `8792773927`, frontend `8793187227`; `e0e87dd9`: full `8786059473`, report `8786235946`, model `8786237568`, frontend `8786394667`; `555379db`: full `8785339371`, report `8785489866`, model `8785495363`, frontend `8785544038`; retained as historical evidence |
| Product-code-parent review threads | 0 unresolved at the exact `69cc6f0` check |
| Latest-main code-bearing-candidate review threads | 0 unresolved at exact `1abce6c` |
| Final unresolved review threads | Recheck Pending on the documentation-bearing final HEAD |
| PR body synchronized | Pending |
| Verification synchronized | Historical `555379db`/`e0e87dd9`/`69cc6f0`/`f2d3805` evidence and exact `2fc5910` local/Hosted/Real results are synchronized; documentation-bearing Hosted evidence remains Pending |

Artifact triage found no production disposed-context, authorization,
transaction, concurrency, cancellation, or nullable-logic defect on PR06
paths. All 79 findings mapped to added lines were independently reviewed;
material PR06 findings remained 0. The extra `AccessToDisposedClosure` occurrence is an awaited
cancellation test; three resource warnings are in Hosted test helpers.
Multiple-enumeration findings are two base Organization tests, identical
ternaries are two base StudentRecord findings, and two redundant assignments
are base findings. The `PlanningRepository` entries are `Contains` usage
notes, not material bounded-query defects.

## Protected and unrelated files

Initial worktree state before this audit file was added:

- `qodana.yaml`: pre-existing user-owned modification; not touched or staged
- `.coglatas-spec-source/`: pre-existing protected untracked directory; used only as
  a read-only specification reference and not staged
- `.tools/`: pre-existing protected untracked directory; not touched or staged
- `frontend/src/app/features/messaging/messaging.facade.ts` and
  `frontend/src/app/features/messaging/messaging-ui.spec.ts`: unrelated
  `9f7b8f3` changes were backed up, then restored to actual `origin/main` by
  authorized forward cleanup `e8bdf47`. Both files are absent from the PR
  diff; the local-only safety branch was not pushed.
- `frontend/package.json`: the exact Syncfusion Angular Gantt dependency is an
  intended change for the vendor-isolated lazy adapter
- `frontend/package-lock.json`: focused npm-major-compatible review passed.
  Lockfile version 3 is unchanged. The intentional delta is the required
  Syncfusion dependency family plus lock-only `tar` 7.5.19-to-7.5.22 retention;
  latest-main compatible updates remain, no unrelated downgrade or registry
  churn was introduced, and the lockfile contains no local path dependency or
  license material.

## Historical pre-2026-08-01 blockers and verdict

Remaining blockers:

- DECISION REQUIRED: numeric snapshot cap and overflow behavior
- final documentation HEAD is not yet established; Draft PR #259 remains open
- unrelated commit `9f7b8f3` contaminated the PR06 branch with user-owned
  Messaging reconnect/authorization changes in prohibited PR07 scope; this
  remediation was not authorized to discard them
- product-code parent `69cc6f0` passed Documentation CI, CI attempt 4, Code
  Quality attempt 3, and npm Security Audit, but its licensed Real Backend run
  failed two deterministic evidence assertions and is not a pass
- historical `f2d3805` ordinary Hosted gates succeeded; fresh Real Backend
  `30634069147` passed its 6/6 smoke step and artifact validation but the
  workflow timed out/cancelled during setup-node post-cache upload, so it is
  not a pass
- current code-bearing `2fc5910` Documentation CI `30637433566`, CI
  `30637433590`, Code Quality `30637433561`, npm Security Audit `30637433551`,
  and Real Backend `30639800642` all succeeded at exact head; downloaded Real
  artifact secret matches were 0
- post-documentation exact-final-HEAD Documentation CI, CI, Code Quality, npm
  Security Audit, and Real Backend reruns are pending
- the current `2fc5910` review check has 0 unresolved threads; final-HEAD review
  recheck plus PR-body synchronization are pending

Current Gate:

- TASK-V1-PR05: Complete
- TASK-V1-PR06 acceptance: Incomplete
- PR #259 Merge: No-Go
- Merge performed: No
- TASK-V1-PR07: No-Go pending PR06 merge and post-merge audit
- PR08: No-Go

This verdict must be replaced only by a final evidence-backed Go/No-Go report
from the exact final PR06 HEAD. Even if every PR06 Gate later passes, this task
must not merge the pull request.

## Post-merge current status

### Historical merge-time state

The immediately preceding blockers and No-Go verdict are retained as the
historical state before PR #259 merged. The numerical decision was unresolved
then, and this correction does not claim otherwise.

### Post-merge owner decision

On 2026-08-01, after the merge, the owner approved the existing 500 combined
item / 2,000 active same-Project dependency / typed HTTP 400 fail-closed
safeguards as the temporary PR06 full-snapshot contract.

### Current contract

- PR #259: Merged
- Merge commit: `d5de01cf303c914c2b390346575a22cadb8b4443`
- PR06 temporary capacity decision: Resolved post-merge
- Overflow: typed HTTP 400, no silent truncation, no partial item set, no
  partial dependency graph, and no successful partial snapshot

### Follow-up scalability work

- PR06B large-project support: Open / Deferred
- `TASK-V1-PR06B` owns pagination and virtualization; this corrective work does
  not implement either
- PR07 or later Gantt-dependent work must incorporate the corrected latest
  `main` before merge and is not responsible for PR06B unless the owner
  formally changes its scope
