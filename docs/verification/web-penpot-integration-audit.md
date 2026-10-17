# Penpot Angular Web integration audit

Status: **PARTIAL_IMPLEMENTATION**. The design-backed implementation sprint has not
completed. This change contains source/API/test preparation and one Storybook
fixture repair, with no production UI changes or Penpot parity claim.

## Immutable starting point and authorized scope

- Audit date: 2026-10-17.
- Product: `NYGsatoshi/Coglatas`.
- Starting `main`: `05cc6095225ca8dc7271bf395c90ba18f23624db`.
- Initial local branch: `work`, at the same commit, with no tracked or
  untracked changes before creating isolated worktrees.
- GitHub open PR list at the initial audit: empty. No competing open-PR scope
  was identified at that observation.
- Integration branch: `codex/web-penpot-unified-20261017`.
- Read-only specification repository metadata/tree inspected at
  `6a0491c925b4980ce737dfbdf775816c237e44fe`. The repository is private; no
  private specification text is copied into these records and no writes were
  made there. This tree inspection is not an approved-specification or
  design-export qualification.

The current task explicitly retains Angular for Web and leaves future Desktop
work to Avalonia. Older migration documents and their issue names do not
authorize deleting or replacing this Web application. No migration gate,
SEC-ARCH decision, `PRE-AVALONIA: BLOCKED` state, schema, RLS, API authorization,
Tenant boundary, business rule, dependency, lockfile, or CI policy changes are
included.

## Design evidence and blocking condition

The first simple Penpot metadata read succeeded and returned:

| Property | Observed value |
| --- | --- |
| File name | `CoglatasUI` |
| File ID | `52a5041f-08a1-80f1-8008-c5a09f8b5f61` |
| Current page | `Coglatas · Chat UI` |
| Current page ID | `52a5041f-08a1-80f1-8008-c5a09f8b5f62` |
| Current revision | **Not retrieved** |
| User-supplied prior revision | `339`; not independently verified |

The subsequent all-page board inventory timed out. Smaller read-only metadata
and targeted shape requests then reported a suspended plugin tab, including
the Coordinator retry with no heartbeat for 312 seconds. After the user
reopened and restarted the plugin, the heartbeat age reset
but a read still failed at 46 seconds; a subsequent read returned
`McpServerError: Connection failed.` The export endpoint also failed with the
suspended-plugin diagnostic. Reading the MCP overview/API documentation
succeeded earlier, but it does not establish access to the design's content.
No design mutation or page navigation was performed.

No verified board UUID, shape tree, token set/value, typography, geometry,
layout, component state, prototype interaction, image, overlay, or PNG/SVG
export was obtained. No matching saved design export was found in the product
repository or the initial reference directories. The user later supplied
`CoglatasUI.penpot`, but the transfer tool rejected it because it exceeds the
32 MiB executor transfer limit. The supplied `penpot.html` (283,280 bytes) is
the generic Penpot 2.18.4-RC3 application shell with external module/config
references; it contains no target file UUID, requested board labels, token
sets or design shape data. It is not a usable design export. A transferable
archive (each part below 32 MiB) or accessible download URL is still needed. The specification path tree
did not supply an independently verifiable replacement export. Existing SCSS
and mock stories are code evidence, not a substitute for Penpot.

The user's explicit unavailable-design fallback therefore limits work to safe
nonvisual preparation and blocker records. The user reopened and restarted the
plugin in this session. The Coordinator also asked to keep the Penpot/plugin
window visible; these recovery attempts have not yielded design content.
Until the connection recovers, design-dependent production changes remain
blocked. Revision 339,
`CoglatasDark`, `CoglatasLight`, and the requested board labels must not be
reported as newly inspected evidence.

## Parallel ownership and integrated work

All four worktrees started from the exact same starting `main` commit. Only
the Coordinator creates the single integration PR. Agent branches are local
isolation branches and do not have individual PRs.

