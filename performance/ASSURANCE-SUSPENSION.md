# Temporary suspension of quantitative performance assurance

**Effective 2026-10-08**. Tracked by [Issue #1128](https://github.com/NYGsatoshi/Coglatas/issues/1128). Implemented in [PR #1127](https://github.com/NYGsatoshi/Coglatas/pull/1127).

Shared GitHub-hosted CPUs do not currently give reproducible numerical performance claims. A fixed runner/paid machine is not available. **Performance is not guaranteed; no earlier unstable or failed result is reclassified as PASS.**

## Current CI boundary

| Area | During suspension | Interpretation |
| --- | --- | --- |
| Build, frontend, functional, security and publication | Required as before | Normal correctness and safety gates |
| API `performance-fast` | Unfiltered PR route, policy verification, non-measured Python and Node contract tests | A green check confirms **contract and suspension only**, never latency/throughput |
| API k6 fast/regression and automatic diagnostics | Not executed | `SUSPENDED / NOT_EVALUATED` |
| PostgreSQL Small and Medium | Real DB collectors plus blocking structural/query-shape comparator | Structural PASS does not imply numerical duration PASS |
| DB duration Small/Medium | No `--duration` comparison on Main | `SUSPENDED / NOT_EVALUATED` |
| Local signed evidence pilot | Remains staged and verification-only | Zero acceptance credit |

The protected `performance-fast` context, six-check registry, CODEOWNERS and ruleset are unchanged. The check name is a compatibility constraint, not a performance guarantee. Machine-readable receipts record `numericalDecision=NOT_EVALUATED`, `numericalAcceptanceCredit=false`, `baselineQualificationCredit=false`, source SHA and Issue #1128. All existing baselines, budgets, failed cohorts, raw digest histories and comparators remain intact. Historical manual candidate and predeclared-campaign workflows cannot be interpreted as renewed required acceptance.

## Reactivation before GA / production SLA

1. Secure an approved repeatable environment or approve a validated same-run statistical comparison method.
2. Produce representative authenticated API and DB runtime evidence, including Medium, with uncertainty checks.
3. Separately approve prospective baseline enrollment and preserve historical failures.
4. Re-enable fail-closed API latency/throughput and DB Small/Medium duration checks **in another reviewed implementation PR**, not by toggling a JSON value.
5. Independently verify live required contexts, PR/Main exact SHA, and production concurrency/load constraints.

Until all these are complete, Issue #1128 stays open and no numerical performance guarantee may be claimed.
