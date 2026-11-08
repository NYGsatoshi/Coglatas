# Isolated performance fixture round-trip controls

These tests exercise the existing performance fixture producer with the tracked
small dataset. They retain its fixed dedicated database name and local-host
restrictions. A fresh CREATE must succeed before the test owns any cleanup;
an existing database is neither adopted nor dropped. Cleanup does not terminate
other connections. No deployment, production database or performance threshold
operation is included.

Connection-free cases reject non-PostgreSQL and invalid target combinations.
A mandatory PostgreSQL control verifies that a different isolated migrated
database retains its sentinel and migration history after rejection. The full
round trip creates a fresh dedicated test database, migrates it, verifies all
tracked small-profile cardinalities, then rebuilds the database twice with the
existing DB scenario version. It checks removal of a prior sentinel, stable
identities, distinct message cursor timestamps, Workspace attachments, real
password verification, original manifest bytes and deterministic generated
fixture evidence. Each rebuild uses committed persistence and repository reads;
this does not establish all-table RLS or external storage bytes.

These are functional fixture correctness controls. They do not approve numeric
performance, representative hardware, deployment, full recovery, physical-device
acceptance or Release GO. Existing test bodies, source scope, producer policies,
coverage reports and thresholds remain unchanged. Actual native execution,
hosted PostgreSQL and original protected checks/Gate must be qualified at the
current HEAD before integration; local coverage is not a projected Main gain.
