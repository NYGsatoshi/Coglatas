# Coglatas

> [!IMPORTANT]
> **Public source, not open source.** This repository is made visible for
> transparency, technical evaluation, portfolio review, and CI. No permission
> is granted to use, copy, modify, redistribute, sublicense, commercially
> exploit, or create derivative works from repository-owned material except as
> required by GitHub's Terms of Service or applicable law. See
> [COPYRIGHT.md](COPYRIGHT.md) and [CONTRIBUTING.md](CONTRIBUTING.md).

### Specification status

The authoritative specification is maintained separately. Its visibility and
source terms are independent from this repository; an inaccessible
specification link must not be treated as permission to use this code.


Coglatas is a tenant-aware school and organization portal implemented as a .NET 10 ASP.NET Core modular monolith.

Repository audit status as of 2026-06-19: the backend contains a broad set of REST APIs, EF Core entities, PostgreSQL migrations, authorization services, and automated tests. A focused backend logic audit also identified critical defects in scoped announcement visibility, search authorization, conversation persistence, and message attachment handling. The MVP-A P0 user-facing frontend target is Angular under `frontend/`; hosted build artifacts are copied into `src/Coglatas.Web/wwwroot`. The repository is suitable for development and controlled technical evaluation, but it is not a turnkey pilot or production deployment.

## Status language

Project documentation uses these labels:

- **Implemented**: wired into the running application and supported by direct code evidence.
- **Partially implemented**: meaningful code exists, but an end-to-end workflow, UI, integration, or enforcement layer is incomplete.
- **Planned**: described as future work and not implemented.
- **Deprecated**: retained only for compatibility or historical reference.
- **Needs verification**: source evidence is insufficient, environment-dependent, or not exercised in this audit.
- **Inferred**: behavior is concluded from code structure rather than a completed runtime check.

## Current implementation summary

| Area | Status | Evidence and limits |
| --- | --- | --- |
| ASP.NET Core host and modular projects | Implemented | `src/Coglatas.Web/Program.cs`, `Coglatas.slnx` |
| PostgreSQL and EF Core migrations | Implemented | `src/Coglatas.Infrastructure/Persistence/AppDbContext.cs`, `src/Coglatas.Infrastructure/Persistence/Migrations/` |
| Cookie login, logout, password change, CSRF, session validation, lockout | Implemented | `src/Coglatas.Application/Auth/`, `src/Coglatas.Web/Security/`, `src/Coglatas.Web/Middleware/CsrfProtectionMiddleware.cs` |
| Initial administrator bootstrap | Implemented | `COGLATAS_SEED_ADMIN_ENABLED=true` can seed or update the first administrator through the normal password hasher; `LocalAdmin__SeedOnStartup` remains a development-only compatibility path |
| Invite registration | Partially implemented | Creates a user and session, but does not create tenant or workspace membership |
| Tenant filters and tenant-aware save rules | Implemented | `src/Coglatas.Infrastructure/Persistence/AppDbContext.cs` |
| Workspaces, groups, channels, messaging, projects, forms, events, files, notifications | Backend implemented; UI varies | Controllers and services exist; several browser routes are placeholders |
| Local filesystem storage | Implemented | `src/Coglatas.Infrastructure/Files/LocalFileStorageService.cs` |
| Object storage | Planned | Config names exist, but `UnsupportedObjectStorageService` is used |
| Tenant metadata export | Partially implemented | ZIP metadata export exists; file bodies and restore do not |
| Webhooks and API tokens | Foundation only | Management/validation code exists; no outbound delivery or request authentication middleware |
| Docker Compose | Partially implemented | Root, local, and on-prem profiles use a controlled SDK migration service; the on-prem profile still requires an external TLS/reverse-proxy topology and fresh-stack runtime evidence |

See [AI context](docs/AI_CONTEXT.md) for the full status matrix and [known issues](docs/KNOWN_ISSUES.md) before planning work.
See [backend logic audit](docs/BACKEND_LOGIC_AUDIT.md) for controller, service, validation, error-handling, file, project, messaging, announcement, DI, and HTTP-status findings.

## Repository layout

```text
src/
  Coglatas.Domain/          Entities, enums, common domain types
  Coglatas.Application/     Use cases, DTOs, authorization, service contracts
  Coglatas.Infrastructure/  EF Core, PostgreSQL, repositories, files, audit, search
  Coglatas.Web/             Startup, middleware, controllers, hosted Angular artifacts
frontend/                    Angular frontend source
tests/
  Coglatas.Tests/           Unit, service, HTTP, tenancy, and PostgreSQL-conditional tests
  ui/                        Playwright infrastructure; legacy static-SPA specs are obsolete
docs/                        Active documentation
docs/archive/                Historical plans, reports, specifications, and status snapshots
```

