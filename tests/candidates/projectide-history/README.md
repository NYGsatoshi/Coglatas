# P0 #1932 — independent local History, Diff and inverse-Draft candidate

This Security-owned candidate exercises actual public `ProjectSourceHistory`,
`ProjectSourceDelta`, `ProjectSemanticDiff` and `ProjectDraftRevert` APIs with
twenty independently authored synthetic facts. It supplies its own document,
Task, opaque-record, revision, Compiler-input and lineage fixtures. It imports
no Product test helper, fixture, expected result or prior execution receipt.

The boundary is already materialized local Source integrity. A successful
reconstruction, semantic comparison or inverse Draft grants no historical
access, current actor authority, database write, reviewed application or Merge
permission. Input-object immutability and all-or-nothing plan publication are
distinct from durable transaction atomicity.

## Exact source composition

| Source | Immutable identity |
| --- | --- |
| Product commit | `ea339ed0bc094630654262ebb28568a95ba4198f` |
| Product tree | `3170396d28e390759acc454194eba411afef2749` |
| Product `ProjectSourceHistory.cs` | `7364ea04462ed420fea080a9feaa684cfd3c7aca` |
| Product `ProjectSemanticDiff.cs` | `1afc7116cd0d7bb94cec49357c996dd1a0019e7b` |
| Product `ProjectDraftRevert.cs` | `36b3ef17ec027a71681688c6f0c502cce408cdd0` |
| Product shared `ProjectDraftRename.cs` transition | `fb998c7ee934ef39c577f0f677eabcc3f7d81156` |
| Product `ProjectSourceCodec.cs` | `b0c3323032d708f18fc65acc9a9bb15419b4a99f` |
| Independent C# candidate | `8ddbb5b95651fb6a3ba68f1d6db67f6421dac9df` |
| Independent composition runner | `746d05e10c5808219b2e280cd626106e4918ab8c` |
| Committed verifier controls | `010f9791df5edee780a8982e4a8cde09f44da6be` |

The runner stages only the exact independent Test blob at
`tests/Coglatas.Tests/ProjectIde/SecurityProjectHistoryCandidateTests.cs` in a
fresh detached worktree of this Product. It checks actual regular-file bytes,
the exact candidate index blob, the immutable Product HEAD, and that no other
tracked Product path changed. These checks execute after composition, after
restore immediately before compilation, after native execution before report
admission, and after compiled discovery. The original source is retained as
`candidate-source.cs` beside original logs and reports.

Every local Git subprocess, including the raw source read, excludes inherited
`GIT_*` environment variables. Repository, worktree, index, object-store and
configuration overrides therefore cannot redirect these checks to another
checkout. Ordinary host and SDK variables remain inherited, and non-Git
commands retain their original environment.

The read-only, nonrequired advisory runs separate Ubuntu-24.04 and
Windows-latest jobs. Both inherit shared immutable Test/runner/controls blob pins, the same Product
commit and its actual `global.json` policy. The actual SDK must be **10.0.403**
before `dotnet test -warnaserror` executes. No registered Required Check,
Product implementation, branch protection or existing CI lane is changed.

## Twenty frozen public-API facts

