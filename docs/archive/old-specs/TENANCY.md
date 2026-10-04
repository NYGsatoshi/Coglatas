# Tenancy

Tenant is the highest-level isolation unit in Coglatas. A tenant can be a school, organization, company, NPO, municipality, studio, or internal pilot environment.

Pilot handoff note: service/EF tenant isolation tests exist, but authenticated HTTP tenant isolation tests and PostgreSQL-backed search isolation tests remain required before broad SaaS pilot.

## Core Model

`Tenant` stores the platform-level organization record:

- `Id`, `Name`, `Slug`, `DisplayName`, `PrimaryDomain`
- `Status`: `Active`, `Suspended`, `Archived`, `Deleted`
- nullable `PlanId`
- audit timestamps and soft-delete timestamp

`TenantUser` stores tenant membership and role:

- `TenantId`, `UserId`
- `Role`: `Owner`, `Admin`, `Staff`, `Member`, `Guest`
- `Status`: `Active`, `Suspended`, `Invited`, `Left`, `Archived`
- `JoinedAt`, nullable `InvitedByUserId`

A user can belong to multiple tenants. Active duplicate membership for the same tenant is blocked by a unique filtered index.

## Current Tenant

Requests resolve server-side into `ICurrentTenant`:

- `TenantId`
- `TenantSlug`
- `IsAvailable`
- `IsPlatformScope`

Normal tenant-scoped endpoints require `IsAvailable`. Platform endpoints under `/api/platform/*` are explicitly marked as platform scope by middleware and may operate without a tenant filter.

Never trust `TenantId` from client request bodies. Create/update use cases must use the current server-side tenant context.

## Global Query Filter

All entities implementing `ITenantEntity` receive an EF Core global query filter:

```text
currentTenant.IsPlatformScope ||
(currentTenant.IsAvailable && entity.TenantId == currentTenant.TenantId)
```

`AppDbContext` also stamps `TenantId` on added tenant entities. If an added or modified tenant entity has a `TenantId` that differs from the current tenant, save is rejected unless the current operation is explicit platform scope.

`IgnoreQueryFilters` is allowed only in platform/tenant infrastructure services with explicit predicates and comments explaining the bypass.

## Deployment Modes

`Tenancy` configuration:

```json
{
  "AppMode": "SaaS",
  "DefaultTenantSlug": "default",
  "TenantResolutionStrategy": "Host",
  "AllowTenantSwitching": true
}
```

Modes:

- `SaaS`: resolve tenant by host, subdomain, session selection, or development header depending on configuration.
- `OnPremSingleTenant`: always resolves the configured default tenant and disables switching.
- `OnPremMultiTenant`: same resolution mechanics as SaaS, operated by the installed organization.

`HeaderForDevelopmentOnly` must not be enabled in production unless `AllowDevelopmentHeaderInProduction` is explicitly true.

## PlatformAdmin vs TenantAdmin

System role `PlatformAdmin` can manage tenants through `/api/platform/*`. Existing `SystemAdmin` is retained as an enum alias for compatibility.

Tenant roles live in `TenantUser`. Tenant `Owner` and `Admin` can manage users inside the current tenant only. Tenant admins cannot create, suspend, list, or modify other tenants.

## Isolation Risks

High-risk areas:

- New entities missing `ITenantEntity`.
- Normal services using `IgnoreQueryFilters`.
- Accepting `TenantId` from client DTOs.
- Cross-tenant joins that do not include tenant predicates.
- File, search, notification, and audit queries that omit `TenantId`.
- Platform admin APIs that do not explicitly verify `PlatformAdmin`.

When adding a tenant-owned feature, add `TenantId`, implement `ITenantEntity`, add tenant-aware unique indexes, and write a tenant isolation test.

## Tenant Export

Tenant metadata export is available through `POST /api/tenant/export`.

Rules:

- Tenant admins can export only the current tenant and only when the `TenantExport` feature is enabled.
- Platform admins can export any tenant through the explicit request payload.
- Export queries must always predicate by `TenantId`, including platform-scope exports.
- Exports must not include password hashes, raw tokens, token hashes, webhook secrets, or other sensitive secrets.
- File bodies are not exported in the MVP. `FileObject` metadata and storage keys are exported for future migration and restore design.
- Each export creates an audit log entry.

## Tenant-Scoped Integrations

Integration accounts, webhook endpoints, and API tokens are tenant-owned records.

Rules:

- Do not accept `TenantId` from integration request bodies.
- Use the current server-side tenant context for create/update/delete operations.
- Webhook APIs require the `WebhookIntegration` feature flag.
- API token APIs require the `ApiAccess` feature flag.
- API token validation returns the token tenant ID so future middleware can set tenant context before any tenant-owned operation.
- API token authentication must never bypass tenant query filters or resource authorization.
