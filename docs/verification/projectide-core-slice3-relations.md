# ProjectIDE Core Slice 3: typed relations

Pure Core implementation for #905/#798, based on the fixed Slice 2 candidate.
Product changes and independent Test changes are reviewed separately. No integration
acceptance, client acceptance, production activation or merge is implied.

## Source and descriptor contract

`TypedRelationReader` projects the existing `ProjectSource.Relations` array. There
is no second writable relation store. Source IDs, documents, scoped contexts,
endpoints, payloads and unknown JSON remain in the original immutable `SourceJson`.
Supported interpretation requires both schema version 1 and a known descriptor
identity/version. Unsupported entries expose no usable typed relation.

The v1 namespaced wire vocabulary is `coglatas.depends-on`, `coglatas.satisfies`,
`coglatas.verifies`, `coglatas.consumes`, `coglatas.allocates`, `coglatas.contains`,
`coglatas.governs`, `coglatas.constrains`, `coglatas.related-to`, and
`coglatas.flows-to`. These tokens project #905's built-in concepts; UI labels and
CLR enum ordinals are not schema identities. Descriptors expose versioned payload,
endpoint, cardinality, scope, effect, capability and invariant contracts with a
canonical digest. All built-ins are directed; relatedness has no implied symmetry.
Milestone membership (`contains`) is distinct from composite hierarchy.

## Supported structural checks

- Exact local or authorized pinned external snapshot context, typed entity,
  ResourceBinding and Port references. Duplicate snapshot matches are Ambiguous.
  Authorization claims come from the host; Core performs no authorization lookup.
- Complete authorized absence is MissingWithinCompleteScope. Incomplete coverage
  and unavailable external snapshots remain UnknownDueToCoverage. Cross-Tenant,
  mismatched Branch/revision and unauthorized references are OutOfScope. Opaque
  target declarations remain Unsupported. No name or current-head fallback.
- Task-to-prerequisite direction, self/duplicate dependencies and iterative
  strongly connected component checks. Diagnostics identify cycle members,
  excluding downstream tails. Dataflow cycles are invalid without supported
  delay/iteration. Relatedness and bounded Workflow repetition are separate.
- Output/input direction, exact shape/unit compatibility with directional
  nullable/Unknown acceptance, scalar maxima and coverage-aware required inputs.
  Typed defaults satisfy connection requirements. No implicit unit conversion,
  collection merge or arbitrary multi-input behavior.
- Reusable/consumable ResourceBinding compatibility, positive/Unknown demand,
  explicit consumption time or allocation interval/release, pinned unit dimensions.
  Consumable stock has no allocation-release replenishment behavior.
- Requirement condition/evidence-kind and pinned committed revision/content digest;
  Workflow state/transition references and conflicting applies; permitted Constraint
  targets and pinned predicate references. Parsing/link existence proves no evidence,
  Constraint satisfaction, Workflow execution or Resource availability.
- Bounded snapshot/declaration/relation/unit input, deterministic finding order and
  immutable Source. Application receipts bind the Source digest/context and honor
  cancellation before and after bounded validation.

## Explicit remaining contracts

Full binding and the Compiler evidence envelope are later slices. Extension
descriptor execution, Relation-target Constraint binding, composite Task hierarchy,
collection merge, dataflow delay/iteration, non-FinishToStart plan lowering,
candidate/Scenario Requirement version interpretation, eligible evidence evaluation,
Resource snapshot capacity/stock checks, calendar scheduling and Workflow execution
remain Unsupported or Unknown as recorded by the validator. Workflow boundedness
is a structural declaration contract, not proof that an engine executed it.

The structural validator is not a Compiler Complete PASS or merge decision.
Further supported semantics can proceed without representing these limitations as
successful checks. There is no Tenant reservation service or live Source mutation.

## Acceptance and reproduction

Tests remain on the #1604 -> #1609 lineage rooted at SecurityTestImplimation.
`tests/ci/projectide_core_isolated.py` combines exact Product/Test SHAs in a fresh
detached worktree. Its receipt lists change commits, Product/combined trees, SDK,
complete TRX inventories and artifact SHA-256 values. All 544 baseline test names
must remain, and Slice 3 tests must run. Results/definitions/entries must match
bijectively, every outcome must be Passed, and incomplete counters are rejected.
Negative controls cover ownership, SHA injection, zero/failing/skipped/incomplete
runs and tampered TRX mappings. Exact candidate evidence is recorded in PRs.

This is supplemental CI under the existing acceptance policy. Test-head-controlled
scripts/workflows still require source review; artifact hashes do not independently
establish trust. Product pin updates need a new Test candidate/run. SDK and direct
packages are pinned by existing repository files; no full transitive package lock
or immutable hosted runner is claimed. Standalone Test PR CI remains structurally
blocked by unmerged Product dependencies; no checks or assertions are weakened.

Latest audit found Spec #300 merged as `16066f182beef0cd7ce57aa23849922f634d82c7`
on 2026-10-31, superseding older Draft-state observations. Its Owner directions
retain parallel Angular Web/Windows consumers and applicable SEC-ARCH gates.
Source review/CODEOWNER approval and formal acceptance remain pending separately.

Main, projectide/integration-20261031 and SecurityTestImplimation receive no push
or merge from this work. Angular is retained; new Web UI is NOT_STARTED and
Windows/Avalonia remains BLOCKED by #1144. No runtime/RLS/SEC-ARCH/Shadow/Enforce,
dependency, production configuration or Student Council release decision changes.
#1128 remains SUSPENDED / NOT_EVALUATED.
