# Angular Project and Task Penpot audit

Audit date: 2026-10-17. Source baseline:
`05cc6095225ca8dc7271bf395c90ba18f23624db` (`main`).

## Evidence boundary

The requested source is Penpot file
`52a5041f-08a1-80f1-8008-c5a09f8b5f61`, reported by the coordinator's
successful metadata read as `CoglatasUI`. The requested pages are
`03 Project Preview` and `07 Task & My Work`. Their presence, page IDs,
board IDs, design revision, geometry, layers, typography, colors, component
variants, and prototype interactions could not be verified in this audit.
The labels PV02, PV04, PV05, and TK01 below are requested design references,
not successfully resolved Penpot board identities.

After reading the Penpot High-Level Overview and the `Penpot`, `Page`, and
`File` API documentation, the attempted read used only `penpot.currentFile`
and `currentFile.pages[].findShapes({ type: 'board' })`. It did not navigate
or mutate the design. The tool failed with this diagnostic:

> The Penpot plugin tab appears to be suspended by the browser (no heartbeat
> for 123s). Please click/focus the Penpot tab to wake it, then retry.

No successful board export or supplied verifiable design export was available.
Consequently there is **no Penpot visual or interaction parity claim** and no
production layout, color, typography, navigation, command, or permission change
in this patch. The source findings below prepare a later design-backed pass.
Private specification text and design content are not reproduced here.

## Status definitions

- **Existing**: the indicated source path and interaction are wired; this is
  source evidence, not production qualification or Penpot parity.
- **Partial**: a maintained implementation exists with an identified gap.
- **Missing**: the specifically identified source capability is absent.
- **Blocked**: evidence needed to assess or implement the requested behavior
  is unavailable.
- **Deferred**: explicitly outside this bounded Angular preparation patch.

Feature paths in the mapping are relative to
[`frontend/src/app/features/projects/`](../../frontend/src/app/features/projects/).
All browser routes below are Angular route paths; the production build's
`/app/` base is applied by the router. The current declarations are in
[`app.routes.ts`](../../frontend/src/app/app.routes.ts).

## Requested board to implementation mapping

Every Penpot page/board UUID and revision in this table is **Blocked:
unavailable**. No UUID was inferred from a code label, screenshot, route, or
component name.

