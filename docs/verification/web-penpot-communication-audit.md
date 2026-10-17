# Penpot Web communication audit

Status: **Blocked for design implementation**. Existing-UI Japanese/English
localization is independently authorized and implemented in the continuation
below. This record does not establish Penpot parity or production acceptance.

## Scope and baseline

- Repository baseline: `05cc6095225ca8dc7271bf395c90ba18f23624db`.
- Audited application: `frontend/src/app/features/messaging/` and its existing
  route, controller, contract, and test references.
- Design file supplied for this task:
  `52a5041f-08a1-80f1-8008-c5a09f8b5f61`.
- Coordinator-verified metadata: file name `CoglatasUI`; current page
  `Coglatas · Chat UI`, ID `52a5041f-08a1-80f1-8008-c5a09f8b5f62`.
- Requested design surfaces: `Coglatas · Chat UI` and `08 Communication`,
  including Channel, DM, Dedicated Thread, Chat UI Kit, and menus/overlays.
- Design revision, remaining page/board IDs, shape trees, geometry, colors, typography,
  interactions, and representative exports: **not obtained**.

The Penpot high-level overview and `File`, `Page`, `ShapeBase`, and `Text` API
documentation were read. The read-only `currentFile.pages` inspection failed:

> The Penpot plugin tab appears to be suspended by the browser (no heartbeat
> for 163s). Please click/focus the Penpot tab to wake it, then retry.

No design mutations or page switches were attempted. Board names in this
record are task-provided targets, not independently verified design objects.
No board ID or design value is inferred from a similarly named component.
The task's unavailable-design fallback therefore prevented visual and
interaction implementation in the original audit slice. No application source, API, schema,
authorization model, dependency, lockfile, snapshot, or hosted artifact is
changed by this audit.

## Design-to-code continuation map

`Existing` means current source has a wired implementation; it is not evidence
of design parity. `Partial` identifies a bounded implementation or observed
gap. `Blocked` means the required design evidence is unavailable. `Deferred`
identifies functionality deliberately outside the current API/product
contract. All visual comparisons below remain **Blocked**.

