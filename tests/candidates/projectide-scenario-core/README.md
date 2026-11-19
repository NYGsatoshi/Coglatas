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
| Independent composition runner | `1ed0af6a743c3c98a31111861431644b1b277eb4` |

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

## Git environment isolation regressions

Every Git subprocess, including the raw committed-source read and detached
worktree creation, removes inherited `GIT_*` variables case-insensitively.
It preserves non-Git settings and never edits the caller's process environment.
This closes a reproduced source-binding gap: with `GIT_DIR` and `GIT_WORK_TREE`
pointing at a different disposable repository, the old runner resolved that
repository's HEAD despite receiving the intended checkout as its working
directory. No Product API or existing C# test source changes in this repair.

The initial Git-isolation follow-up added **eight** regression methods, preserved
by the current suite. They admit
original retained native TRX/discovery before constructing fixtures, freezes
the unchanged C# blob and method roster, reproduces the old redirect with a
read-only unprotected Git call, and executes the actual runner's `main()`
against real disposable Git repositories. Separate controls cover a clean
environment, foreign repository/worktree, foreign index, foreign object/common
directories, counted Git configuration and parameter-based Git configuration.
Each controlled composition verifies the intended Test/Product/blob/tree and
all six false qualification flags, and compares every file in the foreign
repository before and after, including its index and object database.

Only the controls' `dotnet` command outputs are supplied from the retained
native inputs. These controls do **not** execute C#, add native passing cases,
or turn retained results into fresh execution evidence. The advisory first
runs the actual unchanged native candidate on each OS, then runs the preserved
eight controls and the current bounded admission follow-up with that same host's original TRX and compiled discovery. It pins
the controls blob and retains their log separately in the workflow context.

```text
python -B tests/candidates/projectide-scenario-core/test_compose_and_verify.py --native-trx <original-candidate.trx> --compiled-discovery <original-compiled-discovery.log> -v
```

## Bounded #2095 admission follow-up

The current runner preserves all twenty C# facts and the same Product/source
identities. It rejects nested TRX result records, including Passed or
NotExecuted descendants hidden beneath an otherwise valid outer roster, and
contradictory native error or non-informational run-level information. Original
counter, namespace, method, identity-bijection and discovery checks remain.

Raw NUL-delimited Git inventories reject `assume-unchanged` and `skip-worktree`
before trusting tracked differences. Staged and unstaged deltas are checked
separately. Ignored compiler inputs are included in the untracked inventory;
after restore only the exact `bin/` and `obj/` roots of committed Product
projects are admitted as generated output. Every restore/test/discovery
command disables external ancestor Directory.Build props and targets; the
frozen Product has neither committed import file.

The preserved eight Git-environment methods are joined by report, disposable
Git and SDK regression methods, for 35 controls. Synthetic reports are labeled
as such; retained-native controls still read the host's unchanged original
TRX/discovery. The SDK probe actually invokes restore/test/discovery against a
separate package-free project: poisoned parent props and targets fail each
unprotected command, and guarded commands exclude them. These probes neither
compile Product tests nor add native passing-case credit. The advisory pins
the current runner/control blobs and retains controls separately from the
actual twenty hosted C# executions. Python uses isolated imports.

Fresh final-head Windows/Ubuntu execution, original archive/run/job audit and
ordinary CI remain separate requirements. Executing-workflow provenance is
still absent for this cohort and is not inferred from the Test checkout.
Allowed generated-output roots are not a hermetic dependency attestation.
#1951, #1032, #907 and broader #2095 remain open; all six qualification fields
stay false, #1144 BLOCKED, #1128 SUSPENDED/NOT_EVALUATED and Release NO_GO.

Direct runner and control execution also checks each script's committed blob
and actual unfiltered bytes immediately before isolated Python starts. The
control invocation additionally binds the runner it imports. Three actual
PowerShell regression methods exercise all four host/entrypoint blocks with
working changes, changed commits, Git-normalized CRLF bytes, and local or
PYTHONPATH import shadows. Original pinned canaries must run only after the
specific refused input has been restored; these are bootstrap controls, not
Product execution.

## Review follow-up: admission policy and control completeness

The native reader rejects `TestResultAggregation` explicitly, including empty records, alongside all nested result families. Both staged and unstaged Git path comparisons disable rename detection, so a Product deletion cannot disappear when paired with the candidate addition.

Run-level admission intentionally permits only `Information` RunInfo outcomes. `Warning`, `Passed`, `Completed`, error outcomes and unknown values make the receipt incomplete even when every candidate case passes. This is a strict local admission policy, not a claim that every warning is a native test failure.

Ignored and ordinary untracked source paths are refused **outside** the registered output roots. Within an allowed root, acceptance depends on the path and execution phase; it does not establish how a file was produced. In particular, an ignored C# file under a registered `obj/` root is admitted after restore. These checks do not make the build hermetic.

Disposable SDK probes pin `10.0.403` with roll-forward disabled and check the selected version before creating either parent import. Their known Git fixture files are explicitly force-staged. Execution controls give each script distinct committed canary bytes and its own pin, require every role's bindings, and verify exactly which isolated canaries ran. These controls add no Product/native execution credit.

The complete committed suite contains 35 unittest methods.

## Native TRX run-message boundary (#2095)

Only an informational RunInfo inside one native ResultSummary/RunInfos container qualifies for the original Scenario Core result roster. Out-of-scope, foreign-namespace, duplicate or unknown child run messages, and any error/warning evidence, are refused even when the frozen twenty C# candidate facts have passing counters. This only strengthens independent advisory evidence admission; the Product, C# source and six false qualification flags are unchanged.
