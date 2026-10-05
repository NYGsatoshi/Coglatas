# P0 Files sharing-state verification

Issue #360 gives direct Workspace attachments a persisted, server-owned
sharing policy and explicit per-file grants. It does not add public links,
email-address authority, client-side audience inference, or a Folder/Move
contract.

## Contract and disclosure

- `FileObject.SharingPolicy` is `Private` or `Workspace`; a migration treats
  existing rows as `Workspace` so the upgrade does not silently remove legacy
  Workspace reads. New direct Workspace uploads begin `Private`.
- An active effective external Project grant makes the presentation state
  `External`; otherwise the baseline maps to `Private` or `Workspace`.
  Browser code maps only that explicit server projection and renders an
  unavailable state for malformed or omitted values.
- A recipient is eligible only when the server derives an active same-Tenant
  Workspace member or Project-scoped external user from canonical persistence.
  An external user must not also have active Workspace membership. A stored
  grant is rechecked against active User, TenantUser, Workspace, attachment,
  FileObject, and membership state on each use.
- All authorized readers may see the textual state in the Files list and
  Preview header. Only a current sharing manager receives external recipient
  counts, recipient identity, or candidate identity. The sharing controls are
  not rendered without the server capability, and all mutation routes recheck
  Workspace management on the server.

## Mutation and reauthorization

- `PUT /api/files/{fileObjectId}/sharing`, `POST
  /api/files/{fileObjectId}/sharing/recipients`, and `DELETE
  /api/files/{fileObjectId}/sharing/recipients/{grantId}` require the current
  `SharingVersion`. Changes increment the version, record a safe audit event,
  and queue the existing File invalidation.
- File list, File detail/Preview, Search, grant issue, and grant use apply the
  current sharing boundary. The download-grant policy stamp includes the
  sharing policy and version, so a prior grant cannot survive a change or
  revoke.
- The manager dialog applies the returned authoritative projection to both the
  visible list/search row and Preview without inventing state locally. An idle
  search snapshot is not rewritten, avoiding an unrelated protected-state
  reset that would close the active Preview.

## Verification commands

```powershell
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj --filter "FullyQualifiedName~FileSharingServiceTests|FullyQualifiedName~WpcFinal03FileAuthorizationTests"
npm --prefix frontend test -- --include="src/app/features/files/files.api.spec.ts" --include="src/app/features/files/files-page/files-page.issue-352.spec.ts"
npm --prefix frontend run build
$env:PLAYWRIGHT_SKIP_BUILD='1'; npm run test:ui:angular -- --grep "server-authorized File sharing state"
dotnet ef migrations has-pending-model-changes --project src/Coglatas.Infrastructure --startup-project src/Coglatas.Web --context AppDbContext --no-build
```

## Focused proof

- Service tests cover exact `Private`/`Workspace`/`External` states,
  manager-only external count disclosure, no count/recipient identity for a
  reader, mutation denial without sharing management, server-only recipient
  eligibility, revocation, version advancement, audit/invalidation, and the
  refreshed projection.
- File authorization tests prove a direct private Workspace attachment is
  denied until the current server-side effective grant succeeds; download uses
  the same read boundary.
- Angular tests fail closed on malformed sharing DTOs, do not expose protected
  recipient data without server inspection authority, omit the sharing control
  without server capability, and reconcile grant/revoke responses in both
  Preview and list state.
- Built-app Playwright coverage runs the manager flow keyboard-only at 320px,
  verifies textual list/Preview labels and revoke reconciliation, focus return,
  dark-theme continuity, no horizontal overflow, and axe on the dialog.
