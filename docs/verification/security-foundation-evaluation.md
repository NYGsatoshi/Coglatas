# Security Foundation evaluation — SEC-FND-03

## Scope and authority

This #1119 candidate starts from qualified exact Main
`a271599a9cca2f2d3c4ad1f99c4648f17a3b060f`. #1117 and #1118 are closed with
normal-merge, acceptance and exact-Main evidence. #1119 owns pure evaluation,
not storage, HTTP, telemetry, production compiler integration or enforcement.
#905, #906 and #1034 retain Source/compiler, authorization/Review/Merge and
shared evidence/retention authority respectively. The private specification
proposal remains Draft; original owner-option meanings are not inferred.

## Input, rules and decisions

`ISecurityEvaluationCoordinator` consumes `SecurityBinding`, which contains
exactly one immutable `SecurityEvaluationRequest` plus independently supplied
host evidence from #1118. The host remains responsible for loading applicable
authorized evidence; a constructed value or self-reported digest cannot
authenticate its own authority. No ambient repository or permission lookup
exists inside the engine.

`ISecurityRuleEvaluator` supplies a bounded stable Rule ID and cooperative
evaluation. Registration is captured once, sorted ordinally and rejects null,
invalid or duplicate IDs. Returned IDs must match the captured registration.
The Application container registers exactly these three rules:

| Rule ID | Completed decision basis |
| --- | --- |
| `SEC-FND-REVISION-BINDING` | Compare declared subject Tenant/resource context/input digest against each available claimed/host Source. Known scope, Revision/Candidate/context or digest contradiction quarantines; missing required Source is Unknown; verified bindings Allow. |
| `SEC-FND-POLICY-BINDING` | Compare claim set/version/schema/digest to the host snapshot derived from actual canonical policy bytes. Missing evidence or matching unsupported schema is Unknown; known contradictory identity/content quarantines; matching schema 1 bindings Allow. |
| `SEC-FND-COMPILER-PROVENANCE` | Compare supplied version/build and optional exact Git identity to independent host provenance. Missing provenance or a one-sided unverified commit is Unknown; known version/build or mutually supplied commit contradiction quarantines; matching known version/build Allow without inventing an optional commit. |

These rules verify bindings and provenance, not all Source semantics or a Policy
DSL. No speculative business/security Deny rule is introduced. Deny aggregation
is tested through an evaluator fixture representing an explicit applicable
violation. Hash comparison uses the existing SHA-256 value identity; arbitrary
digest-object extensions are not new identity fields, whereas preserved Source
context extensions remain part of the context comparison.

Only when every registered rule completed is the final outcome aggregated:
**Quarantine > Deny > Unknown > Allow**. All individual results remain ordered.
No empty rule set or unexecuted coverage can be advertised as proven Allow.
Disabled returns NotExecuted and executes no rule. Enforce is rejected before
any evaluator executes; the existing startup guard remains unchanged.

Finite `SecurityReasonCode` values retain stable safe basis. Revision, Policy and
Compiler verified/missing/mismatch codes correspond to their Issue reason-code
families; aggregate `BindingsVerified`, `PolicyViolation`, `MissingEvidence` and
`BindingMismatch` explain qualified outcomes. `EvaluationFailed`,
`EvaluationCancelled`, `EvaluationTimedOut`, `RuleExecutionFailed` and
`RuleNotExecuted` explain execution and coverage. They contain no free text,
exception message, raw input, credentials or unrestricted evidence JSON. This
slice does not introduce a new serialized public reason-code API or evidence DAG.

## Failure, cancellation and deadline

Evaluator exception, null result, wrong Rule ID or returned Pending becomes a
typed Failed execution with no decision. Real earlier results are preserved;
the interrupted rule receives a finite execution reason and remaining rules
stay NotExecuted. A returned terminal execution state also remains distinct
from completed policy decisions. Earlier Deny/Allow cannot turn a failed run
into a qualified final decision.

Caller cancellation has priority over the processing deadline. The coordinator
uses a linked cooperative token and a default five-second execution budget,
configurable by explicit constructor input for a host/test. This is a processing
guard, not a numerical performance or retention promise. Results returned after
the token/deadline expired are not qualified as Allow.

Every evaluator is awaited through completion and cleanup. No Task.Run,
fire-and-forget or detached timeout task exists. A custom evaluator that ignores
cancellation cannot be forcibly preempted by this in-process seam; this is an
explicit limitation. The registered built-in rules are bounded pure comparisons.
Persistence and the nonblocking operational mapping belong to #1120/#1121.

## Verification and limitations

The first focused run passed 66 new tests. Two further fixtures cover a mutable
registration identity and a result returned after timeout; the final focused
ProjectIDE suite passes 196 tests including 68 new coordinator/rule cases, with
zero failures/skips. All 11 architecture checks pass, including a new executable
engine rule forbidding transport, persistence, platform I/O, UI and ambient
Application services. Existing Source/Domain boundaries remain enforced.

Coverage includes every outcome, mixed precedence, repeatability/order,
duplicates/malformed registrations, Disabled/empty/Enforce, all bound scope and
Candidate identity changes, unknown context preservation, semantic digest
comparison, missing/unsupported/self-declared policy, contradictory policy
content/version/schema, absent/contradictory compiler identity, partial failure,
cancellation, cooperative timeout/cleanup, late result rejection and unchanged
immutable Source/policy content.

The full backend suite passes 1,959 tests with zero failures/skips in 7m3s,
with `POSTGRES_TEST_CONNECTION_STRING` and migrated-template reuse explicitly
enabled against the owned disposable PostgreSQL 18.6 container (73 existing
migrations). The 11 architecture checks also pass. No production database is
used. No migration, package, lockfile, generated hosted asset or production
database change is required. An optional local InspectCode invocation was
rejected before analysis; it is not reported as an inspection pass. Applicable
PR and exact-Main Backend/Security/Functional/CodeQL/ReSharper/Qodana evidence
must qualify before normal merge/closure. Numerical performance remains
SUSPENDED / NOT_EVALUATED under #1127; #1128 is separate. No production-wide
access-prevention, completed compiler, overall release readiness or new
Avalonia/ProjectIDE implementation is claimed.

The first PR inspection at head `85a53deccab5254674863e88f78554fee2ee7107`
failed on three `AccessToDisposedClosure` test callbacks and one redundant
explicit default argument. Its SARIF artifact 11579156546 was downloaded and
SHA-256 verified as `bb36ac652cf0a3fc108d711e098baf84d291c2d5f688443c5d9bb8e03b49ead5`.
The callbacks now receive a narrow cancellation action, with their owning
source retained until the awaited evaluation finishes; the redundant default
argument is removed. No suppression, quality budget or production behavior
changes. The full backend run preceded this test-only cleanup; the complete
196-test ProjectIDE suite passed again afterward with zero failures/skips.
Final exact-head inspection remains required.
