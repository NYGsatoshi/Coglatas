# Owned output and selected pass publication cancellation

This partial #1034 / #782 Product correction covers cancellation during the final
publication notification for original Compiler output and selected pass-boundary
snapshots in the Windows pure ViewModel. It extends the separately qualified
whole-pass/operator/predicate correction without changing those protocols.

Both readers already pass their token to the bounded original-byte reader. A
synchronous observer can cancel after that read, when the snapshot is published.
Actual existing Compiler/owned-host fixtures supply nonempty output or selected
original boundary bodies. Counterexamples cancel at that notification, with or
without first completing a newer read. Original result, receipt or execution plan
and earlier caller snapshot facts are checked before the current-view assertion.
Exact original Windows/Ubuntu failures are qualified separately before repair.

The post-notification check clears only the same read generation and exact
published snapshot, then throws OperationCanceledException. A newer read remains
owned by its own generation; an older cancelled caller cannot clear it. Source,
run/result, input, selected boundary, map-navigation and lifecycle guards remain.
The selected-boundary clear still withdraws its dependent Source page. No
Compiler execution, original pass capture, body budget, token registration or
physical deletion of previous caller objects is introduced.

Regression fixtures detach their subscriptions before disposing the cancellation
source. Actual cancellation, nonempty bodies, original immutable facts and newer
replacement success are required. Strict nonincremental builds, source pre/post
pins and full raw cross-host inventories qualify the candidate separately.

This is offline Product display consistency; actual Avalonia and current runtime
read access remain separate. Wider parents stay OPEN; #1144 BLOCKED, #1128
SUSPENDED/NOT_EVALUATED and Registry/Trust/Eligibility/RLS/Native/Production/Release
holds remain. Security/shared CI work stays with its P0 owner issues under the
current ProjectIDE-only scope.
