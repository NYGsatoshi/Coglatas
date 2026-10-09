# PostgreSQL structural Main integration and indefinite numerical suspension

Owner decision: 2026-10-09. Tracks #1046, #606, #1056 and deferred #1128.
The suspension introduced by merged #1127 is preserved and made indefinite.
This record describes the implementation candidate. Hosted PR and resulting
exact-Main execution evidence must be added to the PR/issues before acceptance;
local tests and configuration do not supply Main qualification.

## Initial live audit

- Main: `e748fa5becc9899377ee23ffdba8730df59865dd`; its own Main CI
  [37892273481/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/37892273481)
  succeeded. This earlier run does not qualify the new integration.
- #1046: Open, non-Draft, conflicted; head
  `9484fb8094de4589ea587652d93c35c989d8b237`, API base snapshot
  `28e2b570def8a646625ada1add4f794e421bd6d8`. Its description contains older
  SHAs/Draft status and obsolete numerical acceptance requirements.
- #1127 merged as `ad0740f60798618a5d08c043ccab7a35b62f6dd0`.
- #606/#1056 are Open with original duration criteria unchecked. #1128 is
  Open and previously described temporary suspension and pre-GA reactivation.
  The current owner decision formally transfers unmet numerical work to #1128;
  it does not mark the original criteria as passed.
- Active ruleset `24643016`: strict six GitHub Actions contexts, integration
  `15368`, zero bypass actors, `current_user_can_bypass=never`, required thread
  resolution, normal merge/squash permitted. Other two inspected rulesets are
  disabled. The connector initially could not read legacy branch protection
  (403); a subsequent authenticated owner-credential read returned 404
  `Branch not protected` for that legacy endpoint. Main protection is supplied
  by the active ruleset, which was read directly and remains unchanged.
- CODEOWNERS remains the repository owner. Existing required identities are
  `build-test`, `frontend-test`, `security-scan`, `publication-readiness`,
  `functional-fast`, `performance-fast`.
- The original working tree has an unrelated deleted IDE `.name` file. Work
  is isolated in another worktree; that user change is preserved.

## #1046 disposition and retained evidence

Use a minimal successor rather than merge #1046's obsolete 42-file diff.
The only retained integration intent is the Main call to the existing DB
workflow. The successor starts from current Main and adds fail-closed result
aggregation and runtime-image provenance validation.

The old diff also adds 27 legacy duration baseline documents under three
hardware keys and `perf05-initial-duration-4c4d9802.json`, plus public-digest
allowlist entries, historical results/documentation and Ubuntu 24.04 runner
trust/configuration changes. A sampled legacy baseline declares `approved=true`;
the branch's historical approval claim is not a new approval. These additions
are retained in the old branch/PR history without importing, deleting,
reclassifying or newly approving them. Current Main's approved EnvironmentClass
catalogs, all baseline files and historical evidence remain byte-for-byte
unchanged. A new suspension decision does not rescue any rejected cohort.

## Complete gate classification

Here, before means Main after #1127; earlier active comparator behavior is
retained as history. Every hardware-dependent comparison is classified
`INDEFINITELY_SUSPENDED`; every independent protection is `ACTIVE_REQUIRED`.

