# Web Penpot audit: AppShell, design system, authentication and Workspace

Current status: **Partial**. The recovered native Penpot export now supports a
bounded shared-theme, navigation, login and adapter presentation implementation.
This record does not claim full screen parity or new product workflows.

## Verified design identity

- Audit date: 2026-10-17.
- Visual implementation baseline: `bf1cafc7012338905c658a5e69663c4c99b4bcec`.
- Branch: `codex/web-design-shell-20261017`.
- Native archive: `Cog.zip`, 65,710,792 bytes; SHA256
  `392f226f6c1092af82f9a73beb318fff5d472ea2fa7fc37c513340be3c14682f`.
- File: `CoglatasUI`, UUID `52a5041f-08a1-80f1-8008-c5a09f8b5f61`,
  `revn: 339`, modified `2026-10-12T13:52:15Z` in its exported metadata.
- Evidence: native `tokens.json`, page/shape JSON and full-size native frame PNG
  objects, read locally after the archive was recovered. MCP remains unavailable;
  the earlier disconnected-plugin failures remain historical evidence only.
- No private specification text or large source archive is copied into this repo.

## Source-to-implementation mapping

Shape IDs below belong to file revision 339. Coordinates are the native shape
geometry, not browser screenshot measurements. Status describes this bounded
implementation; final integrated browser/real-backend qualification is separate.

| Board | Verified board UUID | Source evidence | Implementation and remaining boundary |
| --- | --- | --- | --- |
| DS01 | `1f711f64-03d8-8014-8008-c5d1c32e3037` | Native 1600×1000 frame; exact `CoglatasDark.ui.color` and `CoglatasLight.ui.color`; body 13–14 px, Noto Sans JP, line height 1.4; spacing 4/8/12/16/24/32/48 px | **Existing, aligned in this batch:** both semantic palettes, 13 px body/1.4, Noto first with existing platform fallbacks. Existing spacing indices retained; no font download/dependency added. Font availability and all feature-local type overrides remain platform/feature qualifications. |
| DS02 | `1f711f64-03d8-8014-8008-c5d20190cb26` | Header `1f711f64-03d8-8014-8008-c5d201b5419a` 1600×56; nav `1f711f64-03d8-8014-8008-c5d202a1f718` width176; primary `1f711f64-03d8-8014-8008-c5d205e5b454` 176×38/r6; field `1f711f64-03d8-8014-8008-c5d208083eb7` 374×42/r6 | **Partial:** compact 176 px feature menu, 18 px Lucide glyphs, 40 px desktop navigation rows, existing labelled links/Pinned controls preserved. Shared filled-primary colors, secondary/status surfaces and focus maintained. Supported account/context/actions and right rail retained; this is not a source-identical single-rail 56 px header. |
| G02 | `1f711f64-03d8-8014-8008-c5d946b1791b` | Form `1f711f64-03d8-8014-8008-c5d94708128e` 554×592/r12; brand panel `1f711f64-03d8-8014-8008-c5d947f3b4f3` 597×667/r16; heading `1f711f64-03d8-8014-8008-c5d947134e7c` 23/700; brand heading `1f711f64-03d8-8014-8008-c5d948003e55` 30/700 | **Partial:** source-backed brand, side/card surfaces, two-column desktop form and narrow stacked layout; localized descriptive copy. Existing credential/CSRF/tenant resolution/error/navigation behavior retained. SSO/reset and unsupported simulation/governance tiles omitted. Controls keep 44 px minimum rather than native34/38 px. |
| G04 | `1f711f64-03d8-8014-8008-c5d93fda2ae2` | Native 1600×1000; content starts x208 after176 px rail; header `1f711f64-03d8-8014-8008-c5d93fe99b09`, nav `1f711f64-03d8-8014-8008-c5d93ffc2644` | **Partial:** common content inset 32 px desktop/16 px narrow, source colors/icons. Existing reauthorized Continue-working remains on Workspace Projects. Standalone global Home and source-wide recent/draft aggregation are **Missing/Deferred**, with no fabricated rows or totals. |
| W01 | `1f711f64-03d8-8014-8008-c5d9fc8b03b4` | Header `1f711f64-03d8-8014-8008-c5d9fc975ac3`, nav `1f711f64-03d8-8014-8008-c5d9fca6dbda`; context `1f711f64-03d8-8014-8008-c5d9fcd51540`; main `1f711f64-03d8-8014-8008-c5d9fd80aeac` | **Partial:** existing DTO-backed Workspace cards, counts, attention, create and context behavior consume aligned shared tokens/inset/action colors. The source Group explorer, Workbench history and unsupported cross-Workspace aggregates remain **Deferred**. This batch does not claim W01 content/layout parity. |
| PV05 shared Board | `1f711f64-03d8-8014-8008-c5dbb60ac896` | Board `1f711f64-03d8-8014-8008-c5dbb7b7a0b0` 1360×626; column `1f711f64-03d8-8014-8008-c5dbb7c7235e` 329.5×626/r7/canvas; card `1f711f64-03d8-8014-8008-c5dbb818ad43` 305.5×149/r7/panel;14 px gutter,12 px card padding,15 px vertical gap; title13/600, kind10/400 | **Partial:** shared Kanban column/card surfaces, radii, gutters, typography and spacing aligned. Cards grow for real fields and keyboard move controls; no fixed source149 px card height, invented stages or modified transitions. Narrow uses a single-column projection. |
| PV04 shared Table | `1f711f64-03d8-8014-8008-c5db7fdb3bdd` | Panel `1f711f64-03d8-8014-8008-c5db825b20e7` r7; header `1f711f64-03d8-8014-8008-c5db82982cec` top fill; alternate row `1f711f64-03d8-8014-8008-c5db83ee503c` canvas fill | **Partial:** shared AG/Syncfusion grid panel/header/alternate-row paint,13 px body/r7. Existing native adapter row sizing, columns, authorization and action behavior retained. Project-local TaskTable geometry belongs to the Project batch. |
| RS02 / RS03 | `1f711f64-03d8-8014-8008-c5e18ce66658` / `1f711f64-03d8-8014-8008-c5e1a670576a` | Narrow/mobile projection and high-contrast/keyboard-focus sheets on page `9b18ec83-d336-8057-8008-c5d02b506b01`; RS02 has390×820 examples | **Partial:** existing drawer/focus/keyboard behavior retained; narrow layouts rearrange, with44 px coarse controls. Existing focus3 px remains stronger than the DS02 primary's2 px outer white stroke. No claim of full390 px/source-only geometry or every responsive overlay. |

