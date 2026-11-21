# Selected diagnostic original predicate arrays

This bounded Product slice advances #1034 from predeclared original capture and actual local host receipts to an explicit read of a selected root diagnostic's already recorded arrays. It adds no Compiler execution or UI action.

## Explicit owned read

ProjectRecordedPredicatePremiseSnapshot.Read requires the actual ProjectRootSourceAdmission, its exact owned ProjectPredicatePremiseRecord, the selected diagnostic object and expected result digest. Equal input/result/record digests from another invocation do not establish ownership. Foreign records or diagnostics and wrong results are refused with no basis, original metadata, references or bodies. Root context must match.

The new local format coglatas.local-recorded-predicate-premise-snapshot/1 and SelectedDiagnosticOriginalRootPredicateArrays scope are separate from existing ProjectDiagnosticPremiseSnapshot.Read/ReadRoot and their input-only formats. Old reads do not gain predicate bodies. The caller must explicitly select AvailableBodies; Manifest is the default and returns no body.

The read checks the original coglatas.predicate-premises/1 hash against each requested array and returns the same immutable SourceJson object. It never reconstructs missing bytes or reserializes the original body. Only arrays whose digest is in the actual selected diagnostic's original premise list are returned. An unrelated successful rule's recorded array remains absent from the selected body map. Input and Source objects are outside this new body scope.

Original metadata, policy, record digest and capture gap remain unchanged and are bound into the new snapshot. A snapshot body budget is a distinct viewer limit: its overflow atomically withdraws all selected bodies while retaining the original capture state. A larger viewer budget cannot repair original Manifest, byte/count overflow, interruption or no-observation gaps. No observed arrays does not certify complete predicate coverage. Compiler execution, coverage, verdict, evidence completeness and original result bytes remain unchanged.

## Remaining admission boundaries

The Pure Core record has no run/attempt identity. This read does not select the currently intended host run. The [paired local client adoption](projectide-paired-predicate-recordings.md) binds the explicit intended pre-run manifest identity before execution, rejects an entire genuine older invocation even when the same request/generation/result/record digests recur, and preserves Source, selected diagnostic and lifecycle/reentry fences. The snapshot does not retain a compilation, request, response or the record's body map; it retains original metadata and only explicitly selected bodies.

This is a local synthetic primitive, not current actor/Tenant/Project/Branch disclosure authority, remote transport, durable retention/Pin, FullDump/FullTrace, regional re-evaluation or AND/OR/causal adoption. P0 #1966/#1932 own runtime disclosure/revocation/redaction/deletion/retention; P0 #1959 owns permanent independent exporter/selection qualification, #1974 lifecycle and #1980 first-attempt browser reliability. No Security/shared-CI implementation changes are made here.

Keep full #1034 OPEN, #1144 BLOCKED, #1128 SUSPENDED/NOT_EVALUATED and Registry/Trust/Eligibility/RLS/Native/Production/Release holds.

## Validation status

Both strict SDK10.0.403 builds pass with zero warnings/errors. Each native host passes 14 focused regressions, 2,240 complete ProjectIDE tests and 12 architecture tests without skips. Exact raw GUIDs and decoded names match; all 2,226 prior cases are retained. All 246 native source inputs match the two executed mirrors. Tests cover original legacy results, default Manifest, a selected failed rule while an unrelated successful rule has another recorded array, original capture gaps versus viewer budget, equal-digest foreign objects, foreign regional findings, resource interruption, immutable original frames, bounds and cancellation.

A public work-only verifier ran 12 actual compilations and 81 snapshot reads per OS over four unchanged legacy fixtures and Manifest/full/budget policies. The original UTF-8 frame documents are byte-identical: 687,367 bytes, SHA-256 e41d5151960c9e88ce3c479b74c350cad60e6e43350a7cec7e2501ba2d09e07d. An independent Node verifier hashes each raw result, record, selected diagnostic, snapshot and body frame and checks original metadata, selected-premise/body lengths and gaps without hash-subject normalization. Original result frames match the preceding qualified host vectors.

The initial strict builds stopped before test execution on new-test xUnit2031 assertion syntax. The original source/log is retained; the equivalent Assert.Single(collection, predicate) form passes without changing the condition. Pinned five-project InspectCode2026.2.2 completes successfully with 105 overall warnings and zero changed-file warnings. CodeRabbit and exact-head hosted qualification are recorded separately before integration. No actual frontend or Avalonia acceptance is inferred.
