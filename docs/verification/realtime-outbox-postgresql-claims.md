# PostgreSQL Outbox claim boundaries

This test-only work under #615 adds real PostgreSQL repository controls for
separately locked claims, stale claim rejection, Tenant-filtered updates,
persisted delivery/release, bounded failure diagnostics, and expired-lock
recovery. Each scenario uses an isolated migrated synthetic database and fresh
observer contexts. The lock scenario holds a different connection's transaction
while the actual claim query executes `FOR UPDATE SKIP LOCKED`.

Production source, old assertions, authorization contracts and coverage policy
remain unchanged. These controls do not establish application-role RLS,
concurrent acknowledgement linearizability, transport delivery, exactly-once
semantics, host-loop recovery, Artifact orphan reconciliation or production
recovery acceptance. No real users, school data or production database is used.

The native PostgreSQL run and original collector bytes must be bound to their
exact candidate source. Required CI, independent review, original PR Gate and
complete resulting-Main qualification remain necessary. Local point increments
are not summed into official Main coverage.
