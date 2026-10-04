# Issue #362 Message thread context verification

Status: candidate implementation on `feat/362-message-thread-context`.

## Implemented contract

- one logical thread is owned by a canonical root Message through nullable
  `ThreadRootMessageId`; replies remain in the same Conversation;
- the main timeline excludes replies and projects authorized root summaries;
- exact authorized GET/POST thread routes provide a pinned root, at most the
  latest 100 replies, truthful `hasMore`, a three-name summary, strict first-
  reply creation authority, and target-isolated idempotency;
- deleted replies remain stable bodyless tombstones and count toward the
  durable summary; a deleted root with durable replies remains ordered and
  discoverable as a pinned bodyless tombstone, while ordinary deleted
  zero-reply Messages remain omitted and no deleted root accepts new replies;
- `Messaging.ThreadChanged.v1` is metadata-only and forces authoritative HTTP
  reconciliation. It deliberately has no aggregate version because reply
  count is not monotonic; reply `MessageCreated` events carry
  `threadRootMessageId`;
- channel and DM routes share a separate-draft thread surface, using a
  contextual desktop panel and dedicated pane at the existing 860-pixel mobile
  breakpoint, with native buttons, Escape, and focus return to the trigger or
  the focusable Message timeline when reconciliation removed that trigger.

Participant display names are capped per root inside one PostgreSQL windowed
query before materialization. Concurrent same-key Message commits reconcile
only the exact filtered unique-index race, return the one committed Message,
and roll back the losing audit/notification/outbox unit. A POST 400 retains the
thread draft and retry key while an authorized GET revalidates the projection;
only an explicit or revalidated access failure clears it. An accepted same-key
retry invalidates an older 400 revalidation response without blocking later
ThreadChanged refreshes. Panel focus is set once when a root opens and is not
reset by loading/ready/error transitions; if deletion disables the focused
reply draft, focus moves to the in-panel Back control before keyboard close.

Every projected root/reply author name and participant-summary name requires
both a same-Tenant `TenantUser` row and a historical same-Conversation
`ConversationMember` row. Lifecycle status, departure, removal, and soft
deletion do not erase a legitimate historical author name. A corrupt Message
that points at another Tenant's global User therefore projects no author name
and contributes no participant name.

Delete reconciliation clears the rendered body immediately and revalidates
the exact thread through the authorized GET. Per-root generations prevent an
overtaken summary response from removing or reviving the anchor. An authorized
zero-reply projection removes an ordinary tombstone; explicit access failure
clears it, while a transient failure retains only the neutral bodyless state
until catch-up or reload can settle the durable reply count.
Authoritative deletion also advances the retained root version. Once any
tombstone is present, delayed Created events (including same-author
client-request reconciliation) and Updated events cannot restore its body,
including while deletion revalidation is transiently degraded.

Legacy `ConversationType.Thread` / `ParentConversationId` data and APIs are
unchanged and compatibility-only. This change performs no anchor backfill,
attachment work, quoted reply, reactions, unread cursor, lock UI, or broader
Issue #343 action work.

Known boundary: general zero-reply main-timeline tombstone presentation remains
part of the broader Message actions work, not this Issue #362 slice. Issue #362
retains only deleted canonical roots with at least one durable reply.

## Security evidence

The focused HTTP tests exercise direct API access for cross-Tenant,
cross-Workspace, Project-scope, nonparticipant/admin, removed, revoked-
membership, read-only, and missing `CanCreateThread` cases. Denials are
asserted not to disclose bodies, counts, or participant names. Authorized
projection always precedes participant-name reads. Audit and ThreadChanged
payload assertions reject Message bodies and participant names.

## Local verification record

- `dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj --filter Scope=Issue362 --no-build`: 4 passed, 5 environment-skipped PostgreSQL cases, 0 failed;
- solution compilation, including all application references:
  passed with seven pre-existing warnings and no Issue #362 error;
- `dotnet ef migrations has-pending-model-changes`: no pending model changes;
- app Angular compiler `ngc -p tsconfig.app.json`: passed;
- frontend spec TypeScript `tsc -p tsconfig.spec.json --noEmit`: passed;
- focused Playwright spec standalone TypeScript compilation: passed;
- Linux Docker production build plus the exact Issue #362 320-pixel Playwright/axe
  scenario: 2 passed across `chromium-desktop` and `chromium-mobile`, 0 failed;
- frontend architecture check and its four-rule Node test suite: passed;
- `git diff --check`: no whitespace error.

Five `PostgreSqlFact` cases compile but were not executed locally because
`POSTGRES_TEST_CONNECTION_STRING` is unavailable. The local Windows Angular
unit runner also remained in transform/runner startup before test collection:
the complete focused file, a split pure-mapper file, and direct single-worker
Vitest attempts all exceeded their bounded timeouts without an assertion or
compile failure. Authoritative Linux CI must supply their execution result.

The branch was synchronized to main `aa51e7882a1b8069b020acffd71ab72013d05921`
and the focused backend, pending-model, Angular/spec TypeScript, Playwright
TypeScript, architecture, production-build, and focused Docker browser checks
above were rerun at that base. The complete static browser suite and Angular
unit assertions were not executed locally; exact-head Linux CI must supply
those remaining gates. Only the explicit two-project focused browser/axe pass
above is claimed locally.
