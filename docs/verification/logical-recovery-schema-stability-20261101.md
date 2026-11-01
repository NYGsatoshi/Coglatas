# Logical recovery expression stability — #1355

This is a bounded schema repair within the existing logical backup strategy.
It does not qualify full Recovery, a target, rollback operations, or release.
Held #1379 is unchanged and contributes no implementation to this change.

## Reproduced cause

Clean Product Main `49962886a875d30dd16c822e4b25b1dccd205ae6`, tree
`0c1afe977e2d134bffeb92c2f6ccb623bf0f4d90`, generated all 74 EF migrations.
The generated SQL SHA256 is
`c618c1cd6d46c8a4462f5462b8f6d8622c00b135f6066c1baa09d21b29c25c45`.
It initialized a disposable PostgreSQL source, with an independently empty target.
No application or real-user data was used.

Actual PostgreSQL `180006` ran from local immutable image
`sha256:77f585114c32fbca283dc835b0596f4e52b51b4c6662d7810b2f4084f60a1873`.
The schema roundtrip cluster had no network and used temporary in-memory storage.
One `pg_dump --format=custom --no-owner --no-privileges` and one
`pg_restore --no-owner --no-privileges --exit-on-error` both completed.
These are the database commands in `deploy/sakura/student-council-pilot.md`;
EF migrations constructed the source only. The restore target was not migrated
or seeded before the full restore. The separate original-DDL/data-only strategy
and its FK-order failure are not substituted or repaired here.

Strict schema-only dump bytes differed at exactly 14 CHECK definitions and one
partial unique index. PostgreSQL initially resolves a varchar `IN` list into a
varchar array coerced as a whole to `text[]`. Its deparsed DDL reparses into
individually coerced array elements. This changes retained schema expression
bytes without demonstrating a changed allowed-value set. A separate minimal
PostgreSQL control reproduced both forms and retained invalid-value rejection.

| Table | CHECK constraint suffixes (prefixed `CK_`) |
| --- | --- |
| projects | projects_activation_provenance, projects_activation_state, projects_visibility |
| security_evaluation_runs | security_evaluation_runs_context, security_evaluation_runs_lifecycle, security_evaluation_runs_mode, security_evaluation_runs_reason |
| security_evaluation_rule_results | security_evaluation_rule_results_execution, security_evaluation_rule_results_reason |
| task_deadline_digest_jobs | task_deadline_digest_jobs_completion |
| task_deadline_digest_attempts | task_deadline_digest_attempts_completion |
| task_execution_materialized_sources | task_execution_materialized_sources_media_type |
| task_execution_runs | task_execution_runs_status |
| task_workflow_template_stages | task_workflow_template_stages_category |

The index is `IX_task_deadline_digest_attempts_one_active`.
Baseline source/target schema SHA256 values are respectively
`37b6b4950f7d6afa703b111e7420ecb2692e1e93dfa593b892a7f75c992fb60c` and
`9b472efd3a78fddf03dd5b495fb9aba8718ff8a9a007f10841b54651c29cbdd4`.
The failure is retained; later candidate success does not change it.

## Repair and controls

The forward migration explicitly selects `text` on the column before each
affected `IN` expression. Comparisons already used PostgreSQL text equality;
allowed literals, null logic, columns, keys, triggers and role/RLS settings are
unchanged. Eleven modeled CHECKs and the modeled index have matching scoped EF
annotations. Three SQL-migration-owned CHECKs remain owned by migrations.
Earlier migrations and the comparison oracle are untouched.

The migration replaces the named objects inside the normal EF migration
transaction. Applying it requires the deployment's approved maintenance process
and table locks. Its Down migration reinstates the original definitions; doing
so also reinstates the known strict roundtrip failure. No deployment is authorized.

The original PostgreSQL regression first requires the exact 15 baseline
deparse/reparse mismatches, then requires none after migration. It reparses
actual catalog definitions into disposable typed tables and a unique index.
An independent test compares actual before/after CHECK and predicate evaluation
for typed allowed, unknown, case/space, null and numeric/timestamp boundary
inputs, then verifies original definitions and results after Down and stable
definitions after reapplying Up. These are expression controls; existing
provider tests separately exercise application writes and authorization.

The candidate's first full logical restore has 75 migration-history rows on
source and target. Strict schema dump SHA256 is identical on both sides:
`023887b5320aae99756650bad0f93601d9de2bfc6c290f19c378d1ca1745e09a`.
The two schema dumps use the same explicit alphanumeric `pg_dump --restrict-key`
so the client-generated command guard is comparable; no output is rewritten,
normalized, filtered or replaced. Catalog definition/constraint validation,
index readiness/validity, user-trigger state and relation ACL/RLS flags also
compare byte-identically (SHA256
`062263be89b01cca7ee23a1eeb130d12cb7bd4df046670d709fd66d5e6ea239f`).
Ordered migration rows compare byte-identically (SHA256
`d06d95de0f964df840b60ccb52a3eafc446595f4a12792eef7742b56dcaa49be`).
Both databases retain 114 tables, 77 CHECKs, 232 FKs, 22 user triggers and zero
sequences, with zero invalid/unready indexes. The source SQL and custom archive
SHA256 values are respectively
`39e24d6ba8eb55b5f45bb682748c378f1ed38f7764c715c2f0b2ff79cbd3717a` and
`69fc312110a5c6f924121408ebcadfc79d40daee30a6ce14a568e790bc298932`.

## Limits and remaining evidence

This source-only fixture contains migration history, not a complete application
checkpoint. Files, Data Protection keys, login, Audit continuity, full row data,
failure injection, target isolation/rollback and approved HTTPS still require
their own qualification. The bootstrap PostgreSQL role is not an application-role
RLS or restored production role/grant qualification. No logical restore into
original volumes or production resources occurred.

An additional diagnostic of PostgreSQL internal serialized parse trees found
35 baseline CHECK-node differences and 12 expression/partial-index-node
differences, including casts not visible as changed deparsed schema text.
That is a different diagnostic from schema/catalog definition comparison;
neither full internal-node equality nor a revised formal oracle is claimed.
Do not adopt a weaker oracle or infer full G09 PASS from this repair.

Local receipts live under ignored `artifacts/recovery-audit/`. Initial setup
errors (missing restored assets/design-time connection and an invalid diagnostic
restrict key) were corrected without rerunning or relabeling the original
schema-failure result. Hosted current-head CI, independent review, official
Sonar and resulting-Main qualification remain required before normal merge.
Issue #1355 stays open and Student Council release remains `RELEASE_NO_GO`.
