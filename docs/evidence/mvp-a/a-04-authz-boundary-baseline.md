# A-04 AuthZ Boundary Baseline

Issue: A-04 - [MVP-A][P0][AuthZ] Verify authentication, authorization, and private data boundary baseline

Date: 2026-06-28

Branch: `main`

Commit: `e013561b04b8a15c239e5d04663c5b71ffa7a0cd`

Result: Needs verification

This AuthZ baseline does not imply production approval, MVP-A Go, production readiness, or acceptance of unrelated MVP-A blockers.

## A-04 Definition

No repo-owned A-04 definition was found before this evidence file was added. The working definition came from the attached issue text supplied for this task: verify the minimum authentication, authorization, and private data boundary baseline; record evidence; keep unverified areas marked Needs verification or Blocked; and avoid copying secrets, tokens, cookies, session IDs, passwords, connection strings, personal data, or real user data into evidence.

## Environment

| Item | Observed value |
| --- | --- |
| OS | Windows 10.0.26200, win-x64 |
| .NET SDK | 10.0.301 |
| .NET host/runtime | 10.0.9 |
| global.json | Present, pins SDK 10.0.301 |
| Docker client | 29.5.3 |
| Docker Compose | v5.1.4 |
| Docker daemon | Not available on `npipe:////./pipe/docker_engine` in this pass |
| PostgreSQL local port 5432 | TCP connection failed |
| Test data | Synthetic in-memory test data only |

## Auth Implementation Summary

Runtime authentication is cookie-based in `src/Coglatas.Web/Program.cs`. The app wires `AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)`, cookie options, `DbSessionCookieAuthenticationEvents`, `UseAuthentication`, optional CSRF middleware, and `UseAuthorization`.

The A-04 HTTP auth security tests use the same cookie authentication scheme with a synthetic in-memory user. The test harness now persists Data Protection keys to an isolated temp directory so CSRF and auth cookies do not depend on the Windows user profile key folder.

API token creation and validation services exist, but API token request authentication middleware is not implemented and was not treated as verified for A-04.

## Authorization Policy Summary

Controller attributes provide coarse route protection. Resource authorization is primarily enforced in application services and infrastructure services:

| Area | Enforcement observed |
| --- | --- |
| Auth/session | Cookie session validation rejects revoked, expired, and disabled-user sessions. |
| Admin | Admin service rejects normal users for admin user access. |
| Tenant | Tenant switching requires active membership and enabled mode; suspended tenants cannot be resolved or switched into. |
| Workspace/group/channel | Workspace, group, private-channel, read-only, posting, and management checks are covered by service tests. |
| Project/task | Project membership, milestone/task creation, assignment, comment, and dependency checks are covered by service tests. |
| Conversation/message | HTTP tenant tests verify conversation detail/list boundaries for seeded participants and outsiders. |
| File/attachment | File metadata/download checks reject cross-tenant and outsider access; local storage tests reject traversal. |
| Audit/security events | `DbAuditQueryService` requires authenticated tenant owner/admin or system/platform admin and stays tenant-scoped for tenant admins. |

## Protected Resource Classification

| Resource type | Classification | Current evidence |
| --- | --- | --- |
| Current user profile/session | self-only / authenticated-only | `AuthSecurityHttpTests`, auth service tests |
| Tenant metadata/current tenant | tenant-scoped | `TenancyFoundationTests`, `HttpTenantIsolationTests` |
| Workspace/group/channel data | tenant-scoped plus membership/role-scoped | `TenantIsolationSecurityTests`, organization tests, HTTP tenant tests |
| Project/task/comment data | project-scoped | project service tests and HTTP tenant tests |
| Conversation/message body | participant-scoped | HTTP tenant tests verify seeded conversation access and outsider denial |
| File metadata/body/download | owner/project/member/grantee-scoped depending on context | file tests and HTTP tenant tests |
| Notification body | self-only plus tenant-scoped | notification tests and HTTP tenant tests |
| Admin management data | admin-only | admin service tests and auth security tests |
| Audit/security event data | tenant-admin/system-admin only | tenant isolation security tests |
| Public auth status and CSRF token route | public metadata/token issuance only | auth security tests; token values not copied |
| API token bearer auth | unknown / Needs verification | service foundation only; request middleware not implemented |

## Actor Matrix

