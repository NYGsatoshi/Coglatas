# Owned local initial-root pass host recording

This bounded #1034 / #905 Product Application slice binds the separately owned
initial-root pass observation to a predeclared local request, intended manifest
and actual host attempt. It uses the existing pure Compiler and adds no HTTP/job,
client transport, Windows ViewModel, Angular or actual Avalonia adoption.

## Before execution and actual outcomes

PassObservationCompilerHostRequest wraps the immutable CompilerHostRequest and
CompilerPassObservationPolicy. Default policy remains Manifest. The policy hash
binds mode, maximum original body bytes and maximum events. The new execution
binding includes the original Core execution/input/materializations hashes.
Generation, policy content and actual request ownership remain separate checks.

LocalPassObservationCompilerRecordingSession.Prepare declares an owned run before
execution and retains metadata rather than the request or Source bodies.
ExecuteAsync checks the owned request/manifest, validates the input transfer and
reserves its run once before dispatching real work away from the caller context.
The actual execution host is internal; public calls cannot bypass the session's
reservation limit through a second public host entry point. No public completion
method accepts a caller-provided compilation or response.

The fixed host profile and actual immutable snapshots, units, calendars and cache
are supplied to CompileWithPassObservation. A Completed host envelope can contain
a failed, inconclusive or resource-exhausted Compiler result. Capture availability
is independent of execution, coverage, verdict and evidenceComplete. Original
result bytes/digests, operations and the Domain record are not rewritten.

Unsupported or cancelled host outcomes have no invented compilation/observation.
Cancellation is checked before work, after compile, after response construction
and before recording under the session gate. Even if the lower-level Core had
observed earlier boundaries, a cancelled host envelope does not claim to return
or recover those frames. No physical erasure of returned caller-owned objects is
implied.

## Intended invocation and original metadata

The actual request token, intended manifest token/reference, response token,
attempt and receipt digest bind the returned PassObservationCompilerRecordedInvocation.
Equal genuine input/result/record digests cannot make an old complete pair satisfy
a newer intended manifest. Reattaching an old response to another manifest or
using a different equal-content request is refused. Local ownership grants no
current actor/Tenant/Project read or export permission.

The request's Core and response's Execution are JsonIgnore. Manifests and receipts
retain original metadata without a compilation graph. Explicit Domain snapshots
still require their original owner/compilation/record plus expected input/result.
The original observation keeps compilerRunIdentity=NotAllocated: the enclosing
local host run/attempt does not fabricate a global identity inside that frame.
No FullDump, regional, optimizer, execution-plan or causal completeness follows.

## Diagnostic reruns, bounds and disposal

An explicit diagnostic rerun has a new run/attempt and links the actual owned
origin manifest/receipt/attempt. The original base execution/materializations must
match. A newly declared policy may capture bodies in that new execution; it cannot
fill the old Manifest, byte or event gap or impersonate the original failure.
A cancelled host outcome may be an origin without an invented original result.

At most32 executions and31 diagnostic ancestors are reserved by the local session.
Prepare alone does not reserve; cancelled/unsupported attempts do. Duplicate and
concurrent duplicate reservations, foreign sessions/origins, cycles, changed actual
input and exhaustion refuse without eviction. These provisional memory metadata
bounds do not adopt durable retention, TTL, Pin or storage quotas.

Dispose withdraws owned execution metadata and refuses new preparation/reservation
or publication after disposal acquires the gate. Already returned caller-owned
records and snapshots remain; historical integrity is distinct from current
session liveness and permission. No physical deletion or revocation service is
claimed.

## Compatibility and qualification

Existing normal/output/array/predicate/operator host and client protocols remain
separate. Product tests exercise actual legacy fixture results, all policy and
Core materialization/generation dimensions, exact owned-pair refusal, diagnostic
origin and unchanged old gaps, cancellation/unsupported/resource-exhausted axes,
duplicate/capacity/lineage bounds, serialization and disposal. Current strict
native builds/discovery/TRX, original hosted artifacts, static analysis and reviews
must be recorded in the PR before integration. Earlier work-only probes are
supplementary and are not Product xUnit or independent qualification.

Current actor/session/Tenant read/export, redaction/revocation, durable retention,
Pin/expiry/deletion and independent qualification remain existing OPEN/P0#1966;
shared CI and selection stay with P0#1959/#1932/#1974/#1980. This slice edits no
Security/shared CI source or policy. Keep full #1034/#905/#782 and other parents
OPEN, #1144 BLOCKED, #1128 SUSPENDED/NOT_EVALUATED, and all Registry/Trust/Eligibility/
RLS/Native/Production/Release holds.

## Native Product checkpoint

Strict SDK10.0.403/runtime10.0.14 solution builds complete on Windows/Linux with
zero warnings/errors. All31 new owned-pass session cases,2567 full ProjectIDE
cases and12 Architecture cases pass on each OS with zero skips. Exact raw test
GUID/name/outcome inventories agree across hosts and retain all2536 prior Product
cases. All272 declared relevant native input bytes match the executed mirrors.
The first native builds failed before test execution because the new resource-gap
test omitted its JsonNode namespace import; the original source/logs remain
retained. Only that import was added, without assertion or gate changes.

A separate original-frame probe executes the actual new public Product APIs on
both native binaries:12 existing output contexts and11 operator/Requirement
contexts,92 predeclared policy vectors,3207 checks and365 actual Core calls per OS.
Original fixture result and ordered actual record/snapshot JSON agree across OS;
random run/attempt/manifest/receipt identities are retained separately. This is
supplementary Product API evidence, not independent Security, client or current
permission qualification. Current committed-source review and hosted results must
still be recorded in the PR before integration.

The full initial review raised one conditional question about concurrent use of a
caller-owned PlanDeclarationReadCache. Its Read, Clear and statistics use the
same gate; scope, dictionary, LRU and byte-accounting mutations and immutable
decode-result publication occur under that gate. The binder reaches that Read
path for the root Source; CheckTenant returns an immutable existing or new result.
No unsynchronized cache mutation or runtime correction is identified. A new
Product regression runs16 distinct owned requests concurrently with one bounded
two-entry/4096-byte cache, same declaration Source but two actual input settings
and alternating policies. Every result and operation count matches its uncached
control, with distinct intended run/attempt identities and bounded retained data.
It passes on both OS, as do the final31/2567/12 suites; all previous2566 raw test
identities/outcomes remain. Review and current hosted evidence remain separate.
