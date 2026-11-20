# PR-N cross-domain evidence and final acceptance contract

Authority: the complete PR-N requirements and exit criteria supplied by the
Owner in this continuation, together with [approved D01–D15](approved-decisions.md).
This restores the previously truncated contract. It is an engineering acceptance
contract, not an integration approval or a replacement governance policy.

The final verifier's required behavior is specified below. The accompanying
`validate-contract.py` checks documentation structure, references, decision text,
explicit state separation and retained source bytes only. It does not implement
the cross-domain final evidence verifier, authenticate execution, or grant a GO.
`finalEvidenceVerifierImplemented=false` remains explicit in
[the machine-readable contract](program-contract.json).

The bounded documentation validator supports relative file references inside
this contract directory. External and anchor-only links are intentionally
unsupported and rejected; it does not silently skip them or claim to validate
remote availability or fragment targets. Source URLs retained in the evidence
manifest are citations, not authenticated execution inputs.

Preservation validation requires the pinned historical Git objects, including
`b34a0503ca0637634b3f492b69f3d3491c12c6d2` and
`1b01e4eede110660b96e2a3ab691f0dc0d2e196e`. A shallow checkout, or full history
of only the recovery branch, may omit the unmerged inventory commit. Before
running the preservation CLI or its mandatory positive test, materialize any
missing objects with read-only exact-SHA fetches from this repository:

```powershell
git fetch origin b34a0503ca0637634b3f492b69f3d3491c12c6d2
git fetch origin 1b01e4eede110660b96e2a3ab691f0dc0d2e196e
```

These fetches do not update Main or another integration branch. Do not skip
preservation tests when history is unavailable: missing history is a failing
prerequisite, and canonical digest mismatch remains a failing result. Any future
CI wiring must materialize these exact objects with its existing read permission
before executing the checks. No such workflow change is made by this slice.

## Required PR-N deliverables

### N01 — Updated machine-readable capability ledger

Update all applicable capability records using the original 45-capability ledger
as a retained baseline. Keep D04's file presence, configuration, native execution,
verified coverage and final-gate integration separate. Preserve ownership,
requirements, applicability, dependency, severity, approval, gaps and references.
Record new gaps and remaining future work without replacing the original record
or treating inventory presence as execution or comprehensive coverage.

### N02 — Full workflow and execution applicability matrix

For every workflow/job, record its source identity, event, branch, path routing,
dependencies, condition, platform, trust/permission boundary, required/advisory
role and expected case/scanner owners. Reconcile actual run/attempt/job outcomes,
including failed, cancelled, skipped, blocked, missing and incomplete executions.
A configured schedule in Security does not establish default-branch registration
or Nightly activation. Non-applicability needs explicit source/policy evidence;
it must not become a blanket waiver for absent mandatory work.

### N03 — Exact source-derived test-case completeness reconciliation

Independently discover the applicable original Suite/Case identities and compare
them with actual native execution identities. Reconcile missing, unexpected and
duplicate cases; skips and their authorization; each original failure, retry,
cancellation and interruption. Preserve each run/attempt and outcome separately.
Counts, mocked browser success, source discovery and later passing attempts do
not establish complete real-backend/PostgreSQL/browser execution.

### N04 — Independent artifact producer and digest verification

Independently verify source SHA, Run ID, Attempt, Producer, GitHub Artifact ID
and content Digest for each mandatory artifact. Bind the executed merge and
reviewed head explicitly; do not relabel a merge as the reviewed head. Match
actual job/workflow/app identity and upload ownership, API metadata, archive
bytes, contained report identities and applicable signatures. A filename,
artifact title or self-asserted sidecar is not independent provenance. Refuse
stale, cross-source, cross-run, cross-attempt or unverified mandatory inputs.

### N05 — Security finding and exception policy reconciliation

Reconcile all applicable scanners, roles, findings and failure states against
approved blocking policy. Critical/High findings and scanner failures block.
Medium ratchet activation retains its reviewed-initial-inventory and Owner
approval prerequisites. Every exception/baseline needs Owner, rationale,
tracking Issue, expiry, explicit scope and verifiable approval. Reject expired
or unauthorized scope expansion; never silently suppress a finding.

