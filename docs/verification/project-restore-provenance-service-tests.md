# Project restore admission retains lifecycle provenance

The existing restore service requires an explicit Project manager, an active
parent Workspace and a consistent stored prior lifecycle state. Twenty-five
Application cases exercise that contract through the existing authorized-manager
fixture without changing the service or authorization policy.

Seventeen invalid provenance combinations cover a missing or unsupported archive
source, legacy activation, contradictory activation timestamps or versions, and
missing or inconsistent nested suspension provenance. They retain the selected
Project fields after reading the fixture repository entry again, with unchanged
inventory and no command save, generic save, tracking clear, audit, Project
invalidation, authorization publication or Task notification.

Eight positive combinations restore the exact recorded Planning, operational or
nested Suspended state. They check the selected activation fields, scoped audit,
Project invalidation and reader authorization publication, and one command save.
A second restore must reject without repeating those selected effects.

These are Application tests using repository, authorization, audit, publisher and
unit-of-work doubles. The repository stores object references; reading it again
does not establish independent PostgreSQL persistence. The production governance
save interceptor owns archive provenance capture and clearing; these tests do not
simulate or qualify that interceptor. Existing native governance tests separately
exercise direct PostgreSQL persistence and nested recovery. They do not replace
an end-to-end service/transaction or full deployment Recovery/Rollback acceptance.

Only selected fields and effects are asserted, not the whole object graph, every
event payload, concurrent authority, caller transaction ownership, deployed RLS,
File/Artifact reconciliation or actual pilot acceptance. Directly prepared fixture
states are not a migration, data repair, activation promotion or Owner approval.
All existing source, fixture, test and assertion bytes remain unchanged. Supported
hosted compilation and all named cases are required; no local .NET is used.
Actual resulting-Main coverage and denominator must be measured independently;
no subject, exclusion, threshold, baseline, report or acceptance criterion changes.
