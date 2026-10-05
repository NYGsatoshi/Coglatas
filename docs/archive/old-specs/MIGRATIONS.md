# Database Migrations

## Add A Migration

From the repository root:

```powershell
dotnet tool restore
dotnet ef migrations add <MigrationName> --project src/Coglatas.Infrastructure --startup-project src/Coglatas.Web
```

If the local tool is not picked up in PowerShell, use the restored tool DLL:

```powershell
dotnet $env:USERPROFILE\.nuget\packages\dotnet-ef\10.0.8\tools\net8.0\any\dotnet-ef.dll migrations add <MigrationName> --project src/Coglatas.Infrastructure --startup-project src/Coglatas.Web
```

Review generated migrations before committing.

## Apply Locally

```powershell
dotnet ef database update --project src/Coglatas.Infrastructure --startup-project src/Coglatas.Web
```

Use a local database connection string through `ConnectionStrings__DefaultConnection`.

## Apply On A VPS

1. Back up PostgreSQL and file storage first.
2. Stop or drain the app if the migration can lock heavily used tables.
3. Set production environment variables.
4. Run:

```powershell
dotnet ef database update --project src/Coglatas.Infrastructure --startup-project src/Coglatas.Web
```

For Docker deployments, run the command inside a temporary SDK container or a controlled maintenance job that has the same environment variables as the app.

## Backup Before Migration

PostgreSQL custom-format backup:

```bash
pg_dump --format=custom --file=coglatas-before-migration.backup "$COGLATAS_DATABASE_URL"
```

Also back up the configured file storage root, bucket, or volume when the migration affects file metadata.

## Rollback

EF migrations can generate down scripts, but destructive changes may not be safely reversible. Prefer:

- Restore the database backup for failed destructive migrations.
- Use forward-fix migrations for small non-destructive issues.
- Keep data-copy migrations idempotent where possible.

Generate a script for review:

```powershell
dotnet ef migrations script --project src/Coglatas.Infrastructure --startup-project src/Coglatas.Web
```

## Tenant Cautions

- Tenant-owned entities must implement `ITenantEntity`.
- Migrations that add tenant-owned tables must include `TenantId`, tenant-aware indexes, and foreign keys where appropriate.
- Backfills must preserve tenant boundaries.
- Do not use a single default tenant backfill for new production data unless the data truly belongs to the default tenant.
- Avoid destructive multi-tenant migrations without a tested restore plan.

## Pre-Tenant Development Data Backfill

Use this section when migrating a database that contains single-tenant development data created before `Tenant`, `TenantId`, and `TenantUser` existed. Back up the database and file storage first.

1. Configure `Tenancy:DefaultTenantSlug`.
2. Create the default `Tenant` if no tenant exists.
3. Create matching `TenantSettings`; create or link a pilot `Plan` and `Subscription` when the schema requires them.
4. Add `TenantId` as nullable first when writing a new backfill migration for an existing table.
5. Backfill each tenant-owned row to the default tenant only when the data is known to belong to that tenant.
6. Backfill `TenantUser` rows for users referenced by workspaces, groups, projects, tasks, messages, files, announcements, and audit records.
7. Make `TenantId` required only after verification queries prove there are no null rows.
8. Add tenant-aware indexes and foreign keys after data is valid.

Do not create a production admin account automatically during backfill. If existing admins cannot be identified reliably, assign tenant owner/admin roles manually after migration.

## File Metadata Compatibility

`FileObject` is the canonical file metadata table for new uploads. Legacy attachment/artifact storage fields must remain readable until a file migration is rehearsed.

- Preserve old storage paths/keys during backfill.
- New storage keys should include the tenant namespace, for example `tenants/{tenantId}/files/{fileId}`.
- If legacy keys do not match the tenant namespace policy, document the file move/copy plan before changing download behavior.
- Do not delete duplicate attachment storage columns until all downloads use `FileObject`.

## Verification Queries

Run equivalent PostgreSQL checks after a backfill migration:

```sql
select count(*) from workspaces where "TenantId" is null;
select count(*) from groups where "TenantId" is null;
select count(*) from projects where "TenantId" is null;
select count(*) from task_items where "TenantId" is null;
select count(*) from conversations where "TenantId" is null;
select count(*) from messages where "TenantId" is null;
select count(*) from attachments where "TenantId" is null;
select count(*) from file_objects where "TenantId" is null;
select count(*) from audit_logs where "TenantId" is null;
select "TenantId", "UserId", count(*) from tenant_users group by "TenantId", "UserId" having count(*) > 1;
```

Index verification examples:

```sql
select indexname from pg_indexes where tablename = 'messages' and indexname = 'IX_messages_TenantId_ConversationId_CreatedAt';
select indexname from pg_indexes where tablename = 'posts' and indexname = 'IX_posts_TenantId_ChannelId_CreatedAt';
select indexname from pg_indexes where tablename = 'audit_logs' and indexname = 'IX_audit_logs_TenantId_CreatedAt';
select indexname from pg_indexes where tablename = 'usage_records' and indexname = 'IX_usage_records_TenantId_Date';
```

## Rollback Limitations

Tenant backfills are data migrations. EF can generate a down script, but restoring the database backup is safer than trying to infer original null or missing tenant state after writes have occurred.

## Manual Steps

- Confirm the configured default tenant slug before applying a backfill.
- Review how existing admin users should map to tenant owner/admin roles.
- Verify legacy file storage keys and download compatibility.
- Run tenant isolation smoke tests against migrated data before allowing non-admin users in.
