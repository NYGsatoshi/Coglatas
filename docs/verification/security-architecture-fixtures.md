# SEC-ARCH isolated fixture status

Tracks #1144–#1152 and RLS children #1156–#1158. **PRE-AVALONIA SEC-ARCH: BLOCKED.** The initial formal mapping, complete policy/role approval, product RLS activation, full adapter coverage and trusted exact-candidate qualification remain outstanding. This file contains only reviewed public status and synthetic test instructions; the detailed mapping, table classification proposal and defense design remain private.

## Implemented verification

The typed contract CLI and deliberate-invalid tests are documented in [the tool README](../../tools/Coglatas.SecurityArchitecture/README.md). Syntax and evidence consistency checks are not approval authentication or runtime attestation.

SecurityArchitectureInventoryTests materializes actual controller route metadata, reflects the active Hub and compares the EF model with a real migrated isolated PostgreSQL catalogue. Optional COGLATAS_SEC_ARCH_PRIVATE_INVENTORY_DIRECTORY output must target a private review location. Unknown endpoints, tables and operational role properties remain UNVERIFIED. The SQL inventory uses a migrated temporary database, independent of the shared test database's schema.

SecurityArchitectureApiInventoryTests additionally executes the actual Web entry point through the existing Test-only AV-MIG inspection exit. It captures composed controller/minimal/Hub/fallback routes, effective endpoint/default/fallback policies, authentication schemes and handler types without starting database workers. The opt-in AvMigContractInventoryDirectory output is private Draft material and never uploaded by these jobs. AvMigContractOpenApi can point to the existing generated OpenAPI 3 artifact for digest-bound operation inventory comparison; undocumented surfaces are retained for review. Anonymous metadata, implicit access through no policy and effective authentication requirements remain distinct. This is metadata observation, not authorization PASS. Conditional deployment/feature branches, middleware/static files, resource/capability/tenant execution and owner-approved contract reconciliation remain open.

The composed-host test now generates OpenAPI through the existing official
prebuilt generator, checks the generator and inspection assemblies agree, and
reconciles all 403 observed entries against 398 documented operations. The five
runtime-only entries retain their explicit source purposes: UI entry, favicon
and readiness redirects, plus authenticated Hub negotiation and transport.
Their normative classification remains Draft/UNVERIFIED. A pre-start OpenAPI
provider alone omits minimal API operations and must not qualify this comparison.
The same private report observes all eight Hub methods, five subscription types,
the DurableEvent wire message and fifteen declared event delivery boundaries.
It also records actual hosted-service registration types and the common scoped
Npgsql DbContext registration without connection identities or credentials.
Service registration observations do not establish executed service edges or
separate least-privilege application/worker database roles.

SecurityArchitectureApiAuthorizationTests executes all 381 non-anonymous
effective HTTP authorization policies, including 28 role-only policies,
against the actual migrated-PostgreSQL Web
host with an anonymous cookie jar and valid CSRF. Matching request media types
avoid incidental routing 415 rejections; only actual 401 responses count.
Authenticated auth/me controls pass before and after. These controls do not
qualify each authorized endpoint operation, resource/tenant/capability denial
or API-to-RLS integration.

All seventeen documented operations without effective endpoint authorization
now have explicit observations from their current handlers: eleven public
credential/token/status/CSRF/configuration/health operations and six operations
with application-owned actor/resource authorization (Gantt, dependencies,
progress and schedule). Their source interpretation remains Draft with approved
normative classification UNVERIFIED. Actual-host controls assert typed 401
authentication errors for all six with valid CSRF and valid request shapes.
Public credential/token rejection and a dependency-readiness 503, if observed,
are classified separately and receive no protected-endpoint denial credit.

Persisted Project-create capability controls execute the actual Web entry point,
cookie/session validation, application evaluator and migrated PostgreSQL. A
member creates successfully with a narrowly scoped synthetic grant. Committed
revocation, expiry, future grant time, foreign Workspace scope, wrong subject
and an unknown capability key return typed `CapabilityDenied` 403 without Project,
Outbox or audit creation effects. Each mutation is followed by a restored
successful create. These are synthetic fixture changes, not a product capability
issuance path or approved RLS integration.

