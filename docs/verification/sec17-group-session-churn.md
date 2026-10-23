# SEC-17 bounded Group and session transport controls

This test-only slice extends the current Main contract for Issue #690. It does
not approve SEC-ARCH mappings, RLS activation, held private-channel changes in
PR #1326, held browser changes in PR #1320, or a pilot release. Issues #1342 and
#1346 and both Owner approval holds remain independent blockers.

## Scope

`GroupConversationSessionChurnPostgreSqlTests` uses the existing migrated,
disposable PostgreSQL fixture and actual compiled Web entry point. Login,
CSRF-protected HTTP mutations, SignalR negotiation and WebSocket delivery are
real. No authentication, Hub, dispatcher, membership or session service is
replaced. Each fact creates its own ordinary Workspace-scoped Groups through
the existing HTTP command. Group threads and optional role semantics are outside
this slice.

| Cell | Fact | Required observations |
| --- | --- | --- |
| SAFE-01 | `ThreeGroupLeaveAndReaddCyclesReauthorizeBothSameSessionSockets` | Exactly three ordinary participant self-leave / owner re-add cycles; two simultaneous sockets from that participant's session; actual committed authorization-event receipt; current subscription, body and send denial; fresh connection denial during each absence; owner business-message delivery paired with exclusion; restored HTTP history and participant-produced delivery to both sockets after every re-add. |
| SAFE-02 | `ConcurrentIndependentGroupRemovalsPreserveOnlyCurrentOppositeScopeDelivery` | Two independent HTTP removals start concurrently in different Groups. Both responses and committed authorization deliveries complete before checking the boundary. Both sockets per affected actor reject the removed Group and receive participant-produced messages in the opposite, still-readable Group; the owner receives both messages. |
| SAFE-03 | `HttpLogoutStopsEstablishedSessionGroupDeliveryAndFreshLoginRestoresIt` | Two established sockets receive an initial Group business event. Actual HTTP logout returns 200; auth/me and negotiation reject the logged-out session with 401. A new owner-produced event reaches the owner but neither retained socket. A fresh eligible login receives a subsequent business event while both old sockets remain excluded. |

Group membership invalidation removes all affected-user subscriptions, including
otherwise readable scopes. SAFE-02 explicitly re-subscribes only to the remaining
authorized scopes before requiring positive delivery. It does not interpret an
unsubscribed socket's silence as isolation. SAFE-03 tests one revoked session,
not an account-wide logout contract. It does not invoke a revoked socket before
the business-event check, which would independently abort that connection.

## Evidence and limits

Every negative business-delivery observation is paired with a real authorized
receiver and the persisted Outbox event's delivered state. Membership controls
also require exact `AccessDenied` subscription results, HTTP 400 read/send
denials and protected-body non-disclosure; HTTP 500 is never denial evidence.
SAFE-03 passively observes each revoked socket for the existing 15-second delivery
horizon with 50-millisecond polling after each business-event positive. The
receiver task must remain active and its WebSocket open throughout; any target
arrival or transport failure fails the observation. No invocation or deliberate
abort of those sockets precedes or occurs during this check. This is a bounded
observation, not proof of perpetual exclusion or linearizability. The additional
`PassiveExclusionRejectsAlreadyDeliveredEventsAndDisconnectedReceivers` fact
requires the real helper to fail on a business event already received by a live
socket and on a previously connected, explicitly disconnected socket. The live
event control also requires a successful invocation after the expected failure.

Assertions do not print message bodies, credentials, cookies or transport tokens.
The existing fixture's delivery/invocation deadlines remain unchanged. There
are no added retries, randomized repetitions or load claims.

This is bounded post-commit reauthorization, not pre-commit linearizability,
exhaustive race coverage, private-channel invitation qualification, capacity,
browser rendering, HTTPS staging or formal SEC-ARCH approval. Repeated delivery
catalogue matrices, DB-only session invalidation, Group HTTP lifecycle and
logout/negotiate controls already exist and remain separate.

Runtime qualification must record the exact committed source, a fresh Release
build, the canonical and test-loaded assembly hashes and PE source versions,
the selected four fact names and TRX totals (four executed/passed; zero failed
or skipped), PostgreSQL version and effective diagnostic-privacy settings,
loopback-only disposable target, and owned cleanup. Source configuration alone
does not establish execution. Initial diagnostic failures must remain distinct
from later candidate evidence. Main and hosted qualification remain separate
from local exact-source execution.
