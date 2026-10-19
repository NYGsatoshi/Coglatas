# Student council pilot: Workspace membership scope audit

Audit date: 2026-10-19 UTC. Source inspected at
`c56cab73f74d16dd95fc02cf6d62233847f3c478`, based on Product Main
`6f940987f8df746736b6df5ea7371ab698028f5e` plus the pilot shell changes.
The original audit below is historical. The read-only detail implementation
checkpoint uses Product Main `f2820210fd0074a71a45ee00583fc897bf30b229` as its
base. Source and synthetic UI results do not establish HTTP, PostgreSQL or
public release acceptance; current execution receipts are recorded separately.

## Acceptance status

| Requested operation | Current server contract | Angular state | Pilot qualification |
| --- | --- | --- | --- |
| Authorized Workspace list and selection | Authorized dashboard plus existing WS-02 selection boundary | Implemented, including direct card selection | Runtime evidence remains in the integrated release gate |
| Workspace member list | Ordinary minimal member projection, current active members only | Implemented with local search and server-derived roles | Supported read surface; full W10 inspector is incomplete |
| Ordinary member detail | Existing `GET /api/workspaces/{id}/members/{userId}` | Read-only panel candidate reauthorizes through the ordinary endpoint, validates the returned identity and copies only display name and canonical role | Focused UI evidence is separate from real-backend and target acceptance; no capability or management inspector is claimed |
| Existing-user addition | `POST /api/workspaces/{id}/members`, `{ userId, role }` | No live addition flow | Incomplete; no membership-operation capability or permitted-role projection |
| Role change | `PATCH /api/workspaces/{id}/members/{userId}`, `{ role, status? }` | No live mutation flow | Incomplete; no membership-operation capability or permitted-role projection |
| Revoke access | `DELETE /api/workspaces/{id}/members/{userId}` | No live mutation flow | Incomplete; no removal capability projection |
| Ordinary member leaves Workspace | No separate self-leave contract; DELETE retains management authorization even for the actor's own ID | Unavailable | Unsupported by the current ordinary-member command contract |
| Exact canonical member-role display | Server enum has Owner, Admin, Adviser, Member and ReadOnly | The correction below preserves all five roles and represents unknown input as unavailable | Presentation candidate; focused execution and integrated qualification are recorded separately |
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

### Read-only detail implementation boundary

The new detail panel reuses the existing `WorkspaceMembersFacade` state owner.
Opening a listed member discards the previous body and performs a fresh ordinary
GET. It rejects a mismatched identity or missing display name. Request generations
and cancellation prevent replaced, closed, reloading or revoked detail responses
from restoring protected content. Session, Tenant, Workspace and authorization
clearing also remove the detail. The page additionally requires its route and the
committed active Workspace to match before opening or rendering protected rows
or the panel. A successful detail read refreshes only the matching directory
row's name and role. A masked 401/403/404 discards the scoped directory as well
as the detail body, cancels pending list delivery and offers an explicit fresh
GET recheck. Closing after denial restores focus to that recheck. No global
Workspace or session permission is inferred from the masked response.

The existing ordinary wire contract contains `userId`, `displayName`, `role` and
membership `status`. It excludes email, joined time, account status and capability
decisions. The panel copies only display name and role; membership status is not
promoted into account status or operation permission. No management endpoint,
mutation, candidate discovery, role option or self-leave contract is added.

Live read-only Penpot inspection at 2026-10-19T20:10:36.809Z confirmed CoglatasUI
revision441 and W10 board `e1d76e7e-ebca-80a9-8008-d017f5414675`. Its 376px
inspector contains expressly illustrative capability decisions without a current
ordinary server projection. The implementation adapts that width and existing
Approved Dark surfaces to minimal member details, using a stacked panel below
1440px. Directory column minimum widths keep the Details action within the
remaining desktop pane. It does not label illustrative capabilities Allowed/Denied or invent
invitation, history or permission data. Complete W10 parity remains incomplete.

Keyboard opening, composition-safe Escape, explicit close/focus restoration,
loading, denial, transient error and GET-only retry are verified separately.
Synthetic browser fixtures do not establish server compatibility; the extended
existing licensed Workspace P0 case must run against the exact integrated source.

### Management contract remains pending

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
- The audited baseline's `members.facade.ts:318` owns the coarse display mapping
  described below. `member-role-badge.component.ts` and the mobile page consume
  that mapping. The correction preserves server roles for presentation without
  making them membership-command capabilities.
- Workspace cards use the server's existing `openWorkspace` presentation flag
  and delegate to `WorkspaceSelectionFacade.selectWorkspace` (`:164`). That
  boundary checks the latest authorized map and identity, navigates away from
  a prior scoped route before switching, rechecks after awaited navigation,
  clears protected state before setting the new scope, stores only the scoped
  opaque preference and starts authoritative HTTP catch-up. Selection is not
  proof of membership-management authority.

## Canonical role presentation correction

The original directory combined Admin and Adviser into Teacher, renamed
ReadOnly as Viewer, and treated unrecognized values as Member. The candidate
instead reuses the existing Angular `WorkspaceMembershipRole` union and adds
only a presentation sentinel, `Unknown`. The adapter retains all five existing
server enum values, including their numeric compatibility encodings, and treats
missing, malformed and unrecognized values as unknown. It accepts no Teacher or
Viewer alias as a server role.

The desktop badge and mobile label render Owner, Admin, Adviser, Member and
Read only distinctly, with distinct Japanese translations. Unknown data renders
Role unavailable. Search matches both the exact canonical role identifier and
the current localized label; stale coarse mock labels do not drive filtering.
Synthetic fixture roles are aligned with the existing canonical enum.

The inspected consumers of this member-directory role are the member facade,
mock records, badge and member-page columns/mobile/search. None of those roles
feeds backend requests, authorization or capability checks. The existing
read-only row-action filter and disabled live detail action are unchanged.
No Domain enum, backend role mapping, capability key, permission semantics,
command, API or database contract changes.

Focused regression source covers all five textual/numeric/string-numeric
encodings, malformed and unknown roles, unchanged read-only actions, distinct
bilingual badge labels, unknown grid input and canonical/localized search.

Final presentation verification on 2026-10-19:

- Pinned shared Linux runner `mcr.microsoft.com/playwright:v1.64.0-noble`,
  `CI=true`, one Vitest worker: **46 tests passed across three files**, exit 0.
  The test bundle compiled in 6.320 seconds and execution took 4.16 seconds.
- The retained local receipt is `artifacts/role-linux-tests.log` (Git-ignored).
- The earlier host one-worker run also passed 46 tests before the final explicit
  empty-array table input and unused-label cleanup. The Linux run covers those
  final changes.
- Frontend architecture and whitespace checks passed. These component/HTTP-mock
  tests are presentation evidence; they do not establish a production backend,
  PostgreSQL, browser screenshot or public-HTTPS acceptance result.

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