Existing Kanban/Gantt HTTP tests retain their assertions and emit optional
explicit response-control receipts. Gantt command accounting reuses existing
schedule/progress/dependency create/delete positives and resource/role negatives;
it does not duplicate those tests. `scripts/ci/sec_arch_http_accounting.py`
reconciles method/template/status/error/timestamps with actual passed TRX,
reviewed verifier source hashes and all six current Release assemblies and loaded
copies. Historical five-assembly receipts remain partial dependency evidence.
The actual composed inventory must match the Web assembly. Without an independent
candidate/run receipt, candidate binding remains UNVERIFIED; reconciled receipts
still need trusted artifact attestation and owner-approved SPEC mappings.

The adapter reports every composed surface and each explicitly observed control.
Absent control dimensions have UNVERIFIED applicability and receive no
NOT_APPLICABLE exemption. A test name, metadata, source reference, bare PASS
field or self-declared approval cannot create execution credit. Each resource
negative requires a successful operation on the same endpoint in the same
verifier execution. All full resource matrices, SPEC bindings and product
API-to-RLS authority remain pending. Detailed observations and accounting outputs
must stay in the existing private inventory location.

The existing PostgreSQL Kanban config/move command tests additionally record
their successful operations, typed seeded-role denials and committed Workspace
membership revocation with unchanged persisted command/audit/Outbox state.
Config includes a foreign-Tenant Project denial. CSRF, stale version, invalid
position and forced constraint errors remain distinct from authority denials.
A seeded role denial does not establish a role-change reauthorization control.

Four existing cookie/session tests now prove successful `GET /api/auth/me`
before session revocation, expiry, account suspension or logout invalidates
access. These use actual current cookie/session services with EF InMemory in a
test-owned Kestrel composition. Three existing tenancy HTTP tests account for
notification-preference, execution-scope and My Tasks controls with synthetic
authentication and EF InMemory. My Tasks empty-page/zero-count responses after
revocation require their precise body assertions; HTTP 200 alone grants no
negative-control credit. These fixtures establish neither PostgreSQL behavior
nor the actual Web entry point's startup/authentication composition.

The adapter validates each verifier's explicit provider/authentication category
and current method declaration cardinality. Primary control dimensions count
PostgreSQL HTTP fixtures only; separate category dimensions retain InMemory
observations. Omitted scoped controls and unrecorded verifiers remain visible as
UNVERIFIED. Bounded JSON reads reject duplicate nested fields, nonfinite values,
excessive nesting and unrecognized observation payloads. The existing reviewed
runtime catalogue count is unchanged because these are existing Facts.

The deterministic adapter controls run through the existing execution-evidence
test entry point in the existing specification checks; no additional required
check or enforcement promotion is introduced.

SecurityArchitectureRlsTests uses the existing PostgreSQL migration fixture and a unique authenticated non-owner/non-superuser/non-BYPASSRLS role with explicit grants. It enables test-only RLS on two synthetic-backed existing tables and checks valid Alpha/Beta access, foreign-row SELECT/INSERT/UPDATE/DELETE denial, missing/invalid context, transaction commit/rollback reset, pool reuse and denied side effects. Actual broad-grant, BYPASSRLS, allow-all policy and disabled-RLS mutations are detected and restored. Privilege revocation is checked with a specific PostgreSQL permission error. The database and role are removed after the test.

SecurityArchitectureParentRlsTests adds actual migrated file-selection parents/items
and text-tenant UI revision heads/journal. Valid scoped reads and applicable writes
precede foreign-parent, reassignment, orphan and absent-context denials. Journal
UPDATE/DELETE stay ungranted and its existing append-only trigger remains intact.
The disposable fixture briefly disables and restores only the journal insert
trigger to distinguish its earlier check-constraint denial from RLS WITH CHECK
denial; both SQL error classes are checked. Pool reuse and text case sensitivity
are observed. Allow-all child and disabled-parent mutations must invalidate the
live row control, followed by restored positive controls. This does not approve
UI host identity binding, deployed roles, all-table policy coverage or activation.

