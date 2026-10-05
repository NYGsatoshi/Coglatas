# A-02 Health Check Baseline

Issue: A-02 - [MVP-A][P0][Health] Verify health check baseline

Date: 2026-06-28

Branch: `main`

Commit: `be65c83fa35019c637228b7149cb0e56d82a1ac8`

Result: Blocked

This health check result does not imply production approval.

## Scope

This pass verifies the current implementation repository behavior on the local Windows host. It does not approve production deployment, MVP-A Go, or unrelated blockers.

## Entrypoint And Health Implementation

| Item | Observed value |
| --- | --- |
| ASP.NET Core entrypoint | `src/Coglatas.Web/Program.cs` |
| Health implementation | Custom minimal endpoints in `Program.cs`; `MapHealthChecks`/`AddHealthChecks` are not used |
| Canonical readiness endpoint | `/health/ready` |
| Liveness endpoint | `/health/live` |
| Redirect endpoint | `/health` redirects to `/health/ready` |
| Alias endpoints checked | `/healthz`, `/ready`, `/live` are not dedicated health endpoints and return SPA fallback HTML in this run |
| Auth requirement | No endpoint-level authorization requirement was found for `/health`, `/health/live`, or `/health/ready` |
| HTTPS behavior | HTTPS redirection excludes paths starting with `/health` when enabled |

Readiness checks database connectivity, pending EF Core migrations, local file storage writability, Data Protection key path writability, and default tenant presence in single-tenant mode. The public unhealthy response is intentionally minimal and does not expose connection strings, hostnames, database names, exception details, or internal topology.

## Environment

| Item | Observed value |
| --- | --- |
| OS | Windows NT 10.0.26200.0, win-x64 |
| .NET SDK | 10.0.301 |
| .NET host/runtime | 10.0.9 |
| global.json | Present, pins SDK 10.0.301 |
| Docker client | 29.5.3 |
| Docker context | `desktop-linux` |
| Docker Compose | v5.1.4 |
| Local app URL used | `http://localhost:5098` |
| Environment | Development, selected by launch profile |

## Startup Command

Command executed:

```powershell
$env:ASPNETCORE_URLS='http://127.0.0.1:18080'; $env:ASPNETCORE_ENVIRONMENT='Development'; dotnet run --project src\Coglatas.Web\Coglatas.Web.csproj --configuration Release --no-build
```

Observed startup behavior: the app used `src/Coglatas.Web/Properties/launchSettings.json` and listened on `http://localhost:5098` instead of the requested `ASPNETCORE_URLS` value.

Relevant sanitized startup log:

```text
Using launch settings from src/Coglatas.Web/Properties/launchSettings.json
warn: Microsoft.AspNetCore.DataProtection.KeyManagement.XmlKeyManager
      No XML encryptor configured. A key may be persisted to storage in unencrypted form.
info: Microsoft.Hosting.Lifetime
      Now listening on: http://localhost:5098
info: Microsoft.Hosting.Lifetime
      Application started. Press Ctrl+C to shut down.
info: Microsoft.Hosting.Lifetime
      Hosting environment: Development
info: Microsoft.Hosting.Lifetime
      Content root path: [repo]\src\Coglatas.Web
```

## Commands Executed

