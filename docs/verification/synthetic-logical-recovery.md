# Synthetic logical recovery mechanism

This original harness exercises the existing logical backup strategy with the
already-Main `SecurityCiFixtureSeed` contract. It is Test/SaaS evidence only.
It does not qualify G09, production recovery, application/migration roles, RLS,
HTTPS, a school target, or pilot release. Held PR #1379 is not reused.

The candidate normally adopts Main `1542923a203191dbe7f37032ef655728ead54533`,
including the script-free Sonar npm lockfile repair in #1651. This synchronization
preserves the recovery commands, strict comparison oracles and original failures.
The following earlier Main results qualify historical dependencies only; they
are not transferred to a new candidate or Main source.

The initial source baseline is Main
`49962886a875d30dd16c822e4b25b1dccd205ae6`. Its known strict-schema failure
remains a failure. A historical revision adopted Main
`f987379b7601df1a8c7bb810f191924dd2208fa2`, including normal schema repair
#1641 and startup repair #1646. Its fresh Main CI `41384212616/1` completed
41 successful jobs and two existing policy skips, with the three strict schema
and seven startup regressions passing. This qualifies the technical dependency
for an isolated drill; official Main Sonar analysis
`003d7a54-6cfb-4bf5-856d-bf90ff37b02f` still fails 74.2% new coverage and
Security E. Release readiness and Owner approval are not established. No
comparison, constraint, trigger or migration-history condition may be weakened.

The historical OnPrem path invoked default tenant/plan seeding even with
`Tenancy:SeedOnStartup=false`; #1646 now honors the explicit controls and retains
the reviewed Development/OnPrem LocalAdmin compatibility path. This mechanism
still uses the existing Test/SaaS fixture and does not establish full OnPrem
recovery, an actual-target operation or a different accepted recovery strategy.

## Invocation and authority

The only actual entry point is manual `suite=logical-recovery` in the existing
`licensed-real-backend-acceptance.yml`, using its existing
`syncfusion-licensed-build` protected environment. It is absent from `all`, push
selection, workflow-call selection, and the eight required P0 journeys.
The script additionally admits only Linux GitHub Actions, the exact repository
ID `1261244608`, its fixed repository-owner actor, that workflow identity, and run
attempt 1. It accepts no CLI overrides. Adding a case does not approve a
dispatch, target operation, production recovery, or release.

The fixed `/usr/bin/docker` and `/usr/bin/git` must be root-owned regular files
with protected parent directories. Docker uses only the explicit local Unix
socket and a fresh private configuration. Remote contexts, hosts and TLS client
credentials are rejected before mutation. No host port is published. Runtime
networks are internal bridges; helper containers use `none` or the owned app's
network namespace. Build and dependency acquisition precede runtime isolation
and use the repository's existing pinned SDK/image contracts.

## Measured mechanism

1. Verify the clean exact Git source/tree and absence of all nonce-named
   resources. Build one application image; prepare one EF migration bundle.
   The runner-UID SDK uses an explicitly owned private HOME and tool cache.
   Fixed preparation substeps and a numeric failure exit are retained without
   exposing command output. The original first build failure remains UNKNOWN;
   a separate pinned-SDK comparison reproduces the missing-owned-HOME defect.
   Record immutable application/PostgreSQL image IDs, actual PostgreSQL version,
   source SHA/tree, run/attempt and seed flags.
2. Apply migrations only to a fresh synthetic source, seed the existing two
   tenants, then recreate the source app with all fixture/bootstrap seed flags
   off. Establish synthetic login, saved cookies, logout revocation, actual
   file byte/hash checks and cross-tenant denials.
3. Require a bounded Outbox-idle observation, stop the actual application and
   reject remaining database client connections. Take one complete custom
   `pg_dump`, plus bounded upload/key archives. Preserve every source table,
   ordered full-row digest, exact schema-dump bytes, migration row, catalog
   contract, sequence state, file byte hash/owner/mode and Data Protection key.
   There is no data normalization or row/table exclusion.
4. Restore once to disjoint fresh target volumes with `pg_restore
   --no-owner --no-privileges --exit-on-error --use-list=/tmp/coglatas-recovery.list`.
   The complete list preserves source FK creation order as described below. No target migration/bootstrap
   service exists. Upload/key extraction explicitly forwards archive stdin with
   Docker `--interactive`, preserving bytes, owners and modes. Fixed restore
   substeps distinguish PostgreSQL, archive extraction and strict comparison.
   Strictly compare the stopped target before startup. Start
   and stop it, compare again before API probes, and reject any startup delta.
