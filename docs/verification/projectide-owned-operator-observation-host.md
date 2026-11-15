# Owned local initial-root operator observation host

This bounded #1034 Product slice gives the actual initial-root Compiler operator
record a separate predeclared local host request, Manifest, attempt response and
owned recording receipt. The existing ordinary/output/Proof-array protocols,
Compiler result bytes, logical operations and paired Inspector are unchanged.
It does not reconstruct observations from an older result.

## Declare, execute and retain the actual pair

PredicateObservationCompilerHostRequest requires the immutable
CompilerPredicateObservationPolicy before execution. Its local execution digest
binds the old Core execution/materialization digest and all four capture policy
dimensions: mode, evaluation limit, node limit and body-byte limit. Manifest
remains the default. Client generation is bound separately by the exact request
instance and Manifest/response metadata; it does not change the old Core digest.

LocalPredicateObservationCompilerRecordingSession.Prepare validates the actual
input transfer and creates an owned declaration before execution. It retains
metadata, input/Source/context/compiler digests and the policy declaration,
without retaining a request or compilation graph. ExecuteAsync accepts only
the same owned Manifest and exact immutable request instance, revalidates the
transfer before reservation, reserves one run UUID and invokes the actual local
Compiler through LocalPredicateObservationCompilerHost. It returns a fresh
PredicateObservationCompilerRecordedInvocation with the response and receipt
constructed together. Host attempts and runs have distinct UUIDv4 identities.

The response is bound to both the actual request token and exact predeclared
Manifest token. A genuine older response with the same request, result and
observation digests cannot be attached to a new Manifest. IsFor(request,
intendedManifest) on the returned invocation checks the actual response/receipt
pair and intended Manifest together. Checking only a content digest or
response.IsFor(request) is insufficient for intended-run selection.

IsFor proves local object ownership and integrity, not current actor/resource
permission, session liveness, freshness of a selected client state or producer
trust. There is no transport deserializer or public receipt constructor. A
standalone direct host call can produce its actual response for a declaration;
only session execution creates the paired recording receipt.

## Explicit diagnostic reruns and bounded lifecycle

A diagnostic origin must be an actual receipt owned by the same local session
and old Core execution input. Policy or generation can change explicitly in a
new request/run; changed input or materializations cannot borrow the older
origin. The Manifest preserves the actual original run, Manifest digest, receipt
digest and attempt ID plus known ancestor run IDs. Neither origin linkage nor a
later successful run changes the original receipt or invents an earlier graph.

The session permits at most32 reserved executions and31 diagnostic ancestors.
These provisional local technical caps do not adopt storage/retention/Pin quotas.
Duplicate UUIDs, foreign declarations/origins, cycles, lineage overflow and
capacity exhaustion are refused. Concurrent duplicate execution reserves only
one actual attempt. Preparation stores no unbounded session list of declarations.

Host Completed means an actual Compiler envelope exists. Compiler execution,
coverage, verdict and capture sufficiency remain separate. Resource exhaustion
can produce a completed host response with an interrupted observation. Cancelled
host attempts have no Compiler result, observation digest or observation metadata.
A retry is a new declared run/attempt linked to its actual cancelled origin.
The host and session recheck cancellation before returning the paired receipt.
Deterministic mid-evaluation or disposal-race timing is not claimed by the
pre-cancellation tests.

Disposal prevents further preparation/execution and clears session execution
metadata. It does not physically erase previously returned caller-owned frames.
The receipt retains only original observation metadata/digests, not the response,
request, compilation graph or operator body maps. Request.Core and
response.Execution are excluded from automatic JSON serialization. Explicit
owned Domain snapshot reads still control access to the original operator and
derived evaluation frames.

## Measured validation

Both SDK10.0.403/runtime10.0.14 strict solution builds have zero warnings/errors.
Each native OS passes41 focused host/session cases and2448 complete ProjectIDE
cases and12 architecture cases without skips. Raw GUIDs and decoded names match
across OSes and all2407 prior identities remain. All261 source inputs match both
executed mirrors. The15 earlier deterministic exporters remain byte-identical
to original assets on both OSes;20 actual legacy array-host calls per OS retain
original hashes and origin links. Current-source hosted/review gates are recorded
before integration; no earlier hosted result is borrowed.

Initial five-project InspectCode2026.2.2 reports107 overall warnings including
two new test statement-layout warnings. Separating using declarations from the
preceding statements preserves behavior and assertions; final static analysis
and native test evidence are recorded in the PR before integration.

The initial full builds on both hosts failed two new test compilation mistakes:
an ambiguous target-typed overload argument and an incorrect old host class
name. Correcting the explicit request type and existing LocalNetCompilerHost
name preserves every assertion. Initial source and failed build logs remain.

A separate public-API executable invokes the actual solution-built Domain and
Application assemblies. Each OS executes4 unchanged synthetic cases under3
policies,12 vectors,384 actual host attempts including12 pre-cancelled outcomes,
372 actual Compiler invocations and1437 native assertions. Each bounded chain
contains32 actual attempts; no fabricated origin wrapper fills the lineage.

An independent JavaScript audit hashes8676 original frames,120 operator bodies,
120 original derived evaluation frames and744 actual origin links across both
hosts. It verifies exact Manifest/request/policy/attempt/receipt/result/observation
bindings and original body lengths without reserializing hash subjects. All
deterministic request, policy, result, observation and selected-view frames match
byte for byte across OSes. Each full document is17242968 bytes; actual random
run/attempt/session identities make the complete documents different. Neither
normalization nor a byte-identical whole-receipt claim is used.

## Remaining acceptance and ownership

The existing Angular and Windows Inspector continues to consume the older
actually recorded Proof arrays. Adopting these operator frames into both
clients, intended current selection/lifecycle checks and actual Avalonia UI
acceptance remain separate #1034/#782 work. Regional evaluations and earlier
intermediate states remain NotRecorded. The Domain operator snapshot still
declares no host-run identity; this new local wrapper supplies its own actual
owned invocation metadata without altering that Domain format.

No HTTP/job endpoint, runtime current-access grant, durable storage, Pin,
redaction, revocation, export, deletion or retention policy is adopted. Permanent
independent/hosted selectors and raw fixture parity remain P0 #1959; runtime
current disclosure/revocation/redaction/deletion/retention remain P0 #1966/#1932,
and operational cleanup remains P0 #1974. Security/shared-CI changes belong to
those owners under the ProjectIDE-only scope. Full #1034 remains OPEN; preserve
#1144 BLOCKED, #1128 SUSPENDED/NOT_EVALUATED and Registry/Trust/Eligibility/RLS/
Native/Production/Release holds.
