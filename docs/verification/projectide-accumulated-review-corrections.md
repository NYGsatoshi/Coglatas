# ProjectIDE accumulated source review corrections

Scope: PR #1955, integration base 3613c648f118cafcb1936fdf5a081aa251c6b4bf, reviewed source d8b1f141609aa7f97d2289d65c49ac631a96a592. These are local synthetic Product corrections, not SecurityBranch implementation or authority promotion.

CodeRabbit CLI completed the cumulative frontend/src review (all 27 files, exit 0, one minor issue) and cumulative src review (all 22 files, exit 0, seven issues: six major conditional concerns and one minor issue). These advisory reviews are distinct from submitted GitHub/CODEOWNER reviews. Earlier partial-review evidence is retained; a complete directory review does not claim coverage of files outside that directory.

## Valid corrections

- Ordinary version-two declaration navigation returned true before source/publication admission, although sourceTarget then hid the record. The new store regression failed with expected false / actual true. Require current admitted version-two publication for every navigation path through resolveCurrentLocation; retain the version-one requireSourceMap behavior. Regression covers initial load, hashing in progress, admitted navigation, and publication reload. Actual en/ja browser controls cover refusal and keyboard focus after admission.
- Two equivalent admitted snapshot aliases without references passed binding but prevented declared-scope publication because the closure required exactly one supplied record per context. The new actual-assembly regression failed with a null publication. Select the lowest ordinal actual admitted snapshot ID for equivalent source bytes, version and coverage; reject conflicting claims. Retain both originally admitted bodies in the binder, retain ambiguous-reference handling, and never invent a snapshot ID/version. Supply-order reversal must produce identical publication/result bytes. Existing conflict and duplicate-body controls remain.

## Conditional Core issues: inspected dispositions

1. Regional Inconclusive aggregation: the public CompilerRuleOutcome DTO permits Completed/Complete/Inconclusive, but regional outcomes are privately constructed by PureCoreCompiler.CompileSingle. The same facts.Incomplete flag controls Partial coverage and Inconclusive verdict; complete executed regional rules carry Pass or Fail. CompilerScopedPublicationTests.OnlyPrivateRegionalProducerOutcomesReachPublicationAndCompleteRulesCarryActualTruth explicitly exercises true/false/unknown. No external DTO injection reaches private region/publication construction. Preserve this producer invariant; do not infer a global DTO invariant.
2. Structural-success/legacy IR: WithPublication intentionally sets Publication with Ir null. Compiler output selection uses Ir pattern guards. Scoped IR and publication read regional Validation.Ir only after Attach requires every regional IR to exist. ProjectSimulation and ProjectSimulationDebugger consume a privately constructed CompilerExecutionPlan, not a success-guarded legacy Ir. Actual version-two host/plan/simulation tests cover that path.
3. Cancellation: Attach returns local immediately for Execution != Completed before checking the token. Its active work also catches requested cancellation and publishes a Cancelled envelope. The existing CancellationPublishesNoNewVersionTwoArtifacts test verifies no artifacts/publication on cancellation.
4. Result serialization: SourceCodecLimits defaults to 4,194,304 bytes, while SimulationRunLimits permits at most 2,097,152 trace bytes. The result wraps the event array with a small bounded manifest (fixed identities/digests, engine artifact ID <=256 and version <=128, limits and counters), not Source/plan payloads. The additional envelope fits the remaining roughly 2 MiB. Existing exact trace-byte controls verify prefix admission. No serializer-limit change is required.
5. Negative lag: ElapsedDuration itself can represent a negative value; that alone is not the plan invariant. The only CompilerExecutionPlan constructor is private and Prepare explicitly rejects lag unless it is known ElapsedDuration with Ticks >= 0. UnsupportedOrUnknownPlanSemanticsRemainNotProduced already includes lag -1. No valid plan can enqueue a negative-lag transition.
6. Closed-scope authorization: ValidateClosedScope is internal; its sole caller CompilerGraphClosure.Build constructs every RelationScopeSnapshot from an admitted binding region with authorization and complete coverage. Denied supplies are excluded by ProjectSemanticBinder before a complete binding closure can exist. Public Validate never enables unionScope. Therefore the hypothetical unauthorized union owner is not reachable through this caller contract. No authorization claim is expanded.

Validation and final source identities are recorded in the PR/evidence ledger after the corrected-source executions finish. Historical failed CI, setup attempts and genuine failing regressions are retained.

## Corrected-source validation

- Actual solution Release build with warnings as errors: 0 warnings, 0 errors.
- Focused actual-assembly materialization/binding/publication/plan regressions: 101 passed. Full actual ProjectIDE suite: 1,807 passed, 0 failed/skipped.
- Existing nine exporter profiles / 93 cases reproduced byte for byte.
- Angular ProjectIDE suite: 580 passed / 23 files. Production build succeeded; existing unrelated CSS-budget warnings remain, sanitizer scanned 6 / changed 1.
- Focused pinned Linux browser scope: 20 passed with 0 retries/skips/flaky, including four new en/ja desktop/mobile declaration-navigation cases. The first new browser test setup omitted opening Source and timed out; correcting the explicit screen operation resolved it. That failed setup evidence is retained; no timeout, assertion or runtime behavior was weakened.
- Full browser regression and corrected exact-HEAD hosted CI/review remain separate gates.
