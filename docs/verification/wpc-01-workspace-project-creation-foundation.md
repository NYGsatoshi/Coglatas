# WPC-01 Workspace / Project creation backend foundation

Status: final merge-readiness remediation candidate

- PR: #281
- Base SHA: `74d0a334e4b3094e1efad48006fce9b13b21bdef`
- Final-targeted-remediation starting HEAD: `e131b6707b25374b9f99f8008c7c417773941a94`
- Final HEAD: recorded in PR #281 after the scoped remediation push
- Specification SHA: `f7535ce7de1846780a1dd6689e93310f0482897b`
- Branch: `wpc/01-workspace-project-creation-foundation`
- Date: 2026-08-14

## Verdict

WPC-01 does not report an incomplete canonical create operation as successful.
Production Workspace creation is gated because the repository has no canonical,
Conversation-backed provisioner for the required Workspace `general` channel.
Canonical Project creation and activation are also unavailable while exact
authority, Visibility migration, default Channel, and Task workflow decisions
remain unresolved.

The implemented foundation is safe and deliberately bounded:

- generic Draft activation is rejected, while the proven operational
  `Active -> Review -> Active` path remains valid;
- `Planning -> Suspended` remains valid, but Suspended recovery into Planning
  or Active is gated until lifecycle provenance is canonical;
- ambiguous Archived/Deleted recovery returns a non-mutating typed conflict;
- Planning and provenance-ambiguous lifecycle states fail closed against broad
  discovery and subordinate-resource access;
- legacy body-scoped Project creation returns 503 without mutation;
- Workspace initialization availability is server-owned, and production cannot
  return 201 while required `general` provisioning is unavailable;
- successful no-op initializer tests exercise the transaction/idempotency seam
  only and are not evidence that `general` was provisioned.

## Canonical sources reconciled

The following files were read at specification SHA
`f7535ce7de1846780a1dd6689e93310f0482897b` before production changes:

- `01-core/16-workspace-project-creation-ui-owner-decision-resolution.md`
- `01-core/11-workspace-project-governance.md`
- `01-core/15-workspace-task-messaging-owner-decision-resolution.md`
- `03-acceptance/workspace-project-creation-ui-acceptance.md`
- `06-implementation-mapping/workspace-task-messaging-implementation-sequence.md`
- `01-core/10-communication-conversation-scope.md`
- `01-core/13-messaging-product-contract.md`
- `01-core/14-workspace-task-messaging-realtime-addendum.md`
- `01-core/api-error-contract.md`
- `01-core/outbox-delivery-contract.md`
- `01-core/22-audit-jobs-consistency.md`

## Requirement matrix

