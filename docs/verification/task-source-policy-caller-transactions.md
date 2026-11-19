# Task source-policy caller transaction controls

The native PostgreSQL tests exercise the existing source-policy repository in a
temporary migrated database. Mutable Project and Task documents use independent
reader contexts to distinguish staged overlays, uncommitted SQL and committed
state. Both upsert and delete paths execute caller rollback and commit, retain
the current transaction and open writer connection, clear pending overlays after
successful flush, and preserve the original state until the caller completes.
The reader restores a connection that it opened. All envelope fields, normalized
policy content, nullable identifiers and versions above the 32-bit range are
compared after round trips.

A separate Run snapshot control rejects repository deletion and duplicate SQL
insertion. It rolls back the failed caller transaction, clears pending state and
compares the original persisted JSON text and complete envelope afterward.

These documents have synthetic scope identities and no item rules. The tests
qualify the repository's storage envelope and transaction boundary, not Project,
Task, file or connected-app admission, item-identity validation, HTTP privacy,
concurrent authorization, runtime execution, delivery or all-table RLS. They use
the existing isolated platform test context; no application role or RLS policy
is changed or qualified. No Web request or provider execution occurs. They do
not establish exception atomicity for an unowned flush, automatic pending-state
recovery, File/Artifact unknown-outcome reconciliation or full Recovery.

Product code, migrations, source-policy semantics, existing tests and assertions,
coverage scope, quality thresholds and Owner decisions remain unchanged. Actual
hosted compilation and both named PostgreSQL cases, required CI, the original
PR Gate and independent review must complete before integration. A PR collection
result does not establish Main coverage improvement; measure each resulting
Main independently. Local .NET execution is not used.