| Area | Command | Result |
| --- | --- | --- |
| Repo status | `git status --short` | No pre-existing modified files were reported before this A-02 work. |
| Repo inventory | `rg --files` | Found `src/Coglatas.Web/Program.cs`, Docker/Compose files, appsettings, and existing `docs/evidence/mvp-a` directory. |
| Health source search | `rg -n "MapHealthChecks|AddHealthChecks|health|ready|live|healthz" ...` | Found existing custom endpoints in `Program.cs`. |
| Environment | `dotnet --info` | Passed; SDK 10.0.301 and runtime 10.0.9 observed. |
| Environment | `docker --version` | Passed with Docker client 29.5.3; Docker config access warning observed. |
| Environment | `docker compose version` | Passed with Docker Compose v5.1.4. |
| Docker daemon | `docker info` | Failed; Docker Desktop Linux engine endpoint unavailable. |
| Build | `dotnet build Coglatas.slnx --configuration Release --disable-build-servers -m:1` | First sandboxed run failed on blocked NuGet network access; rerun with network/process permission passed with 0 warnings and 0 errors. |
| Test | `dotnet test Coglatas.slnx --configuration Release --no-build --verbosity normal --disable-build-servers -m:1` | Failed; 115 passed, 13 failed. Failures were all `AuthSecurityHttpTests` paths receiving 500 while obtaining CSRF/login setup. |
| Compose config | `docker compose --env-file .env.example config --quiet` | Passed. Evidence does not copy `.env.example` values. |
| Compose status | `docker compose --env-file .env.example ps` | Failed because Docker daemon endpoint was unavailable. |
| Local DB port | `Test-NetConnection -ComputerName localhost -Port 5432` | Failed TCP connection to local PostgreSQL port 5432. |
| Startup | `dotnet run --project src\Coglatas.Web\Coglatas.Web.csproj --configuration Release --no-build` with local env vars | App started in Development on `http://localhost:5098`. |
| Health | `curl.exe -i -s http://localhost:5098/health` | Returned 302 redirect to `/health/ready`. |
| Health | `curl.exe -i -s http://localhost:5098/health/live` | Returned 200 with sanitized body `{"status":"OK"}`. |
| Health | `curl.exe -i -s http://localhost:5098/health/ready` | Returned 503 with sanitized body `{"status":"Unhealthy"}`. |
| Alias check | `curl.exe -i -s http://localhost:5098/healthz`; `/ready`; `/live` | Returned SPA fallback HTML, not health endpoint responses. |

## HTTP Evidence

### `GET /health`

HTTP status: `302 Found`

Relevant sanitized headers/body:

```text
Location: /health/ready
Content-Length: 0
```

### `GET /health/live`

HTTP status: `200 OK`

Sanitized response body:

```json
{"status":"OK"}
```

### `GET /health/ready`

HTTP status: `503 Service Unavailable`

Sanitized response body:

```json
{"status":"Unhealthy"}
```

The readiness response did not expose DB host, DB name, connection string, exception text, tenant identifiers, or internal dependency details.

## Dependency Status

| Dependency | Status | Evidence |
| --- | --- | --- |
| Application process | Verified reachable | Startup log and `/health/live` 200. |
| Database | Blocked / unavailable locally | `Test-NetConnection localhost:5432` failed; `/health/ready` returned 503. |
| EF migrations | Needs verification in this run | Readiness was unhealthy because DB connectivity was unavailable; pending migration state could not be verified against a live DB. |
| PostgreSQL container | Blocked | Docker daemon unavailable; `docker compose ps` could not inspect containers. |
| File storage | Needs verification in this run | Readiness does not expose per-check details when unhealthy; local storage was not independently proven by endpoint response. |
| Data Protection path | Needs verification in this run | Startup emitted a warning about unencrypted local key persistence, but path writability was not independently proven because readiness was unhealthy. |
| Default tenant | Needs verification in this run | Default tenant check depends on DB availability for single-tenant readiness. |
| Auth dependency | Verified not required for health endpoints | Source inspection found no endpoint-level authorization requirement for `/health`, `/health/live`, or `/health/ready`. |
| External OAuth / outbound services | Needs verification for broader deployment; not part of current health response | No OAuth readiness dependency was found. Webhook/API-token features exist, but health readiness did not call outbound OAuth/webhook services in this pass. |
| Object storage | Needs verification for object-storage deployments | Source/docs indicate object-storage provider names return unhealthy until a real adapter exists; this local run used the default local file storage configuration and did not verify object storage. |

## Result

Status: Blocked

Reason: the app process and liveness endpoint were reachable, but the deployment-gate readiness endpoint returned 503 because required DB-backed readiness could not be satisfied on this host. Docker/PostgreSQL container health could not be checked because the Docker Desktop Linux engine endpoint was unavailable.

## Limitations

- This pass did not verify a healthy `/health/ready` result.
- This pass did not start PostgreSQL locally or through Docker.
- This pass did not apply EF Core migrations against a live DB.
- This pass did not prove file storage, Data Protection, or default tenant readiness independently because `/health/ready` returned the intentionally minimal unhealthy body.
- This pass did not validate a production or cloud deployment target.
- This pass does not imply production approval, MVP-A Go, or readiness for real users.

## Required Follow-Up

1. Start Docker Desktop Linux engine or provide a non-production local PostgreSQL instance.
2. Apply migrations non-destructively against the verification database.
3. Start the app without launch-profile surprises when environment overrides matter, or record the launch profile explicitly.
4. Re-run `GET /health/ready` and record HTTP status, sanitized body, startup log, DB status, and container status.
5. Re-run backend tests or triage the current `AuthSecurityHttpTests` 500 failures separately.
