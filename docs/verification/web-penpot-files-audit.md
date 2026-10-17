# Penpot Web UI audit: Files and Artifacts

Audit date: 2026-10-17. Source baseline: `05cc6095225ca8dc7271bf395c90ba18f23624db`.
Scope: `frontend/src/app/features/files/`, `frontend/src/app/features/artifacts/`, and read-only inspection of their current route, shared-style, API, and test dependencies.

## Initial design evidence and stop condition

This section records the initial blocked audit. The recovered revision 339 evidence and bounded visual implementation below supersede its design-access status; the initial source inventory and verification record remain historical evidence.

The requested Penpot file is `52a5041f-08a1-80f1-8008-c5a09f8b5f61`.
The requested design targets are `09 Files & Artifacts`, `14 File Hub v2 — Desktop`, `15 Files v2 — Components`, FHV01/FHV02/FHV03/FHV04, and their menus. These are request-supplied targets, not independently inspected board names or identifiers.

The Penpot high-level overview and File API documentation were read. Read-only `currentFile.pages` / `shapeStructure` inspection was attempted without changing pages or mutating the design. Both attempts failed because the connected plugin tab was suspended; the tool reported missing heartbeats of 164 seconds and 204 seconds. No board structure, file revision, geometry, color, typography, interaction, or representative export was obtained by this audit.

**Design status: Blocked.** Board UUIDs and revision remain unverified. No visual parity, design completeness, or FHV-to-component correspondence is claimed. No UI, behavior, route, API, dependency, generated asset, or baseline changes were made. The following inventory is safe preparation from source, not a substitute design.

## Status meanings

- **Existing:** a current source path implements the stated narrow behavior; this does not establish design parity or production qualification.
- **Partial:** some behavior exists but a concrete workflow, contract, or verification gap remains.
- **Missing:** no matching frontend implementation was found in the audited feature paths.
- **Blocked:** the evidence needed to decide or implement safely is unavailable.
- **Deferred:** intentionally outside this bounded frontend pass or dependent on a separately approved contract.

All board-ID cells below are intentionally unverified. Assigning FHV numbers to guessed screen purposes would manufacture design evidence.

## Route, component, API, interaction, and test inventory

Canonical frontend route source: [`app.routes.ts`](../../frontend/src/app/app.routes.ts). Files and Artifact detail are children of the authenticated shell. The explicit Workspace Files route also uses `workspaceContextGuard`.