SecurityArchitectureRlsCatalogTests installs a draft policy prototype on every
direct TenantId table plus both known parent-derived tables in a disposable
migrated database. It inspects enabled/forced flags, command, role, owner,
USING/WITH CHECK and effective table/column/PUBLIC privileges. Missing/extra
policies, disabled/unforced RLS, broad grants, owner and BYPASSRLS mutations must
invalidate the retained prepared catalogue and be restored. Live Outbox/audit
reads prove the injected permissive/bypass exposures; the remaining prototype
tables have no seeded rows in this catalogue scenario. Its optional detailed
snapshot remains private and Draft, and explicitly records all-table row,
operation/role approval and deployed-role equivalence as UNVERIFIED. The six-table
row matrix elsewhere remains representative; catalogue agreement is not
authorization PASS, approved policy or all-table RLS completion.

SecurityArchitectureRlsOperationTests adds source-model synthetic rows and explicit
SQL-adapter fixtures on all 105 proposed tables in a disposable migrated database.
Required parents, UUID/text scope, actual source lifecycle transitions and the
existing mutation guards remain active. Separate fresh parent graphs avoid making
an INSERT or DELETE probe fail just because another fixture owns a unique parent
or references the row. Every CRUD scenario records its same-operation positive
control, affected-row count and observed mechanism. RLS filtering/WITH CHECK,
permission errors, native constraint/trigger rejection and unexpected errors are
distinct; an unavailable positive control cannot qualify a negative result.

All tables receive a live permissive-policy exposure/restoration control, current
SELECT privilege revocation/restoration and forbidden TRUNCATE-grant detection.
Transaction-local settings are checked after physical pool reuse. Optional
`draft-rls-operation-matrix.json` output is exclusive, private and Draft; it binds
the supplied candidate, executing assembly digest, observed provider environment,
actual synthetic role flags and table/operation results without row contents.
The roles and CRUD grants are fixture hypotheses, not application/worker policy
approval. Tenant/parent reassignment probes do not establish same-Tenant resource,
subject or capability authority. Database grant revocation does not establish
authenticated session/membership revocation. Source-immutable operations and
guard-rejected probes retain their exact error class and remain UNVERIFIED.
All-table fixture rows do not complete runtime context propagation, normative
operation/role review, the identity decision or product activation.

Operation receipts also bind the live trigger, function and native constraint
identities to definition digests. Bodies remain in the disposable database and
are never exported. Deliberately disabling a guard or replacing its function
body changes the observed identity even when the trigger name is unchanged.
A trigger forging policy-error text and SQLSTATE cannot qualify as RLS denial;
the observed PostgreSQL server routine distinguishes it from a policy check.
These source-bound dispositions remain UNVERIFIED until the applicable operation
and authority are approved.

The blocked direct UPDATE/DELETE cells now include specific current-source
dispositions, exact migration-file digests and their enabled native guard or
constraint identity. Immutable records and persistent defaults are not converted
to RLS-denial successes. The automatically captured immutable File-version ledger
prevents File hard deletion through its RESTRICT foreign key. A separate positive
control exercises the Security-rule parent-deletion cascade before checking
cross-Tenant isolation; that database branch does not approve retention authority
or make direct rule deletion available.

SecurityArchitectureRlsRuntimeTests composes an isolated HTTP fixture with the
current tenant resolver, cookie events, persisted session/membership validator
and repositories. Only a validated signed principal and current membership can
create its separate immutable tenant scope. A test-owned EF transaction
interceptor applies transaction-local context to explicit asynchronous
transactions. Actual EF and raw SQL controls observe positive reads/inserts,
foreign insert denial, commit, exception rollback, connection reuse, tenant
switching and current membership/session revocation before scoped database access.
Its private fixture principal issuer does not qualify the product password-login
flow, controllers, startup or bootstrap.

The actual Outbox repository is also exercised through a distinct non-owner,
non-BYPASSRLS synthetic worker role. Current lock-token positives precede a
foreign delivery-mutation denial. Claim, delivery and stale-lock recovery run
under bounded fixture authority and explicitly owned transactions. Unscoped
repository reads fail closed with the prototype, and arbitrary SQL through the
same role can select another tenant by changing the mutable context setting.
Both limitations are retained; the fixture does not qualify platform worker
discovery, an operational application/worker identity or arbitrary-SQL containment.