### N06 — SAST/SCA/SBOM and native coverage reconciliation

Reconcile configured and actually executed CodeQL, OSV, Trivy, npm audit, NuGet,
Socket, Gitleaks, Dependency Review, Syft, Grype, Cosign and Renovate roles.
Distinguish execution success from complete dependency graphs, vulnerability
databases, external binaries, runtime images and generated-artifact coverage.
Bind original native .NET/TypeScript/JavaScript/Python source ranges, reports,
source maps and imports to source/run identities. Preserve zeros and missing
branches, prevent test-body or duplicate-report inflation, and distinguish
analysis from trusted publication. Reuse #1749 without importing its unverified
Sonar result as success. Both Qodana analysis paths remain until equivalent
consumer/output/signature/failure contracts and real Cloud ingestion are proven.

### N07 — Source-matched CI performance evidence

Reuse #1696. Compare repeated compatible exact-source runs, excluding runner
allocation waiting. Report workflow DAG critical path, security/backend/frontend
duration, total runner-seconds, artifact size/transfer, detection completeness,
failures, retries, flakes and confidence. A job-only timing report or historical
duration is not a verified whole-CI improvement. Preserve nonbeneficial/failed
experiments. No scanner-budget, fuzzing, role, assertion or deadline relaxation.

### N08 — Expected-versus-live governance comparison

Compare registered identities and repository-owned contracts with actual enabled
rulesets, exact contexts/apps, strictness, branch include/exclude targets, review
and Code Owner requirements, bypass actors, update/deletion restrictions and
enforcement. Report CHECK_TOPOLOGY_VALID, CHECK_EXECUTION_VALID,
RULESET_IDENTITY_MATCHED, RULESET_ENFORCEMENT_VALID,
SECURITY_BRANCH_PROTECTION_VALID and INDEPENDENT_REVIEW_VALID separately.
Alternate six-check parity cannot replace the registered identity implicitly.
Disabled registered rulesets and unmet scope/enforcement requirements block.
Governance settings and policy changes remain separately prohibited here.

### N09 — Independent review status

Record review provider, exact reviewed SHA/diff/base, scope, result, findings,
resolution and availability. Preserve failed/cancelled attempts and service
errors. Old-head comments, skipped review checks and self-review do not satisfy
mandatory current-source review. Use prescribed CodeRabbit review; its skill
forbids manual fallback. Restoring CLI availability alone is not review
completion. Absent mandatory review evidence blocks eligible integration.

### N10 — Owner decision and unresolved HOLD register

Retain each decision's authority, scope and evidence, and each hold's owner,
dependency, impact, resolution condition and approval boundary. Approval of
D01–D15 does not grant final acceptance, governance changes, Main changes,
release approval or a new required check. A mandatory unresolved requirement
is a blocker, never a completed exception. Future/non-applicable capabilities
need explicit classification and applicability evidence rather than invented
approval or silent omission.

### N11 — Cross-domain final acceptance decision

Produce an exact-source, evidence-bound decision over all applicable mandatory
capabilities and exit criteria. Keep implementation, native execution, coverage,
provenance, independent review, governance, security, performance, Nightly
activation, release and Owner approval independent. Success in one state must
not automatically imply success in any other. Local validation and branch-native
execution do not establish trusted execution, Nightly activation or release.
Only explicit, verified applicability may exclude a future requirement. Any
mandatory failed, missing, unverified, inconsistent or unexecuted requirement
prevents acceptance; unavailable evidence cannot default to true.

### N12 — Rollback and evidence preservation plan

Retain immutable source/run/attempt/job/artifact identities, original bytes,
digests, original failures/cancellations and unsuccessful review attempts.
Publish append-only corrections with links to prior observations; never rewrite
published Git history or overwrite first-attempt outcomes. Define rollback of
each eligible implementation through a reviewed Security-targeted revert,
including dependencies, disabled activation and post-revert qualification.
Keep raw evidence in authorized storage with a retention/expiry register; Git
hashes alone do not promise permanent external artifact availability. Do not
discard evidence or modify Main to perform rollback. Integration remains held
until a reviewable rollback path and evidence retention are established.

## Mandatory final-verifier rejection controls