| Requirement | Normative source | Classification | Actual state |
| --- | --- | --- | --- |
| Tenant Owner/Admin Workspace-create authority | WPC-DEC-002; governance §8.1 | IMPLEMENTED | Current active Tenant membership is authoritative. Platform/SystemAdmin status alone is not a bypass. |
| Delegated `workspace.create` | WPC-DEC-002 | DEPENDENCY BLOCKER | No canonical delegation store/evaluator exists; delegated callers remain denied. |
| Backend create capability | WPC-DEC-011 | IMPLEMENTED | `GET /api/workspaces/capabilities` uses create authority plus required-initialization availability. Production currently returns `canCreate: false`. |
| Required Workspace `general` | WPC-DEC-025; WPC §4.4 | FAIL-CLOSED / GATED | Production registers the unavailable initializer. A valid create receives 503 before any durable mutation. |
| Canonical Workspace-channel provisioner | Messaging contract §§2, 10 | DEPENDENCY BLOCKER | Conversation has no WorkspaceChannel/default identity or uniqueness boundary. Legacy Channel/Post was not extended. |
| Workspace transaction orchestration | WPC-DEC-006/021/025 | IMPLEMENTED | Claim, Workspace, Owner, required initializer, audit, Outbox, and commit share one relational coordinator transaction. Initializer/Outbox failures roll back all staged effects. |
| Workspace create idempotency | WPC-DEC-021; WPC §13 | IMPLEMENTED | Tenant + actor + operation + SHA-256 key identity, normalized fingerprint, replay authorization, and concurrent reconciliation are preserved. |
| WPC success/error envelope | API error contract; hardening §2 | IMPLEMENTED | Changed WPC endpoints and their pre-controller failures use the full success/error envelope and canonical HTTP mapping. |
| Canonical cross-module RedactionService | API error contract | DEPENDENCY BLOCKER | No repository redaction service exists. WPC uses fixed public messages, empty details, masked targets, and `redactionApplied`; it does not claim repository-wide redactor integration. |
| Generic Project transition to Active | WPC-DEC-014/024; WPC §12.2 | IMPLEMENTED | Planning/Suspended/other non-Review attempts to target Active return 409 `InvalidStateTransition`; Review may return to Active because its production provenance is operational. |
| Never-activated archive/restore | WPC-DEC-014/024 | IMPLEMENTED | Archive produces Archived; recovery does not guess Planning or Active and returns a non-mutating 409. |
| Suspended recovery | No normative provenance mapping found | FAIL-CLOSED / GATED | Suspended cannot return to Planning or Active until canonical lifecycle provenance exists. Both attempts return a non-mutating typed 409; Suspended may still transition to Archived. |
| Previously-active lifecycle provenance | No normative mapping found | DECISION REQUIRED | No `ActivatedAt`, audit inference, or data backfill was invented. Recovery needing unknown provenance conflicts without mutation. |
| Draft/ambiguous-state non-disclosure | WPC-DEC-016; governance §§5, 13 | IMPLEMENTED | Planning/Suspended require current Workspace access plus explicit Project membership across list/detail/search, Tasks, My Tasks, digest, Messaging, and realtime. Project-derived Search and Messaging reapply current authorization before returning protected content. |
| Canonical Workspace-scoped Project create | WPC-DEC-020; WPC §12.1 | FAIL-CLOSED / GATED | The route is absent. Deprecated `POST /api/projects` returns 503 and performs no mutation. |
| Workspace-root Project-create authority | Governance §§4, 9; WPC §12.1 | DECISION REQUIRED | The specification names no exact capability or actor predicate. |
| Non-default Visibility authority | WPC-DEC-016 | DECISION REQUIRED | The specification requires a capability but does not name it. |
| Optional Group | WPC-DEC-012 | FAIL-CLOSED / GATED | Domain/response support nullable Group, but no canonical create command is exposed. |
| Existing Project Visibility backfill | No normative mapping found | DECISION REQUIRED | No Visibility schema/default/backfill was added. |
| Project creator Owner/idempotency | WPC §§11, 13 | FAIL-CLOSED / GATED | No create succeeds until the complete atomic command can be implemented. |
| Explicit activation endpoint | WPC-DEC-024; WPC §12.2 | FAIL-CLOSED / GATED | The route is absent; PATCH/restore cannot substitute. |
| Default Project Channel | WPC-DEC-024 | DEPENDENCY BLOCKER | No canonical idempotent Conversation provisioner exists. |
| Task workflow activation mapping | WPC-DEC-024 | DEPENDENCY BLOCKER | Existing create-time workflow initialization has no approved activation compatibility rule. |
| Angular production UI | Scope rule | OUT OF SCOPE | No production frontend code was changed. |

## Workspace initialization audit

Canonical `general` provisioning: **FAIL-CLOSED / GATED**.

Production evidence:

- `UnavailableWorkspaceRequiredInitialization.IsAvailable` is false;
- `GetCapabilitiesAsync` therefore reports `canCreate: false`;
- an otherwise authorized valid POST returns 503 `DependencyUnavailable`;
- the availability gate is evaluated before the idempotency coordinator, so no
  Workspace, Owner, audit, Outbox, idempotency record, Conversation, or partial
  default is persisted.

Transaction evidence:

- injected successful initializer tests exercise only the coordinator seam;
- the successful fake is intentionally a no-op and is not `general` evidence;
- injected initializer failure tests prove rollback of Workspace, Owner, audit,
  Outbox, and the idempotency claim;