| Requested target / Board ID | Current route and component | State or API owner | Current interaction and source status | Existing verification source |
| --- | --- | --- | --- | --- |
| Files hub / unverified | `/files`, `/workspaces/:workspaceId/files` → `FilesPageComponent` | `FilesFacade`, `ActiveWorkspaceFacade`; `GET /api/files?workspaceId&page&pageSize` | **Existing:** bounded inventory, server-owned paging, upload status, empty states, and protected-state clearing. **Blocked:** Penpot composition and visual comparison. | `files.facade.spec.ts`; `files-page.component.spec.ts`; `files-page.issue-337.spec.ts` |
| File search and filters / unverified | Same Files routes | `files-search.api.ts`, `FilesFacade`; `GET /api/search` with File type and Workspace/name/type/date/current-uploader facets | **Existing:** submitted query, authoritative rows/count, removable Type/Modified/Owner chips, cancellation, and mismatched-scope rejection. No browser-owned result filtering should replace it. | `files-search.api.spec.ts`; `files.facade.spec.ts`; `files-page.component.spec.ts`; static `angular-smoke.spec.ts` |
| File browser / unverified | Same Files routes → `FileBrowserSidebarComponent` | `FileFolderStore`; `GET /api/file-folders?workspaceId` | **Partial:** asynchronous folder tree, disclosure, resize/collapse, keyboard focus, and folder-move trigger exist. Parent does not bind `folderSelected` or `selectedFolderId`; choosing a folder does not load its contents. Current Files list DTO/query has no folder facet. | `file-browser-sidebar.component.spec.ts`; `file-folders.service.spec.ts` |
| Recent / Starred / Shared views / unverified | Same Files routes | `browserShortcut` local signal; authoritative inventory only for Recent | **Partial:** Recent displays the bounded Workspace inventory. Starred and Shared change the heading and return an empty local list without an authoritative view request; no starring endpoint or persisted state is wired here. Search results take precedence over these shortcuts. | Sidebar specs cover independent emitted shortcuts; no page-level authoritative Starred/Shared coverage found. |
| Upload / unverified | Same Files routes → shared `CoglatasFileUploaderComponent` adapter | `FilesFacade`; multipart `POST /api/files` | **Existing:** Workspace owner identity, backend validation, queue/progress, failed retry, cancellation, and post-success inventory refresh. Type/size policy remains server-owned. | `files-page.component.spec.ts`; `files.facade.spec.ts`; real-stack Files lifecycle source |
| File list / unverified | Same Files routes → shared `AppDataGridComponent`, mobile `RecentFilesListComponent` / `FileRowComponent` | Backend list/search projections; feature-owned column definitions | **Existing:** primary Name/Modified/Owner/Access/Status columns, optional Type/Size/Scan columns, compact/comfortable density, bounded pages, distinct open and selection, and mobile list. No row More/context-menu implementation was found in this feature. | `files-page.issue-337.spec.ts`; `files-page.component.spec.ts`; shared grid coverage |
| Selection and menus / unverified | Same Files routes; normal/contextual toolbar and native View settings disclosure | Page-local selection; `FilesFacade`; server selection-snapshot capture/delete APIs | **Existing:** select page, Shift range, clear selection, scoped all-search-results snapshot, count-aware confirmation, capability-derived delete, and non-atomic partial outcome. Multi-file download and batch Share are disabled. **Partial:** Move toolbar accepts multiple local rows, but the move dialog enables confirmation only for exactly one file. | `files-page.component.spec.ts`; `files.facade.spec.ts`; [`file-batch-selection-v1.md`](../contracts/file-batch-selection-v1.md) |
| Move dialog / unverified | Same Files routes → `FileMoveDialogComponent` | `FileFolderStore`; `GET /api/files/{id}/location`, `POST /api/files/{id}/move`, `POST /api/file-folders/{id}/move` | **Existing:** single-file and folder moves reuse current source/destination versions, exclude descendant folder destinations, and reload folder navigation after an operation. **Partial:** no dedicated component spec found; destination-loading/failed-state submission behavior needs verification. | `file-folders.service.spec.ts`; `tests/functional/files/files-lifecycle-steps.ts` checks persisted move and stale-version rejection. |
| Preview inspector / unverified | Same Files routes → Files page inspector | `FilePreviewService`; `POST /api/files/{id}/download-grants`, then `POST /api/file-download-grants/{grantId}/download` | **Existing:** exact grant identity check, protected Blob preview for supported types, MIME guards, text bounds, unsupported/denied states, URL revocation, overlay focus trap, Escape, and return focus. Opening and selecting remain separate. | `files-page.issue-352.spec.ts`; `files-page.component.spec.ts` |
| Details / Activity tabs / unverified | Same Files inspector → `FileActivityPanelComponent` | Authorized row metadata; `GET /api/files/{id}/activity`; `GET /api/files/{id}/versions/{versionId}/content` | **Existing:** one tabbed inspector, roving tab semantics, secondary details disclosure, typed file-specific activity, historical version viewing, stale-request guards, and MIME checks. It does not read the broad Audit feed. | `files-page.issue-356.spec.ts`; `file-activity-panel.component.spec.ts`; `file-activity-panel.security.spec.ts`; current static `file-activity-version-history.spec.ts`; real-stack Files lifecycle source |
| Sharing state and management / unverified | Same Files routes; inspector sharing dialog | `FilesFacade`; `/api/files/{id}/sharing` and recipient grant/revoke APIs | **Existing:** server-projected Private/Workspace/External state and management capability, versioned policy/grants, permission-safe recipient display, and list/preview reconciliation. Batch Share remains disabled. | `files-page.issue-352.spec.ts`; `files.api.spec.ts`; real-stack Files lifecycle source |
| Artifact detail / unverified | `/artifacts/:artifactId` → `ArtifactDetailPageComponent` | Direct `HttpClient`; `GET /api/artifacts/{artifactId}` | **Partial:** authorized detail title/description/type/status/date with loading/unavailable/error states. Existing response-ID equality check is useful. It does not render the backend's versions, navigate to reports, or expose artifact repository/linking. Nested detail subscriptions are not canceled on parameter changes; no feature protected-state clearer/catch-up or dedicated spec was found. | Backend `ArtifactsController` / `ArtifactService`; no `artifact-detail-page.component.spec.ts` found. |
| Report and Evidence inspector / unverified | `/projects/:projectId/reports/:artifactVersionId`, `/projects/:projectId/tasks/:taskId/reports/:artifactVersionId`, and their `/app/projects/...` route aliases → `ReportReaderPageComponent` | Direct `HttpClient`; Project-scoped report read, refinement preflight/POST, and Task execution-scope read | **Existing:** section/run rendering, citation evidence inspector, current source-scope confirmation, version conflict rejection, and localized refinement. **Partial:** snapshot-only initial route read, uncanceled HTTP subscriptions, no feature protected-state registration, no focus trap/Escape handler for the custom modal, and limited component test coverage. These require dedicated implementation review before changing behavior. | `report-reader-page.component.spec.ts`; backend Artifact report/refinement service and guard tests |
| Artifact repository / linking / unverified | No standalone repository/linking page in the owned feature paths | Project Artifact CRUD/version endpoints exist, but no inspected contract establishes the requested hub/linking model | **Missing / Deferred:** do not infer a new repository, link command, or global list from Project-scoped CRUD. Define the exact Penpot intent and current capability contract before wiring controls; unsupported actions must be explicitly unavailable. | No corresponding feature page, facade, story, or focused component test found. |