| Task-provided surface / board ID | Route and component | State/API owner | Existing interaction and test references | Source status / design status |
| --- | --- | --- | --- | --- |
| Chat UI / inbox; board ID unavailable | `/messages`; `MessagesPageComponent`, `MessageSearchFiltersComponent`, `ConversationListComponent` | `MessagingFacade`, `MessagingApi`; `GET /api/conversations?view=All|Unread|Mentions|Later`; Message search below | Authoritative category counts, selected/unread/mention/Later states, recipient search and direct-conversation creation, mobile list return. `messaging-ui.spec.ts`, `message-inbox-workflow.spec.ts`, `conversation-list.component.spec.ts`, `message-navigation-state.service.spec.ts`; `tests/ui/message-mobile-navigation.spec.ts` | Existing; recipient dialog gaps below / Blocked |
| Channel; board ID unavailable | `/workspaces/:workspaceId/channels/:conversationId`; `ChannelMessagingPageComponent` | `MessagingFacade`, `ActiveWorkspaceFacade`; exact Conversation detail, participant state, Messages, read marker | Committed Workspace matching precedes detail loading; participant/read/post checks, manual refresh, degraded-realtime notice, contextual thread. `messaging-ui.spec.ts`, `message-thread-context.spec.ts`, `message-settings-scope.spec.ts` | Existing / Blocked |
| Channel compatibility route; board ID unavailable | `/conversations/:conversationId`; same channel component | Same facade; resolves expected scope from active Workspace | Missing active Workspace fails closed; mismatched detail is rejected before Messages/realtime. `messaging-ui.spec.ts` | Existing compatibility route / Blocked |
| DM; board ID unavailable | `/dm/:conversationId`; `DmPageComponent` | `MessagingFacade`; exact Conversation detail, participant state, Messages, read marker | Read requires DM kind and current participation; nonparticipants/removed users do not receive a composer; precise other-user read times are disabled. DM list preview is withheld. `messaging-ui.spec.ts`, `message-thread-context.spec.ts` | Existing / Blocked |
| Dedicated Thread; board ID unavailable | Channel/DM contextual panel; dedicated pane at the existing mobile breakpoint; `ThreadPreviewComponent` | `MessagingFacade.thread`; `GET /api/messages/{messageId}/thread`, `POST /api/messages/{messageId}/thread/messages` | Pinned exact parent, separate draft, bounded reply notice, first-reply creation permission, deleted-root denial, retry identity, Escape/Back/Close and focus return. `message-thread-context.spec.ts`, `messaging-thread-mapper.spec.ts`; `tests/ui/message-thread-context.spec.ts` | Existing bounded panel; no standalone canonical thread route / Blocked |
| Chat UI Kit: composer; board ID unavailable | Channel and DM; `MessageComposerComponent` | `MessagingFacade`, `DraftStorageService`; `POST /api/conversations/{conversationId}/messages` | Enter sends; Shift+Enter inserts a newline; current composition state and `isComposing` guard keydown; disabled/sending/failure states; explicit mention IDs. `message-composer.component.spec.ts`, `draft-storage.service.spec.ts`, `messaging-ui.spec.ts` | Existing; IME edge candidates below / Blocked |
| Chat UI Kit: timeline/message; board ID unavailable | Channel and DM; `MessageTimelineComponent`, `MessageItemComponent`, `FailedMessageItemComponent`, `NewMessageBannerComponent` | `MessagingFacade`; existing update/delete/report/thread/private-save endpoints | Confirmed/pending/failed rows, body-hidden/empty states, direct Reply/Save/More, own edit, destructive confirmation, safe generic report, action status, deleted-thread anchors. `messaging-ui.spec.ts`, `message-thread-context.spec.ts`; `tests/ui/message-actions.spec.ts` | Existing; disclosure/state candidates below / Blocked |
| Menus/overlays; board ID unavailable | Message More disclosure, delete/report dialogs, new-DM dialog, conversation settings, global settings | Existing component state; `CoglatasDialogComponent` where already used; existing mutation contracts | More is intentionally a plain disclosure rather than an ARIA menu. Shared confirmation and settings tests cover Escape/focus return. New-DM dialog is a separate local implementation. `messaging-ui.spec.ts`, `message-settings-scope.spec.ts` | Partial; keyboard gaps below / Blocked |
| Saved Messages; board ID unavailable | `/messages/saved`; `MessageFollowUpsPageComponent` | `MessageFollowUpFacade`; `/api/me/message-follow-ups` | Participant-private save/removal, exact root/reply navigation, current-authority refresh and protected-state clearing. `message-follow-up.facade.spec.ts`, `message-follow-ups-page.component.spec.ts`; `tests/ui/message-follow-ups.spec.ts` | Existing distinct authority / Blocked |
| Global Message settings; board ID unavailable | `/messages/settings`; `MessageSettingsPageComponent` | `MessageGlobalSettingsService`; `/api/me/message-notification-preferences` | Global Message notifications are separate from conversation mute and local badge display preferences. `message-settings-scope.spec.ts` | Existing distinct scope / Blocked |

Relative component/spec references in this table are under
`frontend/src/app/features/messaging/`. Route declarations are in
`frontend/src/app/app.routes.ts`.

## Existing server contracts to preserve

The browser uses `MessagingApi`; the relevant server entry points are
`ConversationsController`, `MessageFollowUpsController`,
`MessageNotificationPreferencesController`, and `SearchController` under
`src/Coglatas.Web/Controllers/`. Source inspection confirms these route
declarations; no real-backend execution was performed for this audit.