| Requested design reference | Angular route and component | Data/API boundary | Existing interaction and test source | Status |
| --- | --- | --- | --- | --- |
| PV02 Project overview | `/projects/:projectId`; `project-detail-page/project-detail-page.component.*`, local `overview` tab | `ProjectDetailFacade`; `GET /api/projects/{projectId}`. Operational Projects additionally read Tasks, Kanban, Gantt, workload, and members. | Draft explanation, server-owned activation affordance, explicit activation command, authoritative follow-up reads; `project-detail-page.component.spec.ts`, `project-detail.facade.spec.ts`, `project-detail-page.task-create-navigation.spec.ts` | **Partial**: existing overview and guarded Draft flow; summary counts and navigation/accessibility gaps below. Design parity **Blocked**. |
| PV04 Project Task table | Same route, local `list` tab; `task-table/task-table.component.*` | `ProjectDetailFacade`; paged `GET /api/projects/{projectId}/tasks`; mapper and server-owned row capabilities | Desktop grid and narrow semantic cards, configured Stage, fixed category, independent Blocked state, semantic update time, boolean Artifact state, Open action, retry after refresh error; `task-table.component.spec.ts`, `project-detail-page.component.spec.ts`, `project-detail.facade.spec.ts` | **Partial**: existing maintained table; remote page navigation is missing. Design parity **Blocked**. |
| PV05 Project board | Same route, local `tasks` tab; `project-detail-page/project-detail-page.component.*`; shared `CoglatasKanbanComponent` adapter | `ProjectDetailFacade`; `GET /api/projects/{projectId}/kanban`, `POST /api/tasks/{taskId}/kanban-move`, `PUT /api/projects/{projectId}/kanban/config` | Authorized card opening and moves, versioned configuration, warning-only WIP limits, swimlanes, recent Done window, explicit older-Done option, rollback/conflict refetch and realtime invalidation; `project-kanban.models.spec.ts`, `project-detail.facade.spec.ts`, `project-detail-page.component.spec.ts` | **Existing** bounded board. Flag-off maintains Task table. Exact design columns/dialog layout **Blocked**. |
| TK01 My Work | `/tasks`; `my-tasks-page/my-tasks-page.component.*` | `MyTasksFacade`; `GET /api/me/tasks` and `/api/me/tasks/counts` with explicit relationship, scope, filters, and server paging | Assigned/participating/review/created/watching/team queue/completed views; relationship keyboard navigation, explicit Workspace scope, search/filters, saved browser preferences, Running/Needs review/Completed presets, page controls, Task opening, retry; `my-tasks.facade.spec.ts`, `work-view-preference.service.spec.ts`, `projects-ui.spec.ts` | **Existing** canonical cross-Project List. A My Tasks Kanban is intentionally absent in current source. Design parity **Blocked**. |
| Related Project-create dialog; design code unavailable | `/projects` or `/workspaces/:workspaceId/projects`; `projects-overview-page/*` and `project-create-dialog/*` | `ProjectsFacade`, `ProjectCreateFacade`, `ProjectCreateApi`; Workspace-scoped create-options and canonical create endpoints | Named authorized Group/Visibility choices, validation, retained uncertain-retry key, strict HTTP 201, committed navigation-only recovery, transient same-scope draft preservation; `project-create-dialog.component.spec.ts`, `project-create.api.spec.ts`, `project-create.facade.spec.ts`, `projects-ui.spec.ts` | **Existing** create-to-Draft flow. Dialog design identity and parity **Blocked**. |
| Related Task-create surface; design code unavailable | `/projects/:projectId/tasks/new`; `task-create-page/*` | `TaskCreateFacade`, `TaskCreateApi`; Project-scoped create-options and `POST /api/projects/{projectId}/tasks/create` | Capability-gated opener, native validated form, optional Brief, manager-only assignee/source override choices, advisory quality checklist, stable uncertain-retry key, strict HTTP 201, committed navigation-only recovery; `task-create-page.component.spec.ts`, `task-create.api.spec.ts`, `task-create.facade.spec.ts`, `task-create.authorization-race.spec.ts` | **Existing** routed page, not a verified design dialog. No runtime is started by creation. Design parity **Blocked**. |
| Related Task detail/editor; design code unavailable | `/projects/:projectId/tasks/:taskId`; `task-detail-page/*`, `task-editor/*`, `task-brief-fields/*` | `ProjectsFacade`; canonical Task detail and versioned Task/subresource commands; separate execution-scope, Research Plan, and execution-result components | Project/Task hierarchy, Brief, current phase, separately lazy Activity, guarded editor, conflict recovery, subtasks/checklist/comments/labels/watch/files; `task-detail-page.component.spec.ts`, `task-editor.component.spec.ts`, `projects-ui.spec.ts`, `projects.facade.spec.ts` | **Existing** substantial detail flow. Design section/dialog parity **Blocked**. |

The project discovery landing already uses `ProjectSummaryPanelComponent`,
authoritative Workspace-scoped Project reads, Create Project, My Tasks, and
the shared Continue-working panel. It is an integration point, not evidence
that any particular Penpot board has been implemented.

## API and authority cross-check

The listed frontend calls were checked against current controller and service
source, rather than accepted from mock fixtures alone:

- [`ProjectsController`](../../src/Coglatas.Web/Controllers/ProjectsController.cs)
  exposes Project detail and paged Task reads. `ProjectService.ListTasksAsync`
  checks `CanViewProject` before the repository projection. The Task DTO
  preserves configured Stage/category, independent Blocked state and the
  authorized boolean Artifact signal; it is not an Artifact inventory API.
