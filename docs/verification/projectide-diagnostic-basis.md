# Recorded diagnostic basis (#1034)

This bounded Product view reads the exact selected diagnostic from an admitted
Compiler result. It pairs the Angular offline Inspector with the Windows pure
ViewModel; actual Avalonia rendering and current server authorization are separate.

The view preserves Compiler input/result IDs and digests, the original diagnostic,
locked rule/version, stage, typed location, evidence kind, missing reason and all
recorded premise digest references. It reads 32 references per page under the
existing 1,024-reference Core bound. It preserves the recorded sequence without
assigning semantic or chronological meaning. Empty references and an explicit
missing reason remain visible. The original result and its simultaneous
execution, coverage, verdict and evidence-completeness axes remain unchanged.

CompilerEvidenceBasis records a flat digest set. AND/OR, observed order, causal
links and premise bodies are not resolved by this view. A model-proof kind alone
cannot supply those missing relationships. This is partial #1034 work; the shared
cross-engine reason DAG, governed capture/Pin/deletion and current-access-qualified
historical navigation remain open. No Registry/Trust/Eligibility adoption follows.

The Windows adapter requires the current accepted compilation and exact selected
owned diagnostic. Reset, disposal, a new request and another selection withdraw
the view. Caller-retained immutable pages are not physically deleted. The Web
adapter also requires current case/result identity, original bundle membership
and successful package revalidation. Existing actor/tenant/workspace invalidation
clears the selection and view. Pagination cannot borrow a diagnostic with matching
IDs from a different result or request.

Product regression checks cover actual Core fixture replay for the six diagnostic
cases, missing/model-proof evidence, exact IDs and references, 1/32/33/1,023/1,024
reference boundaries, invalid offsets, copied diagnostics and lifecycle withdrawal.
Angular component and production-browser checks cover English/Japanese rendering,
keyboard selection, missing evidence and existing access-withdrawal behavior.
These Product checks are not independent Security qualification. Preserve #1144
BLOCKED, #1128 SUSPENDED/NOT_EVALUATED and all native/production/release holds.
