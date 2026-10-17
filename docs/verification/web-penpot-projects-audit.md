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

## Localization continuation: separately authorized Japanese and English UI

Continuation date: 2026-10-17. The original audit and fixture-build evidence
above describe the earlier `05cc6095225ca8dc7271bf395c90ba18f23624db`
baseline and remain historical evidence. The user subsequently authorized
Japanese/English localization independently of design availability. That
authorization permitted the presentation changes in this continuation; it did
not turn the earlier blocked Penpot review into verified visual parity.

The localization work started from common seed
`8590263845e46eecdedf6666c1f098a685fc6e70`. Project presentation commits
`a4e8d0735317b68266450e19d96c8fc866e7e0c7` and
`ba7e9a377f389de9e2f47a72de6bdeca5e93c20e` were integrated by the coordinator
as `cc7f589b` and `cd6ae61b`, respectively. A read-only continuation review
inspected integration `cd6ae61bb9ef03d820c268240c112eb4c28327a5`; the bounded
follow-up described below fixes its eight identified presentation gaps. These
identities do not establish later integration or hosted CI results.

### Implemented presentation scope

The first two integrated Project commits supplied **549 English/Japanese key
pairs**. This follow-up adds 18, bringing this catalog to **567 matching key
pairs and interpolation placeholder sets**. The feature-local helper accepts
a typed set of application-owned UI copy. Shared adapter templates use explicit
catalog keys. This count measures catalog entries, not complete route or
repository localization.

The ten covered components are:

- Project landing, Project detail, and Project summary cards;
- My Tasks and the maintained Task table;
- Project-create dialog and Task-create page;
- Task detail, Task editor, and reusable Task Brief fields.

Their maintained presentation includes translated headings, native controls,
accessible labels, loading/empty states, common actions, locally generated
validation, machine-category/priority labels, and contextual copy in these
maintained surfaces.
Configured Stage labels and resource names remain separate from those fixed
application labels.

The shared Kanban/Gantt adapter work covers board move controls, accessible
card/count text, WIP warnings, scheduled/unscheduled sections, schedule and
progress forms, dependency actions, and locally generated validation and
vendor-load errors. Those errors retain typed message identity so an open
form can change language without replacing its input or command intent.
Kanban and Gantt register Project dictionaries through the lazy feature
provider; generic adapter shells do not acquire Project registration from
their shared base class.

Counts and applicable numeric displays use the selected locale. Instant
timestamps use locale-aware date/time formatting. Calendar-only dates use the
shared strict `formatDateOnly` helper with its UTC formatting boundary;
form controls and command payloads retain their original `YYYY-MM-DD` values.
No library, API, authorization, database, workflow, capability, or styling
contract is changed by this localization work.

Locale updates are read during rendering and presentation derivation. They do
not reload a facade or replace the Project/Task form model. Focused regressions
exercise Japanese-to-English changes while preserving Task-create draft
values and authorized selections, My Tasks filter-name input and relationship
selection, a board move reason/target Stage, and an open progress validation
error. Additional regressions cover singular/plural removed-choice notices,
compatibility Schedule summaries, Workload counts and canonical item/dependency
labels. These checks also preserve configured Stage text, supplied names,
source timestamps, date-only values and dependency enum values.

### Source-owned content and remaining ownership

The localization pass does not translate arbitrary server strings. User-authored
Project/Task titles, descriptions, comments, Brief values and filter names;
person/Group names; configured Stage and swimlane labels; IDs, enum values and
request identifiers remain source-owned. These values are interpolated into
translated UI framing without being dictionary lookup inputs. Structured
backend warnings, calendar limitations and unknown provider/backend prose
also retain their supplied text unless a separate typed presentation contract
identifies an application-owned message.

Remaining work was handed off explicitly:

- Agent C owns the six residual component folders: Task execution scope,
  execution result, Research Plan, read-only dependencies, progress field and
  Task status badge. Its separate catalog is `project-execution.messages.ts`.
  A Task badge may receive a configured Stage label and must preserve it.
- Agent D owns feedback from `ProjectsFacade`, `ProjectDetailFacade`,
  `MyTasksFacade`, `ProjectCreateFacade` and `TaskCreateFacade`, using
  `project-feedback.messages.ts` and typed presentation metadata. This is
  separate from the component-local validation covered here.

