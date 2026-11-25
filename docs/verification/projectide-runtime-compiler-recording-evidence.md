# Development Compiler recording evidence

This implementation adds a PostgreSQL store for digest-only recording evidence
from an original, owned Compiler invocation. It supports the #1938 development
foundation and the #905 and #1034 Source and evidence contracts.

The Application projection preserves scoped Source identity, Compiler input and
materialization digests, the declared recording manifest, the actual invocation
attempt, and the receipt identity. When the host produces a result, the record
also binds the deterministic result and its evidence manifest, and preserves
execution, coverage, verdict and evidence completeness as separate values.
Host-only outcomes keep the result and capture fields empty. The original
capture state and digest describe the local invocation. The durable store retains
no Source, diagnostic or capture bodies, session tokens or session identifiers.

Tenant and actor identity come from the shared persisted viewer reader. A
development host must supply an operation authorization implementation for the
exact Source and protected premises. The store requires distinct recording and
reading operations and rechecks before commit and disclosure. A known run ID or
digest grants no permission. The application composition root has no registration
or route for this store.

The Project and Tenant foreign key binds every recording to the same Project
scope. Rows are write-once. A retry by the same actor succeeds idempotently only
for the exact same evidence. A diagnostic rerun must link a stored origin with
the same Source and execution input and the original receipt digest.

The store makes no currentness, producer trust, evidence eligibility or Release
decision. #1938 independent acceptance and the remaining #1034 capture,
retention, deletion and redacted export contracts require their own qualification.
