# Migration Test Plan

Migration/backfill tests are database-heavy and should be rehearsed against a disposable PostgreSQL database with a pre-tenant fixture.

## Fixture

- One user with workspace/project/task/message/file data and no tenant rows.
- One existing admin-like user if identifiable from legacy roles.
- Attachments and artifact versions using legacy storage metadata.
- Audit logs without `TenantId` where the old schema allowed it.

## Expected Results

- A default tenant exists using `Tenancy:DefaultTenantSlug`.
- Existing workspace, group, project, task, message, attachment, file, audit, and security rows receive the default `TenantId`.
- Referenced users receive one active `TenantUser` row for the default tenant.
- Duplicate `TenantUser` memberships are not created.
- Migrated data is queryable under the default tenant.
- A second tenant cannot see migrated rows through normal tenant-scoped services.
- Legacy file downloads still resolve using preserved storage fields.

## Verification

Run the verification queries in `docs/archive/old-specs/MIGRATIONS.md`, then run:

```powershell
dotnet build Coglatas.slnx
dotnet test Coglatas.slnx
```

For a production-like rehearsal, also run an application smoke test with authenticated default-tenant and other-tenant users.