Optional exclusive `draft-rls-runtime-context-application.json` and
`draft-rls-runtime-context-worker.json` receipts remain private and Draft.
Authentication/identity and worker authority, complete adapter/retry coverage,
product startup integration, migration/recovery and activation require separate
review and implementation. No production context interceptor, role, policy or
migration is registered by this test-only composition.

Additional isolated controls create real PostgreSQL SERIALIZABLE conflicts with
a separate committed writer. An opt-in test execution strategy retries the whole
transaction, uses a fresh persisted session/membership validation context before
each attempt, and retains the original validated Tenant despite mutable request
resolution. Successful retry commits one event after rolling back its first
staged write. Session or membership revocation prevents a second scoped
transaction. The product retry configuration remains unchanged and unqualified.

SecurityArchitectureRlsAdapterTests executes current configured-workflow,
Message-preference, File-version, Task-result/provenance and source-policy raw
adapters inside owned context transactions. Valid foreign targets are proven
before negative assertions; preference writes roll back and physical pooled
connections reset. It also executes current announcement/digest claim methods,
digest scheduling/failure transitions and Audit-export queue/stale-recovery
methods. These controls retain unscoped failures and distinguish application
claim-token fences from RLS evidence. Queue controls deliberately fail if an
uncomposed package/storage/authorization dependency is invoked.

Optional retry/adapter receipts remain exclusive, private and Draft. Their
synthetic CRUD and identity-display grants do not approve normative authority.
Hosted worker loops, platform discovery, full generation/publication/export
delivery, product-owned transactions and all remaining adapters require separate
qualification. No operational identity, production interceptor or activation
is selected by these fixtures.

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

Two additional real transport scenarios cover the Hub origin boundary.
An authenticated same-origin socket receives positive events before and after
foreign, opaque and malformed origin upgrades return an observed HTTP 403.
A separately configured credentialed origin receives legitimate events while
foreign-resource subscription and an anonymous connection remain denied.
The original development rejection regression failed against the host without
the origin middleware and is retained. These controls do not establish browser
cookie behavior, proxy deployment equivalence or complete event coverage.

Additional real transport controls preserve the existing scenarios and cover
cookie-based tenant switch with membership in both synthetic tenants, connection
tenant affinity, committed membership revocation, reconnect and authoritative
HTTP message catch-up. A cookie switch does not revoke other valid memberships
or retarget an existing connection. Project/Workspace unsubscribe controls prove
that only the calling connection loses delivery, with legitimate peer delivery
and restored subscriptions before/after.

SecurityArchitectureSignalREventTests supplies positive live delivery and
foreign-tenant non-delivery for every one of the fifteen declared catalogue
types through the real PostgreSQL Outbox and WebSocket dispatcher. All fourteen
protected event types also reject committed tenant-membership revocation with
a live authorized peer. The metadata-only authorization invalidation has its
distinct recipient-mismatch control and retains its deliberate delivery-before-
subscription-removal behavior. Actual persisted Task, Project, File and recipient
Notification targets support the applicable authorizers. Synthetic durable
envelopes do not execute every business producer, payload schema or role/grant
combination, and a declared workflow event does not establish an active publisher.
Canonical SPEC mapping approval and full #690/#1150 qualification remain open.

The execution catalogue retains all original 89 representative cases and adds
five explicitly named HTTP/transport scenarios (94 before other independently
reviewed coverage additions). Omitting any new scenario remains UNVERIFIED.
Internal event/endpoint controls are recorded separately; a scenario count is
not a count of approved normative requirements or complete adapter coverage.

## Local execution

The candidate-bound runtime launcher also requires four actual PostgreSQL
manual replay service controls: authorized original-event replay and required
reason audit; persisted grant/session/membership/role/scope negatives without
effects; a worker claim committed after an earlier tracked read; and rollback
after a real required-audit constraint failure. These provider method identities
are explicitly included even though they use the existing PostgreSql namespace.
Omitting them leaves execution coverage UNVERIFIED. Their representative coverage
does not certify grant issuance, every event/routing contract or operator rollout.

