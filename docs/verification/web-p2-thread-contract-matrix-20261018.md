# Web Thread Workspace — existing API vs multi-root lifecycle

Evidence date: 2026-10-18. Baseline Main `cb2805bca02cfcf75f4370d167baeb41fc024a9c`.
Penpot `CoglatasUI`, revision **339**, Chat board
`9b18ec83-d336-8057-8008-c5bb050c35c1`
("Dedicated Thread Channel — Multi-thread Workspace").
Owner: Web Angular, separate from future Avalonia Desktop.

## Current execution update — 2026-10-18

The Owner has explicitly selected THREAD-01 A, THREAD-02 A and THREAD-03 A:
canonical Message threads, memory-only multi-tab placement and existing
Conversation read semantics. The earlier proposal/hold language below is
retained as historical pre-selection evidence. The bounded Angular candidate
and its verification limits are documented in
[Dedicated Thread Workspace candidate](web-p2-thread-workspace-20261018.md).
THREAD-04 B is the selected persistent-Draft goal; its concrete API, database,
concurrency, authorization and RLS contracts remain subject to separate approval.
No durable tab state, new read aggregate or SEC-ARCH activation is approved.

## Original pre-selection matrix

This is a contract/risk matrix for [#1239](https://github.com/NYGsatoshi/Coglatas/issues/1239),
not an implementation approval or a claim that multiple concurrent
threads are supported by the current client.

| User action / state | Verified authority | Web status | Safe implementation boundary |
| --- | --- | --- | --- |
| Open one thread from Channel or DM | `GET /api/messages/{messageId}/thread` under `ConversationsController`, `MessagingApi.getMessageThread` | **Existing** contextual `ThreadPreviewComponent` | Request only after authoritative conversation scope; never retain denied bodies |
| Reply to root | `POST /api/messages/{messageId}/thread/messages`; current server authorization and send idempotency | **Existing** | Preserve origin conversation, caller scope, thread root and latest capability |
| Anchor / scroll / exact deep link | Thread GET accepts optional `anchorReplyMessageId`; current Message route context | **Partial** | No synthetic read history or cross-Channel anchor redirects |
| Several independent thread tabs | No verified server tab manifest, ordering, cross-device or reopen persistence contract | **Deferred** | Local-only navigation **may be proposed** after tests; no protected Message-body browser persistence |
| Pinned, watched, multi-root read/unread | Existing conversation participant/read API does not define independent thread hub state | **Held** | New read aggregate, durable preference and ownership semantics need explicit approval |
| Files/Task/Artifact links inside thread | Existing Task/File APIs and current message content are insufficient for arbitrary canonical links | **Held** | Do not fabricate link commands or cross-scope previews |
| DM vs Team Chat | Distinct route/origin, conversation participation and Workspace checks | **Required** | Never mix Channel directory and DM roster as one canonical state owner |

## Proposed contract for Owner review (not approved)

- **Identity:** TenantId, optional WorkspaceId for Channel, ConversationId,
  ThreadRootMessageId, selected tab ID, origin route, version/generation.
- **Scope:** Team Chat vs DM must remain separate; fast Channel↔DM switches
  cancel old requests, clear previous bodies, and reauthorize from server.
- **Lifetime:** decide whether tabs are ephemeral within current browser view,
  session-only (no contents cached), or durable per user across devices.
- **UI:** active tab, closed tab, loading/empty/denied/retry, focus restoration,
  keyboard/IME composition, unsent text strategy, and exact back-navigation.
- **Concurrency:** reconnect catch-up, duplicate events, deleted root,
  authorization revocation, older async response, stale draft and changed
  Conversation permission must all fail closed.
- **Server:** durable ordering/pins/read aggregates, if required, need
  explicit tenant-aware API/data-model review and SEC-ARCH/RLS qualification.
  Do not infer those semantics from the existing single-thread endpoint.

## Acceptance plan

1. Use current `MessagingFacade`, `ThreadPreviewComponent`, and existing
   source tests as the baseline; no new backend changes for UI-only work.
2. Add isolated tests with two root IDs, same display text but different
   identities, rapid switch and late response, revoked participant,
   Channel↔DM boundary, reconnect and composer IME.
3. For durable tabs or thread read state, create a separate design/API decision
   for Owner review before implementing server persistence.
4. Real backend E2E and [cross-screen regression #482](https://github.com/NYGsatoshi/Coglatas/issues/482)
   remain separate acceptance gates. A Storybook simulation is not evidence
   of a real multi-root conversation feature.

No new DB, RLS, SEC-ARCH approval, credential storage, or release assurance
is inferred. Related: [#1239](https://github.com/NYGsatoshi/Coglatas/issues/1239),
[#785](https://github.com/NYGsatoshi/Coglatas/issues/785), [PR #1234](https://github.com/NYGsatoshi/Coglatas/pull/1234).
