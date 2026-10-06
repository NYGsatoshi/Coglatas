# API source audit for the rejected bb9 cohort

Audited source: `bb9e339f118e4cef58a4804ce4ccaa188325e0b9`.
Fresh GitHub audit on 2026-10-07 confirms Main
`5d2ccd260353608b47e7aeb0951f5c85c227aa76`, PR #1106 open/unmerged at that head,
five required contexts successful and `performance-fast` failed. CODEOWNERS is
owner-only (`* @NYGsatoshi`). #1046 remains Draft/Open/unmerged.

## Attribution

The four ordinary unstable decisions remain **UNKNOWN**. The diagnostic has
scalar EF command counts, not SQL fingerprints. A repeated-query defect can be
demonstrated by source and deterministic tests without proving that it caused
the particular ordinary-cohort MAD failure. GC, queue and worker overlap do not
establish attribution; no positive throttling delta was observed.

| Scenario | Observed expensive/repeated operation | Per-request count | Deterministic? | Candidate variance mechanism | Evidence strength |
| --- | --- | --- | --- | --- | --- |
| task.detail / p99 | Nested Task permission calls repeat Task, Project, Workspace/member/user and Project-member reads; subresource readers reauthorize the same parent | 157 scoped EF commands in all 100 diagnostic requests; individual SQL counts unavailable | Command count was invariant in this fixture; membership/lifecycle/relationship branches can differ with data | Repeated network round trips amplify DB/service-time variation; this is a hypothesis, not MAD attribution | Strong source evidence of repetition and measured total; unknown contribution to rejected p99 |
| task.detail / p99 | ToResponseAsync reads every Project Task with WorkflowStage/Collaborators to compute direct-child dates/progress and subtask count | One whole-Project materialization per canonical response; cardinality grows with unrelated Tasks | Deterministic for fixed data; allocations grow with Project size | Unnecessary entity/collection allocation and reader-drain work could increase GC/service-time sensitivity | Strong source-level boundedness defect; no isolated GC causality |
| task.detail / p99 | Relationships read twice; checklist and labels read for summary and detail; user/time-zone/file authorization mapping may query per item | Two relationship/checklist/label passes; mapping count depends on collaborators, subtasks and files | Fixed for fixed page/data; data-dependent branches | More repeated round trips and allocations | Strong source evidence; diagnostic does not attribute commands individually |
| notification.list / p95 and p99 | Generic-target Any fence, authorized Count, ordered Skip/Take; protected rows use SQL authorization subqueries | 6 scoped EF commands in all 100 diagnostic requests including shared request pipeline; list path is 3 commands for the generic fixture | Fixed command count; SQL work depends on recipient/target data | DB/service-time variation remains possible; no per-item loop on the production SQL path | Strong evidence that existing bounded repair is present; specific instability cause UNKNOWN |
| workspace.detail / p99 | Workspace membership with parent/user, active SystemAdmin check, exact Workspace read | 6 scoped EF commands in all 100 diagnostic requests including shared request pipeline; member-present active endpoint path is 3 | Fixed for fixture; missing membership adds parent fallback | DB/service-time variation remains possible; no collection growth on this DTO | Strong bounded source evidence; specific instability cause UNKNOWN |

## Request paths

All three requests pass TenantResolutionMiddleware/HttpTenantResolver (tenant
lookup), cookie ValidatePrincipal/UserSessionService (session with User and
active TenantUser with Tenant), then MVC authorization. LastSeen persistence is
conditional on the five-minute interval; it is a possible shared branch, not a
demonstrated cause of these failures. Session revocation and tenant membership
checks must stay live. Global AppDbContext tenant filters apply to the reads.

### task.detail

ProjectsController.GetTask -> TaskSubresourceService.GetDetailAsync ->
TaskCommandService.GetAsync -> ProjectAuthorizationService.CanViewProject ->
WorkspaceAuthorizationService.CanViewWorkspace. GetTaskAsync includes Stage and
Collaborators. Canonical mapping performs RelationshipsAsync, five separate Task
capability evaluations, stage categories, full Project Task read, time-zone
resolution, checklist/label reads and comment Count. CanDeleteTask and
CanOverrideTaskReview each delegate to CanAssignTask and repeat its full path.
CanUpdate/CanReview also repeat mutation-scope and management evaluation.

The composite then performs VisibleTaskAsync, detail capabilities, relationships,
watch, checklist, labels, direct-subtask Count/page, comment page/Count/mention
IDs and attachment Count/page. These readers use the same parent authorization;
subtask mapper reads assignee/time zone per item; active clean file mapping
performs live view/download authorization. Deleted comments retain tombstones.
Mutation methods and separate subresource routes must retain their boundaries.

### notification.list (both unstable percentiles)

NotificationsController.List -> NotificationApplicationService.ListAsync ->
DbNotificationService.ListAsync. Recipient and tenant filters precede a
CurrentlyVisibleQueryAsync Any probe. Generic-only rows retain an SQL exclusion
fence against concurrent protected insertions. Protected Task/Artifact/Message/
digest authorization comes from CanonicalCurrentAuthorizationTargetResolver,
the existing resolver and VisibleProjectsFor inside SQL. Count precedes the page;
CreatedAt descending then Id descending fixes ties. ToListItem maps read state
directly, without acknowledgement lookups. Production DI supports the SQL query;
the bounded scan fallback is for resolvers lacking that support. No later N+1
was found on the observed generic fixture. No speculative notification change
is justified.

### workspace.detail

