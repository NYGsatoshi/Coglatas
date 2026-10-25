# SEC-17 private Thread replay controls

Status: REVIEW_ONLY / OWNER_HOLD. This test candidate does not qualify the
Student Council release, close #690, or authorize security-policy activation.

## Bounded gap

Main `f31b15f2b42d3b83b5c1ffae5ea304922e10cc16` has real canonical Workspace
channel Thread/reply controls, including Workspace suspension and repository
replay. Held #1399 adds private Conversation-only member removal while retaining
Workspace membership, but its bounded event is a new root Message. Held #1326
covers private invitation and Workspace revocation. Neither candidate establishes
private Thread hint/reply replay after Conversation-only removal.

The new `PrivateChannelThreadReplyAndReplayRequireCurrentConversationMembership`
fact uses the current Test Web entry point, migrated disposable PostgreSQL,
business-produced durable events, the real dispatcher, and authenticated live
WebSockets. It does not replace authentication, authorization, Hub or Outbox
services.

## Assertions

- Create a private Workspace channel and invite a current Workspace member using
  real scoped HTTP commands. Require actual Message and Thread hint delivery to
  the owner and member before removal, and successful HTTP history/Thread reads.
- Remove only Conversation membership through HTTP. Require persisted removal
  markers, retained active Workspace membership, permitted Workspace subscription,
  denied Conversation subscription, denied history/Thread read and denied reply.
- Require a new owner's reply and Thread hint to reach the authorized owner;
  exclude the removed member and a connected foreign-Tenant peer.
- Reconnect the removed member and require current private subscription denial.
  Replay the earlier business-produced reply and Thread hint through the existing
  repository/dispatcher path. Require additional owner deliveries, unchanged old
  member delivery counts, and no delivery to fresh or foreign peers.
- A fresh real invitation restores current history/subscription authority and
  additional delivery of both replayed events to the member.

Each exclusion follows delivered Outbox state, an observed authorized recipient,
and a same-socket RPC barrier proving the excluded transport is still live.
Replay is the repository path, not qualification of the operator replay API.
Authorization invalidations settle before observing each resulting authority;
this is not a concurrent revocation-versus-dispatch race test.

## Source-bound existing held evidence

Refreshed from GitHub on 2026-10-26 JST; all holds remain in force:

| Candidate | Current head | PR CI / attempt | Actual tested merge | Bounded result |
| --- | --- | --- | --- | --- |
| #1399 | `758d722aa56fe724a498dbb47a2a2ba09a12fb2d` | `40166770523 / 1` | `531c38875aa001de34e37dd8a3bf687121db460b` | Named private member-removal fact PASS (25 s), scoped backend 467/467, backend TRX verification PASS |
| #1326 | `2bdce7581349037d9a96ba784239515d70773332` | `40179000994 / 1` | `6a43278944fd9d739b2a6527337fa74c459e0056` | Named private invitation/Workspace-revocation fact PASS (14 s), scoped backend 467/467, backend TRX verification PASS |

Independent backend job logs `121089651032` and `121126267547` identify those
checkout merges, named passed methods and PostgreSQL 18.6 startup. These are
separate sources and do not provide runtime credit to this new candidate or Main.
PR security-scan success includes route-selected checks and is not complete deep
security execution; optional configured skips remain skips. Earlier first-attempt
failures remain immutable, including #1399's earlier Composer failure.

## Limits

No product source, migration, RLS policy, role/grant, security contract, deployment
or approved target changes. Current authority is exercised after committed
membership changes; comprehensive SEC-17 event/role/session/capability/concurrency
coverage remains incomplete. Formal SEC-ARCH/RLS approval and release/target
Owner decisions remain outstanding. No raw frames, session/resource identifiers,
credentials, private specification text or school data are published.

Local execution and exact candidate source attribution are recorded after the
focused run. Hosted exact-head CI, independent review and any Main qualification
remain separate requirements. Student Council status remains RELEASE_NO_GO.
