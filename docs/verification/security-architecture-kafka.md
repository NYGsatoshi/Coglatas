# SEC-ARCH isolated Kafka fixture

## Linux client-file ownership repair candidate

Exact Main `4a2b562ce3edd529cfbe91e8c9cc8b6ea16679c9`, run 37947757369
attempt 1, passed 2,143 backend tests but the Kafka step failed authenticated
readiness before any ACL control ran. That result remains ERROR.
The pinned broker runs its CLI as UID/GID 1000. A controlled disposable Linux
probe found a root-owned 0600 file unreadable to that user and an owner-created
0600 file readable. [Docker's copy semantics](https://docs.docker.com/reference/cli/docker/container/cp/)
assign root ownership inside containers; host permission translation can hide
the problem on Windows. This is the diagnosed fixture defect, pending fresh
Linux broker execution to qualify the repair against the original failure.

Verifier v2 creates synthetic client configurations inside the container as
the same non-root user that runs the CLI, with umask 077 and no overwrite.
It verifies exact 0600 mode and matching owner/group before authenticated
readiness. Fixed principal paths reject path/shell injection. Credentials stay
off stdout and are removed with the owned container. Authentication, ACLs,
timeouts, denial assertions and the 16 runtime controls remain required.

Tracks #1149 and candidate-bound fixture evidence under #1152. The product Kafka adapter remains inactive/conditional. This fixture does not activate product infrastructure or certify product Kafka compliance. PRE-AVALONIA SEC-ARCH remains BLOCKED by the outstanding mapping, complete RLS/runtime and assurance gates.

## Isolation and execution

The runner pins the official Apache Kafka 4.1.2 image by immutable digest. It creates one disposable KRaft broker/controller with network=none, no host port publishing, loopback listeners, SASL authentication, StandardAuthorizer, exact topic/group/host ACLs and no-ACL deny fallback. Credentials are randomly generated in a private temporary directory, never printed or written to evidence. Only the synthetic fixture administrator is a superuser; Alpha/Beta clients are not. PLAIN is limited to this network-disabled fixture; it is not a production TLS/security configuration.

Clients execute inside that same container. No external broker, production topic, operational credential or school/customer data is used. Readiness is authenticated. Effective non-secret broker settings and actual ACL inventory are checked against explicit synthetic expectations. The container label/name and resolved temporary directory boundary are checked before cleanup. Per-command container timeouts bound client execution as well as host process waits.

```text
node --test scripts/security/sec-arch-kafka-acls.test.mjs
node scripts/security/run-sec-arch-kafka.mjs artifacts/sec-arch/kafka.json
```

Run from this repository root. Candidate-bound execution requires a clean tracked checkout; the exact HEAD is checked again after execution. Development runs can use --development, which records UNVERIFIED for a dirty checkout even when individual controls pass. Missing setup, timeout, generic failure or cleanup failure cannot become PASS. Do not reuse development evidence for a PR/Main candidate.

## Controls

Real producer/consumer controls establish both authorized tenant identities. Rejection checks cover authentication, foreign topic read/write, unauthorized producer/consumer, foreign consumer group, no-ACL fallback, topic Describe and cluster privilege. A masked Describe denial requires live own-topic and administrator-visible target controls; resource absence or a broken client is insufficient.

Real ACL mutations add excess literal/prefixed access and remove required write privilege. The inventory adapter rejects drift, and a real producer loses write permission. ACLs are restored and verified. The superuser control demonstrates its intentional bypass risk and excludes that identity from tenant compliance.

Synthetic events traverse the real broker, then a tenant-bound consumer stub rejects a forged tenant payload and deduplicates an actual replay. This does not establish a product Outbox/consumer adapter or exactly-once broker delivery. Unit mutations additionally reject wildcard/prefix/broad-host/excess/missing/duplicate/disabled ACLs, superuser/fallback/authorizer/authentication configuration changes and foreign event processing.

## Evidence and CI limits

The report records verifier/version, exact candidate, environment fingerprint/image digest, UTC execution, sanitized per-case outcomes, execution scope and open blind spots. It contains no raw payloads, ACL command output, token/password, broker configuration or private specification. Product state remains INACTIVE_CONDITIONAL with no inferred owner activation approval. Trusted CI report digest/receipt reconciliation and canonical contract/SPEC allocation remain separate prerequisites; a local JSON report is not server attestation.

PR preflight runs bounded parser/mutation tests. Existing Main Test runs the full broker fixture and retains the sanitized artifact. Check names, rulesets and existing checks remain intact. Fixture assertions test verifier quality; this does not promote the full SEC-ARCH compliance gate from Advisory or close #842/#614 audits. No continue-on-error, skip, baseline growth or existing-failure suppression is introduced.

Initial development runs exposed an incorrectly encoded listener environment key and a missing DescribeConfigs fixture privilege, then established the masked Describe behavior with working controls. Failed reports are retained locally; subsequent successful fixture execution does not rewrite them as PASS. Complete release/Main reconciliation and approved product contracts remain open.
