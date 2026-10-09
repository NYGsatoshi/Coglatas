# SEC-ARCH isolated fixture status

Tracks #1144–#1152 and RLS children #1156–#1158. **PRE-AVALONIA SEC-ARCH: BLOCKED.** The initial formal mapping, complete policy/role approval, product RLS activation, full adapter coverage and trusted exact-candidate qualification remain outstanding. This file contains only reviewed public status and synthetic test instructions; the detailed mapping, table classification proposal and defense design remain private.

## Implemented verification

The typed contract CLI and deliberate-invalid tests are documented in [the tool README](../../tools/Coglatas.SecurityArchitecture/README.md). Syntax and evidence consistency checks are not approval authentication or runtime attestation.

SecurityArchitectureInventoryTests materializes actual controller route metadata, reflects the active Hub and compares the EF model with a real migrated isolated PostgreSQL catalogue. Optional COGLATAS_SEC_ARCH_PRIVATE_INVENTORY_DIRECTORY output must target a private review location. It does not extract the complete production host/default/fallback/minimal endpoint configuration or establish authorization success. Unknown endpoints, tables and operational role properties remain UNVERIFIED. The SQL inventory uses a migrated temporary database, independent of the shared test database's schema.

SecurityArchitectureRlsTests uses the existing PostgreSQL migration fixture and a unique authenticated non-owner/non-superuser/non-BYPASSRLS role with explicit grants. It enables test-only RLS on two synthetic-backed existing tables and checks valid Alpha/Beta access, foreign-row SELECT/INSERT/UPDATE/DELETE denial, missing/invalid context, transaction commit/rollback reset, pool reuse and denied side effects. Actual broad-grant, BYPASSRLS, allow-all policy and disabled-RLS mutations are detected and restored. Privilege revocation is checked with a specific PostgreSQL permission error. The database and role are removed after the test.

This representative probe does not apply RLS to the product, qualify every required table, prove deployed role equivalence, or implement API→EF context propagation. Custom context settings remain changeable by a role with arbitrary SQL; this mechanism does not provide complete protection from a compromised role. Approved all-table policies, parent-derived/global/internal semantics, worker/claim/export/audit/retry behavior and product activation remain open under #1148/#1156–#1158.

SecurityArchitectureServiceTests runs an actual loopback-only Kestrel TLS service with short-lived synthetic certificates, exact certificate pinning and synthetic HMAC-signed test credentials. It checks two authorized identities and rejects wrong identity/issuer/audience, missing/expired/revoked credentials, missing/excess scope, tenant spoofing and an unintended operation without side effects. Plain HTTP and wrong certificate trust are rejected, with live positive controls before and after. Destination/network-rule mutation checks validate fixture policy structure; they are not deployed firewall/Kubernetes enforcement.

The synthetic credential protocol is a test stub, not product JWT/OAuth or service-token authentication. Certificates are never installed as a trusted machine root. Temporary imported private keys are disposed; no PersistKeySet or committed certificate/credential is used. The product remains a modular monolith; this fixture does not introduce or activate a product service, mesh or cluster.

SecurityArchitectureSignalRTests launches the actual Web entry point against a
dedicated migrated PostgreSQL database and SEC-02 synthetic Alpha/Beta users.
It uses real cookie login, CSRF-protected negotiation and WebSocket JSON Hub
invocations without replacing authentication, the Hub, dispatcher or current
resource authorizers. A supported synthetic private ProjectChannel supplements
the existing legacy-type canaries without altering them.

Connected positive controls precede negative delivery assertions. Coverage
includes tenant/workspace/project/conversation subscription boundaries,
unexposed arbitrary group joins, foreign event non-delivery, permission
revocation, actual replay delivery to the still-authorized peer, revoked and
expired sessions, rejected reconnect and a read-only posting downgrade with
unchanged message/Outbox state and retained denial audit. The positive posting
control follows API commit through PostgreSQL Outbox and the production
dispatcher to an actual WebSocket event.

This representative transport matrix does not qualify every event type,
capability combination, browser origin/reconnect/catch-up behavior or manual
replay service authorization. Fixture repository replay bypasses the manual
replay service intentionally; that service's authorization remains unverified.
Full #690/#1150 mapping and runtime coverage remain open. No product RLS,
infrastructure activation or security-evaluation Enforce promotion occurs.

## Local execution

Use a dedicated disposable synthetic PostgreSQL instance and the existing POSTGRES_TEST_CONNECTION_STRING fixture setting. Never supply an operational database or credentials.

For reproducible local qualification, `scripts/security/run-sec-arch-runtime.mjs`
creates its own digest-pinned PostgreSQL 18.6 container and dedicated bridge,
publishes only a random loopback port, compiles the exact clean candidate once
and executes the reviewed SEC-ARCH catalogue. It accepts no external database
connection. Sanitized candidate/build/environment/TRX receipts stay local;
ownership labels protect cleanup of its own container/network. Docker and the
existing .NET SDK are required, with a pinned Python container for TRX parsing.

```text
node scripts/security/run-sec-arch-runtime.mjs artifacts/sec-arch/runtime.json --candidate-sha <exact-clean-SHA>
```

Retained receipt paths must be new. The isolated Kafka runner similarly accepts
a JSON artifact path and optional exact `--candidate-sha`; unknown/duplicate
arguments are rejected. Outputs cannot replace an existing receipt. A passing
local representative run is separate from trusted CI attestation and full
Avalonia exit acceptance.

```text
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj --filter FullyQualifiedName~Coglatas.Tests.SecurityArchitecture --logger trx
```

The PostgreSqlFact category requires the connection setting. Existing discovery marks it skipped locally when absent and fails in CI when missing. A skipped/provider-unexecuted result is not RLS acceptance. Report the actual TRX counts and environment; do not infer PostgreSQL from InMemory tests.

The existing backend Required Check executes these discovered tests with its synthetic PostgreSQL service. No check name, ruleset, product migration or enforcement state changes. Full catalogue reconciliation and trusted Main/release evidence remain separate work under #1152/#842/#614.

## Open gates

The private PostgreSQL catalogue also records object ownership, role privilege
flags and memberships, effective table/schema/column/default grants, policy
expressions, view security options, partitions, function execution grants and
security-definer/search-path metadata, TenantId types, foreign keys and indexes.
It reads no table contents, role passwords or function bodies. Its identities
and privileges describe only the isolated migration fixture; deployed application
and worker equivalence remains UNVERIFIED. Detailed output must stay private.

- Personal owner approval of the initial private TMT/SPEC mapping and reconciliation of #835/#836 identities.
- Complete RLS classification, approved applicable operations/roles/policies, all-table real row controls and safe runtime/migration integration.
- Separate exact-diff product RLS activation approval.
- Complete API/OpenAPI authorization contract coverage and reused #576 real HTTP matrix evidence.
- Complete #690 real SignalR event/capability/origin/catch-up coverage and #1150 contract reconciliation; the representative real transport tests above do not qualify the entire catalogue.
- Isolated Kafka broker/ACL qualification and complete service/event/replay coverage; production activation remains separate.
- Trusted execution/digest/SHA reconciliation, false-green audits and existing CI qualification before assurance promotion.

No SEC-ARCH child is declared complete by these fixtures.
