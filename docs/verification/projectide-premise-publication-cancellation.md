# Owned diagnostic premise publication cancellation

This partial #1034 / #782 Product correction checks cancellation after the
Windows pure ViewModel publishes an actual retained premise snapshot and notifies
observers. It changes no Compiler result, diagnostic, body catalog, source
admission, digest, budget, logical/causal interpretation or runtime authority.

The existing admission-time token checks did not cover cancellation inside the
final PropertyChanged callback. An actual existing Compiler diagnostic supplies
three captured bodies. A subscriber cancels at publication; the original method
returns with that cancelled view still current. A second real counterexample
starts a newer premise read in the callback before cancelling the older request:
the newer view remains valid, but the cancelled older caller does not throw.
Both original cases fail on Windows and Ubuntu with exact raw identities. The
original Compiler data, result object and newer successful view are asserted
unchanged before those failures.

After the publication callback, a cancelled token withdraws only the snapshot
still owned by the same query generation and exact snapshot object, then throws
OperationCanceledException. A newer read, selection, admission or lifecycle
transition has advanced ownership and cannot be cleared by the older caller.
The existing initial-admission checks remain. No cancellation registration or
long-lived token ownership is introduced.

The two actual-data regressions retain the original result and previous caller
object, require the callback and token cancellation to have occurred, verify the
newer replacement when present, and require the exact cancellation exception.
They do not invent bodies or use an empty fixture. Strict nonincremental builds,
complete original inventories, raw cross-host identities, source pre/post pins
and static/review qualification are recorded separately for the candidate. The
first Linux source-pin refusal is retained; it executed no tests and was
reconciled with exact current inputs without weakening the guard.

This is local presentation consistency. Retained caller objects are not physical
deletion. Actual Avalonia, current actor/Tenant/Project/Branch authority,
independent Security, Registry/Trust/Eligibility and runtime activation remain
separate. Wider parents stay OPEN; #1144 BLOCKED, #1128 SUSPENDED/NOT_EVALUATED,
and all RLS/Native/Production/Release holds remain. Security/shared CI work stays
with its P0 owner issues under the current ProjectIDE-only scope.
