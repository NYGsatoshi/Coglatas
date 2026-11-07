# Project membership service controls

The additive `ProjectServiceTests.MembershipLifecycle.cs` cases reuse the existing
recording Repository fixture and production Project/Workspace authorization
services. They exercise the membership commands independently of canonical
Project creation, which retains its separate idempotency and activation contract.

Owner and Manager controls add, list, change and remove a target membership while
checking projected identity, unchanged join time, save counts, audit actions,
Project invalidations and target-scoped authorization signals. An unrelated
Project membership for the same target must remain unchanged.

Denial cases cover missing/empty identity, unavailable parent Workspace or
membership, non-managing Project roles, missing/deleted Projects, missing target
users, unavailable target Workspace membership, missing target Group membership,
duplicate membership and missing update/removal targets. Every rejected command
independently checks unchanged membership rows and no audit, invalidation,
authorization-signal or save calls.

These are service-level controls with recording dependencies. They do not prove
PostgreSQL persistence, transaction rollback, current persisted account or Tenant
queries, HTTP status/privacy envelopes, Outbox serialization or actual Realtime
delivery/revocation. Those require their own provider and transport evidence.
No Project lifecycle, authorization rule, membership role, existing assertion,
coverage/import scope, quality budget or deployment behavior is changed.

Native execution, exact source identity, hosted checks, independent review and
the original PR Gate must be recorded separately. Main coverage changes require
measurement on the resulting Main; local coverage is not an additive credit.

The initial authoring commit `7037b97362d91be997be8ae11896a2995fcf78d6`
failed compilation with two `CS0117` and two `CS0200` errors: Workspace membership
uses `Pending`, and soft deletion uses `MarkDeleted`. No test ran at that commit.
The original build log is retained with SHA256
`f68e0ab4493e63bb4c00c4a825c3bd7a08061aa2157b3cb408ee9bcd04e0dcd4`.
Correcting those calls preserves the denial assertions and production behavior.

At `a029525cd1dc0a0a38830bf3fa4a99e7adb7d4b5`, native execution passed the
23 denial controls and failed both positive controls at the first save-count
assertion. The new assertion had selected the generic Unit of Work recorder;
`SaveProjectMutationAsync` uses `SaveTaskCommandAsync`. The positive counts now
bind that existing command recorder, while asserting no generic save and no
tracking-clear calls. Denials check both recorders independently. The failed
TRX, log, raw report and seven executed DLL/PDB pairs remain sealed separately;
no production save path or existing Main assertion was changed.

Source review identified evidence gaps in the initial recording controls. The
expanded snapshots include membership ID, Tenant ID, parent/user identities,
role, join time and audit timestamps; update checks preserve all recorded fields
except the requested role. Fixtures seed nonempty parent Tenant identities and
existing row timestamps. Recorded invalidations now retain Project, actor and
change arguments, and every audit checks its actor and entity identity. Both
generic save entry points and the command-save/clear entry points are counted.
Only recorder instrumentation changes in the original fixture; its existing
public tests and assertions remain unchanged.

The recording Repository does not apply EF Tenant or audit stamping to newly
added rows. The test preserves their observed staged identity on update; it does
not infer a committed Tenant stamp or persisted timestamp from that snapshot.

At `98a6883058dbe62cea5f02ccb02dfa75455e36df`, native controls passed,
but the first hosted PR CI attempt failed its ReSharper fast lane on
`ParameterOnlyUsedForPreconditionCheck.Local` in the new audit assertion lambda.
An explicit iteration retains every actor/entity assertion and the existing
three-action assertion, without changing any quality policy. The original
failed job `128455442711` and SARIF archive remain retained separately.
