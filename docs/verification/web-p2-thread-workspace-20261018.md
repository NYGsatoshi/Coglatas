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

## Independent browser findings and repair

Integration QA independently tested frozen integration candidate
`28484c4d32116d2622325ec0a5dd3886fb1b1f7b`, including original Thread commit
`5fac1230f83aebc9ada1c49c99e3072f67e1a6fd`, in the real built Angular application
with synthetic mock HTTP responses. Its first run executed 12 cases: **0 passed,
12 failed, 0 skipped**. Ten failed axe `aria-required-children`; two failed
protected-body removal after HTTP 403 at 320 and 1366 pixels. Literal `{number}`
also appeared in English/Japanese tab labels and close accessible names.

The initial failure artifacts remain in Integration QA's
`work/qa-initial-28484c4d/` directory. Evidence bindings supplied by QA and
independently read from its summary/log:

- Test-source SHA-256:
  `cef706b98debb35b2cac40ff3d421fcf694628ad48a6c5dc742cf54529bc6cab`.
- Initial run-log SHA-256:
  `3a184ff0146e89f7332dfb334a446bdf13b08d4de87ea85a59c2ee912293b0bc`.
- Initial report SHA-256:
  `e69db1d7a0b9e1393588b57f24a4664bc6217d2dd3801c0bd6f7930a9a710fb5`.

The tablist's presentation wrappers exposed ordinary sibling close buttons as
invalid children. The repair makes each direct tablist child a `role=tab`
button, moves the active-tab close control outside the tablist and retains
Delete-to-close with neighbor focus. No interactive control nests inside a tab.
Both language catalogs now use the existing I18nService `{{number}}` convention.
Mounted English/Japanese DOM regressions check tab ownership, localized numbers,
absence of literal braces, close operation and restored neighbor focus.

The actual `authSessionInterceptor` emits normalized `FrontendApiError.httpStatus`.
Thread GET, refresh and POST callbacks previously read only `error.status`, so
production HTTP denials reached the generic error branch while the initial
unit fixtures exposed raw `HttpErrorResponse`. These three Thread error paths
now use the existing shared `httpStatus(error)` helper. Authoritative GET
400/401/403/404 and POST 401/403/404 therefore reach protected-state purging.
POST 400 still preserves its draft/retry key and revalidates through GET;
network 0/503 retain same-key retry and never become false authorization denial.

Repair validation: **122/122 PASS** across five focused suites: 60 Thread
context, 51 Messaging UI, 9 existing auth-interceptor, 1 localization and 1
thread mapper. Fourteen added negative/retry cases run through the actual
`authSessionInterceptor` with HttpTestingController transport responses,
including initial/refresh GET 400/401/403/404, POST 401/403/404, POST 400 followed
by denied GET and network 0/503. The two added locale DOM cases cover the ARIA
and interpolation regressions. This remains mocked-transport unit evidence;
Integration QA must rerun its strict browser checks on the repaired integration
SHA. No original failure is removed or relabeled as passed. Real backend and
security release assurance remain unverified.

## Same-Tenant actor replacement boundary

A later independent review traced supported `AuthSessionFacade.refreshCurrentUser`
and `refreshSessionContext` responses that replace the actor while authentication
and Tenant remain unchanged. The previous Thread GET checked root/Conversation
and request generations, but could restore an old actor's cached draft. Thread
and main-composer dispatch also required a local actor boundary before using old
text. This is a frontend state-ownership issue, not a backend authorization bypass.

The selected-Workspace enclosing control was checked before classifying the
narrow failing case: the real `WorkspacesFacade`, `WorkspaceSelectionFacade` and
`RealtimeFacade` clear registry already clear protected state when an existing
Workspace selection is discarded. With two authorized Workspace choices and
none committed, that Workspace boundary does not fire. The DM route has no
Workspace-context guard. A synthetic HTTP/Angular regression using these actual
production services, actual AuthSession refresh HTTP responses and mutable
Signals confirmed the difference after their real observers ran. The clearer
was spied for observation only; its production implementation was not replaced.

Before the patch, the feature-local actor suite had eight failures and two
passing same-actor controls. The stronger enclosing test was also replayed with
the exact original `28484c4d32116d2622325ec0a5dd3886fb1b1f7b` Messaging blob
`8789aa891d4f7e45ffd7eee2d4b11fa545e7c918`: the no-selection case failed by
restoring the previous actor draft after a late GET, while the committed-Workspace
control passed and invoked the real Workspace clearer once. The AuthSession,
Workspaces, WorkspaceSelection and Realtime runtime blobs match that frozen
candidate and the later `701d155e217a79971a75e03e171dc13471a4b00f` source base.
Other cases were intentionally filtered during this two-case reproduction; no
test declaration was skipped or weakened.

Retained exact-byte validation artifacts are in the standalone security artifact
collection for this worktree:

- `artifacts/validation/actor-baseline-28484c4d.log`, SHA-256
  `7a71c8ec20dd9d56d42f11bcf06f167cd0d5c2bfeea41307ead491f4a54115ab`.
- `artifacts/validation/actor-boundary-executed.spec.ts`, SHA-256
  `527f7828655a61cd05d132e85709540b297f27c2af390fe96bb48158ac47ff83`.

The repair stays inside Messaging. A Tenant/actor identity snapshot immediately
redacts every public protected view when the authenticated identity changes.
An untracked Signals observer clears cached state, draft storage, route intent
and subscriptions. Explicit command and HTTP/realtime response checks perform
the same reset synchronously before Angular schedules the observer, so previous
actor text cannot be dispatched as the replacement actor or restored by a late
response. Cached Thread draft admission also rechecks the full current identity.
The identity snapshot advances only after protected state has been cleared.
Global AuthSession/Realtime/Workspace policies, backend contracts and storage
schemas are unchanged. Freshly authorized replacement-actor loads and ordinary
same-actor refreshes remain supported.

Final local validation: **184/184 PASS**, seven suites, with 73 Thread context,
51 Messaging UI, 9 auth interceptor, 27 Workspace facade, 22 Workspace selection,
1 localization and 1 mapper test. Thirteen added actor cases cover both supported
refresh methods, immediate view redaction, pre-observer send denial, late GET/POST,
cancellation, both real enclosing Workspace states, same-actor compatibility and
fresh authorization for the replacement actor. Production build **PASS** at
927.20 kB initial bundle; architecture check and diff check **PASS**. The temporary
baseline swap restored the exact final runtime bytes before the final build.
Real cookie/backend account replacement, full AppShell browser lifecycle and
exact-Main release qualification remain separate integration evidence; the
service-level test does not claim those executions occurred.

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
