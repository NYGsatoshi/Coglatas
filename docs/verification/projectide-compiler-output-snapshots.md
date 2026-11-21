# Bounded Compiler output snapshot foundation

Existing work: #1034 under #905/#888. This is a local Pure Core/Windows synthetic ViewModel slice over an already produced typed `CoreCompilation`. It does not close #1034, introduce a storage/authorization service, adopt an Artifact Registry/retention policy or qualify actual Avalonia UI.

## Original result and independent capture state

`ProjectCompilerOutputSnapshot.Read` binds the expected full Compiler result digest and the compilation/result input envelope. Wrong result or inconsistent input returns a refusal with no original input/result/Source/context stamp, body, stage/output inventory or result summary. The local candidate format is `coglatas.local-compiler-output-snapshot/1`.

Default mode is Manifest: original input/Source/context/compiler/result identities, original execution/coverage/verdict/evidence-complete axes, actual stage/output references and explicit capture gaps; no produced body is retained. Original Source bodies and earlier intermediate states are not captured by this API. It allocates no run identity and claims neither FullDump nor FullTrace. An available result snapshot is not a record of an earlier unrecorded pass or failure; a diagnostic rerun requires a separately identified execution/origin path.

A separately supplied desired input produces an independent Current/Stale/Undetermined freshness assessment, both original/desired stamps and changed dimensions. With no desired input, freshness is Undetermined. This fixed-input comparison is not current actor permission, Producer trust, evidence eligibility or live freshness. Those fields remain NotEvaluated; retention policy remains NotAdopted.

Explicit AvailableOutputs mode retains the actual immutable version-two semantic model, IR and Source map only when the owned publication is declared by the actual Compiler result and each output digest matches. The original objects/bytes are preserved. Version-one references remain in the manifest while their body provider is explicitly unavailable; no body is fabricated or silently migrated. A cancelled compilation with no outputs remains NoOutputsProduced with its original cancellation axes and capture gaps.

## Bounds and immutable views

The provisional local output-body budget defaults to1MiB and accepts1byte through32MiB. It is neither a product capacity/retention policy nor a numerical/CI threshold. The requested budget/mode is bound in the snapshot digest. Body bytes are canonical UTF-8 bytes, counted independently from the bounded metadata manifest. At the exact body boundary all supported bodies are retained; if even the final body exceeds the budget, the snapshot keeps the original manifest and returns BodyBudgetExceeded with no partially retained body set. The Compiler's own verdict/coverage are unchanged. Invalid modes/bounds are rejected, not clamped; cancellation stops the query without changing any original output.

The existing internal `ImmutableMapView` and `ImmutableListView` own collection storage. Dictionary/keys/values/changed-dimension views expose no storage-bearing non-generic synchronization root; generic mutation throws and copied entries/bytes cannot mutate retained state. A work-only prototype using the framework ReadOnlyDictionary exposed its mutable dictionary through ICollection.SyncRoot. That pre-Product failure was retained and corrected by reusing the existing repository helper; no shipped collection helper or previously integrated source was changed.

## Windows pure ViewModel

`ReadSyntheticCompilerOutputs` requires the current admitted scoped compilation. It reads original outputs and starts no compiler, simulator or external operation. A query, reset, disposal, input change or scoped readmission withdraws the old view. Generation/request/compilation/scoped-reference guards reject interrupted publication and preserve a newer reentrant query. Wrong-result refusal has no old bodies; cancellation or invalid options withdraws the prior snapshot. Reading output metadata/bodies preserves the owned Compiler, plan and any independent simulation result.

View withdrawal does not erase a snapshot already returned to a caller. Required physical deletion, revocation, retention, redaction, Pin quotas and authorized exports belong to a separately governed storage/runtime adapter. This API provides none of those authorities.

## Verification and remaining work

Actual Product tests cover all twelve scoped fixed inputs with original Compiler-result replay, original bodies/stages, budget boundaries, wrong identities, desired-input freshness, legacy-provider gaps, cancellation, culture and mutable-view attempts. Windows pure ViewModel tests cover source admission, immutable coexistence, refusal/budget/cancellation, reset/disposal/input/readmission and notification reentry. Exact two-platform inventory counts, source hashes, CI producers and review outcomes belong in the exact-source PR record; work-only prototype controls are not substituted for Product or independent acceptance.

Keep full #1034 open for shared Compiler/Test/Simulator/Impact/Profiler reason DAGs with AND/OR and actual missing evidence, preselected/buffered full pass capture, diagnostic execution/origin identity, permitted None contexts, durable retention/Pin/deletion/current-permission/redacted export and independent UI/runtime acceptance. Necessary Security/independent work is handed off through P0 Issues; this Product slice writes no SecurityBranch, workflow or protection policy. #1144 BLOCKED; #1128 SUSPENDED/NOT_EVALUATED; Native/RLS/Trust/Eligibility/Production/Release holds remain.