5. Check restored saved-cookie identity, fresh login/logout, revoked-cookie
   denial, original file bytes and cross-tenant denials. Preserve all original
   Audit rows exactly and require subsequent Audit growth. These expected API
   writes occur after, and separately from, the strict startup checkpoint.
6. Truncate a distinct in-memory copy of the backup and require one failed
   restore into a third quarantined fresh database. Recheck the untouched
   stopped source and original backup hash, then restart the original same-image
   source container and repeat the login/file/isolation checks as rollback.
7. Delete only projects first observed absent and owned by this nonce; verify
   that their containers, volumes and networks are absent. Cleanup failure is
   an execution failure. A failed target never replaces the original source.

There is no retry of restore, failed assertions, probes or the workflow attempt.
Health/Outbox polling only observes bounded readiness and never mutates evidence.
Schema/catalog checks complement each other; SQL catalog rendering is not a
claim of physical OID or `pg_node_tree` identity. File metadata compares modes,
owners, paths, kinds and bytes; timestamps and physical disk identity are not
qualified.

### Generated internal RI trigger rendering

The complete product migration schema exposed a separate restore-order defect:
the original custom archive restored FKs in name order, changing effective
co-firing sequences despite equal raw schema bytes and all 950 logical trigger
records. The original failing comparison remains evidence. Replaying that same
frozen archive with only a complete FK-slot TOC permutation preserved all 950
records and their actual co-firing order in an isolated PostgreSQL 18.6 control.

Before restore, one native `pg_restore --list` inventories the original archive.
The bounded strict-UTF8 planner preserves every line, entry, comment and non-FK
position, moving only whole FK-entry lines among their original FK slots by
ascending original constraint OID. IDs and FK OIDs must be unique and bounded;
independent plan verification rejects omissions, substitutions and other moves.
The native header's total includes implicit entries and is not asserted equal
to the selected line count. Original/planned lists stay private. A fixed stdin
command writes the same list with mode 0600, without clobbering an existing
path, into each owned PostgreSQL target/fault container and verifies its hash.
Both retain one `pg_restore` attempt, the original archive/corrupt copy and
unchanged error/strict-snapshot assertions. No filter, parallel restore,
constraint disabling, schema rewrite or additional restore is introduced.

Original OIDs only guide replay order; they are not snapshot identities or a
universal ordering guarantee. OID wrap, decimal-name-width changes, unusual
dependencies or user/internal interleaving can make the target order differ.
Such cases still fail the complete original firing-order assertion; no fallback
or normalization accepts them. The native regression includes FKs created on
different child tables in an order opposite to their archive name order.
This is a causal repair within the same logical backup strategy, not approval
of a new strategy, actual target, role/RLS change or G09 qualification.

The original catalog query retains its fields and its complete scalar output is
retained privately as `<checkpoint>.catalog.raw.json`. A separate PostgreSQL 18
inventory is retained as `<checkpoint>.triggers.raw.json`. Non-trigger catalog
fields, strict raw schema bytes, complete row contents, sequences, files, keys,
restore counts and original assertion calls remain unchanged. The inventory
predicate correction below additionally includes previously omitted user names.

The system-namespace/role predicates use the literal reserved `pg_` prefix.
An actual PostgreSQL counterexample showed the former `NOT LIKE 'pg_%'`
predicate also excluded legitimate user names such as `pgx`, allowing a changed
restored row to produce an identical manifest. Only those predicates changed to
`!~ '^pg_'` consistently across rows/catalog/sequences/roles and the trigger
inventory. This strengthens complete user inventory; no row/table exclusion or
data normalization is added. Native controls retain the original red result,
include a `pgx` table/sequence/trigger and nonreserved `pgfixture` role, reject
its restored-row mutation, and still exclude system/temp namespaces.

Only a known built-in `pg_catalog` internal RI function, linked to the matching
foreign key and relation/index identities, with the server-generated name
verified against that trigger's own OID, receives a structured logical identity.
Unexpected internal names/functions and every user-trigger name/definition stay
exact. No arbitrary suffix removal, internal-row filtering or data/schema
normalization occurs. Full inventory counts and original raw rows are reconciled
before rendering. Parent trigger references and column numbers resolve to
logical trigger/column names; missing links, cycles and duplicate identities fail.

