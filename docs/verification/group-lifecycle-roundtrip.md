# Group lifecycle persistence controls

The added round trip creates a Group and its creator Owner membership, changes
parent/name/type/description, adds and changes a participant, retains the existing
ReadOnly membership history after removal, and archives/restores the Group.
Repository reads reload persisted rows, verify Workspace list filtering and a
sibling Workspace control, preserve a historical join timestamp, and check the
seven service-save/audit calls. A PostgreSQL variant requires the existing
migrated temporary database and preserves the caller-owned fixture transaction
until fixture rollback. The scenario database is dropped by the existing helper;
successful migration templates can remain for job reuse.

These controls reuse real EF repositories and the explicit current-user, grant,
audit and unit-of-work doubles from the validation fixture. They do not establish
runtime authorization, HTTP, durable audit-row persistence, concurrent isolation,
all-table RLS, File/Artifact reconciliation, physical acceptance or Release GO.
ReadOnly retention is the existing compatibility behavior, not a new authority
policy. Successful type/status values are now asserted through a full workflow;
the original fourteen validation cases and separate PostgreSQL partial remain
unchanged.

The candidate depends on the independently reviewed Group validation repair.
Native execution, current protected checks and the original Gate, independent
review and exact resulting-Main qualification remain required. Source coverage
is measured by the existing producer; no local increment is projected into Main.
