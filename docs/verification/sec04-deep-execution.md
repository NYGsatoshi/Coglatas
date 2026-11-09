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

## Retained first-source outcome

Source `c6c56de0541e6fc38469782c1d9f184974a460c4` produced
[fixed-push run40440632930, attempt1](https://github.com/NYGsatoshi/Coglatas/actions/runs/40440632930),
a workflow-validation failure with zero jobs. No unit or scanner execution is
credited. That source placed runner-context expressions in job-level environment
configuration; the forward correction puts those expressions in step-level
environments. The first failure remains historical and is not rerun or relabeled.
Fresh source qualification is required.

The next accepted source `6bbbb51245e19a48d4b02c1994782d58eade6bb9`
ran13 deterministic contracts and the existing processor contracts successfully in
[40441302923/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/40441302923).
OpenAPI generation, current image build, preparation and SEC-03 exited0;
SEC-04 exited1 and cleanup exited0. This remains a runtime-stage failure with
unknown native cause. Closed outcome artifact12706357663 has archive SHA-256
`e2aff1d53cec8896dffd3c9a4bb90ce34017a1346481df64af9fcf96cfd287aa`.

Source inspection identified a new caller compatibility hazard: umask077 makes
the generated public OpenAPI path unreadable to the unchanged capability-dropped
preflight containers using another UID. The forward caller reserves only a fresh
public SEC-01 subtree and sets its two directory modes0755 and generated JSON
mode0644, admitting the mounted public source traversal first. It does not adopt
existing directories, recursively change modes, or touch private log, runtime,
credential, report or key modes. The public artifact is generated solely from
the public Product API source; no private specification or credential is staged.
This source hypothesis is not retrospectively asserted as the prior native cause.

Failure-only observations retain bounded private-log digests, fixed progress/error
classes and typed per-role metadata availability/counts/digests. No child text,
SQL, identifiers, responses or authentication values are published. These are
untrusted observations with qualificationNONE; they never satisfy the unchanged
five-role native success guard or turn any failed stage intoPASS. Both prior
source failures stay immutable; fresh execution must qualify independently.