The representation preserves event/type, enabled/replication mode,
deferrability, arguments, columns, WHEN-bearing definitions, transition aliases,
function definition/signature/security attributes, constraint/index/relation
identities and partition-parent relationships. The original PostgreSQL name
order separately determines effective co-firing sequences per table, timing,
row/statement level, individual event and origin/replica mode. Multi-event user
triggers participate in every applicable sequence. A changed RI/RI or RI/user
order remains a failure even when the unordered logical records match.

Function cost/row estimates use PostgreSQL's decimal text, preserving fractional
metadata without passing floating-point JSON numbers to the unchanged strict
safe-integer guard. Nonfinite or malformed estimates fail closed. The native
positive invokes the actual original snapshot assertion on the complete
database snapshot; fractional-cost mutation must fail that same assertion.
Empty file/key manifests in this database-only fixture do not qualify files or
Data Protection restoration.

This repairs a rendering that unintentionally depended on physical OIDs,
consistent with the documented assurance above; it does not waive a meaningful
catalog difference or approve a new backup strategy, actual target, role/RLS
change or release. Historical first-attempt failures remain unchanged.

The durable unit controls run in the existing preflight and canonical native
Node-coverage cohort. That same canonical Node producer prepares the identical
immutable PostgreSQL repository digest before analysis credentials and admits
the original native PostgreSQL test with step-local `COGLATAS_RI_NATIVE_TEST=1`.
Its actual native V8 ranges enter the original LCOV report and source verifier;
no coverage exclusion, threshold, report counter or source scope changes.
Raw database/schema/catalog/list diagnostics stay outside the narrow LCOV and
raw-V8 upload paths. This does not qualify the protected application Full drill.
The native test remains default-off in other contexts and can run explicitly:

```bash
COGLATAS_RI_NATIVE_TEST=1 node --test tests/ci/synthetic-trigger-catalog.postgres-test.mjs
```

It admits only an explicit local Docker endpoint and cached immutable PostgreSQL
18.6 image, creates a fresh network-none/tmpfs cluster, performs one original
custom dump/restore, retains raw schema/catalog/trigger evidence privately, and
checks semantic/order mutations before removing and verifying only its owned
container. Windows additionally requires `COGLATAS_RI_DOCKER` to name the trusted
absolute Docker executable. No configured or local control qualifies the full
application recovery path; exact-source hosted Full/CI/Sonar and independent
review remain separate requirements.