OutboxReplayPostgreSqlTests exercises the existing manual replay service with
real persisted sessions, users, memberships and tenant-scoped capability grants.
Replay retains the PlatformAdmin restriction and also requires current session,
role, account, membership and `realtime.outbox.replay` grant authority. Revocation,
expiry, wrong scope, foreign events and unsupported states cannot mutate the
event or create a replay audit. A deliberate PostgreSQL audit constraint failure
proves that the repository's immediate replay save rolls back with the required
audit. The original event identity, payload and routing are preserved.

This hardening adds no replay endpoint or capability issuance path. The isolated
fixture seeds synthetic grants directly; product operator eligibility, grant
issuance, full event routing authorization and approved contract registration
remain separate review and integration work. It does not complete #1150.

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

## Same-Tenant transport and current resource controls

The existing catalogue transport fixture also proves recipient isolation for
all five default User-routed event types and the TaskChanged User route, with
both same-Tenant recipients subscribed.
The existing unsubscribe verifier now covers Conversation as well as Project
and Workspace, proving initial delivery, removal of only the calling connection,
idempotent removal and restored delivery. Its historical verifier identity is
preserved.

Two additional actual-Web/PostgreSQL/WebSocket scenarios reject subscription
and delivery for hidden same-Tenant Workspace, Project and Conversation
resources, and exercise committed current read changes. Workspace membership
suspension and explicit MembersOnly Project membership removal each cover
thirteen resource-dependent catalogue event types through fifteen routes,
including TaskChanged User and ProjectChanged Workspace. Conversation read revocation
covers five Messaging event types. Every negative has initial authorized
delivery, an actual live peer delivery and restored authorized delivery.
Announcement recipient delivery and metadata-only authorization invalidation
retain their separate existing semantics.

The original revoked-session and expired-session verifiers exercise all fourteen
protected event types through sixteen routes. A real fresh login issues a new
session and restores every legitimate route while the invalidated original
connection remains excluded. The existing Tenant-cookie switch verifier covers
all fifteen event types through seventeen routes, preserving connection Tenant
pinning; it separately rechecks original-Tenant membership suspension and restores
all fourteen protected types without suppressing legitimate new-Tenant delivery.

The same event loop requeues all fifteen catalogue types and both additional
publisher routes. Protected replay rechecks current Tenant/Workspace/Project/
Conversation authority, and restored authority receives the original event.
Metadata invalidation replay retains its separate exact-recipient rule. Replay
asserts unchanged durable identity, payload and routing. This direct repository
fixture mutation qualifies dispatch reauthorization only; the separate manual
operator replay service and its capability issuance/authorization remain distinct.

Private assertion receipts bind the verifier source and loaded assembly hashes
and record only event type, subscription category, control and observed delivery
outcomes. They still require independent passed TRX and exact candidate/build
reconciliation. Catalogue envelopes are synthetic and do not qualify every
business producer, payload, role, capability, replay or frontend catch-up adapter.
The product Outbox rejects Tenant routing; SubscribeTenant invocation alone does
not establish a business event delivery contract. Its applicability and approved
canonical SPEC relationships remain UNVERIFIED. These controls do not qualify
product RLS authority, an operational deployment or complete #690/#1150.

`scripts/ci/sec_arch_signalr_accounting.py` reconciles explicit assertion
receipts with the current composed inventory, verifier source, all six producer
assemblies and their loaded copies, and actual TRX method intervals. Historical
five-assembly receipts retain scoped evidence with full dependency qualification
UNVERIFIED. Live positive assertions check the received envelope type and schema,
and replay cannot change that metadata. The adapter reports the reviewed event/route/
control triples and every missing assertion separately. Duplicate identities,
changed inventories/builds/sources, missing positive delivery, unexpected delivery,
unsafe fields and self-declared approval fail validation. Its deterministic
controls run through the existing specification checks. Complete assertion
accounting still leaves approved SPEC mappings, full producer/capability coverage,
manual replay integration and product RLS pending. Existing helpers and facts
also record actual results for all eight active Hub methods, including typed
resource denial and idempotent unsubscribe results. Method-name inventory alone
does not receive invocation credit; repeated calls cannot multiply coverage.

