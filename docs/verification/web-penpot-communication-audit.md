# Penpot Web communication audit

Status: **Partial, source-backed presentation implemented** after recovery of
the revision 339 native export. Japanese/English localization is implemented.
The original blocked audit is retained below as history; the final continuation
records the recovered boards, inspected layers, implementation, and verification.
This record does not establish full Penpot parity or production acceptance.

## Original blocked audit: scope and baseline

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

## Original blocked audit: design-to-code map

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

## Original blocked audit: bounded follow-up candidates

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

## Original blocked audit: verification and remaining work

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

## Recovered design and bounded presentation continuation

The coordinator recovered the native `CoglatasUI` export and confirmed revision
**339**. This slice starts from integrated frontend commit
`bf1cafc7012338905c658a5e69663c4c99b4bcec`. The export manifest identifies file
`52a5041f-08a1-80f1-8008-c5a09f8b5f61` and `penpot/2.18.4-RC3`; the manifest
does not independently contain the revision number. Local evidence is under
`/workspace/work/penpot-unified/design/`: native shape JSON in
`export/files/<fileId>/pages/<pageId>/<shapeId>.json`, `tokens.json` in the file
root, board inventories, and native PNG references indexed by board UUID.
These temporary design assets are not checked into the application repository.

Native Channel, DM, thread, Saved Messages, and Message Settings PNGs were
visually inspected alongside the layer trees. Chat boards are 1536 by 1024;
Saved Messages and Settings boards are 1600 by 1000. The following board IDs
replace the unavailable IDs in the historical map.

| Verified page and board | Existing route/component and state owner | Current disposition |
| --- | --- | --- |
| `Coglatas · Chat UI`, page `52a5041f-08a1-80f1-8008-c5a09f8b5f62`; Channel `9b18ec83-d336-8057-8008-c5a1fe4aaa3d` | Workspace Channel and compatibility routes; `ChannelMessagingPageComponent`, `ConversationListComponent`, timeline/composer; existing `MessagingFacade` and exact Conversation APIs | **Partial**: source-sized feature navigator, channel-local header/canvas, compact rows and message typography; flat Channel messages retained. |
| Same page; DM `9b18ec83-d336-8057-8008-c5ad393394ef` | `/dm/:conversationId`; `DmPageComponent`, timeline/message item; existing DM participation and Message APIs | **Partial**: explicit DM incoming and own-message card variant, own alignment, source radii/surfaces/type. Missing contact projection remains **Deferred**. |
| Same page; thread workspace `9b18ec83-d336-8057-8008-c5bb050c35c1` | Existing Channel/DM same-root contextual thread; `ThreadPreviewComponent`, `MessagingFacade.thread`, Message-root thread API | **Partial**: existing pane uses 344px desktop width and source card density. Multi-root tab/workbench model is **Missing / Deferred**; no replacement persistence or route was invented. |
| Same page; Chat UI Kit `9b18ec83-d336-8057-8008-c5ae662bc8ad` | Shared messaging feature composer/timeline/item/thread components and existing action APIs | **Partial**: native layer inspection informs the bounded cards/composer; unavailable reactions, attachments and multi-root controls remain **Deferred**. |
| `08 Communication`, page `9b18ec83-d336-8057-8008-c5d02b492cc0`; Saved Messages `1f711f64-03d8-8014-8008-c5de871b4968` | `/messages/saved`; `MessageFollowUpsPageComponent`, `MessageFollowUpFacade`, private follow-up API | **Existing**, localized. Source origin table and follow-up/completed stages exceed the present save/remove contract and are **Deferred**. |
| Same page; Message Settings `1f711f64-03d8-8014-8008-c5de8b5091a1` | `/messages/settings`; `MessageSettingsPageComponent`, existing global notification service/API | **Existing**, localized. Configurable send keys, density and font persistence are **Deferred**. Global notifications, conversation mute and badge preferences retain separate authority. |
| `12 Menus & Popovers`, page `1f711f64-03d8-8014-8008-c603e64bd44f`; CHAT02 `1f711f64-03d8-8014-8008-c6095ba934dd`, CHAT03 `1f711f64-03d8-8014-8008-c60962a4d844`, CHAT10 `1f711f64-03d8-8014-8008-c609e8abcb18`, CHAT12 `1f711f64-03d8-8014-8008-c609f6747a87` | Existing Channel/DM action disclosure, private-save origin navigation and composer; current Message endpoints | **Existing / Partial**, localized. Inspected specimens have no interaction array establishing additional runtime behavior; reactions, new task tracking, configurable send settings and unsupported thread tabs remain **Deferred**. |

### Exact inspected implementation references

Geometry below is in source pixels. Board-relative rectangles are shown as
`x, y, width, height`; DM/card child coordinates are relative to their parent.
Theme colors are mapped through the existing semantic token system, whose
shared ownership remains outside this feature slice.

