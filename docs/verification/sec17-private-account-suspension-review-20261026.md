# SEC-17 private-channel account suspension: review-only control

## Scope and integration restriction

This isolated test candidate starts from public Main
`03dcbbfde03dd3364e637ee8fee92d2545b5e7c1`. It adds one representative
real ASP.NET Core, PostgreSQL, Outbox and SignalR control for the existing
active-account requirement in `UserSessionService.ValidateSessionAsync`.
It changes no production authorizer, runtime policy, RLS, migration or activation.

This is preparation for a separately reviewed integration into held PR #1399,
preserving that branch's history and Owner hold. It is not a new merge route
around held #1399/#1326 or review-only #1406, and must not be merged into Main independently
under this test-preparation authorization. No held candidate code was copied
into this branch. Those held and review-only candidates cover other transitions: private
invitation/workspace revocation, private participant removal, and Thread/reply
replay after participant removal. They are not superseded by this control.

## Falsifiable boundary

`SuspendedAccountWithUnchangedSessionAndMembershipCannotReceivePrivateChannelDeliveryReplayOrReconnect`
creates a private channel and invites the synthetic member through current HTTP
routes. It drains the resulting authorization-invalidation Outbox events before
installing the baseline subscriptions, following the existing canonical channel
fixture pattern. Persisted conversation Type, Private visibility, Tenant and Workspace identity
are asserted before baseline delivery. Both owner and member then receive an actual business-produced
message, and the member reads its message history and Thread over HTTP.

The only authority mutation is a committed synthetic `User.Status = Suspended`.
The persisted User state is asserted Active before the SQL change and Suspended
immediately afterward, before observing exclusion.
The test repeatedly compares persisted session identity/expiry/revocation and
Tenant, Workspace and private conversation membership snapshots. Sessions remain
unrevoked and unexpired, and those memberships stay active/readable/postable.
The current owner receives a new business event and its Outbox row reaches
Delivered, while the original member socket stays open throughout the existing
bounded passive exclusion observation. No new Hub invocation is issued that
could abort that socket before the passive check.

Repository replay of that same denied event must increment the owner's observed
delivery count and reach a fresh Delivered timestamp. The established member
must still receive no copy. This is repository/dispatcher replay, not acceptance
of the separately authorized operator replay service. A retained-cookie WebSocket
upgrade and message/Thread HTTP reads must return 401 without protected body data.
Each HTTP route uses a fresh client with automatic cookie handling disabled and
the same frozen pre-denial Cookie header, so both routes independently submit
the retained credential. The cookie value is never logged. The authorized owner retains readable history.

## Actual local evidence and retained failures

The current-source build used SDK `10.0.403` and .NET package `10.0.14`, with
zero warnings and errors. Real PostgreSQL reported `server_version_num=180006`,
using an independently owned disposable `postgres:18-alpine` container at digest
`sha256:77f585114c32fbca283dc835b0596f4e52b51b4c6662d7810b2f4084f60a1873`.
The final changed-source test executed **1/1 PASS, 0 skipped**, in **48.7625918s**.
TRX SHA-256:
`8d475602a29b78ba76b07da2e364164598bd4bdbe44f3683aab9e37b5eef5688`.

The earlier `f47d6b12` fixture passed 1/1 in 58.0830878s, but its HTTP reads
shared a cookie jar: the first 401 could expire the cookie before the Thread
read. That source established socket/replay isolation and the first current
HTTP denial, but did not independently establish retained-credential admission
for each HTTP route. The new source closes that evidence gap without changing
production behavior. That earlier PASS and its scope limit remain recorded.

The earlier fixture source also passed 1/1 in 48.0083448s before the additional
persisted-scope and explicit User-transition assertions were added. Its PASS
remains bound to that earlier source. Two initial test-source failures remain
evidence:

- Initial fixture: **1 FAIL**, 18.6758665s, the second authorized POST returned
  400 after the first positive controls passed. Its repeated identical body
  matched the existing 15-second duplicate-post guard. The response detail was
  not captured, so attribution to that guard is a source-supported hypothesis,
  not a retrospective proof. The helper now uses distinct synthetic bodies.
- Unique-body fixture: **1 FAIL**, 31.5680444s, initial positive delivery failed
  before account suspension. Pending setup invalidation can remove newly
  installed subscriptions. The final fixture fences those actual terminal
  events before subscriptions; it does not sleep, retry tests or change the
  runtime contract. This correction and the subsequent pass support the fixture
  mechanism without proving the exact ordering of the old failure.

An earlier build exited before compilation because the SDK's default CLI-home
sentinel path was read-only. A task-specific writable `DOTNET_CLI_HOME` corrected
that infrastructure issue. No global home, shared database or daemon setting
was changed. These initial outcomes are not reclassified as passes.

## Limits

This is one synthetic, nonconcurrent account-status transition. It does not
qualify every SEC-17 event, public admission or account-administration workflow,
concurrent authorization race, private specification mapping, complete RLS,
operator replay, PR CI, resulting Main or release acceptance. It is a local
review-only candidate; no formal Owner approval is inferred. Student Council
release remains **NO_GO** and all other Owner holds remain in force.
