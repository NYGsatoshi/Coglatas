# A-02 Health Check Failure Log

Date: 2026-06-28

Related evidence: `docs/evidence/mvp-a/a-02-health-check-baseline.md`

This failure log avoids copying secrets, connection strings, cookies, tokens, personal data, or private values.

## Failure: Readiness Endpoint Returned Unhealthy

Failure scenario: local app started successfully, but readiness was not healthy.

Command executed:

```powershell
curl.exe -i -s http://localhost:5098/health/ready
```

HTTP status: `503 Service Unavailable`

Sanitized response body:

```json
{"status":"Unhealthy"}
```

Sanitized error summary: readiness failed without exposing internal dependency details.

Suspected cause: local PostgreSQL was not reachable on port 5432, so database connectivity, migration state, and default tenant readiness could not pass.

Required follow-up: start a non-production PostgreSQL instance or Docker Compose stack, apply migrations, and rerun `/health/ready`.

Whether this blocks MVP-A: yes for A-02 deployment-gate confidence. It does not prove production failure; it proves this local pass did not collect accepted readiness evidence.

Status: Blocked

## Failure: Local PostgreSQL Port Unavailable

Failure scenario: local DB dependency check could not connect to PostgreSQL on the default port.

Command executed:

```powershell
Test-NetConnection -ComputerName localhost -Port 5432
```

Sanitized error summary:

```text
TCP connect to localhost:5432 failed.
TcpTestSucceeded: False
```

Suspected cause: no local PostgreSQL service is listening on port 5432, or it is blocked/unavailable in this host context.

Required follow-up: start the intended verification PostgreSQL instance and rerun readiness.

Whether this blocks MVP-A: yes for this A-02 local readiness evidence.

Status: Blocked

## Failure: Docker Daemon Unavailable

Failure scenario: Docker Compose service status could not be inspected, and the PostgreSQL/app containers could not be started or checked in this pass.

Commands executed:

```powershell
docker info
docker compose --env-file .env.example ps
```

Sanitized error summary:

```text
failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine
```

Suspected cause: Docker Desktop Linux engine is not running or the `desktop-linux` endpoint is unavailable.

Required follow-up: start Docker Desktop with the Linux engine available, verify `docker info`, then run Compose startup and capture app/PostgreSQL health.

Whether this blocks MVP-A: yes for Docker/container health evidence in A-02.

Status: Blocked

## Failure: Backend Test Suite Failed During A-02 Pass

Failure scenario: backend Release build passed, but the full backend test suite did not pass in this current run.

Command executed:

```powershell
dotnet test Coglatas.slnx --configuration Release --no-build --verbosity normal --disable-build-servers -m:1
```

Sanitized error summary:

```text
Test total: 128
Passed: 115
Failed: 13
Failure cluster: AuthSecurityHttpTests received 500 while obtaining CSRF/login setup.
```

Suspected cause: not determined in this A-02 health pass. The failures are in auth/CSRF HTTP tests and were not treated as health endpoint implementation changes.

Required follow-up: triage the `AuthSecurityHttpTests` 500 responses separately before using this test suite as clean deployment-gate evidence.

Whether this blocks MVP-A: yes for clean full-suite evidence in this pass. It does not change the direct `/health/live` and `/health/ready` HTTP observations.

Status: Blocked
