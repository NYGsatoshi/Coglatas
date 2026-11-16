# Owned Root Source publication cancellation

This partial #782 / #1034 Product correction checks cancellation after the pure
ViewModel publishes an exact original Root Source admission and notifies
observers. It is captured local input admission, not current runtime authority.
Original Compiler execution/result and retained Source bytes remain unchanged.

The original bounded admission reader checked the token while reading. A final
synchronous Ready notification could cancel after that check. An actual existing
Compiler proof fixture reproduces a cancelled Source remaining current. A second
case first performs a newer successful Root Source admission and a nonempty
original premise read, then cancels the older request: the newer state survives,
but the old caller returns without a cancellation exception. Both original cases
fail on Windows and Ubuntu, with original result and earlier Source facts
asserted intact before failure. Exact raw identities are retained separately.

After notification, cancellation clears only the same Root Source generation and
exact admission object, then throws OperationCanceledException. A newer admission
and its premise snapshot remain owned by the newer generation. Existing intended
request/compilation/result checks, Source/result digest checks, stale/reentry
fences and cancellation catch behavior remain. Withdrawing the current admission
clears its dependent displays through the existing clear operation; it does not
physically delete caller-retained Source objects or revoke runtime authority.

The regression fixture owns and detaches its event subscription and cancellation
source. It requires actual cancellation and nonempty original Source, preserves
Compiler/result/previous Source facts, and checks the newer Root Source and
nonempty premise snapshot before requiring the old caller's exact cancellation
exception. Strict nonincremental builds, pre/post source pins, original full
inventories and raw cross-host identities qualify the candidate separately.

This is offline Product lifecycle consistency. Actual Avalonia, current runtime
read access, independent Security, Registry/Trust/Eligibility and physical
storage/Pin/deletion remain separate. Wider parentsOPEN; #1144 BLOCKED; #1128
SUSPENDED/NOT_EVALUATED; RLS/Native/Production/Release HOLD. Security/shared CI
work stays with its P0 owner issues under the current ProjectIDE-only scope.