Those handoffs were in progress at this review. Neither their completion nor
their final verification is claimed by this record. The coordinator owns the
combined route/browser and repository-wide catalog checks.

### Integration findings and completed follow-up

At `cd6ae61b`, no accidental translation was found in Project card summaries,
assignee/Group names, parent titles, configured column/Task Stage names or
swimlane labels. The regression deliberately uses configured Stage `Review`
and confirms it stays `Review` while surrounding Japanese copy changes.

The review found eight application-owned presentation gaps outside the C/D
handoff scope. This bounded follow-up completes them:

| Source | Completed local presentation |
| --- | --- |
| Shared adapter shells, compatibility Schedule branch | Both read-only and available Schedule sentences now use typed keys. |
| Shared adapter shells, canonical item heading | Milestone / Derived parent Task / Task labels reuse the existing catalog. |
| Shared adapter shells, dependency labels | Finish-to-Start and Legacy framing and endpoint conjunction are translated; dependency type values and supplied endpoint titles remain unchanged. |
| Project detail `gantt().taskLabel` | The compatibility Task summary uses keyed interpolation, locale-aware calendar dates and typed priority/status labels; Task title and configured Stage text remain supplied content. |
| Project detail Workload tab | Assigned/overdue/estimated/actual-hour framing is keyed and numeric values use locale formatting; supplied member names and role text remain unchanged. |
| Task-create options reconciliation | Removed fields are retained as typed identities and rendered reactively, including singular/plural notices and the review instruction. The same authorized-choice clearing behavior is preserved. |
| Task-create unsent-draft paragraph | The browser-tab draft explanation uses the catalog without changing submission or discard behavior. |
| Task detail context/progress/activity | The Project context paragraph and Group framing, missing-Stage fallback and Activity explanation use keys; supplied Project/Group/Stage values remain untouched. |

The options-reconciliation effect never stores translated notice text or reads
the locale. Changing language only recomputes presentation; it does not repeat
choice reconciliation, clear another field, refresh options, or recreate the
form. Both one-field and two-field removal regressions preserve the remaining
form values and original title input element while switching Japanese to
English.

The completed table is scoped to the reviewed gaps in these owned files. It
does not certify the separate execution/facade handoffs or every route in the
repository.

### Focused verification and limits

Final focused results for the adapter/detail and bounded copy continuation:

| Check | Result |
| --- | --- |
| `coglatas-adapter-shells.component.spec.ts` | 19 tests passed, including open-interaction and canonical item/dependency locale regressions. |
| `project-detail-page.component.spec.ts` | 17 tests passed, including compatibility Schedule/Workload hot switching and preservation of supplied names and Stage text. |
| `task-detail-page.component.spec.ts` | 11 tests passed. |
| `task-create-page.component.spec.ts` | 15 tests passed, including both removed-choice notice cases and preservation of the remaining draft. |
| Project catalog key/placeholder comparison | All 567 pairs matched. |
| Storybook TypeScript compilation with `--noEmit` | Passed. |
| Frontend architecture check and `git diff --check` | Passed. |

The ten-component pass also recorded passing focused runs across
`projects-ui`, Project-create dialog, Project summary, Project-detail Task-create
navigation, Task-create page, My Tasks story fixtures, Task table, Task Brief
and Task editor suites. Together with the detail/adapter suites, these are
scoped implementation checks, not one full exact-integration test run.

Representative final commands were:

```bash
npm --prefix frontend test -- \
  --include='src/app/shared/ui/adapters/syncfusion/coglatas-adapter-shells.component.spec.ts' \
  --include='src/app/features/projects/project-detail-page/project-detail-page.component.spec.ts' \
  --include='src/app/features/projects/task-create-page/task-create-page.component.spec.ts' \
  --include='src/app/features/projects/task-detail-page/task-detail-page.component.spec.ts'
npm --prefix frontend test -- \
  --include='src/app/shared/ui/adapters/syncfusion/coglatas-adapter-shells.component.spec.ts'
frontend/node_modules/.bin/tsc --project frontend/tsconfig.storybook.json --noEmit
npm --prefix frontend run check:architecture
git diff --check
```

