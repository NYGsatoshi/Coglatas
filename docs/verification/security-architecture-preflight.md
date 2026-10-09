# SEC-ARCH pre-Avalonia exit contract

Status: **PRE-AVALONIA SEC-ARCH: BLOCKED**. SEC-ARCH verification remains Advisory.
This adds the owner-approved SA-01 through SA-08 conditions to the existing
Avalonia backend boundary without granting mapping, policy or activation approval.

## Required exit evidence

All of the following must be established for one exact candidate and applicable
immutable artifacts before recording COMPLETE — PRE-AVALONIA SEC-ARCH PASS:

1. #1144 through #1152 meet every retained acceptance criterion.
2. Every adapter has a reproducible isolated synthetic execution environment and executed positive/negative controls.
3. The initial private TMT/STRIDE/SPEC mapping has personal owner approval at an immutable revision/digest.
4. Every PostgreSQL table has an approved semantic classification, with no silent exclusions.
5. All required RLS policies, roles, applicable operations and migration/runtime changes are approved and implemented.
6. All required tables pass actual PostgreSQL row-access/denial, grant/bypass, context, transaction/pool and integration controls using the applicable application/worker identities.
7. The exact product RLS activation change has separate owner approval.
8. API and SignalR actual current-state authorization tests pass with complete applicable contract coverage and live controls.
9. Pinned authenticated isolated Kafka producer/consumer/ACL positive and rejection tests pass.
10. Applicable isolated service identity/audience/scope/tenant/TLS/credential/network tests pass.
11. #842/#614 audits qualify false-green, stale/missing/disabled verifier, scope shrinkage and evidence governance behavior.
12. Existing Required CI/review conditions pass; any SEC-ARCH Blocking promotion has completed its SA-07 qualification.
13. Execution, source/build, environment, contract/policy, run/attempt and artifact digests reconcile to that exact candidate.
14. No unapproved contract relaxation, expired/unapproved exception or critical security gap remains.
15. Public artifacts pass disclosure/sanitization review without private specification/model/configuration leakage.

Missing execution/approval and indeterminate coverage remain UNVERIFIED/ERROR or
BLOCKED. Adapter absence is not NOT_APPLICABLE. Human review is not runtime PASS.
Product Kafka/services can remain inactive after isolated fixture qualification;
that does not certify operational compliance or authorize infrastructure.

## Current implementation limits

The [runtime fixtures](security-architecture-fixtures.md),
[Kafka fixture](security-architecture-kafka.md) and typed CLI provide
representative controls. Every SEC-ARCH issue remains open. Full table/operation,
endpoint/event/capability and worker/context qualification, registered canonical
SPEC identities, owner approvals and trusted final reconciliation remain open.
No product RLS migration/activation or gate promotion is qualified by this file.

`scripts/ci/sec_arch_evidence.py` captures observed results from actual TRX
definitions/results, an explicit reviewed execution catalogue, exact checkout,
same-candidate build stamp, assembly/result hashes and observed environment.
It rejects identity/counter/timestamp disagreement and records missing/skipped
methods as UNVERIFIED, failed cases as FAIL and aborted cases as ERROR.
Case parameters, machine/user names, raw logs, paths, credentials and bodies are
excluded. Independent metadata mutation controls run in existing CI preflight.
The historical 89-case catalogue is preserved and expanded through explicit
registry/traceability and approval-reference controls. Each receipt records its
own exact required and observed count; new tooling cases do not establish new
product contract coverage.

The existing backend/Main jobs capture these sanitized observations without
rebuilding the product or changing Required Check identities. Unselected PR
coverage is explicitly UNVERIFIED. A build stamp/job identity is self-reported
metadata until independently reconciled with the trusted producer/run/artifact;
the receipt always records that prerequisite and pre-Avalonia BLOCKED. Receipt
presence and representative passing counts cannot close #842/#614 or establish
all-adapter completion. Changes to this execution catalogue need review-visible
coverage accounting; silent removal is not completion.

`scripts/ci/sec_arch_reconcile.py` independently binds downloaded execution and
producer ZIP bytes to expected GitHub artifact digests, exact candidate/run/attempt,
the producer source/build stamp, all five compiled assemblies and the environment
fingerprint. It streams a bounded hashed snapshot and never extracts archive paths.
The original receipt and outcomes remain unchanged, including FAIL/ERROR.
Expected identities/digests must be obtained independently from reviewed GitHub
run/artifact provenance. This offline check does not authenticate that provenance,
raw TRX, signatures, claimed test coverage or owner approvals. Its report always
retains trusted attestation UNVERIFIED and pre-Avalonia BLOCKED. Existing PR
preflight and trusted Main backend jobs execute positive controls and deliberate
archive/binding mutations without rebuilding the product.

```text
python scripts/ci/sec_arch_reconcile.py --producer <producer.zip> --execution <execution.zip> --candidate-sha <exact-SHA> --run-id <GitHub-run> --run-attempt <attempt> --producer-digest <independent-ZIP-SHA256> --execution-digest <independent-ZIP-SHA256> --output <new-private-report.json>
```

Keep downloaded artifacts and detailed reconciliation reports in private review
storage. A historical Main or PR test-merge report cannot qualify a different SHA.

`scripts/ci/sec_arch_github_provenance.py` resolves current HTTPS GitHub API run,
attempt, workflow, required producer/backend/security jobs and exact artifact IDs
for a completed Main push. It reads the immutable workflow source at the supplied
candidate, rejects forks, renamed/missing/skipped jobs, replaced/expired artifacts
and changed authority, and obtains artifact digests directly from GitHub. Optional
local ZIP paths then reuse the bounded five-assembly byte reconciliation. API
redirects are disabled; credentials are read only from `GH_TOKEN`/`GITHUB_TOKEN`
and never included in diagnostics, URLs or reports.

```text
python scripts/ci/sec_arch_github_provenance.py --candidate-sha <exact-Main-SHA> --run-id <GitHub-run> --run-attempt <attempt> --producer-id <main-dotnet-build-ID> --execution-id <main-sec-arch-kafka-ID> --producer <producer.zip> --execution <execution.zip> --output <new-private-report.json>
```

This authenticates a refreshed GitHub server observation, not an atomic or signed
execution attestation, raw TRX semantics, complete contract scope or personal
approval. The artifact API has no upload-attempt field; the original execution
receipt must bind the exact run/attempt. Omitting both ZIP paths reports metadata
only and grants no producer-byte credit. A failed original execution remains
failed. Every report retains trusted attestation UNVERIFIED and pre-Avalonia
BLOCKED. Public CI runs synthetic mutation controls in existing preflight/Main
steps; it does not receive private review packets or declare full acceptance.

## Owner gates and retained constraints

Initial mapping, material boundary/contract changes, concrete RLS policy/role
approval, product RLS activation, operational privilege changes, exceptions,
infrastructure activation and control relaxation require the specific approval
required by SA-02/04/05/06/08. An ordinary qualified merge does not supply it.
Preserve A-R2, #1036 activation, SEC-FND Disabled/Shadow-only, existing security
and performance structural gates, and the indefinite hardware-duration hold.
