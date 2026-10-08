# SEC-FND-01 Domain contracts and runtime boundary

This implements the #1117 contract slice. Binding, coordinator, durable storage,
ProjectIDE integration and authorized reads remain separate #1118-#1122 work.
No Security Foundation completion, specification approval or enforcement is
inferred from the existence of these types.

## Prerequisite and authority

The independent Main repairs are normally merged: #1129 at
`6843daf7165f0ce281fbe0300dc132bfbe3586be`, #1130 at
`64c070da52928d8066e0236e058195c4d434cbdc`, and #1131 at
`28e2b570def8a646625ada1add4f794e421bd6d8`.
The last SHA is this slice's qualified base:
[Main CI 37784049840](https://github.com/NYGsatoshi/Coglatas/actions/runs/37784049840)
passes all six required checks and applicable Backend, Security, Functional,
licensed real-backend, Qodana and CodeQL checks. The five deep Schemathesis and
three authenticated ZAP roles pass. Earlier failed runs remain failed evidence.

#905 owns canonical Source/Revision/Compiler; #906 owns authorization, review
and Merge; #1034 owns shared provenance, redaction and retention; #834 owns
traceability; #804 owns dependencies. This slice does not replace any of them.
The specification proposal remains Draft. Owner S01-S14 selections are retained
unchanged; their unavailable original option text is not assigned invented
meanings. The safe defaults below follow the explicit implementation instruction,
not an asserted mapping to those selections.

## Implemented contracts

All twelve required contracts reside in the dependency-free
`Coglatas.Domain.ProjectIde` boundary: `SecurityDecisionOutcome`,
`SecurityEvaluationStatus`, `SecurityEnforcementMode`, `SecuritySubjectRef`,
`SecurityOperationRef`, `SecurityResourceRef`, `SecurityPolicySnapshot`,
`SecurityCompilerProvenance`, `SecurityEvaluationRequest`, `SecurityDecision`,
`SecurityRuleResult` and `SecurityAnalysisSummary`.

They reuse `TenantId`, `SourceRevisionContext`, `ProjectSource` and
`ContentDigest`. References grant no authorization. Policy identity/digest
metadata is self-reported until compared to independent trustworthy host
evidence. Missing compiler identity is absent; no version/build is fabricated.
Full Source may be transient input and is not an approved persistence payload.

Values are immutable; decisions copy caller collections and expose readonly
results. Execution state is separate from nullable outcome. Completed execution
requires actual completed rule results and an outcome. Failure, cancellation,
timeout, pending and unexecuted states cannot masquerade as Unknown or Deny.
Unknown requires a missing-evidence code, Quarantine an integrity/binding code,
and Deny an explicit policy-violation code. Finite `SecurityReasonCode` values
carry no arbitrary exception/input/evidence text. The coordinator remains
responsible for actually applying rules and aggregating outcomes in #1119.

Every summary is non-authoritative. Shadow Allow/Deny/Unknown/Quarantine cannot
grant access or change existing ProjectIDE decisions merely by constructing one.

## Configuration boundary

`Security:EvaluationMode` defaults to `Disabled`; `Shadow` is explicit opt-in.
`Security:EnforcementAllowed` defaults to `false`. The existing registered
`HttpSecurityConfigurationValidator` rejects unknown modes, `Enforce`, and an
enabled approval flag in every environment. Setting the flag to true cannot
activate future enforcement. `Enforce` remains a compatibility enum value only.

An operator may configure the supported boundary as:

```json
{
  "Security": {
    "EvaluationMode": "Shadow",
    "EnforcementAllowed": false
  }
}
```

This slice validates configuration; it does not yet execute Shadow evaluations.
No Application evaluator, database table, new route, package, hosted frontend
artifact or Avalonia dependency is introduced.

## Verification

- Focused Domain and actual-host startup tests: 43 passed, zero failed/skipped.
- Architecture suite: 10 passed, zero failed/skipped. The new contract coverage
  checks immutability and executes the existing pure-core dependency rule.
- Full solution regression with the existing migrated PostgreSQL 18.6 disposable
  test database: 1,828 backend and 10 architecture tests passed, zero failed or
  skipped. `POSTGRES_TEST_CONNECTION_STRING` and
  `COGLATAS_TEST_USE_MIGRATED_TEMPLATE=true` were set; this is real PostgreSQL
  evidence, not the conditional tests' no-database early-return path.
- `git diff --check`: passed.

The initial focused compile identified a fixture API mismatch (`Decode` consumes
bytes); it was corrected to the canonical byte input before the passing run.
The existing unread `clock` compiler warning is unrelated. No new migration is
required or applied by this slice; the test database already has 73 migrations.

PR number, exact head, normal merge SHA and exact-Main qualification must be
recorded before #1117 closes. No public revision API/status DTO is frozen here.
Unverified owner-option mapping, external API, retention/capability policy and
future enforcement approval remain with their owning workstreams. Numerical
performance is `SUSPENDED / NOT_EVALUATED`; #1128 remains a separate production
prerequisite. This foundation is not a production-wide access-prevention claim.
