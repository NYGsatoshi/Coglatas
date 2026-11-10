# Announcement draft save and lifecycle controls

These nineteen additional `AnnouncementDraftServiceTests` cases exercise the
existing Application service and EF Core InMemory draft repository. Every
original test, fixture and product source remains unchanged.

- Twelve editable-save controls span all three priorities and Global,
  Workspace, Group and Channel target shapes. They verify trimmed content,
  flags, expiry, the returned target/identity/version and retained publication
  state. A complete pre-operation mapped-scalar dictionary supplies the
  independent expected persisted row; only the named editable fields, version
  and observed persistence timestamp may change. Reusing the original version
  must fail without further row, announcement, invalidation or idempotency
  identity changes.
- Four controls obtain Scheduled or Published state through the original
  queue/worker APIs before attempting an edit with a current or stale version.
  The edit must report not editable and retain every mapped draft scalar,
  announcement identity, recorded announcement invalidation and idempotency
  identity.
- Three list controls retain two own drafts and another author's draft. They
  recheck each own audience, exclude a selectively revoked audience and the
  other author, preserve ordering and every list projection field, and leave
  all mapped draft rows unchanged.

The original raw Main OpenCover report identified unvisited save/list branches;
that observation selects test scope and does not forecast a coverage gain.
No producer, raw report, source accounting, coverage scope, assertion, budget
or threshold changes are made.

The existing audience adapter is synthetic and allows unknown scope IDs by
default. These cases establish neither current persisted user/membership
authority nor valid foreign keys, HTTP/privacy, PostgreSQL concurrency or
transactions, distribution-store behavior, SQL Audit/Outbox delivery, deployed
roles/RLS, reconciliation or full Recovery. The original audit recorder does
not retain audit entries, so no audit-payload or zero-audit claim is made.
Identity checks on related aggregates do not establish their full byte/state
preservation. Independent source review, supported hosted compilation and
named execution, analyzers, all required checks and original PR Gate remain
mandatory; no local .NET execution is claimed. This candidate cannot overtake
the completed resulting-Main assessments and existing #1889 sequence.
Every merged Main requires fresh native/report/source qualification and actual
official line/condition/denominator measurements. Pilot status remains NO_GO.
