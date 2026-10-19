# Student council pilot: Workspace membership scope audit

Audit date: 2026-10-19 UTC. Source inspected at
`c56cab73f74d16dd95fc02cf6d62233847f3c478`, based on Product Main
`6f940987f8df746736b6df5ea7371ab698028f5e` plus the pilot shell changes.
This is a source audit, not a new HTTP, PostgreSQL or release acceptance result.

## Acceptance status

| Requested operation | Current server contract | Angular state | Pilot qualification |
| --- | --- | --- | --- |
| Authorized Workspace list and selection | Authorized dashboard plus existing WS-02 selection boundary | Implemented, including direct card selection | Runtime evidence remains in the integrated release gate |
| Workspace member list | Ordinary minimal member projection, current active members only | Implemented with local search and server-derived roles | Supported read surface; full W10 inspector is incomplete |
| Existing-user addition | `POST /api/workspaces/{id}/members`, `{ userId, role }` | No live addition flow | Incomplete; no membership-operation capability or permitted-role projection |
| Role change | `PATCH /api/workspaces/{id}/members/{userId}`, `{ role, status? }` | No live mutation flow | Incomplete; no membership-operation capability or permitted-role projection |
| Revoke access | `DELETE /api/workspaces/{id}/members/{userId}` | No live mutation flow | Incomplete; no removal capability projection |
| Ordinary member leaves Workspace | No separate self-leave contract; DELETE retains management authorization even for the actor's own ID | Unavailable | Unsupported by the current ordinary-member command contract |
| Exact canonical member-role display | Server enum has Owner, Admin, Adviser, Member and ReadOnly | Member directory collapses Admin/Adviser to Teacher and ReadOnly to Viewer; unknown roles become Member | Partial; do not claim exact canonical role fidelity |
| Email invitation | Distinct token acceptance flow exists; the Tenant endpoint named `users/invite` adds an existing User ID | No Workspace email-delivery flow integrated here | Do not describe existing-user addition as email invitation |

## Exact command and authorization evidence

- `src/Coglatas.Web/Controllers/WorkspacesController.cs:247` maps existing-user
  POST, PATCH and DELETE commands. POST additionally calls
  `IsActiveCurrentTenantUserAsync` (`:290`), requiring an exact active current
  Tenant relationship and active, non-deleted User before forwarding the command.
- `src/Coglatas.Application/Workspaces/WorkspaceService.cs:457`, `:516` and
  `:561` implement add, update and removal. Each requires authenticated identity,
  current Workspace visibility, a non-archived Workspace and
  `CanManageWorkspace`. Each stages the required WorkspaceGeneral membership
  synchronization, audit and authorization invalidation before saving.
- Removal sets the membership to `Suspended`; it does not delete historical
  membership. The current command has no `actor == target` self-leave exception.
- `src/Coglatas.Application/Workspaces/WorkspaceAuthorizationService.cs:45`
  owns management authority. The browser must not duplicate its role or
  SystemAdmin rules. Ordinary Workspace visibility alone does not grant it.
- `src/Coglatas.Application/Workspaces/WorkspaceDtos.cs:127` defines add as
  User ID plus the existing WorkspaceRole enum; update at `:129` accepts the
  existing role and optional MembershipStatus. Neither command supplies an
  operation-capability read model or an allowed-role selection model.

## Missing authority projection and candidate discovery

The current dashboard DTO (`WorkspaceDtos.cs:36`) exposes `CanOpenMembers`,
sharing booleans, and separate create/file capabilities. Its page capability
DTO (`:79`) contains only `CanCreate`. It exposes no `CanAddMember`,
`CanChangeRole`, `CanRemoveMember` or `CanLeave` decision.

`WorkspaceDashboardQuery.cs:277` explicitly describes navigation as read-only.
`CanOpenMembers` is current read access; it is not membership management.
`CanManageSharing` and `CanInspectSharing` have sharing semantics. Reusing those
fields as membership-command permission would invent a new contract even if
some current server predicates happen to coincide.

`WorkspaceMemberProjectionService.cs:74` and `:93` provide management list/detail
reads. Their authorization rechecks `CanViewWorkspace` and
`CanManageWorkspace` (`:110`), but their response
`WorkspaceMemberManagementResponse` (`WorkspaceDtos.cs:118`) contains member
identity, role, lifecycle, account status and joined time only. A successful
sensitive read must not be silently promoted into a per-target mutation or
self-leave capability. These endpoints are not a mutation-options projection.

The existing Tenant user directory
(`TenantService.cs:244`, `GET /api/tenant/users`) independently requires Tenant
management authority. Workspace management is not Tenant management. No
Workspace-scoped existing-user candidate picker contract was found in the
inspected Workspace routes, DTOs or use cases. The ordinary member directory
contains current Workspace members, so it cannot enumerate users not yet added.

Consequently this audit adds no membership mutation controls, new endpoints,
schema, capability keys, role semantics or inferred authorization. A reviewed
server-owned operation/candidate/allowed-role presentation contract is required
before those missing controls can be integrated under the current pilot rules.
Ordinary-member self-leave additionally needs an explicit approved command
policy; an existing removal command cannot be relabeled to create that policy.

## Angular read and switching evidence

- `/workspaces/:workspaceId/members` uses `workspaceContextGuard`
  (`frontend/src/app/app.routes.ts:131`). `members.facade.ts:119` reads only the
  ordinary member endpoint. It cancels and clears protected rows on scope or
  authorization changes and registers current-scope HTTP catch-up.
- `members.facade.ts:281` filters actions to `openMemberDetail`. The live row
  adapter (`:284`) marks that detail unsupported, so its rendered action is
  disabled. The mock type's `changeRole` and `disableMember` names are not
  evidence of implemented commands. `submitAuditReason` in
  `workspace-members-page.component.ts:139` stores a local reason; it issues no
  membership request and cannot establish that a member was disabled.
- `members.facade.ts:318` owns the coarse display mapping described above.
  `member-role-badge.component.ts` and the mobile page consume that mapping.
  This audit introduces no replacement mapping or privilege inference.
- Workspace cards use the server's existing `openWorkspace` presentation flag
  and delegate to `WorkspaceSelectionFacade.selectWorkspace` (`:164`). That
  boundary checks the latest authorized map and identity, navigates away from
  a prior scoped route before switching, rechecks after awaited navigation,
  clears protected state before setting the new scope, stores only the scoped
  opaque preference and starts authoritative HTTP catch-up. Selection is not
  proof of membership-management authority.

## Invitation distinction and verification limits

`TenantAdministrationController.cs:54` forwards `users/invite` to
`AddCurrentTenantUserAsync` with User ID and Tenant role; that endpoint is not
email delivery or Workspace addition. Separately, current
`AuthService.RegisterByInviteAsync` uses an acceptance-owned transaction and
`EnsureTenantMembershipAsync` / `EnsureWorkspaceMembershipAsync` (`:412` / `:447`).
The earlier blanket claim that token acceptance never creates memberships is
stale; see `issue-527-invite-acceptance.md`. This source audit does not qualify
invitation generation, delivery, token provisioning or target-host acceptance.

Existing regression source includes `WorkspaceMemberPrivacyProjectionTests`,
`WpcFinal03WorkspaceMembershipBoundaryTests`, `Wpc02AWorkspaceAuthorizationTests`
and conditional `Ws01WorkspaceDashboardProjectionPostgreSqlTests`. Their
presence is not execution evidence. No runtime command or newly configured
PostgreSQL test was run for this documentation-only audit. The pilot release
report must keep membership read/selection separate from missing add, role
change, revoke and ordinary self-leave UI acceptance.
