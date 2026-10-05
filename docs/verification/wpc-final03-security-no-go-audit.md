# WPC-Final03 Security No-Go Audit

## 1. Decision

**Current decision: CONDITIONAL NO-GO.**

The integrated WPC baseline contained seven security-significant authorization, lifecycle, dependency, and redaction gaps. This branch contains targeted remediations and dedicated unit/PostgreSQL acceptance coverage. The merge-audit implementation blockers identified after the initial acceptance pass have also been closed: WPC-Final01 and WPC-Final02 are merged into the current `main`, the temporary restore-repair workflow has been removed, generic Task assignment authorization is live-row-only again, and the soft-deleted Task exception is scoped explicitly to Restore at the command boundary.

The PR remains Draft until all required CI checks pass on the final exact PR head and that head is rechecked against the then-current `main`.

No auto-merge is permitted.

## 2. Fixed baselines

| Target | Baseline |
|---|---|
| Implementation repository | `NYGsatoshi/Coglatas` |
| Original integrated implementation baseline | `31a5d2986d8a83c31fbaee22a679042a17b4335c` |
| Current merge-audit `main` baseline | `74cca8756bac69061c676d924fcb74a6edf3123b` |
| WPC-Final01 merged baseline | `6eed5071736a71eb80d3d7688614b59bcaa063fc` |
| WPC-Final02 merged baseline | `6cdc29434293ff03f066a4974323e2bd61b8c437` |
| Specification repository | `NYGsatoshi/Coglatas-Spec` |
| Normative specification baseline | `38339ba2964587f225c4c4151f643abb5523e862` |
| Audit PR | `#324` |
| Audit branch | `wpc-final03-security-no-go` |

The original implementation baseline includes the merged WPC-02A, WPC-02B, WPC-02C, WPC-02D, WPC-02E-1, WPC-02E-2, and WPC-02F workstreams. The current merge-audit baseline additionally includes merged WPC-Final01 and WPC-Final02 plus UI Shell PR #326. PR #326 changes frontend App Shell/FeatureMenu/RightPanel files only and does not overlap the Final03 backend/security hot spots. PR #324 has merged that current `main` baseline into its branch with no outstanding base drift at the time of this update.

## 3. Normative security rules used

The audit treats the following specification requirements as merge blockers:

1. Project visibility is a discovery/read decision; it is not resource-mutation authority.
2. `WorkspaceRole.ReadOnly` and Project Viewer are read-only and cannot perform mutation.
3. Tenant membership is necessary, but not sufficient, for Workspace membership and access.
4. Cross-Tenant Workspace, Project, membership, and File relationships are prohibited.
5. Membership revocation must immediately invalidate File and other dependent authorization.
6. Unknown, legacy-unknown, incomplete, or unavailable authorization state fails closed.
7. File metadata uses the canonical `IRedactionService` and applies field policy to filename, uploader, target label, and classification.
8. Persisted historical identity and routes are attribution/context only; current authorization is authoritative.
9. Task and Milestone mutation requires a current authorized, activated, operational Project state at the command and persistence boundaries.
10. Required default-resource provisioning and membership synchronization cannot be silently skipped.

Primary normative files:

- `docs/specs/coglatas-core-v4/01-core/04-permission-data-access-security.md`
- `docs/specs/coglatas-core-v4/01-core/11-workspace-project-governance.md`
- `docs/specs/coglatas-core-v4/01-core/11-task-work-planning-scope.md`
- `docs/specs/coglatas-core-v4/03-acceptance/task-work-planning-acceptance.md`

## 4. Audited WPC surfaces

| Surface | Reviewed implementation |
|---|---|
| Project read and management | `ProjectAuthorizationService`, `ProjectReadScope` |
| Task/Milestone/comment mutation | `TaskCommandService`, `TaskSubresourceService`, `ProjectAuthorizationService`, `ProjectGovernanceSaveChangesInterceptor` |
| Canonical Project create | `CanonicalProjectCreateService`, `WorkspaceProjectsController` |
| Project activation | `ProjectActivationService`, activation unit of work/provisioners |
| Workspace membership | `WorkspaceService`, `WorkspacesController`, Workspace `general` synchronizer |
| Capability delegation | `CapabilityGrantEvaluator`, `CapabilityGrantService` |
| File access | `FileAuthorizationService`, `FileService`, `FileRepository`, `FilesController` |
| Redaction | `IRedactionService`, canonical projection filters, File metadata projections |
| Notification navigation | current target resolver, notification open transaction, Angular protected-target flow |
| Archived state | Workspace/Project read and restore authorization |
| Migration fail-closed states | Project Visibility/Activation `LegacyUnknown` handling |

