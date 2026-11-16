# Owned semantic query publication cancellation

This partial #1030 / #782 Product correction checks cancellation after the Windows
pure ViewModel publishes reference-completion or Requirement-to-Task suggestion
pages and notifies observers. Original Compiler/result, semantic inspection,
Source bytes, candidate meaning and current runtime authority remain unchanged.

Actual existing Compiler/host fixtures produce three real candidates per query.
A subscriber cancels when the current page is published: the original methods
leave that cancelled page current. A second case completes a newer one-candidate
query before cancelling the older caller: the newer page survives, but the older
caller returns without a cancellation exception. Four original cases fail on
both Windows and Ubuntu. Original result/Source/inspection and previous caller
candidates, plus the newer replacement when present, are asserted before failure.

After notification, a cancelled token withdraws only the exact current page still
owned by the same suggestion-query generation or completion selection, then
throws OperationCanceledException. The old caller cannot clear a newer page.
Owned suggestion withdrawal advances its existing query generation; completion
withdrawal leaves the selection anchor unchanged. Initial token, invalidation,
query/bounds/cursor, Source/index/result and selection behavior remain. No new
query abstraction, long-lived registration, Compiler run or Source write is added.

Candidate counts/order, typed Source locations, coverage and missing/unknown
semantics are unchanged. Suggestions remain candidates, not evidence of
satisfaction. Existing current-page/member checks still fence navigation. The
regressions own and detach subscriptions, require cancellation and nonempty real
candidates, preserve original immutable facts and require the newer replacement.
Strict nonincremental builds, pre/post source pins and raw full cross-host
inventories qualify the candidate separately. The initial counterexample's
composed, separately qualified Source-page work is explicitly recorded.

This is offline Product presentation consistency, not actual Avalonia, current
runtime authority or physical deletion of caller objects. Wider parentsOPEN;
#1144 BLOCKED; #1128 SUSPENDED/NOT_EVALUATED; Registry/Trust/Eligibility/RLS/Native/
Production/Release HOLD. Security/shared CI work stays with its P0 owner issues
under the current ProjectIDE-only scope.