The Files layout depends on global feature-scoped rules in [`frontend/src/styles.scss`](../../frontend/src/styles.scss): `app-files-page` is a size container; the browser modifier declares the three named `browser main inspector` grid areas; container rules stack the layout and choose the mobile list. Inspecting only the component's base two-column stylesheet would incorrectly diagnose the existing layout. Shared style changes belong to the integration/shared owner.

## Current contracts that must remain intact

1. Keep session/Tenant/Workspace and realtime authorization clearing ahead of rendering or catch-up. Browser visibility and opaque IDs never authorize a file or artifact.
2. Preserve the current grant/token-backed Files download and preview path. Tokens are transient request data; do not put them in URLs, logs, browser history, signals, or local storage. Do not substitute the legacy direct download endpoint for this UI flow.
3. Preserve server-owned list/search membership, counts, scope, sharing state, redaction, and capabilities. Retain bounded pages and strict response identity checks.
4. Keep all-search-results capture server-owned, actor/Tenant/Workspace/query bound, capped at 100, five-minute, and reauthorized at best-effort non-atomic deletion. A snapshot is not delete authority.
5. File folder moves and activity/history already have current source implementations. Older active summaries that call Move or Activity unavailable are incomplete at this baseline; do not regress current source to those summaries. Broader artifact linking, multi-file download, batch Share, rename, and starring must not acquire fake success or invented persistence.
6. Preserve text-only rendering of supplied content, Blob MIME checks, text-preview bounds, unsupported active-content rejection, object-URL disposal, modal keyboard operation, and denied-state redaction.

## Proposed bounded work after design access is restored

These are candidates, not completed changes or inferred Penpot requirements.