| Operation | Existing HTTP contract | Boundary |
| --- | --- | --- |
| Inbox and exact Conversation | `GET /api/conversations`; `GET /api/conversations/{conversationId}` | Server-authorized Conversation rows/counts and exact membership. Browser filtering cannot confer access. |
| Start DM | `GET /api/conversations/recipients?query=...`; `POST /api/conversations/direct` with `{ recipientUserId }` | Existing server-authorized recipient projection and direct-conversation contract. |
| Main timeline | `GET /api/conversations/{conversationId}/messages` with optional `before`/`anchorMessageId`; `POST` with `{ body, clientRequestId, mentionedUserIds }` | Exact stored Conversation authority; sending becomes confirmed only after an accepted response. |
| Canonical same-Conversation thread | `GET /api/messages/{messageId}/thread` with optional `anchorReplyMessageId`; `POST /api/messages/{messageId}/thread/messages` | Canonical root Message identity, exact Conversation scope, first-reply `createThread` permission, bounded replies, and target-scoped retry identity. |
| Existing Message actions | `PATCH /api/messages/{messageId}` with `{ body }`; `DELETE /api/messages/{messageId}`; `POST /api/messages/{messageId}/report` with `{ reasonCode }` | Existing author/resource authorization. UI must not invent moderator authority or a report-case workflow. |
| Private saved Message | `GET /api/me/message-follow-ups`; `PUT`/`DELETE /api/me/message-follow-ups/{messageId}` | Participant-private saved Message, independently reauthorized through its current Conversation. |
| Conversation preference and read cursor | `GET`/`PATCH /api/conversations/{conversationId}/state`; `POST /api/conversations/{conversationId}/read` | `isMuted`, Conversation-level `isLater`, and read state remain distinct from each other and from saved Messages. |
| Global notifications | `GET`/`PATCH /api/me/message-notification-preferences` | Global Message notification preference; it does not mutate Conversation unread state. |
| Message discovery | `GET /api/search?type=Message`; `GET /api/search/message-authors` | Bounded, server-authorized results and sender options. Free text/snippets remain memory-only; allowed non-sensitive facets follow the existing contract. |

The current canonical thread is not a new Conversation. Legacy
`ConversationType.Thread`/`ParentConversationId` must not be remapped to an
invented Message root. Team channels, DM participation, Conversation-level
Later, private saved Messages, conversation mute, and global notification
preferences must retain their separate meanings.

Main drafts are partitioned by Tenant, authenticated user, Workspace (or DM
bucket), and Conversation in session storage. Storage failures are best effort;
Workspace boundaries clear rendered protected state while preserving the
partitioned main draft, and session/Tenant boundaries clear stored drafts.
Thread drafts are separate in-memory state; they are not persisted by
`DraftStorageService`. Request generations, cancellation, tombstone handling,
and authoritative realtime refetches already have focused regression coverage.

## Bounded follow-up candidates

These are current-source observations, not implemented changes or verified
Penpot discrepancies. They remain blocked under this task's fallback until
the actual relevant design and representative export can be inspected.

1. **P0 new-DM dialog keyboard lifecycle.**
   `MessagesPageComponent` declares `role="dialog"`/`aria-modal="true"`, but
   has no focus entry/trap/return or Escape handler. Its recipient options use
   `role="listbox"`/`role="option"` without listbox arrow-key behavior.
   Compare the actual overlay with the existing shared dialog contract, then
   add a focused keyboard/focus regression if implementation is authorized.
2. **P0 stale recipient search responses.**
   `onRecipientQueryChange` creates independent subscriptions without request
   cancellation or generation checks. An older result can replace a newer
   query or refill state after dialog close. Test controlled out-of-order
   responses and close/reopen before choosing the smallest existing-pattern
   repair. This audit does not change recipient or DM authority.
3. **P0 composer IME edge coverage.**
   Main and thread keydown handlers already guard `isComposing` and local
   composition state. Neither checks legacy `keyCode === 229`; form-submit
   paths do not independently guard active composition. Current tests do not
   cover those paths. Preserve ordinary Enter, Shift+Enter, modified keys,
   failure drafts, and disabled/sending behavior when validating any repair.
