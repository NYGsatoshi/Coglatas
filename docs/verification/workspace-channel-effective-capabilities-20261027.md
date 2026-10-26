# Workspace channel effective viewer capabilities

## Problem and scope

An ordinary member can be admitted to a private Workspace channel through the
normal HTTP APIs. Changing that user's Workspace role to ReadOnly retains the
private participant's stored Member role and contribution flags. The existing
message and canonical reply commands correctly deny contribution, but the
Conversation detail still returned `canPost=true` and `canCreateThread=true`.
The Angular mapper consumes those viewer flags for its composer and Thread
creation affordance.

The detail projection now intersects the authenticated viewer's flags with
the existing current WorkspaceChannel posting and thread-creation checks.
It performs a bounded viewer-only resolution. Other participants, the separate
membership-management response and persisted grants retain their values.
Direct, Group, Project and legacy Conversation-Thread projections retain their
existing behavior. The response shape is unchanged.

The posting projection reuses `CanPostWorkspaceChannelAsync`, including the
documented active persisted SystemAdmin contribution compatibility with exact
Workspace membership and authorized participation. Thread creation continues
to use its stricter existing scoped contributor check. Server mutation
decisions, denial status/error envelopes, audit and Outbox behavior are unchanged.

## Actual HTTP regression

The new `WorkspaceChannelCapabilityProjectionPostgreSqlTests` fact runs the
actual ASP.NET Core entry point against a migrated temporary PostgreSQL database.
Authentication, authorization and HTTP services are retained. Workspace creation,
Workspace member admission, private channel creation/invitation and role changes
use the public cookie/CSRF-protected APIs. It does not promote a participant with
SQL or depend on a fabricated private Admin role.

Before the capability expectation, it verifies:

- Normal Member participation and a successful message before downgrade.
- Committed Workspace ReadOnly and retained private Member/read/post/thread flags.
- Both a message and a canonical reply are denied with the existing HTTP 400
  envelope and error.
- No Message, Messaging Outbox or successful-post audit rows are added; exactly
  two existing contribution-denial audits are added.

It then requires false viewer contribution flags while preserving read access,
the owner's capabilities and raw membership-management flags. Same-Workspace
Direct and Group controls retain posting. Restoring the Workspace Member role
restores the original private contribution and a successful post.

## Source-bound local execution

All executions used SDK 10.0.403 and PostgreSQL 18.6, with repository image digest
`postgres@sha256:77f585114c32fbca283dc835b0596f4e52b51b4c6662d7810b2f4084f60a1873`.
Each trial created its own labelled ephemeral container, bound PostgreSQL to a
loopback-only ephemeral port, used tmpfs without persistent volumes, and removed
only that verified container afterward. Passwords and connection strings remained
private. Backend builds used `BuildAngularFrontendOnPublish=false`; no Angular
license placeholder or product publish was used.

| Source | Observation |
| --- | --- |
| `104564559c7a3e3da037c26b8795c3fbd7c36c06` | Initial new Group control supplied two participants and failed its existing three-member admission rule before downgrade. Retained fixture failure; no product RED credit. |
| `8c20522109603e0d89ffee03e4de893e9639632e` | Corrected test-only baseline, runtime source tree identical to Main `e191494144628150d3d5136d7e704b270e67b342`: all preceding API/denial/persistence assertions passed, then the detail expectation failed with expected false/actual true. One executed failure, zero skips/retries. |
| `c273719f9c8cefb4b94b17f7cd5721ac2054a85a` | Identical HTTP regression bytes passed completely: one executed success, zero skips/retries. |

The candidate also passed these three unchanged real PostgreSQL service facts:

- `CurrentSystemAdminContributionCompatibilityStillRequiresWorkspaceMembership`.
- `ReadOnlyWorkspaceRoleDeniesRetainedConversationAdminSendReplyAndReplay`.
- `ExistingConversationPermissionLockAndArchiveGuardsStillDenyChannelSends`.

The candidate Release build completed with zero warnings/errors. Four unchanged
Angular mapper, composer and participant test files passed all 35 cases. The
existing future Vite configuration-loader warning was observed and not suppressed.
These unit tests supply no browser/target qualification.

The original private receipts, TRX files and sanitized aggregate remain retained
in the task workspace. They record source/test/assembly hashes, actual image
identity, counters, cleanup and first failures. Current-head hosted CI and
separate resulting-Main qualification remain required before integration is
considered complete.

## Authority and limits

This is an ordinary Pilot projection correction within the existing server
contribution policy. It introduces no schema, migration, role/grant/RLS change,
dependency, workflow, required-check or retry change. Existing Owner HOLDs,
formal security/recovery conditions and target/school admission gates remain.
Passing local HTTP and PostgreSQL checks does not establish complete cross-session
browser acceptance, physical IME/accessibility, deployment authority or RELEASE_GO.
