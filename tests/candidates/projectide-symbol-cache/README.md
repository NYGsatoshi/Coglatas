# P0 #1938 — independent retained symbol-binding storage candidate

This Security-owned cohort independently exercises the public
`ProjectSymbolBindingCache`, `ProjectSemanticBinder.Bind` and
`PureCoreCompiler.Compile` APIs. The Sources, nested typed values, fixed contexts,
compiler inputs and materialized-snapshot claims are authored here using public
constructors and canonical JSON parsing. No Product test fixture, internal cache
entry, internal declaration constructor or fabricated compilation result is used.

The bounded handoff is
[#1938 comment 6955810655](https://github.com/NYGsatoshi/Coglatas/issues/1938#issuecomment-6955810655).
The existing immutable-storage cohort and every existing candidate pin remain
unchanged. This addition covers the caller-owned symbol cache introduced by
[#2066](https://github.com/NYGsatoshi/Coglatas/pull/2066); it does not close #1938
or assume that previously retained values become inaccessible after `Clear`.

## Explicit new Product pin

- Product commit: `508f641c2391f55f788d6658f3da78939a94308c`.
- Product tree: `6472827fd981a375e49d8a78992d67bc3d332ebf`.
- Independent C# blob: `af25db405319329c62479dfbd390b922e116d180`.
- The advisory workflow pins the exact composition runner and verifier control blobs in `RUNNER_BLOB` and `CONTROLS_BLOB`.
- Product SDK: .NET `10.0.403`, `rollForward: disable`, no prerelease SDK.
- Execution scope: `SYNTHETIC_SYMBOL_CACHE_CORE_ONLY`.

The handoff originally names #2066 source `ac02346d4e8ebe05fb28148c67ebd45707c0b515`.
This new cohort deliberately pins the stable merged descendant above. It includes
#2066's merged cache at `c664c36a56b3d3b6349d0ddd9a20efa0711fcbd9`,
[#2067](https://github.com/NYGsatoshi/Coglatas/pull/2067)'s fresh per-invocation
Binder target indexes at `666c7e8b4719ba3755c7ae486945d41d7ea5d613`, and the
subsequent full-index work #2068 plus test-only #2069. The pin is not a floating
Product branch or a claim that another source cohort was qualified. The separate
semantic-index handoff retains its own scope.

## Twenty-two independently required facts

| Fact | Required observation |
| --- | --- |
| `ColdAndWarmBindsPreserveCanonicalDeclarationsAndSymbols` | Genuine uncached/cold/warm bindings agree on canonical public fields and logical charges; warm binding actually reuses the same immutable declaration and symbol instances. |
| `CallerOwnedBuffersAndDocumentArraysCannotRewriteRetainedModels` | Changing caller-owned JSON buffers, document arrays and returned canonical-byte arrays leaves the original Source and warm model unchanged. |
| `PublishedModelCollectionsRejectWritableInterfaceSurfaces` | Model, Source, manifest, rule, root and port collections reject generic mutation and expose neither arrays nor non-generic collection/storage interfaces. |
| `NestedStructureAndMapCollectionsKeepPrivateStorage` | Decoded structure fields, type fields, list values, map entries and nested map values retain their concrete contents through rejected mutation and warm reuse. |
| `DetachedArraysAndCopiedRecordsCannotRewriteRetainedBindings` | Changed array copies, `CopyTo` outputs, dictionaries and copied symbol/reference/structure-field/map-entry records cannot rewrite retained objects. |
| `ContextAndLocationByteCopiesCannotRetargetSymbols` | Mutated context/location byte copies cannot change any symbol's exact context, document, entity or port property path. |
| `EqualReparsedContextsReuseExactPartitions` | Separately decoded equal Sources reuse exact cached partitions while each model retains its own actual input object. |
| `CommittedCandidateAndScenarioContextsRemainDistinct` | Identical declaration bodies in committed, candidate and Scenario contexts produce distinct symbol identities and preserve their original context/location fields after clearing. |
| `EditedPortsCannotRewriteRetainedModels` | A new immutable revision and changed port/default value produce new bindings without altering the old nested values, old port or old canonical model. |
| `ChangedDeclarationKindsDoNotReuseOldDecodeStatus` | A future declaration kind stays Unsupported, without a typed declaration or stale port symbol, through warm reuse; a corrected new revision decodes again. |
| `EntryEvictionPreservesOldNestedModels` | Repeated two-entry eviction preserves caller-retained models, and an evicted input obtains new objects with the same exact canonical result. |
| `CanonicalByteBudgetBypassKeepsBindingsUncached` | Entries too large for a one-byte declaration/context key budget remain uncached, with zero retention counters and equivalent fresh bindings. |
| `ClearDropsRetentionAndRebindsFreshObjects` | `Clear` resets entry/byte counters; rebinding creates fresh objects while previously published immutable data remains unchanged. |
| `BranchProjectAndTenantSwitchesCannotRetainAnotherOwner` | Each changed root ownership dimension flushes retained partitions, publishes only the current context, and permits a fresh correct return to the original owner. |
| `ConcurrentDifferentRootsKeepIndependentSymbolsAndValues` | Four concurrently scheduled owners repeatedly bind through one cache, preserving exact per-owner contexts, locations, nested values and uncached results after final clearing. |
| `ResourceTenantChecksRemainInvalidUntilCorrected` | A same-Tenant resource decodes, a different-Tenant resource stays Invalid through warm reuse, and a corrected new revision decodes without changing the earlier model. |
| `WarmCacheDoesNotAuthorizeDeniedMaterializedSnapshots` | Previously admitted external symbols disappear when the supplied host claim is denied, despite warm storage; restoring the original claim admits the original immutable symbols again. |
| `ChangedSnapshotPinsCannotReusePreviouslyAdmittedSymbols` | Wrong versions and changed context/body materializations fail pin admission without publishing cached external symbols; the exact original materialization still succeeds. |
| `PartialCoverageNeverBecomesCompleteThroughCacheReuse` | A new partial-coverage input can reuse immutable declarations while retaining its own input and incomplete finding, matching an uncached bind. |
| `CancelledBindsPreservePreviouslyPublishedModels` | Pre-cancelled binding throws before changing owner retention; original counters, objects and nested model remain usable by their existing caller. |
| `WarmCacheDoesNotBypassLogicalOperationBudgets` | A one-operation input fails before publishing declarations or symbols, identically with or without a warm cache; normal input remains usable afterward. |
| `FullCompilationMatchesColdWarmEvictedAndClearedBindings` | Actual full compilation establishes the positive control; original result bytes/digest, IR, model and logical charges agree across cold, warm, evicted and cleared cache states. |

The tests deliberately retain old objects while editing, clearing and evicting.
Cache removal is not memory erasure or revocation of references already held by
the caller. The byte counter measures canonical declaration/context key bytes,
not total managed memory. The concurrent control is a bounded ownership
regression, not a stress, capacity or quantitative performance qualification.

## Exact-source native evidence

The runner composes only the committed C# blob at
`tests/Coglatas.Tests/ProjectIde/SecuritySymbolCacheCandidateTests.cs` in a fresh
detached worktree of the exact Product commit. It verifies the Product tree,
actual compiler-input bytes, staged candidate blob and unchanged Product HEAD.
After composition, restore, execution and compiled discovery, the complete
staged delta must contain only the candidate, the unstaged tracked delta must
be empty, and no unexpected untracked source may exist. Every local Git
subprocess removes inherited
`GIT_*` overrides while retaining all other host and SDK variables.

Native admission requires exactly the 22 frozen fully qualified names, all
Passed, with no incomplete/error counters. It requires the native TeamTest/2010
root, one supported direct result/definition/entry inventory, exact class and
bare method fields, and complete result/definition/entry identity bijections.
Compiled discovery must contain the same exact roster. Unexpected, duplicated,
unsupported, missing, aliased or whitespace-substituted records fail admission.

The full report is checked for extra native result records, inner result
collections and result aggregations. An ErrorInfo anywhere is contradictory.
Only informational RunInfo entries in the native summary's RunInfos collection
are admitted. A warning leaves this passing-only cohort unqualified.

The Ubuntu 24.04 and Windows latest jobs use pinned Action commits and the exact
Product SDK. They share workflow-level Product, candidate, runner and controls
identities, retain each host's original artifacts independently, and keep raw
candidate source, TRX, discovery, command logs, source/composed-tree receipts and
retained-file SHA256 values. Workflow configuration does not itself establish
that either native job ran or passed.

```text
python -B tests/candidates/projectide-symbol-cache/compose_and_verify.py --product-head 508f641c2391f55f788d6658f3da78939a94308c --worktree <fresh-product-path> --evidence <fresh-evidence-path>
```

## Committed verifier controls

The adapted strict runner and its portable unittest methods preserve the
existing independently reviewed run-comparison evidence pattern. Synthetic
22-case reports/discovery establish positives before counter, Execution,
identity-bijection, class/method, container and raw-name rejection controls.
Every rejection requires the exact expected error message. The split-class/
dotted-method case preserves the concatenated full name while violating the
independently required class and bare method fields.

Disposable Git repositories exercise actual source bytes, candidate index
substitution, same-tree Product HEAD substitution and unrelated tracked-file
drift. An additional staged Product edit with restored working bytes proves
that the former net diff could hide an index substitution; separate staged and
unstaged checks reject it. An untracked C# input is independently rejected.
Both controls restore the valid composition and require it to pass again.
An intentionally unprotected read demonstrates that hostile `GIT_DIR`
can redirect Git; the protected runner remains on the requested repository and
leaves both disposable caller repositories unchanged. Fixture setup separately
scrubs inherited Git overrides before initialization or writes. Restored valid
compositions are admitted again.

```text
python -B -m unittest discover -s tests/candidates/projectide-symbol-cache -p test_compose_and_verify.py -v
```

Both native jobs check the controls blob and run these controls first, retaining
`admission-controls.log` and its exact `controlsBlob` workflow context. These are
synthetic verifier and disposable-Git controls. They do not execute C# or replace
original native reports, compiled discovery, exact-head CI or independent review.

## Preserved first native qualification failure

[Run 44215047316, attempt 1](https://github.com/NYGsatoshi/Coglatas/actions/runs/44215047316)
tested source `0970e4eedfbbce6ecfdd73706aba5fc47b9f286f` with C# blob
`42a83c7decd04f3ae2cefb66200b551c78c42d69`. The Ubuntu TRX recorded 22 passing
cases, but the `dotnet test -warnaserror` command returned nonzero because seven
`Assert.Single(collection.Where(predicate))` calls triggered `xUnit2031`.
The runner preserved `FAILED_OR_INCOMPLETE`; compiled discovery did not run.
Those partial results do not qualify the cohort.

The correction uses `Assert.Single(collection, predicate)` at those seven
sites while preserving every predicate, all 22 facts, Product, the strict
runner and the 16 verifier controls. The new source requires fresh successful
native commands and compiled discovery on both Windows and Ubuntu; the original
failed run remains historical evidence.

## Evidence limits and remaining owner work

This cohort is synthetic in-process Core evidence. Snapshot `IsAuthorized`
values are caller-supplied claims: their fresh treatment does not prove current
actor/session/Tenant authorization, HTTP privacy, durable storage, actual
revocation, deletion or export/retention policy. The scenario context case does
not execute a Scenario engine or certify UI lifecycle behavior. Runtime/query
privacy and ownership remain under #1930, with the broader retained-storage and
lifecycle obligations under #1938.

Every receipt retains `requiredCheckQualified`, `independentReviewComplete`,
`nativeUiQualified`, `productionQualified`, `trustQualified` and
`releaseQualified` as `false`. No ordinary required check is replaced. Keep
**#1938 OPEN**, #1144 BLOCKED, #1128 SUSPENDED/NOT_EVALUATED, and the existing
RLS/Native/Trust/Eligibility/Production/Release holds unchanged. Fresh hosted
Windows/Ubuntu execution and an audit of the original artifacts remain necessary
before claiming qualification of this exact source cohort.

## Isolated Python workflow execution (#2095)

Both hosted OS legs now use Python `-I -B` for the verifier and candidate runner, after independently comparing each executing script's committed Git blob **and unfiltered working bytes** to the pinned source identity. Recheck immediately before each execution, rather than trusting a previous Git-tree check to prove subsequent working bytes. A synthetic import-shadow control tests Python's isolation from an injected `PYTHONPATH`; static controls require both OS scripts to retain the pre-execution checks. No frozen C# candidate, Product source, security check, or six false qualification flags are changed.
