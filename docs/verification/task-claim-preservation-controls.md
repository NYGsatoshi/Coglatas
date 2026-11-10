# Task claim preservation controls

The additive `TaskCommandServiceTests.Claim.cs` source declares 23 Application
cases: 13 refusal cases, six existing-Watch combinations and two new-Watch
controls that also repeat the claim. The original test source changes only its
outer class declaration to `partial`; its fixture and assertions remain intact.
Two additional cases give the Task a creator distinct from the current actor,
requiring the actor in both stored/returned assignee identity and notification,
separate Creator/PrimaryAssignee Watches and both affected-user identities.

Before each command, the controls capture every stored Task, Watch and
collaborator key, original object reference and immutable scalar value. They
retain an unrelated deleted Task and its Watch/collaborator rows. Refusals must
retain those complete collections and produce no audit, notification,
invalidation, command save or tracking clear. Successful controls change only
the selected assignee/version and the expected Watch state, check the returned
identity/version, and bind audit, semantic invalidations and notification
requests to the independently captured Task and actor identities. Explicit
opt-out and manual Watch intent remain unchanged. A repeated claim produces no
second effect.

These are repository and effect doubles. The Project fixture always returns a
membership; the cases do not establish the missing-Project-membership branch,
persisted-current membership, Tenant admission, HTTP privacy, PostgreSQL
atomicity, actual notification delivery, deployed roles/RLS or Recovery.
Navigation properties and intermediate states are outside the scalar snapshot.
The new-Watch control separately checks its fields before adopting the new row
into the expected collection; its generated ID is not predetermined.
Its empty staged Tenant ID is the existing fake-save observation; actual EF
Tenant stamping and a committed row are not established by that assertion.

This source is uncompiled and unexecuted until its exact-head hosted run is
qualified. Preserve source/tree, tested parents, run/job/attempt and original
artifact/report/log digests; require the six checks, original official PR Gate,
independent review and live protection/thread/Owner-HOLD checks. Measure actual
resulting-Main covered-line/condition and denominator deltas separately. No
coverage forecast, report rewriting, scope reduction, threshold/budget change,
formal security disposition, historical issue closure or Release GO is claimed.
