# Bounded materialization admission using existing Core contracts

Related: #905, #798, #1666, #1667, #1668. Product review base:
`585727a9b480e61d08cbeeb3d996904b5f1f4b5e`; independent Test review base:
`6edddea75a8dfd0ecaca8559827ffb92ba06a365`.

## Implemented runtime boundary

`ProjectSemanticBinder.Bind` continues to accept the existing immutable
`CompilerInputManifest` and `CompilerMaterializedSnapshot` in-memory contracts.
It validates exact snapshot ID, version, Source digest and full revision context
against `authorizedExternalSnapshots`. No new public schema or receipt is added.
The Source/input golden encoding and meaning of existing reference digests stay
unchanged. The input's embedded Source document manifest is **not** the proposed
Option B dependency-manifest artifact.

Admission now detects duplicate snapshot identities even when no reference uses
them, conflicting bodies/versions/coverage claims, and attempts to substitute a
snapshot for the local Source context. Conflicting contexts cannot select the
first otherwise matching body. Findings use deterministic local locations and
deduplicated stable codes. Unknown pin or Source envelope meaning remains
Unsupported; original immutable data is preserved.

Supply enumeration checks cancellation before and after each `MoveNext`, disposes
the enumerator on every exit, and charges entries and authorized bodies to the
same operation/declaration budget as local binding. Exhaustion before admission
publishes no partial symbol/materialization set. The provisional 32-entry cap
counts denied and null entries as well. Caller enumerators must be cooperative;
Core cannot preempt a blocking `MoveNext` or arbitrary caller code.

Denied bodies are not inspected for version/digest/pin integrity or decoded into
symbols. `BoundSnapshots` contains admitted authorized bodies only. Contexts are
retained privately for OutOfScope resolution. Public findings never expose a
denied body's ID, digest, revision, metadata, coverage or content, and different
denied bodies produce the same generic local finding.

## Separate materialization and evidence stages

| Stage | Current evidence | Limit |
| --- | --- | --- |
| Declared dependency | Exact existing input pin | Declaration only |
| Available body | Explicit caller-supplied Source | No acquisition producer |
| Verified integrity | Exact immutable Source/pin/version/context match | Matching content does not confer trust |
| Authenticated producer | UNVERIFIED | No receipt signer/attestation contract |
| Current authorization | Existing host boolean claim only; UNVERIFIED by Core | No freshness/revocation interface |
| Eligible evidence | Unavailable | Receipt presence cannot establish eligibility |
| Requirement satisfaction | Not discharged by these helpers | `requirement-evidence-unavailable` remains |

This is not a receipt producer or a trusted closure certificate. The existing
Evidence and Resource helpers retain their separate Missing/Unsupported outcomes.
Resource Definition, Project-local Binding, Allocation, external Snapshot,
Materialization, Authorization and Evidence remain different objects/stages.
Actual snapshot acquisition and Resource arithmetic/reservation stay HOLD.

## Independent verification and proposal models

The SecurityTestImplimation-derived Test line owns
`CompilerMaterializationBindingTests`: duplicate/conflicting supplies, exact
body/version/context substitution, unknown dependency, unknown envelope meaning,
non-disclosure, cancellation, disposal and shared budgets. Initial behavioral
reproduction on unchanged Product `585727a9` with Test `8404e250` executed
**8 FAIL / 5 PASS / 0 SKIP** (13 cases), rather than a missing-API compile failure.
Exact final candidate evidence is recorded in the existing continuation ledger.

`tests/ci/projectide_materialization_prototype.py` is a **test-only,
non-normative** executable proposal model, owned entirely by Test. Independent
controls exercise Source/manifest substitution, revision/candidate/Scenario
replay, body tampering, unknown schema/canonical versions, duplicate/conflicting
pins, missing/unknown bodies, explicit unverified trust, partial graph frontiers,
scoped node/edge identity, exact versions, deterministic traversal, cancellation,
budgets and provisional kind-specific cycle rules. Its ASCII hash examples and
tuple shapes are not production canonicalization, schemas or authorization.

Logical related-reference cycles and artifact content-hash cycles are separate.
The prototype permits related logical cycles and rejects dependency cycles only
under its explicitly provisional rule. Production graph schema and per-kind
closure policy remain pending; no graph result from this model reaches Core IR.

`external-closure-not-lowered` remains intact. Actual transitive external lowering
requires each dependency's approved input/manifest, authorized materialized body,
supported semantics and complete closure. No new external navigation or native
UI acceptance is claimed. #1144 native UI/IME/accessibility/activation remains
HOLD; #1128 remains SUSPENDED / NOT_EVALUATED.
