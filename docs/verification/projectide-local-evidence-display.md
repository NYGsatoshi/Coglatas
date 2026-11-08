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


## Phase 9 qualification candidate (2026-11-09)

Mission: `LOCAL_EVIDENCE_DISPLAY_INTEGRATION_QUALIFICATION`.
Integration baseline: `1a32cfa5886281a6342af2a83c54f648b97d388d`.
Product source before this update: `7e1daa251c751589903d5cce61d91ef8ceb2f4cd`
(#1823, six files); independent source:
`afb227e4fb074c9fd83f29d220cedaba9adb22d0` (#1825, four files against
its exact SecurityTestImplimation base `1dc64c38390208e45c4496c685cd4c37de00607c`);
scoped source: `d7a7b8268d0d19fa62511fd79064af7a85585f54` (#1828, two files).
All three were open Drafts, with no submitted reviews, at audit start.

The Product branch was updated by a normal merge of the integration baseline.
Only #1834's two-file Entry CI repair was missing; there was no code conflict.
The repair's broader source triggers and removal of the branch-specific predicate
are preserved byte-for-byte. Existing Reader, Pure Core and Entry tests are not
edited. Independent source is never raw-merged into ProjectIDE.

Both promoted test blobs match the current independent source exactly. The C#
blob named in #1828's initial description predates the explicit-disposal repair;
that historical receipt does not describe the latest test source. Preserve the
original commits and assertions and record final source/tree identities and
actual check runs in PR receipts rather than copying an earlier PASS.

This update makes unloaded evidence explicit, rejects malformed completeness,
checks the separately supplied manifest identity/context/source digest and required rule coverage, and
prevents unsupported or unresolved source projections from showing local PASS.
Angular also requires the loaded result to be the currently selected case.
Windows retains exact request/generation/materialization admission and clears
results before lifecycle notifications. Both clients preserve the existing
presentation vocabulary. These checks inspect synthetic result projections;
they do not authenticate a producer or make an external artifact trustworthy.

Independent tests retain all original test identities and assertions and add
explicit fixture expectations, source substitution, terminal-state cleanup,
reentrant reset/dispose/reselection, malformed completeness, and delayed read
rejection. The qualification runner must reconcile compiled xUnit discovery and
actual TRX identities, retain Vitest assertion identities, and fail incomplete,
duplicate or non-passing execution. The scoped promotion remains exactly the
two test source files; candidate workflow/history stays in #1825.

Exact final-head ordinary CI, two-host composition, ReSharper report integrity,
scoped review and real browser evidence are separate checks. Configuration and
previous runs are not current completion evidence. Headless Windows success
never qualifies the actual Avalonia UI. All activation and adoption holds above
remain unchanged. No feature PR is merged by this mission.