## 5. Findings and remediation

### F03-SEC-001 — Project read authority escalated to File mutation

**Severity: High**  
**Status: Remediated in PR #324**

`FileAuthorizationService.CanUploadAttachment` previously used `CanViewProject` for every Project-backed attachment owner. After WPC-02A, an activated `WorkspaceVisible` Project may be read by an active Workspace member without explicit Project membership. This meant a Workspace ReadOnly user or a non-Project-member reader could reach the File write path solely because Project discovery/read was allowed.

Exploit preconditions were limited but realistic:

1. active Tenant and Workspace membership;
2. `WorkspaceVisible`, activated Project;
3. attachment target resolvable to that Project;
4. no explicit Project contributor membership required.

Remediation:

- added `IProjectAuthorizationService.CanContributeProject`;
- require activated `Active`/`Review` Project state;
- require current contributing Workspace membership;
- require an explicit non-viewer Project role;
- deny Project Viewer, Workspace ReadOnly, Planning, Completed, Suspended, Archived, Deleted, and LegacyUnknown states;
- changed File upload to use this mutation-specific decision.

### F03-SEC-002 — Historical uploader identity acted as durable delete capability

**Severity: High**  
**Status: Remediated in PR #324**

`CanDeleteAttachment` previously allowed `UploadedByUserId` or `OwnerUserId` before resolving and reauthorizing the current attachment owner scope. A former uploader could therefore retain delete authority after Workspace/Project/Conversation/Channel access was revoked.

Remediation:

- resolve the current owner target first;
- reauthorize current mutation authority for Project, Conversation, Channel, or Workspace;
- only then allow the current uploader/owner to delete;
- allow another user's Project attachment to be moderated only when the actor has both current Project contribution and Project management authority.

Historical identity is now attribution, not a capability.

### F03-SEC-003 — Canonical File metadata fields were not classified

**Severity: High for Confidential/Restricted metadata; Medium otherwise**  
**Status: Remediated in PR #324**

The canonical redaction engine had an empty `FileMetadata` confidential/restricted field map. The standard authorized field policy therefore treated filename and uploader metadata as Internal and returned it unchanged. This contradicted the specification's explicit File metadata field list.

Remediation:

- added `CanonicalFileMetadataRedactionService` as the registered `IRedactionService` boundary;
- applies canonical Confidential classification recursively to filename, uploader identity/display, target labels, and classification;
- preserves non-sensitive routing/size identifiers;
- requires explicit `ThroughConfidential` field policy to disclose those values;
- continues to delegate all non-FileMetadata profiles and all authorization-state handling to `CanonicalRedactionService`;
- verified production `AddApplication()` resolves the hardened implementation.

### F03-SEC-004 — Workspace membership admitted globally valid but wrong-Tenant users

**Severity: High**  
**Status: Remediated in PR #324**

The Workspace member command validated the global `User` but did not require a current active `TenantUser` for the Workspace Tenant. A wrong-Tenant global user could be staged as a Workspace member and then mirrored into canonical Workspace `general` membership.

Remediation:

- the HTTP command boundary checks the current non-platform Tenant context;
- requires an exact active `TenantUser` with active, non-deleted `User` and `Tenant`;
- validates scalar and navigation Tenant/User identities against the current request and requested user;
- invalid, inactive, mismatched, cross-Tenant, or unavailable cases are not-found masked before Workspace mutation;
- production Workspace `general` synchronization revalidates the same Tenant/User/TenantUser state before granting Conversation participation;
- revocation remains available even after Tenant suspension because inactive Workspace membership follows the removal path.

### F03-SEC-005 — Viewer and stale Project lifecycle could reach Task, Gantt, review, and comment mutation

**Severity: High**  
**Status: Remediated in PR #324**

The Task authorization path treated any Project membership as sufficient for creation, and creator/assignee/reviewer relationships could permit mutation without excluding Project Viewer. Project comments also reused Project read authority. The Gantt schedule/progress compatibility path used a separate authorization branch that excluded only Archived/Deleted Projects, so Planning, Suspended, Completed, or non-canonical activation states could reach mutation.

Remediation:

- Project Viewer is denied Task creation, Task-body update, review outcomes, and Project/Task/Milestone comment mutation;
- creator, assignee, and reviewer history cannot override the current non-viewer Project boundary;
- Task mutation requires an activated Project in `Active` or `Review` state and a contributing Workspace membership;
- explicit reviewer assignment remains a narrow review-outcome permission for a current non-viewer Project member, not unrestricted Task editing or Project management;
- Project content comments use the same current contributor-or-manager operational boundary rather than Project visibility;
- `ProjectGovernanceSaveChangesInterceptor` rejects every tracked Task/Milestone persistence mutation unless the current Project is tracked in the same unit of work with coherent Activated provenance and `Active`/`Review` status;
- the persistence fence covers alternate Gantt adapters as well as ordinary Task commands;
- reusable `ITaskAuthorizationService.CanAssignTask` / delete / override authority remains live-row-only and rejects soft-deleted Tasks;
- `TaskCommandService.RestoreAsync` is the sole soft-deleted Task mutation exception: `includeDeleted` is explicit and Restore additionally requires both current `CanContributeProject` and current `CanManageProject` authority before version/concurrency validation and persistence.

This preserves the ordinary deleted-row fail-closed boundary while allowing the canonical Restore command to reach optimistic concurrency handling.

### F03-SEC-006 — Missing Workspace `general` synchronizer failed open

**Severity: High**  
**Status: Remediated in PR #324**

Workspace member add/update/remove previously treated an unavailable `IWorkspaceGeneralMembershipSynchronizer` as `Result.Success()`. This allowed the Workspace membership write to commit while the required canonical Conversation participant state was silently skipped, leaving split authorization state.

Remediation:

- unavailable synchronization now returns a dependency failure;
- member add, role/status update, and removal do not save the unit of work when synchronization fails;
- the HTTP boundary maps the typed dependency failure to service unavailable;
- a dedicated contract test proves the null dependency fails closed.

### F03-SEC-007 — Task File responses used the generic Project redaction profile

**Severity: High for Confidential/Restricted metadata; Medium otherwise**  
**Status: Remediated in PR #324**

`CanonicalProjectsResponseProjectionFilter` applied `UiDetail` to every successful `ProjectsController` response. `TaskFileAssociationResponse` and `TaskFileAssociationPage` include `FileName`, so Task readers could receive File metadata without the mandatory `FileMetadata` field-policy projection.

Remediation:

- the global Projects response filter selects `FileMetadata` for Task File response/page DTOs;
- all other Project responses remain on `UiDetail`;
- the canonical File metadata engine recursively redacts nested page items;
- a dedicated test verifies both profile selection and nested filename redaction.

## 6. Reviewed areas with no additional No-Go finding

### Canonical Project create

The create path revalidates current Tenant/Workspace membership, Group scope, delegated capabilities, requested Visibility, idempotency, owner membership, audit, and outbox within the canonical transaction. No new cross-Tenant or partial-write blocker was found in Final03 review.

### Project activation

Activation revalidates Project/Workspace state and authorization inside the serializable unit of work, uses expected version checks, stages Project `general` and Task workflow defaults, and commits audit/outbox with activation. No new Final03 blocker was found.

### Capability grants

Evaluation revalidates active Tenant membership, user state, scope identity, revocation, and expiry. Workspace-scoped grants also revalidate current Workspace state/membership. No new Final03 blocker was found.

### Notification open/navigation

Notification open reauthorizes the current target before read-state mutation and outbox commit. Artifact and Message navigation use current authoritative routes rather than persisted historical `targetRoute`. No new Final03 blocker was found.

### Workspace File listing

The Workspace File list repository returns Workspace-owned attachments only; it does not mix Project/Conversation/Channel attachment rows into a Workspace-wide page. Record authorization plus the hardened File metadata projection therefore does not expose Restricted Project rows through this endpoint.

### Archived and LegacyUnknown state

Archived Workspace access and restore remain explicit-member/owner constrained. Project `Visibility == null` and legacy activation state are not inferred as canonical write authority. File, Task, Milestone, Gantt, review, and comment mutation boundaries fail closed for non-activated/legacy states.

### WPC-Final01 / WPC-Final02 / current-main integration

WPC-Final01 is merged before WPC-Final02, and the current `main` is `74cca8756bac69061c676d924fcb74a6edf3123b`. The only post-Final02 main movement reviewed here is merged UI Shell PR #326, whose diff is confined to frontend App Shell, FeatureMenu, RightPanel, and component-token files. It does not touch Final03 authorization, membership, persistence, redaction, or backend CI ownership surfaces. PR #324 has merged this current main into the audit branch. The current Final03 production authorization/redaction changes were re-read against that integrated baseline; no additional source-level collision blocker was identified. Any subsequent movement of `main` before merge requires the same final collision check again.