| Priority | Candidate | Required evidence or constraint | Focused verification to add or retain |
| --- | --- | --- | --- |
| P0 | Complete the design-to-source mapping for all requested Files/Artifact boards and menus | Read the actual page/board UUIDs and revision, inspect structure and computed styles, and export representative desktop, inspector, modal, and menu states. No visual editing before this evidence exists. | Capture immutable evidence references and compare built states at the design viewport plus 320 CSS pixels. |
| P0 | Make unavailable sidebar destinations truthful | Verify Starred/Shared intent in Penpot and confirm whether a current authoritative view API exists. Do not derive Shared from one bounded inventory page, invent stars, or turn unavailable views into misleading empty results. | Page-level checks for unavailable messaging, selection/preview clearing, and no unsupported request. |
| P0 | Close Artifact detail route/state races | Follow existing lifecycle patterns: cancel the prior request on parameter changes and destruction, clear protected content on the common boundary, and reject obsolete completions. Do not change authorization or backend contract. | Delayed A → B responses, destroy cancellation, denied refresh, and synchronous clear tests. |
| P0 | Verify Report reader lifecycle and modal access | Confirm report route semantics and current result contracts; add generation/cancellation and clear-boundary behavior only in a separately reviewed code change. Use existing dialog/CDK patterns for focus containment and restoration. | Route change with pending read/preflight/POST, authorization clear, Escape/Tab focus, stale result, and submission closure cases. |
| P1 | Align folder-tree visibility with ARIA expansion | Current `visibleFolders` always includes level-two children when `level < 2`, while root `aria-expanded` reads only `expandedIds`. Verify intended initial expansion against the design and use one effective expansion state. | Assert descendant visibility and `aria-expanded` agree; ArrowRight/ArrowLeft/parent focus; focus recovery after async folder replacement. |
| P1 | Make Move eligibility consistent across toolbar and dialog | Current toolbar permits multiple local rows while `hasMoveTarget()` permits one. Retain single-file command behavior unless a separately approved batch contract exists. Confirm loading/error/version behavior without introducing persistence. | Multi-row disable/help, exact one-file request, failed navigation, stale version, and successful authoritative folder refresh. |
| P1 | Retain the current Activity test routing; extend states when a change warrants it | `playwright.config.ts` already excludes the superseded embedded #356 placeholder scenario through `supersededFileActivityPlaceholderSmoke` / `grepInvert`. The static runner already includes `file-activity-version-history.spec.ts`, which supplies the current empty Activity DTO and asserts the authorized File-specific request. No active stale-placeholder test defect is established. | Retain the existing 320-pixel keyboard, empty Activity, no broad Audit/unrequested version-content fetch, axe/overflow, and focus-return checks. The dedicated static case does not cover ready/denied/loading transitions or historical content; unit specs and the real-stack lifecycle source cover parts of these behaviors. Add browser cases only when subsequent changes require them, and distinguish inspected source from execution evidence. |
| Deferred | Folder-content browsing, rename, stars, repository/linking, batch sharing/download | No corresponding inspected frontend-authoritative contract establishes these complete workflows. Any backend or business-rule expansion requires a separate scope. | Record the missing contract and expose a truthful disabled/unavailable action when the verified design requires one. |

## Verification and evidence limits

The focused existing Angular suite was invoked at the unchanged source baseline with Node `v24.19.0` and the baseline dependency directory, symlinked by the coordinator. No package install or lockfile modification was performed:

```bash
npm --prefix frontend test -- \
  --include='src/app/features/files/**/*.spec.ts' \
  --include='src/app/features/artifacts/**/*.spec.ts'
```

Result: **12 spec files passed; 90 tests passed; exit 0**. Vitest reported 115.58 seconds. Existing output included the npm project-config warnings, a Vite configuration-loader compatibility warning, and the CDK message that `[cdkFocusInitial]` was not focusable in the 320-pixel overlay test. These warnings were retained; the audit did not suppress or repair them. `git diff --check` also passed.

The follow-up configuration discovery command also exited 0:

```bash
node node_modules/@playwright/test/cli.js test \
  tests/ui/angular-smoke.spec.ts tests/ui/file-activity-version-history.spec.ts \
  --list --reporter=list \
  --grep='one keyboard-accessible File inspector|loading only authorized Activity'
```

It selected **2 tests in 1 file**: the current dedicated Activity case in `chromium-desktop` and `chromium-mobile`; the superseded embedded placeholder case was excluded. This validates configured selection only and executes no browser assertions.

The current Files Storybook source contains default, upload, scan, download-denied, quota, override-disabled, preview-disabled, and mobile scenarios. These are mock presentation fixtures, not Penpot evidence or backend compatibility evidence. The current static Activity runner/configuration and `file-activity-version-history.spec.ts` were inspected, including the explicit exclusion of the old embedded placeholder scenario. No Storybook build, production build, browser run, screenshot baseline approval, backend test, PostgreSQL test, or real-stack lifecycle run is claimed by this audit. The static UI and real-stack lifecycle behavior assertions were inspected only. Authoritative screenshot parity still requires the pinned Linux Docker runner specified in `docs/TESTING.md`.

The Files/Artifacts audit agent created no public issue or PR and performed no push, merge, or Penpot mutation. Coordinator-owned integration publication is separate from this agent's work. This audit introduces no API, migration, persistence model, authentication change, authorization change, package, or business rule.

## Authorized Japanese/English presentation follow-up

The user subsequently authorized Japanese/English localization independently of the unavailable visual design. The follow-up starts from integration seed `8590263845e46eecdedf6666c1f098a685fc6e70`. It adds feature-owned catalog entries for Artifact detail, report reading/refinement, and File Activity/version presentation, while reusing existing Files control keys. Dates, counts, fallback labels, held status/error messages, and accessible labels render in the active locale. Typed presentation metadata retains application copy keys without matching or rewriting filenames, folder names, supplied report text, source labels, or raw DTO/enum values. The existing refinement target discriminant selects the localized explanation of the already-established preflight semantics; the response itself stays unchanged.