DS01/DS02 page: `9b18ec83-d336-8057-8008-c5d02b353dc8`.
G02/G04 page: `9b18ec83-d336-8057-8008-c5d02b3ab550`.
W01 page: `9b18ec83-d336-8057-8008-c5d02b3d72aa`.
PV04/PV05 page: `9b18ec83-d336-8057-8008-c5d02b3f2ebd`.

## Token contract

Both theme files map canonical `ui.color.*` values directly. Existing semantic
roles remain stable; explicit base/side/card/input/heading/violet and filled-action
roles avoid overloading a readable link color as a filled button.

| Canonical source role | Dark | Light | Product semantic role |
| --- | --- | --- | --- |
| base | `#0D141C` | `#F4F7FB` | `bg-base` |
| canvas | `#111820` | `#FFFFFF` | `bg-canvas` |
| top | `#151C25` | `#FFFFFF` | `bg-shell` |
| rail | `#141B24` | `#EDF2F8` | `bg-rail` |
| side | `#171F29` | `#F1F5FA` | `bg-side`, `bg-sidebar` |
| panel | `#1C2733` | `#FFFFFF` | `bg-surface`, `bg-card` |
| input | `#172230` | `#F8FAFE` | `bg-control`, `bg-input` |
| raised | `#202A37` | `#F3F7FC` | `bg-elevated`, `bg-surface-subtle` |
| hover | `#222D3B` | `#E8EEF8` | `bg-hover` |
| selected | `#243A61` | `#DFEAFD` | `bg-selected`, `bg-rail-active` |
| white | `#F0F4FA` | `#142033` | `text-heading` |
| text | `#D7E0ED` | `#29374C` | `text-primary` |
| muted | `#A5B1C1` | `#526174` | `text-secondary` |
| dim | `#8D9EB5` | `#64748B` | `text-muted` |
| border | `#2B3645` | `#CCD7E5` | `border-default` |
| borderStrong | `#344151` | `#A7B7CC` | `border-strong` |
| blue | `#86ACFF` | `#315BD8` | `action-primary`, `focus`, `info` |
| primary | `#315BD8` | `#315BD8` | `action-primary-fill` |
| mint | `#34CBA8` | `#087B65` | `success` |
| violet | `#8B82FB` | `#6753BD` | `violet` |
| amber | `#F5B45D` | `#915B0B` | `warning` |
| red | `#F47B7B` | `#B42333` | `danger` |

