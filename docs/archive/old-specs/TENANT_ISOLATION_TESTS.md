# Tenant Isolation Tests

## What Is Tested

The tenant isolation suite is in `tests/Coglatas.Tests/Tenancy`.

Covered:

- Tenant fixture data for `TenantA`, `TenantB`, `SuspendedTenant`, platform admin, tenant owners/admins/staff/members/guests, cross-tenant user, outsider, and suspended-tenant user.
- Tenant-owned records for workspaces, groups, projects, tasks, files, conversations, announcements, notifications, audit logs, and security events.
- EF global query filters return only current-tenant records.
- Normal `DbContext` queries cannot find another tenant's workspace, group, project, task, file, conversation, or announcement by ID.
- New tenant entities are stamped with the current tenant.
- TenantId changes that do not match the current tenant are rejected.
- Platform-scope creation of tenant-owned data must set TenantId explicitly.
- Cross-tenant user switching to both active tenants succeeds.
- Single-tenant users and outsiders cannot switch into tenants they do not belong to.
- `OnPremSingleTenant` disables tenant switching.
- Suspended tenants cannot be resolved by the HTTP tenant resolver or switched into.
- Stale suspended tenant contexts cannot save tenant-owned writes after tenant status changes.
- Active tenant contexts can still save tenant-owned writes.
- PlatformAdmin can list tenants, suspend tenants, activate tenants, and audit entries are emitted.
- PlatformAdmin does not bypass tenant filters on normal tenant-scoped endpoints.
- TenantAdmin audit query sees only current-tenant audit logs.
- Tenant feature flags and quota limits are tenant-scoped.
- File storage keys include the tenant namespace.
- Local storage path traversal is rejected by `LocalFileStorageServiceTests`.
- Local storage does not expose signed URLs.

## How To Run

```powershell
dotnet test Coglatas.slnx --filter "FullyQualifiedName~Tenancy"
```

Full verification:

```powershell
dotnet build Coglatas.slnx
dotnet test Coglatas.slnx
```

CI runs restore, build, and test through `.github/workflows/ci.yml`.

## Known Gaps

- HTTP API isolation tests are not yet backed by `WebApplicationFactory` or authenticated test clients. Current coverage is EF/service-level, not full request pipeline coverage.
- Search tests currently verify tenant-scoped query roots, but not a full PostgreSQL-backed search request because EF InMemory does not execute provider-specific `ILike`.
- Suspended-tenant write blocking is proven at tenant resolution, switching boundaries, and tenant-owned save boundaries.
- File download authorization is covered in application code and file tests, but a full cross-tenant HTTP download test should be added with the integration harness.
- Platform API cross-tenant access is tested at service level for tenant lifecycle. Add HTTP tests for `/api/platform/*` before a public pilot.

## Manual Verification Checklist

- Sign in as a TenantA user and confirm TenantB workspaces, groups, projects, tasks, files, messages, announcements, notifications, and audit logs are invisible.
- Repeat as a TenantB user.
- Confirm `CrossTenantUser` can switch between TenantA and TenantB.
- Confirm `TenantAMember` cannot switch to TenantB.
- Confirm `Outsider` cannot switch to TenantA.
- Suspend a tenant as PlatformAdmin and confirm normal tenant routes no longer resolve it.
- Confirm PlatformAdmin uses `/api/platform/*` for tenant lifecycle actions.
- Confirm normal user endpoints remain tenant-filtered even for PlatformAdmin.
- Upload and download a file and verify the `FileObject.StorageKey` starts with `tenants/{TenantId}/`.
- Try path traversal storage keys in local storage and confirm they fail.
- Confirm tenant feature-flag changes affect only the selected tenant.
- Confirm storage quota and project limit failures are tenant-local.

## Dangerous Patterns To Avoid

- Accepting `TenantId` from request bodies.
- Using `IgnoreQueryFilters` in normal services.
- Querying tenant-owned data by `Id` without a tenant filter or current tenant context.
- Returning `FileObject` or attachment metadata without an access check.
- Exposing signed URLs without checking authorization first.
- Adding admin endpoints without clear platform-admin and tenant-admin separation.
- Running search without tenant filtering.
- Creating tenant-owned entities in platform scope without explicitly setting TenantId.
- Letting PlatformAdmin use normal tenant endpoints as an accidental cross-tenant bypass.
