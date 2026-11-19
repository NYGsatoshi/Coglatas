# P0 #1930 — independent full semantic-index Core candidate

This Security-owned cohort exercises the actual public
`PureCoreCompiler.Compile`, `CompilerBindingClosure.Build` and
`CompilerSemanticIndex` APIs with independently authored Sources, declarations,
typed relation endpoints, explicit input settings and fixed materializations.
It does not import Product test fixtures or construct internal compiler/index
objects. The previously merged Draft Rename cohort and its Product pin remain
unchanged.

The responsibility comes from the existing
[#1930 final-source handoff 6963705134](https://github.com/NYGsatoshi/Coglatas/issues/1930#issuecomment-6963705134)
and the preceding full-index handoff
[6961518117](https://github.com/NYGsatoshi/Coglatas/issues/1930#issuecomment-6961518117).
Product PR #2068 was Squash merged from
`1e1e993acb5e54765742c1f482194d2dcec1cfb6` with the identical source tree below.

## Frozen composition

- Product commit: `df213dd93ccfbba03ac0aa303abd53f4a2bf3c1d`.
- Product tree: `cd82ae56b4a9b6a3b1b83b1e34c4cb27a3eb2f0b`.
- SDK: the original Product .NET `10.0.403` policy.
- Execution scope: `SYNTHETIC_SEMANTIC_INDEX_CORE_ONLY`.
- Source, runner and verifier-control Git blobs are pinned once at workflow
  level in `projectide-semantic-index-advisory.yml` and recorded in both hosts'
  original workflow contexts.

The detached composition adds only
`tests/Coglatas.Tests/ProjectIde/SecuritySemanticIndexCandidateTests.cs` to the
immutable Product. The runner rejects a different Product SHA/tree, existing
candidate destination, dirty independent checkout, changed actual C# bytes,
changed candidate index, Product HEAD drift, unrelated staged or unstaged Product
edits, or unexpected untracked source files. The complete cached delta must be
the single candidate destination and there must be no unstaged tracked delta.
These checks run after composition, restore, execution and compiled discovery.
Every local Git subprocess strips inherited `GIT_*` overrides; unrelated host
and SDK settings are preserved. Raw C# bytes, Product/Test/composed identities,
TRX, compiled discovery, command logs and original SHA256 receipts are retained.

## Twenty-two independent facts

| Fact | Required observation |
| --- | --- |
| `FullIndexRetainsActualRegionalSourcesAndOriginalCompilerIdentity` | Three actual owned regions, fourteen symbols and eight original relation endpoints retain exact Sources, closure/input identity and the original compiler result. |
| `DefinitionsUseExactRootAndDependencyOwnedRelationOrigins` | Both endpoints of actual root and dependency-owned relations resolve to their exact context/entity, using original location paths. |
| `EqualEntityIdsInTwoPinnedRevisionsRemainDifferentDefinitions` | Equal entity IDs in two fixed revisions keep different keys, contexts and reference memberships. |
| `DeclaredUnusedSnapshotsEnterOnlyTheExplicitFullClosure` | Full declared closure includes unused admitted snapshots; reachable-only closure remains explicitly incomplete even when a finite query scan finishes. |
| `MissingDeniedAndConflictingMaterializationsCannotSupplyPrivateIndexRows` | Missing, denied, denial-plus-positive and conflicting-version supplies cannot expose the affected region, symbols, names or original dependency references; a restored valid supply succeeds separately. |
| `WrongVersionAndSameContextBodySubstitutionCannotBorrowAnInputPin` | A wrong materialization version or changed Source body under the old context/pin cannot supply indexed private rows. |
| `PartialCapturedCoverageCannotBecomeACompleteIndexOrPage` | Partial captured declarations/relations preserve incomplete coverage independently of finite query scan completion. |
| `StaleBuildContainsNoRegionsSymbolsOrReferences` | A stale expected input produces an empty stale index. |
| `StaleQueryInputRefusesCandidatesTargetsCountsAndContinuation` | Stale reads return no inspected member, target/candidate list, exposed count or cursor. |
| `ForeignTenantProjectBranchAndRevisionLocationsStayOutOfScope` | Each independently changed context component is rejected by inspect, definition, completion and reference queries. |
| `ExactLocationMembershipRejectsAlteredDocumentEntityAndPropertyPath` | An exact parsed location resolves to the retained original; changed document, entity or path cannot borrow membership or a definition. |
| `CompletionRequiresAnActualSupportedEntityReferencePort` | A Task, document or Boolean port cannot become an entity-reference completion origin; the supported original port returns the actual Tasks. |
| `CompletionPagesPreserveThirtyTwoResultBoundsAndExactOrderedMembership` | Seventy original Task candidates appear once in 32/32/6 result pages, with bounded scans and exact order. |
| `CompletionCursorsCannotCrossIndexInstancesOrOriginPorts` | Even an equal index digest does not transfer cursor ownership; a different origin is also refused and the original owner still works. |
| `ContinuationsCannotCrossCompletionAndReferenceQueryKinds` | Neither query kind accepts the other's original continuation. |
| `ReferencePagesPreserveOriginalRowsAndThirtyTwoResultLimits` | Seventy original fan-in references survive bounded pages exactly once, retaining their original object and location identities. |
| `EmptyTwoHundredFiftySixScanPageStillReachesItsLateOriginalReference` | A genuine 260-reference compile yields an empty first 256-scan page and one late original reference in the final four scanned rows, without conflating coverage with scan completion. |
| `ReferenceCursorsCannotCrossEqualIndexesOrTargetMembers` | A reference continuation belongs to the exact index instance and target member. |
| `ResourceExhaustionAndInvalidPageSizesNeverRelaxExistingBounds` | Exhausted derived work returns an empty resource-limited index; zero/33 page sizes and above-profile work requests are rejected. |
| `CancellationBeforeBuildAndQueriesPreservesTheOriginalIndexAndCompiler` | Pre-cancelled build, definition, reference and completion operations throw while preserving original bytes; an uncancelled read remains valid. |
| `PublishedRostersAndLegacySynchronizationRootsCannotRewriteMembership` | Public region/symbol/reference/page lists reject available generic/legacy mutation paths; any array synchronization copy cannot alter retained membership. |
| `DetachedJsonAndRecordCopiesCannotChangeRetainedIndexOrMemberIdentity` | Changed detached JSON, returned byte arrays and copied records cannot replace retained index data or the original member returned by exact lookup. |

The small regional fixture includes two distinct revisions on one external
Project/Branch, each with its own original local relation. Root relations bind
both exact revisions through separate Snapshot ID/version/digest pins.
Materialization changes are passed to a new real compile, not applied to a
fabricated index. These supplied local admission flags are not persisted server
authority.

## Bounds and explicitly separate client work

Core query limits stay 32 results and 256 scanned rows. Every derived index in
the common fixture path receives at most 1,000,000 logical operations. Small
fixtures use a 1,000,000-operation Compiler profile; the 70/130-edge fan-in
fixtures use the already supported, explicitly declared 3,000,000-operation
Compiler profile. Compiler execution and derived-index work are different
budgets. No Product limit, execution policy or global workflow is edited.

The late-result fixture additionally requires an actual scoped publication and
index bytes within 4 MiB. This is a Core/source bound, not execution of the Web
reader or its complete transport admission. The existing client 1M reader-work
and 4 MiB transport caps remain unchanged. The handoff's original 1005/1001 wide
Core fixture remains rejected by its unchanged preview transport; it is not
repackaged, restamped, imported or counted as an admitted client case here.
The Product-authored 165/301 example is also not reused as independent evidence.

Core lookup intentionally accepts a separately parsed *equal* location and
returns its retained member. It does not accept an arbitrary copied member as
authority. The separate Windows/Web original-body admission and member/page
checks, delayed hashing, lifecycle withdrawal, cancellation during publication,
reentry and preservation of a newer view remain unqualified by this Core cohort.

## Strict native evidence and verifier controls

The runner admits exactly the frozen 22 fully qualified C# facts with no
failure, skip, nonexecution or incomplete counter. It requires the native
TeamTest/2010 root and one supported direct result/definition/entry inventory,
exact class and bare method names, unique test and execution IDs, complete
identity bijections, and the identical compiled discovery roster. Duplicate,
foreign, aliased, unsupported or additional records fail admission.

The full report is checked for extra native result records, inner result
collections and result aggregations. An ErrorInfo anywhere is contradictory.
Only informational RunInfo entries in the native summary's RunInfos collection
are admitted. A warning leaves this passing-only cohort unqualified.

The committed Python unittest methods run synthetic TRX/discovery
positives and exact-error negative controls, plus real disposable Git source,
HEAD, index, unrelated-source and hostile-environment controls. The two added
source-inventory controls reproduce an unrelated staged-only Product change
hidden by restoring the working bytes, and an injected untracked C# file. Both
must fail at their intended guard and admit the composition again after repair.
The staged-only control explicitly demonstrates that the old net diff misses
the altered index. These are
verifier tests; their success is not C# execution or native qualification.
Both Ubuntu 24.04 and Windows latest run the controls before the native
composition, with immutable Action pins and the same Product/source/runner/
controls identities. Each host retains its original artifact independently.

```text
python -B -m unittest discover -s tests/candidates/projectide-semantic-index -p test_compose_and_verify.py -v
python -B tests/candidates/projectide-semantic-index/compose_and_verify.py --product-head df213dd93ccfbba03ac0aa303abd53f4a2bf3c1d --worktree <fresh-product-worktree> --evidence <fresh-evidence-directory>
```

Native qualification must be recorded only after the exact-source hosted runs
complete and original ZIPs, native TRX/discovery and source/member hashes have
been independently inspected. No local C# execution is claimed by preparation
of this candidate. Ordinary current-head required checks and source review
remain separate from the read-only advisory.

## Remaining P0 responsibilities

No current actor/session/Tenant/Project/Branch query/open/apply authority,
revocation, persisted permission race, runtime cache activation, durable index
ownership, heap/retention measurement, redaction/deletion/export, actual
Avalonia/Native UI, producer trust, Registry/eligibility or Production/Release
acceptance follows from these fixed-input tests. A digest, owned cursor,
complete closure or matching member is not an authorization grant. #1938 owns
the separate symbol-cache retained-storage qualification.

All receipts retain `requiredCheckQualified`, `independentReviewComplete`,
`nativeUiQualified`, `productionQualified`, `trustQualified` and
`releaseQualified` as **false**. Keep #1930 and its broader Product parents OPEN,
#1144 BLOCKED, #1128 SUSPENDED/NOT_EVALUATED and Release NO_GO. This candidate
does not change Main, protections, required checks or activation settings.
