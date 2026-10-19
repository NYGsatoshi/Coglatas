# Student Council Files and preview candidate

Date: 2026-10-19. Scope: the maintained Angular Files route only.

## Read-only design source

The connected live Penpot file was `CoglatasUI`,
`52a5041f-08a1-80f1-8008-c5a09f8b5f61`. The following boards were inspected
through the Penpot API and exported to PNG without changing the design:

- File Hub: `9a59a7a6-1369-80f7-8008-cf126eeff377`, named
  `FHV05 / File Hub — Approved Dark · 2026-10-18`.
- File Preview: `9a59a7a6-1369-80f7-8008-cf261fd20680`, named
  `FHV09 / File Preview & Transfer Queue — Approved Dark · 2026-10-18`.

Both source boards are 1536 × 1024. The File Hub has a 28-pixel, weight-700
heading, 13-pixel subtitle, 208-pixel folder sidebar, 754-pixel inventory,
302-pixel inspector, 12–14-pixel gutters, and 7-pixel panel corners. The
Preview board has a 901-pixel primary viewer and 371-pixel transfer column.
Typography is Noto Sans JP. The Files board's local fills are canvas
`#0B1620`, panel `#131F2B`, border `#26394B`, heading `#F2F5FB`, secondary
text `#B2C3D9`, muted text `#8599AF`, and accent `#74B8FF`.

The implementation consumes the shared Approved Dark semantic variables from
PR #1275. It deliberately does not create a second palette for the small
per-board canvas, border, and accent differences.

## Implemented adaptation

- A compact heading, intake policy, quota/search overview, bordered browser,
  inventory, and inspector use the existing semantic tokens. Unknown quota
  capacity renders its unavailable message without a fabricated `0 / 0`
  capacity or progress bar.
- The existing server-owned Type, Modified, Owner, and filename search,
  removable chips, local stars, pagination, and held Shared view retain their
  existing contracts. No Shared/Filters/Artifacts HOLD contract is activated.
- Inventory loading, denied/unavailable, transient failure with manual GET
  retry, and successful empty results are distinct. A retry clears old rows;
  denied reads render no previous filename or raw diagnostic.
- Existing grant-backed image/PDF/video/text preview, metadata, Activity,
  focus restoration, keyboard tabs, and mobile focus trapping are retained.
  At 860 pixels and below, the mobile list and existing preview drawer remain
  the primary presentation. Intermediate widths place the inspector below
  the inventory; large widths use the source's three-column composition.
- No synthetic version labels, project associations, transfer queue, scanning
  progress, or successful malware checks are added. These source elements do
  not have qualified pilot contracts. Existing files retain their existing
  read/download authorization and scan-policy handling.

## Pilot intake boundary

The Files page exposes an unavailable Upload button and a localized policy
message. It mounts no file chooser. Its direct accept/retry handlers return
without dispatching an upload. The underlying server upload service remains
unchanged, and this presentation restriction is not server authorization.

`FileService.UploadAsync` and `ArtifactService.UploadVersionAsync` already
require `FeatureKeys.FileSharing`. The Files shell module is keyed `Files`,
which is distinct from that feature key; setting the tenant's existing
`FeatureFlagsJson` override to `{"FileSharing":false}` does not remove the
Files navigation. File inventory/detail/download reads do not use that
upload gate. The pilot operator runbook and actual cookie/CSRF negative
upload evidence are owned by the integrated qualification work. Until the
target tenant's persisted flag and denial are verified, **general-user
upload remains NO_GO and read-only pilot qualification is not claimed**.

## Verification

- `git diff --check`: passed.
- Standalone Sass compression: below the unchanged 9 kB component error
  budget after consolidating repeated component declarations. Production
  Angular output remains the authoritative budget check.
- The first Files unit compilation identified stale uploader references in
  three existing UI tests. They were replaced by no-dispatch pilot cases.
- The next Files selection compiled and ran 149 tests: 124 passed and 25
  failed in one component file. Its first failure was a stale test expectation
  for an empty grid during reauthorization; the new state correctly renders a
  loading message and no grid. The unflushed GET then caused 24 TestBed
  lifecycle cascades. The expectation now asserts loading/no grid, and strict
  HTTP verification runs with guaranteed fixture cleanup.
- Final focused Files selection: **149 passed across 16 files** in
  `mcr.microsoft.com/playwright:v1.64.0-noble` with existing shared dependencies,
  `CI=true`, and two workers. The existing Vite CommonJS/native-loader warning
  and jsdom focusability diagnostic remain visible. No assertion, request
  verification, timeout, or dependency policy was weakened.
- Frontend architecture source check: **passed**.
- `student-council-files.spec.ts` adds the requested 1536/1366/768/390/320
  widths with synthetic list, preview, Details, scoped search, denied read,
  no-upload, overflow, browser-error, keyboard focus, and axe assertions.
  The integrated pinned Linux run and visual comparison are pending.
- No dependencies, lockfiles, hosted output, migrations, backend API, or
  feature activation were changed. No PostgreSQL, actual pilot host, public
  HTTPS, scanner, or release qualification is claimed by this candidate.

Visual QA final result: **blocked pending integrated Linux rendered
comparison**. Exporting the reference and compiling code alone do not prove
Penpot parity. The absence of source transfer/runtime data is an explicit
adaptation, not an implemented source workflow.