English behavior tests now explicitly choose English. Story fixture checks
allow the legitimate `coglatas.locale` preference write and continue to reject
saved-filter writes to browser storage. The earlier zero-storage-write browser
probe above predates localization; it is not current evidence that changing
language writes nothing.

No production/backend/provider test, combined localized browser run, new
Storybook browser build, screenshot baseline approval or Penpot parity result
is claimed by this continuation. Those results belong to the coordinator's
final integration record when executed.

## Recovered design continuation: revision 339

The source became available after the localization pass. The recovered `Cog.zip`
export has SHA-256
`392f226f6c1092af82f9a73beb318fff5d472ea2fa7fc37c513340be3c14682f` and identifies
file `52a5041f-08a1-80f1-8008-c5a09f8b5f61` (`CoglatasUI`), revision **339**.
This supersedes the earlier source-unavailable blocker for this continuation;
it does not rewrite the historical blocked audit or fixture results.

The bounded visual branch starts from integrated checkpoint
`bf1cafc7012338905c658a5e69663c4c99b4bcec`, after the Japanese/English catalogs
and the separately owned execution/facade localization were integrated.
Read-only comparison covered integration `0978db7c8b14db19b73f593352af635d1e5b9045`
and the same unchanged feature presentation at the common checkpoint. This
continuation changes presentation in four owned files; it does not change
facades, API calls, permissions, filter meanings, commands or source content.

### Actual board and layer evidence

All four source boards are 1600 by 1000 pixels. Each native exported board PNG
was visually inspected alongside recursive shape JSON. The retained export
stores shapes under
`files/<file UUID>/pages/<page UUID>/<shape UUID>.json`; `tokens.json` supplies
both `CoglatasDark` and `CoglatasLight` token sets. Layer names are identifiers,
not the source of displayed copy: the PV02 layer named `Text / 68%` contains
`67%`, matching its native preview. Neither sample value is copied into the app.

| Board | Exact board UUID | Page UUID | Feature mapping and evidence |
| --- | --- | --- | --- |
| PV02 Project overview | `1f711f64-03d8-8014-8008-c5d37f4178da` | `9b18ec83-d336-8057-8008-c5d02b3f2ebd` | `/projects/:projectId`, `ProjectDetailPageComponent`, overview tab. Main content `1f711f64-03d8-8014-8008-c5d3807b6d75`: x208/y230, width 1360. Metric card `1f711f64-03d8-8014-8008-c5d380852a55`: 328 by 133, radius 8, border 1; title 16px/600, value 31px/700, note 11px. |
| PV04 Task Table | `1f711f64-03d8-8014-8008-c5db7fdb3bdd` | `9b18ec83-d336-8057-8008-c5d02b3f2ebd` | Project detail List tab and maintained `TaskTableComponent`. Table panel `1f711f64-03d8-8014-8008-c5db825b20e7`: 1360 by 536, radius 7. Title band 46px; column header 35px; rows 61px, with alternating canvas fill. |
| PV05 Board | `1f711f64-03d8-8014-8008-c5dbb60ac896` | `9b18ec83-d336-8057-8008-c5d02b3f2ebd` | Project detail Tasks tab and maintained shared Kanban adapter. Column `1f711f64-03d8-8014-8008-c5dbb7c7235e`: 329.5 by 626, radius 7. Card `1f711f64-03d8-8014-8008-c5dbb818ad43`: 305.5 by 149, radius 7, padding 12; card title 13px/600. Shared column/card styling remains Agent A's responsibility. |
| TK01 My Work | `1f711f64-03d8-8014-8008-c5dc802b462c` | `9b18ec83-d336-8057-8008-c5d02b470ee7` | `/tasks`, `MyTasksPageComponent`. Saved-view panel `1f711f64-03d8-8014-8008-c5dc814dddf5`: 254 by 659, radius 8. Assigned panel `1f711f64-03d8-8014-8008-c5dc821c5666`: 1082 by 439, radius 7, separated by 24px. Semantic rows are 66px. |

Relevant dark source values are canvas `#111820`, card `#1c2733`, border
`#2b3645`, primary text `#f0f4fa`, default text `#d7e0ed`, secondary text
`#a5b1c1` and selected fill `#243a61`. The source uses Noto Sans JP with
line-height 1.4. Page titles are 24px/700; subtitles 12px/400. These feature changes
consume semantic theme tokens; Agent A owns the actual shared palette/font
mapping and shell inset of 32px desktop / 16px narrow. This avoids duplicate
feature padding and independently hard-coded dark-only colors.