Use `--coglatas-color-action-primary-text` (`#F0F4FA` in both themes) on the
filled primary, preserving `text-inverse` for its existing purpose. This name
satisfies the unchanged architecture check; the former `text-on-action` name
is deliberately rejected by that check.

The native DS02 primary raw shape and some G02 controls still use legacy
`#3768F5`; the canonical `ui.color.primary` and DS01 primary swatch are `#315BD8`.
This implementation follows the canonical token, records that source discrepancy,
and does not claim the older raw shape is an exact color match. Hover darkening
and semantic subtle-status fills use derived `color-mix` states where no exact
canonical equivalent exists. These derived states are implementation adaptations.

`ui.space.2/3/4/6` map by their values8/12/16/24 px. The existing product's
`space-5` is24 px and `space-6` is32 px; indices are not silently renumbered.
Control radius remains6 px; the source's7 px panel/card radius has a separate
`radius-panel` role. Source typography is a font-family preference with platform
fallbacks, not proof that Noto Sans JP is installed on every host.

## Verification and limits

- Native token comparison: all 44 canonical `ui.color` mappings (22 roles × 2
  themes) match the recovered `tokens.json` directly.
- Focused Angular tests: auth/login, navigation, theme, status and dialog 5 files /
  24 tests passed; shared Kanban/AG/Syncfusion adapter 3 files / 24 tests passed.
  Navigation 7 tests passed again after the final decorative-icon defer boundary.
  The navigation fixture setup allows 30 seconds for cold deferred third-party
  module transformation; test assertions and application timing remain unchanged.
- `npm --prefix frontend run check:architecture`: passed, with the original
  legacy-token rejection unchanged.
- `npm --prefix frontend run build`: passed at initial 927.04 kB under the
  unchanged 1.02 MB hard limit; existing warning budgets remain reported.
- Initial baseline in the retained checkpoint build log:918.33 kB. The first
  eager icon implementation failed the hard limit; the corrected version defers
  only decorative glyph rendering with an immediate trigger and fixed 18 px
  placeholder. Links, labels, routing, capabilities and focus remain synchronous.
  The resulting icon component chunk is3,467 bytes, plus a 122,315-byte shared
  Lucide chunk now requested immediately after rendering. That shared module
  includes previously lazy status glyphs: bounded initial size does not mean
  those bytes disappear or that first-load total requested JavaScript is reduced.
- Diagnostic static Chromium probe: dark/light at 1600 and 320 CSS px, login and
  Workspace shell; no horizontal document overflow,44 px login submit, desktop
  content inset 32 px/narrow 16 px, and real navigation glyphs appear without user
  interaction. The mobile capture waits for drawer transition completion as well
  as glyph visibility. At1600 px, the responsive form is 554.47×592 and the brand
  panel 597.53×667; positions differ from the native drawing by about 8 px.
- This probe is local static/mocked evidence. The integration owner runs combined
  browser, mobile, accessibility and real-stack gates. It does not establish
  backend compatibility, authoritative Linux screenshot parity or full Penpot
  fidelity.

## Historical initial audit

The remaining record preserves the earlier blocked-design audit and the separately
authorized localization implementation. Its statements about unavailable design
and unchanged styling describe that earlier checkpoint, not the recovered-source
batch above.

Status: **Blocked** for design comparison and implementation. The original
source audit below predates the separately authorized localization follow-up
recorded at the end of this document.

## Evidence identity and design boundary

- Audit date: 2026-10-17.
- Product baseline: `05cc6095225ca8dc7271bf395c90ba18f23624db`.
- Agent branch: `codex/web-ui-shell-20261017`.
- Connected Penpot file: `CoglatasUI`,
  `52a5041f-08a1-80f1-8008-c5a09f8b5f61`.
- The Coordinator's simple metadata read returned the file identity and the
  current page `Coglatas · Chat UI`,
  `52a5041f-08a1-80f1-8008-c5a09f8b5f62`. This is not evidence for the shell
  pages or boards.
- Revision `339` is the user-supplied investigation reference. The current
  revision, revision difference, target-page IDs, board IDs, shape structure,
  tokens, component hierarchy and representative exports could not be read.
