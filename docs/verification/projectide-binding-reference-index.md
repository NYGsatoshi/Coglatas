# Fresh ProjectIDE binding reference indexes

This #1030 / #905 Product change builds context and typed-symbol lookup indexes
once per Binder invocation, after fresh Source and supplied-snapshot admission.
References share these lookups instead of scanning the admitted snapshot list
and serializing a canonical entity/port key for every target.

The context key is the complete immutable SourceRevisionContext, including all
committed, candidate and Scenario dimensions and opaque preserved context data.
The symbol lookup uses context, EntityId and nullable PortId together. Published
CompilerSymbol keys retain their original canonical JSON bytes. Document
reference symbols retain their separate identity and do not become entity
lookup candidates when added during reference traversal.

Each lookup retains every matching snapshot and symbol. Matching snapshot
aliases still make a referenced context ambiguous, and denied/conflicting
contexts retain their earlier precedence. Typed value references use only
materialized/closure snapshots; ordinary references additionally admit the
local Source. Pin/version/digest, origin, kind and coverage checks remain fresh.
No resolved reference, authorization, diagnostic or semantic verdict is cached.
The indexes are local variables and disappear after the invocation.

All existing logical charges, input/result stamps, ordering, cancellation and
partial-result boundaries remain unchanged. The work removes repeated lookup
construction; it establishes no wall-clock, managed-heap or #1128 performance
claim. Product regressions exercise equal parsed contexts, repeated local and
external identities, entity versus port identity, all three context kinds,
aliases, denial/conflict, missing/mismatched pins, partial coverage and document
symbols. Actual prior/current Compiler differential evidence belongs to the PR.

This advances full-index construction and supports the existing shared Compiler
and language-service clients. Full dependency-directed reference invalidation,
semantic-verdict reuse, actual editor/Graph acceptance, current query/open/apply
authority and independent Security qualification remain open in #1030 and its
existing P0 handoffs. Actual Avalonia/Native, Registry/Trust/Eligibility/RLS,
Production, Release and #1128 HOLDs remain unchanged. No Security branch or
shared CI change is included.
