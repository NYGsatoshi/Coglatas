# Local reference neighborhoods

Existing #1030 / #782 work renders the current Find References page as a bounded
reference neighborhood in the Angular Workbench and the Windows pure ViewModel.
Each edge is an original resolved reference from that page to the selected
definition. This is a view of reference identity, not a causal, scheduling or
execution graph. It does not infer additional relationships or global degree.

The same Core-prepared query, exact input/context, Source admission, coverage and
32-result / 256-scan limits apply to list and Graph views. The Graph view contains
one definition target and only this page's original reference origins. An empty
continuation remains distinct from an exhausted scan. Both renderers navigate
the original regional Source through the existing owned query/member checks.
Layout selection is local presentation state; no Compiler or prepared artifact
schema is added or changed.

The pure ViewModel publishes a detached read-only edge roster tied to its actual
current reference page and query generation. Copied edges and older Graph objects
cannot replace a newer Source target. Query replacement, cancellation, Source or
selection changes, reset and disposal withdraw the old projection. Angular reuses
the current admitted query directly, with native keyboard buttons, responsive
layout and localized labels. Losing read permission removes the protected view.

Product tests cover actual regional Source/definition navigation, 32+4 pages,
stale page refusal, ownership replacement, read-only storage, permission loss and
the original empty 256-reference continuation. Original fixtures and Compiler
results remain unchanged. Actual execution and immutable source/build/browser
qualification belong to the candidate PR evidence.

This advances local Graph integration; it does not complete #1030 or #782.
Actual editor/typed Rename, dependency-directed full reference invalidation,
current runtime query/open/apply authority and independent acceptance remain
separate. Existing OPEN/P0 #1930/#1938 retain Security responsibilities. Actual
Avalonia/Native, #1144, #1128, Trust/Eligibility/RLS/Registry, main, Production and
Release holds remain unchanged. No Security branch or shared CI is modified.
