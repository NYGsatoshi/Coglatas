# Pilot SEC-16 / SEC-17 regression integration — 2026-10-24

Status: **IMPLEMENTED IN REVIEW BRANCH / CURRENT-HEAD CI UNVERIFIED / RELEASE NO_GO**.

## Source and change boundary

Base Product Main: `48cf0b397122fb3cadcf18926a1891e7bbf80a5d`, the normal
merge of #1380. This integration retains the exact original blobs and
Git commit ancestry from review candidates #1368, #1370 and #1371.
No production source, API, schema, migration, role, grant, policy, RLS, build
workflow, security budget or authorization threshold was changed.

| Source PR | Integrated named fact | Original test source |
| --- | --- | --- |
| [#1368](https://github.com/NYGsatoshi/Coglatas/pull/1368) | `ActiveDirectSelfLeavePersistsAndDeniesSameSessionHttpAndEstablishedAndReconnectingTransport` | `tests/Coglatas.Tests/PostgreSql/DirectConversationSelfLeaveTransportPostgreSqlTests.cs` |
| [#1370](https://github.com/NYGsatoshi/Coglatas/pull/1370) | `PersistedWorkspaceFileEligibilityReauthorizesRetainedUploaderAndOwnerCredentials` | `tests/Coglatas.Tests/PostgreSql/FileEligibilityRetainedCredentialHostedHttpTests.cs` |
| [#1371](https://github.com/NYGsatoshi/Coglatas/pull/1371) | `WorkspaceFolderPlacementPreservesRetainedGrantsAndRejectsStaleAndOtherWorkspaceDestinations` | `tests/Coglatas.Tests/PostgreSql/FilePlacementRetainedCredentialHostedHttpTests.cs` |

Each named fact was previously reported as passing in its **own prior-source**
hosted PR CI. Those results and past local controls do not qualify this
combined source, current Main, or every scenario under SEC-16/17. The existing
#1371 placement document retains a historical preparation-only checkpoint.
This document is the additive current-source coordination record.

## Current-source verification required

- [ ] Independently compare the three integrated test blobs and placement
      document with their original PR HEAD contents and verify ancestry.
- [ ] Run actual ASP.NET Core HTTP / PostgreSQL / established SignalR tests on
      this candidate; each of the three named facts must execute and PASS with
      nonzero duration. No skipped/not-executed facts qualify.
- [ ] Reconcile the actual test-merge SHA, application build producer,
      relevant source/test assemblies, runner PostgreSQL version, individual
      result IDs and first attempts.
- [ ] Run the ordinary PR CI, security, quality and architecture checks and
      inspect their internal skipped/retry/failed cases, not only the workflow
      conclusion. Preserve failed source/run/attempt evidence.
- [ ] Obtain independent current-source code and execution review. Only after
      the applicable current-state policy permits, perform a normal Merge and
      separately qualify resulting Main.

## Release and security boundaries

The Direct self-leave fact verifies current HTTP and realtime boundaries in
one existing synthetic Tenant/Workspace flow. It is not the complete event,
role/scope, revoke/reconnect matrix under
[SEC-17 #690](https://github.com/NYGsatoshi/Coglatas/issues/690).

The two File facts exercise current eligibility and logical folder placement,
retained FileObject/Attachment credentials, positive byte access and bounded
denials. They do not establish the complete actor/role/route/sharing/selection/
historical-content matrix, nor authorize a new public signed-URL or scanning
contract. Other SEC-16 evidence remains separate.

Do not lift either Owner HOLD (approved staging SSH/stable HTTPS target, or
exact formal SEC-ARCH mapping / SQL role/grant/policy / RLS activation).
The held #1320/#1326/#1361, diagnostic #1378, formal backup strategy #1379,
current Main reliability #1360/#1365, strict restore and rollback, physical IME,
school/privacy/operator approvals and external HTTPS 17-flow gate remain
independent. **No student account creation, target access, deployment, RLS
activation or public GO follows from this PR.**