| Gate / inventory | Classification and reason | Before -> after | Verification |
| --- | --- | --- | --- |
| API p50/p95/p99 absolute/relative performance budgets and historical comparisons | INDEFINITELY_SUSPENDED: shared CPU/VM timing can determine acceptance | Suspended k6 -> indefinite SUSPENDED / NOT_EVALUATED; no numerical credit | `test_performance_assurance.py`, existing API comparator/collection tests retain diagnostic behavior |
| k6 throughput and relative regression | INDEFINITELY_SUSPENDED: hardware capacity affects throughput | Suspended -> indefinite NOT_EVALUATED | API routing/suspension tests; historical thresholds unchanged |
| CPU/VM timing, relative MAD/variance and stability eligibility | INDEFINITELY_SUSPENDED: no approved cross-run reproducibility | Dormant numerical comparator -> remains dormant for acceptance | Existing comparator hardening/environment tests; date/GA/runner reactivation rejection |
| DB Small/Medium repeated duration, past-baseline comparisons, nine canonical streams per profile | INDEFINITELY_SUSPENDED: elapsed duration varies with host | No regular `--duration` -> remains absent in PR/Main/nightly; separate receipt NOT_EVALUATED | DB assurance tests, preserved DB duration adapter tests |
| Same-measurement-environment numerical qualification and automatic baseline enrollment | INDEFINITELY_SUSPENDED: reproducibility/approval is unresolved | Staged/dormant -> zero baseline qualification credit, no new enrollment/campaign | Suspension receipt, local signed-evidence and existing approval/campaign validators |
| Nine query counts in each Small/Medium profile (18 decisions) | ACTIVE_REQUIRED: fixed command budgets do not depend on duration | Strict real DB PR/nightly collector -> also same-run Main artifact execution | DB normal/count mutation tests and actual hosted collector |
| Nine Small-to-Medium and page-size growth / N+1 decisions | ACTIVE_REQUIRED: command growth is structural | Strict growth checks -> unchanged | Controlled entity/page N+1 and aggregate mutation tests |
| SQL fingerprint/shape, bounded ordered paging, unbounded materialization and DB read bounds | ACTIVE_REQUIRED: query structure/read counts | Per-sample structural checks -> unchanged within the 18 query-count decisions | DB paging/materialization/redaction tests; collector evaluates response order and disjoint pages |
| Selected Task-ID indexed lookup (one decision) | ACTIVE_REQUIRED: semantic plan invariant | Real Medium EXPLAIN on >=3,000 Tasks -> unchanged | Plan semantic/index mutation tests; hosted Medium collector |
| Dataset cardinality/growth and fixture integrity | ACTIVE_REQUIRED: fixture completeness is independent of timing | Exact fixtures/identity/growth -> unchanged | Fixture, partial evidence, wrong-SHA and growth tests |
| Ten-second per-command emergency ceiling | ACTIVE_REQUIRED: independent extreme-failure limit; no baseline/hardware comparison | Fail above 10,000 ms -> unchanged | Existing extreme-slow-command test and suspended aggregate hard-ceiling mutation |
| Startup/command/request/workflow timeouts | ACTIVE_REQUIRED: bound hung/failed collection, not latency qualification | Existing finite limits -> unchanged | Existing preflight/collection failure tests, collector failure handling |
| Migration consistency and actual PostgreSQL execution | ACTIVE_REQUIRED: persistence and provider correctness | Real PostgreSQL 18 with migrations -> unchanged Small/Medium; never replaced with mocks | Backend PostgreSQL integration and real hosted DB collection |
| Evidence schema, missing/duplicate samples, safe SQL metadata and SHA/fixture/environment integrity | ACTIVE_REQUIRED: valid complete evidence is required even while timing is unevaluated | Fail closed -> unchanged | DB schema/privacy/identity and collector failure tests |
| Runtime artifact provenance | ACTIVE_REQUIRED: binds actual runtime to target source and producer | Source/.NET stamps -> also SHA-tag and recorded/loaded image-ID equality; same run passed explicitly | `test-main-runtime-artifact.py`; exact hosted artifact restore |
| Main DB collector/routing/result aggregation | ACTIVE_REQUIRED: failed/skipped required work cannot pass | PR/nightly only -> Main reusable call; producer result checked; `build-test` also requires DB success | `test_performance_db_ci.py`, `test-main-check-results.py`, runtime workflow integration test |
| API contract, harness unit tests, routing, suspension contract and six required identities | ACTIVE_REQUIRED: no measured numerical acceptance | Required `performance-fast` policy/contract -> unchanged; invalid suspension and missing checks fail | Assurance/API tests, `test-required-pr-checks.py`, live exact-head checks |
| Build, frontend, architecture, security, functional, ReSharper, Qodana, CodeQL, publication and governance | ACTIVE_REQUIRED: independent product/quality protections | Existing identities, thresholds and ruleset -> unchanged | Applicable normal PR checks and full exact-Main CI; no local substitution |

The 28 decisions remain 18 per-profile structural checks + nine growth checks +
one selected plan invariant. Structural duration metadata remains validated and
the emergency ceiling remains enforced. Unstable durations below that ceiling
cannot change structural PASS; exceeding it is still a genuine failure.
Existing baseline immutability/approval/schema validators remain active even
though new numerical suitability/enrollment receives no acceptance credit.

## Runtime and aggregation path

`main-build-artifacts.yml` builds the licensed production runtime once, records
source SHA and full image ID and uploads the artifact. `performance-db` runs
with `always()`, supplies that exact SHA/run and producer result, and only reuses
the successful producer. Both real Small/Medium collectors restore source/.NET
stamps and image identity. A missing stamp, wrong tag, wrong loaded image,
missing collector, failed collector, malformed evidence or structural regression
fails the DB workflow. Main `build-test` uses its existing `always()` guard and
requires that DB workflow to succeed as well as its original dependencies.

The shared restore helper also serves PR Functional execution. Its existing
PR image family is preserved and its producer now records the same image ID;
Main and PR image families cannot substitute for each other. Historical artifacts
without the new image-ID member are not rewritten; current qualification must
use the new producer on the exact source.

## Regression acceptance and stop boundary

Required cases: normal 28/28 structural PASS; query count, N+1, paging/index,
emergency ceiling, collector failure and missing/mismatched evidence FAIL;
duration-only variance and API timing variance NOT_EVALUATED; invalid suspension,
config-only reactivation and missing required identity FAIL. Existing comparator
and historical diagnostic tests continue to validate their preserved algorithms
without granting current numerical acceptance.

Local Linux-export verification passes 267 performance tests, nine Main-result
tests, four artifact-restore tests and 45 required-check governance tests; the
Node harness passes nine tests (334 total). Static required-check and publication
policies and performance contract/environment validators also pass. Initial
Windows-CRLF/missing-SSH-tool failures are retained without changing fixed hashes
or expectations; normalization uses Git blob content. Initial local backend
execution omitted migrated schema and is retained as a setup failure; only a
correctly migrated rerun can establish backend/provider success.

Hosted PR qualification
must preserve all six contexts and applicable quality/security/functional checks,
zero unresolved threads and the normal protected merge. The resulting exact
Main requires its own real 28/28 DB run, Main aggregation, full backend/provider,
architecture, functional/security, quality and publication verification. Existing
failure evidence must remain available; an independent repair uses another normal
PR if Main fails. Record exact source/run/attempt/artifact IDs and digests in the
PR and Issues #606/#1056/#1128. No benchmark campaign or Avalonia implementation
is authorized. No SLA or production performance readiness is asserted.