- [`ProjectKanbanController`](../../src/Coglatas.Web/Controllers/ProjectKanbanController.cs)
  matches snapshot/config/move paths. `ProjectKanbanService` checks current
  Project visibility, manager/move authority, persisted versions and canonical
  Stage/neighbor identities. The facade uses HTTP results/refetches as content
  authority and treats realtime messages as invalidations.
- [`PlanningController`](../../src/Coglatas.Web/Controllers/PlanningController.cs)
  exposes the canonical My Tasks list/count pair.
  [`PlanningService`](../../src/Coglatas.Application/Planning/PlanningService.cs)
  validates current identity, explicit scope and optional Project access.
  [`PlanningRepository`](../../src/Coglatas.Infrastructure/Persistence/PlanningRepository.cs)
  applies visible Projects and active Workspace membership before filtering,
  totals, ordering and paging. Saved browser conditions never authorize rows.
- [`WorkspaceProjectsController`](../../src/Coglatas.Web/Controllers/WorkspaceProjectsController.cs),
  [`ProjectActivationController`](../../src/Coglatas.Web/Controllers/ProjectActivationController.cs)
  and [`ProjectTaskCreateController`](../../src/Coglatas.Web/Controllers/ProjectTaskCreateController.cs)
  match the canonical create-options/create/activation flows. The browser
  consumes server-owned capability bits and strict command envelopes. Draft
  Projects do not load operational subresources. The deprecated unscoped
  Project-create command is not the maintained dialog's write path.

These are static compatibility and authority checks. This audit did not run
ASP.NET Core, PostgreSQL, cookies, CSRF or realtime transport; their runtime
integration is **not verified** by the frontend checks below.

## Bounded preparation change

`my-tasks-page/my-tasks-page.stories.ts` supplied `COGLATAS_PROJECTS_MOCK`,
while the rendered page consumes `MyTasksFacade`, whose scenario token is
`COGLATAS_MY_TASKS_MOCK`. Both the default and scenario decorator now provide
the token the actual facade reads.

A subsequent built-Storybook browser probe showed that this token correction
alone was insufficient: both stories still displayed `Loading tasks` after
settling, with zero Task buttons, and logged Angular `NG04002: 'iframe.html'`.
No HTTP-provider injection error was observed. Anonymous session effects and
the realtime session boundary cleared the fixture after initialization, while
the empty story router attempted to navigate the Storybook iframe URL.

The story-only harness now supplies a synthetic authenticated session, passive
realtime registration/events and disabled initial router navigation. Saved
filters use a fresh in-memory store rather than browser storage. The actual
page and `MyTasksFacade` remain in use. Scenario rows are restricted to the
existing explicit synthetic `authorized` fixture markers, excluding the
default fixture's hidden sentinel. This fixture adaptation is not a production
authorization decision. The historical many-row scenario remains synthetic
data and does not qualify server paging or project-wide counts.

The new `my-tasks-page.stories.spec.ts` composes the actual story decorators,
checks that default and variant rows survive settled session effects without
HTTP requests, and verifies that saving a fixture filter does not write browser
storage. These are fixture preparation changes for future design inspection;
no mock provider is added to the product application.

No new dependency, global/shared style, backend API, database/schema,
authorization, compiler, Project IDE or runtime behavior changes are included.

## Source gaps retained for the design-backed pass

1. **Partial / deferred:** the Project-detail breadcrumb declares `routerLink`
   but its standalone component does not import `RouterLink`. It needs a
   runtime navigation regression and a scoped repair when production UI
   changes resume; no navigation change is made in this blocked pass.
2. **Partial / deferred:** Project-detail buttons declare `role="tab"` but
   lack roving focus, Arrow/Home/End handlers and tab/panel associations.
   My Tasks already handles keyboard tab navigation, but its results section
   is not associated with the relationship tabs as a tabpanel. Existing
   click-based tests do not establish these missing keyboard/ARIA behaviors.
