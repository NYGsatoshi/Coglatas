# Data Lifecycle

Coglatas uses a conservative lifecycle for school data. User-generated and business-critical records should not be physically deleted by normal UI actions.

## States

- `Active`: normal current work.
- `Archived`: historical, normally read-only, hidden from ordinary active lists unless an archive filter is used.
- `SoftDeleted`: hidden from normal views, retained for auditability and recovery.
- `Purged`: physically removed by a future explicit admin cleanup process.

## Default Policy

- Active projects are available for normal collaboration.
- Completed projects and graduation-year data should be archived.
- Deleted projects should be soft-deleted, not physically deleted.
- Purge is a future manual/admin-only operation.
- Audit logs are retained according to policy and are not user-deletable through normal UI.
- Files use metadata soft delete. Physical file cleanup is a later explicit cleanup job.

## Archive vs Delete

Archive is not delete. Archive keeps historical records available for authorized review and export. Delete hides records from normal views and records deletion metadata:

- `DeletedAt`
- `DeletedByUserId`
- `DeleteReason`

Major tenant-owned soft-deletable entities inherit this standard metadata through `SoftDeletableEntity`. `FileObject` keeps the same metadata with file-specific status handling.

## Current MVP Behavior

Implemented:

- Standard soft-delete metadata on the shared soft-delete base.
- Project, workspace, and group archive/restore endpoints.
- Normal project lists exclude soft-deleted projects.
- Normal project lists exclude archived projects unless the archive filter is used.
- Archive operations create audit logs.
- Tenant export includes soft-delete metadata.

Documented only:

- Automatic purge jobs.
- Full restore.
- Tenant restore.
- Project/file restore.

## Purge

`IDataPurgeService` exists as a placeholder interface. Automatic purging is not implemented in the MVP. Any future purge job must:

- Be explicit and administrator-controlled.
- Exclude audit logs unless a retention policy authorizes deletion.
- Verify tenant scope.
- Record an audit log.
- Avoid deleting file bodies before metadata and retention checks pass.
