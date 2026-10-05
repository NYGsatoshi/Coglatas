# Code Review Report

Review date: 2026-06-07

## Reviewed Scope

- SaaS/on-prem multi-tenant foundation and configuration.
- Tenant model, tenant membership, global EF query filters, tenant stamping, and tenant switching.
- Authorization shape across auth, platform admin, tenant admin, workspaces, groups, channels, posts, direct messages, announcements, notifications, projects, tasks, artifacts, events, forms, search, audit logs, files, integrations, feature flags, quotas, and UI shell foundations.
- File storage, upload validation, API token/webhook integration foundation, security headers, rate limiting, docs, and tests.

## Build Status

`dotnet build Coglatas.slnx` passed with 0 errors. It produced NU1900 warnings because the sandbox could not reach the NuGet vulnerability feed at `https://api.nuget.org/v3/index.json`.

## Test Status

`dotnet test Coglatas.slnx` passed: 77 passed, 0 failed, 0 skipped. It produced the same NU1900 vulnerability-feed warnings.

## Fixed Now

- Added a save-time active-tenant guard for normal tenant-owned writes so suspended/archived/deleted tenants cannot continue writing through a stale tenant context.
- Added tenant isolation tests for suspended-tenant write blocking and active-tenant write continuity.
- Extended `GET /api/tenants/current` with tenant display/status/user-role/app-mode/switching context for safe tenant-aware UI rendering.
- Added tenant-aware shell UI, tenant switcher, separated Platform Admin and Tenant Admin navigation, compact admin dashboards, quota/feature display, and onboarding checklist UI over existing backend APIs.
- Added `FileStorage:AllowedContentTypes` configuration and startup validation.
- Enforced MIME-type allowlist checks in general file uploads and artifact version uploads.
- Added file-upload rate limiting to the legacy `/api/attachments` upload route.
- Added file-upload rate limiting to artifact version uploads.
- Replaced the stock `SECURITY.md` template with project-specific reporting and baseline guidance.
- Updated configuration and pilot docs to match the implemented upload policy.

## Critical Findings

None found in this pass.

## High Findings

- HTTP-level tenant isolation coverage is still missing. EF/service tests are strong, but SaaS release should not rely on service-level checks alone.
- PostgreSQL-backed search isolation is not covered in CI, even though search uses provider-specific predicates and visibility joins.
- Suspended tenant write blocking is now enforced at tenant-owned `SaveChanges` boundaries and covered by tests.
- Production SaaS file storage is not ready until a real object storage adapter exists.

## Medium Findings

- CSRF enforcement is not implemented for cookie-auth unsafe API methods; current configuration is a reserved switch, not active protection.
- API token authentication middleware is not implemented. Token creation/validation is foundation-only.
- Backup and restore docs exist, but a restore drill was not verified during this review.
- Feature flags hide/control several modules, but broader API-level feature gating for all product areas remains incomplete.

## Low Findings

- Upload MIME validation needs direct service-level tests in addition to policy/config coverage.
- Some admin/system naming still uses `SystemAdmin` as a compatibility alias for `PlatformAdmin`; keep docs and UI language consistent around `PlatformAdmin`.

## Architecture Boundary Review

Controllers are thin and call application services. APIs return DTOs rather than EF entities. Business rules and resource authorization generally live in application services or infrastructure abstractions. No large boundary violation was fixed in this pass.

## Multi-Tenant Isolation Review

Tenant, TenantUser, tenant roles, platform roles, `ITenantEntity`, global query filters, tenant stamping, mismatched TenantId rejection, tenant switching, feature flags, quotas, files, search, notifications, audit logs, integrations, API tokens, forms, events, and UI shell tenant-owned records are present. Normal services do not show broad `IgnoreQueryFilters` usage; observed bypasses are limited to tenant switching/usage paths with explicit tenant predicates.

Tenant isolation confidence: Medium. The foundation is strong, but HTTP integration tests and PostgreSQL-backed search tests are required before calling SaaS isolation high-confidence.

## SaaS/On-Prem Review

SaaS, OnPremSingleTenant, and OnPremMultiTenant modes are documented and represented in configuration. OnPremSingleTenant forces configured default tenant resolution and disables switching. Development tenant headers are blocked in production by startup validation. Production setup mode is blocked in production.

SaaS readiness: Not ready for broad production SaaS because object storage, HTTP isolation tests, and PostgreSQL-backed search isolation tests are still missing.

On-prem readiness: Conditionally ready for a controlled pilot after backup/restore rehearsal and local storage operational checks.

## Recommended Next Actions

1. Add authenticated `WebApplicationFactory` tenant isolation tests.
2. Add PostgreSQL-backed search isolation tests.
3. Add suspended-tenant write guards in application services.
4. Implement object storage adapter for SaaS.
5. Add CSRF enforcement or an explicit non-cookie unsafe-request strategy.
6. Run and record a backup/restore drill.

## Final Recommendation

Pilot release recommendation: conditionally safe for an internal controlled pilot only. Unsafe for broad production SaaS until the high findings are fixed.