Mounted locale-switch regressions cover the Files selection, authorized Blob preview, search drafts and authoritative counts, upload/sharing failures, Activity loading/history/version denial, Move destination/error, Artifact loading/detail/denied/error states, and report inspector/refinement feedback. They assert that a language change preserves the current protected data and unsent input, does not recreate the preview Blob, and issues no new HTTP request or mutation. The Workspace-load effect tracks Workspace identity while leaving presentation reads untracked, so locale changes cannot trigger its reset/load work. Existing commands, API bodies, grants, capabilities, authorization boundaries, unsupported actions, and styles are preserved.

The final focused Angular run passed **14 spec files / 99 tests**, exit 0, in 45.85 seconds. `npm --prefix frontend run check:architecture`, application and spec TypeScript checks (`tsc -p frontend/tsconfig.app.json --noEmit` and `tsc -p frontend/tsconfig.spec.json --noEmit`), and `git diff --check` passed. Existing npm/Vite/CDK warnings remain visible. This adds no Penpot geometry, export, revision, or visual-parity evidence and claims no backend, PostgreSQL, production-browser, or screenshot execution. The design block and the non-localization implementation gaps identified above remain open.


## Recovered Penpot source and bounded visual implementation

The actual export became available on 2026-10-17. File metadata identifies CoglatasUI `52a5041f-08a1-80f1-8008-c5a09f8b5f61`, **revision 339**. The received `Cog.zip` SHA-256 is `392f226f6c1092af82f9a73beb318fff5d472ea2fa7fc37c513340be3c14682f`. The coordinator extracted the archive into `/workspace/work/penpot-unified/design/export`; board shape records are under `files/<file UUID>/pages/<page UUID>/<shape UUID>.json`, and the file's `tokens.json` contains the supplied dark/light token sets. Native export PNGs were inspected through their thumbnail media maps, including all seven boards below. No Penpot mutation was performed, and no design assets or private export contents were added to the repository.

The visual pass starts from integrated checkpoint `bf1cafc7012338905c658a5e69663c4c99b4bcec`, preserving the Japanese/English presentation work. The following mapping replaces the unverified board-ID cells in the initial inventory for these inspected targets. **Partial** means the existing authorized workflow received a bounded presentation correction; it does not mean that a sample screen is fully implemented.

| Verified board | Board UUID | Current route and component | Status and bounded result |
| --- | --- | --- | --- |
| FHV01 / File Hub — Dark | `1f711f64-03d8-8014-8008-c73389b99ffb` | `/files`, `/workspaces/:workspaceId/files`; `FilesPageComponent`, `FileBrowserSidebarComponent` | **Partial.** Existing sidebar and inspector surfaces, header, underline tabs, metadata rows, and preview canvas follow inspected component styles. The list/search/upload/quota/selection/grant-backed preview/sharing/Activity/history/Move APIs and commands remain unchanged. The sample dashboard sections are deferred. |
| FHV02 / Project Files — Dark | `1f711f64-03d8-8014-8008-c7338bfbbea7` | The existing Workspace Files screen and folder metadata/move services are the nearest implementation; there is no equivalent Project-scoped file/folder listing route | **Deferred.** No sample Project inventory, file business status, folder counts, folder creation, or relationship records were fabricated. |
| FHV03 / Artifact Repository — Dark | `1f711f64-03d8-8014-8008-c7338e7cb0ec` | `/artifacts/:artifactId`; `ArtifactDetailPageComponent` and `GET /api/artifacts/{artifactId}` provide narrow detail only | **Partial / Deferred.** DTO-backed title/description/type/status/date now use the inspected card/typography palette. Repository categories, Run/Revision/provenance relations, pinned references, comparison, and repository download are deferred. |
| FHV04 / Link File — Modal & Target Picker | `1f711f64-03d8-8014-8008-c73946f6137b` | No authoritative typed File relationship workflow exists in the inspected feature APIs | **Deferred.** No link, draft, target-search, version-policy, reference-scope, or success behavior was invented. |
| FL01 / Files Explorer | `1f711f64-03d8-8014-8008-c5dcce01366d` | Existing Files list/sidebar/inspector | **Partial.** Retains the actual authorized data, bounded pages, state messages, preview and download-grant flow. This is an earlier explorer variant; the outer shell and shared grid belong to the shared implementation owner. |
| FL03 / File Preview & Transfer Queue | `1f711f64-03d8-8014-8008-c5dd4e231712` | Existing Files inspector plus uploader queue | **Partial.** Existing MIME-safe image/text/video/PDF fallback rendering and real transfer states remain. No static workflow illustration, fake PDF page, unsupported zoom/comparison, or sample progress was substituted for actual content. |
| FL06 / Artifact / Report Reader | `1f711f64-03d8-8014-8008-c5dd52b0825d` | `/projects/:projectId[/tasks/:taskId]/reports/:artifactVersionId` and existing `/app` aliases; `ReportReaderPageComponent` | **Partial.** Report/evidence cards use the inspected 900:436 desktop proportion, 24-pixel gap, 8-pixel radius, and typography with shared theme tokens. Supplied report sections, evidence passages, source titles, refinement scope and commands remain intact. Sample findings, Run metadata, manifest/version navigation and actions absent from the actual DTO remain deferred. |

