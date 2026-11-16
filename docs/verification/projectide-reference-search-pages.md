# Bounded local reference search pages

This partial #1030 / #905 Product slice follows the existing #888 / A-R2
Language Service direction. It adds an in-process Core query over an already
admitted immutable semantic inspection. Current query/open authority remains the
caller's responsibility; a matching digest, index hit or cursor grants no access.

## Query contract

`CompilerSemanticInspection.FindReferencesPage` requires the expected input
digest and the exact location of a decoded unique symbol in that inspection.
Stale inputs, foreign contexts, missing/ambiguous symbols and foreign cursors
return explicit refusal states without references, target, input digest or scan
facts. The in-process continuation is owned by one inspection and resolved
SymbolKey; it is not a serialized server token.

Each call examines at most 256 canonical references and returns at most 32
resolved references for that exact key. Page size is explicitly 1–32. Results
retain the existing projection's actual object identities and canonical order.
An empty 256-reference prefix still carries a continuation when work remains;
a real match beyond that prefix remains reachable. Cancellation throws before,
during and after the bounded scan.

`IndexCoverageComplete` describes binding and decoded unique-symbol coverage.
`ScanComplete` describes exhaustion after this page, not global absence,
Compiler result coverage, Source map readiness, trusted evidence or current
access. Callers must account for preceding pages and partial coverage. Page
collections are owned immutable views. The existing unpaged query and original
semantic-inspection bytes/digest are unchanged.

## Adoption and verification boundary

The primitive preserves the existing unpaged queries. The subsequent
[paired local adoption](projectide-paired-reference-pages.md) uses actual Core
pages in the Windows pure ViewModel and a Core-prepared Angular offline profile.
Legacy query limits and Source-map/navigation checks remain unchanged. Actual
Avalonia, runtime current-actor query/open checks, persisted/incremental indexes
and full #1030 editor acceptance remain separate work. No Compiler artifact,
Registry schema, dependency or CI/Security change is introduced.

Focused regressions use real distinct Project Test assertion origins bound to
one existing typed Task port. They cover all 70 references across three pages,
a 257-reference empty prefix, a real late match, foreign inspection/symbol/input
cursors, every context dimension, bounds and cancellation. Strict builds and
full ProjectIDE/architecture inventories are qualified separately on Windows and
Ubuntu; execution status belongs to the current PR evidence, not this contract.

Actual Avalonia, independent Security, runtime authorization/retention and
Registry/Trust/Eligibility/RLS/Native/Production/Release gates remain separate.
#1144 remains BLOCKED; #1128 SUSPENDED/NOT_EVALUATED. Wider parents remain OPEN.
