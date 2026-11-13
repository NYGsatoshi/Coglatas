# ProjectIDE incremental declaration decoding

This is an initial #1030 index optimization under #905's full Compiler reference.
It reuses only immutable declaration shape decoding by exact canonical data,
document identity and codec limits. A caller-owned cache has explicit entry and
canonical-byte bounds, LRU eviction and Clear. Changing Tenant/Project/Branch
clears retained entries. A revision/candidate change can reuse an unchanged
declaration shape; bound symbol contexts and all input stamps are newly derived.

Every invocation still admits Source, applies the normal logical/numeric budget,
rechecks materializations and their authorization flags, rebuilds references,
performs global semantic validation and derives diagnostics/results. Settings,
rules, descriptor/unit/calendar locks, observations, package data and coverage are
not part of a cached verdict. External snapshot decoding is not reused. Resource
Tenant checks also run on every returned declaration. Unknown/invalid shapes keep
their original status and Source data; caching does not interpret opaque payloads.

The retained graph owns its collection storage behind read-only list and
dictionary views. Existing generic IList/IDictionary mutation APIs still reject
writes with NotSupportedException; the views expose no non-generic ICollection
or synchronization-storage API. Standard read-only wrappers are insufficient:
their non-generic ICollection.SyncRoot can expose the mutable backing array or
dictionary. Declaration/Type lists, nested values, predicates and dimensions use
private-storage views whose enumeration, Keys and Values expose no such path.

The local .NET host accepts an explicitly caller-owned cache. The default remains
uncached. This provides the same computation seam to Windows pure preparation;
it adds no operational transport or actual Avalonia acceptance. The offline Web
fixture exporter opts in and compares each result to uncached full compilation
before exporting. The generated contract remains byte-identical. Angular still
consumes prepared fixtures and does not run a browser Compiler or reuse server
authorization decisions.

Differential coverage includes Draft Rename, dependency/input changes, loss or
revocation of materializations, bounded retention, explicit Clear, codec/document
identity, cross-Tenant rejection and concurrent local-host generations. Retained
canonical bytes are not a managed heap measurement or a performance SLA. A
cancelled request produces no accepted current result; immutable decode entries
alone cannot authorize a later request.

This does not complete the incremental Semantic Index acceptance criterion:
dependency-directed incremental binding/validation, broader differential traces,
real editor integration and operational lifecycle/permission/retention qualification
remain required. Callers must own and clear this local session at their lifecycle
boundaries; no runtime cache activation is introduced. #1030 stays open. Existing
P0 #1930 retains runtime index/query authority, with Security execution outside this
ProjectIDE agent's scope. Native, Trust/RLS, Production, numerical and Release HOLDs
remain unchanged.
