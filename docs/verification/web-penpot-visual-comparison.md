# Penpot revision 339: Web visual comparison

## Source and capture provenance

The sole visual authority is the user-supplied native `CoglatasUI` revision-339
export, file `52a5041f-08a1-80f1-8008-c5a09f8b5f61`. The
[source inventory](penpot-rev339-source-inventory.json) pins archive, page/board
IDs, dimensions and original PNG hashes. Layers, tokens and native frame PNGs
were inspected together. Source PNGs below are copied unmodified from the
export; they are not browser-generated reconstructions.

Before captures use the integrated localization checkpoint
`bf1cafc7012338905c658a5e69663c4c99b4bcec`, before the recovered-source visual
changes. They use the same synthetic supplied names/content and 1536×1024
viewport as the after captures. Before images are diagnostic native Chromium
captures, not approved Linux baselines. The Report before capture records the
actual stale loading display after a successful response; it does not substitute
a completed report.

After captures use the production build with the integrated source changes,
in Playwright **v1.64.0 Noble**, image digest
`sha256:06a9939e57531807f8d5fd76ce44b53165ffb7d7501d87ab10e285c20b1e971f`.
The final after captures use production source
`febb26ce95d7e61f93d11c72a016ae7521df8d73`. The fixture API is confined to `tests/ui/fixtures/core-workflow-api.ts`.
Supplied multilingual text is deliberate test data, not production seed data.
Static captures establish presentation and interaction behavior only; they do
not establish authorization, persistence or frontend/backend compatibility.

## Comparison categories

| Category | Source-backed change | Web adaptation / remaining difference |
| --- | --- | --- |
| Geometry | 176 px app navigation, 254 px My Tasks filter column with 24 px gap, 236 px Messaging feature column, 344 px contextual thread, source report 900:436 column ratio with 24 px gap. | Existing account rail, server-authorized header and browser navigation remain. Source native window controls and unsupported dashboard/feed projections are not copied. Content availability changes panel heights. |
| Typography | 13 px base, source type hierarchy and Noto Sans JP family declaration; localized labels and date/count formatting. | Installed font fallback remains; no new remote font dependency. Source PNG rasterization and browser fonts differ. Raw server/user content retains its language. |
| Color | All 44 `ui.color.*` values across CoglatasDark/Light map exactly to the canonical Web palette; grid, Kanban, status, Files and report consumers use semantic roles. | Small nav headings use secondary text; allowed File badge uses primary text against its success tint; Login, Files metadata and tinted-control small labels use secondary text to meet WCAG. These accessible consumer choices preserve source token values. |
| Spacing | Existing scale maps 32 px desktop / 16 px narrow content insets, source gaps, control density and panel radii. | Coarse-pointer targets remain at least 44 px; narrow content is reflowed instead of scaled. |
| Components | Login brand/form treatment, Project underline tabs and actual counts, distinct DM bubbles / flat Channel rows, Files browser/inspector, report/evidence panels. | Production displays existing HTTP DTOs. No fake statistics, repository/run/provenance rows or unsupported linking controls were added to imitate source `UI SAMPLE` data. |
| Responsive | Captures at 1536×1024, 1366×768, 768×1024 and 390×844, both themes and existing desktop/mobile Chromium profiles; additional 320 px journeys. | My Tasks sidebar stacks, app navigation becomes a drawer, Channel thread stacks below 1200 px and Files switches at its actual container boundary. Full source overlay/state coverage remains partial. |

The matrix checks actual visible Task List/Kanban data, authored Message and
thread bodies, File inspector, report/evidence opening and closing with focus
restoration, single-main landmarks, horizontal containment and zero unexpected
mutation requests. Login, Files and report receive axe checks with no rule
exclusions. It is not a pixel-parity assertion against a board populated with
different sample data.

## Representative original / before / after captures

Each row links full-resolution files. Native reference boards have their original
1600×1000 or 1535/1536×1024 canvas; browser captures are 1536×1024. The original
board IDs and dimensions are pinned in the source inventory.

| Projection | Original native reference | Before | After |
| --- | --- | --- | --- |
| G02 Login | [Reference](assets/penpot-rev339/login-reference.png) | [Before](assets/penpot-rev339/login-before.png) | [After](assets/penpot-rev339/login-after.png) |
| TK01 My Work | [Reference](assets/penpot-rev339/my-tasks-reference.png) | [Before](assets/penpot-rev339/my-tasks-before.png) | [After](assets/penpot-rev339/my-tasks-after.png) |
| Direct Messages | [Reference](assets/penpot-rev339/dm-reference.png) | [Before](assets/penpot-rev339/dm-before.png) | [After](assets/penpot-rev339/dm-after.png) |
| FHV01 File Hub | [Reference](assets/penpot-rev339/files-reference.png) | [Before](assets/penpot-rev339/files-before.png) | [After](assets/penpot-rev339/files-after.png) |
| FL06 Report Reader | [Reference](assets/penpot-rev339/report-reference.png) | [Before](assets/penpot-rev339/report-before.png) | [After](assets/penpot-rev339/report-after.png) |

The native source is not edited to resemble the implementation. The full archive
and thousands of unrelated assets remain outside the repository.

## Executed matrix result

The complete integrated pinned static suite at production source
`80f35a3fa8b4bf5eef56103b997f926c013acac8` passed **159 tests**, with **13 existing
profile skips**, zero failures and zero retries used, in 6.8 minutes. All eight
matrix cases passed: four viewport sizes in each existing Chromium profile,
each checking both themes. Both 1366/320 locale journeys also passed. After the last
small heading-wrap adjustment, all **four focused pinned cases passed**: both
regression snapshots and the 1536 px matrix in both profiles, with both themes
and interaction checks. Exact-final-head hosted CI is recorded in the PR.

## Regression screenshot baselines

The two maintained Linux Angular P0 images are regenerated with the pinned
Noble image after reviewing actual and difference images. Source-backed changes
include the canonical palette, 13 px type, 176 px desktop navigation, decorative
Lucide glyphs, localized Workspace date/attention labels, and stable complete
icons before capture. The pinned section heading stays whole and its toggle
wraps as an item when necessary inside the 176 px rail. The original mobile drawer comparison reported 6,807
differing pixels (about 3%); this is an intended UI change, not a font/platform
exception. Desktop also receives the new source-backed reference even though
its difference remained within its existing tolerance.

Only `desktop-shell-workspaces.png` and `mobile-shell-workspaces-drawer.png`
under `tests/ui/__angular_snapshots__/linux/angular-smoke.spec.ts/` change.
The desktop `0.055` and mobile `0.002` thresholds remain unchanged. The broader
fixture matrix checks interactions and containment separately; these two
regression images alone are not Penpot acceptance.

## Acceptance boundary

The accepted scope is alignment of **supported existing Web workflows**, with
explicit differences above. Complete native-board parity, all operation/state
combinations and unsupported Project Files / Artifact Repository / dedicated
multi-root Thread / Workbench capabilities remain deferred. The integration
[audit](web-penpot-integration-audit.md) records executed checks and hosted
acceptance separately. An image of a mock response is not evidence that a
business operation succeeded against the real backend.