| Owner | Local branch | Exclusive production area | Delivered preparation |
| --- | --- | --- | --- |
| Agent A | `codex/web-ui-shell-20261017` | `layout/`, `shared/`, global styles/theme; shell/auth/Workspace presentation | [Shell audit](web-penpot-shell-audit.md), including token destinations, live versus incomplete shell controls and existing recovery boundaries. No source change. |
| Agent B | `codex/web-ui-project-20261017` | `features/projects/` | [Project/Task audit](web-penpot-projects-audit.md); repaired the My Tasks Storybook scenario token. No production component change. |
| Agent C | `codex/web-ui-communication-20261017` | `features/messaging/` | [Communication audit](web-penpot-communication-audit.md), including separate Channel, DM, saved-message and preference contracts. No source change. |
| Agent D | `codex/web-ui-files-20261017` | `features/files/`, `features/artifacts/` | [Files/Artifacts audit](web-penpot-files-audit.md), including existing Move/Activity and absent folder-view/repository contracts. No source change. |
| Coordinator | `codex/web-penpot-unified-20261017` | Integration, routes, root browser tests, CI observation and final review | Reviewed and cherry-picked the isolated commits, corrected an audit's obsolete Activity-test interpretation, and owns this evidence record and single draft PR. |

Common styles had one assigned writer (Agent A); no agent changed them.
Route/contract coordination remained with the Coordinator. No shared file was
edited concurrently. Existing dependency directories were linked into the
isolated worktrees; those local symlinks are not committed.

## Requested page coverage

`Existing`, `Partial`, `Missing`, `Blocked`, and `Deferred` describe the narrow
source or evidence state, not completion of the requested design. All visual
comparison cells are **Blocked**. Board names below came from the request;
board UUIDs and design-to-code correspondences remain unverified.

| Requested Penpot page / target | Current source mapping and disposition | Detailed mapping |
| --- | --- | --- |
| 00 Design System; DS01 / DS02 | **Existing:** dark/light/density primitives, shared components and adapters. **Blocked:** actual token/typography/state comparison and mapping. | Shell audit |
| 01 AppShell & Authentication; G02 / G04 | **Existing:** `/login`, authenticated shell, navigation, search, notifications and Continue working on Workspace Projects. **Partial:** bounded shell state/accessibility gaps. | Shell audit |
| 02 Workspace & Tenant; W01 | **Existing:** `/workspaces`, capability-gated create, authorized Workspace switching and Tenant/session boundaries. **Blocked:** target design. | Shell audit |
| 03 Project Preview; PV02 / PV04 / PV05 | **Existing/Partial:** `/projects`, `/workspaces/:workspaceId/projects`, `/projects/:projectId` overview/table/board/Gantt. Large-list paging and aggregate completeness remain gaps. | Project/Task audit |
| 04–06 ProjectIDE, Versioning, Analysis | **Deferred:** navigation/reference review against unavailable design; no new compiler, simulation or Branch/Merge persistence. | Project/Task audit |
| 07 Task & My Work; TK01 | **Existing:** `/tasks`, Task-create/detail/editor, current My Tasks list and filters. **Blocked:** design alignment and required screen states. | Project/Task audit |
| 08 Communication; Coglatas · Chat UI | **Existing:** `/messages`, Workspace Channel, `/dm/:conversationId`, contextual thread, saved messages and settings. **Partial:** documented dialog/search/keyboard candidates. Dedicated multi-thread workspace parity is unverified. | Communication audit |
| 09 Files & Artifacts | **Existing/Partial:** Workspace Files hub, grant-backed preview, sharing, Activity/version reads; bounded Artifact detail/report reader. | Files/Artifacts audit |
| 11 Dialogs & Responsive | **Existing/Partial:** shared CDK dialog and feature-specific dialogs/responsive layouts. **Blocked:** overlay IDs, exact states and requested viewport parity. | All four audits |
| 12 Menus & Popovers | **Existing/Partial:** feature disclosures/actions and shared primitives. Missing keyboard/focus candidates are recorded, not changed. | All four audits |
| 13 Overlay UX & Coverage | **Existing/Partial:** retry/conflict/denied/loading/confirmation source paths. **Blocked:** operation-by-operation design coverage and runtime qualification. | All four audits |
| 14 File Hub v2 — Desktop; FHV01 / FHV02 / FHV03 / FHV04 | **Partial/Missing:** existing Workspace hub and Task File association do not establish complete Project Files, Artifact Repository or Link File picker workflows. No contract is invented. | Files/Artifacts audit |
| 15 Files v2 — Components | **Existing/Partial:** browser/sidebar/grid/preview/move/upload/shared adapters. **Blocked:** component hierarchy, state and visual comparison. | Files/Artifacts audit |

