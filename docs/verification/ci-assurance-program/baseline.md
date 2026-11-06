# Security-branch CI assurance baseline

Status: **INCOMPLETE**. Integration target: **SecurityTestImplimation only**.
This inventory grants no policy activation, exception, release approval, new
required check, or performance-assurance credit. No Main mutation is authorized.

## Scope and evidence model

The initial audit is pinned to Security head
`3865b09a2d3ea3cb82f450c6d19ec643ffbae01f`. The clean starting worktree was on
an unrelated Qodana branch and was preserved. Work proceeds in a separate
Security-based worktree on `ci/security-assurance-baseline-20261106`.

[The gap ledger](gap-ledger.json) has 45 capability records across all five
mission domains. Each retains FILE_PRESENT, CONFIGURED, EXECUTED,
COVERAGE_PROVEN and GATE_INTEGRATED separately, together with the required owner,
implementation, case-set, dependency, severity, issue and approval fields.
Individual ownership is UNVERIFIED rather than invented. Null case identities
mean **not authenticated**, never an empty successful test inventory.

[The source/native inventory](source-native-inventory.json) records every one
of the 72 workflow files, its file digest, events, concurrency, permissions and
job dependency/condition/runner configuration. It also preserves all 76 returned
check records, 35 run records, referenced PR heads and issue lifecycle states at
the recorded observation time. Both API inventories fit their requested first
page; no omitted page is treated as success. These are source and GitHub metadata
observations, not signed execution or complete coverage attestations.
Non-GitHub-Actions app slugs are omitted from the public summary; app IDs and
check IDs remain, and the original responses are retained locally. All required
GitHub Actions app/producer identities remain explicit.

The audit covers source/configuration routing across workflows, CI/security/
performance harnesses, governance, backend/provider/architecture tests,
Angular/Playwright/Functional owners and active verification records. Deeper
per-route, per-event, per-transition and per-case reconciliation remains PARTIAL.
The size of this inventory does not establish coverage or Phase 0 completion.

The separate [SEC-05 native observation](observations/sec05-native-42132259076.md)
reconciles all 37 declared cases against a later exact-run artifact and classifies
404 native OpenAPI operations. Its bounded credit does not change this original
snapshot's source or incomplete domain-coverage state.

## Required contract and source identity

`governance/required-checks.json` remains authoritative. The six exact contexts
are `build-test`, `frontend-test`, `security-scan`, `publication-readiness`,
`functional-fast` and `performance-fast`, produced by GitHub Actions app 15368.
Their workflow/job identities, dependencies, applicability and success conditions
are unchanged. In particular, performance-fast success under suspended numerical
assurance is **NOT_EVALUATED**, not a measured latency/throughput pass.

All six have successful native check records at the baseline head. This is not
an all-CI pass: PR CI `42131490366/1` failed its non-required actor owner, Sonar
was still running at observation, and several specialized checks were skipped.
Each skipped/pending record remains visible in the snapshot.

PR CI executed merge `f08d37afd82be8c33e31e1fc98b4e0826b4e5bc6`, whose parents
are `586ee441f21a33d3e39c6bfec9bdfde1c5b971ae` and the Security head above.
Its tree is `e29c0d35dc6c1db5827785b581e9a90ee8f836ec`; the reviewed head tree
is `7c7e86aaacc9306307ec14873305ee6585ae06c1`. That tested merge contains
changes absent from the Security head. None of those changes was imported into
this branch. Local cached Main divergence was 112 Security-only / 86 Main-only
commits; this cached comparison is not an up-to-date Main qualification.

## First-attempt actor failure

[The retained failure record](actor-first-attempt.json) identifies run
`42131490366`, attempt 1, job `127611373281` and first failing step
`Require the fixed real-browser actor journey`.

The published receipt is `INCOMPLETE`, phase `ADMISSION`, release
`RELEASE_NO_GO`. The actual browser scenario did **not execute**. The eight
offline runner controls passed beforehand; they are not native journey evidence.
The admission guard requires exact merge/head tree equality. The recorded trees
necessarily violate that guard if reached. The catch-all receipt does not expose
the first failed assertion, so this is a verified incompatible precondition,
**not proof that it was the earliest rejection**, nor a product/browser failure.
Historical equivalence is not established. No flaky classification, rerun waiver,
assertion relaxation, retry or timeout increase is justified.

The job log reports successful source-artifact restoration and digest checks for
runtime `13463751971`, .NET `13463766527` and frontend `13464485150`. Those inputs
were not independently redownloaded in this audit. The failure artifact
`13462918887` was independently downloaded; its 231 ZIP bytes match GitHub's
SHA-256 `619deee57c80bee7fe931d3fc9c24048c78d5408723f5898ece7d21bf4ec9300`.
Its producer upload was matched to this job's log. Raw run/jobs/artifact metadata,
job log, ZIP and extracted receipt are retained locally in the sibling
`Coglatas-assurance-evidence-20261106` directory. Only sanitized finite facts
are committed here; remote retention can expire and is not permanent archival.

