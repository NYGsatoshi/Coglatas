# P0 #1951 — independent immutable run-comparison Core candidate

This Security-owned cohort independently exercises
`ProjectSimulationRunComparison.Compare` against genuine fixed plans produced
by `PureCoreCompiler.Compile` and recordings returned by
`ProjectSimulation.Execute`. Its fixtures use newly authored one-task Sources,
explicit compiler settings and actual captured Source materializations. They
do not import Product test fixtures or construct run/comparison internals.

This is the run-comparison slice requested in
[#1951 comment 6883532021](https://github.com/NYGsatoshi/Coglatas/issues/1951#issuecomment-6883532021).
The separately merged 20-fact Scenario Core cohort remains unchanged.

## Frozen composition

- Product commit: `ea339ed0bc094630654262ebb28568a95ba4198f`.
- Product tree: `3170396d28e390759acc454194eba411afef2749`.
- Independent C# blob: `73969c88ae939bc805e8e853b0022ef609a58f03`.
- Composition runner blob: `e1ac9cda32af9252dd91896cef6d13df31401439`.
- Verifier controls blob: `abcbaf1073b9e53a5b9e9a3f9336546f53d054a1`.
- Product SDK: .NET `10.0.403`, with the original roll-forward policy.
- Execution scope: `SYNTHETIC_RUN_COMPARISON_CORE_ONLY`.

The runner adds only the committed candidate C# blob to a detached worktree
at the exact Product commit and verifies the Product tree. It checks actual
candidate bytes, candidate index identity, Product HEAD and unrelated tracked
Product changes after composition, restore, execution and compiled discovery.
Local Git subprocesses remove inherited `GIT_*` overrides so another repository,
worktree, index or object store cannot replace the requested checkout; all
non-Git host and SDK environment variables remain intact.
The artifact retains raw `candidate-source.cs`, original TRX and discovery,
logs, source/composed-tree receipts and all retained-file SHA256 values.

Native admission requires exactly 22 distinct frozen fully qualified facts,
all Passed, with no incomplete/error counters. The native TeamTest/2010 root,
one direct supported result/definition/entry inventory, exact class and bare
method metadata, and complete result/definition/entry identity bijection are
required. Discovery must contain the same exact roster. Foreign, duplicated,
unsupported, missing or substituted records cannot qualify.

The read-only advisory targets Ubuntu 24.04 and Windows latest, pins Action
commits, and shares one workflow-level definition of Product, candidate, runner
and controls identities. It retains each host's original artifact independently.
Fresh exact-source hosted results and artifact audits must be recorded before
claiming native qualification.

## Twenty-two independent public-API facts

| Fact | Independently required observation |
| --- | --- |
| `CompleteComparisonBindsBothOriginalRunsAndEverySummaryField` | Both original run objects, all run/manifest/input/Source digests, contexts, states, clocks and counts remain exact; actual plan and simulation engine bindings remain inspectable. |
| `ComparisonBytesUseTheirOwnDomainAndDeclareNoOutcomeAuthority` | Original canonical comparison bytes hash under their own domain, distinct from run/manifest domains; verdict stays NotRun, evidence stays simulated and authority stays NotEvaluated. |
| `DistinctRunsOfTheSamePlanRemainSeparateButCompareEqualElapsed` | Equal plans retain different actual run/manifest identities and recordings, while their observed elapsed difference is zero. |
| `DifferentStartsAreRemovedBeforeSignedElapsedDifference` | Different start offsets are removed before computing either signed difference. |
| `LargeElapsedDifferencesRemainExactInBothDirections` | Positive and negative differences larger than JavaScript's exact integer range retain their exact signed decimal strings and original elapsed clocks. |
| `WrongAndSwappedRecordingDigestsRefuseWithoutSummaries` | Either wrong expected digest or swapped expected digests refuse the pair without before/after summaries, counts or delta; the correct pair still succeeds. |
| `ForeignTenantProjectAndBranchPairsRefuseWithoutSummaries` | Every changed root scope component is refused in both comparison directions without disclosing either recording. |
| `ReusedRunIdentityCannotNameDifferentRecordings` | Reusing one RunId for different recordings refuses; comparing the identical original recording remains valid. |
| `SameContextWithDifferentSourceBodiesIsRejected` | Two genuine plans cannot assign changed Source bodies to one retained normalized context. |
| `SameSourceUnderDifferentSettingsKeepsSourceAndInputAxesSeparate` | The unchanged Source/context may have different fixed settings/input/plan identities; SameInput is false without implying changed Source or elapsed time. |
| `ExactSharedCapturedSourcesAreRetainedAcrossBothPlans` | Both genuine two-region plans retain the actual fixed captured context/body and Snapshot ID/version, with two completed Tasks and four events. |
| `SameSnapshotVersionCannotRebindAnotherContext` | A fixed Snapshot ID/version cannot name two captured contexts across the pair. |
| `SameCapturedContextCannotHideAChangedBody` | Different Snapshot IDs cannot hide conflicting bodies for the same captured context. |
| `CapturedContextCannotReplaceTheOtherRootBody` | One plan's captured region cannot substitute a changed body for the other plan's original root context. |
| `DeclaredCaptureBoundaryAdmits256AndRefuses257WithOneActualSupply` | Exactly 256 declared captures admit and 257 refuse. Each bounded plan receives one actual materialized fixed Source and compiles two regions; compiler supply/closure bounds are not bypassed. |
| `FutureEngineRecordingsAreRefusedWithoutSummaries` | An actual unsupported-engine NotRun recording cannot enter either side of a comparison. |
| `PartialEventBoundKeepsActualCountsAndNoCompleteDelta` | A real one-event prefix retains one started/zero completed Tasks, its original clock and Partial coverage, with no complete delta in either direction. |
| `TimeOperationAndTraceBoundsNeverBecomeCompleteDeltas` | Actual elapsed-time, logical-work and trace-byte exhaustion preserve only committed counts/events and never receive a complete difference. |
| `StaleCancelledAndOverflowRecordingsRemainIncomplete` | Stale-input NotRun, cancelled and tick-overflow recordings retain their actual distinct states and empty prefixes without a complete delta. |
| `DifferentValidLimitsRemainInspectableAndIndependentlyCompared` | Two complete runs may have different valid event limits; SameLimits remains false independently of equal input and observed elapsed time. |
| `CancellationBeforeComparisonPreservesOriginalRecordings` | Query cancellation throws and preserves both original recordings and plan bytes; an uncancelled comparison still succeeds. |
| `ReturnedSummaryAndTraceCopiesCannotRewriteRetainedEvidence` | Modified byte arrays, copied trace entries and changed/rehashed detached JSON summaries cannot rewrite original recordings, inputs or comparison identity. |

All Sources carry the actual structural document and declaration headers
required by `ProjectSourceCodec`. Captures carry an explicit Snapshot ID,
version, exact Source digest/context, coverage and as-of field; every used
materialization matches its declared pin. Compiler publication and actual plan
production are asserted before recording. No hand-authored summary or fabricated
recording substitutes for a genuine run in these facts.

## Committed verifier regression controls

Original preparation at `c5a6b65001817a769450ae55b02d78f8de932a90` recorded
28 ad hoc local structural parser/discovery controls (three synthetic positives
and 25 rejections) and 11 actual Git/source controls. Those historical commands
were not committed as a reusable regression suite; their counts apply only to
that preparation, with no C# execution or native qualification claim.

The committed `test_compose_and_verify.py` now supplies **14 portable unittest
methods**. Synthetic 22-case TRX and discovery positives precede targeted
counter, Execution, identity-bijection, class/method, container and raw-name
refusals. The split-class/dotted-method control preserves the concatenated full
name while independently violating the required class and bare-method fields.
Every rejection requires the exact intended error message.

Disposable Git repositories exercise actual source bytes, candidate index,
same-tree Product HEAD and unrelated tracked-file drift. A hostile `GIT_DIR`
demonstrably redirects an unprotected read while the runner stays on its
requested repository, with every disposable caller file unchanged. Fixture
setup independently scrubs inherited Git overrides before initializing either
repository. Restored valid compositions pass again.

```text
python -B -m unittest discover -s tests/candidates/projectide-run-comparison -p test_compose_and_verify.py -v
```

Both advisory jobs verify the controls blob and run the suite before native
composition. Each retains `admission-controls.log` in its workflow-context
directory and records `controlsBlob` in `workflow-context.json`. These verifier
controls use synthetic reports and disposable Git only. They do not run C# or
replace the separate native evidence, compiled discovery or exact-head CI/review.

## Evidence limits and remaining P0 work

Complete means both recordings are Completed, Complete and Current for their
fixed model inputs. Their difference is observed model-clock elapsed time.
It does not establish Project/Test PASS, Resource capacity or feasibility,
prediction, quantitative performance assurance, current authority, Trust,
eligibility or Merge permission. Local duration and processing limits remain
provisional technical bounds.

This Core API consumes immutable in-process objects; this cohort's detached
summary mutations do not qualify an Angular serialization reader. Windows/Web
paired lifecycle, actual Avalonia, runtime acquisition, durable recording
uniqueness, current actor/session/Tenant authorization of both inputs and every
captured contribution, revocation, redaction, deletion, caching, export and
retention remain separate. In-memory identity checks and synthetic captured
Source admission are not server authorization.

Every receipt retains `requiredCheckQualified`, `independentReviewComplete`,
`nativeUiQualified`, `productionQualified`, `trustQualified` and
`releaseQualified` as `false`. This advisory does not replace the six original
required check contexts or independent review and owner governance. Keep
**#1951 OPEN**, #1144 BLOCKED, #1128 SUSPENDED/NOT_EVALUATED, and
RLS/Native/Trust/Eligibility/Production/Release holds unchanged.

```text
python -B tests/candidates/projectide-run-comparison/compose_and_verify.py --product-head ea339ed0bc094630654262ebb28568a95ba4198f --worktree <fresh-product-path> --evidence <fresh-evidence-path>
```
