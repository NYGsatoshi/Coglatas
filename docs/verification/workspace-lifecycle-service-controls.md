# Workspace lifecycle service regression controls

This additive candidate exercises the existing `WorkspaceService` Archive,
Restore and metadata-update commands. It changes no production implementation,
authorization contract, workflow, coverage scope or quality policy. The baseline
test class and fixture gain only `partial` declarations; their original tests,
assertions and repository doubles remain unchanged.

The five new methods declare 24 cases:

- Fourteen Archive/Restore denial rows cover unauthenticated, hidden, missing,
  deleted-status, separately soft-deleted, incorrect-lifecycle and insufficient-role
  states. Each operation
  retains the complete selected Workspace and membership snapshots and records
  no audit, invalidation or save.
  The selected Workspace snapshot includes the deletion time, actor and reason.
- One round-trip control checks archive, repeated-archive denial, restore and
  repeated-restore denial separately. Only distinct active recipients receive
  the selected Tenant/user/Workspace/change metadata. Pending, suspended,
  duplicate and other-Workspace membership rows do not add recipients. Selected
  Workspace metadata and membership rows remain intact after each operation.
- Four invalid-update rows check the three database-unsafe null-character
  fields and the separate lifecycle-command requirement. Other requested fields
  must not be partially applied; selected state and effects remain unchanged.
- One metadata control checks trimming, the identifier-bound slug, all returned
  fields, complete selected stored state and omitted-field retention after each
  update. It checks the recorded audit and save counts without a lifecycle change.
  Both DTO expectations and every invalidation record are constructed from
  known pre-operation state and recipient identities, independently of actual output.
- Four dependency-failure rows retain exception identity and distinguish an
  attempted save from a failure before saving. Mutable fixture state, recorded
  audit and attempted invalidation remain staged; they are not represented as
  committed or rolled back.

The service uses the existing `WorkspaceAuthorizationService` over the original
lightweight user and Workspace repositories. Selected membership rows receive
the fixture Tenant identity before snapshots are taken. The audit, publisher
and unit-of-work recorders are controlled test doubles. They do not prove
current persisted authority, tenant admission, HTTP privacy, SQL isolation,
durable Outbox staging, delivery, rollback, effect atomicity, deployed roles,
all-table RLS, File/Artifact reconciliation or full Recovery. No native timeout
cause, security disposition, Owner approval or release acceptance is inferred.

Local verification is source inspection and `git diff --check` only. No local
.NET/WSL execution is used. Supported exact-head hosted execution, compiler and
analyzer checks, original PR Gate, independent review and completed parent/Main
qualification remain mandatory. Any observed Main coverage delta must be
measured independently; no local increment or projected gain is claimed.

The first exact `44dd9eba` PR CI run failed the ReSharper gate on 25 positional
properties used only by implicit record equality. Its original diagnostics and
successful test execution remain separate evidence. An additive follow-up keeps
every typed record comparison and explicitly compares all 16 Workspace, 11
membership and five invalidation scalar fields. No property, assertion,
inspection, threshold or policy is removed or suppressed. Fresh supported
execution and analyzer results are required for the follow-up source.

The later complete source review of `c7a8a800` identified a separate repository
collection oracle gap even though its CI passed. An additive follow-up captures
an independent dictionary of every Workspace key, original object reference
and immutable sixteen-field scalar snapshot before each test's operations.
Every refusal, transition, repeated command, metadata update and dependency
failure compares the complete key set and all stored rows. Expected selected
changes come from pre-operation IDs and literal state changes; all other rows
retain their original identity and values. A deleted, foreign-Tenant sentinel
is included in every existing-Workspace fixture, while missing-Workspace rows
explicitly start with and retain an empty dictionary. The existing selected
object, membership, effect and failure assertions remain unchanged. This adds
collection-state observations in repository doubles, not SQL, persistence,
rollback or Tenant-admission qualification. Fresh exact-head execution,
inspection and complete independent review remain required.
