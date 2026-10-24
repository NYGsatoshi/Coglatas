# Retained download credentials across Workspace folder placement

Status: source-only candidate; **NOT_EXECUTED**. Base Main:
`0c5f9baace186940f948758b1731fd3ec891c2f3`. Local resource HOLD remains active;
build, compiler, test, PostgreSQL and browser execution counts are null.

The new named fact is
`FileDownloadGrantLifecycleHostedHttpTests.WorkspaceFolderPlacementPreservesRetainedGrantsAndRejectsStaleAndOtherWorkspaceDestinations`
in `PostgreSql/FilePlacementRetainedCredentialHostedHttpTests.cs`.

## Existing contract and selected gap

`FileFolderService.MoveFileAsync` organizes a current readable, contributing
Workspace File using source/destination versions. Its destination lookup remains
inside the source Workspace. It updates logical placement and container versions;
it does not transfer Tenant/Workspace ownership or change sharing. The existing
`FileFolderHostedHttpTests` checks those boundaries and direct physical bytes,
but does not retain opaque download credentials across the move.

`FileService.ComputeFilePolicyStamp` includes current actor, resource, owner scope,
classification, sharing version, target status and scan status. It excludes folder
placement. The selected expectation is continued current-authorized access through
the same retained FileObject and Attachment credentials, rather than treating a
folder as an additional permission boundary. This follows current source and
existing placement tests; it neither adopts a new private specification mapping
nor creates a folder permission or cross-Workspace move contract.

## Proposed controls

- Real synthetic Workspace upload and a separate current Owner-only Workspace File;
  independent Member and Owner cookie/CSRF sessions. The move actor is an active
  Owner in both same-Tenant Workspaces before the cross-Workspace destination denial.
- Six distinct current credentials: Member and Owner for both target route families,
  plus both unrelated control routes. Exact physical bytes before and after moves.
- Missing-CSRF 403 and unchanged selected state, followed by current-version move
  200, exact location/container version changes and unchanged File/share/grant state.
- Stale File version, stale destination version and another currently visible
  Workspace's destination each require bounded 400, no redirect/filename/protected
  markers and unchanged selected File/grant/use/folder/placement state.
- Return to root with current versions, retained binding/read positives, fresh
  current Member grants and direct downloads, unchanged unrelated placement and
  safe selected audit contents. Original Member session remains current.

No shared helper, product, API, schema, migration, authorization policy, intake,
scanner, RLS, workflow, deadline, retry or threshold is changed. Only this new
test file and this document are included. Test-only PostgreSql paths select the
existing migrated scoped backend and TRX verification route; CI must establish
actual named-case execution. Minimum-count checks alone are insufficient.

This candidate covers neither foreign-Tenant placement, every role/folder
combination, concurrent linearizability, full SEC-16, HTTPS staging, RLS nor release
readiness. Existing private candidates and held PRs are not integrated. Independent
source review and exact-source hosted execution remain required before merge.
