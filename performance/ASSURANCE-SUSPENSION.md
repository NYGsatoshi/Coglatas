# Indefinite suspension of hardware-dependent numerical performance assurance

**Effective 2026-10-08**. Tracked by [Issue #1128](https://github.com/NYGsatoshi/Coglatas/issues/1128). Implemented in [PR #1127](https://github.com/NYGsatoshi/Coglatas/pull/1127).

Shared GitHub-hosted CPUs and VMs do not currently provide an approved reproducible basis for comparisons across hardware. The owner confirmed indefinite suspension on 2026-10-09. There is no expiry or automatic return at GA, a date, or a runner change. **Performance is not guaranteed; no earlier unstable, invalid or failed result is reclassified as PASS.**

## Current CI boundary

| Area | During suspension | Interpretation |
| --- | --- | --- |
| Build, frontend, functional, security and publication | Required as before | Normal correctness and safety gates |
| API `performance-fast` | Unfiltered PR route, policy verification, non-measured Python and Node contract tests | A green check confirms **contract and suspension only**, never latency/throughput |
| API k6 fast/regression and automatic diagnostics | Not executed | `SUSPENDED / NOT_EVALUATED` |
| PostgreSQL Small and Medium | Real DB collectors plus blocking structural/query-shape comparator | Structural PASS does not imply numerical duration PASS |
| DB duration Small/Medium | No `--duration` comparison on Main or nightly | `SUSPENDED / NOT_EVALUATED` |
| Local signed evidence pilot | Remains staged and verification-only | Zero acceptance credit |

The protected `performance-fast` context, six-check registry, CODEOWNERS and ruleset are unchanged. The check name is a compatibility constraint, not a performance guarantee. Machine-readable receipts preserve `policyState=SUSPENDED` and also record `assuranceMode=SUSPENDED`, `suspensionDuration=INDEFINITE`, `automaticReactivation=false`, `numericalDecision=NOT_EVALUATED`, `numericalAcceptanceCredit=false`, `baselineQualificationCredit=false`, source SHA and Issue #1128. All existing baselines, budgets, failed cohorts, raw digest histories and comparators remain intact. Historical manual candidate and predeclared-campaign workflows cannot be interpreted as renewed required acceptance. No new campaign, automatic enrollment, expensive fixed runner or self-hosted runner is authorized by this policy.

## Explicit reauthorization

1. Obtain explicit owner direction to resume numerical assurance.
2. Approve a measurement method and environment with demonstrated reproducibility: the same hardware, equivalent VM execution conditions, or another independently verified repeatable method.
3. Re-enable fail-closed numerical gates **in a separately reviewed implementation PR**, preserving historical failures and prospective baseline approval.
4. Complete independent verification and exact-Main CI qualification, including representative authenticated API/DB workloads and required contexts. Address production concurrency/load constraints before any SLA or production performance readiness claim.

All four conditions are mandatory. A JSON edit, calendar date, GA plan or runner replacement alone cannot resume assurance. Until explicit reauthorization completes, Issue #1128 stays open and no numerical performance guarantee, SLA achievement or production performance readiness may be claimed.

## Gate classification and integration evidence

The complete gate inventory, before/after behavior, reasons and regression-test mapping are in [the structural Main integration record](../docs/verification/performance-structural-main-integration.md). The existing 28 structural decisions, real PostgreSQL Small/Medium execution, schema/provenance validation and ten-second per-command emergency ceiling remain active. Timeouts and the emergency ceiling are independent bounded-failure protections; they do not compare hardware or award numerical acceptance.
