# SEC-16 retained File reads across a Workspace ReadOnly transition

Related: #1349. This test-only slice uses the existing current contract; it
does not approve a formal SEC-ARCH mapping, RLS activation or pilot upload policy.

Two explicit facts in `FileReadOnlyRetainedCredentialHostedHttpTests.cs` cover
the FileObject and Attachment grant/delete route families. The original fifteen
lifecycle, actor/resource and sharing-policy facts remain unchanged.

Each fact uses migrated real PostgreSQL, the actual Web entry point, independent
Owner/Member cookie and CSRF sessions, and physical synthetic bytes. The Member
uploads a private direct Workspace File through the existing fixture capability.
A separate owned File proves that the selected deletion route works before the
role change. The Owner then uses the actual membership PATCH to commit
`Member -> ReadOnly -> Member`, with persisted role/status checks. The original
Member client and active database session remain the same throughout.

The retained opaque grant and a newly issued ReadOnly grant must return identical
bytes with `no-store`; the independent Owner remains an authorized positive.
ReadOnly deletion with valid CSRF must return the established safe 400 envelope
without changing selected File, Attachment, sharing, grant or successful-use
state or producing a successful deletion audit. After Member restoration, both
retained credentials still read, and the selected deletion succeeds with both
soft deletions and its audit persisted. Sensitive assertion diagnostics and audit
checks do not print File content, credentials, identifiers or storage values.

This distinction is deliberate: current Workspace view and historical uploader
identity permit this private File read, while deletion also requires current
contribution. A role-only downgrade does not revoke an otherwise valid view
grant. No new authorization behavior is specified by these tests.

Qualification requires an exact clean committed Release build, seventeen named
File facts executed with zero failure/skip/not-executed, source/version/digest
checks for the seven test-directory assemblies, a final TCP/database/version
query and effective PostgreSQL privacy settings, and owned-fixture cleanup.
Configured tests are not evidence that this qualification ran. Local results
must remain bound to their actual commit and do not transfer to a later Main.

This is not full SEC-16 role/history/placement/batch/eligibility coverage, formal
RLS proof, target HTTPS or release readiness. #1349, #582, the held PRs #1320 and
#1326, unresolved #1342/#1346/#1353/#1355 and both Owner HOLDs remain unchanged.
