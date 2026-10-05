# WPC-Final01 Integration Audit

Status: Corrected integration candidate; merge requires current-head CI evidence  
Audit date: 2026-08-22  
Implementation repository: `NYGsatoshi/Coglatas`\
Implementation baseline: `76a3e3122ab24474dc97bb8ac170e155104b64d4`  
Normative specification repository: `NYGsatoshi/Coglatas-Spec`\
Specification baseline: `38339ba2964587f225c4c4151f643abb5523e862`

## 1. Scope

WPC-Final01 is the bounded integration gate after WPC-02A through WPC-02F and the second redaction-adoption pass have merged.

The normative completion contract is:

- `WPC-DEC-026` through `WPC-DEC-034`;
- `docs/specs/coglatas-core-v4/03-acceptance/workspace-project-canonical-completion-acceptance.md`;
- the fail-closed security No-Go cases in that contract.

This record verifies integration and evidence. It does not claim that the whole product is production-ready, and it does not reopen the approved owner decisions.

## 2. Integrated predecessor set

| Work item | Merged PR | Integrated responsibility |
|---|---:|---|
| WPC-01 | #281 | Workspace-create transaction/idempotency foundation and shared authorization/read boundaries |
| WPC-02F | #288 | Artifact/Message click-time notification reauthorization and Angular protected navigation |
| WPC-02E-1 | #289 | Canonical redaction service and WPC error-envelope foundation |
| WPC-02A | #290 | Project Visibility, activation provenance, archived Workspace authorization, first WPC schema migration |
| WPC-02A corrective | #291 | Repository/interceptor corrections after integration review |
| WPC-02B | #301 | CapabilityGrant and canonical WorkspaceGeneral Conversation |
| WPC-02C | #308 | Canonical Workspace-scoped Project create |
| WPC-02D | #313 | Project activation, ProjectGeneral, and activation-time Task workflow |
| WPC-02E-2 | #319 | Canonical redaction adoption across WPC response/export boundaries |

The Final01 branch is rebased on the implementation baseline above. The corrective work in this revision is intentionally limited to gaps discovered by the pre-merge audit and their regression evidence.

## 3. Corrective findings closed by Final01

The pre-merge audit found four integration gaps that individual predecessor checks did not cover:

1. Historical `LegacyUnknown` Project Visibility had no explicit authorized, audited, concurrency-controlled classification command.
2. Task notification and Task/Project realtime delivery used a hand-written Project predicate that could be broader than the canonical `VisibleProjectsFor` read scope for `MembersOnly` and `Restricted` Projects.
3. ProjectGeneral participant rows were initialized at activation but were not synchronized when explicit Project membership was later changed or removed.
4. Explicit Project managers could reach membership mutations while the Project was Archived because restore authority and ordinary membership-mutation authority shared the same management predicate.

The corrective implementation closes those gaps as follows:

- `PUT /api/projects/{projectId}/visibility` is the explicit Visibility mutation/classification boundary. It requires `ExpectedVersion`, uses Workspace governance or `project.visibility.manage` for non-default Visibility, records `ProjectVisibilityChanged`, and stages authorization/realtime invalidation in the same save.
- `CanonicalCurrentAuthorizationTargetResolver` is an AND-only fence over the existing current-state resolver. Task notifications and Task/Project realtime must also satisfy `VisibleProjectsFor`; the fence can narrow but cannot widen existing authorization.
- Production Project membership mutation is routed through `IProjectService` -> `CanonicalProjectService` -> `ProjectMembershipService`. `ProjectGeneralMembershipSynchronizer` stages the Project-derived participant change, audit evidence, business invalidation, and authorization invalidation before the shared `ITaskCommandUnitOfWork.SaveTaskCommandAsync` persistence boundary commits them together.
- `ProjectMembershipService` rejects Archived/Deleted Project member add/update/remove before any membership, participant, audit, or outbox mutation can commit. There is no second production SaveChanges interceptor for this responsibility.

## 4. Decision-to-evidence matrix

