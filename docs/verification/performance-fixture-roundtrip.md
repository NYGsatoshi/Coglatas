# Isolated performance fixture round-trip controls

These tests exercise the existing performance fixture producer with the tracked
small dataset. They retain its fixed dedicated database name and local-host
restrictions. A fresh CREATE must succeed before the test owns any cleanup;
an existing database is neither adopted nor dropped. Dedicated-database cleanup
does not terminate other connections. The different-database rejection retains
the existing temporary helper's forced cleanup for its own unique database.
No deployment, production database or performance threshold
operation is included.

Connection-free cases reject non-PostgreSQL and invalid target combinations.
A mandatory PostgreSQL control verifies that a different isolated migrated
database retains its sentinel and migration history after rejection. The full
round trip creates a fresh dedicated test database, migrates it, verifies all
tracked small-profile cardinalities, then rebuilds the database twice with the
existing DB scenario version. It checks removal of a prior sentinel, stable
identities, distinct message cursor timestamps, Workspace attachments, real
password verification, original manifest bytes and deterministic generated
fixture evidence. Each rebuild uses committed persistence and fresh EF queries;
this does not establish all-table RLS or external storage bytes.

After v1 and each v2 rebuild, no-tracking reads independently select the named
small-profile Tenant, operator, Workspace, focus Project, Conversation and Task
by fixture labels rather than evidence IDs. The test checks focus Task,
milestone, dependency, assigned-Task and message cardinalities and the minimum
Kanban capacity. All three small-profile Project aliases must report that
persisted focus Project. A consistently wrong but existing reported identity
cannot select a different workload and still pass.
Identity comparisons normalize UUIDs and compare dictionaries rather than JSON
formatting. The attachment checks include Workspace ownership and persisted
FileObject links. Manifest discovery checks the consumer working directory and
artifact directory before falling back to the compile-time source path.

The rejection control snapshots all existing Tenant identities and labels before
the attempted seed, including rows created by migrations. It reloads the
sentinel by its own identity and verifies the complete snapshot afterward.
Generated fixture JSON is read as UTF-8 text so the producer's existing BOM is
accepted; original bytes remain intact for deterministic-byte and manifest
checks. The first hosted execution at `2bb6de72` failed both PostgreSQL controls
on an unqualified single-Tenant query and BOM parsing respectively. Its original
run `42460630425/1` and separate redundant-qualifier inspection failure remain
historical failures. These corrections require fresh execution at the new HEAD.

These are functional fixture correctness controls. They do not approve numeric
performance, representative hardware, deployment, full recovery, physical-device
acceptance or Release GO. Existing test bodies, source scope, producer policies,
coverage reports and thresholds remain unchanged. Actual native execution,
hosted PostgreSQL and original protected checks/Gate must be qualified at the
current HEAD before integration; local coverage is not a projected Main gain.
