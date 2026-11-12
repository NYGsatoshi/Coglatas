# Historical pre-repair migration round trip

`PreRepairMigrationRoundTripPostgreSqlTests` exercises the migration prefix before
`20260728010000_CreateMissingTenantSettingsTable` on a fresh disposable PostgreSQL
database. It applies that exact discovered prefix, rewinds it to the initial
database, and reapplies the same prefix. Both intentionally retaining July 28
repairs and every later migration remain pending throughout this scenario.

The test requires Npgsql and the existing nonparallel PostgreSQL collection. The
fresh-database helper bypasses the migrated-template optimization. It checks
applied and pending migration identities at every boundary, requires only
`__EFMigrationsHistory` after the rewind, and compares selected logical public
table, column, constraint and index definitions after reapplication. The original
historical schema must lack the omitted repair tables before rollback.

This is a separate historical empty-schema scenario. The original full-to-zero
experiment at `b857e6c4bf5cdf9f13a5db72c15184273b9e243b` failed with `2BP01`
at `file_objects` after the intentionally preserving repair tables had been
applied. Its original assertion and failed execution remain retained. This test
does not change any migration, dependency guard, existing assertion or retention
contract, and does not qualify a full rewind of the latest schema.

No physical column ordinals, populated data, object bytes, complete PostgreSQL
catalog, failure atomicity, backup/restore, application-role RLS, deployment or
Pilot acceptance is established. The separate post-repair logical-catalog test
and its recorded physical-ordinal mismatch retain their original scope.

Native execution, exact-source artifacts, hosted checks, original PR Gate and
independent review must be recorded separately. A configured or skipped test is
not runtime evidence; a local pass is not a measured Main coverage gain.
