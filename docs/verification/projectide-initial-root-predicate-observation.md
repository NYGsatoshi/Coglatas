# Actual initial-root Compiler predicate observation

This bounded #1034 slice connects the shared traversal-time predicate observer
to actual initial-root Compiler evaluations. It preserves the existing Compiler
result, logical operation accounting, legacy sorted Proof arrays and host
protocols. It does not reconstruct a tree from those arrays or replay an older
result to manufacture observations.

## Separate explicit compile and ownership

PureCoreCompiler.CompileWithPredicateObservation requires an immutable
CompilerPredicateObservationPolicy before execution. It returns a fresh
RecordedPredicateCompilation containing the actual final CoreCompilation and
a separately owned ProjectCompilerPredicateObservation. The wrapper excludes
Compilation from automatic JSON serialization. The existing CoreCompilation
shape and sole seven-parameter constructor remain unchanged.

The existing Compile and CompileWithPredicateCapture entry points keep their
original behavior. The new entry point does not implicitly opt into the older
Proof-array capture contract. Existing host request/response and client admission
types do not adopt this new record.

A fresh private collector observes the actual constraint and requirement-condition
evaluation calls after structural closure and operation-budget checks. Each
retained evaluation has its actual ordinal, rule/version, typed location and
original shared evaluation record. Started and completed evaluation counts are
separate; a cancelled call cannot acquire a completed result.

The collector links only actual emitted condition diagnostics to their actual
evaluation instances. A requirement's separate evidence-unavailable diagnostic
does not receive a condition graph. Successful evaluations do not invent a
diagnostic. Equal truth, ordered literal digests or diagnostic IDs are not
invocation or expression identity.

Completion binds the record to the actual final input/result after dependency
attachment and scoped publication. Only the same actual diagnostic objects
surviving final publication are selectable. A whole genuine older compilation,
equal-digest record or equal-ID diagnostic object cannot own the current read.

## Initial-root and regional evaluation differ

The record covers ActualInitialRootEvaluationOnly. Regional re-evaluations are
NotRecorded. Scoped version-two compilation can have no initial-root evaluations
and later produce actual regional false/Unknown diagnostics. Their selected
read reports NoObservedPublishedDiagnostic rather than borrowing a graph.
Other publication paths can replace diagnostic objects; ID/byte equality cannot
substitute for actual instance links.

This does not certify complete regional coverage, Compiler pass capture,
causal meaning, host session/attempt identity or intended current-run selection.
The existing paired Inspector still consumes its actually recorded legacy
arrays and leaves unadopted operator relationships unavailable.

## Bounded collection and explicit selected reads

Manifest is the default. ObservedOperators declares local aggregate limits:
at most128 evaluations,1024 actual visited nodes and1 MiB of original operator
bodies by default, with the existing32 MiB hard body maximum. Remaining node/body
capacity is passed into each shared evaluation before it runs. These are
provisional technical caps, not adopted retention, Pin, storage or device quotas.

The first evaluation/node/body gap withdraws the entire retained graph. Later
evaluations preserve original Compiler truth and operation accounting without
restoring a prefix. Cancellation or a noncompleted final Compiler execution
withdraws every graph. An earlier capture gap survives later resource exhaustion;
the actual Compiler execution/coverage/verdict/evidence state remains independent.

ProjectCompilerPredicateObservationSnapshot.Read defaults to metadata only.
AvailableOperators requires the exact wrapper, compilation, owned record and
actual published diagnostic instance. It distinguishes no recorded operators,
no observed published diagnostic, refused ownership and a smaller viewer budget.
Refused ownership reveals no original record metadata or diagnostic and labels
selection NotEvaluated. A capture gap labels selection NotRecorded; neither
asserts that the actual subject had no matching evaluation.

The explicit view rechecks original node lengths/digests and returns immutable
original operator frames plus the exact original derived evaluation hash subjects.
The latter contain ordered literal digests, not literal bodies. Operator-body
bytes and derived-frame bytes are counted separately; the viewer budget applies
to operator bodies. Total derived-frame size is bounded by the retained node/
evaluation caps. A smaller viewer budget withdraws both maps without changing
the original capture state. A cancelled read publishes no partial snapshot.
Byte copies and returned collections cannot mutate original frames.

Local formats coglatas.local-initial-root-predicate-observation/1 and
coglatas.local-initial-root-predicate-observation-snapshot/1 bind original
Source/context/compiler/input/result and selected diagnostic metadata. They
keep compiler run identity NotAllocated, run selection/authority/trust/eligibility
NotEvaluated, regional/causal meaning NotRecorded and retention NotAdopted.
They are not adopted artifact Registry or transport schemas.

## Measured validation

Both strict SDK10.0.403/runtime10.0.14 solution builds pass with zero warnings
and errors. Each OS passes74 focused,2407 complete ProjectIDE and12 architecture
cases without skips. Raw GUIDs and decoded names match across OSes; all2333 base
cases remain. All257 executed source inputs match both mirrors and repository
without normalization.

The regressions cover three-valued and nested AND/OR/NOT evaluation, actual
child order and short circuiting, ordered duplicate premises versus the older
distinct sorted array, four unchanged result goldens, requirement evidence
separation, scoped regional gaps, opaque foreign identities, aggregate caps,
actual resource exhaustion after two evaluations, cancellation, immutable maps,
exact viewer-budget boundaries and concurrent policy reuse.

The first full builds failed a new test helper's object-to-JsonNode conversion
on both OSes. Original source and logs remain. The correction uses the public
SourceJson parser; no Product behavior or assertion was weakened.

A separate public-API executable uses the actual full solution-built Domain
assemblies on both hosts. It executes58 cases under five policies,290 exported
vectors and3613 native checks per host. Original16626399-byte documents are
byte-identical, SHA-256
5d84ac26f9d316a33ca115fac1c0d5bda91e34a1845870668d928e6aaa5ed3c7.
An independent JavaScript verifier checks2735 original hash subjects,
324 operator nodes,82 original derived evaluation frames and24 short-circuit
nodes across82 available diagnostic views. Original canonical Source/metadata
subspans are hashed without reserialization. It also verifies two actual
regional diagnostics have no initial-root observation. Reference ownership is
tested by the native executable, not inferred from serialized digest equality.

A separate negative verifier reproduced two false absence labels in refused
and capture-gap reads. The correction distinguishes NotEvaluated/NotRecorded
from a genuine no-observed-published-diagnostic result; original frames and
failures are retained and native regression assertions cover both paths.

Static analysis, unchanged exporter compatibility, full review and exact-source
hosted gates are recorded in the PR before integration. No earlier frontend/
browser counts are borrowed as current-source executions.

## Remaining acceptance and ownership

The [separate owned local host](projectide-owned-operator-observation-host.md)
adds predeclared local session/attempt/receipt binding without changing this
Domain contract. No remote transport, Angular or Windows operator client
adoption, actual Avalonia acceptance, runtime endpoint, current authorization
decision, durable storage/Pin/export/redaction/deletion or retention adoption
is added. Public mid-evaluation cancellation timing is not claimed by the
pre-cancellation and actual resource-exhaustion tests.

Full #1034 and #782 remain OPEN. Permanent independent/hosted coverage remains
P0 #1959; runtime disclosure/revocation/redaction/deletion/retention remains
P0 #1966/#1932. Security/shared-CI implementation belongs to those owners under
the ProjectIDE-only scope. Preserve #1144 BLOCKED, #1128 SUSPENDED/NOT_EVALUATED
and Registry/Trust/Eligibility/RLS/Native/Production/Release holds.
