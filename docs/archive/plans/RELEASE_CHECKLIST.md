# Release Checklist

## Build And Tests

- [x] `dotnet restore Coglatas.slnx` passes for the release environment. Verified 2026-06-08 through build/test restore; NuGet vulnerability feed produced NU1900 warnings.
- [x] `dotnet build Coglatas.slnx` passes for the release commit. Verified 2026-06-08 with 0 errors and NU1900 warnings.
- [x] `dotnet test Coglatas.slnx` passes for the release commit. Verified 2026-06-08: 84 passed, 0 failed, 0 skipped.
- [x] Tenant isolation tests pass for the release commit. Covered by the full test run on 2026-06-08.
- [ ] Smoke test confirms app starts.
- [ ] Smoke test confirms database is reachable.
- [ ] Smoke test confirms `/health/live` and `/health/ready`.
- [ ] API smoke examples in `docs/API_SMOKE_TESTS.http` run against a seeded pilot environment.

## Security

- [ ] No secrets committed.
- [ ] Production configuration reviewed.
- [ ] Default passwords removed.
- [ ] `Security:CookieSecurePolicy=Always`.
- [ ] `Security:RequireHttps=true`.
- [ ] `Security:EnableHsts=true`.
- [ ] `Security:EnableCsrfProtection=true`.
- [ ] `DataProtection:KeysPath` points to persisted storage.
- [ ] HTTPS enabled at reverse proxy.
- [ ] Development tenant header disabled in production.
- [ ] `Platform:PlatformAdminSetupMode=false`.
- [ ] PlatformAdmin and TenantAdmin separation tested.
- [ ] Suspended tenant behavior tested.
- [ ] File download authorization tested.
- [ ] API tokens, invite tokens, and webhook secrets are never logged raw.
- [ ] Authenticated HTTP tenant isolation tests pass.
- [ ] PostgreSQL-backed search isolation tests pass, or the missing coverage is accepted as a documented pilot risk.

## Operations

- [ ] Database migrated.
- [ ] Backup taken before migration.
- [ ] Restore tested at least once in development.
- [ ] File storage backup included.
- [ ] Admin setup documented.
- [ ] First tenant creation documented.
- [ ] Audit logs working.
- [ ] Known risks listed.
- [ ] On-prem file storage path or SaaS object storage bucket is backed up.
- [ ] Production object storage adapter is implemented before any broad SaaS pilot.

## Product Smoke Tests

- [ ] Login works.
- [ ] Default tenant exists when required.
- [ ] Tenant isolation works.
- [ ] File upload works.
- [ ] File download works.
- [ ] Project creation works.
- [ ] Task creation works.
- [ ] Admin APIs are protected.
- [ ] Search is tenant-scoped.
- [ ] Notifications are tenant-scoped.
- [ ] Direct message unread/read state works.
- [ ] Announcement read confirmation works.
- [ ] Gantt, dashboard, and my-tasks views work.

## Go/No-Go

- [ ] Local demo ready.
- [ ] Internal pilot ready.
- [ ] School pilot conditionally ready.
- [ ] SaaS pilot ready.

Pilot release is allowed only when all critical security and backup items above are complete. If any item remains open, mark the release conditionally safe or unsafe in `docs/archive/status/PILOT_STATUS.md` and identify who accepted the risk.
