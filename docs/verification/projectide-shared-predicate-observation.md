# Actual shared predicate evaluation observation

This bounded Pure Core primitive advances #1034 by observing the actual shared
PurePredicate traversal. It records visited Boolean operators, child evaluation
order and skipped operand counts while that evaluation runs. Existing Compiler
Proof arrays remain a separate contract: they cannot reconstruct this tree or
gain observations after execution.

## Explicit evaluation and ownership

The existing Evaluate() method and PredicateEvaluation serialization remain
unchanged. EvaluateWithObservation requires an immutable capture policy before
execution and returns a fresh RecordedPredicateEvaluation. Manifest is the
default. ObservedOperators explicitly requests original operator frames.
The original typed literal objects and their evaluated order remain unchanged
across capture policies.

The private collector belongs to one actual call. The record requires the exact
invocation and input objects; equal input bytes or a whole genuine older record
with identical digests cannot own the current read. No public receipt attachment,
ambient callback, mutable observer, result cache or fabricated rerun origin is
introduced. This standalone call allocates no Compiler run/session/attempt and
does not select the intended current host run.

Each node has its actual entry ordinal, operand-index path, operator kind, truth,
visited child ordinals, skipped operand count and, for a visited literal, its
original premise ordinal and digest. The new local domains are
coglatas.observed-predicate-node/1, coglatas.evaluated-predicate-literal/1 and
coglatas.local-predicate-evaluation/1. The existing coglatas.predicate/1 domain
still hashes the original predicate bytes. The exact derived evaluation frame
contains truth and ordered original literal digests, without nesting literal
bodies. EvaluationFrame retains that original hash subject for explicit
in-process verification and is excluded from automatic JSON serialization.
It is not a legacy PredicateEvaluation or Compiler-result hash subject.

## Capture, cancellation and reads

The opt-in input must fit the existing default SourceJson profile, 4 MiB and
depth64. A valid legacy predicate using deeper opaque Unknown data can still
evaluate through Evaluate(); the separate observation entry point explicitly
refuses that unsupported input profile. It does not mutate or reinterpret it.

Local provisional capture limits are 1..1024 visited nodes and 1 MiB of original
operator bodies by default, with a 32 MiB hard body maximum reused from the
existing output snapshot. A parsed tree may exceed capture capacity: evaluation
continues with unchanged truth and original premises while the first capture
gap atomically withdraws every node body. There is no recoverable prefix or
later budget repair. These bounds are technical limits, not adopted runtime
retention or device capacity decisions.

Cancellation produces no evaluation result, result digest, evaluation frame or
partial operator graph. Execution completion and capture sufficiency are
independent: a later cancellation preserves an earlier node/byte capture gap
while marking execution Cancelled. Without an earlier gap, cancellation records
CaptureInterrupted. The original failed component transition probes are retained;
their corrected checks do not establish public concurrent-cancellation timing.

PredicateObservationSnapshot.Read defaults to metadata only. Explicit
AvailableOperators requires exact invocation/input/record ownership, rechecks
original frame lengths and hashes, and returns immutable original SourceJson
instances. A smaller viewer budget withdraws every frame while preserving the
original capture state. A cancelled read throws without publishing a partial
snapshot; a later valid read can retrieve the unchanged original record.
Refused ownership discloses no original record metadata or bodies.

Metadata formats coglatas.local-predicate-evaluation-record/1 and
coglatas.local-predicate-observation-snapshot/1 state the standalone scope.
They keep Compiler run identity NotAllocated, run selection/authority/trust/
eligibility NotEvaluated, causal/regional meaning NotRecorded and retention
NotAdopted. The operator tree represents this actual Boolean evaluation only;
it makes no causal, complete regional or current-access claim.

## Validation and remaining work

The focused regressions cover three-valued AND/OR/NOT, nested actual child order,
short circuiting, original literal reference/serialization parity, atomic capture
and viewer gaps, a1057-node input, equal-digest foreign invocations, immutable
collections, cancellation, invalid bounds and deeper opaque input refusal.

The separate public-API verifier executes186 AST cases under four predeclared
policies on each OS:744 exported evaluations and8643 native checks per host.
Both original documents are byte-identical, 12951722 bytes, SHA-256
ebc94159305294e287025b5bcb7e202b63c12243de920cee0c24e591751e0b6c.
An independent JavaScript verifier hashes7352 original frames without
reserializing hash subjects and checks942 actual nodes,150 short-circuit nodes,
truth, original literal sequence, paths, child order and skipped operands. It
also matches the earlier separate-evaluator prototype's original inputs,
truth and literal sequence; that prototype is not the shared implementation.

An internal component transition probe separately reproduces the original two
node/byte-gap-then-cancel failures, then verifies both corrected transitions on
both OSes with identical original metadata. It uses reflection only in a
work-only executable; no production test hook or friend assembly is added.
This is not Compiler or public concurrent-cancellation acceptance.

Both strict SDK10.0.403/runtime10.0.14 solution builds pass with zero warnings
and errors. Each OS passes60 focused,2333 complete ProjectIDE and12 architecture
cases without skips. Raw GUIDs and decoded names match across OSes, and all2273
base cases remain. All254 executed source inputs match the repository and both
mirrors without normalization. All15 earlier deterministic exporter profiles
reproduce the original committed asset bytes on both hosts.

The unchanged actual legacy predicate host exporter additionally produces20
attempts per host. Independent checks verify656 raw frames,216 snapshots and8
body instances, original run/attempt/origin bindings and unchanged Source/input/
result. Random identity documents remain distinct; no normalized full-document
parity is claimed. Final static analysis and current-head hosted/review gates
are recorded in the PR before integration. No frontend, host transport or
generated asset is changed in this slice.

Compiler-owned selected diagnostic/run binding and paired Angular/Windows
adoption of these new operator observations remain future #1034 work. Existing
paired Inspector behavior continues to expose only its actually recorded legacy
arrays and explicitly marks unrecorded operator relationships. Actual Avalonia
acceptance is separate. Full #1034 stays OPEN.

Runtime disclosure/revocation/redaction/deletion/retention remains P0 #1966/#1932;
permanent independent/exporter/CI adoption remains P0 #1959. Security/shared-CI
implementation belongs to its owners under the ProjectIDE-only scope. Preserve
#1144 BLOCKED, #1128 SUSPENDED/NOT_EVALUATED and Registry/Trust/Eligibility/RLS/
Native/Production/Release holds.
