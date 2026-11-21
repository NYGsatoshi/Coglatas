# Owned Compiler recording display preparation

ProjectIDE-only continuation of #1034. The local recording foundation remains an implementation contract, not a normative durable registry or retention policy.

Use an immutable LocalCompilerRecordedInvocation created by an actual owned session invocation to carry the original response and receipt together. Public response metadata can be copied; equal input/result/execution digests alone cannot identify which original invocation produced the compilation currently admitted to a ViewModel. The private constructor prevents callers from assembling arbitrary response/receipt pairs into this view input.

The synthetic Windows ViewModel explicitly reads an already executed invocation. It requires its exact original compilation object to be the current admitted result, current captured Source scope, original attempt/input/generation/materialization binding and receipt/result/capture digests. Default Manifest displays no bodies; explicit output capture is unchanged. A host-only cancellation/error without a Compiler result is not fabricated into a compiled-output view.

Scope reload, result reset/replacement and disposal clear the receipt and compilation alias through the existing scoped Source lifecycle. Query generation and scoped generation protect against notification reentry or newer reads; read success never grants current runtime authorization. Caller-owned old invocations/receipts remain separate; withdrawal does not claim physical erasure.

A work-only public-VM composition counterexample mixed copied AttemptId metadata with an equal-input/equal-result compilation from another execution. That failure was reproduced on both OS before this candidate; the corrected work-only immutable-invocation-pair controller passed372 controls/12actualcases on Windows/Linux. Those controls do not replace Product tests, current hosted CI or independent qualification.

No actual Avalonia/Web UI, current actor/Tenant/Project authorization, RLS, Registry/Trust/Eligibility, durable storage/Pin/TTL/deletion/redaction/export, Native/Production/Release activation. Full #1034 and P0 #1966/#1959 remain open; all existing holds remain.