- After reading Penpot's high-level overview, this agent attempted a read-only
  `penpotUtils.getPages()` and targeted board query. The tool returned:
  `The Penpot plugin tab appears to be suspended by the browser (no heartbeat for 124s)`.
  The Coordinator's broader inventory also timed out. Neither response
  contained usable target-board data.
- No verifiable replacement export was available in the product repository.
  The user's explicit fallback therefore applies: restrict work to safe
  nonvisual preparation and blocker documentation. Do not derive replacement
  colors, spacing, typography, layout or interactions from board names.

The requested board names below are investigation targets supplied by the user,
not verified design findings. `Unretrieved` is deliberate; no UUID is inferred
from a name or from the current Chat page.

## Screen and interaction mapping

Classification describes current product source. **Existing** means the named
implementation is wired in source; it does not mean visual parity, real-host
execution or complete product acceptance. **Partial** identifies a specific
missing source workflow. Design verification for every row is **Blocked**.
Paths in this table are relative to `frontend/src/app/` unless otherwise noted.

| Penpot page / board target | Board ID | Angular route / component | Data source | Current interaction | Test source | Product status |
| --- | --- | --- | --- | --- | --- | --- |
| 00 Design System / DS01: Theme, type and spacing | Unretrieved | All routes; `core/theme/coglatas-theme.service.ts`, `shared/theme/coglatas-theme-toggle/`; `frontend/src/styles/_coglatas-*.scss` | Local theme preference, OS color preference, viewport/pointer density; no API | Toggle light/dark, persist explicit preference, apply root theme/density and Syncfusion body class | `core/theme/coglatas-theme.service.spec.ts`; theme persistence scenario in `tests/ui/angular-smoke.spec.ts` | Existing; Penpot token mapping Blocked |
| 00 Design System / DS02: Shared components and native chrome | Unretrieved | Shared dialog, error/loading/empty/permission/form controls and vendor adapters under `shared/` | Inputs from existing feature facades and API error adapters | Labelled CDK dialog, initial focus, Escape/backdrop cancellation, busy/disabled suppression, confirm-form submission and focus return/fallback | `shared/ui/coglatas-dialog/coglatas-dialog.component.spec.ts`, `shared/shared-ui-states.spec.ts`, adapter specs | Existing shared primitives; exact DS02 component coverage Blocked |
| 01 AppShell & Authentication / G02: Authentication / Login | Unretrieved | `/login`; `/signin` redirects to `/login`; `features/auth/login-page/` | `AuthSessionFacade`; GET `/api/security/csrf-token`, POST `/api/auth/login`, GET `/api/tenants/current` | Reactive email/password form, duplicate-submit guard, generic rejection, preserve password whitespace, navigate to `/workspaces` after success | `features/auth/login-page/login-page.component.spec.ts`, `core/auth/auth-session.api.spec.ts`, `core/auth/auth-session.facade.spec.ts`, `core/auth/auth-session.guard.spec.ts` | Existing login; SSO/reset/new account flows Deferred |
| 01 AppShell & Authentication / G04: Continue working | Unretrieved | `/workspaces/:workspaceId/projects`; `features/projects/projects-overview-page/` hosts `shared/continue-working/continue-working-panel.component.ts` | Tenant/user/Workspace-partitioned opaque local history; exact GET `/api/projects/:id` and GET `/api/files/:id`; grant-backed File download | Reauthorize before rendering; at most 3 concurrent exact reads, 6 displayed items; retry transient reads; prune revoked/mismatched entries; Project navigation/File download | `shared/continue-working/continue-working-history.service.spec.ts`, `continue-working.facade.spec.ts`, `continue-working-panel.component.spec.ts`; `docs/verification/p0-continue-working.md` | Existing on Workspace-scoped Projects; distinct global Home route Missing |
| 02 Workspace & Tenant / W01: Workspace Hub | Unretrieved | `/workspaces`; `features/workspaces/workspace-dashboard-page/`, `workspace-summary-list/`, `workspace-card/`, `workspace-quick-actions/` | `WorkspacesFacade`; GET `/api/workspaces`, GET `/api/workspaces/capabilities` | Authoritative summary cards/count availability, actionable attention links, page-local filtering, capability-derived navigation and create action | `features/workspaces/workspaces.api.spec.ts`, `workspaces.facade.spec.ts`, dashboard component and needs-attention specs; static Workspace scenarios in `tests/ui/angular-smoke.spec.ts` | Existing dashboard; exact W01 content/layout coverage Blocked |
| 01 AppShell & Authentication / navigation and Workspace context | Unretrieved | Authenticated route parent; `layout/app-shell/`, `feature-menu/`, `account-rail/`, `top-bar/`, `mobile-header/` | Auth capabilities, `WorkspacesFacade`, `WorkspaceSelectionFacade`, `ActiveWorkspaceFacade` | Capability-filtered links; collapsible desktop rail; separate reorderable Pinned shortcuts; Workspace selection; mobile drawer; logout | `layout/app-shell/*.spec.ts`, `layout/feature-menu/feature-menu.component.spec.ts`, `layout/top-bar/top-bar.component.spec.ts`, `core/workspace/workspace-selection.facade.spec.ts`; static mobile/keyboard scenarios | Existing; account rail presents the current user and does not implement account switching |
| 01 AppShell & Authentication / global search overlay | Unretrieved | Authenticated top bar; `layout/workspace-search/workspace-search.component.ts` | GET `/api/search` separately for `Project` and `File`, current `workspaceId`, bounded paging | Minimum query validation; Ctrl/Meta+K; loading/empty/fixed error; scope filtering; dedupe; at most 8 results; Escape; cancel/clear on Workspace ID change | `layout/workspace-search/workspace-search.component.spec.ts` | Partial relative to broad global search: implemented scope is current Workspace Projects/Files only |
| 01 AppShell & Authentication / notifications and right panel | Unretrieved | Authenticated shell; `shared/right-panel/` | GET `/api/notifications`, PATCH `/api/notifications/:id/read`, POST `/api/notifications/:id/open`; durable realtime invalidation | Expand/collapse/mobile drawer; notifications/members tabs; mark read; server-authoritative Task/digest open; fixed unavailable feedback; explicit refresh when degraded | `shared/right-panel/right-panel.component.spec.ts`, `wpc-02f-notification-navigation.spec.ts`; static mobile drawer/focus scenario | Partial: live Notifications are wired; member list has no live fetch in this facade |
| 11 Dialogs & Responsive / shell and Workspace creation | Unretrieved | `/workspaces` modal; `features/workspaces/workspace-create-dialog/`, `shared/ui/coglatas-dialog/`; shell mobile drawer | Existing create capability and POST `/api/workspaces` with stable idempotency key, followed by authorized list/selection | Required Name/optional Description/Icon; validation summary; busy close/submit suppression; committed-create activation recovery performs no second POST; narrow dialog and focus trap | `features/workspaces/workspace-create-dialog/workspace-create-dialog.component.spec.ts`, dashboard specs, shared dialog specs; create dialog browser scenarios | Existing bounded create flow; page/board visual and responsive parity Blocked |
| 12 Menus & Popovers; 13 Overlay UX & Coverage / shell overlays | Unretrieved | Feature navigation, top-bar search results and right-panel drawer; shared dialogs | Existing services above | Native links/selector/buttons, search result list and bounded modal controls | Existing shell/search/dialog/right-panel unit and static-browser sources | Partial inventory; exact action-by-action Penpot overlay mapping Blocked |

