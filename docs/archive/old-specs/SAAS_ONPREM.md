# SaaS And On-Prem Modes

Coglatas is built as one multi-tenant platform that can run in SaaS or installed on-premise modes.

Pilot handoff note: OnPremSingleTenant is the safest controlled pilot mode after manual smoke and restore rehearsal. SaaS mode is not ready for broad pilot until object storage, HTTP isolation tests, PostgreSQL search isolation tests, and restore evidence are complete.

## SaaS

`AppMode = SaaS`

Tenant resolution can use:

- `Host`: match request host to `Tenant.PrimaryDomain`; localhost falls back to `DefaultTenantSlug`.
- `Subdomain`: use the first host segment as the tenant slug.
- `Session`: use the tenant selection cookie set by `/api/tenants/switch`.
- `HeaderForDevelopmentOnly`: read `X-Tenant-Slug` only when explicitly allowed.
- `ConfigDefault`: use `DefaultTenantSlug`.

SaaS deployments should prefer `Host` or `Subdomain`. Tenant switching is allowed only to tenants where the authenticated user has active membership.

PlatformAdmin APIs are enabled in SaaS mode for tenant lifecycle, plans, subscription configuration, usage, audit logs, and security events. Tenant creation is PlatformAdmin-only. Use object storage for production SaaS; local filesystem storage is for development or temporary evaluation only.

## OnPremSingleTenant

`AppMode = OnPremSingleTenant`

The configured `DefaultTenantSlug` is always used. Tenant switching is disabled, and startup seed creates the default tenant if needed.

This mode is for an installed single organization while still keeping internal data tenant-scoped.

Startup requires `DefaultTenantSlug`. Runtime seed creates the configured default tenant and initial plans idempotently. Tenant switching is disabled by default. Plan/subscription records behave as license/configuration objects; no payment or billing integration is implied.

## OnPremMultiTenant

`AppMode = OnPremMultiTenant`

Resolution works like SaaS, but the tenants are operated by the installed organization. This supports cases such as multiple schools or departments inside one installation.

Tenant isolation is identical to SaaS. PlatformAdmin or a local super admin can manage tenants. Tenant resolution may use host, subdomain, session, or configured routing depending on the internal network and reverse proxy setup.

## File Storage

File bodies are stored outside PostgreSQL through the `IFileStorageService` abstraction. The database stores `FileObject` metadata, tenant scope, ownership scope, content type, size, scan/status fields, and the generated storage key.

Supported provider names:

- `LocalFileSystem`: implemented for development and small on-prem deployments.
- `ObjectStorage`, `S3Compatible`, `OCIObjectStorage`: configuration placeholders for SaaS/object-storage deployments; production adapters should keep the same provider-neutral interface.

Local filesystem storage is acceptable for small on-prem deployments when the storage root is backed up and monitored. SaaS deployments should use object storage. On-prem installations can use local filesystem storage, NAS-mounted storage, MinIO, or another S3-compatible object storage service once an adapter is enabled.

Storage keys are tenant-namespaced, for example `tenants/{tenantId}/files/{fileId}` and `tenants/{tenantId}/projects/{projectId}/files/{fileId}`. User-provided file names are metadata only and are never used as storage keys. Downloads go through application authorization and must not expose raw permanent object-storage URLs.

Backups must include both the database and the configured file storage root or bucket. The tenant ID in each storage key makes tenant export, restore, and forensic review easier.

## Plans, Features, And Quotas

On-prem deployments can run without payment integration. `Plan` and `Subscription` still exist as license/configuration objects so the same feature flag and quota paths work in SaaS and installed deployments.

Quota enforcement may be advisory or strict depending on deployment policy. The current foundation enforces file upload size and tenant storage quota through `IQuotaService`; user, project, guest, and API quotas have service hooks for later enforcement.

Feature flags are tenant-scoped and can be derived from plan defaults plus tenant overrides. They control enabled modules such as production tracking, radial menu, docking layout, forms, calendar, API access, and file sharing. Enterprise/on-prem admins can later use the same model for offline license files or centrally managed configuration.

Tenant metadata export is controlled by the `TenantExport` feature. Webhooks are controlled by `WebhookIntegration`. API tokens are controlled by `ApiAccess`.

## Tenant-Aware UI

The authenticated shell calls `GET /api/tenants/current` and shows the current tenant display name, tenant status, current user's tenant role, app mode, and whether tenant switching is enabled. When switching is allowed, the shell calls `GET /api/tenants/my` and renders only server-returned memberships; switching posts the selected tenant id to `POST /api/tenants/switch` and reloads tenant-scoped navigation/data.

OnPremSingleTenant hides tenant switching because `AllowTenantSwitching` is false for that mode. The UI exposes an on-prem setup checklist at `/onboarding` for default tenant, admin user, file storage, database, backup, and HTTPS checks. It does not create a production admin password or bypass backend setup policy.

PlatformAdmin users see a separate `/platform-admin` area backed by `/api/platform/*` endpoints for platform overview, tenants, usage, and plans. Tenant Owner/Admin users see `/tenant-admin` for the current tenant overview, settings summary, usage/quota, and enabled features. Tenant admin UI is current-tenant only and does not expose plan/subscription editing.

Feature-aware navigation comes from `GET /api/ui/modules`, which is filtered by tenant feature flags and roles in application code. The frontend hides unavailable modules, but backend feature gates remain authoritative.

SaaS deployments should keep webhook URLs HTTPS-only. On-prem deployments may later allow internal HTTP webhooks through an explicit configuration switch, but the MVP rejects HTTP URLs by default.

SaaS backups must include database backups, object storage backups or versioning, app settings, vault/secret recovery, audit retention, and tenant-level export. On-prem backups must include PostgreSQL dumps or snapshots, local/NAS/object file storage, Docker volumes when used, configuration, and secrets. See `docs/archive/old-specs/BACKUP_RESTORE.md`.

## Seed And Migration

Migration `MultiTenantFoundation` creates a deterministic default tenant:

```text
11111111-1111-1111-1111-111111111111
```

Existing tenant-owned rows are backfilled to that tenant. Runtime seed is idempotent and can be enabled with `Tenancy:SeedOnStartup`; it also runs automatically for `OnPremSingleTenant`.

Do not hardcode production platform-admin passwords. Use a documented setup flow or development-only seed when that feature is added.

## Production Safety Checklist

- Use production secrets from environment variables or a secret manager.
- Keep `Tenancy:AllowDevelopmentHeaderInProduction=false`.
- Keep `Security:CookieSecurePolicy=Always`, `Security:RequireHttps=true`, and `Security:EnableHsts=true`.
- Disable `Platform:PlatformAdminSetupMode` before production.
- Back up both PostgreSQL and the configured file storage root or bucket.
- Test tenant resolution for every configured host/subdomain before go-live.
