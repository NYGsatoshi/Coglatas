# Task run request admission controls

This additive candidate fixes existing `RequestRunAsync` admission and failure
response contracts in sixteen cases: nine invalid key forms, two printable
boundary lengths, four missing coordinator results and one exception before
staging. It changes no production code, existing tests, source policy,
runtime provider, activation, schema, producer, coverage scope or threshold.

The initial independent source review identified incomplete seed preconditions
and missing generic replay-message assertions. The follow-up explicitly binds
the stored override and Project scope, Accepted run status/scope/provider,
Idempotency key/fingerprint/resource identity, Outbox Task envelope and audit
actor/resource identity before each operation. It freezes the retained run ID
before checking a new coordinator resource identity and rejects shadow fields
in all snapshots. Every original candidate assertion remains in place, with
the generic replay message now asserted for each unavailable outcome.
The initial native job passed all sixteen cases within 848 scoped tests, but
the initial ReSharper lane rejected a token used only in an assertion helper.
The follow-up retains that assertion and also compares the complete key/token
identity and independently frozen non-reusable run IDs. The original native
success and analyzer failure remain separate historical results; the new
source requires fresh execution and review.

Each case first creates a real accepted run through the existing service and
EF InMemory idempotency coordinator, with a stored override, Idempotency row,
Outbox row and recorded audit entry. Refusal must retain all mapped scalar
properties and identities of every unfiltered Task, Project scope, override,
run, Idempotency and Outbox row, and the recorded audit list. No added,
modified or deleted tracked entry may remain. The snapshots accept no changed
fields; they do not project a subset of mutable fields as an allowed result.

Invalid keys must not reach the replacement coordinator. Printable keys of
length eight and 128 retain their leading/trailing spaces and reach the
coordinator with exact actor, Tenant, operation, original key, Task-derived
fingerprint, fresh resource identity and cancellation token. Missing Created
or Replayed values cannot be reported as acceptance. Request mismatch retains
the header error target; unavailable replay and a pre-staging coordinator
exception return their existing generic errors. The injected coordinator
never invokes either callback or writes state; these are deliberately bounded
admission/response controls, not post-staging failure atomicity tests.

The counting unit wrapper observes only calls through that wrapper, not direct
DbContext saves. The existing fixture constructs the EF coordinator without
its execution-scope repository, so it does not establish source-policy document
persistence. Complete stored-row snapshots and tracked-state checks remain
separate observations; they do not supply those missing guarantees.

The Project authority adapter and clock are the existing synthetic fixture.
These cases do not establish current persisted authority, PostgreSQL behavior,
HTTP validation/privacy, transaction rollback after staging, concurrent
idempotency, actual dispatch, file materialization, runtime execution,
application roles/RLS, orphan reconciliation or Recovery. They grant no
canonical-spec/runtime promotion and do not close Issue #357 or #461. The
original Task scope and ClearOverride tests remain byte-identical.

Compilation, named hosted execution, independent full-diff review, required
checks and original PR Gate remain pending until separately observed. No local
.NET, WSL or PostgreSQL execution is claimed. This candidate follows #1891 and
#1892 and their complete resulting-Main evaluations; it cannot overtake them.
Every eventual merge requires fresh exact-Main native/report/source evidence
and actual official covered-line, condition and denominator measurements.
Existing Owner holds and the pilot NO_GO remain in force.
