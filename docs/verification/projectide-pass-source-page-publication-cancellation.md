# Owned pass Source page publication cancellation

This partial #1030 / #782 / #1034 Product correction checks cancellation after
the Windows pure ViewModel publishes a bounded page from an explicitly read
original pass map and notifies observers. It changes no Compiler/result/receipt,
selected boundary, Source/map bytes or current runtime authorization.

Actual existing owned pass fixtures supply a nonempty original map with multiple
real targets. The callback opens a target from the published page and cancels.
The original reader leaves that cancelled page and its target current. A second
case first reads and opens a newer page, then cancels the old caller: the newer
page and target remain intact but the old caller returns without cancellation.
Both original cases fail on Windows and Ubuntu. Original result/receipt, selected
boundary, root Source and previous caller page facts are asserted before failure.

The post-notification check clears only the same page generation and exact page
object, then throws OperationCanceledException. The existing clear operation
withdraws only that page's owned navigation target. A newer page and its target
remain owned by their newer generation. Existing original boundary/map membership,
Source/run ownership, checked bounds, target count, original digest identities
and lifecycle/navigation guards remain unchanged. No shared token registration,
Compiler execution, body acquisition or physical deletion of caller objects is
introduced.

Regression fixtures own/detach their callbacks and cancellation sources, require
an actual opened target and cancellation, retain nonempty original caller data,
and require the newer page/target when present. Strict nonincremental builds,
source pre/post pins and full raw cross-host inventories qualify the candidate
separately. The initial reproduction explicitly records its composed, separately
qualified Root Source work; it is not presented as an unmodified public HEAD.

This is offline Product presentation/navigation consistency, not actual Avalonia
or current runtime authority. Wider parents remain OPEN; #1144 BLOCKED, #1128
SUSPENDED/NOT_EVALUATED and Registry/Trust/Eligibility/RLS/Native/Production/Release
holds remain. Security/shared CI work stays with its P0 owner issues under the
current ProjectIDE-only scope.