The runner was Ubuntu 24.04.5, runner image `20261101.360.1`. Its pinned browser
container pull succeeded. The browser itself never launched, so no browser/runtime
version is inferred from the configured image. A future finite admission-stage
diagnostic can expose a fixed refusal code without publishing error messages,
paths, credentials or protected response bodies. Keep every existing guard.

## Existing work reconciliation and priorities

1. **PR-A / first-attempt triage:** preserve the failure above and improve finite
   admission diagnostics. Do not claim the journey fixed while it is unexecuted.
   Existing #611 historical unknown failures remain independent obligations.
2. **PR-B / #1717:** existing Draft head
   `64ba845fe29dd18112400ac741cc98e5fb710801`, based on integrated #1716,
   already supplies original-source `--list` discovery and Linux directory-FD
   output hardening. Its current native run `42132259076/1` was in progress.
   Review and qualify that PR rather than duplicate it. Discovery is not execution.
3. **PR-C / #1680:** open head `23ed840a21f271185f9a7c200b6b2abbcbc92fab`
   contains an offline shard verifier, not trusted artifact ownership or enabled
   sharding. Keep the full protected frontend run authoritative.
4. **#1696 / #1691:** open profiler head
   `e08bc6271eb04b2aad1a1562b6e40a7aa890e4a5` needs current-base reconciliation,
   source-qualified execution and review. Job timing alone is not full DAG proof.
   #1715 remains closed unmerged after increased runner cost without a demonstrated
   critical-path benefit. Do not repeat the additive experiment as an improvement.
5. **PR-D:** SEC-05 is a bounded P0 catalogue, not every current route. SEC-16
   real HTTP/PostgreSQL grant lifecycle and SEC-17 real WebSocket controls already
   exist. Establish explicit current route/event/transition sets and reconcile
   real native evidence before adding tests or claiming domain completeness.
6. **PR-E:** #582 is closed but the current SEC-08 evaluator is explicitly
   NOT_ACTIVATED, inventory UNREVIEWED, zero real exceptions, approvalVerified
   false and liveGateQualified false. Initial Medium governance stays OWNER_HOLD
   under #614. No ratchet or exception is activated by this inventory.
7. **PR-F/G/H:** authenticated Angular/AJAX DAST, RESTler sequence execution and
   advanced adapters remain incomplete. The existing browser fixture is not an
   Angular crawl. Reuse SEC-02 isolation, authentication and the common finding
   contract. Preserve independent SEC-04/06 coverage and admission requirements.
   The supplied mission ends mid-PR-H; its remaining requirements are pending.
8. **Other active gaps:** Qodana duplicate-analysis #1500, Sonar native coverage
   #1507, all-table RLS/architecture approval and release exact-candidate trust
   remain open. #828 does not authorize adding a seventh protected context.
   Numerical API/DB assurance remains indefinitely suspended under #1128.

The baseline logs observe 12 architecture and 3,339 backend passes, 2,029 Vitest
passes and 443 Playwright passes. These results belong to the tested merge, not
the Security head tree. Counts are context only; no full expected/executed identity
set, source-only execution, complete route coverage or release credit is granted.
SEC-06 logs report all three existing roles and zero High alerts; complete report,
finding, filter, request and producer reconciliation remains outstanding.

## Validation and integration decision

Local baseline checks: existing required-check policy validator PASS; existing
publication-readiness validator PASS across 72 workflows; actor runner controls
8 PASS / 0 failed / 0 skipped. Native baseline checks above are historical
observations of their exact source, not execution of this documentation slice.
No active scanner was launched by this audit. Docker is available; local native
PostgreSQL/browser acceptance has not been executed for this slice.

**Integration decision: NO_MERGE pending independent review and exact-head native
qualification. Program remains INCOMPLETE.** Subsequent native evidence belongs in
the PR or a separate dated observation, preserving this original snapshot.

The inventory's own initial head `13655527` failed native LegacyNameGuard and
PR CI preflight because a copied external app slug violated the existing content
policy. Five downstream jobs also failed their producer/evidence requirements;
they receive no test execution credit. [The first-attempt record](inventory-first-attempt.json)
preserves those failures. The correction omits the unnecessary public app slug,
retains the original API response locally and changes no guard/exception. Fresh
qualification is required. The first local CodeRabbit request also failed with
`payload_too_large`: 122,532,160 bytes exceeded its 100 MiB limit. A scoped
independent review is required; that failure grants no review credit.

Rollback: revert only the inventory slice on a Security-targeted feature branch
and open a PR to SecurityTestImplimation. Do not delete the original failed-run
record or replace it with later green evidence. There are no runtime or policy
changes to roll back in this slice.
