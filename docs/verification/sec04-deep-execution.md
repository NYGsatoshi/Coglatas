# SEC-04 deep caller execution boundary

The former manual `sec04-schemathesis-deep.yml` job only validated Compose
configuration. That check established topology, not scanner execution. Issue
[#574](https://github.com/NYGsatoshi/Coglatas/issues/574) requires the existing
isolated SEC-02/03 runtime and pinned Schemathesis execution to fail closed.

The caller now builds the existing public, backend-only security runtime image
from the checked-out source, verifies the current SEC-01 contract, and invokes
the unchanged `prepare`, `sec03`, `sec04` and `cleanup` stages once. The deep
role set, examples, deadlines, scanner image, policies and no-retry behavior are
unchanged. No public target, protected environment, private build credential,
licensed UI, new scanner engine or production activation is introduced.

A passing caller additionally requires all five current deep-role metadata
records, nonzero requests/operations, zero scanner/network failures, the exact
current contract hash, and the existing native parser's matching Initialize and
completed EngineFinished events within this execution window. Missing, stale,
wrong-role, failed or incomplete native evidence fails. Unobserved OpenAPI
operation counts stay explicit; this does not establish every security-program
criterion, formal RLS, release-image attestation or Owner acceptance.

Only a closed source/run/attempt-bound digest/count outcome is uploaded. Child
logs and native reports remain private to the hosted runner and are not retained
by this caller after teardown. First failed receipts cannot be overwritten.
Configured source and deterministic fixture tests are not hosted runtime proof.

The Owner-paused weekly cron remains commented out. The fixed, read-only
`fix/sec04-deep-execution-20261027` push route provides source qualification when
manual dispatch is unavailable; it does not reactivate scheduling or change any
required security context. Source publication requires independent review.