The feature audits provide the requested Route → Component → Data Source →
Interaction → Test continuation maps. The active route authority is
[`app.routes.ts`](../../frontend/src/app/app.routes.ts). The architecture
inventory checks passed against the current 38-entry route set; the existing
[source inventory](../migration/avalonia/angular-frontend-inventory.json)
retains its historical pin and does not redefine this task's Web direction.
No new route or broad state-management foundation was introduced.

## Actual repair, reuse and API compatibility

The frontend code change is confined to
[`my-tasks-page.stories.ts`](../../frontend/src/app/features/projects/my-tasks-page/my-tasks-page.stories.ts).
The story rendered `MyTasksPageComponent` but supplied
`COGLATAS_PROJECTS_MOCK`; `MyTasksFacade` reads `COGLATAS_MY_TASKS_MOCK`.
Default and scenario decorators now supply the consumed token. The browser
probe additionally reproduced initial-navigation errors and an anonymous
session resetting fixture rows. Story-local synthetic session, passive
transport and in-memory preference providers isolate those effects; initial
router navigation is disabled for the iframe. Only explicitly authorized
synthetic rows from the existing fixture are passed to its My Tasks scenario.
The existing `PROJECTS_SCENARIOS`, page, facade, grid and test infrastructure
are reused. A focused test composes the actual story decorators and checks
settled rendering, no HTTP and no browser-storage writes. No production
component, service, facade, API or mock-success path is created.

The audits trace current frontend calls to controller/service definitions and
preserve cookie/CSRF, current server capabilities, scoped list membership,
grant-backed downloads, request cancellation, authorization clearing, draft
partitioning and realtime invalidation ownership. Static source matching and
mocked tests do not prove browser/backend or PostgreSQL compatibility.

## Verification record

The Coordinator's baseline production build and full unit suite were started
at `05cc6095225ca8dc7271bf395c90ba18f23624db`. Integrated audit commits do not
change production app inputs. The final Storybook build/browser check ran after
the fixture repair at Agent B commit `2f46ade85bffc05ad370b0bab7638aefdc59044b`
(cherry-picked as `b0a53708`), with the same final story/test contents. Exact command results are finalized before PR creation;
hosted final-head results belong in the PR body to avoid self-referential
evidence commits.

| Check | Command / evidence | Result and limit |
| --- | --- | --- |
| Angular production build | `npm --prefix frontend run build` | Passed. Initial total 906.07 kB; existing initial/style budget warnings retained. No performance-improvement claim. |
| Full Angular unit/component suite | `npm --prefix frontend test` | 122 files / 1,161 tests passed; 0 failed/skipped. Mocked tests do not establish backend integration. |
| Architecture source | `npm --prefix frontend run check:architecture` | Passed. |
| Architecture/tooling suite | `npm --prefix frontend run test:architecture` | 36 passed, 0 failed/skipped. Includes current source/route inventory checks. |
| Project/Task focused suite | Agent B command in the linked audit | 4 files / 74 existing tests passed, plus the new actual-story regression 1 file / 3 tests. Storybook TypeScript compilation also passed. |
| Files/Artifacts focused suite | Agent D command in the linked audit | 12 files / 90 tests passed at unchanged production source. |
| Storybook build and browser rendering | `NODE_OPTIONS=--max-old-space-size=4096 npm --prefix frontend run build-storybook` | Final build passed. After fixing the reproduced NG04002/loading fixture failures, 3 actual-story browser cases passed (default/many-row desktop and default 320-pixel); 0 console/page errors, API/Hub requests or browser-storage writes. Saved filters remained in memory. |
| Static Angular Playwright | `PLAYWRIGHT_SKIP_BUILD=1 npm run test:ui:angular` with locally installed Chromium | 142 passed / 13 existing skips / 1 mobile screenshot mismatch (4,860 pixels, ratio 0.02) on Debian 13. Mocked APIs; native diagnostic run, not pinned Docker baseline approval. The unresolved host comparison was checked separately in the repository-pinned Playwright v1.64.0 Noble image: Desktop and Mobile both passed (2/2), with no baseline update. |
| License guard regression | `npm --prefix frontend run test:syncfusion-license` | 4 passed, including missing/blank license rejection and no runtime license registration. |
| Licensed build prerequisite | `npm --prefix frontend run build:licensed` | Blocked: `SYNCFUSION_LICENSE is not configured.` Activation exited 1 before the licensed build. The license guard was preserved. |
| Real-backend smoke prerequisite | Existing isolated Compose configuration validated with `config --quiet` | Configuration passed; stack/browser not started because the required licensed app-build secret is absent. Cookie/CSRF/authorization/PostgreSQL journeys remain unverified locally. Host .NET SDK and PostgreSQL test connection are also unavailable. |
| Penpot export / visual comparison | Target board PNG/SVG versus same-state built screen | Blocked before capture; no source board, revision or export. No Penpot comparison was approved; existing screenshot baselines were not updated. |
| Pinned Linux baseline comparison | Repository runner in `mcr.microsoft.com/playwright:v1.64.0-noble`, image digest `sha256:06a9939e57531807f8d5fd76ce44b53165ffb7d7501d87ab10e285c20b1e971f`; existing screenshot cases only | 2 passed / 0 failed/skipped. Reused unchanged production output and installed root dependencies mounted read-only; no package install or threshold change. Does not establish Penpot parity. |
| Whitespace / documentation links | `git diff --check`, local changed-document link validation | Passed for all five audit documents; no unresolved local links, invalid UTF-8 or conflict markers. |

