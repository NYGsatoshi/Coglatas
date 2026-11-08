# ProjectIDE local compiler-evidence display boundary

## Scope and authority

This is a read-only, synthetic ProjectIDE consumer slice under #888 IDE-START-01/02, #905, #1034, #793 and #794. It reuses the already integrated Core compiler result axes, not the separate provisional PrototypeReceiptAssessment type. Angular Web and the pure Windows preparation ViewModel must show the same **local compiler-evidence state** without interpreting it as Producer trust, Evidence Eligibility, current user access, Security Architecture exit, or any promotion/Release authority.

The source artifact remains the Core-generated synthetic preview. The Angular UI does not compile or fetch privileged data. The Windows ViewModel remains headless; this PR does not activate Avalonia or any native runtime adapter.

## Classification and precedence

1. `not-loaded`: no accepted result exists, including after reset, rejection, cancellation, or disposal.
2. `stale`: source/revision/input binding mismatch. Angular also verifies the exact result input ID, input digest, bound input stamp and source digest before presenting a positive state; Windows relies on the current Core request/response checks and rechecks accepted digest/source binding.
3. `not-executed`: Core execution is not `Completed`.
4. `partial-coverage`: execution finished but declared coverage is not complete.
5. `missing-evidence`: coverage is complete but the evidence-complete flag is false.
6. `not-passed`: all preceding local conditions are satisfied but the verdict is not `Pass`.
7. `complete-local-pass`: current exact input, completed execution, complete coverage, complete evidence, and `Pass` verdict. **This is exclusively an observed synthetic Core result, never a trusted/eligible/authorized assertion.**

These are **presentation** outcomes, not new canonical SPEC IDs, artifact roles, runtime entitlement predicates or an authorization mechanism. No real Compiler execution is unlocked and no evidence/receipt producer is enrolled.

## Explicit test and quality expectations

Independent tests should cover every classification, forged input IDs/digests, stale desired revisions, missing evidence, non-Pass verdict, cancellation/reset and Windows observer invalidation. The current Angular preview must still deny access when the account/Tenant/capability is unavailable; display state is not authorization. All project-level artifact and source maps remain separately validated before navigation. Positive UI assertions must not be misreported as current runtime authorization, real verification, or adoption qualification.

Product work is proposed in a dedicated Draft PR against `projectide/integration-20261031`. Independent verification work remains separately owned under #793 and must be composed at exact candidate SHAs before any merge. Retain previous test IDs, ordinary CI and all existing Required Checks.

## Restrictions and retained holds

Do not introduce new formal Reader/Registry/Migration versions, external acquisition, signing, keys, Trust Root/enrollment, Eligibility policy, operational RLS/SQL/GRANT, Native/Avalonia activation, Production deployment, or Release. Preserve #1144 BLOCKED, #1128 SUSPENDED/NOT_EVALUATED, 15 deferred NOW decisions and zero adopted items out of the 99 still unready Spec requests. Private Spec #301 remains Draft and must not be copied into this public source.
