# Web frontend wave: navigation and messaging foundation

Date: 2026-10-19 JST (execution timestamps below use UTC). Base Product Main:
`9b44d631df88cdc2f15a45b6278b3e1b2552a8df`. Branch:
`codex/webwave-ui-20261019`. Status: IMPLEMENTED_AND_VERIFIED locally;
independent review and exact-head hosted CI remain separate gates.

## Implemented scope

SonarCloud check `117713818392` failed on original head `c3dab4d7` because
repeated directory locale keys produced 5.4% new-code duplication (limit 3%).
The repair keeps the eleven English/Japanese product-localization pairs in one
typed directory catalogue and derives the two existing message maps. Existing
keys, translations and registration are preserved. Two nested conditionals were
also made explicit. No inspection threshold or exclusion changed; the original
failed check remains recorded and latest-head qualification is required.
The focused messaging/i18n/shell/routes selection passed **71/71** after this
repair (`a-sonar-repair-tests.log`); final integrated browser/build and hosted
quality checks are independently tracked.

- DM and Teams are separate top-level desktop, collapsed-rail and mobile-menu
  destinations at `/dm` and `/teams`; `/messages` retains the combined inbox.
- Both directories use the existing authorized conversation projection and
  conversation routes. Teams means channel conversations in this view. No new
  Team entity, hierarchy, membership authority or endpoint is invented. Existing
  channel and Thread Workspace behavior is retained.
- Directory filtering applies to the current inbox page. Visible copy explicitly
  identifies that scope and that inbox counts and message search remain global
  across the current user's authorized conversations. An empty directory page
  does not claim that no matching conversations exist elsewhere.
- Previous/next navigation uses the existing `Page` query on
  `GET /api/conversations`, preserving the selected server inbox view. It clears
  old rows during a page change, prevents duplicate pending page actions and
  returns to page one after changing view or refreshing. Pagination is exposed
  only when server pagination metadata establishes multiple pages.
- Both raw and interceptor-normalized HTTP 401/403 list failures clear prior
  rows and the current list draft plus its exact browser storage entry. The
  unavailable view disables new-conversation creation. Unrelated conversation
  drafts are not erased by a list-only denial. Existing generation changes
  clear Thread Workspace entries/state and cancel protected requests; this
  patch makes no claim that one list denial qualifies every resource boundary.
- Superseded/closed recipient searches are cancelled; protected list refreshes
  clear dialog state. A create command requires a recipient in the current
  search response. Existing CDK focus capture/trapping, initial recipient focus,
  Escape dismissal and trigger focus restoration are used for the dialog.
- The messaging landing removes redundant nested padding and uses existing
  density, dark/light surface, selection and focus tokens. Lucide icon data
  comes from the installed 1.53.0 package. No dependency or lockfile changed.

No Penpot service or recently specified home reference was available to this
workstream. Existing repository tokens were reused; visual equivalence with an
unretrieved design is NOT_EXECUTED. This is a scoped navigation/messaging change,
not a claim of a complete screen-by-screen theme or mobile-density audit.

## Local verification

Windows; Node `v24.19.0`, Angular `22.2.2`, NgRx `22.0.1`, SignalR `10.0.11`,
Vitest `5.0.3`. Dependency installation was shared through ignored worktree
junctions. Commands below ran from `frontend/` unless stated otherwise; no shared
dependency files were modified.

