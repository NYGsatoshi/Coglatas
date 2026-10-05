# A-03 Application Smoke Baseline

Issue: A-03 - [MVP-A][P0][Smoke] Verify application smoke path and runtime dependency boundary

Date: 2026-06-28

Branch: `main`

Commit: `d20589a6d237e3c7c0fbbefb8040861bfd4b7506`

Result: Blocked

This smoke baseline does not imply production approval, MVP-A Go, production readiness, or acceptance of unrelated MVP-A blockers.

## A-03 Definition

No repo-owned A-03 definition was found before this evidence file was added. The working definition came from the attached issue text supplied for this task: verify the minimal application smoke path and runtime dependency boundary, record direct evidence, preserve auth and tenant boundaries, avoid copying secrets or private data into evidence, and mark unverified or externally blocked areas as Needs verification or Blocked.

## Scope

This pass verifies the current implementation repository behavior on the local Windows host. It is intentionally narrow:

- app process startup;
- root and SPA fallback routes;
- health endpoints and aliases;
- public auth status and CSRF endpoint shape;
- anonymous protected-route behavior;
- API 404 behavior;
- Docker, database, frontend, and UI dependency availability.

This pass does not create users, seed private tenant data outside existing startup behavior, bypass authorization, disable CSRF, expose private routes, or test production OAuth/provider integrations.

## Environment

| Item | Observed value |
| --- | --- |
| OS | Windows 10.0.26200, win-x64 |
| .NET SDK | 10.0.301 |
| .NET host/runtime | 10.0.9 |
| global.json | Present, pins SDK 10.0.301 |
| Docker client | 29.5.3 |
| Docker context | `desktop-linux` |
| Docker Compose | v5.1.4 |
| Node.js | v26.4.0 |
| npm | 11.17.0 through `npm.cmd` |
| App environment | Development |
| App startup URL | `http://127.0.0.1:18083` |

## Startup Method

Command executed:

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ASPNETCORE_URLS='http://127.0.0.1:18083'
dotnet run --project src\Coglatas.Web\Coglatas.Web.csproj --configuration Release --no-build --no-launch-profile
```

Observed sanitized startup log:

```text
Now listening on: http://127.0.0.1:18083
Application started. Press Ctrl+C to shut down.
Hosting environment: Development
Content root path: [repo]\src\Coglatas.Web
```

## Commands Executed

| Area | Command | Result |
| --- | --- | --- |
| Repo search | `rg -n "A-03|MVP-A|smoke|startup|runtime|endpoint|evidence|exit gate|blocker" .` | No pre-existing repo-owned A-03 definition identified; existing MVP-A evidence docs found. |
| Source inspection | `Program.cs`, launch settings, controllers, appsettings, Compose, A-01/A-02 docs | Existing app routes and auth attributes identified. |
| Environment | `dotnet --info` | Passed; SDK 10.0.301 and runtime 10.0.9 observed. |
| Restore | `dotnet restore Coglatas.slnx --disable-build-servers` | Passed; projects were up-to-date. |
| Build | `dotnet build Coglatas.slnx --configuration Release --no-restore --disable-build-servers -m:1` | Passed; 0 warnings, 0 errors. |
| Test | `dotnet test Coglatas.slnx --configuration Release --no-build --verbosity normal --disable-build-servers -m:1` | Passed; 128 total, 128 passed, 0 failed. |
| Compose config | `docker compose --env-file .env.example config --quiet` | Passed. Evidence does not copy `.env.example` values. |
| Docker daemon | `docker info` | Failed; Docker Desktop Linux engine endpoint unavailable. |
| Local DB port | `Test-NetConnection -ComputerName localhost -Port 5432` | Failed TCP connection to local PostgreSQL port 5432. |
| Frontend build | `npm.cmd run build` in `coglatas-frontend` | Blocked; local `node_modules` did not contain `@angular/cli/bin/ng.js`. |
| Storybook build | `npm.cmd run build-storybook` in `coglatas-frontend` | Blocked; local `node_modules` did not contain `@angular/cli/bin/ng.js`. |
| Root UI tests | `npm.cmd test -- --reporter=list` | Blocked; `playwright` executable was not available. |
| App startup | `dotnet run ... --no-launch-profile` | Passed; app listened on `http://127.0.0.1:18083`. |
| HTTP smoke | local anonymous GET/POST probes | Mixed; public shell and liveness worked, readiness and dependency-backed checks remained blocked. |

## Smoke Target List

