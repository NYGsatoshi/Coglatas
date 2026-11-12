# Legacy Announcement service contract tests

Current-source independent inspection6674828994 found no blocking regression
and identified a reader-binding assertion gap. The list projection cases now
also assert the Actor and CancellationToken passed to the read-ID query,
retaining the existing requested-ID and projection assertions. Product sources
and all earlier assertions remain unchanged.

`AnnouncementLegacyLifecycleContractTests` exercises the existing
`IAnnouncementService` implementation used by `AnnouncementsController`.
It complements the separate Analytics, audience and Attachment service suites.
It does not replace the persisted current-authority tests or their boundaries.

The tests cover missing authentication/identity, bounded pagination with scope
and reader-state projection, invisible/missing/deleted resources, active
persisted administrator admission for global creation, trimmed creation,
invalid content/expiration, pin transitions, preservation of unspecified fields,
idempotent read staging, unread-only reminders, the 499/500/501-character reminder
body boundary, soft deletion, and recipient-scoped invalidations. The
non-author denial cases cover updates, deletion, read status and reminders.
Scoped creation controls deny Workspace, Group and Channel operations after
their required resource/authority reads. Existing resource-before-identity
ordering is preserved for mutation and read-status APIs. Strict
dependencies reject unexpected effects and require audit, invalidation and save
in the expected order. Reminder dependency failures propagate without later
effects or a save. The test does not claim rollback of already staged changes.

Request constructors reject blank bodies. The service validation tests also
exercise public record object initializers, which can change init properties
after constructor validation. Neither validation boundary is removed or relaxed.

This suite executes Application code with contract doubles. It does not prove
SQL translation, PostgreSQL transactions, repository tenant filtering, current
persisted authorization, HTTP admission, delivery, deployed RLS, or pilot
acceptance. Local coverage is diagnostic; only an independently qualified
resulting Main analysis establishes Main coverage or Quality Gate changes.