### Unrouted Task subresource summary helper

`TaskSubresourceService.GetSummaryAsync` currently has no direct Controller route or repository caller. It was recorded as a future hardening point, but no current external reachability was found and it is not classified as a Final03 merge blocker.

## 7. Acceptance coverage

### Unit/contract tests

- `WpcFinal03FileAuthorizationTests`
  - WorkspaceVisible reader cannot upload;
  - explicit contributor can upload;
  - revoked historical uploader cannot delete;
  - current uploader may delete own attachment;
  - explicit current Project manager may moderate another attachment.
- `WpcFinal03FileMetadataRedactionTests`
  - default policy redacts all mandated File metadata fields;
  - explicit Confidential policy discloses them;
  - unknown authorization fails closed;
  - application DI resolves the hardened redaction boundary;
  - Task File response/page selects `FileMetadata` and recursively redacts nested filenames.
- `WpcFinal03WorkspaceMembershipBoundaryTests`
  - missing current Tenant membership is rejected before service mutation;
  - an active but mismatched Tenant membership record is rejected;
  - active exact-Tenant membership is forwarded;
  - Workspace `general` synchronization rejects a suspended Tenant membership;
  - unavailable Workspace `general` synchronization fails closed.

### PostgreSQL acceptance

- `CrossTenantUserCannotBePersistedThroughWorkspaceGeneralAdmission`
  - uses PostgreSQL 18 and current migrations;
  - creates two Tenants and a user who belongs only to the other Tenant;
  - verifies the canonical synchronizer rejects the staged Workspace membership;
  - verifies no invalid Workspace member row is persisted.
- `WorkspaceVisibleReadDoesNotBecomeProjectFileOrTaskMutationAuthority`
  - verifies a Workspace ReadOnly reader may read a WorkspaceVisible Project but cannot create Tasks, comment, or upload;
  - verifies an explicit Project Viewer cannot create/update/review/comment/upload even when historical creator/assignee/reviewer data matches;
  - verifies an explicit current Project Contributor with reviewer assignment can create, update, review, comment, and upload;
  - verifies Completed remains readable but not mutable;
  - proves the persistence interceptor rejects a direct Task write in Completed state.
- ordinary PostgreSQL Task concurrency coverage includes the delete/restore child race and verifies canonical Restore reaches the optimistic-concurrency boundary rather than being rejected by the generic live-row assignment primitive.

Dedicated gate:

- `.github/workflows/wpc-final03-security-acceptance.yml`
- PostgreSQL 18 service;
- `Scope=WPCFinal03` filter;
- exactly named required-test verification;
- minimum 17-test enforcement;
- TRX artifact retention.

## 8. Scope isolation

This Final03 PR does not modify:

- EF migrations or `AppDbContextModelSnapshot`;
- legacy migration inventory/rollout evidence;
- WPC integration acceptance matrices owned by Final01;
- migration/legacy execution material owned by Final02;
- unrelated frontend behavior.

The additional Infrastructure/Web changes are limited to the existing Project governance interceptor and canonical Projects response projection filter because those are the authoritative persistence and HTTP redaction boundaries for the confirmed findings.

Temporary repair workflows used while stabilizing CI and the Restore authorization boundary have been removed from the final PR diff. No workflow with branch-write permission is retained for that repair.

## 9. Remaining merge gates

The PR must remain Draft until all are true:

1. `WPC-Final03 Security Acceptance` passes on the final exact PR head;
2. repository build/test, code quality, npm security, WPC-Final01 composite acceptance, and existing WPC PostgreSQL gates are green or proven unrelated on that same final head;
3. the final PR head is still based on the current `main`, or any later `main` movement has been collision-checked and re-audited;
4. PR changed-file and review-thread collision checks are clean;
5. this document is updated from `CONDITIONAL NO-GO` to the final evidence-backed decision only after items 1–4 are satisfied.

The source-level merge blockers identified during the Final03 merge audit are closed at the time of this update. The remaining gate is evidence: exact-head CI plus the final current-main/review-thread check. A passing unit test set alone is not sufficient. PostgreSQL-backed acceptance and current-main revalidation are mandatory.
