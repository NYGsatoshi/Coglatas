# Messaging catch-up completion coordination

Status: **IMPLEMENTED_NOT_VERIFIED** for hosted qualification. This is a bounded
production correction with deterministic Angular controls, not proof of the
initiating cause of a hosted failure. Student Council remains **RELEASE_NO_GO**.

## Retained Main failure

Base Main `0149d93d7c1e98613639ca81046b14c6ece322f5`, run `40214273861/1`,
failed the Fast Collaboration `FUNC-MSG-001 / MSG-THREAD-DENIAL` initial
authorized root assertion before restricted-cookie replacement. The bounded
receipt contains two overlapping HTTP 200 completions and positive DTO
consistency flags, followed by absent Thread UI. These observations do not prove
Angular applied either response or establish the request initiator. The same
source's Full positive is separate evidence and does not waive the failure.
Historical #1365/#1342 and the earlier Main f31 Extended failure remain open.

## Reachable caller boundary and demonstrated mechanism

`RealtimeFacade.connectAndSynchronize()` coordinates its transport-start
promise, but `runAuthoritativeHttpCatchUps()` has an independent runner and
attempt map. Production callers include Workspace-selection rehydration and
`WorkspacesFacade.completeHttpFallbackRecovery()` after an authorized HTTP
fallback. A real RealtimeFacade registry/runner control, with a fake transport,
requires one feature callback to be entered twice concurrently by transport
synchronization and public HTTP recovery. This establishes the local caller
overlap capability; it is not a real WebSocket or hosted-origin trace.

Messaging already waits for protected HTTP requests before routine reload.
That wait does not cover the entire reload lifecycle. After the last HTTP
subscription completes, the first asynchronous continuation still has to restore
its captured session-only Thread intent. A second callback entering in that
completion gap sees no protected requests and empty tab state, starts another
generation, and prevents the first continuation from restoring its captured
tabs. The second reload then has no tabs to restore and issues no fresh Thread
GET. A deterministic control on unchanged production Main reproduces this gap.
Overlap while the earlier HTTP requests are still pending instead succeeds.

## Focused correction and security boundaries

Keep only the active local catch-up completion, Conversation identity and request
generation. A second callback in that same generation waits for the whole
earlier reload/restoration lifecycle. It then passes the existing current-actor,
generation and Conversation guards and protected-request wait before starting
its own fresh aggregate read. Restored Thread bodies still require their own
fresh authorized Thread GET; no old response supplies current authority.

Identity-checked finalization cannot remove a replacement operation. Hard actor,
Tenant, Workspace, Conversation/route and authorization boundaries retain their
existing synchronous clear/cancel behavior and advance the request generation.
A callback queued before such a boundary cannot restore or reload another
scope. A current aggregate denial still clears protected state and prevents a
queued restoration. Main and Thread sending remain unavailable while the
aggregate's authority is pending.

This changes only Messaging-local routine coordination. Global Realtime
authorization, server contracts, subscriptions, RLS, migrations and deployment
remain unchanged. The held Composer node/input changes and held diagnostic code
are excluded. No existing native owner action, fresh-request requirement,
assertion, deadline, retry, Japanese IME behavior or required gate is relaxed.

## Source-bound local controls

- Initial test-only RED `7805363b239969a158f65ad21b3f0e1906d6da82`:
  **85 PASS / 1 FAIL**, with missing tab `root-a` in the completion-gap case.
  Pending-HTTP overlap and Workspace-boundary negative controls pass.
- Stronger test-only RED `18a44f7de0d7d3c4d64e1725993d6d05f01be778`:
  **143 PASS / 1 FAIL** across the Thread and real Realtime runner files. The
  gap lacks the required first restoration GET. This source additionally binds
  actual runner overlap, hard boundaries, actor/route replacement and current
  denial/no-send controls. No product correction is present at either RED.
- With the 17-line production delta, the identical stronger control files pass
  **144/144** (92 Thread and 52 Realtime). A broader relevant selection passes
  **524/524 in 28 files**, including existing Messaging security, draft, read,
  navigation, scope, Workspace and synthetic keyboard/composition controls.

Tests use clean lock-matching frontend dependencies, Vitest 5.0.5, no skipped
cases and no retries. The broader selection retains existing Vite and
focusability warnings. The positive control checks both fresh aggregate and
Thread reads, pending-body withholding and same-scope draft restoration. Its
cleanup drains observed HTTP work before asserting exact request counts, so an
expected RED does not cascade through teardown failures.

```sh
npm --prefix frontend test -- --include=src/app/features/messaging/message-thread-context.spec.ts --include=src/app/core/realtime/realtime.facade.spec.ts
npm --prefix frontend test -- --include='src/app/features/messaging/**/*.spec.ts' --include='src/app/core/realtime/**/*.spec.ts' --include=src/app/core/workspace/workspace-selection.facade.spec.ts --include=src/app/features/workspaces/workspaces.facade.spec.ts
```

These are Angular scheduling/HTTP controls with a fake transport. The exact
canonical real-backend owner, hosted source/build reconciliation, independent
review and resulting Main qualification remain separate. The observed hosted
two-200 pattern is compatible with multiple mechanisms; this controlled repair
does not retrospectively prove a shared cause or close historical reliability,
security, physical-device or release gates. All Owner holds remain in force.
