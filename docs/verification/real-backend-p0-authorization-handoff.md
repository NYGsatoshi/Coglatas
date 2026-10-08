# Real-backend P0 authorization fixture handoff

## Failure evidence

Security binding PR #1134 normally merged as
`035a54f404878223fe4796aa403553d044b201fa`. Its exact-Main run
[37824263258](https://github.com/NYGsatoshi/Coglatas/actions/runs/37824263258),
licensed job `113474547925`, failed the mandatory MVP0 journey while the other
seven legacy browser tests passed. At that failed revision, #1118 remained open
pending qualification; the final exact-Main result is recorded below.

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

Head `ff0cd7cc6c4370dbdafb71769113a5e5995763fe` passed required checks but
failed licensed dispatch
[37831905946](https://github.com/NYGsatoshi/Coglatas/actions/runs/37831905946),
job `113499119472`, waiting for Project-create and U-22 Workspace-create grant
frames. The other six legacy tests passed. Artifact `11573558535` has
GitHub-reported digest
`sha256:363c6a574a1af7c8624c3127f661e7fc7ea8340b2714967f6e38fc5559cb461b`.
Requiring receipt of every frame is stronger than the existing best-effort
delivery contract: a completed dispatch can have no connected recipient.
Those failed receipt barriers were removed; their commits and evidence remain.

## Scoped repair

Before revoking membership, preparation opens a temporary headless browser
using the existing synthetic API session in memory. It waits for a real Hub
connection, observes the exact actor/Workspace revocation frame, requires the
authorization HTTP refresh to succeed, and waits for the observer to reconnect.
Only then does the existing My Tasks isolation check finish and the fixture
return. The observer is closed in cleanup. Session material and raw frames are
never written to logs or files.

Preparation and Workspace-create, Project-create and U-22 archive cleanup now
also await completed dispatch of the current actor's authorization queue. The
read-only `/internal/browser-smoke/authorization-outbox` probe is mapped only
under the existing explicit Test-environment/browser-smoke fixture opt-in and
requires authentication, a synthetic account and a current tenant scope. It
queries only that tenant/actor's AuthorizationState events and returns one
`isSettled` boolean. It returns no IDs, payloads, routing, tokens or evidence.
Pending, processing, retry, dead-lettered and cancelled work cannot qualify the
handoff. Delivered work includes `NoAuthorizedRecipient`; that establishes
completed queue work, not browser receipt or authorization. Cleanup reloads
from fresh HTTP authorization after the queue settles. Existing HTTP scope
checks and first-attempt assertions remain. No general queue API was added.

Missing delivery, failed refresh or incomplete synchronization fails setup.
The matcher accepts the owned SignalR invocation/event schema and rejects
foreign users, Workspaces, scope types, changes, malformed traffic and unrelated
events. There is no fixed sleep, test retry, timeout increase, automatic command
replay, runtime authorization change, cancellation relaxation or required-check
change. Existing revocation tests remain required.

## Verification status

- Node syntax check: passed.
- Frame/runner behavior checks: 22 passed, zero failed/skipped.
- Complete existing P0 preflight Node suite: 74 passed, zero failed/skipped.
- Probe and existing fixture boundary checks: 25 passed, zero failed/skipped.
- Architecture checks: 10 passed, zero failed/skipped.
- Local InspectCode did not begin analysis: its invocation reported
  `Specify only one solution file`. Authoritative ReSharper CI remains required;
  no local inspection pass is claimed for the probe candidate.
- Initial six probe test failures were invalid tenant-write fixtures; corrected
  using the established platform-seeding pattern, preserving the write guard.
- Playwright lists all 11 desktop legacy tests; required P0 titles are unchanged.
- Candidate licensed browser proof and subsequent exact-Main CI: pending.
- Local licensed browser execution: unavailable because the protected license
  is absent. Mocked protocol checks do not establish real backend compatibility.

The original failure is retained. Dependency updater run `37824350885` separately
failed `security_update_not_found` for `braces`; this repair does not remediate or
dismiss dependency alerts. Numerical performance remains
`SUSPENDED / NOT_EVALUATED`, with #1128 deferred. No new Avalonia/ProjectIDE work
has started.
# Exact-Main qualification — 2026-10-08 UTC

PR #1135 merged normally at `a271599a9cca2f2d3c4ad1f99c4648f17a3b060f`,
with expected reviewed head `bb3af2914bbbe453bb73dda20b43f1595c871bba`.
Exact-Main [run 37838083775](https://github.com/NYGsatoshi/Coglatas/actions/runs/37838083775),
attempt 1, passed all six required contexts and applicable runtime/quality jobs.
Licensed acceptance passed the original eight mandatory P0 journeys without
retries/skips. Fast/full/extended Functional, deep Security, CodeQL and both full
Qodana lanes passed. Earlier failure evidence below is retained; earlier dated
pending statements describe the candidate history, superseded by this result.
The separate Dependabot updater failed and remains the #804 dependency workstream.
