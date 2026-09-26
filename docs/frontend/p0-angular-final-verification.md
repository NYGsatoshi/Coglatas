# MVP-A P0 Angular Final Frontend Verification

Date: 2026-07-03

Scope: Angular frontend under `frontend/`, repo-level Angular Playwright wrapper, and backend test coverage required by CI. Legacy static SPA behavior, selectors, and screenshot baselines were not used as acceptance evidence.

## P0 Status

Conditional Go.

The host-native Windows Angular gate previously passed, but GitHub Actions
Ubuntu reported screenshot-only baseline drift for the desktop workspace shell
and mobile workspace drawer. Functional Angular smoke checks still pass. Final
Go remains blocked until the Docker/Linux Playwright screenshot regression
passes in GitHub Actions.

## Pass/Fail Summary

| Area | Status | Evidence |
| --- | --- | --- |
| Root Playwright dependencies | Pass | `docs/evidence/mvp-a/frontend-final-2026-07-03/01-root-npm-ci.log` |
| Angular dependencies | Pass | `docs/evidence/mvp-a/frontend-final-2026-07-03/02-frontend-npm-ci.log` |
| Angular production build | Pass | Initial sandbox run hit the known `Cannot read directory "../../../..": Access is denied.` issue; valid post-fix unsandboxed run passed in `13-frontend-build-after-csrf-fix.log`; final screenshot closeout build passed in `17c-frontend-build-for-screenshot-baselines-clean-exit.log` with the known non-fatal initial bundle budget warning. |
| Angular unit tests | Pass | Post-fix rerun passed 17 files / 125 tests in `14b-frontend-test-after-csrf-fix-rerun.log`. The previous post-fix attempt timed out in existing tests under host load; it is retained as `14-frontend-test-after-csrf-fix.log`. |
| Storybook build | Pass | Post-fix unsandboxed run completed and emitted `frontend/storybook-static`; see `15-frontend-build-storybook-after-csrf-fix.log`. |
| Angular Playwright smoke | Local Docker pass, CI pending | Docker Playwright run passed 48 tests with 2 skipped non-P0 legacy static SPA placeholders after regenerating the two approved Linux baselines. GitHub Actions screenshot regression is still pending. |
| Backend tests | Pass | Unsandboxed rerun passed 234/234 tests; see `07-backend-dotnet-test-rerun-unsandboxed.log`. The sandbox attempt was blocked by NuGet network access. |
| AG Grid Enterprise absence | Pass | Source/package scan found no package or import usage; only tests asserting absence matched. See `08-ag-grid-enterprise-scan.log`. |
| AG Grid wrapper boundary | Pass | `AgGridAngular` is used by `AppDataGridComponent`; route pages import the wrapper. See `09-ag-grid-wrapper-scan.log`. |
| Global search boundary | Pass | Route/nav scan found page-local search only, no global DB search route. See `10-search-route-scan.log`. |
| CSRF/session/storage scan | Pass after fix | Unsafe same-origin API requests attach CSRF, token fetch failure blocks mutation, third-party requests do not receive CSRF, and retry is now covered once. See `11-csrf-storage-scan.log` and updated unit test. |
| Screenshot regression | Conditional | The two GitHub Actions-drifting baselines were regenerated in the pinned Docker/Linux Playwright environment and passed locally. Final Go waits for the same lane to pass in GitHub Actions. |
| Lint/format scripts | Not configured | Root and frontend package scripts do not define `lint` or `format`; final whitespace check is `git diff --check`. |

## Commands Run

| Command | Working directory | Result |
| --- | --- | --- |
| `npm.cmd ci` | repo root | Pass |
| `npm.cmd ci` | `frontend/` | Pass |
| `npm.cmd run build` | `frontend/` | Sandbox blocked first; valid unsandboxed reruns passed. |
| `npm.cmd run test` | `frontend/` | Sandbox blocked first; valid pre-fix run passed 124 tests; post-fix rerun passed 125 tests. |
| `npm.cmd run build-storybook` | `frontend/` | Sandbox blocked first; valid unsandboxed reruns passed. |
| `npm.cmd run build` | `frontend/` | Final screenshot closeout build passed; see `17c-frontend-build-for-screenshot-baselines-clean-exit.log`. |
| `npm.cmd run test:ui:angular -- tests/ui/angular-smoke.spec.ts --grep "matches approved Angular P0 screenshot baselines" --update-snapshots` | repo root | Pass; generated the approved Angular P0 screenshot baselines. |
| `npm.cmd run test:ui:angular -- tests/ui/angular-smoke.spec.ts --grep "matches approved Angular P0 screenshot baselines"` | repo root | Pass; screenshot regression matched the new baselines without updating. |
| `npm.cmd run test:ui:angular` | repo root | Pass; served `frontend/dist/coglatas-web` and passed 48 tests with 2 obsolete non-P0 skips. |
| `npm.cmd run test:ui:angular:docker` | repo root | Pass; pinned Linux Playwright image ran production build plus Angular UI smoke and passed 48 tests with 2 skipped non-P0 legacy static SPA placeholders. |
| `dotnet test Coglatas.slnx --configuration Release --verbosity normal --disable-build-servers -m:1` | repo root | Sandbox restore blocked by NuGet network; valid unsandboxed rerun passed 234 tests. |
| `rg` guardrail scans for AG Grid, search, CSRF/storage, screenshots | repo root | Pass. |
| `git diff --check` | repo root | Pass. |

