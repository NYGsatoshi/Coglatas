# Audit pagination overflow - independent Main repair

Current status: normally merged and exact-Main qualified; #1119 is closed.
The original failure and intermediate pending checkpoints below are retained.

## Failure and reproduction

Exact Main `7e357885c4637584d1467a387e13726d5c542dba`,
[run 37851959012](https://github.com/NYGsatoshi/Coglatas/actions/runs/37851959012),
attempt 1, failed Security job 113567788063. Deep Schemathesis alpha-owner,
seed `1495649275`, found HTTP 500 on both `/api/audit-logs` and
`/api/tenant/audit-logs` with `Page=28737957&PageSize=275`.
PostgreSQL reported `2201X: OFFSET must not be negative`.

The effective page size is the existing maximum of 100. Multiplication in
`(page - 1) * pageSize` overflowed a signed 32-bit integer. The correct offset
2,873,795,600 became -1,421,171,696. A different large page can wrap to zero
and incorrectly return the first page. The grid and security-event queries
use the same unsafe arithmetic in the same service.

The downloaded Security artifact 11583284319 was SHA-256 verified as
`f99aa8e0c5a507aa52859ee6fc0793065396786bff6f49f2bdcaf61e9aa1c497`.
The original failure is preserved. Actual ZAP scans passed all three roles
with zero high/medium/low findings; Java Preferences warnings are unrelated.
The failure propagated to `build-test`, `frontend-test` and `security-scan`,
despite the backend and frontend jobs individually passing. This is a failed
whole Main, not qualified evidence for #1119 closure.

## Minimal correction

`DbAuditQueryService` now computes each of its three offsets using
`(page - 1L) * pageSize`. After existing authorization, normalization, scoped
filtering and counting, offsets beyond `int.MaxValue` return an empty page
with the original normalized page, clamped page size and scoped total count.
Representable offsets use the existing EF projection and ordering.
This follows the neighboring announcement repository's existing pattern.

The repair adds no new page limit, DTO/route, permission, package, migration,
schema, quality suppression or workflow configuration. Current audit
authorization and sensitive-field projection remain ahead of pagination.
The scanner still treats HTTP 500 as a failure; no DAST rule or role is skipped.
Security Foundation persistence/integration/read work remains #1120-#1122.

## Local verification

The new regression first failed on unchanged source with the exact PostgreSQL
`2201X` error: one failed test, zero passed/skipped. After the correction,
the focused Audit plus PostgreSQL filter suite passes all 64 tests with zero
failures/skips. It uses the owned disposable PostgreSQL 18.6 server and
migrated-template clones of the existing 73-migration schema, not production.
All 11 architecture checks also pass with zero failures/skips.

The provider fixture checks audit list, grid and security events for the exact
failing page/size, a wrap-to-zero pair, `int.MaxValue`, a normal empty second
page and the existing negative-page normalization. Two seeded tenants verify
that counts and returned first-page data retain the requested tenant scope.
Its fixed authorization fixture establishes query/provider behavior; existing
Audit authorization/controller tests provide separate authorization coverage.
Required exact-head CI and repaired exact-Main Security/Functional/quality
qualification remain pending. #1119 stays open until qualified.

## Independent scheduled producer race

Scheduled structural DB run 37853194884 attempt 1, at the same exact SHA,
failed before query evaluation because its resolver requires a completed Main
producer while 37851959012 was running. After the producer completed, its own
assembler success and exact non-expired artifact were verified before one
full same-SHA rerun was submitted. The resolver explicitly accepts successful
assembled artifacts even if a later validation lane fails. That rerun remains
pending; it does not repair the separate audit-pagination source defect.
The original routing/results artifacts and failure remain recorded.

Numerical performance assurance remains SUSPENDED / NOT_EVALUATED under #1127.
#1128 reactivation and performance PR #1046 remain separate workstreams.

## Final qualification - 2026-10-09 UTC

PR [#1139](https://github.com/NYGsatoshi/Coglatas/pull/1139), exact head
`307fb3f19764d626d92a48c29bcdbc8d69150fe2`, passed applicable PR checks in
run 37856131505: 357 scoped provider/backend tests, actual Security runtime,
CodeQL and ReSharper (93 existing warnings, zero changed-file findings).
All six required contexts passed under unchanged strict ruleset 24643016.
Normal expected-head merge produced `35e541436222654b5ee345082ec82487b6d323b5`.

[Exact-Main run 37858309449](https://github.com/NYGsatoshi/Coglatas/actions/runs/37858309449)
passed attempt 1: 1,960 backend tests including the regression, 11 architecture
checks, frontend, licensed acceptance, all seven Extended Functional journeys,
both full Qodana lanes (2,395 findings, zero critical/unresolved/model failures),
CodeQL and applicable structural/environment checks. Five deep Schemathesis
roles passed 108,224-109,512 requests each, all scanner exits zero; three actual
ZAP roles had zero high/medium/low findings. Security artifact 11586324986 has
published SHA-256 `5c59f801dd581413d11d3dece7fa9990eec9e04e2f98020ea8a0453ea41e20c4`.
#1119 is closed with [acceptance/retained-failure evidence](https://github.com/NYGsatoshi/Coglatas/issues/1119#issuecomment-6071466871).

The earlier scheduled structural run 37853194884 recovered on same-SHA attempt 2
after producer completion, preserving failed attempt 1. No query/performance
contract changed; duration remained SUSPENDED / NOT_EVALUATED. A separate scheduled
Functional run at the final repair SHA similarly stopped before tests while its
producer was incomplete, then passed same-SHA attempt 2 after exact-Main success.
These retries address changed producer availability; neither concealed a source
failure or resumed #1128 numerical assurance.