| Native layer ID | Source evidence and implemented relationship |
| --- | --- |
| `9b18ec83-d336-8057-8008-c5a1febc3f5f` | Conversation navigator `165,76,236,948`, fill `#171F29`; Channel feature navigator uses 236px and the sidebar token. Existing server-authorized rows/counts remain visible. |
| `9b18ec83-d336-8057-8008-c5a1fecb233d` | Channel workspace `401,76,740,948`, fill `#111820`; existing Channel header is moved inside its conversation canvas. Available width remains responsive to the existing application shell. |
| `9b18ec83-d336-8057-8008-c5ac5ac245b7` | Channel title `429,98,95,29`, Noto Sans JP 21px/700/1.35; feature title uses 21px and 1.35 line height. |
| `9b18ec83-d336-8057-8008-c5ac666aa560` | Flat Channel message `430,278,680,117`, no card fill; Channel messages stay flat rather than inheriting the DM variant. |
| `9b18ec83-d336-8057-8008-c5ac66d6bef1`, `9b18ec83-d336-8057-8008-c5ac66e8c8ad`, `9b18ec83-d336-8057-8008-c5ac66fa44f8` | Author 13px/600, timestamp 11px/400, body 13px/400/1.35. Presentation uses these sizes and weights while preserving opaque author names and bodies. |
| `9b18ec83-d336-8057-8008-c5ad83e4d731` | DM title 19px/700/1.35; existing DM title uses the source size/line height. |
| `9b18ec83-d336-8057-8008-c5ad9ef20270` | Incoming DM card `69,214,498,102`, radius 9, fill `#1C2733`; DM incoming cards have a responsive 498px maximum width, source radius and card token. |
| `9b18ec83-d336-8057-8008-c5ad9f5b2b33`, `9b18ec83-d336-8057-8008-c5ada05ea0f6` | Own DM cards are right aligned, widths 422/423px, radius 9, fill `#243A61`; existing `isOwnMessage` plus explicit DM route provenance selects this variant. No author text is used to infer origin. |
| `9b18ec83-d336-8057-8008-c5ac840cad5b`, `9b18ec83-d336-8057-8008-c5adaf46ade0` | Channel/DM composers 704 by 70, radius 8, fill `#172230`; existing composer receives source radius/input surface while retaining all available controls, validation and IME behavior. Heights remain content-driven for current controls and accessibility. |
| `9b18ec83-d336-8057-8008-c5bb7b657f42` | Thread pane `419,275,344,732`, radius 9, fill `#171F29`; existing desktop thread uses 344px width, source radius and sidebar surface. Mobile retains the established dedicated pane. |
| `9b18ec83-d336-8057-8008-c5bd93f073eb`, `9b18ec83-d336-8057-8008-c5bbb65b6fd5` | Root card 318 by 113/radius 8 and reply 314 by 84/radius 7, both `#1C2733`; existing root/reply cards use source surfaces/radii and 7px reply gaps. Text/controls determine actual height. |
| `9b18ec83-d336-8057-8008-c5bb7b9a13c2`, `9b18ec83-d336-8057-8008-c5bbdc735d88` | Thread title 15px/700 and composer 314 by 80/radius 8/input surface; existing title, body/meta type and textarea surface follow the available source. |

Additional inspected, unimplemented projections are Channel right information
pane `9b18ec83-d336-8057-8008-c5a1fedb6490` (395px), Saved main content
`1f711f64-03d8-8014-8008-c5de884830f0` (1360 by 718), Settings main content
`1f711f64-03d8-8014-8008-c5de8cc9e9e7` (1360 by 718), and menu cards
`1f711f64-03d8-8014-8008-c6095c9fe069`,
`1f711f64-03d8-8014-8008-c60963a453ab`,
`1f711f64-03d8-8014-8008-c609e9bfa064`, and
`1f711f64-03d8-8014-8008-c609f77feda0` (410px wide, radius 9). Missing contact,
membership, presence, reaction and richer Saved/status data are not fabricated.

The existing source contract is preserved: direct Reply/Save/More stay keyboard
and touch operable; Save is participant-private; Delete remains authorized by
the current API; Channel, DM, Later, unread, mute, saved state and global settings
remain distinct. Draft storage, composition handlers, HTTP DTOs, facades,
request effects, thread identity and retry state are unchanged by this visual
slice. No shared tokens, dependencies, screenshot baselines or hosted artifacts
are modified here. Native designs establish desktop references; the existing
mobile navigation, sticky composer and dedicated-thread adaptations are retained.

### Presentation verification

- Focused messaging Angular suite: **13 files / 131 tests passed**, including
  two new mounted Channel/DM route tests that verify explicit presentation
  provenance, unchanged supplied body, and retained Reply/Save/More controls.
- Production frontend build passed without changing budgets. Existing component
  stylesheet warning thresholds still emit warnings; the Message item stylesheet
  is 6.36 kB against its 4 kB warning threshold. No initial-budget exception was
  introduced.
- Narrow built-browser Message actions, mobile hierarchy and thread-context
  checks: **7 passed / 3 intentionally skipped** across Chromium desktop/mobile.
  They exercise keyboard/touch actions, delete confirmation, 320px no-overflow,
  composer reachability in a 320 by 420 keyboard viewport, list/focus/scroll
  restoration, the 860/861px responsive boundary, and isolated thread drafts.
  The first attempt could not launch the absent default-cache browser; rerunning
  with the existing shared browser cache completed. These checks use mocked API
  responses and the feature branch's pre-integration shared token snapshot.
- Full visual parity, unsupported projections, real-backend compatibility and
  authoritative Linux screenshot comparison are not established by unit/build
  checks. The coordinator owns combined-token viewport and Docker verification.
