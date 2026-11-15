# Policy-bound local predicate host recording

This Product slice advances #1034 from the original Pure Core array collector to actual owned local host calls and run/attempt receipts. It does not extend the existing host wire protocol or either UI admission path.

## Explicit pre-run request and actual host call

PredicateCompilerHostRequest wraps the existing immutable CompilerHostRequest and an explicit CompilerPredicateCapturePolicy. Constructing that policy without arguments selects Manifest. The new execution input hash binds the original declared input/materializations execution hash and every capture mode/byte/count field. Client generation remains a separate current-call dimension.

LocalPredicateCompilerHost allocates a fresh attempt before dispatching actual work away from the caller context. It verifies the canonical transfer and the fixed host-owned CompilerHostProfile before calling CompileWithPredicateCapture with the exact snapshots, units, calendars, cache and capture policy. The returned compilation retains its original result bytes/digest, execution, coverage, verdict, evidence completeness, outputs and logical operations.

A completed host call can contain a failed, inconclusive or resource-exhausted Compiler result. A capture budget/count gap is also separate from Compiler truth. Root initial Proof arrays are recorded; this does not assert complete predicate evaluation, regional re-evaluation, AND/OR/causal structure, earlier states or FullDump/FullTrace.

Invalid, unsupported and cancelled host-only outcomes contain no invented compilation or predicate body. Cancellation is checked before work, after compile, after response construction and again before session receipt publication. Returned caller-owned data is not retroactively erased by later cancellation.

## Ownership and original frames

The new response is opaque outside Application. IsFor requires the actual local request token, generation and policy-bound input hash; a newly constructed equal-hash request is not that same owned request object. Repeated calls with one immutable request remain separate actual attempts.

LocalPredicateCompilerRecordingSession.Prepare allocates a declared run in an owned session before execution. Its manifest binds the request hash, base execution/materializations hashes, policy and generation, without retaining an input body or request. ExecuteAsync rechecks the actual supplied immutable request before reserving the run and then invokes the real sealed local host. No public completion method accepts a caller-supplied older compilation or response.

A receipt binds the actual host response digest/token and attempt to the pre-run manifest/run. Its original PredicateMetadata and body map are the objects emitted by the actual Pure Core record, without reserialization. It retains no host response/request/CoreCompilation graph. The opaque PredicateCompilerRecordedInvocation pairs the actual response and receipt; an older same-input response cannot substitute for that pair.

The Pure Core record still says runIdentity=NotAllocated: that layer has no host/session identity. The enclosing host response/session receipt carries the newly allocated actual attempt/run without rewriting the original record frame.

## Diagnostic reruns and local limits

A diagnostic rerun gets a new run and actual attempt. Its manifest links the genuine owned origin run/manifest/receipt/attempt. The original base execution input and materializations must match; a different capture policy or client generation can be explicitly declared for the new call. Changed Source/compiler/materializations cannot impersonate the same original execution basis.

An original Manifest remains body-free after a recorded rerun. A cancelled host outcome may be an actual origin, but it has no original Compiler result or predicate body. No rerun recovers unrecorded earlier states or changes the original failure.

The session permits at most 32 reserved executions and 31 diagnostic ancestors, matching existing provisional local metadata bounds. Duplicate/concurrent run reservations, foreign sessions, cycles and exhaustion are refused without silent eviction. Cancelled/unsupported attempts still consume their reserved run. Prepare alone does not consume capacity. These are not adopted durable retention, Pin or storage limits.

Dispose withdraws session metadata and refuses preparing, reserving or publishing after disposal acquires the session gate. Publication is committed while that gate is held; disposal is not physical deletion of a previously committed Task result. Already returned caller-owned frames are not physically erased. Current actor/Tenant/Project/Branch authorization, revocation/redaction/deletion and durable storage/export remain P0 #1966/#1932.

## Compatibility and remaining work

The old CompilerHostRequest constructor, execution digest, ICoreCompilerHost/ServerCompilerHost, LocalCompilerRecordingSession and Manifest/AvailableOutputs recording meanings stay unchanged. The old server continues to refuse unsolicited predicate records. No implicit conversion admits the new local types through the old protocol. No HTTP/remote transport, background job, UI action, retention service or runtime permission is installed.

Future Windows/Web admission must preserve actual original frames and current Source/selected diagnostic/generation/lifecycle ownership. Shared exporter/selection adoption remains P0 #1959; shared browser first-attempt reliability and durable trace evidence remain P0 #1980. Independent Security qualification belongs to that owner; this slice changes no Security/shared-CI source.

Keep full #1034 OPEN, #1144 BLOCKED, #1128 SUSPENDED/NOT_EVALUATED and Registry/Trust/Eligibility/RLS/Native/Production/Release holds.

## Validation

Initial strict SDK10.0.403 solution builds passed on Windows/Linux with zero warnings/errors and 42 new host/session tests. The final expanded 43 new cases and complete 2,226 ProjectIDE cases pass on both hosts. They exercise four original legacy results, all policy dimensions and actual materializations, owned response/receipt pairs, default Manifest, explicit rerun origin, cancellation/unsupported/resource gaps, duplicate/capacity/lineage bounds and immutable original frames. Both final strict builds have zero warnings/errors; all 43/2,226/12 focused/full/architecture results, raw GUIDs and decoded names agree across hosts and retain all 2,183 previous cases. All 244 native input file bytes match both executed mirrors. The 15 earlier deterministic exporters remain byte-identical to original assets on both hosts. Pinned five-project InspectCode2026.2.2 completes with 105 overall and zero changed warnings after correcting five new-test layout/lifetime/array warnings without removing assertions.

A work-only verifier executed 20 genuine host/session calls on each host, retaining original request/manifest/response/receipt/result/predicate/body frames and actual random IDs. All frame hashes, body byte counts and origin/lineage links are checked independently. Unmodified deterministic frames match across hosts; complete random-ID receipts are retained separately rather than normalized. The first Windows text-stdout output changed Japanese result bytes; that failed artifact is preserved, and direct original UTF-8 byte output resolves it without changing Product data or claimed hashes. CodeRabbit and current-head hosted results are recorded separately before integration. No frontend execution or actual Avalonia acceptance is inferred from native results.