The current router (`frontend/src/app/app.routes.ts`) redirects the authenticated
empty path to `/workspaces`. It defines no standalone `/home` route. A similarly
named Penpot board does not authorize introducing that route or copying a
planned workflow.

## Existing token structure, awaiting a verified Penpot mapping

The current SCSS values below were read from product source only. They are not
Penpot token values and are not evidence that `CoglatasDark` or `CoglatasLight`
matches the app. Do not replace them until both design token sets are retrieved.

| Requested Penpot family | Current product destination | Source observation | Mapping status |
| --- | --- | --- | --- |
| `ui.color.*` in `CoglatasDark` and `CoglatasLight` | `_coglatas-theme-dark.scss`, `_coglatas-theme-light.scss`; semantic `--coglatas-color-*` | Separate canvas, shell, surface, control, selected, text, border, focus and status roles already exist | Blocked: design names, values, aliases and theme correspondence unread |
| `ui.space.*` | `_coglatas-primitives.scss`; `--coglatas-space-0` through `--coglatas-space-8` | Product scale: 0, 4, 8, 12, 16, 24, 32, 48, 64 px | Blocked: no design scale or layout measurements retrieved |
| `ui.radius.*` | `_coglatas-primitives.scss`; `--coglatas-radius-*` | Product radii: 0, 4, 6, 8, 12, 999 px | Blocked: no design component corner styles retrieved |
| `ui.type.*` | `_coglatas-primitives.scss`; `--coglatas-font-family`, `--coglatas-font-size-*` | Product family starts with Segoe UI and Japanese system fallbacks; font-size scale exists | Blocked: font family, weights, sizes, line heights and text styles unread |
| Shell dimensions and density | `_coglatas-component-tokens.scss`, `_coglatas-density.scss` | Product desktop rail/menu/right-panel and comfortable/compact dimensions exist; shell breakpoint is 860 px | Blocked: no representative Desktop/tablet/mobile board geometry retrieved |
| Overlay, focus and motion | `_coglatas-primitives.scss`, `_coglatas-semantic-tokens.scss`, `_coglatas-motion.scss` | Central z-index/focus/motion primitives exist; some component overlays use local numeric layers | Blocked: design overlay/component states unread |

