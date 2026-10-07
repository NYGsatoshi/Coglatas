# MVP-A P0 Blockers

Verification date: 2026-06-24

## P0-001: Fresh MVP-A Startup Has No Usable Login/Admin Bootstrap

Status: Open

Classification: P0

Affected areas: Auth / Login, Tenant / User / Role, Authorization, Dashboard reachability, AuditLog verification

Evidence:

- `src/Coglatas.Infrastructure/Persistence/AppDbContextSeed.cs` seeds tenants, plans, UI shell metadata, modules, panels, commands, and radial menus. It does not seed a user, password, invite, tenant membership, workspace membership, or administrator.
- Fresh database verification against `coglatas_mvpa_fresh` after migrations and Test startup produced:
  - `tenants = 1`
  - `plans = 4`
  - `users = 0`
  - `tenant_users = 0`
- `GET /api/auth/status` returned `200` with `{"isAuthenticated":false}`.
- `GET /api/auth/me` and `GET /api/admin/users` returned `401 Unauthorized`.
- `POST /api/auth/login` with a valid CSRF token and non-existent credentials returned `401` with the generic error `Invalid email or password.`

Impact:

The login endpoint exists and rejects invalid credentials correctly, but a fresh MVP-A baseline has no supported way to authenticate as an administrator or regular user. Authenticated dashboard reachability, admin APIs, role behavior, tenant membership enforcement, 403 behavior for non-admin users, and runtime AuditLog coverage for authenticated core actions cannot be fully verified from a fresh baseline.

Required next action:

Add the smallest approved first-admin/bootstrap path or documented local seed path for MVP-A verification. The fix should create only the minimum local/dev verification identity and membership needed to verify existing auth and authorization behavior. Do not add production account management scope.

## P0-002: End-to-End Authorization Verification Is Blocked By Missing Baseline Identity

Status: Open, blocked by P0-001

Classification: P0

Affected areas: Authorization, Tenant / User / Role, File / Messaging permission checks, Dashboard reachability

Evidence:

- Anonymous protected APIs were verified as protected:
  - `GET /api/auth/me` returned `401`.
  - `GET /api/admin/users` returned `401`.
  - `GET /api/projects` returned `401`.
  - `GET /api/ui/modules` returned `401`.
- Static inspection found broad controller-level `[Authorize]` coverage and admin role attributes, including `AdminController` and `PlatformTenantsController`.
- Automated tests passed for many authorization services, including tenancy isolation, admin access denial, organization authorization, projects, files, and auth session security.
- Runtime role/tenant negative tests requiring logged-in non-admin and admin users could not be performed on a fresh baseline because no user, role, tenant membership, or session can be created through startup seed.

Impact:

Server-side authorization appears materially implemented by code and tests, but MVP-A cannot mark Authorization as Pass because direct runtime evidence for admin vs non-admin and tenant boundary behavior is blocked.

Required next action:

After P0-001, run a narrow authorization smoke suite with a seeded/admin identity and a non-admin tenant member:

- Admin endpoint as anonymous: expect 401.
- Admin endpoint as non-admin: expect 403.
- Admin endpoint as admin: expect 200 or expected domain response.
- Tenant-scoped data as wrong tenant/member: expect not found or forbidden according to API contract.
- File download as unauthorized user: expect not found or forbidden according to API contract.

## Not P0 Based On Current Evidence

- Build: Pass after rerun with escalated MSBuild process permissions.
- Database migrations: Pass against local PostgreSQL.
- Startup: Pass against local PostgreSQL in Test environment.
- Health: previous 2026-06-24 evidence passed `/health/live` and `/health/ready`; the 2026-06-28 A-02 Windows refresh is blocked because `/health/ready` returned 503 with local PostgreSQL and Docker unavailable.
- AuditLog implementation: Present, but runtime coverage is partial/blocked by P0-001.
- CI configuration: Present, but local UI test execution is blocked by missing frontend dependencies. Classified as P1 for local verification.

## A-01 Baseline Refresh Note

Refresh date: 2026-06-28

The A-01 evidence baseline is recorded in `docs/evidence/mvp-a/a-01-build-test-baseline.md` and `docs/evidence/mvp-a/a-01-baseline-failure-log.md`.

The .NET restore/build/test baseline passed on the Windows host. Docker/container evidence remains blocked by local Docker Desktop Linux engine availability, and live PostgreSQL assertions remain Needs verification because no local/dev PostgreSQL connection string was provided. These results do not change P0-001 or P0-002 and do not imply MVP-A Go or production readiness.

## A-02 Health Refresh Note

Refresh date: 2026-06-28

The A-02 health evidence is recorded in `docs/evidence/mvp-a/a-02-health-check-baseline.md` and `docs/evidence/mvp-a/a-02-health-check-failure-log.md`.

The local app process started and `/health/live` returned 200, but `/health/ready` returned 503. Local PostgreSQL on port 5432 was unavailable, Docker Desktop Linux engine was unavailable, and container/PostgreSQL health could not be verified. This A-02 refresh is Blocked and does not imply production approval, MVP-A Go, or production readiness.

## A-03 Application Smoke Refresh Note

Refresh date: 2026-06-28

The A-03 smoke evidence is recorded in `docs/evidence/mvp-a/a-03-application-smoke-baseline.md` and `docs/evidence/mvp-a/a-03-smoke-failure-log.md`.

The local app process started and public shell routes, liveness, anonymous auth status, CSRF rejection, protected-anonymous 401 behavior, and safe API 404 behavior were verified. `/health/ready` remained 503 because local DB-backed readiness was unavailable, Docker Desktop Linux engine was unavailable, frontend/UI dependencies were incomplete locally, and authenticated admin/non-admin/tenant runtime smoke remains blocked by P0-001/P0-002. This A-03 refresh is Blocked and does not imply production approval, MVP-A Go, or production readiness.

