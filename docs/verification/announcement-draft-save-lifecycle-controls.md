# Announcement draft save and lifecycle controls

These nineteen additional `AnnouncementDraftServiceTests` cases exercise the
existing Application service and EF Core InMemory draft repository. Every
original test, fixture and product source remains unchanged.

- Twelve editable-save controls span all three priorities and Global,
  Workspace, Group and Channel target shapes. They verify trimmed content,
  flags, expiry, the returned target/identity/version and retained publication
  state. Creation covers the inverse target shape and rotated priority, with
  opposite flags/expiry, before replacement; literal actor, Tenant, version,
  content, initial publication/claim fields and zero attempt count are checked.
  A complete pre-operation mapped-scalar dictionary supplies the
  independent expected persisted row; only the named editable fields, version
  and observed persistence timestamp may change. The updated timestamp is
  independently bounded by the save call's wall-clock interval and may not
  precede creation; strict time advancement is not asserted. An unrelated
  retained draft must remain unchanged. Reusing the original version
  must fail without further row, announcement, invalidation or idempotency
  identity changes.
- Four controls obtain Scheduled or Published state through the original
  queue/worker APIs before attempting an edit with a current or stale version.
  The edit must report not editable and retain every mapped draft scalar,
  announcement identity, recorded announcement invalidation and idempotency
  identity. The original create ID is captured before queueing and checked on
  the queue response, worker claim and persisted draft. Scheduled state has no
  published row/invalidation; Published state has exactly the linked
  announcement and matching recorded invalidation before the rejected edit.
- Three list controls retain two own drafts and another author's draft. They
  recheck two distinct own audiences, exclude a selectively revoked audience and the
  other author, preserve ordering and every list projection field, and leave
  all mapped draft rows unchanged. A separate recording adapter returns
  authorization by exact target identity and records the exact ordered pair
  of actor/target/cancellation-token calls; extra or repeated calls cannot
  consume an unrelated queued outcome. Equal-timestamp ordering and cancelled
  operation behavior remain outside this scope.

The pre-operation snapshots explicitly establish the sentinel and other-author
rows and their independently captured author/Tenant identities. Related
announcement and idempotency identity checks observe all synthetic stored rows
with query filters disabled. The save response also requires null CTA and
Attachment fields for plain content, and its creation timestamp must match the
independently loaded persisted pre-save row, alongside the original response.

The snapshot helper requires exactly the explicit twenty-eight mapped scalar
names and rejects shadow properties. Unknown non-shadow fields cannot become
implicitly accepted preservation baselines.

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
