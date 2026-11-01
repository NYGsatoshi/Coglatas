# Synthetic logical recovery mechanism

This original harness exercises the existing logical backup strategy with the
already-Main `SecurityCiFixtureSeed` contract. It is Test/SaaS evidence only.
It does not qualify G09, production recovery, application/migration roles, RLS,
HTTPS, a school target, or pilot release. Held PR #1379 is not reused.

The initial source baseline is Main
`49962886a875d30dd16c822e4b25b1dccd205ae6`. Its known strict-schema failure
remains a failure. Execution must wait for the independent schema repair to
merge normally and the resulting Main to qualify; this branch must then merge
that Main normally. No comparison, constraint, trigger or migration-history
condition may be weakened to obtain a successful receipt.

The current production OnPrem startup path invokes default tenant/plan seeding
even with `Tenancy:SeedOnStartup=false`. This harness deliberately uses the
existing Test/SaaS fixture and cannot establish OnPrem startup preservation.
Changing that production behavior or accepting a different recovery strategy
requires its own review and, where applicable, Owner decision.

## Invocation and authority

The only actual entry point is manual `suite=logical-recovery` in the existing
`licensed-real-backend-acceptance.yml`, using its existing
`syncfusion-licensed-build` protected environment. It is absent from `all`, push
selection, workflow-call selection, and the eight required P0 journeys.
The script additionally admits only Linux GitHub Actions, the exact repository
ID `1261244608`, Owner actor `NYGsatoshi`, that workflow identity, and run
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
   --no-owner --no-privileges --exit-on-error`. No target migration/bootstrap
   service exists. Strictly compare the stopped target before startup. Start
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

## Evidence and privacy

Raw dumps, SQL/catalog snapshots, file/key archives, credentials and cookies
remain under an owner-only `RUNNER_TEMP` directory, outside every upload glob.
The helper never prints private response bodies or command stderr. Upload is
restricted to `artifacts/synthetic-logical-recovery/*/receipt.json`, retained
seven days. Receipts contain closed verdicts, hashes and counts only, and keep
`technicalReleaseReady=false`, `studentCouncilPilot=RELEASE_NO_GO`, and roles,
RLS and production HTTPS `NOT_EVALUATED` even when the mechanism passes.
Provider job-ID reconciliation and trusted final attestation remain explicitly
unverified until the reviewer binds the receipt to exact provider metadata.

Local Node guard tests and Compose configuration checks prove only their stated
contracts. A configured workflow or passing guards do not establish application
startup, PostgreSQL restore, actual cookies, Data Protection continuity or G09.
Keep first-attempt failures and unexecuted checks visible in the release ledger.

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