| Actor | Evidence result |
| --- | --- |
| Anonymous user | Protected HTTP endpoints reject missing authentication before tenant data is returned. Unsafe cookie-auth POST without CSRF returns 403. |
| Authenticated normal user | Admin access denied; non-admin audit query denied; revoked/expired/disabled sessions rejected. |
| Tenant member | Can access allowed tenant/workspace/project/conversation/file data when explicitly seeded as member. |
| Non-tenant member / outsider | Tenant header alone does not grant workspace, project, conversation, or file download access. |
| Project member | Can view seeded allowed project/task data; non-member project access rejected. |
| Non-project member | Service and HTTP tests reject project/task access where membership is absent. |
| Conversation participant | Can view seeded allowed conversation detail/list. |
| Non-participant / outsider | HTTP tests reject seeded conversation access. |
| Removed participant | Needs verification; no explicit removed-participant test identified in this pass. |
| Admin / platform admin | Platform admin can query global audit/security data; tenant admin stays tenant-scoped. |
| Teacher / school admin | Needs verification; no distinct actor model identified in current implementation. |
| File owner / allowed member | Authorized file metadata/download path passes for seeded file. |
| File non-owner / non-grantee | Cross-tenant and outsider file metadata/download access rejected. |

## Endpoint Matrix

| Endpoint or test surface | Resource type | Required access | Actor tested | Expected result | Actual result | Result | Evidence command |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `/api/auth/login` without CSRF | auth | valid CSRF token | anonymous | 403 | 403 | Pass | `dotnet test ... --filter FullyQualifiedName~AuthSecurityHttpTests` |
| `/api/auth/login` with CSRF | auth | public plus valid credentials | synthetic user | 200 | 200 | Pass | `dotnet test ... --filter FullyQualifiedName~AuthSecurityHttpTests` |
| `/api/auth/me` after revoked/expired/disabled state | profile/session | active valid session | synthetic user | 401 | 401 | Pass | `dotnet test ... --filter FullyQualifiedName~AuthSecurityHttpTests` |
| `/api/workspaces` | workspace list | authenticated tenant request | anonymous | 401 | 401 | Pass | `MissingAuthenticationIsRejectedBeforeTenantDataIsReturned` |
| `/api/workspaces/{id}` | workspace detail | workspace membership | cross-tenant member / outsider | 200 for allowed, safe denial for disallowed | 200 for allowed, 400 for disallowed | Pass | `HttpTenantIsolationTests` |
| `/api/projects/{id}` | project detail | project visibility | cross-tenant member / outsider | 200 for allowed, safe denial for disallowed | 200 for allowed, 400 for disallowed | Pass | `HttpTenantIsolationTests` |
| `/api/conversations/{id}` | conversation detail/body | participant visibility | participant / outsider | 200 for allowed, safe denial for disallowed | 200 for allowed, 400 for disallowed | Pass | `HttpTenantIsolationTests` |
| `/api/files/{id}` | file metadata | allowed project/file access | allowed member / outsider | 200 for allowed, safe denial for disallowed | 200 for allowed, 400 for disallowed | Pass | `HttpTenantIsolationTests` |
| `/api/files/{id}/download` | file body | allowed project/file access | allowed member / outsider | 200 for allowed, safe denial for disallowed | 200 for allowed, 400 for disallowed | Pass | `HttpTenantIsolationTests` |
| audit query service | audit logs | tenant owner/admin or platform admin | tenant admin, owner, workspace admin, non-admin, platform admin | allowed only for authorized roles | matched expectation | Pass | `TenantIsolationSecurityTests` |
| security event query service | security events | tenant owner/admin or platform admin | tenant admin / platform admin | tenant-scoped or global according to role | matched expectation | Pass | `TenantIsolationSecurityTests` |

The HTTP tests currently assert some safe denials as `400 BadRequest`, reflecting existing controller/service result mapping. A-04 treats this as safe denial evidence only; it does not mean the HTTP error contract is ideal.

## Commands Executed

