# Task scope clear caller transaction controls

The additive PostgreSQL fact invokes the existing Application clear service
with the real scope repository and the `EfUnitOfWork` wired to that same
repository. Its isolated migrated database has synthetic Tenant, account,
Workspace, Project, Task, default/override rows and both policy documents.
Distinct policies and versions above the 32-bit range make inheritance and
version comparisons explicit.

The fact runs caller rollback and caller commit. The service removes the
override row and Task policy document, advances the Task version and stores one
invalidation within the caller transaction. It retains the transaction and open
connection, clears pending repository state, and returns the inherited policy.
An independent reader sees the original selected rows, both complete document
envelopes/policies and empty Outbox until the caller finishes. Rollback restores
that selected snapshot. Commit exposes the removal, advanced version and the
same invalidation identity with selected envelope fields in an independent
context. The Project policy remains inherited.

The audit recorder checks one exact service call and metadata per invocation;
it does not persist an Audit row or qualify audit transaction atomicity.
Management authority is a fixture grant limited to the selected actor/Project.
This does not establish persisted-current authorization, HTTP privacy,
concurrent revocation, nonempty item-rule admission, dispatch, application-role
or all-table RLS, failure atomicity, File/Artifact reconciliation or full
Recovery. No production database, migration, role, policy or product logic is
changed. Existing tests/assertions and coverage/quality contracts remain intact.
Hosted compilation and actual named PostgreSQL execution with migrations,
required CI, the original PR Gate and independent review must complete before
integration; a PR pass does not establish resulting-Main coverage improvement.
