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

The current composed catalogue contains 213 cases. It includes the two independent
native source-reference controls, two additional transport controls and four
initial selected composed-host controls, a further EF/audit mutation control and
the actual application replay-to-transport control. Catalogue inclusion is not
passing execution.

## Exact local candidate observation

Clean candidate `890b42c5f41e7a6be97f9bd91a3d2aca62ceab9a` rebuilt and passed
all **211** then-required runtime cases with no missing methods, verified cleanup
and matching canonical/loaded copies of all six assemblies. The test interval was
`2026-10-10T00:15:35.990980+00:00` to
`2026-10-10T00:22:48.673310+00:00`, approximately 432.68 seconds under the
unchanged 600-second deadline. TRX SHA256:
`dcd1c688e67adad464059145b460395738ceab8106edca3dc010578ae5bfee25`.
Runtime receipt SHA256:
`e7633866828440e20182c8e70e525e060466698a347338e8dac61a9d7d250602`.
The explicit 2 GiB tmpfs fixture retained `fsync=on` and
`full_page_writes=on`; crash recovery remains unqualified. This observation
cannot qualify the two subsequently added cases or a different candidate.

The same clean candidate passed all 16 actual isolated Kafka controls; its
receipt SHA256 is
`deaccad38765afef9d892f46b6b336291bd29ba066ad1c25a002dec66ee2bced`.
Kafka remains inactive in the product.

Independent reconciliation against a separate exact clean checkout and fresh
114-table native reference retained 2,069 passing and 451 unverified RLS cells,
136 applicable gaps, 19 source-bound unavailable direct operations and zero
unbound native rejections or missing table fixtures. Its receipt SHA256 is
`adc25773fa9136bb3a8145a5e2436b0ebcad1be4a20a017c1159c89897a6f6ab`.
The matching source-binding outcome is PASS; the operation-matrix outcome remains
UNVERIFIED. Local HTTP/SignalR accounting reconciles exact original TRX, source
and all six local assembly bytes without fabricating a GitHub run identity.
Trusted workflow attestation, normative mappings and owner authority remain
unverified.

The first combined PostgreSQL development review run reported checkout head
`f05195a333ebb60c5001ed93cadf9e0013ec9a00` and passed 15 of 22 facts while failing seven,
with zero skips. Its retained private TRX SHA256 is
`3ab1020578ae6c36cb1a9cd21c8fa9e2a86224fca2f6968889d35627c3331e8e`.
The source-reference failure occurred in temporary database deletion; six other
failures surfaced dependent-role cleanup errors. The assembly was built before
that source commit, so the reported checkout head is not clean-candidate proof.

Serializing only fixture database creation/deletion was insufficient: two new
disk-backed development runs reported 19/22 and 21/22 passed, with three and one
failures respectively and no skips. Their private TRX SHA256 values are
`799f12e45a9c02c9c214f99aad28a160c1d87e6e5ce5bb590fe9412bbd89901a` and
`0903ce811252e047f176d3fbdb03101fdb85a2de32a83b0adeeda5c707e53fde`.
Live metadata showed deletion waiting on `CheckpointDone`/`CheckpointStart`
while the checkpointer waited on `DataFileSync`. The speculative lifecycle
serialization has been removed; the original helper deadlines remain unchanged.

An explicit bounded volatile-storage development rerun passed all 22 facts,
with no skips. Its TRX SHA256 is
`d5cb175c7649d28afd65b13870d27bc206f4f18ca792dad002c27cc744798183`.
These four runs retain their actual source/build identities and do not certify
a clean candidate. The final launcher must rebuild and reconcile its exact
clean checkout, all six assemblies and independent input bytes.

The local launcher now accepts `--postgres-storage tmpfs` for a disposable
2 GiB fixture; `disk` remains the default. It checks actual Docker configuration
and filesystem type, and requires `fsync` and `full_page_writes` to stay enabled.
The environment fingerprint records storage mode, bound and lack of crash-recovery
qualification. Volatile storage can verify policy and transactional controls;
it cannot establish restart durability, operational backup/PITR or production
storage performance. No test deadline or suspended performance threshold changes.

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
