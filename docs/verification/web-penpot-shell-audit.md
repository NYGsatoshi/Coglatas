# Web Penpot audit: AppShell, design system, authentication and Workspace

Status: **Blocked** for design comparison and implementation. Source audit only;
no visual or behavioral production change is included in this record.

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