The stored interactions establish specific navigation, not every action implied
by a sample label. PV02 Task tab `1f711f64-03d8-8014-8008-c5d37ff0cb39` targets
PV04; Board tab `1f711f64-03d8-8014-8008-c5d380085b20` targets PV05. The first
four TK01 row shapes navigate to TK02 board
`1f711f64-03d8-8014-8008-c5dc86f191fa`, corresponding to maintained Task detail
navigation. PV04 and PV05 contain no stored prototype interactions. The PV02
ProjectIDE button opens a design navigation handoff overlay; it does not define
a supported compiler or production mutation API.

### Current implementation status at this checkpoint

| Board | Reused data boundary and focused evidence | Current status |
| --- | --- | --- |
| PV02 | `ProjectDetailFacade`, authorized `GET /api/projects/{projectId}` and existing operational projections; `project-detail-page.component.spec.ts`. | **Partial**: available counts/header aligned; unsupported overview projections **Deferred**. |
| PV04 | `ProjectDetailFacade`, paged `GET /api/projects/{projectId}/tasks`, maintained row capabilities; `task-table.component.spec.ts` and Project detail suite. | **Partial**: local card theme/geometry aligned; shared desktop grid work separately owned. The previously recorded remote-page-navigation gap is not changed here. |
| PV05 | `ProjectDetailFacade`, existing Kanban snapshot/config/move endpoints and shared adapter; Project detail suite and unchanged model/facade tests referenced above. | Behavior **Existing**; shared board visual alignment separately owned. No new move/config command behavior is claimed. |
| TK01 | `MyTasksFacade`, `GET /api/me/tasks` and `/api/me/tasks/counts`, unchanged browser preference boundary; My Tasks story regressions and browser probe. | **Partial**: supported List/saved filters aligned; source cross-Project Board and bulk commands **Missing/Deferred**. |

The earlier API/authority cross-check remains the static compatibility basis.
These presentation checks do not independently requalify backend authorization,
CSRF, real cookies, PostgreSQL or SignalR.

### Completed bounded alignment and Web adaptations

- Project context uses the measured 24px title hierarchy and 12px date text.
  Project tabs use the source underline selection treatment and minimum 85px
  tab width while retaining the maintained tab IDs, labels and click handlers.
  The retained breadcrumb, operational status and capability-gated Create Task
  control remain visible. Controls retain 44px minimum height, adapting the
  source's 41px tabs to the Web touch target.
- Available overview counts are framed as theme-aware cards with the measured
  133px minimum height, 18px inset, 8px radius, 16px label and 31px value. The app
  continues to render its three actual total/completed/blocked counts; it does
  not substitute the source's four different sample projections.
- My Tasks groups its existing saved-filter section and existing main flow in
  two layout wrappers. At viewport widths of 1200px and above, the sidebar is
  exactly 254px with 24px separation and 659px minimum height. Narrow layouts
  stack the same sections in their existing reading order. No extra desktop
  feature padding is added inside the shared shell inset.
- The same relationship/scope/filter controls, named saved filters, current
  conditions, errors, loading state and paging remain available. Native
  controls use theme colors, borders and 44px targets. Real selected/pressed
  states drive tab/scope styling. Existing semantic Task results use the source
  panel radius, title hierarchy, alternating rows and 66px minimum row height.
  Additional supported filter controls and degraded-status messaging make the
  runtime main flow taller than the sparse source sample; none is suppressed
  to imitate that sample.
- Mobile Task-table cards replace fixed white/dark text colors with semantic
  tokens, using 7px panel radius, 12px padding, 13px/600 titles and 11px labels.
  Existing source Stage text, separate status/blocking meanings, timestamps,
  artifact state, row actions and 44px action targets remain intact. Shared
  desktop grid/board geometry is handled by Agent A.

### Explicitly deferred design affordances

PV02 includes release targets, requirement evaluation, open proposals, an
inline timeline, resource-capacity bars and conflict/impact context. The
maintained overview does not expose all of those authorized projections.
They are deferred pending their real contracts; source sample counts, capacity
percentages, revisions and success states are not fabricated.

