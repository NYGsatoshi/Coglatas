# Development Environment Modes

This repository supports three local development modes. Full Docker is optional.

## Mode A: Recommended lightweight contributor flow

Use Docker for PostgreSQL only. Run the ASP.NET Core backend and Angular dev server on the host.

Prerequisites:

- Docker Desktop or another Docker engine with Compose v2
- .NET 10 SDK
- Node.js 24

One-time setup:

```bash
dotnet restore Coglatas.slnx
dotnet tool restore
dotnet ef database update \
  --project src/Coglatas.Infrastructure \
  --startup-project src/Coglatas.Web
cd frontend
npm ci
cd ..
```

Day-to-day commands:

```bash
docker compose -f docker-compose.db.yml up -d
dotnet run --project src/Coglatas.Web
cd frontend && npm run start
```

Notes:

- `src/Coglatas.Web/appsettings.Development.json` points to `localhost:5433` with safe development-only credentials so this mode works without extra connection-string overrides.
- The backend listens on `http://localhost:5098` from `src/Coglatas.Web/Properties/launchSettings.json`.
- The Angular dev server listens on `http://localhost:4200` and proxies `/api`, `/health`, and `/healthz` to `http://localhost:5098`.
- If you prefer a different local PostgreSQL instance, override `ConnectionStrings__DefaultConnection` before running `dotnet ef` or `dotnet run`.

## Mode B: Optional full Docker development

This mode runs PostgreSQL, the backend, and the frontend in containers. It is optional and is not the default contributor path.

```bash
docker compose -f docker-compose.dev.yml up --build
```

Notes:

- This mode can be slower on Windows Docker Desktop because backend and frontend file watching use bind mounts plus polling.
- Use it when you want a more containerized local stack, not because the repository requires it.
- Stop it with `docker compose -f docker-compose.dev.yml down`.

## Mode C: CI parity for Playwright screenshots

Use the pinned Linux Playwright runner when you need to reproduce GitHub Actions screenshot rendering locally.

```bash
npm run test:ui:angular:docker
```

Notes:

- `Dockerfile.playwright` is pinned to the exact `@playwright/test` version resolved in the repository `package-lock.json`.
- This runner builds the Angular app and executes the strict screenshot regression suite in Linux.
- Windows and macOS local screenshots are diagnostic only and are not authoritative for baseline approval.
- A GitHub Actions screenshot failure remains Conditional Go until the regression passes again in the Linux Playwright environment.
- Do not skip or weaken screenshot baseline checks to make local host rendering differences disappear.

## Related files

- `docker-compose.db.yml`: recommended PostgreSQL-only Docker usage
- `docker-compose.dev.yml`: optional full-container development stack
- `docker-compose.playwright.yml`: optional Linux Playwright parity runner
- `Dockerfile.playwright`: pinned Playwright Linux image definition