| Fact | Required observation |
| --- | --- |
| `CheckpointReadPreservesExactSourceAndRequestedRevision` | A minimal checkpoint reconstructs identical canonical Source bytes, digest and exact revision. Structural admission remains its own result axis. |
| `ForwardDeltaReconstructsExactBytesIndependentOfRecordOrder` | A real changed-record delta reconstructs the exact after Source from either supplied-record ordering; original before/after identities and digests remain bound. |
| `DeltasRetainOpaqueMetadataAndOriginalAddedRemovedValues` | Removed and added declaration values, changed document bytes, opaque Source/manifest metadata and both original sides survive reconstruction. Unsupported structural data receives no semantic-completeness credit. |
| `CompactedCheckpointPreservesLaterDeltaAndMissingAnchorRefuses` | Replacing older ancestry with an exact intermediate checkpoint preserves the later Source and records only the actual retained path. Removing its required anchor refuses. |
| `MissingOrUnavailableHistoryNeverExposesSourceOrPath` | Empty, absent and explicitly unavailable history returns no Source, admission status or path; null supplied records are corrupt. A separate positive remains reconstructible. |
| `CheckpointIdentitySchemaAndBodyFailuresHaveExplicitOutcomes` | Wrong digests, wrong revision context and contradictory bodies under one version are corrupt. Future Source schema remains Unsupported without a body. |
| `DuplicateSelfParentAndCycleLineageAreRefused` | Duplicate revisions, duplicate parents, self-parenting and a known cross-Branch parent cycle cannot produce a reconstruction. |
| `ForeignTenantProjectAndParentScopesAreRefused` | Even an extra foreign record or foreign parent in an otherwise usable set refuses before Source disclosure. Delta construction cannot cross those Branch boundaries. |
| `SameBranchSequenceMustIncreaseButCrossBranchSequenceIsIndependent` | Equal/decreasing same-Branch counters fail, while an independently numbered cross-Branch checkpoint parent is permitted. Cross-Branch or same-revision deltas remain invalid. |
| `RecordAndDepthBoundsRefuseWithoutPartialHistory` | Exactly 256 supplied records, 32 reconstruction records and eight parents are admitted by their applicable positive controls; one additional item refuses without partial history. |
| `CancellationDuringHistoryEnumerationDisposesAndPreservesInputs` | Cancellation during an actual supplied enumeration disposes it, throws and retains original Source bytes. Already cancelled reads/delta creation also refuse. |
| `RetainedHistorySourceAndDeltaCollectionsCannotBeMutated` | Caller parent-array replacement, generic and legacy collection interfaces, copied arrays and copied bytes cannot change retained parents, documents, manifest IDs, deltas or reconstruction path. |
| `SemanticDiffBindsOriginalValuesToTheirExactSides` | One actual display-name change retains original values, typed identity and correct before/after locations. Equal semantic content across distinct revisions can report NoChanges without mixing their identities. |
| `SemanticDiffRefusesMissingStaleAndForeignInputsWithoutMetadata` | Missing inputs, either stale expected digest, another Project and another Tenant return neither rows nor before/after metadata. |
| `SameVersionContradictionsAndOpaqueChangesRemainIncomplete` | Contradictory same-version bodies remain Incomplete. Opaque Source, document and manifest changes retain their original values and side contexts without complete-comparison credit. |
| `DiffBoundsNeverPublishATruncatedEquality` | Two real changes succeed at a two-row budget and fail at one row with no truncated equality. Invalid bounds and cancellation remain explicit. |
| `InverseDraftRestoresChangedRecordsAndPreservesLaterIndependentWork` | The inverse of one earlier edit restores that record while retaining a different later record. The whole candidate equals an independently constructed expected Source and retains the current settings and exact reverted identities. |
| `InverseDraftRejectsOverlappingChangesAtomically` | A later overlap prevents all candidate publication even though another target record could be inverted. The specific conflict and unchanged current bytes are retained; an independent positive restores both target records. |
| `InverseDraftRequiresExactHistoryAndCurrentProposalBasis` | Missing/substituted historical bodies, stale expected current input, wrong Proposal base/head, multiple reverted parents and another Branch cannot prepare an inverse. |
| `InverseApplicationRejectsStaleBodiesSettingsAndCancellation` | Whole-input application succeeds only for the exact current basis. Changed same-version Source, revision, settings or cancellation refuses while preserving both original and prepared bytes. |

Fixtures follow the pinned public codec's structural headers. Every declaration
has its own typed identity, owning document, namespaced kind, version and
payload; the opaque extension roster includes its required `namespace` key.
Document digests and the manifest are produced through `SourceDocument.Create`
and `ProjectSource.Create`, then re-admitted through the real codec. Compiler
inputs use schema version `1` and the required Schema/Declarations/Relations
rule baseline. Opaque examples deliberately remain unsupported to the semantic
binder rather than pretending to be complete models. No persisted records,
runtime authority service, private Source or operational effect is involved.

## Evidence admission and qualification status

The native reader requires exactly **20/20 distinct actual compiled facts**,
zero failed/skipped/unexecuted or other non-PASS counters, the native
TeamTest/2010 namespace and exactly one supported result/definition/entry
container. Original result names, separate full class and bare-method metadata,
and unique test/execution IDs must form an exact bijection across all three
inventories. Original compiled discovery must equal the frozen full-name set;
substring matches, substituted names and trailing whitespace cannot qualify.

Retain each host's original TRX, discovery, raw C# source, SDK/restore/test logs,
source/composed-tree receipts and SHA256 manifest. Verify the original hosted
ZIP's reported size, SHA256 and member bytes before accepting its internal
hashes. Both hosts must agree on Product/Test source, composed tree and raw
test-ID/name identities; host execution IDs and archive hashes may differ.

Original preparation included ad hoc local parser, discovery and temporary-Git
checks alongside syntax, roster, workflow and structural-header review. Their
commands were not retained as a committed regression suite; those historical
checks do not supply a reproducible repository test count.

The committed `test_compose_and_verify.py` now supplies **27 portable unittest
methods**. Synthetic native-shaped TRX/discovery positives precede targeted
counter, Execution, binding, class/method, container and raw-name refusals.
Disposable Git repositories exercise actual source, index, same-tree HEAD and
unrelated tracked drift. A hostile `GIT_DIR` demonstrably redirects an
unprotected read while the runner stays on its requested repository, with every
disposable caller file unchanged. Fixture setup independently scrubs inherited
Git overrides before initializing either repository. Negative checks require
the exact intended rejection reason, and restored valid compositions pass.

```text
python -B -m unittest discover -s tests/candidates/projectide-history -p test_compose_and_verify.py -v
```

Both advisory jobs validate the controls blob and run this suite before native
composition. Each retains `admission-controls.log` in its workflow-context
directory and records `controlsBlob` in `workflow-context.json`. The separate
native evidence directory and its eight retained files remain unchanged.

