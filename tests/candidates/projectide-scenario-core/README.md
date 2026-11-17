# P0 #1951 — independent fixed Scenario Core candidate

This Security-owned candidate independently exercises the already integrated
`ProjectScenarios` public API against an immutable ProjectIDE Product. Its
twenty native xUnit facts construct their own synthetic Source, settings,
typed inputs and dependency relation. No Product test fixture or expected
Product test result is imported.

The selected boundary is **fixed Scenario input, revision, explicit rebase
and current-base Draft conversion**. The tests call genuine `Prepare`,
`Revise`, `Rebase`, `ToDraft`, `Materialize` and `ApplyToDraft` operations and
compare the original Source/input/settings before and after the transition.
They do not execute a compiler, test manifest, simulator or comparison engine.

## Exact Product/Test composition

| Source | Immutable identity |
| --- | --- |
| Product commit | `ea339ed0bc094630654262ebb28568a95ba4198f` |
| Product tree | `3170396d28e390759acc454194eba411afef2749` |
| Product `ProjectScenarios.cs` | `e466ac683ca8883c38b3ea569673b1648fd2cb3e` |
| Product shared `ProjectDraftRename.cs` transition | `fb998c7ee934ef39c577f0f677eabcc3f7d81156` |
| Product `CompilerInputManifest.cs` | `862bdf70c22ee3daee9ab821f7191061ed0f7a50` |
| Product `SourceContext.cs` | `e191cc347f72c0a5e915a4b12f7239d7ca0f9081` |
| Independent C# candidate | `abdd8eb8d2975567602d22d19f969458587b3a04` |
| Independent composition runner | `9c1ba77b4eda7c7102a4ca9b86b3f9f3ed5ec44a` |

The runner admits only the pinned Product commit and tree. It stages only the
exact independent Test blob at
`tests/Coglatas.Tests/ProjectIde/SecurityScenarioCoreCandidateTests.cs` in a
fresh detached Product worktree. It checks that the candidate is a regular
file with the exact compiler-input bytes, the detached HEAD still equals the
pinned Product, the Git index blob agrees and the complete tracked delta is
only that candidate. These checks run initially, after restore immediately
before compilation, immediately after native test execution before evidence
parsing, and after discovery. Symlink/non-file candidates, a moved Product HEAD,
other tracked Product changes or a different candidate body are refused.
The original candidate bytes are also retained as `candidate-source.cs`.

Product `global.json` pins .NET **10.0.403**, disables roll-forward and excludes
prerelease SDKs. The nonrequired Ubuntu-24.04 and Windows-latest advisory checks
both Git source pins, obtains that exact SDK policy from Product and requires
the actual SDK version before invoking real `dotnet test -warnaserror`.
The workflow has only `contents: read`, uses the same-repository pull request
head and changes none of the six registered required checks or their rules.

## Twenty independent controls

Each row is one frozen fact in
`Coglatas.Tests.ProjectIde.SecurityScenarioCoreCandidateTests`. Refusal
controls assert the specific state and absence of a published plan; accepted
controls inspect the actual resulting objects and canonical values.

| Fact | Required observation |
| --- | --- |
| `PreparedScenarioBindsExactBasisOverlayAndAllSettings` | The exact base input, Source and context bind the overlay and new Scenario version. All other settings, five pin collections, documents and manifest remain exact; the dependency changes only its two endpoint contexts. |
| `TypedOverridesApplyAtomicallyWithoutChangingCapturedInputs` | Duration, typed Boolean input default and Milestone target are applied together. Replacing every entry in the actual caller-supplied array cannot alter captured changes or input; copied bytes cannot mutate the source. |
| `InvalidLaterOverrideCannotPublishAPartialScenario` | A valid first request followed by a negative duration or wrong typed value publishes no partial plan and preserves the original duration and input. |
| `PortDirectionTypeAndTargetKindsAreEnforced` | A valid input default is accepted; output/missing ports, a mismatched type, an unsupported declaration kind and a location without an entity are refused. |
| `StaleExpectedInputsAndForeignTargetContextsAreRefused` | Wrong expected input and targets from another revision, branch, Project or Tenant are refused. |
| `SameVersionSourceSubstitutionCannotMaterializeReviseRebaseOrConvert` | A different Source body under the same context cannot replace the exact captured basis through any of those four operations. |
| `ScenarioBasesAndMixedContextIdentitiesAreRefused` | A Scenario cannot become a Scenario base; nesting and mixed committed/Scenario identity representations are rejected. |
| `PinnedScenarioVersionsCannotBeReallocated` | Each of the five pin collections independently prevents reuse of a Scenario ID/revision already bound to a different overlay. |
| `CanonicalOrderingDuplicatesAndNoChangeRemainDistinct` | Request order preserves canonical overlay/input identity; duplicate targets conflict, while empty or unchanged requests return NoChange. |
| `OpaqueSourceIsPreservedAndFutureOverrideTypesAreRefused` | Opaque root, document and manifest metadata, extension declarations, package locks and source references survive the supported edit; a future typed-value schema cannot qualify. |
| `OverrideCountBoundaryIsExact` | 128 distinct valid input overrides are applied; the same sequence with a 129th request is refused atomically. |
| `CombinedUtf8OverrideByteBoundaryIsExact` | The combined canonical target/value bytes admit exactly 262,144 bytes and refuse one additional byte, including an over-budget non-ASCII payload. |
| `CancellationBeforeAndDuringEnumerationPreservesOriginals` | All six operations honor an already cancelled token; cancellation after one enumerated request prevents publication and disposes the real iterator. |
| `RevisionRequiresANewVersionAndTheExactOriginalBasis` | An explicit new Scenario revision is accepted against the captured basis; version reuse and a different basis are refused, preserving the earlier plan. |
| `ExplicitRebasePreservesUnrelatedNewBaseChanges` | A new committed base retains its unrelated Task and clock changes while carrying the explicit override forward under a new Scenario revision. |
| `RebaseRefusesChangedMissingMovedAndForeignTargets` | A changed prior value, missing or relocated declaration, foreign branch and stale expected input cannot produce a rebased plan. |
| `CommittedBaseConversionBindsTheDraftAndRestoresOriginalSettings` | A genuine Candidate Draft restores exact base settings, binds base/Scenario/after inputs and materializes only against the exact current base; a different proposal base is refused. |
| `CandidateConversionRequiresTheSameProposalAndANewCandidate` | Candidate conversion requires a fresh candidate version within the same Proposal, base and captured target head. |
| `DraftContextsReservedByPinsOrHypotheticalReferencesAreRefused` | A pin or a typed reference newly introduced by the hypothesis prevents allocating its reserved Draft context; the reference origin moves to the Scenario while its target stays fixed. |
| `DraftApplicationRequiresCurrentExactSettingsContextAndSource` | Genuine Draft application succeeds against the exact current basis; changed settings, revision or same-version Source body is refused, with the original and Draft bytes preserved. |

