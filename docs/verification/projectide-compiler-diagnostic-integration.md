# Compiler diagnostic and materialization integration

Continuation of #905 after Product #1681 and independent Test #1682. This slice
uses the existing CompilerInput, CompilerDiagnostic and CompilerStageOutcome
contracts. Authority: A-R2 COMP-01/02/03/04/11, SEM-01/10, ANALYSIS-05/08 and
PEE-07, plus IDE-START-01/03 in the merged Web/Windows amendment. It introduces
no Source, Manifest, Receipt or Result wire shape or new eligibility policy.

## Full-reference pipeline audit

| Boundary | Current actual connection and limit |
| --- | --- |
| Codec / parser | CanonicalSourceParser retains the raw buffer and uses ProjectSourceCodec. PureCoreCompiler independently reparses the exact canonical Source before binding. Unsupported syntax has no substitute executable AST. |
| Admission / typed values | PureCoreCompiler calls ProjectSemanticBinder.Bind with the supplied immutable snapshots. Existing admission validates unused claims, pins, duplicates/conflicts and authorization before indexing; typed Entity/Document values use those admitted snapshots. This behavior is reused. |
| Semantic validation / IR | CompilerSemanticValidation receives the exact partial model, inherited findings and consumed logical operations. It checks unit/calendar pins and structural closure. Invalid, incomplete or unsupported closure produces no IR. IR is an inspection artifact, not an executable plan. |
| Diagnostics / Result | Inherited binding findings reach validation and final Result without recreating diagnostic identity or location. Result applies deterministic ordering and exact input binding. Known proven failure can have complete evidence of failure; it cannot become Pass. |
| Requirement evidence | RequirementEvidenceValidator is an explicit caller-invoked claim consistency helper. It is not an eligibility or satisfaction stage in Compile. Compile retains requirement-evidence-unavailable; no claims are silently admitted as proof. |
| Resource boundary | ResourceReferenceValidator remains a caller-invoked structural comparison. No approved ResourceId-to-materialized-body mapping exists; Compile retains resource-snapshot-unavailable. |
| External graph | Typed resolution is not transitive closure. external-closure-not-lowered remains; absent external adjacency is not a complete empty frontier. No implicit acquisition or external navigation is added. |
| Host / freshness | Application delegates to PureCoreCompiler. Existing Local/Server host request binding includes actual materializations. Existing freshness compares the exact desired input/request and generation; it does not establish current authorization. |

## Reproduced gaps and correction

The semantic stage previously drained bounded Unit/Calendar enumerations before
observing cancellation. Cancellation from an iterator returning false could open
the next supplier and let its exception replace the cancellation result. A failed
binding's processing budget also did not prevent later supplier invocation.
Body processing was charged only after collecting the full supply prefix.

A subsequent exact-head review identified the zero boundary separately: a body
can consume its final allowed operation without overspending. A following
GetEnumerator or MoveNext must still be rejected before invoking caller code.
Four additional independent controls reproduce that gap on the first corrected
candidate. ReadSupply now checks for zero remaining operations before both calls,
preserving cancellation precedence and the existing exhaustion diagnostic.

The semantic stage now checks cancellation before opening and around every
MoveNext, including its false return, and disposes each acquired enumerator.
Source work and each supplied body consume the inherited logical budget before
the next item is requested. Binding exhaustion prevents opening either later
supplier; exhausting Unit work prevents opening Calendar work. Existing family
caps and safe local diagnostic codes remain. Arbitrary supplier exceptions retain
the existing host failure handling; they are not interpreted as successful work.

Binding and semantic Stage execution previously always reported Completed,
including when their findings established ResourceExhausted. The full compiler
now preserves ResourceExhausted on each affected Stage as well as its existing
rule/final result axes. A normal known Constraint failure remains a Completed
evaluation with Fail verdict. No Unsupported/Partial state gains Complete Pass.

Independent regression tests cover cancellation during both supply families,
cancelled end-of-enumeration, unopened later suppliers, inherited/shared budget,
family caps, stage propagation, known failure, exact diagnostic identity/order
and stale revision rejection. Before/after exact candidates, inventories, host
outcomes and hashes are recorded in the existing continuation ledger and paired
Draft PRs; test source stays in the independent SecurityTestImplimation lineage.

## Remaining decisions

Option B remains selected. Concrete schemas/hash domains/identity/mapping and
migration, authenticated producer receipts, authorization freshness, offline
bundle contracts and Evidence Eligibility require explicit Owner decisions.
Actual Requirement satisfaction, Resource acquisition, transitive executable
closure and native Avalonia activation remain incomplete/HOLD. #1144 is unchanged;
#1128 remains SUSPENDED / NOT_EVALUATED. Full review, CODEOWNER/Owner acceptance,
ordinary CI and supplemental verification remain distinct.
