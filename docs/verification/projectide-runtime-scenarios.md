# Saved ProjectIDE Scenarios in development

The development use cases save a fixed Scenario from an admitted base and
the existing typed override planner. A successful save allocates the
Scenario and revision identities on the server. The idempotency fingerprint
binds the exact base context, Source and input digests, and canonical
overrides. Reusing a key with an altered payload returns unavailable. An
identical retry rechecks current access to the original version.

An explicitly provisioned disposable PostgreSQL database keeps the scoped
immutable row and its canonical typed delta in `projectide_development`.
`ProjectScenarioDevelopmentSchema.ProvisionAsync` installs the table after
the normal AppDb migrations. Scoped SQL uses the existing AppDbContext
transaction without registering a Scenario entity in the default model.
The base Source, settings, run bodies and regional contributions are absent
from this table. The audit entry contains only the event and scope identities.
The save, idempotency claim and audit entry share one transaction.
After their writes flush, the shared coordinator rechecks current persisted
viewer and save-operation authority for both the fixed base and the Scenario
before committing. Revocation or operation denial at that boundary rolls back
the entire set and leaves the same request available for an authorized retry.
The development host composes the store, idempotency coordinator and audit
adapter on one clean scoped DbContext. A save with unrelated tracked writes or
an active transaction returns unavailable before the idempotency claim.

Metadata lookup, private assumptions, and comparison use distinct operation
policies on both the fixed base and the Scenario. Metadata lookup projects
the identity without loading the private overlay. An authorized materialization
reconstructs the original exact input from its saved delta and verifies both
the stored digest and the resulting Source/input identity before returning it.
The persisted viewer and operation policies are rechecked before disclosure.

Comparison accepts server-owned original Simulation runs and exact expected
digests. The development host supplies immutable Sources, semantic models and
run results from trusted execution or reconstruction. A client result assertion
cannot substitute for those original records. The saved baseline and Scenario
identities must match, and every materialized contribution must pass current
authorization. The underlying
comparison retains the existing observed model-clock meaning with no outcome
assertion. An incomplete comparison has no complete elapsed delta. A denied
side returns unavailable for the whole pair.

The development type is explicitly composed with its operation policy and
PostgreSQL fixture. Live registration, production access, export, rebase,
retention and deletion policies, and Web or Native acceptance require their
applicable authority and transports. See [#1951](https://github.com/NYGsatoshi/Coglatas/issues/1951).