The final verifier must reject every condition below with a failing result,
retain the original input identity and avoid printing secrets/report contents.
These controls must be independently executed before eligible integration.

| Control | Mandatory rejection |
| --- | --- |
| R01 | Wrong source identity, including a mislabeled merge/head or source drift |
| R02 | Wrong Run ID or Attempt, including stale/cross-run/cross-attempt reports |
| R03 | Incorrect or unverified producer/workflow/job/application identity |
| R04 | Missing or unverifiable GitHub Artifact ID |
| R05 | Content/archive/report digest mismatch or corrupted mandatory evidence |
| R06 | Incomplete case inventory, missing identities, unexpected identities or duplicates |
| R07 | Unexpected or unauthorized skips; omitted failure/retry/cancellation outcomes |
| R08 | Missing, empty, malformed, stale, inconsistent or unexecuted mandatory reports |
| R09 | Expired exceptions or baselines lacking any required D08 approval/scope field |
| R10 | Absent, failed, interrupted or unexecuted mandatory scanners |
| R11 | Disabled registered rulesets or unmet governance identity/enforcement/scope |
| R12 | Missing mandatory exact-source independent review evidence |

Reject unsupported schemas, ambiguous identities and unknown applicability
rather than manufacturing an empty successful result. Positive controls must
also demonstrate that valid bounded inputs are parsed without granting authority
beyond their authenticated scope. A passing fixture is not final acceptance.

## Independent completion states

| State | Required evidence before completion |
| --- | --- |
| implementation | Applicable source/contract implemented, reviewed scope and rollback |
| nativeExecution | Actual current-source native execution, all applicable owners |
| coverage | Original applicable Suite/Case and source-range completeness reconciliation |
| provenance | Independent D10 verification for every mandatory artifact |
| independentReview | Mandatory exact-source review complete and findings resolved |
| governance | Expected identity, enforcement and branch-scope requirements satisfied |
| security | Required scanners/findings/exceptions meet approved policy |
| performance | Reproducible compatible measurements wherever improvement is claimed |
| nightlyActivation | Applicable trusted execution, default-branch registration and activation |
| release | Separate explicit release criteria, evidence and authorization |
| ownerApproval | Verifiable approval for the exact final decision and its scope |

The [current state register](program-status.json) records these states separately.
None is an alias of a check conclusion, another completion flag or a previous
source's result. Future requirements remain explicitly classified; unavailable
default-branch activation is an Owner hold, not successful Nightly operation.

## Program exit criteria

The program remains INCOMPLETE until all applicable mandatory capabilities are
verified and all twelve criteria below are satisfied:

1. The original six required checks remain intact.
2. No original failure is hidden or reclassified without evidence.
3. Applicable test-case sets are reconciled.
4. Required artifacts have independently verified provenance.
5. Security findings follow approved blocking and exception policies.
6. Native execution evidence is current-source qualified.
7. Mandatory independent reviews are complete.
8. Governance identity and enforcement requirements are satisfied.
9. Performance improvements are reproducibly demonstrated where claimed.
10. Applicable PR-J–N obligations are accepted.
11. Remaining future work is explicitly classified.
12. No Main modifications occurred.

A mandatory unresolved requirement remains a blocker, not a completed exception.
No verifier result authorizes Main changes or supersedes the sole integration
target SecurityTestImplimation. Eligible integration still requires all D15
conditions and resolution of applicable governance/review holds.

## Current recovery and next implementation slice

The supplied decisions and PR-N text are now available; their former absence
is no longer a blocker. Original #1738 evidence and #1747 observations remain
unchanged, with byte-preservation anchors in [the preservation manifest](evidence-preservation.json).
PR #1749 remains independent and unmerged. Trusted Sonar import and PR-J–N
acceptance remain unverified/open.

The dependency-ready PR-N preparation in this slice implements executable
documentation/reference/state/preservation validation with negative controls.
It does not implement or activate the final evidence verifier. The next
independently executable PR-J slice is a bounded Functional source-versus-native
case reconciliation adapter: reuse #1717 source discovery, existing native
reporters and #1680 rejection semantics, retain every attempt/skip/failure and
prove missing/duplicate/extra case rejection before requesting native admission.
Do not substitute the existing aggregate owner counts for original case IDs.
