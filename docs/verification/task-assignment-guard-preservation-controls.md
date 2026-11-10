# Task assignment guard preservation controls

The additive `ProjectServiceTests.AssignmentGuards.cs` suite exercises the
existing assignment compatibility adapter with the existing authorization
services and repository fixtures. It changes no product or role policy.

The source declares 39 cases in nine methods: eight Add guard cases, eleven
Update guards, four Delete guards, four unchanged-hours controls, three unmapped
same-role refusals, three unmapped role-change refusals, and two cases each for
occupied canonical assignments, ambiguous Add and ambiguous Delete.

Each case captures immutable scalar projections before the operation. The
unfiltered Task, assignment, collaborator and Watch fixture collections include
an unrelated seeded graph. Comparisons retain cardinality, identities, all
assignment/collaborator/Watch scalar fields and sixteen selected Task fields,
including canonical relationship IDs, version and inherited deletion state.
No filter discards newly created or wrongly linked relationship rows.
Refusals and unchanged-hours controls require no audit or notification intent,
authorization invalidation, Task/assignment/Project invalidation, generic save,
command save or tracking clear. The unchanged-hours DTO is checked against
identities and values captured before the service call.

All baseline tests, assertions and fixture bodies remain unchanged. These are
repository-double controls, not HTTP privacy, current persisted authority,
PostgreSQL transaction, durable Audit/Outbox identity, exception atomicity,
delivery, deployed RLS/application-role, historical incident or full Recovery
qualification. Source declarations are not discovered/executed results; fresh
supported hosted execution, inspection, CI, original PR Gate, independent
review and resulting-Main qualification are required separately. Coverage scope,
producer reports, zero-hit ranges, thresholds and budgets remain unchanged.
No projected or summed Overall Coverage gain or release approval is inferred.
