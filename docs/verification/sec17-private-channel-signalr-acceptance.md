# SEC-17 — Private Workspace channel real SignalR boundary

Status: **Ready for review / unmerged — bounded SEC-17 P0 PASS; release NO_GO**. Owner: #690.

## Review scope and existing Hub inventory

The product's current `/hubs/app` Hub exposes server-authorized
`SubscribeUser`, `SubscribeTenant`, `SubscribeWorkspace`,
`SubscribeProject` and `SubscribeConversation` methods, plus matching
Workspace/Project/Conversation unsubscriptions. It does **not** expose a
client-selectable arbitrary group subscription.

This candidate extends
`tests/Coglatas.Tests/PostgreSql/WorkspaceChannelTransportPostgreSqlTests.cs`
and reuses the real migrated-PostgreSQL/Test-Web/SignalR/WebSocket fixture,
the actual subscription authorizer, outbox dispatcher and metadata-only
SEC-ARCH control recorder. Existing broader SEC-ARCH event-catalog and
cross-Tenant matrix tests are preserved, not duplicated.

## Exact new blocking sequence

1. Alpha owner creates a **named private** Workspace channel via real
   CSRF-authenticated HTTP. Alpha member is already a Workspace participant,
   but has not been invited into the new private Conversation.
2. Separate owner/member/Beta WebSockets connect to the real Hub. Owner
   subscribes, member's **Workspace** subscription succeeds, but member
   and Beta cannot subscribe to the private Conversation.
3. Actual owner Message POST produces a persisted Outbox event. The owner
   receives it, and neither excluded socket does. Zero-event/transport
   failure cannot pass because a positive event must be observed.
4. Real scoped channel-member invitation allows an authenticated member
   to subscribe and read protected history. A subsequent real Message
   is delivered to both owner and member, not Beta.
5. Owner removes member's Workspace membership through the real HTTP
   endpoint. The stored Conversation participant remains readable but
   current authorization denies history/subscription and real event delivery
   to the existing member WebSocket.
6. A **fresh** WebSocket for that authenticated Tenant member also cannot
   subscribe or receive the subsequent private Message. Positive owner
   delivery is mandatory at every exclusion boundary.

Tests fail closed on server errors, missing responses, missing positive
event receipt and incorrect subscription decisions. Inputs and events are
synthetic; no public/production Hub, credentials, school users, protected
message bodies or routing groups are added to artifacts.

## Evidence and remaining exclusions

- Real migrated-PostgreSQL/WebSocket test PASS in [PR CI #39913980015](https://github.com/NYGsatoshi/Coglatas/actions/runs/39913980015)
  on `827d685315f3ec7c070ec2da4692cdc6888fabef`: the new
  `PrivateChannelInvitationAndWorkspaceRevocationIsolateRealSignalRDelivery`
  executed in 20 seconds and passed. The backend suite passed **438/438** and
  the TRX verifier passed. Sanitized SEC-ARCH execution evidence was uploaded.
  This qualifies the bounded test at that code SHA, **not** every SEC-17
  acceptance criterion or a subsequent documentation-only HEAD.
- Independent SEC-ARCH Owner/spec/RLS approval remains held.
- SEC-16 signed/download grant lifecycle, broad SignalR catalogue and
  retained subscription churn, production TLS/origin, physical IME and
  operational rollback require separate evidence.
- No endpoint, authorization policy, database schema/migration,
  CI gate threshold or deployment change is part of this test-only PR.

Result: **Ready for review / unmerged / RELEASE NO_GO**.
