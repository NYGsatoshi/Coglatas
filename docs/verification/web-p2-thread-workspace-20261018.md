# Dedicated Thread Workspace candidate

Date: 2026-10-18. Product baseline: `50c3242950d22105063ec3b97a90adf7b5b34b0d`.
Related work: [#1239](https://github.com/NYGsatoshi/Coglatas/issues/1239),
[existing contract matrix](web-p2-thread-contract-matrix-20261018.md).
This candidate requires Coordinator review, protected PR checks and exact-Main
verification. Local mocked HTTP tests do not establish real-backend acceptance.

## Authorization and design basis

The current execution instruction explicitly selected THREAD-01 A (canonical
Message threads), THREAD-02 A (session-memory tab placement) and THREAD-03 A
(existing Conversation read semantics). These selections supersede the older
UI-lifecycle proposal hold in #1239 for this bounded implementation only.
THREAD-04 B remains a completion goal whose concrete server API, persistence,
concurrency, authorization and RLS contracts require separate Owner approval.
No backend route, DTO, database schema, migration, RLS, role, grant, read-marker
semantics, SEC-ARCH mapping or release approval changes in this candidate.
PRE-AVALONIA SEC-ARCH remains **BLOCKED**.

The attached native `CoglatasUI.penpot` was inspected read-only. File identity:
`52a5041f-08a1-80f1-8008-c5a09f8b5f61`; export revision 339; archive SHA-256
`847be6abd1669353a2f8f67e1a518e21ac09851b820167df0de7d47564c4580c`.
The native frame and its referenced PNG were inspected for Chat board
`9b18ec83-d336-8057-8008-c5bb050c35c1`,
"04 / Dedicated Thread Channel — Multi-thread Workspace", 1536 x 1024.
The current live Penpot revision is **UNVERIFIED**. This is a bounded functional
implementation informed by its tabs, dedicated workspace and root sidebar;
it is not a full visual-parity claim.

## Delivered behavior

- Channel and DM reuse `ThreadWorkspaceComponent` around the existing
  `ThreadPreviewComponent`. The contextual panel can expand into a dedicated
  workspace; its root sidebar describes only the currently loaded authorized
  Conversation messages, with an explicit bounded-list explanation.
- Multiple tabs open, switch and close independently. Topic labels are derived
  from the current authorized timeline body with bounded Unicode-safe truncation;
  labels fall back to localized Thread N and are not stored separately.
- Each tab binds Tenant, authenticated user, Workspace, Conversation, Channel/DM
  kind and canonical root Message. Every activation calls existing
  `GET /api/messages/{root}/thread` again before restoring a draft.
  Root/reply Conversation and Workspace metadata must agree with the current
  authorized context. No synthetic Conversation or cross-Conversation catalog
  is created.
- Inactive tabs retain only identity, navigation metadata, draft text, pending
  send identity and safe send-error state in process memory. They retain no
  response body or participant projection. Thread text is never written through
  `DraftStorageService`, localStorage, sessionStorage or a new API.
- A pending send locks its draft. Completion is bound to the route generation,
  root and immutable client request ID. Switching tabs cannot clear another
  draft; closing the tab cannot be undone by a delayed send completion. Failed
  background sends retain the draft, safe error and same-key retry behavior.
  Send completion during tab reauthorization starts a fresh projection instead
  of stranding the active tab in loading state.
- Authorization denial clears Conversation bodies, mention candidates, action
  selection, current main-composer session draft, all tab drafts/identities,
  pending keys and expanded selection. The unavailable UI carries no selected
  root. Session/Tenant/Workspace authorization boundary callbacks also clear
  every Thread tab. Late responses cannot refill an earlier scope.
- Transport catch-up waits for pending protected requests, reauthorizes the
  Conversation, then reauthorizes the selected Thread. Memory-only intent is
  restored only within the same exact identity. Restored entries normalize
  sending to false while retaining any uncertain-send retry key.
- Tabs support ArrowLeft/ArrowRight, Home/End and Delete with roving focus;
  closing the active tab selects and reauthorizes its neighbor. Existing
  Back/Escape/Close and timeline focus restoration remain shared. IME composition
  and keyCode 229 prevent accidental Enter sends and Escape closure.
- English/Japanese labels explicitly state that drafts are memory-only, that
  closing a tab/leaving the Conversation discards its draft, and that server
  saving/cross-device synchronization is unavailable. Closing a tab sends no
  Message, Thread or Conversation delete request.

## Verification

Commands run from the **agent worktree frontend directory** so a dependency
junction does not accidentally select the baseline product workspace:

```powershell
node node_modules/@angular/cli/bin/ng.js test --watch=false --include='src/app/features/messaging/message-thread-context.spec.ts' --include='src/app/features/messaging/messaging-ui.spec.ts' --include='src/app/features/messaging/messaging-i18n.spec.ts' --include='src/app/features/messaging/messaging-thread-mapper.spec.ts'
node node_modules/@angular/cli/bin/ng.js build --configuration production
```

- Focused Angular/Vitest: **PASS**, 97 tests in 4 files: 44 Thread context
  (15 new workspace cases), 51 existing Messaging UI, 1 localization and 1
  thread-mapper suite. Coverage includes same-title distinct roots, fresh GET
  before draft restoration, stale loads, inactive send success/failure,
  close-during-send, active-load/send races, uncertain same-key retry during
  reconnect, Workspace mismatch, four protected-state boundaries, denial purge,
  keyboard tabs, loaded-root navigation, IME and existing route focus behavior.
- Production Angular build: **PASS**. Existing warning budgets remain visible;
  no threshold or dependency was changed. Initial bundle remains below the
  configured error budget.
- `git diff --check`: **PASS**.
- Hosted Required Checks, SonarQube Quality Gate/duplication, independent
  Coordinator review and exact-Main CI: **NOT_EXECUTED** by this agent.
- New browser width/theme/a11y/screenshot journeys and real-backend staging
  acceptance: **NOT_EXECUTED** by this agent; assigned to Integration QA.
  Native source PNG inspection is not a rendered-product visual comparison.

## Explicit limits and follow-up

The design's simultaneous two-column Thread panels, cross-Conversation Thread
catalog, custom topics/statuses, thread-level unread aggregates, watch/pin state,
new canonical File/Project/Task links and durable tab arrangement are not
implemented. The sidebar is the bounded current Conversation timeline, not a
server-wide or paginated Thread repository. Existing Conversation read semantics
are unchanged. Browser reload/navigation and tab closure discard local Thread
drafts; no server-saved/synchronized indicator is shown.

Cross-device persistent Drafts require the separate private Owner Decision
Packet and approval of its exact API/data/authorization/concurrency/RLS contracts.
Production Draft persistence, browser-restart restore and cross-device sync
remain **OWNER_DECISION_REQUIRED / NOT_EXECUTED**. Real-backend authorization,
revocation, SignalR reconnect and protected HTTPS qualification remain separate
execution gates; no mock result, local build or source inspection clears them.