| Check | Result | Evidence |
| --- | --- | --- |
| Initial focused Angular selection | FAIL: 66 passed, 2 failed of 68 | Existing 30-second cold shell compile hook timed out under concurrent transformation. New focus assertion initially assumed layout boxes in jsdom. No timeout, test or guarantee was removed or relaxed. |
| Corrected focused selection | PASS: 70/70, 3 files | Explicit jsdom focusability stub in the focus test; actual browser focus also checked below. Existing shell hook passed on rerun. |
| Broad messaging/shell/theme selection | PASS: 210/210, 18 files | Includes 73 existing Thread Workspace/context cases and 10 composer cases, preserving IME coverage. Completed 2026-10-18T15:20:27Z approximately. |
| Final stored-draft follow-up | PASS: 59/59, 1 file | Nonempty draft is written and observed in DraftStorage before normalized 403, then absent in both page and storage. Completed 2026-10-18T15:21:14Z approximately. |
| Final production build | PASS | Completed 2026-10-18T15:22:22Z; initial raw 927.61 kB, estimated transfer 193.07 kB. No low-spec hardware/runtime performance qualification is inferred from bundle size. |
| Theme sanitizer | PASS | Six copied vendor theme files scanned; generated CSS sanitized. Generated build output is not committed. |
| Architecture source check | PASS | `node scripts/check-architecture.mjs`, exit 0. |
| New mocked Playwright cases | PASS: 4/4, 0 skipped | Two cases in desktop/mobile Chromium projects, final build; dark 320px directories, global scope copy, page navigation/empty state, normalized denied response, keyboard focus trap/restore, horizontal containment and axe. |
| Patch whitespace | PASS | `git diff --check`, exit 0. |
| Backend/PostgreSQL/live API/SignalR runtime | NOT_EXECUTED for this patch | Frontend HTTP responses are mocked. Existing runtime implementations are reused, not independently qualified here. |
| Authoritative Linux screenshot parity | NOT_EXECUTED | Docker engine unavailable. Windows PNGs are diagnostic and are not approved baselines. |
| Exact-head GitHub Actions / CODEOWNERS review | NOT_EXECUTED locally | Must be recorded against the published PR head before a merge decision. |

Production build warning budgets remain visible. The initial bundle exceeds the
existing 500 kB warning (below the unchanged 1.02 MB error cap). The messaging
landing style is 4.92 kB, exceeding its 4 kB warning (below the unchanged 9 kB
error cap); other existing component-style warnings also remain. These warnings
are not represented as clean performance qualification.

Commands:

```powershell
# Focused selection and corrected rerun
node node_modules/@angular/cli/bin/ng.js test --watch=false --include='src/app/features/messaging/messaging-ui.spec.ts' --include='src/app/layout/app-shell/app-shell.component.spec.ts' --include='src/app/app.routes.spec.ts'
# Broader relevant regression selection
node node_modules/@angular/cli/bin/ng.js test --watch=false --include='src/app/features/messaging/**/*.spec.ts' --include='src/app/layout/app-shell/*.spec.ts' --include='src/app/layout/feature-menu/*.spec.ts' --include='src/app/app.routes.spec.ts' --include='src/app/core/theme/*.spec.ts'
# Final stored-draft follow-up
node node_modules/@angular/cli/bin/ng.js test --watch=false --include='src/app/features/messaging/messaging-ui.spec.ts'
node node_modules/@angular/cli/bin/ng.js build --configuration production
node scripts/sanitize-syncfusion-theme-css.mjs
node scripts/check-architecture.mjs
# From repository root, against this worktree's final build
$env:PLAYWRIGHT_SKIP_BUILD='1'
$env:PLAYWRIGHT_PORT='4176'
node node_modules/@playwright/test/cli.js test tests/ui/message-search-filters.spec.ts --grep='separates DM and Teams|pages the existing authorized' --project=chromium-desktop --project=chromium-mobile
git diff --check
```

Browser report: `playwright-report/index.html`; JUnit:
`test-results/playwright-results.xml`. Each new focus test writes
`test-results/message-search-filters-*/teams-dark-320-diagnostic.png`; the mobile
PNG was inspected and showed consistent existing dark surfaces and contained
directory controls. These ignored local files are disposable execution outputs.
The full application mobile header remains a separate density review concern.

## Hosted Linux frontend follow-up

