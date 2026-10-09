# Pre-Avalonia Security Foundation verification

Workstream: #1117 through #1122, in dependency order, one agent and one protected
PR at a time. This record covers the narrow Disabled/Shadow foundation only.
It is not a production intrusion-prevention or whole-product release guarantee.

## Final qualification and exact Main

The final exact Main SHA, SEC-FND-06 implementation PR/normal merge SHA, executed
test totals, workflow/run/attempt IDs and final disposition are recorded in the
[SEC-FND-06 final qualification record](https://github.com/NYGsatoshi/Coglatas/issues/1122#issuecomment-6074411767).
That stable record is Pending until all applicable checks on the actual merged
Main pass. It supplies the final immutable commit identity for this document,
whose own merge SHA cannot be embedded before its commit exists. An implementation
candidate, configured workflow or draft specification is not completion evidence.

Qualified starting Main for SEC-FND-06:
`82f37c6929d1f749a3ecb5d0ed99bdb184293b4a`, Main CI 37881143243 attempt 1
PASS; 54 completed checks, 50 success, four existing conditional skips. All six
required app-15368 checks passed. #1117–#1121 are closed with actual evidence.

| Issue | Normal implementation/repair PRs | Final qualifying Main | Main run / evidence |
| --- | --- | --- | --- |
| #1117 | #1132 merge `a6e615905dba0fd2df77fafa736c132b5f18d7a9`; #1133 merge `de577d5e3534f510a61dfc746c0162c604eb1e8f` | `de577d5e3534f510a61dfc746c0162c604eb1e8f` | 37804238175, full attempt 3; [completion](https://github.com/NYGsatoshi/Coglatas/issues/1117#issuecomment-6065340779) |
| #1118 | #1134 merge `035a54f404878223fe4796aa403553d044b201fa`; independent fixture #1135 merge `a271599a9cca2f2d3c4ad1f99c4648f17a3b060f` | `a271599a9cca2f2d3c4ad1f99c4648f17a3b060f` | 37838083775, attempt 1; [completion](https://github.com/NYGsatoshi/Coglatas/issues/1118#issuecomment-6068755642) |
| #1119 | #1137 merge `c131492c12773206b4295d49cc8a494ea06b1911`; #1138 merge `7e357885c4637584d1467a387e13726d5c542dba`; independent pagination #1139 merge `35e541436222654b5ee345082ec82487b6d323b5` | `35e541436222654b5ee345082ec82487b6d323b5` | 37858309449, attempt 1; [completion](https://github.com/NYGsatoshi/Coglatas/issues/1119#issuecomment-6071466871) |
| #1120 | #1140 merge `0f436a88b30f0ea33f5f444c27b5a55ec6832aa5`; #1141 merge `1af720b77f324b566bf633572740fc97994184c4` | `1af720b77f324b566bf633572740fc97994184c4` | 37871997058, attempt 1; [completion](https://github.com/NYGsatoshi/Coglatas/issues/1120#issuecomment-6073067197) |
| #1121 | #1142 merge `23f09b53424516e5385684e929c8cec935bf5d8a`; #1143 merge `82f37c6929d1f749a3ecb5d0ed99bdb184293b4a` | `82f37c6929d1f749a3ecb5d0ed99bdb184293b4a` | 37881143243, attempt 1; [completion](https://github.com/NYGsatoshi/Coglatas/issues/1121#issuecomment-6074365011) |
| #1122 | [PR #1153](https://github.com/NYGsatoshi/Coglatas/pull/1153) merge `7e2ed42b7b2caf808a1e8f19eae4e10f077ce68d`: authorized read, bounded diagnostics and exit evidence; subsequent qualification repair recorded in the linked final record | [Final exact Main record](https://github.com/NYGsatoshi/Coglatas/issues/1122#issuecomment-6074411767) | Same record; Pending grants no PASS credit |

The independent initial Main repairs are #1129 (`6843daf7165f0ce281fbe0300dc132bfbe3586be`),
#1130 (`64c070da52928d8066e0236e058195c4d434cbdc`) and #1131
(`28e2b570def8a646625ada1add4f794e421bd6d8`). They repaired exact wire-query
validation, realtime recovery and owned successful scanner-response attribution.
No Security lane, mandatory check, authorization or protection rule was weakened.
Original failed runs, artifacts, forward repairs and exact qualified commits are
retained in the linked issue/verification records.

SEC-FND-06 Main CI 37888563628 attempt 1 failed before tests when NuGet package
downloads reset their connections. A full unchanged-SHA attempt 2 restored and
built successfully. Both full Qodana inventories then reported three new
`UnusedParameter.Global` findings on ISecurityCurrentContextProvider parameters
(24 current, unchanged budget 21). The repair extends the reader regression to
verify the exact Project, historical resource and cancellation token forwarded to
the independent host. It changes no production contract or quality budget. The
original SARIF artifact 11598611809 was downloaded and its SHA-256 verified as
`807ad5f56979d11c4586079f338f5b9b6a540d8e57679077cb86ec582e7ec516`.
Passing individual lanes on that failed candidate do not qualify its whole Main.

## Contracts, binding and evaluation

Domain defines immutable SecurityDecisionOutcome, SecurityEvaluationStatus,
SecurityEnforcementMode, SecuritySubjectRef, SecurityOperationRef,
SecurityResourceRef, SecurityPolicySnapshot, SecurityCompilerProvenance,
SecurityEvaluationRequest, SecurityDecision, SecurityRuleResult and
SecurityAnalysisSummary. Outcomes Allow/Deny/Unknown/Quarantine are separate
from execution Pending/Completed/Failed/Cancelled/TimedOut/NotExecuted.
Unknown means insufficient evidence; Quarantine is an integrity anomaly;
Deny requires an applied violation. Failures do not fabricate a decision.

Bindings reuse #905 SourceRevisionContext, ProjectIdentities, CanonicalJson,
SourceJson and ContentDigest. Domain `coglatas.security-binding/1`, schema 1
binds Tenant/Project/Branch/Revision/Candidate, complete canonical Source/context,
claimed and independently supplied policy content identity, compiler version/build
and optional actual commit. Property/dictionary order cannot affect the digest.
Evaluation ID, timestamps, database IDs and mode are excluded. Same policy version
with different content differs. Missing compiler evidence remains absent. Digest
equality grants no authorization, verified by negative tests.

The pure coordinator orders unique rules by ordinal RuleId and preserves all
results/partial execution. Initial rules are SEC-FND-REVISION-BINDING,
SEC-FND-POLICY-BINDING and SEC-FND-COMPILER-PROVENANCE. Known contradictions
quarantine; missing host evidence stays Unknown. Successful decisions aggregate
Quarantine > Deny > Unknown > Allow. Exception/cancellation/cooperative timeout
remain execution states. No Source mutation, database I/O, Merge or permissions
occur inside the coordinator. A self-reported policy digest is not authenticity.
Initial rules do not invent a business-policy Deny implementation.

## Persistence and PostgreSQL evidence

`security_evaluation_runs` and `security_evaluation_rule_results` implement
Pending → terminal once. Binding identity is immutable; final result and ordered
rules cannot be replaced. Re-evaluation creates a new ID. Scoped keys, indexes,
checks and row-lock/trigger guards enforce isolation and competing terminalization.
The authorized adapter rejects unrelated dirty state/caller transactions, awaits
its own transaction, checks current permission before disclosure/commit and
detects cached authorization facts revoked in an already-used context.

Only closed safe identity metadata, digests, UTC times, finite reason/status/outcome
and authorized ordered rules are persisted. Full Source, context extensions,
policy contents, credentials, tokens, keys, decrypted data and exception text
are excluded. Append-oriented does not mean infinite retention. No new TTL,
pin quota or deletion capability is invented; #1034 retains that authority.

Actual disposable PostgreSQL 18.6 was used locally and in CI. Migration
`20261009000906_AddSecurityEvaluationRecords` upgraded 73 registered migrations
to 74 with existing Projects, downgraded only Security tables to 73 while retaining
the three seeded Projects, then upgraded again to 74 with no pending migrations.
Provider regressions cover isolation/known IDs/revocation, immutable identity,
concurrency, ordered rules, redaction, failed transactions and re-evaluation.
No production database migration was applied. EF InMemory or mocked UI results
are not provider evidence. See [the persistence record](security-foundation-persistence.md).

## Shadow integration and authorized read

Integration is **SEAM_ONLY**. The future #905 production compiler call site is
**NOT_INTEGRATED**. A deterministic host proves the minimal awaited
IRevisionSecurityGate with immutable input → evaluation → durable record →
non-authoritative summary → existing decision. Both existing allow/deny decisions,
required-check/conflict/head metadata and serialized bytes remain unchanged for
Disabled, all four Shadow outcomes and execution/recording failures. Disabled
does not invoke evaluator/store. Recorded means this call's final result committed;
Pending/AlreadyTerminal/unavailable/failed persistence is not a durable final receipt.
Cancellation cleanup is cooperative and awaited; no fire-and-forget writer exists.

`ISecurityEvaluationReader` is an Application contract; no unsettled public
Revision HTTP API is frozen. Reads reuse the store's current authentication and
Tenant/Project permission checks, then repeat them after awaited independent host
resolution. Known IDs/digests confer no access; revocation during resolution returns
no record. Projection includes identity/context, policy/compiler identities,
mode/status/outcome/reason, UTC times, frozen safe rules and freshness.

`ISecurityCurrentContextProvider` is the minimal future #905 host seam. Its default
implementation supplies no evidence. **Unverified** is therefore the production
default. **Current** requires complete matching claimed/independent Source,
policy and compiler identities; **Stale** identifies known scope/context/input/
policy/compiler differences. Complete context digests bind unknown extensions,
proposal/candidate/base/head and scenario identities. A different authorized
viewer is allowed; the original subject is historical metadata. Subject Tenant
remains scoped. Missing evidence or matching nulls cannot certify currentness.
Current is observation metadata, never authority or a replacement for #906 checks.

## Observability

The singleton SecurityEvaluationDiagnostics follows existing Interlocked
process diagnostics. Snapshot fields count total attempts; Failed, Cancelled and
TimedOut execution separately; Allow/Deny/Unknown/Quarantine only for Completed
decisions; exceptional persistence failure; binding mismatch observations; and
requested Disabled/Shadow/Enforce modes. Requested mode differs from host-effective
mode; rejected Enforce requests are counted before the guard and never execute.
Unavailable/competing-terminal persistence remains visible in recording status
and is not mislabeled as an exceptional database failure.

Counters have fixed cardinality, are process-local and count attempts/observations,
not unique durable rows. They have no Tenant/User/Revision/Evaluation labels,
payloads or exception strings. No new monitoring package, public diagnostic route
or ordinary secret-bearing log is added. Process restart resets counters.

## Exit-test mapping and execution

| Exit requirement | Executable evidence |
| --- | --- |
| 1 Disabled unchanged | RevisionSecurityGateTests legacy decision matrix, default/effective options |
| 2–5 Shadow Allow/Deny/Unknown/Quarantine nonblocking | Same matrix for both baseline decisions; safe durable provider seam |
| 6 Evaluation exception nonblocking | Coordinator exception tests and legacy matrix; Failed counter |
| 7 Persistence failure nonblocking | Create/terminal failure matrix and real PostgreSQL forced terminal-write rollback |
| 8 Cross-Tenant denial | Real store and authorized-reader known-ID scope negatives |
| 9 Cross-Project denial | Same tests with an otherwise-authorized different Project |
| 10 Revoked permission denial | Already-used store revocation plus committed revocation during awaited host resolution |
| 11 Stale/foreign rejection | Reader Tenant/subject Tenant/Project/Branch/Revision/Candidate/input/policy/compiler/extension/base/head tests; missing evidence Unverified |
| 12 Enforce rejected | All-environment startup validation, gate constructor/request guards, counted rejected request |
| 13 #906 controls unchanged | Architecture excludes mutation/Merge/permission services; unchanged legacy decision bytes and existing authorization/Functional CI |
| 14 No secret leakage | Closed durable identity/canary tests; safe read/rules/summary/counter serialization |
| 15 Real PostgreSQL migration | 73→74→73→74 provider regression and actual configured PostgreSQL 18.6 runs |
| 16 Existing Backend/Security/Functional PASS | Final exact-Main qualification record; no new required context |

Focused checks use Release `dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj
--no-restore --filter 'FullyQualifiedName~Coglatas.Tests.ProjectIde|FullyQualifiedName~SecurityEvaluationPersistencePostgreSqlTests'`.
Provider runs explicitly set POSTGRES_TEST_CONNECTION_STRING and opt into the
migrated template. The first authorized-read/provider/gate selection passed
90 tests, zero skipped. The expanded ProjectIDE/provider selection passed 302,
zero skipped; it includes candidate base/head identity and diagnostics cases.
Startup configuration cases are included in the full solution selection.
Subsequent full totals are in the final qualification record. Full backend/architecture runs use
`dotnet test Coglatas.slnx -c Release --no-restore` with actual PostgreSQL.
Frontend/UI and licensed real-stack journeys require actual final Main results.
Full Qodana, CodeQL and Functional gates require the same qualification.
Runtime scans cover deep Schemathesis and three authenticated ZAP roles.
Filesystem vulnerability scanning and secret scanning also require actual final
Main evidence. Conditional existing UI skips remain explicitly qualified.

The original publication-readiness run 37885603246 failed on a single ordinary
scanner-name phrase in initial commit `45f044844377b49a42f6f8f86b223cfbf7a2dac2`,
document line 166. Pinned Gitleaks 8.24.3 reproduced that generic-api-key false
positive. The prose is rewritten and only the exact historical commit/path/rule/
line fingerprint is excepted, following the existing reviewed convention; future
content remains scanned. Redacted failure artifact 11595957132 was downloaded
and SHA-256 verified as
`611dbe2cda50d720a6da97cf90b8a0f9ee47311c3ce3f26cc17e0b2872443c9c`.

## Governance, dependency audit and limitations

Ruleset 24643016 stays active with no bypass. Existing required checks remain
build-test, frontend-test, security-scan, publication-readiness, functional-fast
and performance-fast (app 15368), with strict base/current-head validation and
resolved review threads. Normal merges specify expected head SHA. No new required
workflow, package, Avalonia dependency, lockfile or deployment relaxation is added.
Domain remains independent of Application/EF/HTTP/UI; the pure coordinator has
no I/O/ambient authority; the adapter boundary excludes mutation/Merge services.
Current source, provider tests and architecture tests establish these boundaries.

The fixed owner S01–S14 choices remain unchanged (S04/S11/S12 B, others A).
Original option wording is unavailable; no meaning is invented or reopened.
Private Coglatas-Spec Draft #86 remains a proposal until independently ratified.
Accepted A-R2 authority is preserved. Future external Revision API, retention/
generic evidence and Enforce decisions remain HOLDs; safe implementation uses
Application-first read, minimal storage and rejected enforcement. No HOLD is
claimed resolved by specification creation or issue closure.

Future enforcement needs separate approved implementation and rollout. Deferred:
Semantic Firewall, SEC-A/B/C, policy DSL/compiler, KMS/HSM/key lifecycle,
field/database encryption, cumulative disclosure budget, supply-chain semantic
verification, runtime quarantine and all-API enforcement. Full #905 compiler,
full #906 Merge engine, new Avalonia UI, unrelated Angular/billing work are outside
this foundation. Cooperative cancellation cannot forcibly stop an adapter ignoring
its token. Historical Current is a checked observation, not continuous invalidation
or proof against a subsequent permission/context change.

#1127 numerical assurance is SUSPENDED / NOT_EVALUATED; performance-fast PASS
verifies that suspension contract, not API/DB latency. #1128 reactivation remains
mandatory before production and #1046 stays a separate performance PR. Remaining
#804 dependency work and #1034 generic evidence/retention are separate workstreams.
Final open PR/deferred issue audit is included in the final qualification record.
Stop after foundation qualification; no new Avalonia/ProjectIDE implementation
is authorized by this completion.
