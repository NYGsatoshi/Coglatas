# Predeclared original root predicate capture

This bounded Pure Core slice advances #1034. It records actual temporary arrays used by the original root invocation's Proof() helper before their bytes disappear. It does not recover unrecorded earlier states or infer AND/OR/causal relationships from a flat array.

## Entry point and ownership

The existing PureCoreCompiler.Compile signature and behavior remain unchanged and leave CoreCompilation.PredicatePremises null. The separate CompileWithPredicateCapture requires an immutable policy before execution. Its default mode is Manifest: no predicate body is retained. Explicit OriginalRootArrays records only original nonempty arrays emitted by the root Proof helper, using the unchanged coglatas.predicate-premises/1 hash domain and bytes. Unknown predicates can be evaluated without emitting such an array; no observations is an explicit gap, not complete predicate coverage.

A fresh private collector belongs to each call. Callers cannot supply observations, attach a receipt after execution, or complete a collector against an older same-input result. The record is built after dependency attachment/scoped publication selects the final result, and is attached to the returned immutable compilation. IsOwnedBy requires the actual record property and exact input/result objects, not digest equality. Reusing immutable policy or declaration cache does not share collectors or records. No global hook, ambient input, scheduler or result cache is added.

## Bounds and result semantics

Local provisional limits reuse existing output-snapshot byte defaults: 1 MiB by default and at most 32 MiB. The caller also declares 1..1024 distinct arrays, default 1024. Repeated digests do not consume the count twice. The first byte/count overflow atomically withdraws all arrays with an explicit gap; later smaller observations cannot restore a prefix. This is not adopted retention, Pin or storage capacity policy.

Interrupted/noncompleted compilation withdraws every captured body. Manifest-only, no original arrays observed, byte budget, count budget and interrupted capture are separate states. Original Compiler execution, coverage, verdict, evidence completeness, result bytes/digest, outputs and logical operations remain separate and unchanged. A root recording can coexist with later regional findings/publication, but does not record regional re-evaluation or certify whole-scope coverage.

Metadata format coglatas.local-root-predicate-premise-record/1 binds input/Source/context/compiler/final result and lists retained array hashes/byte lengths. Bodies are owned immutable SourceJson objects in an immutable map; byte copies do not mutate them. Metadata records runIdentity=NotAllocated, authority/trust/eligibility NotEvaluated and retention NotAdopted. A pure invocation record is not a host session/attempt, diagnostic rerun origin or durable artifact.

## Scope and remaining work

This primitive has no host/transport/cache/recording-protocol adoption or Web/Windows UI admission. The existing ServerCompilerHost refuses any unsolicited predicate record because its current request has no pre-run predicate capture declaration; even Manifest-only records cannot ride an older response protocol. Existing LocalCompilerRecordingSession, Manifest/AvailableOutputs contracts, diagnostic-premise snapshot scope and old fixtures remain unchanged. An existing original result cannot gain arrays retroactively. Future host integration must bind explicit capture policy into actual execution input and preserve run/session/attempt/origin and current selection ownership before paired client exposure.

Runtime query/open/export authorization, revocation/redaction/deletion and durable retention remain P0 #1966/#1932; permanent exporter/selection coverage remains P0 #1959. Shared browser first-attempt reliability/evidence retention remains P0 #1980. This Product slice changes no Security/shared-CI source. Keep full #1034 OPEN, #1144 BLOCKED, #1128 SUSPENDED/NOT_EVALUATED and Registry/Trust/Eligibility/RLS/Native/Production/Release holds.

## Validation

Both strict SDK10.0.403 solution builds passed with zero warnings/errors. Windows and Linux each passed73 focused cases,2183 complete ProjectIDE cases and12 architecture cases, without skips. Exact raw GUIDs and decoded native names match; all2166 previous identities are retained and17 new cases pass. All240 native source inputs match both executed mirrors. All15 earlier deterministic exporters reproduce their original committed asset bytes on both hosts; random run/session/attempt recording identities are excluded from byte-identity claims.

The regressions replay four unchanged legacy input/result identities, compare actual predicate bytes, preserve result/operations and distinguish Manifest/no-observation. They test last-byte/count atomic refusal, duplicate bodies, equal-input foreign records, concurrent policy reuse, immutable access, cache/publication retention, cancellation/resource gaps and existing-protocol refusal of unsolicited Manifest/body records. No frontend input or generated fixture changes are made.

The initial full run failed7 existing adversarial transport controls on both hosts because an eighth optional constructor parameter changed reflection-based construction. Its original TRX/source is retained. The correction preserves the exact sole seven-parameter constructor and creates a separate immutable owned copy; no existing test or assertion changed. A copied-Domain prototype separately retained its same-input older-result counterexample; that prototype is not Product acceptance. Static analysis, source review and current-head hosted qualification are recorded in the PR before merge.