Global styles are owned by Agent A for this task. No token, shared-control,
global-style, icon, theme, dependency or bundle change has been made.

## Source findings to validate when design access resumes

These findings identify bounded follow-up work, not accepted design changes or
new server contracts:

1. `RightPanelComponent` renders a tablist with selected state and associated
   panel IDs, but both tabs remain ordinary tab stops and there is no Arrow,
   Home or End handler. Verify the intended keyboard pattern and add behavioral
   regression coverage if changed.
2. `AppShellComponent.openMobileDrawer()` and
   `toggleRightPanelFromTopBar()` independently open their drawers. Source
   contains no mutual-exclusion boundary. Verify simultaneous-open/focus-trap
   behavior in a real browser before implementing a correction.
3. Right-panel member state initializes from the explicit mock token or an
   empty array and is only reset/cleared afterward. Notifications have a live
   service path; right-panel Members does not. The separate authorized
   Workspace Members page and header member-preview projection must not be
   mistaken for this missing live panel integration.
4. The account rail uses display-name initials and a brand glyph. It presents
   identity; it does not implement a multi-account login/switch command. Do not
   fabricate account-switching success based on the rail's appearance.
5. Workspace search clears on its Workspace ID input change and cancels
   stale HTTP generations. Unlike Continue working and Notifications, it has
   no direct protected-state-clearer registration. The existing global
   authorization flow invalidates Workspace selection; verify the full
   synchronous clear/reload ordering and same-ID reauthorization path before
   classifying a defect or changing state ownership.
6. Existing `AppShell` Storybook stories contain obsolete placeholder copy and
   `/app/*` fixture route entries. Their render is a component fixture, not
   proof that current production route journeys or a Penpot board were
   exercised. Storybook fidelity work must use verified design evidence.

## Compatibility and protected-state boundaries

Source review covered `AuthSessionFacade`, `WorkspaceSelectionFacade`,
`WorkspacesFacade`, `RightPanelFacade`, `ContinueWorkingFacade`, and the
auth/search/Workspace/notification controllers. No endpoint, DTO,
authorization policy, schema, migration or persisted business behavior changed.

- Login/logout use existing cookie and CSRF paths. Password whitespace remains
  unchanged. Logout clears local state after success or terminal 401; a
  transient logout failure is surfaced rather than reported as success.
- Active Workspace selection checks the latest authorized list. Route,
  tenant/user-scoped preference and sole-authorized-Workspace precedence
  remain in place; API row zero is not implicit authority.
- A real Workspace switch clears old protected projections and scoped
  subscriptions before committing the new pointer and requesting catch-up.
- Notifications and Continue working already register protected-state
  clearers. Their cancellation/version/scope checks must survive future
  presentation edits.
- Continue working stores only opaque kind/resource/time history and
  reauthorizes metadata. File downloads use existing server-issued grants;
  token values stay in the request closure.
- Frontend capabilities control presentation only. Existing controller and
  Application authorization remains authoritative.
- Current SEC-ARCH decisions, `PRE-AVALONIA: BLOCKED`, Angular, Syncfusion and
  existing CI gates are unchanged.

Relevant current records: `ws-02-workspace-context-header.md`,
`ws-03-workspace-create.md`, `ws-01-workspace-dashboard-backend-projection.md`,
`p0-workspace-sharing-header.md`, `p0-continue-working.md` and `docs/TESTING.md`.
Historical counts in these records are not fresh execution evidence.

## Verification and remaining acceptance

