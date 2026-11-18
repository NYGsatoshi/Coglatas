# Private semantic-index lookup reuse

Existing #1030 work adds an optional caller-owned
`ProjectSemanticIndexLookupCache` to full Core index construction. It retains
only private symbol-key/location and reference-origin positions for an exact
ordered roster. Every public index, symbol wrapper, reference roster, input
stamp, coverage decision, canonical body and query cursor is still produced
from the current actual binding closure.

The key uses exact length-framed ordered symbol keys/locations and reference
origins. Duplicate keys and locations remain non-unique; duplicate reference
origins remain unavailable for definition lookup. Table positions are always
resolved into the new index's actual rows. Names, declared types, status,
reference target/resolution and coverage are read afresh; retaining a position
does not retain an earlier symbol, reference, Source, model or permission.

The session has explicit LRU entry/key-byte limits and owner-scoped reuse
counts. Key bytes do not measure managed heap usage or establish a performance
SLA. Oversized keys use fresh tables without retention. Root Tenant/Project/Branch
changes clear prior tables even for a stale/empty index. Clear invalidates
pending publication by an earlier builder, including invalidation between build
start and lookup preparation. A build carries its original owner/epoch token
through preparation and commit; invalidation cannot restore an earlier owner. Prepared tables are committed only
after the complete index has passed its original derived-work, canonical-byte
and cancellation checks; a refused build publishes no partial roster or new
prepared cache entry. Existing Compiler and derived-index logical charges stay unchanged.

The pure Windows ViewModel can explicitly receive this caller-owned session;
its parameterless/default path stays available. Existing original Source
admission and compilation/query ownership checks still apply. Reloading an
index cannot reactivate an old symbol wrapper, page or Graph object. The caller
owns session clearing and retention across consumer lifetimes. The offline
exporter also opts in for its prepared index profile, without changing its
projection format, bounds or original output bytes. Angular continues reading
prepared Core output and runs no Compiler.

Product regressions use fresh uncached compilation and full-index construction
as the reference. They compare complete index bytes/digests, work charges,
definitions and every reference/completion page. Current metadata/target changes,
dependency admission, global input dimensions, contexts, bounds, late resource
exhaustion, cancellation, concurrent builders and pure ViewModel lifecycle are
explicit. Actual runs, raw identities, immutable source/artifact evidence and
review belong to the candidate PR qualification record.

This reuses lookup construction, not semantic validation or Compiler verdicts.
Complete editor/typed Rename and paired acceptance remain open in #1030/#782.
Existing P0 #1930/#1938 retain runtime query/open/apply/privacy/revocation and
independent retained-storage qualification. Actual Avalonia/Native, #1144/#1128,
Trust/Eligibility/RLS/Registry, main, Production and Release HOLDs remain. No
Security branch or shared CI is modified.