| Decision / acceptance area | Implementation owner | Required evidence bound by Final01 |
|---|---|---|
| WPC-DEC-026 Project Visibility | 02A + 02C + Final01 corrective | LegacyUnknown migration, database constraints, explicit classification, optimistic concurrency, non-default authority, positive `project.visibility.manage` authority, canonical create defaults |
| WPC-DEC-027 activation provenance | 02A + 02D | historical unknown state, exact suspend/archive recovery, atomic explicit activation, stale/concurrent failure behavior |
| WPC-DEC-028 archived Workspace/Project read-only boundaries | 02A + Final01 corrective | current-member historical read, Owner-only Workspace restore, Admin/SystemAdmin negatives, Archived Project membership mutation denial |
| WPC-DEC-029 CapabilityGrant | 02B + 02C + Final01 corrective | persisted scoped grants, current revalidation, revoked/expired/cross-scope denial, positive `project.visibility.manage` mutation authority |
| WPC-DEC-030 WorkspaceGeneral | 02B | canonical identity/uniqueness, least-privilege membership mapping, create-transaction participation |
| WPC-DEC-031 Project create authority | 02C | Workspace-scoped route, Group-bound limits, no implicit SystemAdmin authority, idempotent atomic outcome |
| WPC-DEC-032 ProjectGeneral | 02D + Final01 corrective | one canonical Project Conversation, activation participation, role/removal synchronization without broad-viewer materialization |
| WPC-DEC-033 Task workflow | 02D | Workspace -> Tenant -> immutable fallback precedence, reuse of compatible state, fail-closed invalid state |
| WPC-02E redaction acceptance | 02E-1 + 02E-2 | all canonical profiles, data-classification policy, fail-closed context, safe error correlation, export reauthorization |
| WPC-DEC-034 notification/realtime current authorization | 02F + Final01 corrective | Artifact/Message navigation plus Task notification open/created-delivery and Task/Project realtime authorization using the canonical Project read boundary, including explicit `Restricted` non-member denial |

## 5. Schema integration order

The WPC schema-bearing work was integrated in the required serial order:

1. `20260816041835_Wpc02AProjectVisibilityAndActivationProvenance`
2. `20260817023749_Wpc02BCapabilityGrantWorkspaceGeneral`
3. `20260819070000_Wpc02DTaskWorkflowTemplates`

The Final01 corrective work adds no schema migration. WPC-02C, WPC-02E, and WPC-02F likewise do not add a competing WPC model-snapshot migration.

Final01 re-applies the current migration chain to PostgreSQL 18 and runs `dotnet ef migrations has-pending-model-changes`. A clean predecessor run is not substituted for the current-head result.

## 6. Final automated gate

`.github/workflows/wpc-final01-integration-acceptance.yml` provides one named integration result without replacing the existing WPC-02B/02C/02D checks.

The gate:

1. starts PostgreSQL 18;
2. restores and builds the Release solution with build-server isolation;
3. applies all migrations;
4. rejects pending EF model changes;
5. executes the WPC-02A through WPC-02F backend scopes plus the unscoped 02A authorization and canonical-redactor suites;
6. aggregates the A/B/C/D/E/F required-test manifests and verifies every required test name in the TRX;
7. runs the Angular unit suite, including `wpc-02f-notification-navigation.spec.ts`;
8. uploads backend TRX, restore log, and frontend unit-test log.

The A/D/F manifests bind the corrective classification, read-only membership, ProjectGeneral synchronization, and Task notification/realtime regressions. The composite WPC-02A evidence now includes an ordinary Workspace Member with a current Workspace-scoped `project.visibility.manage` grant successfully performing an explicit Visibility change. The composite WPC-02F evidence now includes a Workspace-only non-member being denied Task notification open, NotificationCreated delivery, Task realtime, and Project realtime for a `Restricted` Project. The existing B/C/E manifests remain the source of truth for their worker scopes.

## 7. Current-head rule

The merge authority for WPC-Final01 is the current PR head after this corrective revision. A previous green SHA, predecessor workflow evidence, a local run without `POSTGRES_TEST_CONNECTION_STRING`, or a workflow configuration review alone is not completion evidence.

The branch must remain based on the implementation baseline above (or a later reviewed `main`) so dependency/CI updates are not silently discarded.

## 8. Merge gate

WPC-Final01 is **NO-GO for merge** until the corrected current head has:

- `WPC-Final01 / PostgreSQL 18 + Angular` successful;
- the ordinary repository `CI` successful;
- all other required repository checks successful;
- every newly added corrective test present through the Final01 aggregate manifest/composite evidence and passing;
- no unresolved review thread that changes an authorization, migration, redaction, membership-synchronization, or navigation conclusion.

No auto-merge is authorized by this record.

After those conditions pass, the bounded Workspace/Project canonical completion work may be marked **GO**, subject to product-wide limitations outside WPC scope.

## 9. Residual documentation note

`docs/AI_CONTEXT.md` and `docs/KNOWN_ISSUES.md` may still contain broad pre-WPC-02 status wording. For WPC-DEC-026 through WPC-DEC-034, current source, the normative specification, and this exact-baseline verification record are the applicable evidence. Refreshing those broad summaries is documentation maintenance and must not be used to bypass this current-head gate.
