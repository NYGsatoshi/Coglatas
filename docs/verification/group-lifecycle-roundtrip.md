# Group lifecycle persistence controls

The added round trip creates a Group and its creator Owner membership, changes
parent/name/type/description, adds and changes a participant, retains the existing
ReadOnly membership history after removal, and archives/restores the Group.
Repository reads reload persisted rows, verify Workspace list filtering and a
sibling Workspace control, preserve a historical join timestamp, and check the
seven service-save calls and the separately ordered audit-action list. The tests
do not assert per-operation audit/save interleaving. No-tracking reads before
later writes check that creation, metadata, each membership transition and
archival were persisted, so a later operation cannot mask an unsaved change.
Creation reloads also check the original parent, description, slug, Workspace,
creator, active status and absence of deletion before update can replace fields.
The update slug/status and newly added membership timestamp are checked before
later operations. This addresses source review `6719302558`; all prior response
and persistence assertions remain.
A PostgreSQL variant requires the existing
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

The original parent is read from the persisted Subject row before the workflow
and must be non-null. This preserves the parent-identity assertion while keeping
the validation fixture's original-parent property private after its Qodana repair.
The query captures the Subject identity scalar rather than the disposable fixture.
The first synchronized `74364156` CI run `42461849795/1` passed its build/tests
but failed the separate `AccessToDisposedClosure` inspection on that capture.
The original failure remains retained; the scalar-only correction needs fresh
execution and review without suppression, budget or assertion changes.
PR qualification does not qualify the repair's resulting Main; exact Main CI,
both full Qodana inventories and official Sonar are required separately.

The candidate depends on the independently reviewed Group validation repair.
Native execution, current protected checks and the original Gate, independent
review and exact resulting-Main qualification remain required. Source coverage
is measured by the existing producer; no local increment is projected into Main.
