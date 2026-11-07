# Disposable PostgreSQL migration schema round trip

`MigrationSchemaRoundTripPostgreSqlTests` creates a new database through the
existing provider fixture. It requires Npgsql and checks the fixture database
name before using the destructive initial-migration target. It uses the empty
database helper directly, so the migrated-template optimization cannot replace
either actual migration application.

The test applies the complete current migration inventory, reverts it to the
initial database, and reapplies the same inventory. It checks applied/pending
history at each boundary, the remaining history table after rollback, and exact
catalog rows for public tables, columns, constraints and indexes before and after
reapplication. No migrations, product behavior, existing assertions, coverage
scope, report bytes or quality thresholds are changed by this candidate.

This is selected schema execution evidence on an empty disposable database.
It does not prove preservation of populated application data, extension/function
or role cleanup, failure atomicity, backup/restore, application-role/RLS behavior,
deployment compatibility or full recovery acceptance. Production migration,
backup/restore, physical recovery and Pilot/Release GO retain their separate
Owner approval boundaries. Native and hosted results must be bound to their
actual source, provider, executed binaries, reports and run before qualification.
