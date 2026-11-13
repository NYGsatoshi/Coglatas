# ProjectIDE immutable semantic interpretation and conversion provenance

This bounded #905/#1034 Product repair follows the existing immutable semantic
input and typed evidence contracts. Independent qualification remains P0 #1938.
It changes collection ownership, not relation meaning or numerical algorithms.

## Reproduced semantic inconsistencies

The built-in Relation descriptor catalog exposed its mutable backing arrays
through ICollection.SyncRoot. Changing one descriptor's typed SourceKinds changed
a completed same-input compilation from Pass to Fail while the pinned descriptor
digest and canonical definition stayed unchanged. Other callers used the same
process-wide object. Public catalog storage could also replace a built-in entry.

A converted quantity's typed provenance had the same exposure. Replacing a valid
factor100 entry with factor101 replayed a retained result200 as202; replacing it
with null changed Known to Invalid. The original canonical converted quantity
still described the first result. These are local in-process counterexamples,
not demonstrated remote exploitation, a production incident or access authority.

A numeric result also exposed its retained input array. Replacing operand2 with20
changed a replay of2+3 from5 to23 while the retained value and basis still
described5. A short-circuited false predicate could expose an evaluated true
literal instead of its actual false premise. Original clean controls fail for
both cases; neither changes the already computed value.

## Owned views preserve existing meaning

The existing ImmutableListView owns the built-in catalog and every descriptor's
source/target kinds, allowed scopes, required capabilities and invariant labels.
No public synchronization view can mutate the shared interpretation or leak the
private shared entity-kind array. Catalog entries, order, descriptor IDs,
versions, payload schemas, roles, capabilities, scopes and canonical digests
retain their existing values.

ConvertedQuantity owns its ordered provenance list. Replaying exposed typed
provenance must reproduce its retained canonical result. Caller path mutation,
empty identity conversion and rejecting generic list edits keep their existing
behavior. No factor, rounding, compatibility, size or evaluation budget changes.

Numeric results own their ordered inputs, and predicate evaluations own only
the actually evaluated premises. Known values, comparisons, three-valued truth,
short-circuit order and input-specific evidence retain their existing meaning.
These changes add no arithmetic, tolerance, solver or numerical assurance claim.

This is storage integrity for already admitted built-ins. It does not enroll a
new formal Registry, Producer or Trust/Eligibility policy.

## Qualification and boundaries

Three initial functional counterexamples fail on the original source. Expanded
controls reproduce nine failures and two passing ownership/empty controls.
The corrected controls retain same-input full compilation, canonical descriptor
lists, catalog lookup, exact conversion replay and existing relation/unit cases.
Evaluation replay covers addition, multiplication and comparison; predicate
controls preserve the actual short-circuited premise. Global catalog mutation controls run in a nonparallel collection and restore
the original array in finally, including assertion failure paths.

Non-incremental builds, exact source identities, original failures and corrected
TRX are retained. The complete Core preview must remain byte-identical, and the
shared Windows pure preparation stays covered. This is not actual Avalonia,
runtime current authority, independent Security approval or full parent closure.

P0 #1938 remains open. Existing #1930/#1932 query/history responsibilities,
#1144 BLOCKED, #1128 SUSPENDED/NOT_EVALUATED, RLS/Trust/Eligibility/Native,
Production and Release HOLDs remain unchanged.
