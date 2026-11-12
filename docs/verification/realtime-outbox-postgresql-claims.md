# PostgreSQL Outbox claim boundaries

This test-only work under #615 adds real PostgreSQL repository controls for
separately locked claims, stale claim rejection, Tenant-filtered updates,
persisted delivery/release, bounded failure diagnostics, and expired-lock
recovery. Each scenario uses an isolated migrated synthetic database and fresh
observer contexts. The lock scenario holds a different connection's transaction
while the actual claim query executes `FOR UPDATE SKIP LOCKED`.

The initial five-control run retained three passes and two failures because
the new controls compared compact input JSON to PostgreSQL's canonical `jsonb`
text. Preservation is now measured against a fresh persisted pre-transition
read and compared as exact PostgreSQL-returned JSON text after the transition,
not physical storage bytes. No production or old
test assertion was changed to accommodate that initial test-authoring mistake.
The claim control also compares the returned token, owner and lock timestamp
against a fresh persisted read and requires its next-attempt value to be cleared.

Production source, old assertions, authorization contracts and coverage policy
remain unchanged. These controls do not establish application-role RLS,
concurrent acknowledgement linearizability, transport delivery, exactly-once
semantics, host-loop recovery, Artifact orphan reconciliation or production
recovery acceptance. No real users, school data or production database is used.

The native PostgreSQL run and original collector bytes must be bound to their
exact candidate source. Required CI, independent review, original PR Gate and
complete resulting-Main qualification remain necessary. Local point increments
are not summed into official Main coverage.