The fixtures contain two Tasks, a Milestone, typed input/output ports and a
finish-to-start dependency. Settings and snapshot records are synthetic fixed
claims used to verify identity and preservation. Their presence does not
establish runtime artifact acquisition, snapshot authority or durable version
allocation. The optional opaque data may remain unsupported to the semantic
binder; this candidate requires structural decoding for the selected
transition and makes no complete compilation or semantic verdict claim.
The typed-reference case tests context reservation and origin preservation,
without resolving or authorizing the target.

The first native [run 44072196028/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/44072196028)
at Test HEAD `6d1b6e178aab28713f0a4edb346a74c19ca24d3b` compiled all twenty
facts on both hosts, with **19 PASS / 1 FAIL / 0 skipped**. The opaque-source
fact failed during fixture construction: its extension and package records
lacked the required `namespace` and `artifactId` set keys, and its opaque
reference lacked `documentId` and `kind`. Those structural headers are now
supplied while retaining the original opaque payloads and every assertion.
The original failure remains historical failure evidence; qualification of
the corrected fixture requires fresh exact-source native results.

## Native evidence admission

Each host must execute exactly **20/20 actual compiled xUnit facts**, with no
failed, skipped or unexecuted cases. The runner freezes the literal full-name
roster; original TRX results, definitions and entries must form a unique
test/execution-ID bijection with exact raw names and matching TestMethod
class and bare-method metadata separately. It requires the native TeamTest/2010
namespace, one completed summary/counter set, explicit zero failed/unexecuted
counters and unique result/definition/entry containers containing only supported
direct records. Additional result containers or unsupported failure records
cannot disappear from admission. Raw
compiled discovery must contain precisely the same twenty distinct names,
including any unexpected foreign namespace or trailing name whitespace in
the comparison. Count-only, duplicated, substituted, incomplete or non-Passed
evidence cannot qualify.

Original TRX, discovery output, candidate source bytes, SDK/restore/test logs,
source/composed-tree receipts and SHA256 file manifest are retained with the
workflow run, attempt, host and source identities. The original hosted ZIP
must also be verified against its reported size and SHA256 before accepting
the per-file manifest. Across hosts, Test/Product identities, candidate bytes,
composed tree and raw native test ID/name sets must agree; execution IDs and
per-host ZIP/TRX hashes may differ.

Do not mark this slice natively qualified until both exact-source hosted jobs
and their original artifacts satisfy these checks. Local source, Python and
parser checks do not establish a C# build or native execution. The runner's
`SYNTHETIC_SCENARIO_CORE_PASSED` describes only the twenty selected local
transitions. Every receipt keeps all six broader qualification fields false:

```text
requiredCheckQualified = false
independentReviewComplete = false
nativeUiQualified = false
productionQualified = false
trustQualified = false
releaseQualified = false
```

## Open acceptance and boundaries

This candidate is a bounded contribution to P0 #1951, not completion of
#1951, #1032 or #907. Independent Compile/Test manifests, simulation controls,
Unknown/NotEvaluated propagation, quantitative Impact, run comparison,
complete nested reference transformations, trace coverage, current
Tenant/actor/session/revocation authority, real runtime acquisition, jobs,
cache/registry publication and durable snapshot/version storage remain
outside this slice. It does not exercise actual Avalonia or Native UI, server
authorization, Trust, Production or Release.

The source advisory does not replace the six registered required app15368
checks, current-head review or Security Owner adoption. #1144 remains BLOCKED,
#1128 SUSPENDED/NOT_EVALUATED and Release NO_GO.

Repeat from a clean independent Security checkout containing the exact
Product commit, with fresh disjoint output directories:

```text
python -B tests/candidates/projectide-scenario-core/compose_and_verify.py --product-head ea339ed0bc094630654262ebb28568a95ba4198f --worktree <fresh-product-path> --evidence <fresh-evidence-path>
```
