# Files folder interaction reliability — 2026-10-18

Tracking: [#1244](https://github.com/NYGsatoshi/Coglatas/issues/1244). Source baseline:
`50c3242950d22105063ec3b97a90adf7b5b34b0d` (includes #1234, #1237, #1240 and #1241).
This is an existing-contract Angular Web implementation candidate. Hosted PR
checks, independent review, merge and exact-Main qualification remain separate.

## Reproduced behavior and bounded repair

`FileFolderStore` previously retained the old Workspace folder names and root
version while loading a different Workspace. Its file and folder move
finalizers unconditionally reloaded the Workspace captured before the command;
a late success or failure could replace the new Workspace context with that old
scope. The dedicated regressions reproduce all four finalizer cases against the
unchanged baseline implementation.

The store now clears metadata before changing scope, cancels superseded folder
reads, cancels old-context move streams, and binds post-command refresh to the
original context epoch and current Workspace. A deferred command cannot dispatch
after an authorization reset, even when the same Workspace ID is subsequently
reloaded. Moves require a successfully loaded current folder-version snapshot.
The store registers with the existing session/Tenant/Workspace/authorization
protected-state clearing boundary and unregisters on destruction. Server
reauthorization and optimistic source/destination versions remain unchanged.
Cancelling browser observation cannot undo a command already received by the
server; no rollback or new command-atomicity guarantee is claimed.

The sidebar distinguishes loading and failed folder reads from an authorized
empty response. Manual Retry repeats only the existing current-Workspace
`GET /api/file-folders`; fixed English/Japanese copy never displays server error
details. Default root expansion now agrees with `aria-expanded` and can actually
be collapsed. Refreshed metadata removes obsolete expanded/focus identities and
restores the remaining tree's tab stop and focus after rendering. Keyboard
activation of the independent Move button is no longer intercepted as a tree
selection.

## Contract and design boundary

This change adds no API parameter, endpoint, DTO, dependency, schema, migration,
role, grant or RLS policy. Folder metadata is not a folder-filtered File list.
Workspace Files, held Project Files filters, Starred/Shared persistence,
cross-Project Artifact search, and Project/Task File links retain the boundaries
in [the existing contract matrix](web-p2-files-contract-matrix-20261018.md).

The supplied native Penpot export identifies file
`52a5041f-08a1-80f1-8008-c5a09f8b5f61`, revision 339. The existing
[Files audit](web-penpot-files-audit.md) remains the source for inspected board and
component evidence. This pass changes state handling and keyboard behavior,
introduces no design asset or new geometry, and claims no new visual parity.
The latest live Penpot revision remains UNVERIFIED because no instance is connected.
PRE-AVALONIA SEC-ARCH remains BLOCKED.

## Local verification

Execution: Windows, Node `v24.19.0`, existing pinned Angular `22.2.2` / Vitest
`5.0.3`; commands ran from this worktree's `frontend` directory. The CLI printed
this worktree's `vitest.config.ts` path, rather than the shared dependency
checkout's configuration.

| Check | Result |
| --- | --- |
| Baseline late-response regression | **4 executed / 4 failed**, as expected: file/folder success/error each dispatched an old-Workspace folder read. The 11 other cases were excluded by the diagnostic filter, not counted as passed. |
| Final complete Files Angular selection | **14 spec files / 117 tests passed**, exit 0, 32.25 seconds; no skipped tests |
| Application TypeScript | PASS, `tsc -p tsconfig.app.json --noEmit` |
| Spec TypeScript | PASS, `tsc -p tsconfig.spec.json --noEmit` |
| Frontend architecture | PASS, `node scripts/check-architecture.mjs` |
| Production build and existing theme sanitization | PASS, 25.211-second Angular build; initial bundle 927.20 kB |
| Whitespace validation | PASS, `git diff --check` |
| Real-backend/PostgreSQL integration | NOT_EXECUTED by this feature pass |
| Browser E2E, Linux screenshot parity, target staging | NOT_EXECUTED by this feature pass |
| SonarQube and hosted exact-head/exact-Main CI | UNVERIFIED pending Coordinator qualification |

The final selection includes the existing preview, file-specific Activity,
sharing, upload, download-grant, selection, pagination and locale regressions;
the new context suite contributes 15 cases. These tests use Angular's HTTP
testing backend and do not establish ASP.NET Core or PostgreSQL behavior.
The existing Vite native-config warning and the existing CDK initial-focus
warning remain visible. Existing bundle/component warning thresholds remain
unchanged; the build passed its error budgets. No generated hosted output,
lockfile or screenshot baseline is included.

Reproduction commands (run from `frontend/`):

```powershell
node node_modules/@angular/cli/bin/ng.js test --watch=false --include='src/app/features/files/file-folders.context.spec.ts' --filter='ignores a late'
node node_modules/@angular/cli/bin/ng.js test --watch=false --include='src/app/features/files/**/*.spec.ts'
node scripts/check-architecture.mjs
node node_modules/typescript/bin/tsc -p tsconfig.app.json --noEmit
node node_modules/typescript/bin/tsc -p tsconfig.spec.json --noEmit
node node_modules/@angular/cli/bin/ng.js build --configuration production
node scripts/sanitize-syncfusion-theme-css.mjs
```

The first command is the failure reproduction only when the original baseline
`file-folders.service.ts` is temporarily supplied; it passes against the repair.
The baseline source was restored to the repair before the final full Files run.