| Target | Expected status | Actual status | Result | Sanitized response summary |
| --- | --- | --- | --- | --- |
| `GET /` | 200 | 200 | Pass | SPA HTML shell, title `Coglatas`; no private data observed. |
| `GET /login` | 200 | 200 | Pass | SPA HTML shell; no private data observed. |
| `GET /dashboard` anonymous | 200 shell or safe redirect | 200 | Partial | SPA HTML shell. Authenticated dashboard data was not returned. |
| `GET /health` | 302 to readiness | 302 | Pass | Redirected to `/health/ready`. |
| `GET /health/live` | 200 | 200 | Pass | `{"status":"OK"}`. |
| `GET /health/ready` | 200 when dependencies healthy | 503 | Blocked | `{"status":"Unhealthy"}` with no internal dependency details. |
| `GET /healthz` | documented health alias or safe behavior | 200 | Missing alias | SPA fallback HTML, not a dedicated health endpoint. |
| `GET /ready` | documented health alias or safe behavior | 200 | Missing alias | SPA fallback HTML, not a dedicated health endpoint. |
| `GET /live` | documented health alias or safe behavior | 200 | Missing alias | SPA fallback HTML, not a dedicated health endpoint. |
| `GET /swagger` | Swagger or safe fallback | 200 | Missing/Not configured | SPA fallback HTML, not Swagger UI. |
| `GET /swagger/index.html` | Swagger UI or 404 | 404 | Missing/Not configured | Empty 404 body. |
| `GET /openapi` | OpenAPI or safe fallback | 200 | Missing/Not configured | SPA fallback HTML, not an OpenAPI document. |
| `GET /api/auth/status` | 200 public status | 200 | Pass | `{"isAuthenticated":false}`. |
| `GET /api/security/csrf-token` | 200 token payload | 200 | Pass | JSON token payload returned; token value intentionally not copied into evidence. |
| `POST /api/auth/login` without CSRF | 403 | 403 | Pass | Generic CSRF error only. Dummy credentials only. |
| `POST /api/auth/logout` without CSRF | 403 or 401 | 403 | Pass | Generic CSRF error only. |
| `GET /api/auth/me` anonymous | 401 | 401 | Pass | No body returned. |
| `GET /api/admin/users` anonymous | 401 | 401 | Pass | No body returned. |
| `GET /api/projects` anonymous | 401 | 401 | Pass | No body returned. |
| `GET /api/ui/modules` anonymous | 401 | 401 | Pass | No body returned. |
| `GET /api/not-found-test` | 404 safe JSON | 404 | Pass | Generic `NotFound` message with trace ID; no stack trace observed. |
| `GET /not-found-test` | SPA fallback or 404 | 200 | Pass | SPA fallback HTML; no private data observed. |
| `GET /signin-google` | configured auth route or safe fallback | 200 | Needs verification | SPA fallback HTML; no Google OAuth handshake was configured or verified in this pass. |
| `GET /auth/not-found-test` | safe fallback or 404 | 200 | Pass | SPA fallback HTML; no private data observed. |
| `GET /api/auth/not-found-test` | 404 safe JSON | 404 | Pass | Generic `NotFound` message with trace ID; no stack trace observed. |

## Auth Boundary Checks

Anonymous protected APIs stayed closed in this pass:

- `GET /api/auth/me` returned 401.
- `GET /api/admin/users` returned 401.
- `GET /api/projects` returned 401.
- `GET /api/ui/modules` returned 401.

Unsafe anonymous POST requests without a CSRF token were rejected:

- `POST /api/auth/login` returned 403 with a generic CSRF message.
- `POST /api/auth/logout` returned 403 with a generic CSRF message.

No private tenant/project/file/message body was returned by these anonymous probes. This does not complete authenticated admin, non-admin, tenant-boundary, or file-download runtime verification because the repo's current P0 bootstrap blocker still prevents a fresh baseline login path.

## Runtime Dependency Boundary

| Dependency | Status | Evidence |
| --- | --- | --- |
| App process | Pass | App started and served `GET /`, `/login`, `/dashboard`, `/health/live`, and API auth/status probes. |
| Database | Blocked | Local PostgreSQL port 5432 did not accept TCP connections; `/health/ready` returned 503. |
| EF migrations against live DB | Needs verification | Backend tests passed, but this A-03 app run did not verify pending/applied migrations against a live local DB. |
| Docker / Compose runtime | Blocked | Compose config passed, but Docker daemon probing failed because the Docker Desktop Linux engine endpoint was unavailable. |
| File storage readiness | Needs verification | Readiness was unhealthy and did not expose per-check details; no independent file-storage endpoint check was performed. |
| Data Protection readiness | Needs verification | Readiness was unhealthy and did not expose per-check details. |
| External OAuth / Google | Needs verification | `/signin-google` returned SPA fallback HTML; no external OAuth handshake was configured or exercised. |
| Frontend Angular build | Blocked | `npm.cmd run build` could not find local Angular CLI in `coglatas-frontend/node_modules`. |
| Storybook build | Blocked | Same missing Angular CLI dependency state. |
| Root Playwright UI tests | Blocked | `playwright` executable was not available in root `node_modules`. |

## Observed Errors

Observed errors are recorded in `docs/evidence/mvp-a/a-03-smoke-failure-log.md`.

## Result

Status: Blocked

The application process started, the root UI shell responded, liveness worked, public auth status was safe, CSRF protection rejected unsafe anonymous POSTs, protected anonymous APIs returned 401, and API 404 behavior did not expose stack traces or secrets.

A-03 cannot be marked Accepted because readiness remains 503 on this host, local PostgreSQL and Docker runtime are unavailable, frontend/UI dependencies are incomplete locally, Swagger/OpenAPI routes are not configured as dedicated endpoints, and authenticated admin/non-admin/tenant runtime checks remain blocked by the existing baseline identity/bootstrap gap.

## Limitations

- No secrets, tokens, passwords, cookies, connection strings, tenant identifiers, or personal data were copied into this evidence.
- The CSRF token endpoint returned a token value, but the value is intentionally omitted.
- No browser console inspection was performed.
- No authenticated user flow was performed.
- No production or cloud target was verified.
- No Docker container was started.
- No external OAuth/provider dependency was exercised.

## Required Follow-Up

1. Start Docker Desktop Linux engine or provide a non-production PostgreSQL instance.
2. Apply migrations non-destructively against the verification database and rerun `/health/ready`.
3. Restore local root UI dependencies with the locked package manifest, then rerun root Playwright tests.
4. Restore `coglatas-frontend` dependencies with the locked package manifest, then rerun Angular and Storybook builds.
5. Decide whether `/healthz`, `/ready`, `/live`, `/swagger`, and `/openapi` should be implemented/documented as dedicated routes or remain safe fallbacks/missing routes.
6. Resolve the MVP-A baseline identity/bootstrap blocker before running authenticated admin/non-admin/tenant smoke checks.
