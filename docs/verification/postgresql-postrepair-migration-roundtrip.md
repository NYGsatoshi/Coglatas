# PostgreSQL catalog round trip above the retained repair boundary

The selected boundary is
`20260728020000_CreateMissingTenantPlatformTables`. This preserves both historical
repair migrations that intentionally keep tables and data that earlier model
snapshots already represented. Their `Down` methods remain unchanged.

`PostRepairMigrationRoundTripPostgreSqlTests` uses a new disposable database and
requires Npgsql. It first applies the retained prefix and captures its public
tables, columns, constraints and indexes. It then applies every later migration,
captures the latest catalog, reverts to the exact retained prefix, and reapplies
the later migrations. It compares logical column definitions, tables, constraints
and indexes at both catalog boundaries and the complete
applied/pending migration identities. The migrated-template optimization is not
used by this fresh-database helper. No product schema or migration is modified.

A separate full-to-zero experiment at commit
`b857e6c4bf5cdf9f13a5db72c15184273b9e243b` failed on PostgreSQL 18.6 with
`2BP01` when dropping `file_objects` while preserved tables still depend on it.
Its test, original failure, TRX and report remain retained in the separate
experimental branch. This bounded test does not qualify that failed full rewind
or replace its assertions. No retained table, dependency or data-preservation
guard is removed to make a full reset succeed.

The initial post-repair experiment at
`3701b916828614f8c4af01a5172f280f70306a68` also compared physical column ordinals.
It failed when dropping and readding the Announcement distribution column moved
its physical position. Its original assertion, failure and binary/report/source
evidence remain retained. The logical-column test compares names, types,
nullability, defaults and generated/identity definitions independently of that
physical position. Physical ordinals remain separately collected and reported;
physical-layout identity is unqualified. No existing Main assertion is changed.

Qualification is limited to selected schema contracts on an empty disposable
database at these exact two boundaries. Populated data, storage bytes, complete
extension/function/role cleanup, transaction-failure atomicity, backup/restore,
application-role/RLS behavior and actual deployment recovery remain separate
work. Production migration/backup/restore, physical acceptance and Pilot/Release
GO retain their Owner boundaries. Native and hosted execution must be bound to
actual source, provider, executed binaries, reports and run before qualification.
