# Owned diagnostic premise rosters

This partial #1034 / #782 Product correction keeps a synthetic diagnostic basis
page's recorded premise roster immutable. It changes no Compiler result, digest,
artifact, Registry schema, runtime authorization or evidence meaning.

The prior page copied the actual premise references into an Array.AsReadOnly
wrapper. Under the qualified .NET 10 runtime its legacy ICollection.SyncRoot
exposed that copied array. A caller could replace a retained page entry with
another actual digest while the page still showed the original diagnostic,
input and result identities. The original Compiler result and its diagnostic
evidence remained unchanged; only the display page became inconsistent.

The page now owns a private copied IReadOnlyList view with only count, checked
indexing and enumeration. It exposes no collection mutator or legacy root that
reveals the array. Original entries, order, 32-item page bound, offset/total,
continuation, input/result/diagnostic identities and empty/missing evidence are
unchanged. LogicalStructureRecorded, CausalStructureRecorded and
PremiseBodiesResolved remain false; no logical DAG, causal relationship or
unrecorded body is inferred.

The regression compiles the actual existing synthetic inputs, requires a real
diagnostic with recorded premises and attempts the legacy array-root rewrite.
It checks the original Compiler result and genuine diagnostic evidence before
checking the retained page roster. An empty fixture cannot qualify the test.
The original failing runs on Windows and Ubuntu remain retained. Existing
bounded-page and lifecycle cases remain unchanged. Strict nonincremental builds,
full original ProjectIDE/architecture inventories and exact raw cross-host rows
are qualified for the current candidate separately from this contract. Copied
source timestamps must never let inherited incremental outputs certify a patch.

This is a pure ViewModel display correction. It grants no current read access,
trusted evidence, premise-body resolution, actual Avalonia acceptance or
independent Security promotion. Wider #1034 / #782 acceptance remains OPEN.
#1144 stays BLOCKED; #1128 SUSPENDED/NOT_EVALUATED; Registry/Trust/Eligibility/
RLS/Native/Production/Release holds remain. Security/shared CI work stays with
its P0 owner issues under the current ProjectIDE-only scope.
