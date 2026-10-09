# SEC-ARCH Phase 2 integration qualification

This integration combines contract-preserving implementation and isolated verification.
It does not approve normative mappings, security exceptions, database authority or
product activation. **PRE-AVALONIA SEC-ARCH: BLOCKED.**

The public Main base is `2f7e6da4d319c4c9b8ebfd8fba766d295d16fee0`, following
registry PR #1185 and composed API/SignalR PR #1186. Historical qualification at
`3335d59d458b50c14071b843a799902429c92689` remains exact historical evidence.
Private Main `cbfa350263a7eea73270cf0aa2a69f67ddc841e3` and Draft private review
records remain separate from ordinary implementation review.

## Included implementation

- #1184: complete draft operation reconciliation and deliberate-invalid inputs.
- #1187: seeded operations on all 105 proposed RLS tables.
- #1188: independent live GitHub run, attempt, artifact and assembly reconciliation.
- #1189: the non-required `specification-contract` Advisory producer and consumer.
- #1190 and #1193: bounded typed HTTP assertion accounting using existing controls.
- #1191 and #1195: isolated context, actual adapter methods, real PostgreSQL retry
  conflicts, current authority revalidation and concrete executing-role binding.
- #1192: all-table DDL/recovery and preservation controls.
- #1194: repairs for the actual registry Qodana rule-debt failure.
- #1196: pinned Kafka tool entry points and bounded failure diagnostics.
- #1197: per-operation executing-role binding and independent native/source inputs.
- Versioned six-assembly producer/loaded-copy binding, including the verifier DLL;
  historical version 1 remains explicitly partial evidence.

Independent branch histories and their failed and successful development evidence
are retained. Merge resolution preserves all verifier methods and mutation controls;
the catalogue is reconciled by its actual method/case sum. The Advisory summary
separately labels tooling, inventory and representative runtime observations. These
labels confer no canonical coverage or product acceptance.

The current composed catalogue contains 205 cases. It includes the two independent
native source-reference controls; later transport controls must be added to the
actual method sum before their candidate qualification.

The first combined PostgreSQL review run at
`f05195a333ebb60c5001ed93cadf9e0013ec9a00` passed 15 of 22 facts and failed seven,
with zero skips. Its retained private TRX SHA256 is
`3ab1020578ae6c36cb1a9cd21c8fa9e2a86224fca2f6968889d35627c3331e8e`.
The source-reference failure occurred in temporary database deletion; six other
failures surfaced dependent-role cleanup errors. Database creation/deletion is
now serialized in the test fixture, while scenarios remain parallel and command
deadlines remain unchanged. This repair needs the same actual provider rerun;
the failed run is not qualification evidence.

## Observed limits

The table inventory contains 114 tables: 105 proposed RLS-required, four proposed
shared, two proposed internal and three requiring personal identity/root review.
The operation matrix has 2,520 cells. Its current observations are 2,069 PASS and
451 UNVERIFIED, including 315 unsupported non-UPDATE ownership-reassignment cells.
The remaining 136 applicable cells lack qualified positive operations. Nineteen
direct positives are rejected by current native source guards or a retained
foreign-key constraint; source-bound explanations do not convert them into RLS
denials or approved applicability exceptions.

The current composed inventory has 403 route/method surfaces and 398 OpenAPI
operations. All five runtime-only surfaces have explicit draft classifications.
Actual anonymous controls cover 387 protected HTTP operations. The expanded
PostgreSQL HTTP accounting observes nine authorized endpoint operations, four
cross-tenant, three same-tenant resource, five workspace revocation, one tenant
revocation, three seeded role denial and one capability endpoint. Cookie/InMemory
and synthetic-auth/InMemory observations remain separate. Full resource/role
matrices, approved SPEC linkage and the product API-to-RLS adapter remain unverified.

Test-owned database identities and transaction context do not establish product
startup, bootstrap or operational worker discovery authority. A role permitted to
select arbitrary tenant context can defeat a mutable GUC boundary; the isolated
controls explicitly preserve that residual risk. Kafka and synthetic service
fixtures do not activate product infrastructure or certify cloud network policies.

## Actual Advisory CI observation

PR CI run `38001619099`, attempt 1, for PR head
`d8d2fad6cbe81dfa6cf2c399199c0a426af9e3e6` produced the Advisory artifact
`11649347780`, 1,898 bytes, SHA256
`23a8a5791740b34f575573d31bac1e3691739e326b9fbba6fdd9ed2aac95e1a9`.
The tested merge candidate recorded in that artifact is
`7774933e045b103afea60d4b2e80df3f4a6e4bd5`, distinct from the PR head.
Its independently downloaded ZIP digest matched the GitHub artifact metadata.
The consumer accepted that exact candidate/run/attempt and rejected substitution
of the PR head. The valid result still reports canonical coverage UNKNOWN,
personal approval UNVERIFIED and pre-Avalonia BLOCKED.

## Outstanding qualification and owner holds

The original Main Qodana Cloud failure at `88733966` is retained. Repairing the
identified source findings does not establish that a later full Main run passed.
Three open High dependency alerts currently have no published patched version;
no alert is dismissed and no exception is created by this integration. #614
supply-chain and scanner acceptance remains open. #842's Closed lifecycle alone
does not qualify the SEC-ARCH gate.

Private initial mapping, registry granularity/allocation, identity/root semantics,
concrete policy/role authority, exact activation-bearing migration and any new
exception require verifiable personal owner review. The six existing Required
Checks, ruleset, scanner baselines, performance thresholds and indefinite
same-hardware database-duration suspension remain unchanged. Exact-candidate
runtime, composed CI, review-thread resolution and subsequent Main qualification
must be recorded before an implementation merge is described as qualified.
