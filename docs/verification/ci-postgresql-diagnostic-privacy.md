# Isolated CI PostgreSQL diagnostic privacy

Status: implementation candidate; hosted startup/probe and exact-candidate CI evidence required.

The PR and Main backend PostgreSQL services previously used default error logging.
Expected negative tests can make PostgreSQL emit row values in `DETAIL`, query text
in `STATEMENT`, and trigger context. GitHub runner service teardown publishes that
stderr independently of sanitized test artifacts. Synthetic-only fixtures do not
make raw row projections acceptable public evidence.

Both services now initialize with terse error fields, failing-statement logging
disabled below PANIC, and parameter logging disabled. Primary error messages remain
available; assertions and client-side error/SQLSTATE behavior are unchanged. These
are isolated CI settings, not production configuration or authorization changes.
PostgreSQL documents [error logging controls](https://www.postgresql.org/docs/18/runtime-config-logging.html)
and [persistent initdb settings](https://www.postgresql.org/docs/18/app-initdb.html).

Before migrations, `verify-postgres-log-privacy.py` checks effective `pg_settings`
and refuses unsafe/missing settings. A temporary synthetic CHECK-constraint probe
requires SQLSTATE 23514 and its live primary server error. Unique row and SQL
statement canaries must be absent from captured service diagnostics. Empty logs,
unavailable Docker/psql, unexpected errors and unapplied settings fail the job.
Captured diagnostics stay in memory; failures print bounded messages without child
output. Mutation tests exercise each setting, disclosure paths, unavailable/silent
logs, subprocess errors and workflow ordering. This guard does not certify every
possible database primary error message or other test/scanner logging surfaces.

Historical logs are retained as historical evidence. This repair does not claim
that previously published diagnostics were sanitized, or close SEC-16/SEC-17.

## Local candidate verification — 2026-10-23

- Nine focused mutation/control tests passed, with no failures or skips.
- Both workflow files parsed with PyYAML 6.0.3 installed only in ignored local
  tooling. Publication-readiness and required-check topology guards passed;
  all 54 topology parser tests passed.
- A new disposable container using the workflows' pinned PostgreSQL 18.6 image
  passed the effective-setting and live SQLSTATE/server-error/canary probe.
  It had no network, host ports or attached application volume.
- A second disposable container using default logging was rejected before
  the probe write. Both owned containers were removed after verification.

These are local candidate checks. Hosted PR execution and resulting-Main
qualification remain necessary; no application acceptance or target-host
verification follows from this isolated diagnostic probe.

## Release traceability limits

| Requirement | Existing evidence | Remaining qualification |
| --- | --- | --- |
| SEC-16 / #689 | #1325 merged as `babcf80f7babafe4087fb7cb5742edb2b98868c2`; required FCI-07 File grant HTTP positive/negative/revocation owner | Full lifecycle HTTP acceptance, exact lane accounting and #582 finding-policy integration; issue closure alone is not completion evidence |
| SEC-16 expiry/lifecycle | `FileDownloadGrantBoundaryTests` covers expiry/revocation/policy changes; `TaskV1FileOpenDownloadReauthorizationPostgreSqlTests` covers persisted lifecycle with a storage spy | Unit/service tests do not substitute for complete HTTP/browser lifecycle acceptance |
| SEC-17 / #690 | #1326 at HEAD `f47e4fb044dd2449016066bf08240438104242eb`; run `39924730456` executes merge candidate `0d5ead3cd9286af1849bab8830d9d62ef327d0fc`; named private-channel real-transport test passes | Preserve distinction between PR HEAD, test merge and post-merge Main; #690 remains open |
| SEC-17 broader controls | Exact Main `babcf80f7babafe4087fb7cb5742edb2b98868c2`, run `39924684501`, backend job `120319741671`: all 14 SignalR/Event methods explicitly PASS; backend 2,591/2,591 and architecture 12/12, both TRX verifiers PASS | This is existing broader control execution, distinct from the 438-case scoped #1326 run and full #690 acceptance |
| SEC-ARCH / RLS | Public owner gates and preflight contract remain authoritative | Formal mapping, concrete policy/role approval, complete RLS/runtime qualification and activation remain separate holds |

No private specification content, real school data, tokens or protected row values
belong in this report. Public release, invitation delivery and real-data intake
remain NO_GO until the complete release gates and explicit owner GO are satisfied.
