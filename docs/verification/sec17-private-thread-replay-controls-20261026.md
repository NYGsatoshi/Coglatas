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

## Local execution record

The initial uncommitted test source, based on Main above and test-file SHA-256
`f000a88d70b5dd53f23160ef34cdfedac60037c716f68b8eb8b7875fded43bb0`,
built without warnings/errors. Its first focused real run executed two methods:
the existing canonical transport method PASS; the new private method FAIL at its
positive HTTP assertion, before removal. The test incorrectly asked the root
Conversation history helper to find a reply ID; root history intentionally omits
replies. The corrected test separately checks root history and the anchored
Thread DTO's replies. This is a test-implementation failure, not evidence of a
product defect, and remains retained. No retry converted that source into PASS.

The correction also adds explicit denied-write persistence controls and positive
baseline delivery counts after independent coordinator review. Its intermediate
build found one xUnit collection-assertion diagnostic warning. A subsequent small
source change keeps the protected DTO out of failure diagnostics; the final
build has zero warnings/errors. An initial CI-routing probe had a nonexistent
temporary directory and fell back to full routing; it supplies no selection
credit. The corrected probe selects `FullyQualifiedName~Coglatas.Tests.PostgreSql`.

Corrected test-bearing commit `e4fbed38400794ac1d489e0b5b327612d2e069d8`
(test-file SHA-256
`2ce6d84da78e151a2c58ab351c965cf0cad89668513b12fbb3140b29eda9704e`)
built with pinned SDK 10.0.402 and zero warnings/errors. Its first focused
execution against a uniquely scoped disposable PostgreSQL **18.6** container
passed both selected methods: the new private control in 20.57 seconds and the
existing canonical control in 14.29 seconds. TRX independently reports 2 total /
2 executed / 2 passed / 0 failed / 0 not-executed. No retry, filtering change,
conditional skip or deadline change supplied that result.

The command selected
`--filter FullyQualifiedName~WorkspaceChannelTransportPostgreSqlTests` on the
Release build with `--no-build --no-restore --disable-build-servers -m:1`, an
explicit disposable `POSTGRES_TEST_CONNECTION_STRING`, and
`COGLATAS_TEST_USE_MIGRATED_TEMPLATE=true`. The fixture creates a migrated
isolated database per fact. No development or school database was used.

TRX SHA-256:
`727ab20f9dd2e8b7b8c5cf4e61fb60ece6fac1d298da446af2a30b63879c6e6d`.
The repository verifier passed with `--minimum-total 2 --label` after an initial
invocation omitted the `--label` flag and was rejected; that invocation supplies
no validation credit. The private live-control artifact has eight explicit
observations, matching test-source digest and six assembly bindings; artifact
SHA-256 `0b60b900aaa071790972fbd85bfa8957f1b508247613507554b35564df82c1f3`.
It explicitly retains `contractCompletion: UNVERIFIED` and `ownerApproval: null`.

Loaded build output SHA-256 values:

- `Coglatas.Tests.dll`:
  `704fd2f06288f9b08aafbe1035cb755859b00a2089790c194803b2a2a69d6510`.
- `Coglatas.Web.dll`:
  `da568ec9746cc5687f5b15e25af484f27f9a399d6083f9d808efd9f2496a4567`.

Raw TRX and private runtime records remain outside the repository. This
subsequent documentation commit preserves the executed source bytes; it does
not imply a new runtime execution. Hosted exact-head CI, final independent
review and any Main qualification remain separate requirements. Student Council
status remains RELEASE_NO_GO.
