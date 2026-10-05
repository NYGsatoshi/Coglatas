# A-04 AuthZ Boundary Failure Log

Issue: A-04 - [MVP-A][P0][AuthZ] Verify authentication, authorization, and private data boundary baseline

Date: 2026-06-28

Result: Needs verification

This failure log does not imply production approval, MVP-A Go, production readiness, or acceptance of unrelated MVP-A blockers.

## Summary

No private data leak was observed in the final A-04 backend verification pass. The final backend suite passed 128/128, including focused auth security and tenant isolation slices.

The remaining A-04 limitations are verification blockers, not observed leaks: fresh-runtime authenticated smoke remains blocked by the baseline identity/bootstrap gap, Docker runtime is unavailable, and local PostgreSQL port 5432 is unavailable. A test-harness failure was found and fixed during this pass.

## Resolved During A-04

### Failure Area

auth security test harness / CSRF token issuance

Endpoint or code area: `AuthSecurityHttpTests`, `/api/security/csrf-token`, ASP.NET Data Protection

Actor: synthetic test user / anonymous CSRF-token request in test harness

Expected result: CSRF token endpoint returns 200 in the test harness so cookie-auth CSRF behavior can be verified.

Actual result before fix: 13 `AuthSecurityHttpTests` failed because the CSRF token request returned 500.

Sanitized response summary: the response body showed an ASP.NET Data Protection encryption failure caused by denied write access to the Windows user-profile Data Protection key folder. No token value was copied.

Sanitized log summary: `Access to the path 'C:\Users\satos\AppData\Local\ASP.NET\DataProtection-Keys\...\*.tmp' is denied.`

Suspected cause: the custom Kestrel test harness used the default Data Protection key repository, which writes outside the workspace and can be blocked in this environment.

Required fix: persist test-harness Data Protection keys to an isolated temp directory.

Whether this blocks MVP-A: no after the fix; it blocked local A-04 test evidence before the fix.

Status: Resolved

Evidence: `dotnet test ... --filter FullyQualifiedName~AuthSecurityHttpTests` passed 15/15 after the fix, and final `dotnet test Coglatas.slnx ...` passed 128/128.

## Remaining Verification Blockers

### Fresh Runtime Authenticated Smoke

Failure area: authenticated runtime smoke

Endpoint or code area: admin/non-admin/tenant runtime smoke on a fresh app baseline

Actor: seeded/admin identity and non-admin tenant member

Expected result: direct runtime evidence for anonymous 401, non-admin 403, admin allowed response, wrong-tenant denial, and unauthorized file denial.

Actual result: not executed in this pass.

Sanitized response summary: no response body captured.

Sanitized log summary: no runtime log captured.

Suspected cause: existing P0-001 baseline identity/bootstrap gap; fresh startup does not provide a supported baseline login identity for full runtime smoke.

Required fix: resolve P0-001 with the smallest approved local/dev/test bootstrap path, then rerun A-04 runtime smoke.

Whether this blocks MVP-A: yes for fresh-runtime A-04 acceptance.

Status: Needs verification

### Docker Runtime

Failure area: Docker/container runtime

Endpoint or code area: Docker daemon, container startup, container health

Actor: local verifier

Expected result: Docker daemon reachable; containers can be started and inspected.

Actual result: `docker info` failed because the Docker daemon endpoint was unavailable.

Sanitized response summary: Docker client metadata was shown; no container runtime was available.

Sanitized log summary: daemon endpoint `npipe:////./pipe/docker_engine` was unavailable; Docker config access also produced a local permission warning.

Suspected cause: Docker Desktop engine not running or inaccessible in the current Windows host/sandbox context.

Required fix: start/repair Docker Desktop engine or provide another non-production container runtime.

Whether this blocks MVP-A: yes for Docker/container runtime evidence, not for the automated backend auth boundary tests.

Status: Blocked

### PostgreSQL Runtime

Failure area: live PostgreSQL runtime

Endpoint or code area: local PostgreSQL port 5432 and PostgreSQL-backed assertions

Actor: local verifier

Expected result: non-production PostgreSQL reachable for database-backed readiness and integration assertions.

Actual result: TCP connection to localhost port 5432 failed.

Sanitized response summary: no database response body.

Sanitized log summary: `TcpTestSucceeded: False`.

Suspected cause: local PostgreSQL not running or not exposed on port 5432.

Required fix: start a non-production PostgreSQL instance or provide a sanitized test connection string.

Whether this blocks MVP-A: yes for live PostgreSQL A-04 runtime confidence, not for in-memory tenant/auth service test evidence.

Status: Blocked

## No Observed P0 Leak In Final Automated Tests

The final automated test pass did not show these P0 leak examples:

- anonymous user reading protected tenant/project/conversation/file body;
- non-participant reading seeded conversation body;
- non-project member reading seeded project private data;
- file non-grantee downloading seeded file body;
- non-admin reading admin management data;
- response body exposing secret, token value, cookie, session ID, password, connection string, or stack trace in successful final evidence.

This statement is limited to the tested synthetic paths and does not mark A-04 Accepted.