## Screens Verified

Playwright verified these Angular routes in desktop and mobile projects:

- `/`
- `/login`
- `/app/workspaces`
- `/app/workspaces/fictional-workspace-1/members`
- `/app/announcements`
- `/app/workspaces/fictional-workspace-1/channels/fictional-conversation-main`
- `/app/dm/fictional-dm-1`
- `/app/files`
- `/app/projects`
- `/app/tasks`
- `/app/admin/audit`
- `/app/admin/export-diagnostics`
- `/app/account`
- `/register/invite`
- `/permission-denied`
- `/not-a-real-angular-route`
- `/api/playwright-angular-smoke` as backend-owned JSON 404, not Angular fallback

Storybook build covered the Angular story bundles for app shell, navigation, right panel, shared states, workspace, announcements, messaging, files, projects/tasks, account, invite registration, audit log, and export diagnostics pages.

## Tests Added Or Updated

- Updated `frontend/src/app/core/auth/auth-session.interceptor.ts` so likely CSRF `403` responses on unsafe first-party API requests clear the stale token, fetch a fresh token, retry once with the refreshed header, and then stop.
- Added `retries unsafe CSRF failures once with a refreshed token` in `frontend/src/app/core/auth/auth-session.interceptor.spec.ts`.
- Added an Angular-only Playwright screenshot regression for the approved P0 targets in `tests/ui/angular-smoke.spec.ts`.
- Updated `tests/ui/run-angular-playwright.mjs` to forward optional Playwright arguments while preserving the default full-suite behavior.

## Screenshot Baseline Decision

The two GitHub Actions-drifting Angular-approved screenshot baselines were
regenerated from the pinned Docker/Linux Playwright environment.

Approved targets:

- `tests/ui/__angular_snapshots__/angular-smoke.spec.ts/desktop-shell-workspaces.png`: `/app/workspaces`, `chromium-desktop`, 1280x900 desktop shell.
- `tests/ui/__angular_snapshots__/angular-smoke.spec.ts/mobile-shell-workspaces-drawer.png`: `/app/workspaces`, `chromium-mobile`, 390x844 mobile shell with drawer open.
- `tests/ui/__angular_snapshots__/angular-smoke.spec.ts/permission-denied-state.png`: retained from the previous local Windows closeout, but not used as current Linux CI acceptance evidence.

No obsolete legacy static SPA baselines were added. The skipped `tests/ui/app.spec.ts` placeholder remains labeled non-P0 and did not receive snapshots.

Historical evidence from the earlier host-native closeout:

- Baseline generation: `docs/evidence/mvp-a/frontend-final-2026-07-03/18b-screenshot-baseline-update-after-mobile-tightening.log`
- Targeted screenshot regression: `docs/evidence/mvp-a/frontend-final-2026-07-03/19-screenshot-regression.log`
- Full Angular UI run: `docs/evidence/mvp-a/frontend-final-2026-07-03/20-root-angular-playwright-with-screenshot-baselines.log`
- Baseline file list: `docs/evidence/mvp-a/frontend-final-2026-07-03/21-screenshot-baseline-file-list.log`
- Final diff check: `docs/evidence/mvp-a/frontend-final-2026-07-03/22-git-diff-check.log`

Generated Playwright artifacts:

- `playwright-report/index.html`
- `test-results/playwright-results.xml`

No failure screenshots, traces, or videos were required by the final local
Docker passing run. GitHub Actions artifact upload remains configured for
`playwright-report/` and `test-results/` on failure.

## Remaining Backend Blockers

No backend automated test blocker remained in this pass: the full backend test command passed 234/234 after rerunning outside the sandbox.

Backend authorization remains mandatory and is not replaced by UI hiding. Live product Go still depends on keeping the backend authorization, tenancy, CSRF, file, messaging, audit, and PostgreSQL checks green in CI and on closing any separate live API-binding gaps tracked outside this final Angular mock verification.

## Remaining Frontend Watch Items

- The production Angular build still reports a non-fatal initial bundle budget warning: initial bundle is about 663 kB against the 500 kB budget. This remains a watch item, not a P0 blocker, because the configured build completed successfully.
- The first post-fix unit run timed out in four existing specs under host load, then the exact rerun passed 125/125. Treat this as an environment-performance warning unless it repeats in CI.

## Evidence Paths

- Logs: `docs/evidence/mvp-a/frontend-final-2026-07-03/`
- Angular build output: `frontend/dist/coglatas-web`
- Storybook output: `frontend/storybook-static`
- Playwright report: `playwright-report/index.html`
- Playwright JUnit: `test-results/playwright-results.xml`
