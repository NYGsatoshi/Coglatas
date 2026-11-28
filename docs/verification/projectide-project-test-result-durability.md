# Development Project Test original-result durability

Issue #1945, Owner J09. This follow-up retains the actual existing local Test
run body with its winning durable job completion. It does not adopt a formal
Artifact syntax, register a new type, choose required suites, or grant evidence
eligibility, Producer Trust or operational activation.

`ProjectTestRuntime` captures the immutable evaluator-produced
`coglatas.project-test-run/1` canonical bytes and their existing digest. The
Development job store writes those bytes and the original summary in the same
pending-to-terminal transaction. PostgreSQL `bytea` preserves the exact bytes;
the payload is not rewritten through JSONB. Original Source/input identity,
the execution-input dependency binding, evaluator identity and plan/result
digests remain bound by the existing job and result contracts.

The row lock and immutable-terminal trigger preserve the winning completion or
cancellation. A losing producer receives the stored winning receipt, including
its original timestamps and retained-body state. Revoked authority, cancellation
or an exception before commit rolls back both result and body. No separate blob
or partial publication survives a failed completion.

The retained result is internal store payload. Existing lifecycle, read and
export DTOs are unchanged and do not expose assertion bodies. Existing separate
operation checks still govern every runtime response. Serialized job metadata
also excludes the retained payload. Content integrity does not establish trust,
environment attestation, required-suite admission or shared Merge authority.

Retained bytes must be canonical and match the original result digest, Source,
input and derived Compiler input identity, plan digest, counters and outcome
axes. Invalid present bytes have an explicit `Mismatch` state and are withheld;
they are distinct from original bytes being `Unavailable`. A historical
summary-only completion remains readable under its original authorization but
has no invented body. Neither a terminal retry nor a later evaluator rerun
backfills it.

Fresh explicitly provisioned Development stores include the nullable result
column. An existing Development store requires an explicit isolated call to
`ProjectTestJobDevelopmentProvisioning.UpgradeRetainedResultsAsync`; that
transaction adds the nullable column and its lifecycle constraint without
changing or backfilling existing rows. No startup composition invokes this
upgrade automatically. The ordinary EF model and production migrations remain
unchanged.

Focused verification covers immutable original-byte recovery in fresh request
scopes, concurrent completion and exact retry, cancellation winning publication,
late persisted revocation/cancellation/failure rollback, mismatched pin refusal,
terminal-body mutation refusal and explicit legacy-schema upgrade without
backfill. Current-source execution and hosted qualification must be recorded
separately before integration. Full #1945 acceptance, environment/provenance
admission, audience/retention policy, real clients and operational activation
remain separate.
