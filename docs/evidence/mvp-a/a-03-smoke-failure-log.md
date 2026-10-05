# A-03 Smoke Failure Log

Date: 2026-06-28

Related evidence: `docs/evidence/mvp-a/a-03-application-smoke-baseline.md`

This failure log avoids copying secrets, connection strings, cookies, tokens, personal data, or private values.

## Failure: Readiness Endpoint Returned Unhealthy

Failure area: health route / database dependency

Command executed:

```powershell
GET http://127.0.0.1:18083/health/ready
```

Expected result: HTTP 200 when runtime dependencies are healthy.

Actual result: HTTP 503.

Sanitized response summary:

```json
{"status":"Unhealthy"}
```

Suspected cause: local PostgreSQL was not reachable on port 5432, so DB-backed readiness could not pass.

Required fix: start the intended non-production PostgreSQL instance or Docker Compose stack, apply migrations, and rerun readiness.

Whether this blocks MVP-A: yes for A-03 runtime smoke confidence on this host.

Status: Blocked

## Failure: Local PostgreSQL Port Unavailable

Failure area: database

Command executed:

```powershell
Test-NetConnection -ComputerName localhost -Port 5432
```

Expected result: successful TCP connection when local PostgreSQL is the verification database.

Actual result: TCP connect to `localhost:5432` failed.

Sanitized log summary:

```text
TcpTestSucceeded: False
```

Suspected cause: no local PostgreSQL service is listening on port 5432, or the intended database service is unavailable in this host context.

Required fix: start the intended verification database and rerun the smoke path.

Whether this blocks MVP-A: yes for this A-03 local runtime pass.

Status: Blocked

## Failure: Docker Daemon Unavailable

Failure area: Docker / external runtime dependency

Command executed:

```powershell
docker info
```

Expected result: Docker client can connect to the Docker Desktop Linux engine.

Actual result: Docker client metadata was available, but server connection failed.

Sanitized log summary:

```text
failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine
```

Suspected cause: Docker Desktop Linux engine is not running or the `desktop-linux` endpoint is unavailable.

Required fix: start Docker Desktop with the Linux engine available, verify `docker info`, then rerun Compose startup and container health checks.

Whether this blocks MVP-A: yes for Docker/container runtime evidence in A-03.

Status: Blocked

## Failure: Frontend Angular Build Dependency Missing

Failure area: frontend

Command executed:

```powershell
npm.cmd run build
```

Working directory: `coglatas-frontend`

Expected result: Angular build starts and completes or reports source/build errors.

Actual result: build did not start because Angular CLI was missing from local dependencies.

Sanitized log summary:

```text
Cannot find module '[repo]\coglatas-frontend\node_modules\@angular\cli\bin\ng.js'
```

Suspected cause: `coglatas-frontend/node_modules` exists but is incomplete or stale.

Required fix: restore frontend dependencies with the locked package manifest, then rerun `npm.cmd run build`.

Whether this blocks MVP-A: yes for A-03 frontend smoke evidence on this host.

Status: Blocked

## Failure: Storybook Build Dependency Missing

Failure area: frontend / Storybook

Command executed:

```powershell
npm.cmd run build-storybook
```

Working directory: `coglatas-frontend`

Expected result: Storybook static build starts and completes or reports source/build errors.

Actual result: Storybook build did not start because Angular CLI was missing from local dependencies.

Sanitized log summary:

```text
Cannot find module '[repo]\coglatas-frontend\node_modules\@angular\cli\bin\ng.js'
```

Suspected cause: `coglatas-frontend/node_modules` exists but is incomplete or stale.

Required fix: restore frontend dependencies with the locked package manifest, then rerun `npm.cmd run build-storybook`.

Whether this blocks MVP-A: yes for A-03 frontend smoke evidence on this host.

Status: Blocked

## Failure: Root Playwright UI Test Dependency Missing

Failure area: UI tests

Command executed:

```powershell
npm.cmd test -- --reporter=list
```

Expected result: Playwright test runner starts and runs root UI smoke tests.

Actual result: Playwright executable was not available.

Sanitized log summary:

```text
'playwright' is not recognized as an internal or external command
```

Suspected cause: root `node_modules` exists but the Playwright CLI dependency is missing or not installed.

Required fix: restore root UI test dependencies with the locked package manifest, then rerun `npm.cmd test`.

Whether this blocks MVP-A: yes for A-03 UI smoke evidence on this host.

Status: Blocked

## Needs Verification: Authenticated Runtime Smoke

Failure area: auth route / protected route / tenant boundary

Command executed: none for authenticated flows in this pass.

Expected result: admin, non-admin, tenant member, wrong-tenant, and protected resource flows verified with approved baseline identities.

Actual result: authenticated runtime checks were not run because the existing MVP-A P0 baseline identity/bootstrap blocker remains open.

Sanitized summary: anonymous protected APIs returned 401 and no private data, but deeper role and tenant runtime behavior still requires approved users/memberships.

Suspected cause: fresh MVP-A startup has no supported baseline user/admin bootstrap path.

Required fix: resolve the first-admin or verification identity bootstrap gap, then run a narrow authenticated authorization smoke suite.

Whether this blocks MVP-A: yes for authenticated A-03 smoke coverage.

Status: Needs verification, blocked by existing P0 bootstrap gap