- required Outbox enqueue failure throws inside that same boundary;
- retry/concurrency tests prove one logical transaction and one side-effect set.

The current Conversation entity supports DirectMessage, ProjectChannel, and
Thread, but not an unambiguous WorkspaceChannel/default marker. Its indexes do
not enforce one default `general` per Workspace. The legacy Channel entity
requires Group scope. Consequently neither model can be used as the required
default without inventing policy or creating a competing Messaging boundary.

## Idempotency and authorization

The durable identity is:

```text
TenantId + ActorUserId + Operation + SHA-256(ClientRequestIdentity)
```

The request fingerprint hashes every normalized authoritative Workspace-create
field. Raw keys and request bodies are not stored. Create authority is checked
before both first execution and replay. Replay additionally requires the same
actor's current active Workspace membership; a revoked actor cannot recover
metadata through a Platform/SystemAdmin shortcut. Another actor, Tenant,
operation, or request fingerprint cannot reconcile the record.

The relational coordinator creates its claim inside an uncommitted transaction,
stages all business effects, saves once, and commits. It rolls back and clears
the EF change tracker on callback or persistence failure. A concurrent loser
reconciles the committed winner; it does not create a second Workspace.

## Project lifecycle audit

Production Project lifecycle entry points:

| Path | Final behavior |
| --- | --- |
| `ProjectService.CreateAsync` | Deprecated command returns 503 and creates nothing. Domain default for any separately constructed new Project remains Planning. |
| `ProjectService.UpdateAsync` | Same-state metadata retention is valid except Archived/Deleted, which are read-only. Graph edges are Planning -> Suspended/Archived; Active -> Review/Completed/Suspended/Archived; Review -> Active/Completed/Suspended/Archived; Completed -> Archived; Suspended -> Archived. Every missing edge returns typed 409 `InvalidStateTransition`, target `body.status`, before metadata/lifecycle mutation, audit, ProjectChanged or authorization invalidation, or save. |
| `ProjectService.ArchiveAsync` | Maps any non-Archived/non-Deleted, non-soft-deleted state to Archived. Repeating archive conflicts without a success side effect. |
| `AdminService.ArchiveProjectAsync` | Compatibility administration path writes Archived plus deletion metadata and a `DataArchived` audit. Before mutation it enumerates the same current Project readers and stages metadata-only authorization invalidations with the archive/audit transaction. Deleted/already-soft-deleted input conflicts before mutation, audit, invalidation, or save; the path never writes Active. |
| `ProjectService.RestoreAsync` | Current persistence cannot choose a safe prior status. Otherwise-authorized restore requests return typed 409 without status/deletion mutation, success audit, invalidation, or save. |
| Suspended recovery | Transition graph permits only Archived. Planning and Active recovery remain gated until canonical provenance exists. An otherwise valid metadata-only update may retain Suspended. |
| Explicit `POST /activate` | Absent. |
| Domain `Restore()` helper | Clears deletion metadata only; it does not change Project status. |
| Migrations | No migration writes Project status to Active and no activation provenance migration exists. |
| Browser-smoke seed | Test-environment-only fixture creation may construct new Active fixtures. Refresh never promotes an existing Planning/non-Active row to Active; four compatibility refresh paths may clear deletion metadata while preserving the existing status. |
| Direct EF/test fixtures | Trusted test setup can construct Active rows because no schema invariant/provenance exists; it is not an application/API lifecycle command. |

This closes every generic first-activation bypass without breaking the
unambiguous operational `Review -> Active` return. Planning, Suspended, and
Archived/Deleted histories are treated conservatively when provenance is
required. Explicit Project members retain bounded access; broader
Workspace/Group governance does not acquire Draft visibility merely by moving
the row to Suspended or Archived. Suspended recovery into Planning or Active is
also gated rather than inferred from audit, child-resource, timestamp,
membership, workflow, or client state. This remains a **FAIL-CLOSED / GATED**
foundation, not a final activation or historical-recovery policy.

