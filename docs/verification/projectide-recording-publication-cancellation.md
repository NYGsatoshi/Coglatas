# Owned recording publication cancellation

This partial #1034 / #782 Product correction checks cancellation after the
Windows pure ViewModel publishes original Pass, Operator or Predicate snapshots
and notifies observers. Original Compiler execution/result, predeclared capture,
owned run/session/attempt receipts, captured bodies and viewer budgets remain
unchanged. It performs no new compilation or diagnostic rerun.

The Domain readers check cancellation while reading retained bytes. That does not
cover cancellation from a synchronous final publication notification. The actual
existing Compiler/host fixtures supply nonempty original bodies for all three
protocols. A callback cancels the current read; another case completes a newer
successful read before cancelling the older caller. Original result/receipt and
previous retained snapshot facts are checked independently of the current view.
The original counterexamples and their exact raw Windows/Ubuntu results are
qualified separately before applying the correction.

After publication notifications, a cancelled token withdraws only the same read
generation and exact snapshot object, then throws OperationCanceledException.
The old caller cannot withdraw a newer successful read or cross a Source,
selection, result or lifecycle transition. Existing initial guards remain. No
long-lived token registration or shared generic publication abstraction is added.

Regression fixtures own and detach their event subscriptions and cancellation
sources. They require cancellation to fire, assert nonempty original captured
bodies, preserve Compiler/result/receipt and earlier caller objects, and require
the newer replacement when present. Strict nonincremental builds, source pre/post
pins, full original inventories and exact raw cross-host rows qualify this slice;
prior-source test executions are never restamped as current. Retained caller
objects are not physically deleted by withdrawing the current display.

This is offline Product display/lifecycle consistency. Actual Avalonia, current
runtime read authorization, storage/Pin/deletion, independent Security and formal
Registry/Trust/Eligibility adoption remain separate. Wider #1034 / #782 remain
OPEN; #1144 BLOCKED, #1128 SUSPENDED/NOT_EVALUATED and RLS/Native/Production/Release
holds remain. Security/shared CI work stays with its P0 owner issues under the
current ProjectIDE-only scope.
