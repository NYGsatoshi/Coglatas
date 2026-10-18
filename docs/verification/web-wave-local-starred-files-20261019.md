# Browser-local Starred files — 2026-10-19

Agent C implementation baseline: Product Main
`9b44d631df88cdc2f15a45b6278b3e1b2552a8df`. This work uses the existing
Angular 22.2.2 Files page and existing FileObject detail route. It does not
implement a server Starred list, a star command, Shared listing, a global
Artifact repository, advanced Files filters, or a new cursor contract.

## Implemented behavior and authority

- The existing inspector can add/remove a **local browser star**. The Starred
  shortcut lists this account's current Workspace preferences, with explicit
  copy that they are not synced to the server or other devices and disappear
  when browser data is cleared. English and Japanese product copy retain the
  existing locale contract; engineering records remain English.
- `LocalFileStarsRepository` is the replaceable persistence adapter. Its strict
  versioned record stores at most 32 canonical FileObject UUIDs, partitioned by
  Tenant, user and Workspace. No filename, owner, content, grant token, path,
  metadata or API response is persisted. Extra fields, duplicate IDs, malformed
  versions and oversized records are discarded. Storage failures never produce
  a saved/synced success state. The cap bounds local hydration work; it is not
  a product or database capacity limit.
- Every Starred visit and retry replaces its metadata via existing
  `GET /api/files/{fileObjectId}` requests, at most three concurrently. Exact
  identity, Workspace, active/nondeleted status and bounded display metadata
  must match. Denied/mismatched entries are removed from the local preference.
  Transient errors retain opaque IDs for retry, never cached labels.
- The current `FileObjectResponse` has no scan, uploader or delete projection.
  The adapter deliberately maps scan state to unavailable and supplies no
  download/delete capability. Starred opens the existing Details inspector;
  content and ordinary file actions remain in the existing authorized Recent
  inventory. A local star grants no resource authority.
- Workspace/account changes synchronously redact the Starred projection before
  effects hydrate the next scope. Retry, leaving Starred, protected-state reset
  and File invalidation cancel obsolete requests and clear metadata. A terminal
  401 clears the entire Starred projection and cancels other pending reads.
  Removal restores keyboard focus to the stable Return to Recent control.
- Existing list/search/paging, previews, upload, sharing, selection, folder move
  and Activity implementations are reused. Shared retains its explicit
  unavailable state. No backend, DTO, schema, migration, role, grant, issuer,
  key, RLS, dependency, generated hosted output or screenshot baseline changes.

The [earlier Files contract matrix](web-p2-files-contract-matrix-20261018.md)
continues to hold server Starred/Shared persistence/listing and the other
unapproved proposals. This API-neutral, explicitly local preference is the
only Starred addition. No private specification content or candidate endpoint
has been copied or adopted. PRE-AVALONIA SEC-ARCH remains BLOCKED.

## Executed verification

Windows; Node v24.19.0 bundled runtime; pinned Angular 22.2.2 / Vitest 5.0.3.
Dependencies were reused through a junction to the Coordinator's clean pinned
frontend install. No lockfile/dependency mutation was made by this workstream.
Commands below ran from this worktree's `frontend/` directory with the explicit
bundled Node executable; `node` is abbreviated in the reproduction commands.
Execution was 2026-10-19 JST / 2026-10-18 UTC.

| Check | Actual result |
| --- | --- |
| First focused development run | **FAIL**: 27 passed / 23 failed. One new scope-switch case exposed a nonreactive scope-key cache; one new component assertion matched an unrelated existing attachment picker, and its unflushed request caused cleanup failures in later cases. Neither failure is counted as a pass. |
| Corrected focused run | **PASS**, 3 spec files / 53 tests, 0 skipped, 57.48 seconds, exit 0 |
| Complete Files regression selection | **PASS**, 16 spec files / 147 tests, 0 skipped, 39.01 seconds, exit 0 |
| App TypeScript | **PASS**, exit 0 |
| Spec TypeScript | **PASS**, exit 0 |
| Frontend architecture boundary | **PASS**, exit 0 |
| Production build | **PASS**, 73.309 seconds, exit 0; initial bundle 927.20 kB / estimated transfer 193.04 kB; Files lazy chunk 146.06 kB / estimated transfer 27.59 kB |
| Existing theme sanitization | **PASS**, six stylesheets scanned, one sanitized, exit 0 |
| Whitespace validation | **PASS**, `git diff --check`, exit 0 |
| New real-backend/PostgreSQL tests | **NOT_EXECUTED** in this Files workstream |
| Browser E2E, screen-reader/axe, visual parity and low-spec device measurements | **NOT_EXECUTED** in this Files workstream |
| Hosted exact-head CI, independent review, merge and exact-Main qualification | Separate Coordinator gates; no local PASS claim |

The full selection includes the existing preview, Activity, sharing, upload,
download-grant, folder context, selection, search, pagination and locale tests.
The new adapter/service suites contribute 29 cases and the added component
flow contributes one. They use Angular HTTP test responses and synthetic data;
they prove frontend behavior, not ASP.NET Core/PostgreSQL compatibility.
Boundary tests cover old-user/Workspace held-request cancellation, synchronous
projection clearing, 401 with a pending sibling, 400/403/404 pruning,
offline retry, invalidation, strict storage, and long Unicode filenames.

The existing Vite native-config warning and existing CDK initial-focus warning
were visible. The build retains the initial 500 kB warning and existing
component style warnings; the Files stylesheet is 8.88 kB, below the unchanged
9 kB error budget. Initial bundle size matches the Coordinator's 927.20 kB
baseline. This is a bundle comparison only, not a runtime performance or
low-spec hardware qualification. No thresholds were relaxed.

```powershell
node node_modules/@angular/cli/bin/ng.js test --watch=false --include='src/app/features/files/file-stars*.spec.ts' --include='src/app/features/files/files-page/files-page.component.spec.ts'
node node_modules/@angular/cli/bin/ng.js test --watch=false --include='src/app/features/files/**/*.spec.ts'
node node_modules/typescript/bin/tsc -p tsconfig.app.json --noEmit
node node_modules/typescript/bin/tsc -p tsconfig.spec.json --noEmit
node scripts/check-architecture.mjs
node node_modules/@angular/cli/bin/ng.js build --configuration production
node scripts/sanitize-syncfusion-theme-css.mjs
git diff --check
```

## Remaining work

Server-synchronized Starred remains **BLOCKED_BY_OWNER** pending an approved
per-user preference/list contract with current-read authorization and revocation
semantics. Shared, held filter candidates and cross-Project Artifact discovery
retain their separate approval/public-disclosure gates. The existing per-Project
Artifact routes and current Files search are unchanged.

Coordinator cross-review and exact-head CI must complete before any merge.
Browser accessibility, responsive E2E and real-backend checks must be reported
separately; passing mocked Angular tests does not discharge those gates. No
production deployment, public exposure or Avalonia start is authorized here.
