# Internal Form parent authority

The implemented `InternalForm` questionnaire service uses its stored Workspace,
Group or Project identity. Group and Project parents are resolved through the
tenant-filtered repositories; a loaded navigation or request-supplied Workspace
does not establish the stored parent's authority.

Management and privileged report reads require current parent Workspace access.
Creator ownership additionally requires current exact-scope visibility. Existing
scope-management grants remain a separate authority source, including explicit
archived-Project manager report access where the Project authorizer allows it.
Ordinary open/closed reads retain their existing exact-scope visibility checks.

Title, scope and status changes, archive and question mutations retain existing
independent scope-management grants after the current parent read check.
Contribution is required when creator ownership alone supplies mutation
authority. Creation and move destinations require the original scope-management
authority plus current parent read. An ordinary contributing creator need not
remain a scope manager. ReadOnly creators retain authorized draft/report reads,
but ownership alone does not authorize their mutations. Existing elevated
visible-scope staff and Group management grants remain independent of the
Workspace membership's ordinary contribution role. Existing operation-specific
validation still controls lifecycle availability; these checks introduce no new
Group ReadOnly or Project Viewer policy and do not change response submission.

The focused regression suites are `FormCreatorAccessRevocationTests`,
`FormParentBoundaryCompatibilityTests`, `FormParentBoundaryHostedHttpTests` and
`FormIndependentGrantHostedHttpTests`.
The first two use real repositories and authorizers with EF InMemory. The hosted
suites use the existing isolated PostgreSQL/Kestrel fixture, real cookie-session
validation, enabled CSRF, tenant middleware and production service registration.
Its Development tenant header and loopback HTTP settings do not establish
deployed HTTPS/cookie acceptance. The isolated migration role does not establish
application-role RLS enforcement. Fresh-request checks do not establish atomic
concurrent revocation and persistence.

This concerns the existing questionnaire implementation. It does not implement
or qualify the separate planned typed FormSubmission/Workflow model, promote
formal assurance, or authorize deployment or release.
