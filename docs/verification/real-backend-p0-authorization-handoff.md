# Real-backend P0 authorization fixture handoff

## Failure evidence

Security binding PR #1134 normally merged as
`035a54f404878223fe4796aa403553d044b201fa`. Its exact-Main run
[37824263258](https://github.com/NYGsatoshi/Coglatas/actions/runs/37824263258),
licensed job `113474547925`, failed the mandatory MVP0 journey while the other
seven legacy browser tests passed. #1118 remains open pending qualification.

Artifact `11571231550` has SHA-256
`83f4e101660b79b0eb14c2bb37364ac248e63d20d73408ef75cad52468f4ce99`.
The trace records the first DM POST at `18:32:09.182Z`, aborted with
`net::ERR_ABORTED`, followed by `/api/auth/status`, a new Hub negotiation and
authoritative conversation reads. There was no HTTP response for the waiter;
the test expired at its unchanged 120-second limit. The final DOM retains the
draft and displays a connected, empty conversation.

The legacy preparation removes the synthetic actor's secondary Workspace
membership, then checks My Tasks by HTTP. `WorkspaceService.RemoveMemberAsync`
stages `Security.AuthorizationStateChanged.v1` with that actor, Workspace and
`revoked` change; HTTP completion does not wait for asynchronous outbox delivery.
The frontend immediately cancels protected requests when such a control frame
arrives. Direct-conversation creation does not publish an authorization event.
This source/network evidence identifies a fixture handoff race; the trace does
not itself include the control-frame payload.

The first repair head `2f38b83550ab68d94c310b0680e3dc93d71494f0` passed all
eight mandatory legacy tests in licensed dispatch
[37827115061](https://github.com/NYGsatoshi/Coglatas/actions/runs/37827115061).
After wiring the helper checks into the existing preflight, exact candidate
`d755cab13b17e8931d8219d0fe0e1a4a0047c2b1` passed required PR checks but failed
licensed dispatch
[37827256997](https://github.com/NYGsatoshi/Coglatas/actions/runs/37827256997),
job `113485342220`, in the U-22 Workspace-create journey. The fixture observer
succeeded and the original DM journey passed; the earlier pass does not qualify
this later head.

Artifact `11571843579` has SHA-256
`409311a0a67c710e3ffa0bee0aea4114116b80e644bef7412966a71e41e6043f`.
The U-22 trace shows no Workspace-create POST, an authorization refresh at
`18:59:02.577Z`, renewed Hub negotiation and Workspace reads. The dialog retains
its draft without a command response. The preceding Project-create test archives
its Project and previously awaited only the scoped HTTP list. Project archive
stages an actor-specific `project` / `archived` authorization event. This is
consistent with another asynchronous handoff race; the trace does not prove
which control payload caused that refresh.

Head `28cfc6f8e84de6da8b9c525ba4199248176fe339` passed required checks but
failed licensed dispatch
[37830347914](https://github.com/NYGsatoshi/Coglatas/actions/runs/37830347914),
job `113493770163`: the Workspace-create and Project-create journeys completed,
but their new cleanup waits did not observe the archive frame. Six other legacy
tests, including U-22 and the original DM journey, passed. Artifact `11572654801`
has SHA-256
`cb442d896412393159b5bb873418a0c3f51674c86f98847512ea5b9f4c0ef082`.

The Workspace trace records creation at `19:18:22.498Z`, archive at
`19:18:22.792Z`, then the first authorization refresh at `19:18:25.672Z`.
The Project trace likewise records creation at `19:18:55.981Z`, archive at
`19:18:57.094Z`, then refresh at `19:19:00.717Z`. Both creations publish a
`granted` authorization event. `OutboxDispatcher` sends a control event before
removing that user's old subscriptions. These transitions can enter the same
outbox batch, leaving no authorized subscription for the following archive
frame until reconnect. Waiting only after archive was therefore insufficient.

## Scoped repair

Before revoking membership, preparation opens a temporary headless browser
using the existing synthetic API session in memory. It waits for a real Hub
connection, observes the exact actor/Workspace revocation frame, requires the
authorization HTTP refresh to succeed, and waits for the observer to reconnect.
Only then does the existing My Tasks isolation check finish and the fixture
return. The observer is closed in cleanup. Session material and raw frames are
never written to logs or files.

Workspace-create, Project-create and U-22 cleanup now also register the observer
before login/navigation. Each successful archive must deliver the exact actor,
scope type, resource and `archived` change, refresh authorization successfully,
and reconnect before the shared account is handed to the next test. The observer
tracks the current expected mutation and a delivery boolean across Hub
reconnects. Creation now also awaits its exact actor/scope/new-resource `granted`
frame, successful authorization refresh and reconnection before proceeding.
Because a grant frame can precede the HTTP response assigning its new ID, the
observer retains at most 16 records of the four bounded safe identity/change
fields; it discards raw frames and unrestricted payload fields. Archive waits
still require fresh delivery after the cleanup starts. Existing HTTP scope
checks and first-attempt assertions remain.

Missing delivery, failed refresh or incomplete synchronization fails setup.
The matcher accepts the owned SignalR invocation/event schema and rejects
foreign users, Workspaces, scope types, changes, malformed traffic and unrelated
events. There is no fixed sleep, test retry, timeout increase, automatic command
replay, runtime authorization change, cancellation relaxation or required-check
change. Existing revocation tests remain required.

## Verification status

- Node syntax check: passed.
- Frame/runner behavior checks: 25 passed, zero failed/skipped.
- Complete existing P0 preflight Node suite: 77 passed, zero failed/skipped.
- Playwright lists all 11 desktop legacy tests; required P0 titles are unchanged.
- Candidate licensed browser proof and subsequent exact-Main CI: pending.
- Local licensed browser execution: unavailable because the protected license
  is absent. Mocked protocol checks do not establish real backend compatibility.

The original failure is retained. Dependency updater run `37824350885` separately
failed `security_update_not_found` for `braces`; this repair does not remediate or
dismiss dependency alerts. Numerical performance remains
`SUSPENDED / NOT_EVALUATED`, with #1128 deferred. No new Avalonia/ProjectIDE work
has started.
