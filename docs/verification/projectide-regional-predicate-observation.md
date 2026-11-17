# Actual regional Compiler predicate observation

This partial #1034 Product slice adds a separate opt-in pure Compiler entrypoint.
It records the regional rule evaluations already performed by the normal full
Compiler. It does not replay an older result or add extra regional executions.
Existing initial-root observation/capture, ordinary Compiler and host/client
protocols retain their existing contracts.

## Declare and observe one actual compilation

PureCoreCompiler.CompileWithRegionalPredicateObservation takes an immutable
CompilerRegionalPredicateObservationPolicy before execution. Manifest is the
default. A fresh private collector spans the actual dependency-analysis regional
calls in that invocation; reusing the policy shares no mutable collector state.

The aggregate technical caps follow the existing primitive:128 evaluations,
1024 visited nodes and1 MiB operator bodies by default, with the existing32 MiB
hard body maximum. Existing closure admission bounds regional calls to the root
plus at most32 admitted snapshots. These are provisional local technical bounds,
not adopted storage, retention, Pin, server, device or distribution quotas.

The result is a RecordedRegionalPredicateCompilation with the actual final
CoreCompilation and a separately owned ProjectCompilerRegionalPredicateObservation.
Automatic JSON serialization excludes the compilation graph. The observation
binds the original root input/result digests, actual regional binding/Source,
derived regional input/result digests and actual evaluation ordinals. A derived
binding input is not the external snapshot's original Compiler input.

CompletedRegionCalls counts returned regional Compiler calls, including non-success
envelopes; it is not a count of successful regional execution or complete
coverage. Each call metadata retains its actual execution, coverage, verdict
and evidence-complete axes. Region metadata preserves the actual call order. Global evaluation ordinals span
these calls; operator ordinals remain local to an actual shared evaluation.
Neither observed order nor matching content hashes asserts causality or invocation
identity. Equal predicates in two regions stay separate actual evaluations.

## Root-only and regional ownership remain separate

The initial-root compile is not observed by this entrypoint. Existing root-only
formats still label regionalEvaluation NotRecorded and cannot borrow these
records. An actual initial-root failure with no regional call remains unrecorded
here; this does not assert that the Compiler performed no evaluation.

Each retained entry is assigned using the actual call's private collector ordinal
range and actual binding/compilation object pair. Its diagnostic links are the
exact diagnostic objects emitted by that regional evaluation and retained in the
actual published regional receipt. Equal contexts, IDs, result digests or a whole
genuine older compilation cannot substitute for this ownership.

A root summary of a dependency diagnostic can be a newly constructed object.
This API does not map those summaries back to a graph. An explicit read needs the
same wrapper, whole compilation, record, actual current CompilerDependencyRegion
and actual diagnostic from that region. Foreign records/regions/diagnostics and
root summaries are refused without original record, region or diagnostic metadata.

The Requirement evidence-unavailable diagnostic has no condition graph. Unknown
conditions retain their actual evaluation metadata but do not invent a condition
diagnostic. False conditions can expose only their actual condition diagnostic's
operators. Candidate evidence, eligibility and current access remain separate.

## Capture, result and viewing bounds are distinct

The first evaluation/node/body capture gap withdraws the entire retained graph.
Later evaluations preserve original Compiler truth and accounting without
restoring a prefix. Interrupted or resource-exhausted final Compiler execution
withdraws every graph, while an earlier independent capture gap keeps its own
reason. Capture metadata publication overflow becomes MetadataBudgetExceeded
without replacing the actual Compiler result. Cancellation observed while
publishing capture metadata also withdraws the graph without rewriting a
completed original Compiler fact.

ProjectCompilerRegionalPredicateObservationSnapshot.Read defaults to Manifest.
AvailableOperators is an explicit read of the original shared operator bodies and
derived evaluation frames for an actual owned regional diagnostic. It rechecks
original hash subjects, keeps operator and derived-frame byte counters separate,
and applies a smaller viewer body budget independently of capture. A cancelled
read returns no partial snapshot. Owned immutable maps and detached byte copies
cannot rewrite the original record.

Manifest/capture gaps label selection NotRecorded; ownership refusal labels it
NotEvaluated. NoObservedRegionalDiagnostic means no observed link to that actual
diagnostic, rather than absence of all regional evaluations. The whole original
record metadata remains an explicit owned property and is excluded from automatic
snapshot JSON duplication; local Data binds its digest.

## Qualification and remaining ownership

This is an isolated Product candidate until exact-source builds, raw inventories,
full ProjectIDE/architecture checks, old root-only result/record controls,
cross-host qualification, static analysis and full technical review complete.
The first broader120-case execution retained118PASS/2FAIL from new expectation
mistakes: Unknown Requirement conditions emit no condition diagnostic, and the
Version1 external-closure guard suppresses initial-root evaluation. Both original
failures remain; corrected controls assert the existing actual contract.
The first128-case stress execution retained123PASS/5FAIL: two invalid flat
Predicate arities were rejected before evaluation and three1m-operation fixture
profiles exhausted the existing Compiler budget before regional calls. Actual
Compiler diagnosis measured approximately2.7m/4m operations for those inputs.
Original1m refusal controls remain; capture-ceiling cases use the existing
permitted10m declared profile, and bounded binary ASTs isolate the aggregate
1024-node ceiling. Product defaults, hard processing/capture limits and gates
remain unchanged.
Initial partial qualification and retained failed test expectations are recorded
in the PR evidence; unexecuted checks are not PASS.

Initial four-project InspectCode reported107overall/2changed: a disposable
closure capture and adjacent using declaration in the cancellation fixture.
A private supplier owns its cancellation source through complete enumeration;
all cancellation, disposal and original-fact assertions remain. The original
static result is retained. The ordinal-range end is labelled as started
evaluations, independently of completed evaluation counts.

The two local formats coglatas.local-regional-predicate-observation/1 and
coglatas.local-regional-predicate-observation-snapshot/1 are provisional local
representations. They allocate no host run, claim no producer trust or evidence
eligibility and adopt no artifact Registry or transport schema. Existing host
session/receipt protocols, paired clients and actual Avalonia remain unchanged.
Runtime query/open/export authority, revocation, redaction/deletion/retention,
Pin and independent Security acceptance remain separate under existing P0#1966,
#1932 and applicable CI responsibilities. No Security/shared CI change is made.
#1034/#782 stay OPEN; #1144 BLOCKED, #1128 SUSPENDED/NOT_EVALUATED and
Registry/Trust/Eligibility/RLS/Native/Production/Release HOLDs remain.
