# Owned pass recording preparation in the pure Workbench

This bounded #1034 / #782 Product slice connects the owned initial-root pass
host to the Windows pure .NET Workbench ViewModel. It also adds a separate
Preview export recipe for original offline frames. Angular reader/panel adoption
is follow-up work; no actual Avalonia, backend transport or runtime access is
introduced by this slice.

## Intended local execution and explicit reads

BeginPassSynthetic receives the exact request and owned predeclared manifest.
It establishes both before publishing Waiting, after clearing the earlier
context. TryAcceptPass requires the actual response/receipt pair for that exact
request and intended manifest. An older genuine pair with equal input, result
and record digests cannot replace the selected run. Ordinary, array and operator
protocol admission remain separate.

A Completed host envelope can contain a failed, inconclusive or resource-exhausted
Compiler result. A cancelled host attempt has no invented result or pass record.
The ViewModel preserves these axes and the original result and operation count.
A diagnostic rerun or retry is a new execution with its actual owned origin;
it never fills a prior capture gap.

ReadPassRecordingMetadata is explicit and requires admission of the exact
retained root Source. It returns original receipt and record metadata references.
ReadRecordedPasses defaults to Manifest with no bodies. AvailableOriginalJson
returns only the owned Domain snapshot, with its exact original body objects.
It checks the expected result and actual input/record/compilation binding.
These are whole initial-root pass observations, so selecting a diagnostic is
neither required nor a reason to withdraw an otherwise current pass view.
Selection-specific predicate/operator views retain their own existing rules.

Original Manifest, capture-byte and event-limit gaps remain distinct from a
later viewer byte limit. A larger viewing budget cannot recover uncaptured bytes.
Root Source replacement, context/result changes, reset and disposal withdraw
current metadata and snapshots. Read generations, exact request/invocation and
compilation references and root Source generations are checked again after
notifications, including notifications after publication. A reentrant read or
lifecycle change cannot publish an old view. Returned immutable caller-owned
frames are not physically erased.

## Separate actual Preview recipe

The new --pass-recordings profile executes six distinct actual local attempts
for each of eleven existing synthetic Boolean/Requirement contexts: original
Manifest, diagnostic original JSON, one-byte capture, one-event capture,
pre-cancelled attempt and a retry of that cancelled attempt. Each completed
execution is compared to the ordinary Compiler result and logical operations.
Run and attempt IDs are distinct. Original metadata is checked after the reruns.
The four legacy root contexts and seven operator/Requirement Sources are reused
without changing their original input or result meaning.

Each member exports the original Source, input, request, policy, base execution,
materializations, manifest, response, receipt and optional result/observation
text with domain-separated digests. Each completed member has explicit Manifest,
available and one-byte snapshots, with original body key/domain/text/digest/byte
length. No diagnostic selection is needed. The cancelled member exports no
invented result, observation or snapshots. Projection bytes are bounded at16MiB.
Producer platform/runtime/assembly hashes describe this local export; they do
not establish trusted producer registration or authenticity of arbitrary files.

This profile does not change earlier deterministic recipes or their committed
assets. The predicate/operator profiles retain their actual random identities;
whole-file byte equality is not asserted for those captures. The new export is
preparation for an Angular original-byte reader, not browser admission or a
current actor/Tenant/Project grant.

## Qualification and remaining gates

Product tests cover eleven actual input/result contexts, genuine equal-content
older-pair refusal, foreign request/session and ordinary/operator isolation,
explicit Source admission, default body-free metadata, original snapshot/body
identity, capture-gap preservation, viewer limits, wrong-result and cancelled
reads, resource exhaustion and cancelled-host retry. Reentrant notifications
before/after reads replace Source, reset, dispose or begin a newer context;
Waiting already exposes the complete intended invocation context.

Current native build/discovery/TRX, original export/hash audits, compatibility,
static analysis, hosted checks and review evidence must be recorded on the PR.
Supplementary original-frame checks are not independent Security qualification.
No new Angular or actual Avalonia acceptance is claimed.

Security/shared-CI and independent qualification remain OPEN/P0#1966 and related
#1959/#1932/#1974/#1980 handoffs, under the Owner's ProjectIDE-only scope. This
slice edits none of that source. Full #905/#907/#782/#1032/#1033/#1034 remain OPEN;
#1144 stays BLOCKED and #1128 stays SUSPENDED/NOT_EVALUATED. Registry, Trust,
Eligibility, RLS, Native, Production and Release holds remain.