3. **Missing / deferred:** Project discovery/detail consume only the returned
   first API page and discard the page envelope. Project-detail overview
   counts derive from those loaded Task rows, not a project-wide aggregate.
   `ProjectListQuery` and `TaskListQuery` default to page 1 / 50 records and
   cap the requested page size at 100. Grid-local pagination cannot fetch a
   missing server page. Do not present these as complete large-Project totals
   or complete Project discovery. This patch does not change that contract
   or broaden reads.
4. **Deferred:** virtualized/paginated large-Project Board/Gantt delivery and
   the existing full-snapshot limits remain separate work. My Tasks remains
   List-only; no cross-Project board or guessed command is introduced.
5. **Blocked:** exact desktop/narrow layout parity, design-defined icon and
   text treatments, navigation grouping, dialogs and prototype interactions
   require verified board IDs/revision, source layers and representative
   exports. Existing CSS values and Storybook/mock images are not substituted
   for the requested Penpot source.

## Verification

The narrow frontend architecture source check passed under Node `24.19.0`:

```bash
npm --prefix frontend run check:architecture
```

Focused existing component/facade checks passed: **4 files / 74 tests**
(`MyTasksFacade`: 22; `ProjectDetailPageComponent`: 16; `TaskTableComponent`:
5; `projects-ui`: 31):

```bash
npm --prefix frontend test -- \
  --include='src/app/features/projects/projects-ui.spec.ts' \
  --include='src/app/features/projects/my-tasks.facade.spec.ts' \
  --include='src/app/features/projects/task-table/task-table.component.spec.ts' \
  --include='src/app/features/projects/project-detail-page/project-detail-page.component.spec.ts'
```

Storybook TypeScript compilation passed with no emitted files:

```bash
frontend/node_modules/.bin/tsc --project frontend/tsconfig.storybook.json --noEmit
```

The focused story-provider regression passed: **1 file / 3 tests**:

```bash
npm --prefix frontend test -- \
  --include='src/app/features/projects/my-tasks-page/my-tasks-page.stories.spec.ts'
```

`git diff --check` passed and all ten local documentation links resolved.
The component tests use fixtures and establish frontend behavior only. No
screenshot baseline was approved and no design comparison, backend test,
PostgreSQL test, real-backend browser smoke or hosted CI was executed by this
audit.

The corrected harness passed the full static Storybook build:

```bash
NODE_OPTIONS=--max-old-space-size=4096 npm --prefix frontend run build-storybook
```

A Playwright Chromium probe then served that generated Storybook output and
loaded the actual `features-projects-mytaskspage--tasks-default` and
`features-projects-mytaskspage--tasks-many-rows-bounded-page` iframe stories.
All three cases passed:

| Story / viewport width | Rendered synthetic Task buttons | Console/page errors | API/Hub requests | Browser-storage writes |
| --- | --- | --- | --- | --- |
| Default / 1280 pixels | 4 | 0 | 0 | 0 |
| Many rows / 1280 pixels | 128 | 0 | 0 | 0 |
| Default / 320 pixels | 4 | 0 | 0 | 0 |

Each case verified the named fixture Task and absence of the hidden sentinel
and loading state, then saved a filter through the rendered form and verified
the filter appeared while local storage remained empty. This checks fixture
rendering and in-memory preference interaction; it does not qualify Task-link
navigation, large-Project paging, visual parity, the other stories, or a real
authenticated backend workflow.

## Resume requirements

Wake/reconnect the Penpot plugin, read file revision and both target pages
without changing the user's active page, resolve the actual board IDs, then
inspect geometry, layout, text properties, layers and interactions. Export
representative Project overview/table/board/My Work and related dialog boards.
Only then select bounded P0 UI changes against this maintained implementation,
preserve the existing authority/draft/recovery boundaries, and run focused
keyboard, narrow-layout and browser checks with the design evidence attached.
