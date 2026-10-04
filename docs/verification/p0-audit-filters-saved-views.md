# P0 Audit filters and saved views verification

Status: Issue #344 implementation candidate (2026-08-30)

## Delivered boundary

The maintained `/app/admin/audit` route now offers an apply-based global search
and Severity, Type, Actor, Source, Status, and relative Time-range facets. The
applied conditions use the canonical removable Filter Chip component, have a
single Clear-all action, expose the backend `totalCount`, and provide a
filter-clearing recovery action when the authorized query returns zero rows.

The complete query and storage contract is
`docs/contracts/audit-filter-view-v1.md`.

`GET /api/admin/audit-grid` owns every filter and the authoritative count.
Tenant/platform scope and `audit.view` are applied before predicates. Actor
filtering requires the independent `audit.sensitive_metadata.view` capability
before the query executes, and global search includes Actor names only with
that capability. Metadata, request IDs, IDs, Claims, Evidence, and raw content
are not search sources.

Applied input is mirrored to a shareable URL. Relative time presets are
re-evaluated to a UTC `fromDate` when applied. Retry repeats the same applied
query. Browser-local saved views contain only strict filter inputs; they are
versioned, limited to 20, and partitioned by authenticated Tenant/platform
scope and user. Applying a URL or saved view always performs a new authorized
request. Rows, counts, capabilities, metadata, and authorization results are
never persisted.

Session, Tenant, Workspace, and realtime authorization invalidation cancel
in-flight Audit list/detail/metadata requests and clear rows, counts, filters,
exact-event state, and capability state before another result can render.

## Focused evidence

- Backend AuditGrid tests cover all predicates together, case/whitespace
  normalization, authoritative filtered count, cross-Tenant exclusion, Actor
  facet denial, Actor exclusion from ordinary global search, and invalid
  values/date order.
- Controller coverage preserves the complete filter DTO while retaining the
  grid's bounded default page size.
- Angular tests cover URL-to-HTTP mapping, canonical chip removal, Clear all,
  zero-result recovery, authoritative count, protected-response cancellation,
  strict saved-view parsing, and user/Tenant storage partitioning.
- The representative Playwright flow applies filters with the keyboard,
  removes a chip with Enter, saves/reapplies/reloads a view, verifies the URL
  and server-filtered counts, checks 320px horizontal overflow, and runs axe.

## Deliberate exclusions

- No Claims/Evidence contract from Issue #340 is introduced or inferred.
- No server-synchronized/team saved views, immutable Audit snapshots, review,
  export, duration, realtime result transport, or arbitrary date/timezone
  picker is added.
- A saved view is a local input convenience, not an authorization grant or a
  frozen result set.

## Local verification

- `dotnet test Coglatas.slnx --no-restore -p:UseSharedCompilation=false`:
  983 passed, 248 skipped, 0 failed. The skipped set is environment-gated;
  `POSTGRES_TEST_CONNECTION_STRING` was not available, so the focused
  PostgreSQL translation test compiled but did not execute.
- Focused Audit backend suite: 18 passed, 1 environment-gated PostgreSQL test
  skipped, 0 failed.
- `npm --prefix frontend test -- --no-progress`: 979 passed across 96 files.
- `npm --prefix frontend run build`: passed with the repository's existing
  bundle/style budget warnings.
- `npm --prefix frontend run check:architecture`: passed.
- Audit-focused Playwright on Chromium desktop and mobile: 10 passed, 2
  intentionally project-skipped; includes the 320px keyboard/chip/URL/saved-
  view flow, horizontal-overflow assertion, and axe scan.