The earlier local-only Linux status above is retained as historical evidence.
The exact A head `461d82c47d4f743fc73797b80a19fc5b143894c1` subsequently ran the
pinned Linux Docker Playwright suite in
[run 39140992654, attempt 1, frontend job 117717829944](https://github.com/NYGsatoshi/Coglatas/actions/runs/39140992654/job/117717829944).
Result: **FAIL**, with 188 passed, 3 failed and 13 existing skips. Two failures
were the desktop/mobile instances of `allows keyboard traversal to primary shell
areas`: its fixed 12-Tab search limit no longer spanned the added navigation
destinations. The third failure was the mobile drawer screenshot, differing by
3,235 pixels after the intended DM/Teams additions. These failures remain
recorded; they are not a successful exact-head run.

The keyboard test now derives one shell focus-cycle limit from the actual
nonnegative-tab-index, visible, enabled, non-inert controls under `app-root`.
Each original destination must still receive keyboard focus within that bound.
The shared helper, timeouts, focus assertions, skip policy and screenshot
thresholds are unchanged. No production source changes accompany this repair.

Authoritative screenshot provenance:

| Field | Recorded value |
| --- | --- |
| Artifact | `12142351762`, `playwright-ui-test-artifacts`, 165,205,360 bytes |
| Artifact source | Run `39140992654`, branch `codex/webwave-ui-20261019`, head `461d82c47d4f743fc73797b80a19fc5b143894c1`; confirmed by artifact API metadata |
| ZIP SHA-256 | `5e990a2508b3bae8e7b06342329ca8e9ca6f5a412dcd940e21a2c0ea10fc4db2`; downloaded bytes match the API digest |
| Exact actual image entry | `test-results/angular-smoke-MVP-A-P0-Ang-4dbb7-lar-P0-screenshot-baselines-chromium-mobile/mobile-shell-workspaces-drawer-actual.png` |
| Prior baseline SHA-256 | `dd3e030c731f672f28dae66464b9fe0c631990d985a0039542d21a30f21639cf`; matches the artifact's expected image |
| Reviewed actual SHA-256 | `067a4a4aad706e7a476a1bd992ff185a34e4fe3d89eb60cee6b9b03f20baad35` |
| Reviewed diff SHA-256 | `cbed5807249c7a7c3797224f5f62303dc22f70b2595d26fad5d5482fa77c6049` |
| Updated baseline | `tests/ui/__angular_snapshots__/linux/angular-smoke.spec.ts/mobile-shell-workspaces-drawer.png`; exact copy of the reviewed actual bytes |

Only this Linux actual, its paired expected image and its diff were inspected
for this baseline decision. The images show the two intended DM/Teams rows and
the corresponding 96-pixel downward shift of later menu/Pinned entries. Header,
drawer width, surrounding background and scrim remain unchanged. No Windows
image, regenerated image, threshold relaxation or unrelated screenshot was used.
The A baseline does not include the integration branch's additional Local drafts
item and must not be presented as the combined wave's final baseline.

Local follow-up against the rebuilt A source: **PASS**, production build
(927.61 kB initial, 193.15 kB estimated transfer), vendor CSS sanitation and the
same keyboard scenario in both Chromium projects (2 passed, 0 failed, 0 skipped).
The rebuilt artifact completed at 2026-10-18T16:00:03Z. The targeted command from
the repository root was:

```powershell
$env:PLAYWRIGHT_SKIP_BUILD='1'
$env:PLAYWRIGHT_PORT='4176'
node node_modules/@playwright/test/cli.js test tests/ui/angular-smoke.spec.ts --grep='allows keyboard traversal to primary shell areas' --project=chromium-desktop --project=chromium-mobile
```

This focused Windows check establishes keyboard behavior only. A fresh pinned
Linux suite on the pushed repair head is still required to qualify the updated
baseline and full frontend job. The original failed run is retained regardless
of a later pass.

## Contract and security holds

Only existing Product conversation/list/recipient/direct-create contracts are
used. No private specification content, new backend endpoint, schema, migration,
RLS, Role/GRANT/Issuer change, key-custody change, deployment or public exposure
is included. Draft/Links and Files approval holds are unaffected. Avalonia
readiness is not established. Merge remains subject to independent review and
the repository's actual required checks and approvals.