These verifier tests do **not** execute C#, reproduce a Product build or supply
native qualification. Native TRX, compiled discovery and current CI/review
remain separate gates for the exact published head; previous hosted results
retain only their original source identity.

The Git-environment follow-up used actual Git with fresh local checkouts of the
fixed Product. Three formerly admitted drift cases (repository/worktree,
worktree-only and alternate index redirection) were refused by the updated
runner. Two `core.worktree` configuration-override cases stayed refused on this
host. Clean composition and exact raw-source reads remained valid with hostile
common-directory, object-directory and alternate-object variables; host/SDK and
non-Git command environments stayed intact. The C# blob and fact roster did not
change. These are verifier controls, with no .NET execution result.

The original #1933–#1936 Product counterexamples and developer outcomes remain
historical evidence for their actual sources. This candidate uses the current
pinned Product and does not relabel those outcomes as independent or current
execution. Its eventual `SYNTHETIC_HISTORY_DIFF_INVERSE_DRAFT_PASSED` receipt
describes only the twenty local facts. All six broader flags remain false:

```text
requiredCheckQualified = false
independentReviewComplete = false
nativeUiQualified = false
productionQualified = false
trustQualified = false
releaseQualified = false
```

## Open acceptance

Keep #1932 and #908 OPEN. Current persisted actor/session/Tenant/Project/Branch
history capability, old-name/existence privacy, revocation, retention,
tombstones, required deletion, storage/compaction finalization, durable reviewed
Revert application, transaction/idempotency/audit/required Outbox and actual
Angular/Native UI qualification remain separate. Local processing limits are
not operational retention policy; Pin and a correct digest grant no access.

No independent formal approval or Security Owner governance result is inferred
from the advisory. #1144 stays BLOCKED, #1128 stays SUSPENDED/NOT_EVALUATED,
and Release stays NO_GO.

From a clean independent Security checkout containing the exact Product commit,
with fresh disjoint output directories:

```text
python -B tests/candidates/projectide-history/compose_and_verify.py --product-head ea339ed0bc094630654262ebb28568a95ba4198f --worktree <fresh-product-path> --evidence <fresh-evidence-path>
```

## Shared evidence admission follow-up (#2095)

The unchanged native candidate and immutable Product pin retain their original
execution scope. Native admission rejects every InnerResults container, every
TestResultAggregation record (including empty records), and every XML record whose local name ends in TestResult outside the exact direct
inventory. This includes Passed or NotExecuted GenericTestResult copies beneath
cases, summaries or definitions with otherwise complete passing outer metadata.
It also refuses ErrorInfo or non-informational RunInfo records. Copied-report
controls preserve the authentic outer names, counters and identities.
Only Information RunInfo messages satisfy this cohort's admission policy.
A Warning or another outcome is refused as incomplete evidence even when
all native facts pass; this does not reclassify warnings as native failures.

Before relying on a Product diff, every composition check reads NUL-delimited
Git path/index records as raw bytes and rejects assume-unchanged, skip-worktree
and other nonordinary flags. Staged and unstaged changes remain separate,
with rename detection disabled so a paired addition cannot hide a deletion.
Untracked and ignored source outside allowed roots is refused. After restore,
bin/obj roots beside committed Product csproj files are admitted by path;
the check does not establish how their contents were produced.
Nested or unrelated output-looking names cannot hide new compiler input.

Every restore, native test and compiled-discovery command disables ancestor
Directory.Build.props and Directory.Build.targets imports. A disposable real
SDK probe pins 10.0.403 with roll-forward disabled and checks the selected
version before poisoning imports. It demonstrates each parent import failing, then checks
that the isolated command succeeds. Its custom VSTest target exercises import
behavior only; it executes no Product C# fact and adds no native case credit.

Both workflows compare committed and actual runner/control bytes immediately
before each Python invocation, use git hash-object --no-filters, and invoke
Python with -I -B. Actual PowerShell bootstrap controls reject a changed script
before its canary runs and verify isolation against local/PYTHONPATH import
shadows. Each script has distinct canary bytes and its own blob pin. Every
launch block must bind its declared runner/control roles, and the marker
identifies the exact executed script. These cohorts have no shared provenance helper; this does not create
an executing-workflow provenance receipt that was absent previously.

The committed suite contains 27 unittest methods, including the original
controls. Source/input, copied-report, SDK and bootstrap checks remain separate
from the unchanged native suite and fresh hosted Windows/Ubuntu execution.
All six broader qualification flags remain false. Complete shared #2095
acceptance, current authority, Native UI, production and Release qualification
remain open; #1144 stays BLOCKED, #1128 SUSPENDED / NOT_EVALUATED and Release NO_GO.

## Whole-document RunInfo location control (#2095)

The native History report reader admits informational RunInfo only directly under the native ResultSummary/RunInfos path. Misplaced or extension-namespace RunInfo and RunInfos containers are refused, including empty containers outside the summary; warnings and ErrorInfo remain disallowed. The original twenty C# facts and six false qualification flags are unchanged.
