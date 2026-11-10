# Artifact evidence manifest validation controls

The additive partial suite retains the seven original Facts and their fixture
implementation. The baseline changes only its outer and fixture declarations
to `partial`; a test-only fixture property exposes the existing context.

Forty-one invalid-input rows cover request cardinality, claim/evidence ordinals,
required text and enum values, evidence cardinality, bounded provenance fields,
publication chronology, conflicting repeated-source provenance and Finding
severity/confidence/detector/policy limits. Except for request cardinality, a
valid first claim precedes the invalid second claim. Each refusal checks its
exact application error, selected Artifact/two-Version scalar state, empty
claims/evidence/Finding/history tables, no pending added/modified/deleted tracked
entity, unchanged Artifact/Version/Attachment/FileObject cardinality, zero audit
calls and zero unit-of-work saves.

Two positive rows use the exact field-size and twenty-evidence limits, equal
publication/retrieval times, normalized enum/text inputs and Finding confidence
0/100. They compare every selected evidence field, initial Finding/history
identities, audit metadata, one save and the untouched other Version.

These are application-service controls backed by the original EF InMemory
fixture. They do not establish PostgreSQL transactions, persisted-current
authorization, HTTP privacy, file bytes, delivery, RLS, unknown-outcome orphan
reconciliation (#1683), full Recovery or pilot acceptance. The test Finding is
synthetic application data; no formal scanner disposition or Owner approval is
performed. Existing product, authorization, fixture behavior, coverage scope,
budgets, thresholds and report bytes remain unchanged.

Exact-source compilation, named hosted execution, analyzers, six required
checks, official original PR Gate, independent review and live merge guards
remain pending. Resulting Main must be qualified separately, including actual
coverage denominator/covered-line/condition deltas. No local .NET/WSL execution,
source exclusion, report rewriting or projected/summed Main gain is claimed.