WorkspacesController.Get -> WorkspaceService.GetAsync ->
WorkspaceAuthorizationService.ResolveAccessAsync ->
WorkspaceRepository.GetMemberWithWorkspaceAsync, then active User check and
WorkspaceRepository.GetByIdAsync. Missing membership uses an exact parent
fallback. Archived access requires active membership, even for SystemAdmin.
ToDetail emits scalar identity/name/status/time fields: no projects/groups,
aggregate counts, member lists or lazy navigation. No speculative change is
justified.

## Mapping, telemetry and caching

Controllers return DTOs through CanonicalRedactionProjection. Serialization does
not receive EF aggregates. EF lazy-loading proxies are not configured. The
composite Task mapping allocates lists/entities before serialization; EF tracked
identity resolution does not eliminate FirstOrDefault database commands.
No endpoint result or authorization cache was found. Ordinary read paths do not
write business audit events. Shared request logging and optional Test-only
diagnostic interception are distinct; process-wide observations cannot be
assigned to a single request. The diagnostic will not be rerun.

## Established repairs and proposed scope

Merged Project/Task list repair (`b9f6db17`, equivalent merged lineage) uses
Count/page projection, SQL child aggregates and batched create-capability IDs.
Collaboration repair (`807ac0e2`) applies authorized Conversation IDs before
Count/page and bounded Announcement projection. Notification repair
(`49a4671c`, reviewed predecessor `9d4198d4`) retains the generic SQL fence and
protected authorization. Prefer these bounded read patterns.

The smallest justified Task repair will consolidate the five read-time Task
capability evaluations while retaining the original command primitives, and
replace whole-Project Task materialization with exact-parent scalar aggregates.
No long-lived authorization cache, API/fixture/tool/workflow/budget/MAD/sample
change, baseline replacement or unrelated endpoint optimization is proposed.
Focused equivalence, lifecycle/tenant/authorization and PostgreSQL command-growth
tests must pass before a single new-source protected workflow is permitted.

## Immutable evidence

Rejected run `37473201044/1`, artifact `11419320460`, original ZIP SHA-256
`117e1054158fa659102b4c27888f0e21fc3163e9c1185ae5ca6ff025d62adfba`, retained commit
`914f6f5b425511cf920409ed9779561302378641`: 74/78 PASS and four UNSTABLE.
Workspace p99 MAD `0.2413591424904104`; Task p99 `0.2144363146926152`;
Notification p95 `0.25772149714514536`, p99 `0.21322065967237847`.

Diagnostic `37479637862/1`, artifact `11420154028`, ZIP SHA-256
`d916fd9084f6951e58131d39adb031018504a5f864953b4d308052a6e921af4e`, retained commit
`f88ae8cdff4fefac8d164fad34d3ba9a38cea387`: five groups, 1,300 ordered traces,
zero gate/baseline credit. Neither evidence tree is edited or relabeled.

Medium rollout and its successor declaration remain blocked until #1106 has all
six genuine exact-head required checks. Small stays frozen. #1046 and final
duration acceptance remain deferred. No Avalonia/ProjectIDE implementation.

## Implemented repair and deterministic validation

Only TaskCommandService.GetAsync opts into the new read projection. Existing
mutation response paths and authorization primitives are unchanged.
ProjectAuthorizationService.GetReadCapabilitiesAsync evaluates the original
mutation-scope and management boundaries once, shares those facts for the five
read permissions, and does not retain them across calls. The repository projects
the exact parent, direct-child aggregates and existing checklist/comment/label
counts into one scalar row, using the existing Task-list aggregate formula.
Repositories without that projection retain the original compatibility path.

The PostgreSQL composite fixture returns byte-equivalent serialized DTOs through
the old repository/authorization path and the new path. Its scoped command count
falls **179 -> 119**. Adding 300 unrelated Project Tasks leaves the fixed command
count unchanged; none of those entities materializes in the composite. The
summary uses one command and at most two reader attempts (one row plus end).
This fixture differs from the hosted diagnostic: do not label 119 as a measured
replacement for its 157 or infer a new ordinary-cohort MAD value.

Validation before hosted acceptance: 396 focused backend tests PASS with zero
skips and real PostgreSQL 18.6 available; the final seven new regressions PASS.
Coverage includes canonical response equivalence, original capability primitives
across roles/visibility/lifecycle/revocation and separate reviewer/assignee/
SystemAdmin boundaries, cross-tenant and not-found equivalence, child aggregation,
query growth, existing endpoint/HTTP/tenant/authorization tests and PERF-05 DB
capture/pagination contracts. The complete existing performance policy suite
is 205/205 PASS; API transport tests 7/7 PASS. Contract/environment/API validators,
fresh authenticated campaign registry validation and the one-approved-Small
baseline validator PASS. Windows checkout line endings were restored to original
Git blob bytes before byte-immutability validation; no evidence blob changed.

The new candidate must expect changed Task-detail query/allocation behavior while
preserving its response and permissions. Notification and Workspace source and
query behavior remain unchanged. All API scenarios remain semantically identical.
The four historical instability causes remain UNKNOWN. No protected acceptance
run is credited until its exact new head finishes; the unchanged bb9 cohort is
never rerun.

## Additional live governance observation

Fresh ruleset `22302146` still requires one native approving CODEOWNER review,
whereas the current repository owner-only policy requires zero native approving
reviews and no external approval for owner-authored PRs. The sole CODEOWNER is
also #1106's author. Live rulesets list an always-bypass actor, while
GOV-BYPASS-001 permits none. The integration cannot read classic branch
protection (403); the two active rulesets were read successfully. No settings
are changed, independent reviewer invented or bypass used by this repair. Any
normal merge must also satisfy actual live review/ruleset requirements.