## Draft and subordinate-resource boundary

Planning/Suspended access requires current active Workspace membership and an
explicit ProjectMember. The same base boundary is applied below, with each
feature retaining stricter role or relationship checks where required:

- Project list, detail, management, and search;
- Task/Artifact/Activity/Comment search;
- My Tasks projection/count/Project scope;
- deadline-digest current-state evaluation;
- project-bound Conversation creation, read/send authorization, list counts,
  unread/update polling, and Message search;
- authorization target resolution and Project/Task realtime delivery;
- delayed `Messaging.ConversationUnreadChanged.v1` user-routed events.
- persisted Artifact and project-bound Message notifications across list,
  totals/unread, read/delete, open, and delayed created/read-state delivery.

For operational Projects, the exact current detail predicate has a shared
SQL-translatable query form used by Project list and all Project-derived Search
categories. Non-deleted Archived history is available only through the archive
list filter to an active Workspace member who is also an explicit Project
member; detail, Search, and subordinate reads remain hidden. The shared scope
preserves explicit ProjectMember, authorized GroupMember,
Workspace Owner/Admin, ungrouped ordinary-member, and current SystemAdmin
access while denying an ordinary member outside a grouped Project/Group and a
revoked member with stale subordinate rows. PostgreSQL Message Search resolves
the complete authoritative recursive readable-Conversation ID set, then
intersects it with all matching Messages before `CreatedAt DESC, Id ASC`
ordering and `Take(100)`;
there is no arbitrary pre-authorization Conversation subset. Production
PostgreSQL detail, pages, counts, polling, and Message Search use the same
set-based ancestry relation; missing identity, inconsistent Workspace/Project/
root scope, cycles, or more than 32 Thread edges fail closed instead of
affecting returned metadata. The bounded non-PostgreSQL provider fallback
remains fail closed.
Send, moderate, and Thread-create checks first require that same structural
boundary. Thread creation rejects a child beyond the readable limit before
success mutation. Direct-message reuse matches
Workspace and nullable Project scope exactly and reauthorizes existing rows.

Conversation membership or historical Outbox routing is not authority.
Project-bound Conversations recheck every non-null `ProjectId`, regardless
of Conversation type. Delayed unread events parse a non-empty Conversation ID
and call current Conversation authorization; malformed identity or revoked
access is denied.

Artifact Notifications resolve through the shared current Project read scope.
Message Notifications resolve through the same recursive Conversation scope.
Their list/count checks batch at most 100 protected Notification IDs, so a
Message batch performs one recursive authorization query rather than an
unbounded per-row N+1. Task/digest created signals remain reference-only;
Artifact/Message legacy embedded signals are dispatched only after current
target reauthorization. Visibility-reducing Project transitions (Suspended or
Archived) capture the pre-transition current readers and stage metadata-only
`Security.AuthorizationStateChanged.v1` events in the same business unit of
work; a failed required Outbox stage prevents save.

## Project create status

- Workspace-scoped route: **FAIL-CLOSED / GATED**
- Optional Group: **FAIL-CLOSED / GATED**
- Create authority: **DECISION REQUIRED**
- Visibility: **DECISION REQUIRED**
- Draft non-disclosure: **IMPLEMENTED**
- Creator Owner atomicity: **FAIL-CLOSED / GATED**
- Idempotency: **FAIL-CLOSED / GATED**

The body-scoped compatibility route remains registered so existing clients
receive a deterministic safe response, but the application command returns
503 before authorization inference or mutation. There is therefore no second
successful Workspace scope, no cross-Workspace/Tenant Group binding, and no
non-idempotent Project retry.

## Activation status

- Explicit endpoint: **FAIL-CLOSED / GATED**
- Concurrency: **FAIL-CLOSED / GATED**
- Default Channel: **DEPENDENCY BLOCKER**
- Task workflow: **DEPENDENCY BLOCKER**
- Visibility validation: **DECISION REQUIRED**
- Audit: **FAIL-CLOSED / GATED**
- Outbox: **FAIL-CLOSED / GATED**
- Atomic rollback: **FAIL-CLOSED / GATED**

