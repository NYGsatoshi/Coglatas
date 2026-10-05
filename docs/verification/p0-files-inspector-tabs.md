# P0 Files inspector tabs verification

Issue #356 uses the existing server-authorized Workspace file inventory and
short-lived preview grant flow. It changes presentation only; it adds no file
metadata, activity, version, edit, or authorization API.

## Interaction and information hierarchy

- One responsive right-side inspector owns Preview, Details, and Activity. At
  widths of 860 pixels or less the same inspector becomes the existing modal
  drawer, so Preview and Details never consume separate permanent columns.
- The tabs use native buttons, `tablist`/`tab`/`tabpanel` relationships, a
  single roving tab stop, Arrow Left/Right wrapping, Home/End navigation, and
  existing drawer focus trapping/restoration.
- Preview remains the default whenever a file is opened or the inspector is
  closed. Its grant-backed rendering and Open, Cite, Research, Share, and
  Download behavior are unchanged.
- Details initially shows only Type, Size, Owner, Modified, Location, and
  Access. Created, scan status, and canonical File ID are in a collapsed native
  disclosure. Checksum, source URL, Research references, and tags are omitted
  because the authorized Files DTO does not project them.
- The current Files contract projects no editable metadata field or edit
  capability, so the inspector renders no edit control. It does not infer
  authority from a role, hidden control, or browser state.

## Security and downstream boundary

All displayed values come from the already-authorized, Workspace-scoped File
list projection. Access is a presentation of the explicit download policy,
scan state, and download capability; it is not an authorization decision.
Internal storage keys, paths, and raw scan metadata are never rendered.

The Activity tab explicitly reports that the current Files API exposes no file
activity or version history. Opening it issues no Audit, activity, or version
request and therefore cannot widen the Files read boundary or leak actor,
correlation, or storage data. Issue #363 remains responsible for defining a
bounded, file-specific, server-authorized activity/version contract.

## Verification commands

```powershell
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj --filter "FullyQualifiedName~FileWorkspaceWorkflowTests|FullyQualifiedName~WpcFinal03FileAuthorizationTests|FullyQualifiedName~FileDownloadGrantBoundaryTests"
npm --prefix frontend test -- --include='src/app/features/files/files-page/files-page.issue-356.spec.ts'
npm --prefix frontend test -- --include='src/app/features/files/**/*.spec.ts'
$env:VITEST_MAX_WORKERS='4'; npm --prefix frontend test
npm --prefix frontend run build
npm --prefix frontend run check:architecture
npm --prefix frontend run test:architecture
$env:PLAYWRIGHT_SKIP_BUILD='1'; npm run test:ui:angular -- --grep "one keyboard-accessible File inspector"
```

## Exact-head result

- Focused backend Files workflow, authorization, and grant-boundary selection:
  29 passed.
- Issue #356 component selection: 5 passed.
- Files feature selection: 50 passed across 6 files.
- Full Angular suite with four supported Vitest workers: 958 passed across 92
  files. An earlier maximum-concurrency diagnostic recorded six five-second
  timeouts (including four unrelated tests); the bounded rerun passed every
  assertion.
- Production Angular build: passed. Existing repository bundle and unrelated
  style-budget warnings remain non-blocking; the Files stylesheet remains
  below its 8 kB error budget.
- Architecture source check: passed. Architecture test suite: 4 passed.
- Representative fresh-build Files inspector smoke: 2 passed (Chromium desktop
  and mobile projects, both forced to a 320-pixel viewport), including keyboard
  operation, focus return, staged disclosure, no horizontal overflow, and axe.
- Browser responses are mocked and establish frontend behavior only. The
  backend/API/schema are unchanged. The focused backend tests use their normal
  non-PostgreSQL fixtures; `POSTGRES_TEST_CONNECTION_STRING` was unset, and no
  conditional PostgreSQL execution is claimed.
