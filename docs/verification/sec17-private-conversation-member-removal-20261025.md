# SEC-17 — Private Conversation member removal across live SignalR sockets

Status: **DRAFT / REVIEW ONLY / UNVERIFIED current-source runtime / RELEASE NO_GO**. Owner: [#690](https://github.com/NYGsatoshi/Coglatas/issues/690).

## Scope and distinction

This test-only candidate adds `PrivateWorkspaceChannelMemberRemovalRevokesBothLiveAndFreshSocketAuthority`
to the existing PostgreSQL/SignalR integration fixture. It tests a distinct authority change
from held [#1326](https://github.com/NYGsatoshi/Coglatas/pull/1326):
**the private Conversation member is removed while their Workspace membership remains active**.
It neither merges nor modifies that held PR.

The test runs against a disposable migrated PostgreSQL database, the real ASP.NET Core
HTTP API, the Hub, durable Outbox dispatcher and authenticated WebSocket receivers.
All identities and messages are synthetic; no target deployment or live school data is involved.

## Required first-attempt controls

1. Create a fresh Workspace and a named private Workspace channel using real HTTP,
   then invite the existing synthetic Workspace member by the scoped channel API.
2. The owner and invited member receive the same real `Messaging.MessageCreated.v1`
   event while the member's protected HTTP history is readable. A positive recipient
   is mandatory: an empty Outbox or disconnected transport cannot produce a pass.
3. Remove that member through `DELETE /api/conversations/{id}/members/{userId}`.
   Confirm the persisted Conversation member has both removal timestamps, while the
   Workspace membership is still `Active`.
4. The existing member WebSocket must retain a valid Workspace subscription but must
   not regain the private Conversation subscription, read its body/thread, or receive
   a subsequent real private Message. The authorized owner must receive that Message.
5. A fresh authenticated WebSocket for the same member must independently retain
   Workspace subscription authority but fail private Conversation subscription.
   A new real private Message must reach the owner and neither member socket.
6. Emit only bounded SEC-ARCH recorder categories, authorization verdicts and
   delivered/not-delivered flags; no raw messages, group keys, cookies or identities.

The original backend authorization predicates, errors, retries, deadlines and quality
thresholds are not modified. A failing first attempt is a test failure, not a waiver
or evidence that the test may be discarded.

## Qualification and holds

- [ ] Inspect the exact test-only diff and its authorization/positive-delivery controls.
- [ ] Confirm a **selected real PostgreSQL** test execution and the named method's
      result in protected backend logs/TRX; a conditional skipped database test is insufficient.
- [ ] Confirm exact-head PR CI, scanner/publication checks and relevant independent review.
- [ ] Keep the resulting Main's tests and protected P0 evidence independent if an authorized
      future merge is considered.

This bounded candidate does not establish the full SEC-17 actor/event/role/reconnect
matrix, SEC-ARCH/RLS policy or mapping approval, recovery/rollback, authorized
HTTPS staging, physical IME/device reliability, the external 17-flow school acceptance,
or release readiness. **Owner HOLD BOTH and Student Council RELEASE NO_GO remain.**
