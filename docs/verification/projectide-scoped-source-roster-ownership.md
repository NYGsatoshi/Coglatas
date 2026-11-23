# Owned admitted Source rosters

This partial #782 / #1034 Product correction keeps the Windows pure ViewModel's
admitted Source display roster faithful to its exact accepted Core projection.
Compiler execution, original result/Source bytes, admission, navigation and
current runtime authority are unchanged.

The prior Array.AsReadOnly copy exposes its backing ProjectSource array through
legacy ICollection.SyncRoot under the qualified .NET 10 runtime. An actual
compiled three-Source graph permits replacing the first displayed slot with a
second already admitted Source. Original Compiler result and CoreScopedIr facts
remain unchanged while the displayed roster no longer matches the Core regions.
The same actual-data regression fails on Windows and Ubuntu. This demonstrates
display inconsistency; it does not demonstrate foreign navigation or an access
grant. Navigation still looks up the canonical admitted Core regions.

A private owned IReadOnlyList now copies the original ProjectSource references
and exposes only count, checked indexing and enumeration. No legacy collection
root reveals its storage. Original order, Source identities, exact versions,
current compilation/request/scoped-IR ownership and empty lifecycle states stay
unchanged. Existing admission, selection and navigation guards remain intact.

The regression uses the actual Compiler host and nonempty admitted graph, attempts
the legacy-root rewrite using another already admitted Source, then checks the
unchanged original Compiler/result and CoreScopedIr facts before the displayed
sequence. Existing reset, reentry, version, denied/partial admission and navigation
cases remain. Strict nonincremental builds, original inventories, raw cross-host
identities and source pre/post pins qualify the candidate separately. An initial
Windows source-pin refusal executed no tests; its original log and the subsequent
exact-input actual failure remain retained without weakening the guard.

This is offline Product presentation consistency, not actual Avalonia acceptance
or independent Security qualification. Wider #782 / #1034 acceptance stays OPEN;
#1144 BLOCKED, #1128 SUSPENDED/NOT_EVALUATED and Registry/Trust/Eligibility/RLS/
Native/Production/Release holds remain. Security/shared CI work stays with its
P0 owner issues under the current ProjectIDE-only scope.