## Development start

Requirements:

- .NET 10 SDK
- Node.js 24 for the Angular dev server and UI test workflow
- Docker and Docker Compose for the recommended PostgreSQL-only mode, optional full-container development, and Linux Playwright screenshot parity

Recommended first-time local setup:

```bash
dotnet restore Coglatas.slnx
dotnet tool restore
docker compose -f docker-compose.db.yml up -d
dotnet ef database update \
  --project src/Coglatas.Infrastructure \
  --startup-project src/Coglatas.Web
cd frontend && npm ci
```

Recommended day-to-day local development:

```bash
dotnet run --project src/Coglatas.Web
cd frontend && npm run start
```

`src/Coglatas.Web/appsettings.Development.json` now points to the Docker PostgreSQL-only profile on `localhost:5433` with safe development-only credentials. Override `ConnectionStrings__DefaultConnection` if you want to use a different local PostgreSQL instance.

Full application Docker is optional. Use it only when you specifically want the whole stack containerized:

```bash
cp .env.example .env
docker compose -f docker-compose.dev.yml up --build
```

For a fresh Docker/VPS environment, set these values in `.env` or your secret manager before first startup. Keep `COGLATAS_SEED_ADMIN_ENABLED=false` after the initial bootstrap unless you intentionally want the startup seed to reconcile that administrator again.

```bash
COGLATAS_SEED_ADMIN_ENABLED=true
COGLATAS_SEED_ADMIN_EMAIL=admin@example.local
COGLATAS_SEED_ADMIN_USERNAME=admin
COGLATAS_SEED_ADMIN_PASSWORD=<strong-password>
```

Use `npm run test:ui:angular:docker` when you need Linux Playwright screenshot parity with GitHub Actions. Windows and macOS host-native screenshots are not authoritative for baseline approval.

See [README.dev-env.md](README.dev-env.md) for the three supported contributor modes and exact commands.

## GCP Compute Engine development deployment

This repository includes a Docker Compose deployment path for a single Google Compute Engine VM with PostgreSQL running inside Compose.

Start from Windows PowerShell in VSCode:

```powershell
gcloud init
gcloud auth login
gcloud config set project YOUR_GCP_PROJECT_ID
.\deploy\gcp\create-vm.ps1 -ProjectId YOUR_GCP_PROJECT_ID -Zone us-central1-a -VmName coglatas-dev
gcloud compute ssh coglatas-dev --zone us-central1-a
```

On the VM:

```bash
bash ~/coglatas-gcp/gcp/bootstrap-vm.sh
exit
```

Reconnect from Windows PowerShell so Docker group membership is active, then deploy:

```powershell
gcloud compute ssh coglatas-dev --zone us-central1-a
```

On the VM:

```bash
bash ~/coglatas-gcp/gcp/deploy-app.sh
```

Then open `http://EXTERNAL_IP:8080`. For updates that keep the database volume, run:

```bash
bash ~/coglatas-gcp/gcp/update-app.sh
```

See [deploy/gcp/README.md](deploy/gcp/README.md) for VM creation, Docker setup, environment variables, logs, reset commands, and common error fixes. This is a development deployment; production should add HTTPS/reverse proxying, backups, monitoring, and hardened secret management.

## Verification snapshot

On 2026-06-18:

- `dotnet test Coglatas.slnx --configuration Release --no-restore --disable-build-servers -m:1` passed 123 tests with one compile warning.
- `POSTGRES_TEST_CONNECTION_STRING` was unset, so the two PostgreSQL tests returned without executing their database assertions even though the test runner counted them as passed.
- All three Compose files passed `docker compose ... config --quiet` when a validation-only `POSTGRES_PASSWORD` was supplied where required.
- Playwright tests were not run because locked frontend dependencies were not installed in this workspace.

See [testing](docs/TESTING.md) for exact interpretation.

## Documentation

- [AI context](docs/AI_CONTEXT.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Development](docs/DEVELOPMENT.md)
- [Deployment](docs/DEPLOYMENT.md)
- [Security model](docs/SECURITY_MODEL.md)
- [Database](docs/DATABASE.md)
- [Testing](docs/TESTING.md)
- [Known issues](docs/KNOWN_ISSUES.md)
- [Backend logic audit](docs/BACKEND_LOGIC_AUDIT.md)
- [Coding rules](docs/CODING_RULES.md)
- [API conventions](docs/API_CONTRACTS.md)
- [Operations](docs/OPERATIONS.md)
- [Reproducible synthetic demo dataset](docs/demo-dataset.md)
- [Roadmap](docs/ROADMAP.md)
- [Archive index](docs/archive/README.md)

Archived documents are historical evidence, not current project truth.