The ProjectIDE and Project-operations source overlays contain planning and
management actions beyond this bounded presentation work. Existing supported
activation/Create Task behavior is preserved, while unsupported compiler,
revision and management controls remain deferred. TK01's cross-Project Board,
row selection and bulk Status/Label/Watch operations are also deferred: its
sample `1 success / 1 conflict` is not a backend result. Current List and saved
filter behavior remains authoritative. No source-only Filter/Group by control
is added to the maintained Task table without a corresponding supported query
and state contract.

### Focused verification

The existing focused suites passed after the layout wrappers and presentation
changes: Project detail 17, Task table 6 and My Tasks Storybook fixtures 4
(**27 tests**). The fixture regression still covers Japanese/English switching
without replacing saved-filter input or relationship choice and without HTTP
or Hub transport. Architecture checks and Storybook TypeScript compilation
passed; the production Storybook build completed successfully.

A read-only browser probe served the built fixture on local port 4180 and
exercised English/Japanese in both themes at a 1360px component content width,
a 320px single-column layout in each theme, and the 128-row fixture at 1280px.
The 1360px component viewport corresponds to the source board's inner content
region, not its complete 1600px shell. It measures a 254px sidebar, 24px gutter
and 1082px main column. All cases preserve in-memory named-filter saving,
exclude unauthorized fixture content, and check page overflow and browser
**All seven cases passed**, with zero horizontal document overflow, console or
page errors, and HTTP/Hub requests. The locale/theme preferences are the only allowed browser
storage keys. This is synthetic fixture evidence; no backend workflow or
full-page pixel parity is established by it.

The browser captures are held with the execution evidence under
`/workspace/work/penpot-unified/design/qa/projects-after/`; probe code/results
are `projects-design-probe.cjs` and `projects-design-probe.json` in the shared
work directory. They are not committed product assets or approved screenshot
baselines. Shared palette/shell changes and the combined application require
coordinator integration verification. Project-header/count-card and mobile
Task-table styling compile in the checked suites; this continuation does not
claim dedicated rendered browser captures for those two surfaces.

Reproducible commands for this bounded continuation:

```bash
npm --prefix frontend test -- \
  --include='src/app/features/projects/project-detail-page/project-detail-page.component.spec.ts' \
  --include='src/app/features/projects/task-table/task-table.component.spec.ts' \
  --include='src/app/features/projects/my-tasks-page/my-tasks-page.stories.spec.ts'
npm --prefix frontend run build-storybook
frontend/node_modules/.bin/tsc --project frontend/tsconfig.storybook.json --noEmit
npm --prefix frontend run check:architecture
python3 -m http.server 4180 --bind 127.0.0.1 --directory frontend/storybook-static
node /workspace/work/penpot-unified/projects-design-probe.cjs
git diff --check
```

### Final review follow-up: Board priority labels

A read-only integration review at
`afcdf718250d1c64384222933cc27b6ca2ab2f79` found that the Board's translated
Priority framing still interpolated the mapper's fixed English enum label.
This follow-up maps the four known normalized priorities to the existing
`projectPriorityText` helper. Japanese now renders the priority label as well
as its framing. The underlying enum, snapshot, command values and supplied
Task/assignee/Stage names are unchanged; other supplied fallback text retains
its existing treatment. No catalog entries or API/model contracts are added.

The existing Project detail component suite now has **21 passing tests**,
including one Japanese-to-English rendering case for each of Low, Medium,
High and Critical. The tests deliberately supply `High` as Task title,
assignee name and configured Stage label and confirm those values remain
`High`. The same card element and snapshot survive the switch, with no extra
facade load, move or configuration command. The architecture check and
`git diff --check` also pass.

Angular emits `NG0956` diagnostics because the shared adapter tracks its five
read-only metadata strings by their text, which changes with the locale.
Those static label nodes are recreated; the tested card and interaction state
remain stable. This is a non-blocking shared rendering follow-up, not a lost
form or command-state regression. The coordinator owns final integrated
Storybook and pinned browser results; the preceding capture evidence is not
silently relabeled as an exact run of this follow-up.

```bash
npm --prefix frontend test -- \
  --include='src/app/features/projects/project-detail-page/project-detail-page.component.spec.ts'
npm --prefix frontend run check:architecture
git diff --check
```