Activation is not exposed. This avoids marking Active before the required
default Project Channel, Task workflow compatibility, Visibility policy,
audit, Outbox, and optimistic-concurrency checks can succeed atomically.

## API contract

Changed endpoint behavior:

- `GET /api/workspaces/capabilities`
  - 200 `{ requestId, data: { canCreate }, warnings: [] }`;
  - production currently reports false because required initialization is unavailable;
  - unauthenticated/invalid current-Tenant failures use full 401/403 envelopes.
- `POST /api/workspaces`
  - future 201 is possible only when a real required initializer is available
    and the transaction commits;
  - current production valid request: 503 `DependencyUnavailable`;
  - 400: `ValidationFailed`, `MalformedJson`,
    `MissingIdempotencyKey`, or `InvalidIdempotencyKey`;
  - 403: `CapabilityDenied` or `CsrfRejected`;
  - 409: `IdempotencyConflict`;
  - 415: `UnsupportedMediaType`;
  - protected replay no longer recoverable: redacted 404 `NotFound`.
- `POST /api/projects`
  - 503 `DependencyUnavailable`, no mutation.
- `PATCH /api/projects/{projectId}` targeting Active
  - Planning/Suspended/other non-Review attempts: 409
    `InvalidStateTransition`, target `body.status`;
  - Review return and Active metadata retention remain valid.
- `PATCH /api/projects/{projectId}` for any invalid lifecycle edge
  - Planning -> Review, Active -> Planning, Completed -> Review, and Suspended
    -> Planning are covered through the actual service/controller boundary;
  - each returns 409 `InvalidStateTransition`, target `body.status`, with no
    rejected-request mutation or success side effect.
- `POST /api/projects/{projectId}/restore`
  - 409 `InvalidStateTransition`, target `project`, with no lifecycle/deletion
    mutation or success side effect.
- hidden `GET /api/projects/{projectId}`
  - indistinguishable full redacted 404 `NotFound`.

`Idempotency-Key` must contain 8–128 printable ASCII characters.
Hosted HTTP coverage includes missing/invalid keys, malformed JSON, valid-JSON
binding or type errors, unsupported media type, authentication, authorization,
CSRF, dependency unavailability, idempotency conflict, lifecycle conflict, and
masked not-found behavior. Unexpected-server and replay-isolation cases are
additionally covered at the middleware/controller boundary.

The envelope helper emits static public messages and empty details. The
canonical repository-wide `IRedactionService`/ErrorResponse profile is absent
and remains a dependency blocker; WPC does not claim to have migrated unrelated
legacy endpoints.

## Migration evidence

No remediation migration was added. The existing WPC migration is:

```text
20260813100711_Wpc01WorkspaceCreateIdempotency
```

It additively creates `idempotency_records`, the restricted actor foreign key,
and bounded actor/Tenant/resource/created-time indexes. Down drops only that
table. SQL review found no Project, Workspace, Conversation, Task, or other
business-row update. No Visibility or lifecycle-provenance column/backfill was
added.

The real PostgreSQL migration test:

- applies the prior schema and seeds an existing Project;
- upgrades to the current migration;
- verifies the Project survives and no Visibility column exists;
- rolls back to the prior migration;
- verifies the Project still survives;
- reapplies current migrations;
- verifies no pending migration or model change.

## Verification

Final verification uses an isolated PostgreSQL 18 container with
`POSTGRES_TEST_CONNECTION_STRING` explicitly present for the database-backed
runs. These counts were recorded after the last production-source change; no
conditional early return is counted as database evidence.

