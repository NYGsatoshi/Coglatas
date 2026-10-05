# P0 Files search and Filter Chips verification

Issue #341 extends the merged Workspace File search contract without creating
a second File authorization boundary.

## Binding and interaction

- The maintained Files page presents file-name search with Type, Modified, and
  Owner controls in one `role=search` surface. Its scope names the active
  Workspace and says that rows and counts are currently authorized results.
- Type supports Documents, Images, PDF, Video, and Archives. Modified supports
  Last 7, 30, and 90 days. Owner deliberately supports only Anyone and Uploaded
  by me; there is no new user-enumeration contract.
- Applied facets remain visible in small shared `CoglatasFilterChipComponent`
  controls. Every removal is a native button with the facet name/value in its
  accessible name and immediately reruns the remaining server query.
- Filtered rows reuse the Files desktop grid, 320-pixel mobile list, and
  short-lived preview/download grants. Search rows never infer delete authority.

## Security boundary

`GET /api/search` remains the owner of filtered membership and `totalCount`.
The File branch first proves the requested active Workspace is currently
readable, then constrains direct Workspace Attachments, applies filename/type/
modified/uploader predicates, and only then pages the result. Revoked or
mismatched Workspaces contribute zero rows and zero count.

The Angular adapter rejects the complete projection, including its count, when
any row has a non-File type, mismatched Workspace, malformed identity, or an
invalid paging envelope. It never maps `snippet`, storage keys, internal paths,
or raw scan metadata. Search state and pending requests are synchronously
cleared by the existing Files protected-state callback on Workspace/session/
authorization boundaries.

## Verification commands

```powershell
dotnet build src/Coglatas.Web/Coglatas.Web.csproj --no-restore
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj --filter "Scope=Issue329"
npm --prefix frontend test -- --include="src/app/features/files/**/*.spec.ts" --include="src/app/shared/ui/coglatas-filter-chip/*.spec.ts"
npm --prefix frontend test
npm --prefix frontend run build
npm --prefix frontend run check:architecture
$env:PLAYWRIGHT_SKIP_BUILD='1'; $env:PLAYWRIGHT_PORT='4193'; npm run test:ui -- --grep "keeps scoped Files search and removable filter chips"
```

The PostgreSQL-specific authorization and facet test requires
`POSTGRES_TEST_CONNECTION_STRING`. Static Playwright responses are mocked and
prove browser behavior, keyboard operation, 320-pixel containment, and axe
coverage only; they are not browser-to-ASP.NET Core integration evidence.

## Current local evidence

- .NET Web build: passed with 0 warnings/errors.
- Full .NET solution suite: passed. PostgreSQL-dependent cases were skipped
  because `POSTGRES_TEST_CONNECTION_STRING` is not set locally.
- Focused Angular Files/chip selection after rebasing onto the Issue #356
  inspector: 61 passed across 8 files.
- Full Angular suite: 964 passed across 93 files.
- Angular production build: passed. Existing repository bundle/style warnings
  remain non-blocking; the Files stylesheet remains below its hard 8 kB gate.
- Syncfusion license policy, bundle analysis, and frontend architecture checks:
  passed (including all 4 architecture policy tests).
- Focused Chromium desktop/mobile Playwright at a forced 320-pixel viewport:
  4 passed across the search/chip and inspector flows, including keyboard chip
  removal, inspector tab operation, horizontal containment, and axe.
- Conditional PostgreSQL execution is not claimed unless the required
  connection string is supplied; authoritative CI supplies that gate.