The five existing Origin, role, reconnect/catch-up and representative replay
facts also emit explicit receipts without adding duplicate scenarios. Origin
negotiation denials require a successful authenticated negotiation on the same
operation and a previously received live frame. Upgrade denials retain the
actual collected HTTP result. Only safe category/status fields are recorded;
Origins, connection tokens and frames stay out of receipts.

The read-only role change retains legitimate delivery while an actual Message
POST returns its existing permission error, preserves Message/Outbox counts and
adds a new denial audit. Reconnect catch-up proves the persisted missed Message
is returned before a committed read revocation yields the existing hidden-resource
error and excludes later delivery. The two existing HTTP operations receive
separate source/build/TRX-bound HTTP receipts. MessageCreated is the specifically
observed business producer; other producers, full role/capability matrices,
browser catch-up and approved SPEC relationships remain UNVERIFIED.

The separate actual HTTP messaging-producer control exercises message creation,
edit, deletion, thread reply and read cursor advancement through the unchanged
Web entry point. It binds each observed frame to the newly persisted event's
actual type, aggregate and schema. Committed participant read/post revocation
must reject all four modifying operations with their existing response bodies,
an unchanged message/read/member state digest, no additional Outbox event and
one matching denial audit with its decision and reason. Authorized owner events
still reach a live socket while excluding the revoked participant; restored
participant operations must produce fresh legitimate frames. Read-state delivery
retains its recipient-specific semantics, and its denied operation must produce
no event. These are named messaging flows, not the complete business-producer,
payload, role or Capability Grant matrix. Other active producers, the declared
workflow event's inactive-publisher disposition and canonical owner approval
remain UNVERIFIED; no generic grant or product RLS authority is inferred.

## Replay lock-wait and tracked-authority regression

Replay authorization is evaluated again after the actual event-row lock.
Read-only persisted snapshots are separate from the tracked repository reads
used to edit entities and update session LastSeen. PostgreSQL controls observe
the blocked replay in pg_stat_activity before committing grant/session expiry
or revocation, membership/Tenant/user suspension or a platform-role downgrade.
All eight changes deny without event/audit effects, then restore a passing
replay with its required audit. A separate Workspace-scoped project-create
capability control rejects a committed status change despite an earlier tracked
Workspace and restores the positive decision afterward.

The original pre-fix grant-revocation case reproduced an unauthorized replay
after the lock wait and is retained as failed development evidence. These
controls exercise the current default PostgreSQL transaction path; they do
not qualify every ambient isolation, writer ordering, operator issuance path
or complete event catalogue. Mapping/policy/activation approval and the full
pre-Avalonia/#842/#614 gates remain open.

## Exact-candidate private operation receipts

The existing test-owned runtime launcher accepts an optional absolute
`--private-inventory-directory` for detailed inspection receipts. It resolves
filesystem aliases, rejects the public checkout and other Git checkouts, and
creates a fresh directory exclusively outside Git. Default execution still
removes inherited private-output configuration. The launcher supplies its
independently checked clean candidate SHA to the tests, owns its pinned isolated
PostgreSQL container/network, and verifies cleanup and assembly/result hashes.

```text
node scripts/security/run-sec-arch-runtime.mjs artifacts/sec-arch/runtime.json --candidate-sha <clean-checkout-SHA> --private-inventory-directory <new-absolute-directory-outside-Git>
```

Keep the detailed receipts private. The generic runtime summary remains an
observed catalogue result with pre-Avalonia BLOCKED; a passing fact does not
convert unresolved table/operation cells into approved RLS coverage. The
operation reconciler separately pins independent inventory, assembly, candidate
and environment inputs. No product connection string, identity role, policy or
activation is selected by this option.

The operation reconciler requires each cell's concrete `databaseRole` to match
the distinct role observed for its `roleKind`. A role catalogue alone cannot
bind the connection that executed a row operation. Historical receipts missing
the cell identity remain unqualified rather than acquiring new evidence credit.