The FHV boards are on page `1f711f64-03d8-8014-8008-c73134f152bd` (`14 File Hub v2 — Desktop`); the FL boards are on page `9b18ec83-d336-8057-8008-c5d02b4ba908` (`09 Files & Artifacts`). Component evidence is on page `1f711f64-03d8-8014-8008-c73134f58c43` (`15 Files v2 — Components`):

| Component evidence | Shape UUID | Inspected geometry and treatment |
| --- | --- | --- |
| Files Secondary Navigation | `1f711f64-03d8-8014-8008-c731dc704785` | 190 × 944, flat `#171f29`, selected `#243a61`, 36-pixel shortcut rows on a 44-pixel rhythm; title 17/700 and labels 12 pixels. The existing resizable 264-pixel default and collapse/tree behavior are preserved, so width parity is not claimed. |
| File Details Inspector | `1f711f64-03d8-8014-8008-c732d191f3e4` | 344 × 968, `#171f29`, title 17/700, 12-pixel tabs with blue underline, preview canvas 312 × 230, 13-pixel section heading, metadata labels/value columns. The existing Preview/Details/Activity tab model is retained rather than substituting unsupported design tabs. |
| File List Row | `1f711f64-03d8-8014-8008-c7323a59fa58` | 844 × 45, radius 4, card `#1c2733`, filename 12/600 and metadata 10–11 pixels. Shared grid rendering remains owned by the shared implementation agent. |
| File Activity Item / Version Timeline Row | `1f711f64-03d8-8014-8008-c7327cd6238c` / `1f711f64-03d8-8014-8008-c732a33a3653` | 398 × 57 and 303 × 51; 10–11-pixel labels and timestamps. Existing live authorized Activity/version states remain; no sample actor, history or version metadata was introduced. |
| Artifact Details Inspector | `1f711f64-03d8-8014-8008-c7331869bc42` | 344 × 968, title 15/700, section title 13/700, labels 10–11 and values 10–12 pixels. Only actual Artifact detail fields receive this hierarchy. |
| FL06 report card / Evidence Inspector | `1f711f64-03d8-8014-8008-c5dd5451bbc3` / `1f711f64-03d8-8014-8008-c5dd56e5ae13` | 900 × 689 and 436 × 689, 24-pixel gap, radius 8, card `#1c2733`, inset 18–24 pixels; report heading 22/700, body 14/400, inspector heading 16/600. Content-driven card heights are retained. |

The source uses Noto Sans JP, primary text `#f0f4fa`, secondary `#a5b1c1`, border `#2b3645`, readable blue `#86acff`, and primary fill `#315bd8`. Feature styles consume the shared semantic palette, including `bg-side`, `bg-card`, `bg-base`, `action-primary-fill`, and `action-primary-text`, whose definitions belong to the shared owner. Light-mode rendering follows the supplied light theme rather than retaining the reader's hardcoded white/light palette.

### Prototype semantics and deferred menus

`FHVSPEC / Files v2 Integration Contracts`, shape `1f711f64-03d8-8014-8008-c744ba43deb7`, labels the source as UI samples and prototype actions. Its contracts distinguish one stored file from typed references, state that relationships grant no read/edit permission, preserve immutable Artifact Run/Manifest references, and require separate backend/storage implementation. Those statements do not authorize new endpoints or business behavior.

