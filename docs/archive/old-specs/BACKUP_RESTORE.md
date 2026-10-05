# Backup And Restore

Coglatas recovery has two layers:

- Full-system backup and restore for operators.
- Tenant metadata export for school-by-school portability and future migration.

The MVP implements tenant metadata export only. It does not implement a full restore engine.

Pilot handoff note: a written backup procedure is not enough. Each pilot environment must record at least one successful restore drill before real school data is relied on.

## SaaS Mode

SaaS backups must include:

- PostgreSQL database backups with point-in-time recovery where the hosting platform supports it.
- Object storage bucket backups or versioning for uploaded file bodies.
- Application settings backup, including deployment configuration and non-secret feature configuration.
- Secret or vault backup using the cloud provider secret-management recovery model.
- Audit log retention according to the tenant contract and platform policy.
- Tenant-level metadata export through `POST /api/tenant/export`.
- Reverse proxy and DNS configuration exports where managed outside infrastructure-as-code.
- Restore runbooks and operator access recovery.

Tenant exports include metadata JSON in a ZIP file. File bodies are not exported in the MVP. File metadata includes storage keys so a future file-body export or restore can reconcile database records with object storage.

Recommended SaaS schedule:

- Database: daily full backup plus point-in-time recovery if available.
- Object storage: versioning or daily bucket backup.
- Configuration: on every deployment change.
- Secrets: secret-manager recovery enabled and tested.
- Audit logs: retain according to contract; do not truncate before export/retention windows.

## On-Prem Mode

For PostgreSQL:

```bash
pg_dump --format=custom --file=coglatas.backup "$COGLATAS_DATABASE_URL"
```

For local file storage, back up the configured `FileStorage:RootPath` using the organization's normal filesystem backup tooling. If Docker volumes are used, back up the named database and storage volumes while the application is stopped or while storage snapshots are consistent.

Back up:

- Database.
- Local file storage root, NAS mount, MinIO bucket, or S3-compatible bucket.
- `appsettings.*.json` and environment-specific configuration.
- Secrets stored outside appsettings, such as connection strings and storage credentials.
- Docker Compose files, Docker volume definitions, and deployment scripts if used.
- Reverse proxy configuration and TLS certificate renewal configuration.
- Admin bootstrap records and support runbooks.

Restore into a test environment first:

1. Restore PostgreSQL from the backup.
2. Restore file storage to the configured path or bucket.
3. Restore configuration and secrets.
4. Start the app against the restored database and storage.
5. Verify login, tenant selection, project lists, file metadata, and file download authorization.
6. Check audit logs for continuity.
7. Verify tenant isolation tests or manual TenantA/TenantB checks.
8. Verify `/health/ready`.

Untested backups are not backups. Every production backup policy must include scheduled restore drills.

## Config And Secrets

Back up non-secret configuration separately from secrets:

- Non-secret config: deployment mode, tenant resolution strategy, file storage provider, allowed file extensions, feature defaults.
- Secrets: database passwords, storage credentials, API keys, webhook secrets, cookie/data-protection keys once configured.

Secrets must be recoverable through a secret manager, protected vault, or documented operator procedure. Do not commit production secrets.

## Test Restore Procedure

1. Create an isolated restore environment.
2. Restore database backup.
3. Restore file storage backup from the same recovery point.
4. Restore configuration and secrets.
5. Apply any pending migrations only after confirming the restored app version.
6. Start the app.
7. Check `/health/live` and `/health/ready`.
8. Sign in as an admin test account.
9. Verify file upload and download authorization.
10. Verify TenantA/TenantB isolation manually or with automated tests.
11. Record restore time, issues, and operator.

## Full-System Restore Outline

A future full-system restore should:

- Restore the complete database and storage set from the same recovery point.
- Verify storage keys referenced by `FileObject` records exist.
- Rebuild derived search indexes or caches.
- Preserve tenant IDs exactly.
- Append operator audit records after the restored system starts.

## Future Tenant Restore

Tenant-level restore is intentionally not implemented yet. A future design must:

- Never overwrite another tenant.
- Validate the source tenant ID and destination tenant ID mapping.
- Import into a staging area before merge.
- Reconcile users, memberships, workspaces, groups, projects, tasks, comments, artifacts, files, settings, feature flags, usage records, and audit references.
- Audit every restore operation.

## Other Restore Types

Future restore types:

- Project-level restore.
- File restore.
- Accidental deletion recovery.
- Tenant migration import.

File restore must verify both `FileObject` metadata and the underlying storage object.
