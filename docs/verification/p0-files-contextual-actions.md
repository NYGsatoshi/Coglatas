# P0 Files contextual actions verification

Issue #345 uses the existing Workspace file inventory and single-file DELETE contract.

## Binding and interaction

- The bounded Workspace list appends server-projected `canDelete` to each row. Missing or malformed capability values map to `false`.
- With no selection, Files shows the normal Upload/View settings toolbar. Selecting rows replaces it with immediately available authorized actions and an explicit Clear selection action.
- One downloadable row exposes Download through the existing grant flow. Delete appears only when every selected row has a canonical file ID and literal `canDelete: true`.
- The destructive dialog echoes the authorized server-projected display label for one file or gives the selected count. A canonically redacted label remains `[redacted:file]`; this surface never upgrades field disclosure to recover the raw filename. Multiple files are deleted serially through `DELETE /api/files/{fileObjectId}` and partial failure text explicitly says the operation was not atomic.
- Selection is row-object state and is cleared on Workspace changes, server inventory replacement, and page changes. Desktop and mobile selection remain keyboard-operable and the shared dialog restores focus safely.

## Security boundary

`canDelete` is an advisory presentation capability, never authorization. The list evaluates the current Workspace contribution boundary once for its already Workspace-owned page, then preserves the existing uploader/owner identity rule per attachment. Every DELETE independently reloads the target and calls the full `FileAuthorizationService.CanDeleteAttachment` policy. Direct same-Workspace non-owner and cross-Tenant/cross-Workspace DELETE tests fail closed without leaking file metadata. HTTP and component coverage also prove that the existing `FileMetadata` redaction boundary survives capability projection, selection, and confirmation; this Issue does not invent a `ThroughConfidential` grant.

Download grant issuance and use retain their existing server-side reauthorization. The change adds no rename, move, share, public-link, or batch-delete contract.

## Verification commands

```powershell
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj --filter "FullyQualifiedName~FileWorkspaceWorkflowTests|FullyQualifiedName~WpcFinal03FileAuthorizationTests|FullyQualifiedName~FileDownloadGrantBoundaryTests|FullyQualifiedName~WorkspaceFileDeleteCapabilityAndDirectMutationRemainOwnerScoped"
npm --prefix frontend test -- --include="src/app/features/files/**/*.spec.ts"
npm --prefix frontend test
npm --prefix frontend run build
npm --prefix frontend run check:architecture
npm --prefix frontend run test:architecture
$env:PLAYWRIGHT_SKIP_BUILD='1'; npm run test:ui -- --grep "does not horizontally overflow at 320px: /app/files"
```

PostgreSQL-specific execution still requires `POSTGRES_TEST_CONNECTION_STRING`; this additive DTO/UI change has no migration.

## Exact-head result

- Focused backend Files, authorization, grant-boundary, and direct HTTP isolation selection: 30 passed.
- Focused Angular Files selection: 32 passed.
- Full Angular suite: 857 passed across 83 files.
- Production Angular build: passed. Existing repository budget warnings remain non-blocking; the Files component stylesheet also exceeds the current 4 kB warning threshold.
- Architecture source check: passed. Architecture test suite: 4 passed.
- Representative built-app Files smoke: 2 passed (Chromium desktop and mobile at 320 px, no horizontal overflow).
- `POSTGRES_TEST_CONNECTION_STRING` was unset, so no conditional PostgreSQL execution is claimed. No schema or migration changed.