The Coordinator owns the fresh baseline production build, full unit suite,
architecture checks and integrated result record. This documentation-only
agent change does not rerun or claim those commands. Existing test source was
inspected; a test path in this audit means coverage source exists, not that the
test executed successfully in this session.

This agent runs `git diff --check` before committing this record. No generated
artifact, snapshot, dependency lockfile, private specification text or product
UI source is changed.

The following remain **Blocked** or **Deferred** and receive no completion
credit:

- **Blocked:** latest design revision/diff, exact board IDs and shape trees;
  dark/light token and typography mapping; representative PNG/SVG exports;
  geometry/type/color/spacing/component comparisons; requested viewport
  comparison at 1536 x 1024, 1366 x 768, 768 x 1024 and 390 x 844.
- **Blocked:** implementation of design-derived shell/auth/Workspace changes
  without verifiable design source. Existing source is retained.
- **Needs verification:** real-backend Login to Workspace to Project to Task,
  session/authorization recovery, cookie/CSRF/DTO compatibility, protected
  hosted CI, Storybook rendering and pinned-Linux browser/screenshot runs.
  Mocked component or static-browser results do not prove these boundaries.
- **Deferred:** new global Home contract, account switching, SSO/password-reset
  delivery, arbitrary docking, native window controls and new persistence/API
  workflows. The current task does not approve new server behavior for them.

Resume by waking/reconnecting the existing Penpot plugin, retrieving revision
and targeted DS01/DS02/G02/G04/W01 board metadata, then reading layout and
component/token properties and exporting the same boards. Only after that
evidence is retained should the bounded changes and their specific regression
tests be selected.

## Japanese and English localization follow-up

The user separately authorized localization of existing UI independently of
the unavailable design. This follow-up starts from integration seed
`8590263845e46eecdedf6666c1f098a685fc6e70` on
`codex/web-i18n-shell-20261017`. It does not establish any new Penpot evidence
or permit geometry, style, API, authority or workflow changes.

The change reuses `I18nService` and its existing Japanese default, English
choice, browser persistence and reactive locale. The shell catalog supplies
copy for Workspace search, the right panel, work-status badges, theme controls,
shared safety/loading/error/dialog controls, invite registration, Workspace
cards/dashboard/create/quick-create and member controls. Existing central
common/login/header labels are reused. Project-specific shared Kanban/Gantt
adapter localization is assigned separately to Agent B.

Local status, role and action labels derive from existing typed identities.
Optional message-key metadata accompanies existing source-owned facade
messages; arbitrary server/scenario messages and resource names are retained.
Locale changes do not restart commands or replace draft state. Local login,
logout, quick-create and successful-creation feedback is rendered from its
state and parameters so settled feedback also changes language.

Valid raw Workspace update/attention/member-join timestamps are retained
beside legacy display labels and formatted at presentation time. Opaque
preformatted fallback values are left intact. Invite expiry and Continue
working timestamps use the current locale. `formatDateOnly` validates exact
calendar dates and fixes UTC during formatting; it does not shift a calendar
day based on the browser timezone. Machine status keys and canonical input
date values are unchanged.

Helper commit `4b7200125ebce749bd2fa73b3fe9eeea4837c7be` passed five service
tests. The final broader local command passed **35 files / 277 tests**:

```bash
npm --prefix frontend test -- \
  --include='src/app/core/i18n/i18n.service.spec.ts' \
  --include='src/app/layout/**/*.spec.ts' \
  --include='src/app/shared/**/*.spec.ts' \
  --include='src/app/features/auth/**/*.spec.ts' \
  --include='src/app/features/workspaces/**/*.spec.ts'
```

New regressions verify Japanese-to-English search switching during pending
reads without another HTTP request or replacement input, dialog draft and
validation retention across locale switches, and typed status/calendar-date
semantics. Existing English-specific tests explicitly choose English; the
application default remains Japanese. Workspace validation retains its full
field/message assertions and now also asserts the typed presentation key.

`npm --prefix frontend run check:architecture` passed. After adding the raw
timestamp mapping regression and local validation deduplication adjustment,
the Workspace mapper and create-dialog suites passed **2 files / 29 tests**.
The Coordinator owns integrated production build, full-suite, browser,
Storybook and hosted evidence. Local mocked tests do not establish backend
compatibility or screenshot parity. Existing CDK fixture and tooling warnings
remain visible. No style, dependency, lockfile, route or screenshot is changed.
