# Pilot Status

Last updated: 2026-06-08

Verification snapshot: `dotnet build Coglatas.slnx -c Release` passed with 0 warnings and 0 errors on 2026-06-08. `dotnet test Coglatas.slnx -c Release` passed with 88 passed, 0 failed, and 0 skipped on 2026-06-08. Runtime startup was partially verified: the app starts in Development, `/health/live` returns 200, and `/api/auth/me` returns 401 when unauthenticated. `/health` and `/health/ready` return 503 because the local PostgreSQL server requires a password while the verified local connection string did not provide one.

## Implemented

- Modular ASP.NET Core app with application, domain, infrastructure, and web layers.
- Tenant model, tenant membership, tenant settings, plans, subscriptions, and usage records.
- EF global query filters for `ITenantEntity`.
- Tenant stamping and mismatched TenantId rejection in `AppDbContext`.
- Tenant resolver for SaaS/on-prem strategies.
- PlatformAdmin tenant lifecycle APIs.
- Tenant switching with active membership checks.
- OnPremSingleTenant default tenant support and switching disabled.
- Suspended tenant resolution rejection and tenant-owned save guard.
- File storage abstraction with local filesystem implementation.
- Tenant-namespaced file storage keys.
- File upload policy and quota hooks.
- Feature-flag service with tenant overrides.
- Audit logs and security events.
- Global exception middleware with safe production errors and TraceId.
- `/health/live` and `/health/ready`.
- HSTS, HTTPS redirection, secure cookie configuration, and baseline security headers.
- Named rate-limit policies for login, invite registration, file upload, search, and API token creation.
- Service/EF tenant isolation and security tests.
- Authenticated HTTP tenant isolation tests for tenant resolution, core resource boundaries, notifications, file metadata/download denial, outsider access, and unauthenticated rejection.
- Tenant-aware shell context, tenant switcher, separated Platform Admin and Tenant Admin navigation, tenant usage/quota/feature display, and onboarding checklist UI.
- Local demo documentation, smoke checklist, API smoke examples, handoff guide, demo script, limitations note, and school-facing explanation.
- Development Data Protection key path configuration for local runs in restricted workspaces.

## Partially Implemented

- Rate limiting exists for sensitive implemented routes, but password reset does not exist yet.
- Readiness checks database, storage configuration, and on-prem default tenant; object storage checks remain configuration-level until object storage is implemented.
- File sharing, upload size, extension allowlist, MIME allowlist, and quota enforcement are implemented in upload paths; broader project/task feature gates need more service-level enforcement before pilot.
- Search is tenant-filtered through global filters and membership predicates, but lacks PostgreSQL-backed HTTP integration tests.
- PlatformAdmin tenant lifecycle is audited at service level; full HTTP audit assertions are still needed.
- Suspended tenant behavior is enforced at resolution/switching boundaries and tenant-owned save boundaries.
- OnPremSingleTenant mode is configuration-backed and seed-aware, but every pilot installation still needs a real startup, storage, backup, and restore smoke pass.
- API smoke examples exist, but they use placeholders and were not run against a seeded environment in this verification pass.

## Not Implemented

- Production object-storage adapter.
- Full tenant restore.
- Password reset flow and rate-limit policy.
- API request metering middleware.
- API token request authentication middleware.
- PostgreSQL-backed authenticated HTTP tenant isolation harness.
- Frontend automated tests for tenant switcher, admin navigation visibility, disabled modules, suspended tenant state, and quota warnings.
- Background job health checks.
- Full CSRF token enforcement if cookie-auth browser clients begin using unsafe methods from rendered pages.
- Live streaming, voice/video, E2EE, full billing, advanced SSO, full-text search engine, complete external integrations, advanced Gantt drag editing, and full free-form docking.

## Verification Status

- Build status: Ready. Release build passed with 0 warnings and 0 errors.
- Test status: Ready. Release test run passed with 88 passed, 0 failed, and 0 skipped.
- Run status: Not ready. App startup and `/health/live` were verified, but `/health` and `/health/ready` return 503 until the local database connection string includes valid PostgreSQL credentials and migrations are applied.
- Migration status: Partially verified. EF migrations compile and list through the direct `dotnet-ef.dll` fallback. Applied/pending database status was not determined because PostgreSQL rejected the passwordless local connection string with `No password has been provided but the backend requires one (in SASL/SCRAM-SHA-256)`.
- Docker status: Not run in this pass. Dockerfile and compose files are present and use required `POSTGRES_PASSWORD` variables, but container startup was not verified.

## Critical Blockers

- Local database readiness is blocked until `ConnectionStrings:DefaultConnection` includes valid credentials for the local PostgreSQL server and migrations are applied.
- Manual smoke workflows were not completed because database readiness failed.
- SaaS readiness remains Unsafe because PostgreSQL-backed search isolation is unverified and production object storage is missing.

## High Blockers

- PostgreSQL-backed search isolation tests are still missing.
- Production object storage is not implemented.
- Backup and restore drill has not been executed or recorded.

## Remaining Manual Checks

- Local dev startup with a valid PostgreSQL password and migrated database.
- `/health/ready` after database migration.
- Default tenant resolution in Development and OnPremSingleTenant modes.
- Login or invite-based user creation.
- Create workspace, group, project, and task.
- Direct message workflow if seeded users exist.
- File upload/download authorization.
- Notification visibility/read state.
- TenantA/TenantB manual HTTP isolation check against a migrated environment.
- Docker Compose startup.
- Backup and restore drill.

## Readiness

- Demo readiness: Not ready. The app starts and live health works, but database-backed readiness and smoke workflows are not verified.
- Internal school pilot readiness: Not ready. Build/tests are green, but local database readiness, manual smoke, and restore rehearsal are still open.
- School pilot readiness: Not ready. Requires target-environment smoke tests, tenant checks, and restore rehearsal.
- SaaS readiness: Unsafe. Authenticated HTTP tenant isolation now has automated coverage, but PostgreSQL search isolation is unverified, production object storage is missing, and no restore drill has been recorded.
- On-prem readiness: Not ready. OnPremSingleTenant support exists, but this pass did not verify migrated startup, default tenant readiness, file storage operations, or restore.

## Fixed In This Verification Pass

- Added optional `DataProtection:KeysPath` support in `src/Coglatas.Web/Program.cs`.
- Added Development-only `DataProtection:KeysPath` and disabled Development EventLog logging in `src/Coglatas.Web/appsettings.Development.json` so local runs in restricted workspaces do not attempt to use inaccessible user-profile key storage or Windows EventLog.
- Added authenticated HTTP tenant isolation tests in `tests/Coglatas.Tests/Tenancy/HttpTenantIsolationTests.cs`.
- Fixed tenant isolation seed attachments so file download authorization tests point at real task owners.

## Known Risks

- HTTP authorization-boundary coverage exists for core tenant isolation paths, but it still uses EF InMemory and does not replace PostgreSQL-backed search isolation tests.
- PostgreSQL-specific search behavior is not tested in CI.
- Local filesystem storage is acceptable for small on-prem only; SaaS should move to object storage.
- Backup/restore has documentation but should be rehearsed before real users.
- Full CSRF token enforcement and API token request authentication are not implemented.

## Recommended Next Work

1. Provide a valid local PostgreSQL password in `ConnectionStrings:DefaultConnection`, run migrations, and re-check `/health/ready`.
2. Run the documented smoke workflow against the migrated local database.
3. Add PostgreSQL-backed search isolation tests.
4. Run and record a backup and restore drill before pilot launch.
5. Verify Docker Compose startup and manual tenant/workflow smoke tests.
