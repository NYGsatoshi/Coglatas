# Owned Scenario preview publication cancellation

This partial #1032 / #782 Product correction checks cancellation after the pure
ViewModel publishes a hypothetical Scenario preview or a Scenario-to-Draft
conversion preview and notifies observers. Previewing starts no new Compiler
execution, real external action, save or Merge. The explicit later load/apply
contracts and current runtime authority remain separate.

Actual existing Compiler fixtures prepare a nonempty typed override; conversion
uses the actually compiled Scenario and its fixed admitted base. The original
methods leave a cancelled published preview current, or retain a newer successful
preview but return without cancellation for the superseded old caller. Four
original cases fail on each host. Original Compiler/result, Source, TestRun, fixed
base, active Scenario and captured baseline, prior immutable plan data and the
newer successful replacement are asserted intact before failure.

After notification, cancellation clears only the exact preview still owned by the
same existing Scenario-preview generation, notifies observers and throws
OperationCanceledException. A superseded caller cannot clear a newer preview. The
existing shared preview-clear operation also withdraws its paired draft preview;
no active Scenario or captured baseline is cleared. Initial cancellation, bounds,
input/selection/lifecycle checks and explicit load/apply behavior remain. There is
no persistent token registration, new abstraction or Source/recording rewrite.

The regressions own and detach their subscriptions and retain actual immutable
facts. Fresh strict builds, source pins and raw cross-host suites qualify the
candidate separately. Initial counterexamples explicitly compose the separately
reviewed recorded-comparison/profile correction. This is offline Product
presentation consistency, not actual Avalonia acceptance, current runtime access,
real operational effects or physical deletion of caller objects. Wider parents
remain OPEN; #1144 BLOCKED; #1128 SUSPENDED/NOT_EVALUATED; Registry/Trust/Eligibility/
RLS/Native/Production/Release HOLD. Security/shared CI needs remain on P0 issues.