## A-04 AuthZ Boundary Refresh Note

Refresh date: 2026-06-28

The A-04 AuthZ evidence is recorded in `docs/evidence/mvp-a/a-04-authz-boundary-baseline.md` and `docs/evidence/mvp-a/a-04-authz-boundary-failure-log.md`.

Automated backend AuthZ boundary evidence improved: the CSRF/auth HTTP harness Data Protection key-path issue was fixed, `AuthSecurityHttpTests` passed 15/15, tenant isolation filtering passed 24/24, and the full backend suite passed 128/128. These tests cover anonymous rejection, CSRF enforcement, revoked/expired/disabled sessions, admin denial, tenant/project/conversation/file cross-scope denial, audit-log role checks, and security-event tenant scoping on synthetic test data.

P0-001 remains open. P0-002 remains open for fresh-runtime MVP-A acceptance because direct admin/non-admin/wrong-tenant smoke on a fresh app baseline still needs a supported seeded/bootstrap identity. Docker runtime and local PostgreSQL were also unavailable. This A-04 refresh is Needs verification and does not imply production approval, MVP-A Go, or production readiness.

## A-05 Sensitive Data Boundary Refresh Note

Refresh date: 2026-06-28

The A-05 sensitive data boundary evidence is recorded in `docs/evidence/mvp-a/a-05-sensitive-data-boundary-baseline.md` and `docs/evidence/mvp-a/a-05-sensitive-data-boundary-failure-log.md`.

One source-level error detail leakage risk was found and fixed: `GlobalExceptionHandlingMiddleware` no longer returns raw exception messages in Development, and the new regression test plus full backend suite passed 129/129. Repo keyword scans and source inspection found no confirmed committed raw secret in this pass, and raw `.env` values were not copied into evidence.

A-05 remains Needs verification. Local redacted Gitleaks reproduction is blocked because local `gitleaks` is not installed and Docker daemon access is unavailable. Ignored local `.env` value provenance remains Needs verification, live runtime logs were not captured, authenticated API/UI/export smoke remains blocked by P0-001/P0-002, and historical docs/evidence still need broader human review. This A-05 refresh does not imply production approval, MVP-A Go, or production readiness.

## A-07 File Boundary Refresh Note

Refresh date: 2026-06-29

The A-07 file boundary evidence is recorded in `docs/evidence/mvp-a/a-07-file-boundary-baseline.md` and `docs/evidence/mvp-a/a-07-file-boundary-failure-log.md`.

One metadata-only file boundary risk was found and fixed: file and artifact-version metadata responses no longer return internal storage identifiers. File, attachment, and artifact-version downloads now send private no-store headers, and denied file metadata/download attempts now write generic metadata-only audit entries. The targeted file/storage/tenant-boundary test slice passed 32/32, and the full backend suite passed 134/134.

No confirmed unauthorized file-body exposure was observed in the tested synthetic paths after the fix. A-07 remains Needs verification for fresh-runtime file smoke under signed-in test users, attachment/conversation sensitive-body coverage, removed-participant behavior, explicit grant/revoked-grant behavior, object-storage/signed-URL behavior, live PostgreSQL/container evidence, and broader authorized metadata classification. P0-001 and P0-002 remain open. This A-07 refresh does not imply production approval, MVP-A Go, or production readiness.

## A-08 Communication Boundary Refresh Note

Refresh date: 2026-06-29

The A-08 communication boundary evidence is recorded in `docs/evidence/mvp-a/a-08-communication-boundary-baseline.md` and `docs/evidence/mvp-a/a-08-communication-boundary-failure-log.md`.

Three communication boundary risks were found and fixed: removed message authors now must remain active conversation participants to edit/delete messages, read cursor updates now reject missing/deleted/cross-conversation message IDs, and message notification bodies no longer embed private message text. The focused HTTP tenant and communication-boundary test slice passed 11/11, and the full backend suite passed 138/138.

No confirmed unauthorized conversation/message body exposure was observed in the tested synthetic paths after the fixes. A-08 remains Needs verification for fresh-runtime authenticated communication smoke, same-tenant DM non-participant/admin policy, teacher/school-admin policy if implemented, thread coverage, realtime/polling coverage, live audit/log review, live PostgreSQL/container evidence, and safe-denial status-code classification. P0-001 and P0-002 remain open. This A-08 refresh does not imply production approval, MVP-A Go, or production readiness.

## A-09 Audit Security Refresh Note

Refresh date: 2026-06-29

The A-09 audit/security evidence is recorded in `docs/evidence/mvp-a/a-09-audit-security-baseline.md` and `docs/evidence/mvp-a/a-09-audit-security-failure-log.md`.

Three audit/security baseline risks were found and fixed: tenant-admin audit/security reads now include explicit `TenantId` predicates in the query service, covered conversation/message denial paths now write generic metadata-only audit entries, and login security metadata no longer stores the submitted email string. The focused auth/tenant/audit test slice passed 42/42, and the full backend suite passed 146/146.

No confirmed audit/security log private body, token, cookie, password, connection-string, file body, or message body exposure was observed in the tested synthetic paths after the fixes. A-09 remains Needs verification for live audit/security log review, full denial/admin/file/communication event matrix coverage, file grant/revoke behavior, destructive-operation attempt policy, metadata sanitizer completeness, `SecurityEvent.Email` policy, live PostgreSQL/container evidence, and fresh-runtime authenticated smoke. P0-001 and P0-002 remain open. This A-09 refresh does not imply production approval, MVP-A Go, or production readiness.
