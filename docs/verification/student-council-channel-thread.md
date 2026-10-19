# Student council pilot: channel and thread workstream

Status: implementation candidate; release acceptance remains unverified.
Base: Product Main `6f940987f8df746736b6df5ea7371ab698028f5e`.

## Source and design boundary

The maintained Angular channel surface consumes `MessagingApi` through
`MessagingFacade`, using `GET /api/conversations`, exact Conversation detail,
and `GET/POST /api/conversations/{id}/messages`. Edit and delete use the existing
`PATCH/DELETE /api/messages/{id}` routes. Canonical threads are Message-owned,
with `GET /api/messages/{id}/thread` and
`POST /api/messages/{id}/thread/messages`. This change introduces no Post model,
Conversation type, public API, schema, dependency, or security activation.

`ConversationsController` delegates to `ConversationService`.
`SendMessageCoreAsync` checks current sending authority before looking up an
existing actor/Conversation/client-request identity. Its legacy non-disclosing
authorization denial can be HTTP 400; that status is not proof of an ordinary
validation failure. Group enablement remains a separate owner decision and is
not introduced by this workstream.

Penpot was inspected read-only through the connected `CoglatasUI` file
`52a5041f-08a1-80f1-8008-c5a09f8b5f61` on 2026-10-19 UTC. The coordinator's
traceability record owns the file revision evidence; Agent A reported live
revision 441. Direct structure/property reads and a PNG export covered:

| Board | Identifier | Applied mapping |
| --- | --- | --- |
| Approved Dark Team Channel Chat | `9b18ec83-d336-8057-8008-c5a1fe4aaa3d` | 266px navigator, 19px heading, compact timeline padding, existing dark semantic tokens |
| Dedicated Thread Workspace | `9b18ec83-d336-8057-8008-c5bb050c35c1` | Existing session-only thread tabs, 344px contextual pane, compact replies and origin card |

The inspected channel conversation is 702px wide on its 1536px board; the
navigator is 266px and the source inspector is 300px. Existing semantic tokens
remain the color authority and are maintained by the AppShell workstream.
The pilot does not add unsupported meetings, audio, reactions, channel Task/Wiki
tabs, pinned-message persistence, or attachment uploads merely because those
controls appear in the design. Full visual parity is not claimed.

## Implemented behavior

- The composer rejects composition-active submits and legacy IME Enter
  (`keyCode === 229`), and treats either in-flight input as a sending state.
- Uncertain main-timeline sends retain their existing idempotency identity for
  unchanged scoped body and canonical recipients, including same-conversation
  manual refresh. The identity stays in memory; this is not cross-device draft
  or retry synchronization. Editing the body or recipients starts a new request.
- Failed-row retry is single flight, clears the matching sent composer draft,
  and preserves a different newly composed draft. Optimistic composer retry
  reconciles the existing pending row rather than appending another copy.
- Explicit POST 401/403/404 removes protected content and the scoped draft.
  POST 400 hides content and reloads the complete authorized HTTP projection;
  denied revalidation cannot restore the old body or draft. A successful read
  preserves a validation-rejected draft and its retry identity.
- Routine transport catch-up captures only the current draft scope before
  hiding the Conversation projection. An input event already queued by the
  composer writes to that same scope, so the subsequent authorized read cannot
  replace the new text with an empty draft. Each request generation discards
  the capture; authorization or authoritative denial clears its stored draft,
  session/Tenant changes retain their full purge, and a Workspace boundary
  retains only the existing partitioned storage behavior. An unchanged
  uncertain send retains its request identity through this catch-up.
- Loading and expired-session states precede empty/permission messaging and
  suppress obsolete Message bodies. Offline and reconnecting channel notices
  describe the actual transport state without claiming delivery.
- At 861–1199px, an open contextual thread collapses the conversation rail so
  conversation controls and the 344px pane remain together. At 860px and below,
  the existing dedicated thread pane remains. Existing focus restoration and
  memory-only tab semantics are retained.

## Verification and remaining gates

Focused Angular specs cover retry identities with and without optimistic
rendering, changed bodies/recipients, concurrent retry, unchanged/different
drafts, interrupted refresh, explicit denials, legacy-400 revalidation,
authorization clearing, IME, and protected timeline states. Existing thread
contract coverage additionally exercises the composition-active form submit.

`tests/ui/message-thread-context.spec.ts` adds Approved Dark channel/contextual
thread diagnostic screenshots at 1536, 1366, 960, 768, 390, and 320px with overflow,
keyboard/IME, accessible thread controls, focus return, and separate-draft
checks. These use synthetic mocked API responses. They do not establish live
ASP.NET Core, PostgreSQL, SignalR transport, or public HTTPS acceptance.

The first host spec-typecheck began before dependency installation completed
and failed with missing Angular modules. The post-installation
`tsc -p tsconfig.spec.json --noEmit` passed. The focused Windows Angular test
bundle compiled, but all four Vitest fork workers timed out before test
collection; no host assertion pass is claimed. Linux execution uses the exact
canonical `mcr.microsoft.com/playwright:v1.64.0-noble` image and a new isolated
frontend dependency volume. Final execution results are recorded after that run.

The later catch-up repair was reproduced before changing production code:
the initial six focused cases had two failures (lost in-flight composer input
and its Workspace-partitioned retention) and four passes. Seven focused cases
passed after the repair, including authoritative-read denial and all four
protected boundary reasons. The final pinned Linux messaging selection passed
215/215 tests in 15 files, and host spec typechecking passed. The broad run also
exposed an existing inbox test precondition failure that reproduced with the
unchanged `95713db7` facade supplied through a read-only Docker mount. Running
fixture change detection after the initial mocked response established the
test's current actor before editing; all original denial assertions remain.
The canonical mention sort now supplies an explicit code-unit comparator,
preserving the prior ordering without relying on the runtime locale. These
are mocked frontend checks and do not establish actual backend delivery.

Required before release: passing exact-head Linux tests/build, independent
review and protected CI, integrated Linux screenshot inspection, actual
PostgreSQL HTTP/SignalR multi-user/revocation evidence, and the separate Student
Council public HTTPS release gate. This document grants no pilot GO decision.
