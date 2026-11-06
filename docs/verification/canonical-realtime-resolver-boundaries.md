# Canonical Realtime resolver boundaries

Two PostgreSQL regressions extend the existing canonical completion suite without
changing production authorization, its fixtures, or any existing assertion.

The envelope matrix checks authorized User, Project and Workspace delivery
targets, all four accepted Task event types, and rejects mismatched event types,
Tenant and aggregate identities, missing or malformed JSON references, unrelated
subscription resources, unsupported subscription types and unknown recipients.
Authorized controls before and after the matrix ensure rejection is not caused
by an unavailable fixture.

The current-state control keeps one resolver and a tracked active Workspace
membership alive. A separate database context commits suspension and restoration.
Project and Task event decisions must change from allow to deny to allow while
the original tracked entity remains active. Both event families use the same
envelopes and User, Project and Workspace targets throughout.

These are actual migrated PostgreSQL query-contract tests when the required test
connection is configured. Discovery skips without PostgreSQL locally; CI fails
when the connection is absent. Record the actual source SHA, provider execution,
test counts, collector bytes and limitations in the PR and Issue #615. A skipped
or unexecuted run is not evidence of current-state behavior.

Direct committed membership updates model persisted authority changes; they do
not exercise an HTTP revocation command, session admission, the Outbox transport,
concurrent authorization through dispatch, application-role RLS, production
migrations, physical devices or pilot acceptance. Existing archived-Workspace
historical read behavior remains governed by its separate original controls.
No formal finding disposition, Owner decision or Release GO follows from these
tests. Resulting Main still needs its own CI, both full Qodana scans, official
Sonar and source-bound coverage qualification.
