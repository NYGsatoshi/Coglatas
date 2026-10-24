# Pilot SEC-16 / SEC-17 regression integration — 2026-10-24

Status: **NEW TEST-ONLY QODANA PREVENTION REVISION / EXACT-HEAD CI PENDING / RELEASE NO_GO**.

## Latest integration and source-byte amendment

Base Main is now `4c4720f77226fa496136a79c17b0b4c3ac948669`, after
the ordinary Qodana property-pattern repair #1382. The earlier combined
`04f436bb26de7cc916612d9696bc6a568c6d1eaa` retains exact source
blobs from #1368/#1370/#1371 and the prior source-specific hosted evidence.
**The current candidate changes two of those test blobs further**, solely
to group existing File/Folder property assertions into C# patterns before
full Main Qodana acceptance. The original three parent commit identities
and Direct self-leave test remain intact; historical exact-blob claims
apply **only to the earlier 04f candidate**, not to this revised HEAD.

The File eligibility assertion still requires Workspace owner type,
matching owner/workspace IDs, Skipped scan, undeleted Attachment/File,
the exact File ID and the existing variable expected status. The folder
test still requires root placement/version, active private File parent,
current Owner membership and same Tenant, and matching folder/Workspace
versions. Grouping these predicates must never weaken any test, role,
authorization or negative assertion.

This change is a **proactive static quality correction** based on prior
`MergeIntoPattern` regressions. It does not establish an analyzer finding
on the new test files, a Qodana pass, or full test equivalence until the
successor's exact-source CI/independent review is completed. Prior passes
on earlier source SHAs must not be carried to the successor.


## Source and change boundary

Original integration was based on Product Main `48cf0b397122fb3cadcf18926a1891e7bbf80a5d`,
normal merge #1380. The current branch incorporates Main `4c4720...`
and retains the original candidate ancestry from #1368/#1370/#1371,
with the two narrow quality amendments described above.
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

- [ ] Independently check the Direct self-leave source and placement document
      against their original PR blobs, reconcile the two deliberate test
      assertion-only amendments and verify original ancestry.
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
