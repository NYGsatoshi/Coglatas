# Owned recorded simulation read cancellation

This partial #907 / #782 Product correction checks cancellation after the pure
ViewModel publishes comparison or trace-profile views over already completed
immutable synthetic recordings. It starts no simulation, Compiler run or external
operation and changes no original run, result, events, verdict or elapsed data.

Actual existing Compiler/owned-host fixtures produce nonempty event recordings.
A subscriber cancels when a comparison/profile is published: the original method
leaves that cancelled read current. A second case finishes a newer successful read
before cancelling the older caller: the replacement survives, but the old caller
returns without a cancellation exception. Four original cases fail on each host.
Original Compiler/result, current run, before recording for comparison, prior
summary data and the newer replacement are asserted intact before failure.

After notification, cancellation withdraws only the exact snapshot still owned by
that comparison/profile generation, using its existing clear operation, notifies
observers and throws OperationCanceledException. A superseded caller cannot clear
a newer read. Existing initial cancellation, missing/refused/bounded reads, run/
request ownership and lifecycle behavior remain. There is no persistent token
registration, new abstraction, recording rewrite or authority change.

The regressions own and detach their subscriptions and retain original immutable
facts. Fresh strict nonincremental builds, source pins and raw cross-host suites
qualify the candidate separately. This is read-only offline presentation
consistency, not actual Avalonia acceptance, numerical assurance, performance
qualification or current runtime access. Wider parents remain OPEN; #1144 BLOCKED;
#1128 SUSPENDED/NOT_EVALUATED; Registry/Trust/Eligibility/RLS/Native/Production/
Release HOLD. Security/shared CI responsibilities remain with existing P0 issues.
