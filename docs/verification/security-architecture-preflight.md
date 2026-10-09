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
definitions/results, an explicit 80-case execution catalogue, exact checkout,
same-candidate build stamp, assembly/result hashes and observed environment.
It rejects identity/counter/timestamp disagreement and records missing/skipped
methods as UNVERIFIED, failed cases as FAIL and aborted cases as ERROR.
Case parameters, machine/user names, raw logs, paths, credentials and bodies are
excluded. Independent metadata mutation controls run in existing CI preflight.

The existing backend/Main jobs capture these sanitized observations without
rebuilding the product or changing Required Check identities. Unselected PR
coverage is explicitly UNVERIFIED. A build stamp/job identity is self-reported
metadata until independently reconciled with the trusted producer/run/artifact;
the receipt always records that prerequisite and pre-Avalonia BLOCKED. Receipt
presence and representative passing counts cannot close #842/#614 or establish
all-adapter completion. Changes to this execution catalogue need review-visible
coverage accounting; silent removal is not completion.

## Owner gates and retained constraints

Initial mapping, material boundary/contract changes, concrete RLS policy/role
approval, product RLS activation, operational privilege changes, exceptions,
infrastructure activation and control relaxation require the specific approval
required by SA-02/04/05/06/08. An ordinary qualified merge does not supply it.
Preserve A-R2, #1036 activation, SEC-FND Disabled/Shadow-only, existing security
and performance structural gates, and the indefinite hardware-duration hold.
