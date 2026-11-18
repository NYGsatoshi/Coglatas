# Incremental ProjectIDE symbol binding

This #1030 / #905 Product change adds an explicitly caller-owned
`ProjectSymbolBindingCache`. It reuses immutable declaration decoding and the
entity/port symbols derived from one exact declaration, document, codec limits
and Source context. An unrelated declaration edit can retain unchanged symbol
partitions; a changed declaration or context binds a new partition. Pinned
materialized dependencies can retain their exact symbols while the root input
advances. Every returned model and query inspection still belongs to its new
actual input.

## Admission and invalidation

The Binder performs its existing Source/numeric/logical budgets, complete
materialization enumeration, current supplied authorization flags, pin/digest,
conflict and supported-envelope checks before consulting retained symbols.
Denied, missing or mismatched materializations cannot be supplied by the cache.
The cache stores no admitted snapshot registry, permission, resolved reference,
diagnostic, Compiler result, semantic validation or execution plan. References,
coverage, diagnostics, dependency closure and global graph/semantic validation
are rebuilt for every invocation. Without a separate optional
[reference-binding session](projectide-incremental-reference-binding.md), all
reference resolution is also fresh. Rules, descriptors, packages, units, calendars,
observations and execution limits retain their original fresh processing.
Logical charges match full compilation even when symbol construction is reused.

Keys include canonical declaration data, its actual codec limits, declaring
DocumentId and the complete committed/candidate/Scenario context. Resource
Tenant validation precedes retained binding construction. The owning root
Tenant/Project/Branch scopes the entire session; closure regions use that same
owner rather than changing ownership to each pinned dependency. A different
root ownership clears retained entries. Revision changes do not authorize
reusing symbols with an old context. Returning to an identical retained input
may reuse its exact immutable symbols; its new inspection still rejects old
completion/reference cursors.

The session has explicit entry and canonical-key-byte limits, LRU eviction and
Clear. The byte counter covers declaration/context canonical key text, not
managed heap size or a performance SLA. Private collection storage preserves
prior model facts and rejects generic mutation. Callers own lifecycle clearing;
retention is not a current permission grant.

## Local adoption and qualification

Ordinary PureCoreCompiler compilation, direct Binder/closure calls and the local
.NET host accept the optional session. Defaults remain uncached. When supplied,
the symbol session owns declaration decoding for that call; the older optional
decode-only cache is not consulted. Recorded capture APIs keep their existing
behavior. The offline Web fixture exporter opts into symbol reuse and compares
with uncached compilation before exporting; Angular consumes prepared output
and does not run the Compiler or acquire server authority.

Product regressions cover actual unchanged-symbol identity, edited dependency
ports/kinds, new root revisions, loss/revocation/mismatched pins, partial coverage,
all global input dimensions, document/codec identity, bounded retention, ownership
switches, private storage, resource Tenant checks, concurrent local-host
requests and cancellation. Current execution and source/artifact qualification
belong to the PR evidence.

The same differential oracle now builds the full regional semantic index from
each uncached and symbol-cached compilation. It compares canonical index bytes,
digests, derived logical charges, every original reference definition, and every
symbol's inspection, typed completion and reference-search pages with page size
one. Query states, exact anchors/input stamps, candidate/reference order, coverage,
scan counts, empty continuation pages and termination must agree. The existing
dependency admission/replacement, rule, unit, calendar, observation, descriptor,
package, budget, context, document and bounded-retention cases all use this oracle.
Compiler result and Source bytes remain unchanged by the queries. An identical
warm recompile also rejects the earlier full index's completion/reference cursors,
even while actually reusing the original immutable symbols.

These are Product differential regressions against fresh uncached compilation.
The separate reference-binding session adds dependency-directed resolution reuse;
neither session provides independent acceptance.

This is incremental declaration/symbol construction, not dependency-directed
reference resolution or semantic-verdict reuse. Full compilation remains the
correctness reference. Full incremental-index/editor/Graph acceptance, current
query/open/apply authority and runtime retention qualification remain open in
#1030 and existing P0 #1930. Actual Avalonia/Native, Registry/Trust/Eligibility/RLS,
Production, Release and #1128 numerical HOLDs remain unchanged. No Security
branch or shared CI change is introduced.
