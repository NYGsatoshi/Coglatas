# Issue #357 Task execution source-scope foundation verification

Status: candidate evidence only; this foundation intentionally does not close
[Issue #357](https://github.com/NYGsatoshi/Coglatas/issues/357).

## Approved boundary

The candidate implements only server-authorized policy metadata for Web and
Project-files source eligibility: a Project default, a complete optional Task
override, immutable per-run policy snapshots, an unavailable/no-I/O runtime
port, and the Task-detail policy panel. It does not implement outbound Web
retrieval, crawling, source selection, file materialization, raw source or
file-content persistence, provider configuration, a hosted execution worker,
or execution output.

Canonical Task automation remains deferred. This work needs an approved
canonical-spec promotion and a separately approved Web execution/provider and
egress contract before a PR may close Issue #357.

## Focused evidence

- `dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj --filter "Scope=Issue357"`
  passed 24 tests. It covers inheritance, server authority/redaction,
  version/idempotency conflict paths, snapshot immutability, no-I/O unavailable
  runtime, required audit behavior, direct persistence guards, idempotent
  transaction-stage snapshot capture, strict JSON, and CSRF.
- Focused Angular execution-scope component tests passed 9/9. They cover
  server-owned visibility, versioned default/override writes, protected-state
  clear (including a denied authoritative refresh), generic failures, latest
  snapshot display, and HTTP refetch after durable invalidations.
- `npm --prefix frontend run build` passed with the repository's existing
  unrelated budget warnings and no new execution-scope warning.
- `npm run test:ui -- --grep "keeps the server-authorized Task execution policy responsive without offering a runtime action"`
  passed 2 Playwright projects (desktop and 320-pixel mobile). It checks the
  authorized summary/save contract, locked snapshot display, responsive layout,
  no runtime action or request, and scoped accessibility using mocked APIs.
- The broader local backend suite passed 942 tests with 242 conditional
  PostgreSQL tests skipped, and the full Angular suite passed 803 tests in 79
  files. The build completed with no errors; the known unrelated test-analyzer
  warnings and existing frontend bundle/style budget warnings remain.
- `git diff --check` passed.

## Limits and required later evidence

- `POSTGRES_TEST_CONNECTION_STRING` was not configured locally. PostgreSQL
  migration backfill, scope-match trigger, snapshot/update/delete trigger, and
  query-plan behavior were not executed in this candidate environment.
- No Compose real-backend browser run was performed locally. Mocked static UI
  tests do not prove the HTTP/DTO/browser integration.
- There is intentionally no Web/provider/runtime execution test: no approved
  executor or egress contract exists. A future execution change needs
  post-commit durable dispatch, SSRF/redirect/egress policy, current file and
  authorization materialization rules, content/credential/retention policy,
  output authorization, and its own real-backend and PostgreSQL evidence.