An independent source-reference fixture starts another freshly migrated
database and captures all native trigger/function and constraint identities
before installing any prototype policies. Native definitions remain in memory;
only names, properties and hashes are retained privately. It records source-file
byte hashes and draft direct-operation dispositions separately from the operation
receipt. Deliberately disabling a native trigger, weakening its function under
the same name, and removing a referencing constraint invalidate the retained
identities; restoration must reproduce their original hashes. Existing native
`NOT VALID` constraints retain that observed state.

Source reconciliation requires these three additional arguments together:

```text
python3 -B scripts/ci/sec_arch_rls_matrix.py <existing-independent-input-arguments> --source-reference <private-fresh-migration-reference.json> --source-reference-digest <independently-observed-file-SHA256> --source-checkout <clean-exact-candidate-checkout>
```

The source reference binds the candidate, test assembly, fixture environment,
full independent table inventory and current source-file bytes. Every retained
schema hash is recomputed from its native objects. A matching function name
alone does not bind a guard, and a trigger/constraint rejection must identify its
actual native object. Source-blocked direct operations and their dependent
negatives remain UNVERIFIED with the same applicable-cell count. Optional absent
source inputs leave source binding UNVERIFIED. These mechanical controls do not
authenticate owner approval, approve operation applicability, qualify deployed
roles or activate product RLS; pre-Avalonia remains BLOCKED.

## Selected composed-host context experiment

The backend test assembly contains an explicitly opt-in hosting startup for an
isolated selected-action experiment. Its separate process executes the current
Web entry point, actual password login, persisted cookie/session validation,
controllers and persistence adapters. No product authentication or worker
service is replaced. The hosting startup registers nothing unless its explicit
probe option is enabled, and rejects that option outside the Test environment.

Only the selected actions own a test transaction and obtain transaction-local
context after current authentication and membership validation. Controls observe
the actual database role and authenticated subject/session, positive EF reads,
negative reads, raw SQL commit/rollback and current session revocation. The
fixture also records pre-authentication discovery and membership compatibility
gaps; those observations are separate from passing post-authentication actions.
Detailed outputs remain private and require a new receipt directory.

```text
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj --filter FullyQualifiedName~SecurityArchitectureRlsComposedHostTests --logger trx
```

This is a test-owned selective prototype. It does not establish full-table
startup, bootstrap, authentication, export or worker compatibility, approve a
combined operational identity, or wrap adapters that already own transactions.
Mutable context remains selectable by a role executing arbitrary SQL. Concrete
policy/role authority and exact product activation remain owner-held; this
experiment does not activate product RLS or create normative requirements.

The selected update control also invokes the actual EF unit of work and audit
adapter. A successful aggregate/audit save precedes both a deliberate exception
and a live RLS rejection of the still-granted audit INSERT. Independent reads
verify metadata and staged audit effects roll back together. The receipt retains
the native error diagnostics and identifies the bounded RLS cause; an arbitrary
permission error is not counted as a policy rejection. Current Workspace
membership downgrade and restoration are exercised with the same real cookie.

The audit observed here is the audit actually staged by the current update
implementation. Its staging-failure behavior is not promoted to an independently
mandatory audit contract. Same-Tenant resource authority, full-table startup and
operational role/policy approval remain separate holds.

## Application replay to real transport

An isolated PostgreSQL control connects the actual manual replay application
service to the product Outbox dispatcher and real authenticated SignalR
WebSockets. It proves initial delivery, replay delivery of the original
identity/payload/routing, committed capability revocation without event or audit
effects, live positive sentinel delivery during denial, and restored authorized
replay. Existing repository replay and lock-wait controls remain separate.

The replay actor and durable envelope are supplied by the test; recipient
password/cookie/session and Hub authorization execute through the current Web
entry point. This control does not establish an authenticated HTTP replay
endpoint, operational CLI authority, operator issuance or every business event
producer. Private receipts retain only bounded counters and identity/state
digests, require a fresh output directory, and remain Draft with pre-Avalonia
BLOCKED. Product RLS and infrastructure activation remain unchanged.
