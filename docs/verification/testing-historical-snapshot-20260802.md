# Retained testing verification snapshot (2026-08-02)

The dated observations below are preserved exactly as previously recorded in
[the testing guide](../TESTING.md) at documentation source `cb6873b3f887806da3ab33ef2cc4535b3a3173da`.
They are historical environment and verification evidence, not current
Main/CI acceptance, a substitute for a PostgreSQL run, or a new approval.
The original guide retains all three heading anchors as links to this record.

## Historical verification snapshot (2026-08-02)

The prior full backend and frontend audit used a disposable migrated PostgreSQL
18 database and a local Windows UI diagnostic. It is retained only as
historical environment context. Its result counts do not establish acceptance
of the current PR07-C remediation; the final run must record its own immutable
HEAD, PostgreSQL availability, and exact totals.

### Historical 2026-06-28 A-04 auth boundary verification

- Release build completed with 0 warnings and 0 errors.
- `AuthSecurityHttpTests` passed 15/15 after the test harness persisted Data Protection keys to an isolated temp directory instead of the Windows user-profile key folder.
- `TenantIsolation` filtered tests passed 24/24.
- Full backend suite passed 128/128.
- Docker daemon and local PostgreSQL were unavailable on this host, so container/runtime database assertions remain separate evidence work.

### 2026-06-19 backend audit verification

- Release build completed with 0 warnings and 0 errors.
- The audit environment prevented the .NET test runner from opening its local IPC socket, so tests did not execute in that turn.
- Existing test files were inspected for coverage, but the backend audit does not claim a fresh passing test run.
- Detailed regression recommendations are in `docs/BACKEND_LOGIC_AUDIT.md`.