Inspected examples include FM01 File Context Menu (`1f711f64-03d8-8014-8008-c737e1a8e9d4`) and FM08 Preview Actions (`1f711f64-03d8-8014-8008-c7382b17a02b`). Existing file search filters, view settings, sharing, history, and single-object Move remain available through their current controls. Unsupported relationship, comparison, target/revision selection, folder creation, evidence/provenance, upload-destination and repository actions from FM01–FM19 remain **Deferred**; this pass adds no fake menu action or saved state.

### Reader state corrections found during mounted verification

Actual Chromium inspection of the unchanged reader stylesheet showed `.dialog-backdrop[hidden]` computing to `display: grid` and `.refine-action[hidden]` to `inline-flex`. The unit-test DOM's user-agent stylesheet instead computed `none`, so attribute-only and ordinary unit visibility checks had masked the browser defect. A feature-scoped `:host [hidden] { display: none !important; }` now preserves the existing loading/error/inspector/refinement visibility states.

Mounted Chromium also reproduced a completed report response whose component fields were `loading: false` and `report.title: Evidence report`, while its visible DOM remained on the loading message until a locale change scheduled rendering. Async report and refinement display callbacks now mark the existing component for checking; the existing preflight focus action runs after the next render. This changes presentation notification only, preserving HTTP methods, URLs, request/response bodies, error keys, source-scope checks, drafts and navigation semantics. The inner report body is an `article`, retaining AppShell's single `main` landmark.

Focused regression coverage adds report success and denial rendering after `fixture.whenStable()` without a manual post-response change-detection call, computed closed-dialog/loading visibility, the report body landmark, rendered preflight dialog focus, and asynchronously rendered refinement failure. Existing locale-switch, authored-content, exact refinement-body and scope-change tests remain.


### Visual-pass verification

The final scoped Angular command passed **7 spec files / 53 tests**, exit 0, in 51.40 seconds:

```bash
npm --prefix frontend test -- \
  --include='src/app/features/files/files-page/*.spec.ts' \
  --include='src/app/features/files/file-browser-sidebar/*.spec.ts' \
  --include='src/app/features/artifacts/**/*.spec.ts'
```

Application/spec TypeScript checks, `npm --prefix frontend run check:architecture`, and `git diff --check` passed. `npm --prefix frontend run build` passed in 37.03 seconds, with an initial bundle of 918.33 kB; the existing 1.02 MB initial and 9 kB component error budgets remain unchanged. The build reports the existing warning thresholds, including this pass's Files styles at 8.62 kB and reader styles at 8.36 kB, above the 4 kB warning threshold. No budget, dependency, lockfile, generated hosted asset or baseline was changed.

A one-off mounted Chromium diagnostic used the actual development app and mocked authorized report/session DTOs. It verified asynchronous report rendering without a locale/manual refresh, the closed backdrop and no-Task refinement controls remaining hidden, evidence open/close, retained supplied multilingual content, zero scoped axe violations in dark and light, and viewport containment at 1536, 800 and 320 CSS pixels. There were no page errors or mutation requests. The current shared owner's dark/light palette files were applied read-only for this diagnostic because shared palette definitions are integrated separately. The measured desktop columns were 759.875 and 368.125 pixels with a 24-pixel gap at the available width, preserving the 900:436 source ratio. Diagnostic screenshots were inspected but are not committed screenshot baselines or a claim of full-board parity. Mocked responses do not establish backend compatibility; the coordinator owns persistent browser regressions and combined shared-style verification. No Docker screenshot approval, PostgreSQL test, real-backend integration qualification, or production publication is claimed by this feature pass.


## Final integrated Files contrast and container follow-up

The pinned integration browser run reported a real light-theme contrast failure in the allowed scan-status badge: the success foreground `#087b65` on its composited success-subtle background `#e1efed` measured 4.4:1 at 12.48-pixel bold text, below 4.5:1. The feature consumer now uses canonical `--coglatas-color-text-primary` for that badge's text, retaining its success-colored border/background, exact status label, scan state, and capability behavior. Global tokens and axe rules are unchanged.

