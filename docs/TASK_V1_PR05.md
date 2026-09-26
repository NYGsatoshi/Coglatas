# TASK-V1-PR05 — Canonical Project Kanban

TASK-V1-PR05 adds one canonical board per Project over the existing Task and
Workflow Stage aggregates. It does not create a board-owned Task store, change
My Tasks into a cross-Project board, or add Gantt scheduling behavior.

## Canonical sources

- Specification revision:
  `20aa5a2e015ae8fb68e5ba2b257a416dfcad5c3f`
- `docs/specs/aip-core-v4/12-implementation-kickoff/task-v1-pr05-kanban-adapter-prompt.md`
- `docs/specs/aip-core-v4/01-core/12-task-work-management.md`
- `docs/specs/aip-core-v4/06-implementation-mapping/task-work-planning-api-realtime-contract.md`
- `docs/specs/aip-core-v4/03-acceptance/task-work-planning-acceptance.md`
- `docs/specs/aip-core-v4/06-implementation-mapping/aipsite-component-adoption-matrix.md`

The specification repository is read-only implementation input. It is not
copied into this repository.

## Backend contract

| Operation | Route | Authority and result |
| --- | --- | --- |
| Project board snapshot | `GET /api/projects/{projectId}/kanban` | Current Tenant and Project visibility are checked before a bounded, vendor-neutral projection is built. |
| Project board configuration | `PUT /api/projects/{projectId}/kanban/config` | Project Manager capability, current board version, the complete existing Stage set, and fixed Stage categories are required. |
| Task move/reorder | `POST /api/tasks/{taskId}/kanban-move` | Creator, primary assignee, or Project Manager capability plus current Task and board versions are required. |

The snapshot contains ordered Workflow Stage columns, warning-only WIP state,
backend-computed permissions, recent-Done behavior, structured warnings, and
canonical Task cards. Parent progress and planning dates are calculated from
direct children with the same rules as Task List and Task Detail. Deleted
parents are not exposed through child context.

Done defaults to the most recent 30 days. `includeOlderCompleted=true`
explicitly requests older completed Tasks. The server clamps `maxCards` to
`1..500`; WIP counts remain authorized Stage counts and are not reduced by
presentation filters.

Approved swimlanes are None, Primary Assignee, Target Group, Priority, and
Parent Task. They change presentation metadata only.

## Ordering and concurrency

`TaskItem.SortKey` remains the canonical persisted card order. Moves normally
insert by taking a deterministic midpoint or a gap of 1000. When no gap is
available, at most 1,000 cards in the target Stage are rebalanced to multiples
of 1000 in one command transaction. Task ID is the stable tie-breaker.

`TaskItem.VersionNo` protects the moved card and every card whose rank changes
during rebalance. `TaskWorkflowDefinition.VersionNo` protects board
configuration and board order. A stale token returns a conflict that requires
an authoritative HTTP refetch.

Root Task and Subtask creation now place new canonical Tasks in the Project's
initial Workflow Stage with the next stable Stage rank. This prevents a newly
created Task from existing outside the board without introducing a second
store.

## Transition, audit, and invalidation

Kanban and Task Detail call the same transition engine. Entering active work,
Review, Done, Cancelled, and reopening therefore retain the canonical
assignee, review, child-completion, reason, progress, and completion guards.
The command does not rewrite assignment, group, collaborator, reviewer,
priority, planning dates, effort, label, blocked, checklist, or dependency
state.

Configuration and moves add metadata-safe audit records. Task and Project
invalidations are inserted into the transactional Outbox with the mutation;
delivery occurs only after the transaction commits. Event payloads contain
identifiers, versions, changed-field hints, and `requiresRefetch`, not card
display data. HTTP responses and refetches remain authoritative.

## Persistence

Migration `20260729140506_AddProjectKanbanDefaultSwimlane` adds one
non-null string column to `task_workflow_definitions`, defaulting existing rows
to `None`. Existing Workflow Stage WIP limits, Stage order, Task sort keys,
foreign keys, indexes, and concurrency tokens are reused.

Rollback removes only the Project display-default column. Task Stage and
`SortKey` changes are canonical Task data and are not undone by disabling the
presentation flag or rolling back this additive migration.

## Angular behavior

The existing Project Detail Tasks tab loads the canonical board endpoint when
`tasks.kanbanV1` is enabled. When disabled, the maintained Project Task List is
shown on the same route. The flag changes presentation only and is never sent
to an authorization decision.

Move responses replace optimistic Task state with the authoritative command
snapshot. When that response uses board-default query presentation, the client
immediately refetches the selected swimlane and completed-Task window so those
query-only choices do not silently reset.

The Coglatas-owned `CoglatasKanban` contract carries columns, cards, ordering intents,
permissions, warnings, state, keyboard actions, focus restoration, and
swimlane metadata. Project feature state contains no vendor record, event,
enum, CSS selector, or DOM contract. The adapter provides pointer and keyboard
movement through the same move intent and renders a status-grouped vertical
layout at narrow widths. The existing package and license policy remains
unchanged; no package, lockfile, runtime license key, or vendor registration
change is part of PR05.

Realtime invalidations are version-aware refetch triggers. An active drag,
keyboard menu, or configuration interaction queues reconciliation. Reconnect
catch-up occurs after centralized reauthorization and also waits for an active
interaction to close. Authorization invalidation advances a local authorization
epoch, clears protected board state before HTTP revalidation, and prevents an
older load, move, configuration, or refresh response from restoring revoked
data.

My Tasks remains the PR04 cross-Project List. A stored legacy `kanban`
preference is normalized to `list`.

## Requirement-to-evidence checklist

| Requirement | Implementation | Focused evidence |
| --- | --- | --- |
| Authorized, bounded snapshot | `ProjectKanbanService`, `ProjectKanbanRepository` | service and PostgreSQL query-shape tests |
| Recent Done and older filter | repository cutoff/query | snapshot service tests |
| Parent/leaf canonical summaries | batched direct-child aggregate | derivation, deleted-parent, and component tests |
| Manager configuration | versioned config command | permission, full-Stage-set, WIP, audit, and conflict tests |
| Stable move/reorder | midpoint rank plus bounded rebalance | repeated move, before/after, reload, PostgreSQL atomic concurrency, and conflict tests |
| Canonical transition guards | shared `TaskTransitionEngine` | assignee, Review, Done, reopen, Cancelled, and parent tests |
| Isolation and non-leakage | global Tenant filter plus Project authorization and generic neighbor errors | Tenant, revoked access, deleted parent, and cross-Project neighbor tests |
| Authoritative optimistic UI | Project Detail facade | success, denial rollback, conflict refetch, queued invalidation, and authorization-epoch race tests |
| Keyboard and focus | Coglatas Kanban adapter | move, Escape, focus restoration, and denied-action tests |
| Narrow/touch alternative | grouped vertical adapter layout | component and desktop/mobile Playwright tests |
| My Tasks List-only | preference normalization and current explanatory copy | preference, UI, and flag-fallback tests |
| Vendor/package boundary | Coglatas contracts and architecture checks | direct-import, bundle, package/config diff checks |
| Additive migration | one default-swimlane column | empty, PR04-upgrade, down, and pending-model tests |

## Explicit non-goals

- PR06 Gantt commands or scheduling UI
- PR07 notification, digest, or shared realtime infrastructure changes
- PR08 integrated cutover
- Workspace production DTO/service/controller changes
- Messaging, Conversation, Channel, or Post production changes
- package, lockfile, Angular configuration, route, AppShell, global style, CI,
  or legacy `wwwroot` changes