| Area | Command | Result |
| --- | --- | --- |
| Repo search | `rg -n "A-04|MVP-A|auth|authorization|permission|policy|role|tenant|private|access control|data boundary|evidence|blocker" docs src tests -S` | No pre-existing A-04 evidence file found; existing MVP-A verification docs found. |
| Environment | `dotnet --info` | Passed; SDK 10.0.301 and runtime 10.0.9 observed. |
| Docker version | `docker --version` | Passed with Docker config access warning; client 29.5.3 observed. |
| Docker Compose version | `docker compose version` | Passed; v5.1.4 observed. |
| Restore, sandbox | `dotnet restore Coglatas.slnx --disable-build-servers` | Failed because sandbox blocked NuGet access to `api.nuget.org:443`. |
| Restore, approved network | `dotnet restore Coglatas.slnx --disable-build-servers` | Passed. |
| Build | `dotnet build Coglatas.slnx --configuration Release --no-restore --disable-build-servers -m:1` | Passed; 0 warnings, 0 errors. |
| Initial full test | `dotnet test Coglatas.slnx --configuration Release --no-build --verbosity normal --disable-build-servers -m:1` | Failed 115 passed / 13 failed due test-harness Data Protection key write denial; see failure log. |
| Auth security focused test | `dotnet test tests\Coglatas.Tests\Coglatas.Tests.csproj --configuration Release --no-build --filter FullyQualifiedName~AuthSecurityHttpTests --logger "console;verbosity=normal"` | Passed; 15/15. |
| Tenant isolation focused test | `dotnet test tests\Coglatas.Tests\Coglatas.Tests.csproj --configuration Release --no-build --filter FullyQualifiedName~TenantIsolation --logger "console;verbosity=normal"` | Passed; 24/24. |
| Final full test | `dotnet test Coglatas.slnx --configuration Release --no-build --verbosity normal --disable-build-servers -m:1` | Passed; 128/128. |
| Compose config | `docker compose --env-file .env.example config --quiet` | Passed. |
| Docker daemon | `docker info` | Failed; Docker daemon endpoint was unavailable. |
| PostgreSQL local port | `Test-NetConnection -ComputerName localhost -Port 5432` | TCP connection failed. |

## Test Result Summary

| Test surface | Total | Passed | Failed | Skipped / caveat |
| --- | ---: | ---: | ---: | --- |
| Full backend suite, final run | 128 | 128 | 0 | `POSTGRES_TEST_CONNECTION_STRING` was not set; PostgreSQL tests in this repo return early when absent. |
| `AuthSecurityHttpTests` | 15 | 15 | 0 | Synthetic in-memory user and cookie auth. |
| `TenantIsolation` filtered tests | 24 | 24 | 0 | Synthetic in-memory tenants/users/resources; not a live PostgreSQL runtime test. |

## Boundary Results

| Boundary | Result |
| --- | --- |
| Anonymous access | Pass for tested protected APIs and unsafe CSRF rejection. |
| Insufficient permission | Pass for tested admin, non-admin audit, workspace, project, conversation, notification, and file denial paths. |
| Valid permission | Pass for tested login, tenant/member/project/conversation/file/audit allowed paths. |
| Tenant boundary | Pass in EF/service and Kestrel-backed in-memory HTTP tests. |
| Project boundary | Pass in service and HTTP tests for seeded project/task access. |
| Conversation boundary | Pass in HTTP tests for seeded participant vs outsider access; removed-participant case remains Needs verification. |
| File boundary | Pass in HTTP tests for metadata/download allowed and denied paths; object storage remains Needs verification. |
| Audit/security event boundary | Pass for tenant admin/owner/non-admin/workspace-admin/platform-admin cases. |
| Denial logging | Partial; security events and audit logging are implemented and tested in selected paths, but a full denial-log matrix was not verified. |

## Limitations

- A-04 is not Accepted because fresh-runtime authenticated smoke still depends on the existing MVP-A baseline identity/bootstrap gap.
- The final backend suite passed after fixing a test harness Data Protection key path, but this does not verify a live production-like PostgreSQL database.
- Docker container startup, app container health, and live PostgreSQL readiness were not verified because the Docker daemon and local PostgreSQL port were unavailable.
- No real users, production tenants, real private files, cookies, CSRF token values, passwords, connection strings, or secrets were copied into this evidence.
- HTTP denial status mapping still includes `400` for several safe denials; this is safe from a data-boundary standpoint but remains a contract-quality follow-up.
- API token request authentication, removed conversation participants, object storage, teacher/school-admin-specific roles, and production OAuth were not verified.

## Required Follow-Up

1. Resolve the MVP-A baseline identity/bootstrap blocker, then run live app admin/non-admin/tenant smoke checks without disabling auth or CSRF.
2. Provide a non-production PostgreSQL connection string or working Docker Desktop engine, then rerun database-backed readiness and PostgreSQL integration assertions.
3. Add explicit removed-participant and API-token request-auth tests if those are MVP-A requirements.
4. Decide whether safe-denial `400` responses should be normalized to 403/404 in the API error contract.
5. Expand denial-log verification into a full actor/resource matrix if A-04 acceptance requires runtime logging evidence for every denial.
