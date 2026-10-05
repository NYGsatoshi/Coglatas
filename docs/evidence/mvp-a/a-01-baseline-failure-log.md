# A-01 Baseline Failure Log

Date: 2026-06-28

Related evidence: `docs/evidence/mvp-a/a-01-build-test-baseline.md`

This failure log avoids copying secrets, connection strings, cookies, tokens, or private values.

## Failure: Docker Daemon Unavailable

Failure area: docker

Commands executed:

- `docker info`
- `docker compose --env-file .env.example build app`
- `docker compose --env-file .env.example up -d --wait`

Sanitized error summary:

```text
failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine
```

Sensitive data check: no secrets were copied. The command used `.env.example`, but this log does not reproduce its values.

Suspected cause: Docker Desktop Linux engine is not running or the `desktop-linux` context endpoint is unavailable on the Windows host.

Required fix: start Docker Desktop with the Linux engine available, verify `docker info`, then rerun Compose build and startup.

Blocks MVP-A: yes for Docker/container evidence. It does not block the .NET restore/build/test baseline.

Owner suggestion: local environment owner.

Status: Blocked

## Failure: UI Test Dependencies Missing

Failure area: test

Commands executed:

- `npm --version`
- `npm test -- --reporter=list`
- `npm.cmd --version`
- `npm.cmd test -- --reporter=list`

Sanitized error summary:

```text
npm.ps1 blocked by PowerShell execution policy
playwright is not recognized as an internal or external command
```

Sensitive data check: no secrets were copied.

Suspected cause: PowerShell blocks `npm.ps1`; after using the Windows cmd shim, root UI test dependencies are not installed in `node_modules`.

Required fix: use `npm.cmd` on this host or adjust execution policy intentionally, run `npm.cmd ci`, then rerun `npm.cmd test`.

Blocks MVP-A: yes for local UI test evidence only. It does not block the backend .NET test baseline.

Owner suggestion: local environment owner.

Status: Blocked

## Failure: Live PostgreSQL Assertions Not Executed

Failure area: database

Commands executed:

- `dotnet test Coglatas.slnx --configuration Release --no-build --verbosity normal --disable-build-servers -m:1`
- `dotnet ef migrations list --project src\Coglatas.Infrastructure --startup-project src\Coglatas.Web --no-connect`

Sanitized error summary: no command failed. However, `POSTGRES_TEST_CONNECTION_STRING` was not set, and source inspection shows the two PostgreSQL integration tests return early when that variable is absent.

Sensitive data check: no connection string was printed or copied.

Suspected cause: no live local/dev PostgreSQL connection was provided, and Docker was unavailable for starting the local PostgreSQL service.

Required fix: provide a sanitized local/dev PostgreSQL connection string through `POSTGRES_TEST_CONNECTION_STRING`, apply migrations non-destructively, and rerun the PostgreSQL integration category.

Blocks MVP-A: yes for live database evidence. It does not block local EF migration inventory or the in-memory/service-backed test baseline.

Owner suggestion: local environment owner.

Status: Needs verification