Primary contracts: [PostgreSQL 18 trigger catalog](https://www.postgresql.org/docs/18/catalog-pg-trigger.html)
[PostgreSQL 18 event and alphabetical firing rules](https://www.postgresql.org/docs/18/sql-createtrigger.html),
[PostgreSQL 18 complete-list restore ordering](https://www.postgresql.org/docs/18/app-pgrestore.html),
and [PostgreSQL 18 generated trigger naming implementation](https://github.com/postgres/postgres/blob/REL_18_STABLE/src/backend/commands/trigger.c).

## Evidence and privacy

Raw dumps, SQL/catalog snapshots, file/key archives, credentials and cookies
remain under an owner-only `RUNNER_TEMP` directory, outside the receipt upload.
The helper never prints private response bodies or command stderr. Selecting
`logical-recovery` always reaches the original hosted admission contract before
dependency, artifact-reuse or Docker setup. Unsupported events, non-Owner
dispatches, later attempts and Main-artifact reuse fail instead of skipping the
mechanism and leaving a green job.

The existing nonce-owned writer creates one exclusive current-run receipt and
only then emits that exact relative path. A separate validation step admits
only the emitted current run/attempt/SHA/nonce and admitted source tree, an
owned regular file with stable descriptor and bounded canonical UTF-8 JSON,
and the closed metadata schema. It never searches a glob. A genuine current
`ERROR` receipt remains uploadable when the mechanism fails, while the job
retains its failed result. `admittedSourceTree` records the admission snapshot;
`testedCombinedTree` remains null on failures before the original source check,
so early errors do not claim an executed checkpoint. Upload uses only the
validated exact path, retained seven days. Receipts keep
`technicalReleaseReady=false`, `studentCouncilPilot=RELEASE_NO_GO`, and roles,
RLS and production HTTPS `NOT_EVALUATED` even when the mechanism passes.
Provider job-ID reconciliation and trusted final attestation remain explicitly
unverified until the reviewer binds the receipt to exact provider metadata.

The previous `3e6453bf` Sonar input included the executable native PostgreSQL
test body: 290 lines and 66 conditions, with 335 of 356 measurement points
covered in the official PR analysis. Its old gate must not be described as a
clean operational-code measurement. The native producer now excludes only
that exact test body and the independently test-classified ZAP CJS test body;
both tests still execute and their raw V8 evidence remains retained. The exact
PostgreSQL test body also joins the existing coverage-only test-body policy.
Operational catalog/recovery/ZAP helpers remain measured, issue analysis stays
enabled, and no Quality Gate threshold changes. A fresh official measurement
is required; no resulting coverage gain is predicted.

Local Node guard tests and Compose configuration checks prove only their stated
contracts. A configured workflow or passing guards do not establish application
startup, PostgreSQL restore, actual cookies, Data Protection continuity or G09.
Keep first-attempt failures and unexecuted checks visible in the release ledger.

The actual owned-project cleanup loop is shared with focused fail-closed Node
controls. They exercise foreign labels/services, source-only helper deletion,
residual compute/storage/network rejection, and unchanged first-error propagation.
The API probe's same fixed-loopback implementation additionally runs against a
stateful synthetic fetch fixture. Saved-cookie identity, CSRF, file bytes,
cross-tenant denials and server-side logout revocation have negative controls;
these are probe-oracle tests, not evidence that a Product backend passed.

A retained counterexample returned HTTP 401 containing the revoked actor's
known identity while the old probe published PASS. Denied `me` checks now reject
the three validated private fixture actors' known user IDs or emails in raw,
case-varied or JSON-escaped response text, including each logout confirmation,
while preserving safe empty/non-JSON errors and the exact HTTP-status and
successful-identity assertions. No response body is logged. The unit I/O seam
changes neither CLI admission, private paths/creation modes nor the fixed target.

The initial local preparation ran 15 new guards successfully on host Node 24
and the pinned Linux Node container, with no skipped tests. The existing
preflight cohort plus the initial 14 guards passed 105 tests; the added source
composition control subsequently raised that cohort to 106. Both resolved
Compose configurations passed guards. The first source configuration check
rejected the omitted, profiled-out browser service; the contract now requires
exactly the three selected source services. A Windows CRLF bind mount prevented
the existing Bash trust-boundary script from starting in Linux, and the cached
Python image lacked PyYAML. Those environment failures are retained, not counted
as validation. No application build, migration, restore or protected execution
was performed in this local preparation.

Independent review found that a denial status could still contain the foreign
file body. The corrected probe retains the counterpart owner's hash-verified
download bytes and rejects those bytes, embedded bytes and the trimmed synthetic
canary in every denied response, in addition to metadata. The original exact-head
mock reproduction reported PASS with two complete foreign bodies; that failure
is retained. The actual corrected module is exercised with `Response`/`fetch`
controls for all admitted denial statuses, genuine closed denials, and an
incorrect oracle. Removing the byte/canary rejection reproduces the failure.
The new guard count is 16; these mocks do not establish real backend isolation.

All seven actual SQL templates also executed once against a fresh network-none
PostgreSQL 18.6 container with a minimal original synthetic schema. They parsed
and returned the expected JSON/count shapes. The container was owned, removed
and its absence checked. This is SQL-template qualification only, not product
schema, migration or recovery qualification. An initial reported SQL-escaping
finding was retracted after immutable source byte inspection; no SQL text was
changed for that report.

Further independent review found two oracle gaps before protected execution.
The corrupt-copy negative control now requires native pg_restore exit 1 with
its exact truncated-input diagnostic, followed by an actual fault-database
readiness query; Docker/exec/connection failures do not qualify. A separate
network-none PostgreSQL 18.6 control generated a valid original custom dump,
verified its listing, truncated a distinct 128-byte copy, observed that native
rejection, preserved the original row/hash, and removed its owned container.
This is archive-client attribution only, not application recovery acceptance.

Sequence bigint parameters and last_value are serialized as decimal text so
adjacent values above JavaScript's safe-integer range remain distinct. Strict
snapshot comparison additionally rejects unsafe numeric input instead of
comparing already rounded values. The zero-sequence product fixture is not
claimed as populated-sequence coverage. The dedicated native sequence control
and adjacent-value/unsafe-number regressions remain separate evidence.
