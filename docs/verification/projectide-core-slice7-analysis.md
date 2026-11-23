# Core Slice 7: initial bounded analysis and full compilation

Pure #905 continuation after #1620, with independent tests after #1621.
`PureCoreCompiler` is the full-reference path through canonical reparse,
normalization/binding, semantic validation/IR and actual supported rule evaluation.
Application exposes the same operation without UI, storage or runtime effects.
The host must supply its fixed expected Compiler identity; this is an integrity
binding, not proof that a caller-supplied label is an approved engine.

The initial Ruleset supports version 1 empty-configuration schema, declaration,
relation, required-Constraint, Requirement-evidence and Resource-contract families.
Unknown rules/configurations are explicitly NotRun rather than claimed executed
success. Required platform/family omissions cannot become Pass. Rule identities,
versions/configuration and scope are locked input. Arbitrary rule/plugin/script
execution and unsupported requested targets remain incomplete.

Attached required Constraints evaluate the existing bounded pure tri-state AST.
True/False/Unknown remain distinct; eligible model-proof basis records the actual
evaluated premise manifest digest. An unattached required predicate cannot silently
become applied enforcement. Advisory failure remains a recorded warning without
changing required Constraint truth. Compiler outcomes do not implement Review/Merge
policy, waivers, automatic dependency edits or date shifting.

Requirement conditions may prove a known failure, but a true condition/link does
not manufacture eligible evidence. Actual evidence eligibility/runs/freshness
materialization remains unavailable and is reported Unknown, even if other required
checks pass. Resource bindings retain reusable/consumable contracts; absent verified
capacity/stock/contribution snapshots are Unknown, never zero demand, infinite
capacity or replenishment on allocation release. No real reservation service exists.

Known failure survives other incomplete required work as Partial/Fail. Execution,
coverage, verdict, evidence and freshness remain independent. Cancellation and
logical/numeric/publication exhaustion cannot be Pass. Deterministic work already
consumed by binding/validation is carried into rule evaluation. Ruleset/identity
expansion bounds protect publication; retained Source is never truncated/repaired
to produce success. Results reference the complete immutable input manifest by
digest rather than duplicating it inside every bound-input stamp.

Produced IR/Source-map/semantic-model artifact references must match actual stage
outputs and the exact input. IR remains inspection/analysis output: this slice
produces no Simulation/execution plan or run. Full compilation is reproducible;
incremental reuse is not implemented. Evidence digests require the original pinned
Source/input and exact engine for replay; durable evidence storage remains #1034.

`CompilerSuppressionRequest` is bounded, version/context/target/policy-bound request
metadata. It does not apply suppression or change any original finding, basis or
platform invariant. Current decision waivers belong to #906. Unknown suppression
semantics in Source are not silently ignored or promoted into successful checks.

Tests cover full repeated compilation, actual Constraint truth/Unknown/advisory
behavior, missing attachment/family checks, known failure with absent Requirement
evidence, reusable/consumable snapshot limitations, unsupported NotRun outcomes,
budgets/cancellation, engine substitution, suppression scope and expansion bounds.
Pre-fix controls reproduce false executed-state claims; fixed-candidate receipts,
hosted CI and review state are recorded on paired Draft PRs.

No protected branch push/merge, Required Check relaxation, dependency, production,
RLS/SEC-ARCH/Shadow/Enforce or Student Council release decision change. Angular
retained; Web UI NOT_STARTED, Windows/Avalonia BLOCKED by #1144. #1128 remains
SUSPENDED / NOT_EVALUATED. Source/CODEOWNER/integration/client acceptance is pending.