| Final command | Passed | Failed | Skipped | Result / qualification |
| --- | ---: | ---: | ---: | --- |
| `dotnet restore Coglatas.slnx` | - | 0 | 0 | All projects already restored. |
| Release build, `--no-restore --disable-build-servers -m:1` | - | 0 | 0 | 0 warnings and 0 errors. |
| `Scope=WPC01` | 57 | 0 | 0 | Includes real-PostgreSQL Workspace/idempotency, lifecycle, authorization, recursive Conversation, >100-Conversation Message Search, archive-history, realtime, and migration cases. |
| `ProjectServiceTests` | 90 | 0 | 0 | Full lifecycle graph, metadata retention, typed conflicts, and rejected-request zero-side-effect assertions. |
| `ProjectsControllerTests` | 11 | 0 | 0 | Controller mapping helpers, including the canonical lifecycle-conflict envelope. |
| Search/Project authorization PostgreSQL group | 13 | 0 | 0 | `PostgreSqlIntegrationTests` plus the WPC PostgreSQL fixture: Project-derived Search parity, recursive scope, >100 Conversations, unauthorized Thread exclusion, and deterministic Message ordering. |
| Messaging focused group | 32 | 0 | 0 | Conversation, polling, safety, hosted communication isolation, and PostgreSQL recursive-authorization coverage. |
| Realtime/authorized-delivery focused group | 52 | 0 | 0 | Project/Task/Conversation/Notification dispatch reauthorization. |
| `HttpTenantIsolationTests` | 47 | 0 | 0 | Hosted WPC envelope, four service-produced invalid-transition 409 cases, Tenant, lifecycle, and Messaging boundaries. |
| `Scope=TaskV1PR07D` | 37 | 0 | 0 | 34 required manifest names plus additional notification regressions; real PostgreSQL enabled. |
| Full `dotnet test Coglatas.slnx --configuration Release --no-build --no-restore` | 916 | 0 | 0 | Real isolated PostgreSQL 18; 4 minutes 55 seconds. |
| EF pending-model check | - | 0 | 0 | `No changes have been made to the model since the last migration.` |
| `git diff --check` | - | 0 | 0 | Clean; line-ending conversion notices only. |
| Independent final source review | 0 reportable findings | 0 | 0 deferred | Adversarial review covered lifecycle ordering/side effects, recursive Search composition, Tenant/Project/Workspace/root consistency, cycle/depth rejection, provider fallback, deterministic ordering, and the >100 regression. |

`dotnet format Coglatas.slnx --verify-no-changes --no-restore --verbosity
minimal` exited 1 on widespread pre-existing whitespace violations, including
untouched `TaskSubresourceService.cs` and `TenantIsolationSecurityTests.cs`.
No repository-wide formatting rewrite was performed.

Successful no-op initializer tests are named coordinator seam tests and are
not counted as default-`general` provisioning evidence.

## Remaining blockers

### Specification decisions

- **DECISION REQUIRED — PROJECT CREATE AUTHORITY**
- **DECISION REQUIRED — EXISTING PROJECT VISIBILITY BACKFILL**
- **DECISION REQUIRED — NON-DEFAULT PROJECT VISIBILITY AUTHORITY**
- **DECISION REQUIRED — PROJECT LIFECYCLE PROVENANCE**

### External/cross-module dependencies

- **DEPENDENCY BLOCKER — DELEGATED workspace.create CAPABILITY INFRASTRUCTURE**
- **DEPENDENCY BLOCKER — CANONICAL WORKSPACE/PROJECT DEFAULT CHANNEL PROVISIONING**
- **DEPENDENCY BLOCKER — PROJECT TASK WORKFLOW INITIALIZATION**
- **DEPENDENCY BLOCKER — CANONICAL CROSS-MODULE REDACTION SERVICE**

### Code defects

- **NON-BLOCKING PERFORMANCE DEFECT - NOTIFICATION MARK-ALL MATERIALIZATION:**
  `MarkAllAsReadAsync` still materializes all unread rows for one recipient
  before applying current-target authorization in bounded batches. It does not
  widen visibility or cross a Tenant/user boundary, but a recipient with an
  unusually large notification history can cause avoidable memory use. A
  transaction-preserving set-based redesign is follow-up work outside WPC-01.

Unresolved specification and cross-module behavior remains unavailable or
conservatively restricted rather than being approximated with inferred policy.