Existing npm project-config warnings, unit fixture/ARIA warnings and bundle
budget warnings are not hidden or reclassified as fresh product fixes. A
before/after visual is unavailable because there is no design export and no
production visual change. The requested viewport matrix (1536×1024,
1366×768, 768×1024, 390×844) has no Penpot comparison result.

## CI, review and merge boundary

The live branch rules were read through
`GET /repos/NYGsatoshi/Coglatas/rules/branches/main`. At observation they require
strict up-to-date status checks from GitHub Actions (integration ID 15368):

- `build-test`
- `frontend-test`
- `security-scan`
- `publication-readiness`
- `functional-fast`
- `performance-fast`

The pull-request rule permits merge/squash, requires resolution of review
threads, and currently requires zero approving reviews with code-owner review
disabled. `.github/CODEOWNERS` still assigns `@NYGsatoshi`; it was not changed.
Rules must be reread before any eventual merge; a successful subset of checks
does not establish acceptance. No self-approval, bypass, force merge, check
weakening, workflow skipping or forced update is authorized.

The starting-main check observation showed successful applicable jobs,
including frontend/build/security, licensed real-backend, Functional and
Qodana lanes, under Main artifact run `39003817265`, with performance-fast
under run `39003817205`. Existing conditional skips were present. This is
baseline health evidence only and is not the new PR's CI result.

The planned single integration PR must remain **draft and blocked** while design evidence
and the requested implementation are incomplete. Passing preparation checks
must not trigger merge or closure of existing feature/security/migration
issues. Relevant follow-up ownership includes #270 (large-Project delivery),
#481 (public HTTPS), #482 (cross-screen regression), #768 (existing freeze
policy), and #1144–#1152 (SEC-ARCH gates); this change grants none of them
completion credit.

## Resume and rollback

1. Wake `CoglatasUI` and keep its MCP plugin active; retrieve the actual
   revision and changes from the supplied reference, exact target page/board
   UUIDs, layer/component/token/type/layout/state data and representative
   exports. Preserve evidence before choosing bounded changes.
2. Resume the same four ownership slices using the existing services/facades.
   Resolve only approved existing-API behavior; mark unsupported persistence
   as Deferred/Blocked without fake success.
3. Exercise the requested core journeys, keyboard/IME/focus, protected-state
   boundaries, responsive matrix and same-state visual comparisons. Supply
   the licensed runtime prerequisite through the supported environment setup
   for real-backend checks; never print or commit its value.
4. Update the same PR, inspect every applicable final-head check and review,
   and perform normal merge only after scope and acceptance are satisfied.

This preparation needs no schema/data rollback. Reverting the story-only
repair restores the previous fixture provider; audit documents can be reverted
independently. No production UI rollback or deployed-state change is needed.