4. **P1 More disclosure dismissal.**
   `MessageItemComponent` toggles the plain disclosure but has no explicit
   Escape/outside-click/focus-out dismissal. Compare the actual menu state
   before changing semantics; preserve the existing direct Reply/Save/More
   hierarchy and author-only edit presentation.
5. **P1 timeline loading state.**
   `MessageTimelineComponent` renders body-unavailable before considering
   `loading`, and the empty branch includes any zero-message state. Audit the
   actual initial-load/error/retry boards before adding truthful state
   presentation; never interpret unavailable membership as an empty inbox.
6. **P1 same-route saved-message navigation.**
   Channel and DM components read focus/thread query parameters from a
   snapshot inside their `paramMap` subscriptions. Exercise query-only
   navigation between saved Messages in the same Conversation before
   concluding that both anchors update under Angular route reuse.

Attachments remain **Deferred** for this UI slice: the composer explicitly
disables them while the canonical File contract is unavailable. Reactions,
quoted-reply models, report evidence/cases, inferred moderation capabilities,
new conversation types, and new thread routes are not authorized substitutes
for unavailable design or API evidence.

## Verification and remaining work

- Audited current routes, messaging API/facade/mapper, Channel/DM/composer/
  timeline/thread/recipient-dialog components, draft storage, Stories, focused
  Angular specs, and the existing Message browser-spec inventory.
- Existing Stories cover Channel/DM default, empty, disabled composer,
  removed participant, refresh error, long Message, failed outgoing retry,
  unavailable attachment, new-message banner, and mobile scenarios. Their
  presence is not a rendered or visual comparison result.
- No new regression test was added because this fallback changes only an
  audit record. Existing tests were inspected; no test pass, real-backend
  compatibility, browser rendering, screenshot parity, or axe result is
  claimed here.
- No PNG/SVG was exported; no design-derived dimensions/colors/type tokens
  were verified. No screenshot baseline was changed.

To resume: recover the connected Penpot plugin; capture file revision and
actual page/board IDs; read the target trees and visual/state properties;
export representative Channel, DM, thread, and overlay boards; compare
existing runnable surfaces; select a bounded source-backed change; then run
focused Angular and appropriate built-browser keyboard/mobile/axe checks.
Authoritative Linux screenshot comparison remains separate from mocked API
and unit evidence.

## Independently authorized localization continuation

The user's later Japanese/English requirement authorizes localization of the
existing communication UI while design reads remain blocked. It does not
authorize inferred layout, color, geometry, new interaction behavior, or new
server capabilities.

- Existing inbox, channel, DM, thread, composer, timeline, message actions,
  search/filter, settings, and saved-Message labels, accessible names,
  placeholders, confirmations, safe status/error copy, counts, and timestamps
  use the current `I18nService` and a 274-key English/Japanese communication
  catalog. Common close/cancel/retry labels reuse shared keys.
- Explicit application-authored translation-key metadata distinguishes
  generated fallbacks and safe errors from opaque server/user content.
  Display projections retranslate state when the existing locale signal
  changes. Server Message bodies, Conversation titles, author names, search
  input, identifiers, wire enums, reason codes, membership/capability checks,
  drafts, and retry identities retain their meanings.
- Switching languages on mounted inbox/composer/thread surfaces preserves
  drafts, focus, DOM identity, and server-authored content, and does not submit
  or refetch. Date filter inputs, URL parameters, and request DTOs retain ISO
  calendar values; filter chips render locale-aware dates.
- Existing English assertions explicitly select English. The global Japanese
  default is unchanged. Japanese composition and sample body fixtures remain
  intentional product/test data rather than engineering prose.

Local verification for the localization source: focused messaging Angular
suite **13 files / 129 tests passed**; frontend spec TypeScript and application
Angular compiler passed; communication catalog key/interpolation parity and
non-empty translations passed; `git diff --check` passed. No backend behavior,
rendered-browser, screenshot parity, or design-conformance result is claimed
by these local checks.