A mounted Chromium diagnostic at 320 pixels reproduced that exact failure in the current built integration and applied the patch's single foreground-token change to all rendered allowed badge instances. The Files surface then passed axe with **zero violations in both light and dark**; the measured text colors were `#29374c` and `#d7e0ed`, and the success borders/backgrounds remained. This diagnostic is a consumer-style reproduction; the coordinator's rebuilt pinned static suite supplies combined integration verification. The existing `files-page.component.spec.ts` passed **21/21 tests** in 19.73 seconds, including scan-blocked/pending download and grant-path behavior. Architecture and diff checks passed. No new unit test merely mirrors the CSS property.

The other File-specific error context was the container-breakpoint assertion. Actual built Chromium measurements found no document or main-content horizontal overflow:

| Viewport width | Files container width | Layout columns | Visible list |
| --- | --- | --- | --- |
| 1440 | 1080 | 3 | Desktop |
| 1100 | 740 | 1 | Desktop |
| 1097 | 737 | 1 | Desktop |
| 1096 | 736 | 1 | Compact |
| 1024 | 664 | 1 | Compact |
| 800 | 768 | 1 | Desktop |
| 769 | 737 | 1 | Desktop |
| 768 | 736 | 1 | Compact |

The inclusive `@container (max-width: 46rem)` boundary is 736 pixels. At a 768-pixel viewport, narrow shell padding leaves exactly 736 pixels; the prior desktop-list expectation was stale. The 1024-pixel compact-list expectation remains correct. No responsive CSS was changed to preserve the obsolete expectation. The coordinator owns the smoke-test correction and the additional 737/736 boundary coverage. No other File-specific layout defect was established by the inspected error contexts.


## Pinned responsive-matrix corrections

The matrix against integrated production `4b3c82ec` exposed two owned presentation defects. First, the citation's expanded attribute depended only on the retained selection, so it stayed true after closing Evidence. Both opening Evidence and returning focus used a microtask before Angular rendered the changed visibility. At narrow widths the report body was still hidden when focus returned. Citation expansion now also depends on `inspectorOpen`; focus enters and leaves the inspector after the next render, while selection/content remain unchanged. A mounted regression asserts rendered inspector focus, collapsed citation semantics and return focus without replacing the selected citation or making another request.

Second, the shared 58-rem container rule forcing preview `position: static` outranked the active feature overlay rule. Actual Chromium at 390 pixels showed a static 390-pixel preview starting at x=16 and ending at x=406, expanding the inspector column and main scroll width by 16 pixels. The feature overlay rule now includes both preview and active-overlay classes so its fixed positioning wins without modifying shared CSS. The 320-pixel override has the same specificity, retaining its 12-pixel padding. Actual corrected preview bounds are x=0/right=390 and x=0/right=320 respectively, with **zero main and document horizontal overflow**. The viewport modal, focus trap, preview grants, MIME handling and existing close action remain.

The additional sidebar hints/empty text and Activity description/privacy/timestamp/actor/history labels used the muted token on light sidebar/card surfaces, where the pinned run measured 4.34:1. These readable metadata consumers now use canonical secondary text. Remaining Files muted consumers are disabled upload, download and preview controls; no theme token or axe rule was changed. The separately reported Login eyebrow belongs to the shared owner and was handed off rather than edited here.

The focused command passed **5 spec files / 26 tests** in 25.65 seconds:

```bash
npm --prefix frontend test -- \
  --include='src/app/features/files/files-page/files-page.issue-352.spec.ts' \
  --include='src/app/features/files/file-activity-panel/*.spec.ts' \
  --include='src/app/features/files/file-browser-sidebar/*.spec.ts' \
  --include='src/app/features/artifacts/report-reader-page/*.spec.ts'
```

Application/spec TypeScript checks, architecture and diff checks passed. Existing npm/Vite warnings and the unit DOM's retained CDK initial-focus warning remain visible. Actual own-worktree Chromium checks passed report open/close focus and collapsed semantics at 768, 390 and 320 pixels; the reader's scoped axe check passed in both themes. Files checks passed fixed-overlay geometry and containment at 390 and 320 pixels, plus sidebar and Activity-inspector scoped axe checks in light and dark using the read-only integrated palette. The preview maintained the existing unsupported-archive fallback and authorized File-specific empty Activity response. These diagnostics used the actual rebuilt development feature components and mocked API responses; no integration build, API/command change, shared-style edit, dependency change, screenshot-baseline update or publication was performed. The coordinator owns the final combined pinned matrix and static run.
