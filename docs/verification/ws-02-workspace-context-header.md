# WS-02 Workspace context header verification

Status: Issue #328 implementation candidate, locally verified before commit.
Provider-backed PostgreSQL execution, hosted checks, and merge state are not
claimed by this documentation edit.

## Change identity

- Branch: `feat/328-workspace-context-header`.
- Starting `main` and worktree HEAD before the candidate was committed:
  `c8e4e411f2a9c99c1d01ae2cda79911fcd3e3f79`.
- Canonical specification source reviewed at
  `Coglatas-Spec@38339ba2964587f225c4c4151f643abb5523e862`.
- The exact final PR head and hosted check results must be recorded in the PR
  because this file cannot name the commit that first contains itself.

## Canonical active Workspace resolution

The client reconciles candidates only against the latest authorized active
Workspace dashboard projection. The order is:

1. a valid Workspace explicitly identified by the current Workspace route;
2. a valid last-used preference scoped to the current `{tenantId, userId}`;
3. automatic selection when exactly one authorized active Workspace exists;
4. explicit user selection when multiple Workspaces remain.

The implementation does not select `cards[0]` or any other arbitrary API row.
`AuthService` also returns `currentWorkspace` only for a sole authorized active
Workspace. A stale/revoked/archived preference is removed when it is absent
from the current authorized list. An invalid route candidate cannot activate a
Workspace outside that list. Zero authorized Workspaces produces unavailable
state; multiple unresolved Workspaces produce selection-required state.

The last-used key contains only an opaque Workspace ID and is partitioned as:

```text
coglatas.workspace.last-used:<encoded tenantId>:<encoded userId>
```

Storage access is guarded. Missing browser storage or read/write/remove
exceptions do not create authority or bypass explicit selection. The preference
is UX state only; every resource operation remains server-authorized.

## Clear-before-activate boundary

Loading a new authorized list invalidates the old Workspace scope. When the
selected Workspace changes, the boundary clear runs before the new active ID is
set. An explicit switch from Workspace-specific content first navigates to the
neutral `/workspaces` route; failed navigation leaves the old selection in
place.

The shared boundary currently clears registered protected state for:

- active Project/Task/Kanban/Gantt and My Tasks projections;
- Messaging conversation IDs, HTTP generations, realtime ownership, and
  unscoped pending list-restoration state while retaining message drafts only
  across same-user Workspace switches; draft keys include Tenant, user,
  Workspace, and conversation and are cleared at session/Tenant boundaries;
- Files page/picker state, uploads, downloads, timers, and late-response
  generations;
- right-panel notification/member/scope state plus in-flight list, open, and
  mutation requests;
- Workspace-private Task notification preference state and notification-open
  context.

Workspace-, Project-, and Conversation-scoped realtime intents, catch-ups,
stale guards, authorized groups, dedupe state, and aggregate versions are
removed. Server-derived user and Tenant subscriptions may remain on the same
authenticated Tenant transport. If a removed resource authorization completes
late, the client immediately unsubscribes it instead of restoring the old
scope.

## Backend projection compatibility

`GET /api/workspaces` remains a direct authorized array. WS-02 adds these
fields to each dashboard item:

- `runningProjectCount`: visible `ProjectStatus.Active` count;
- `needsReviewProjectCount`: visible `ProjectStatus.Review` count.

The existing `inProgressProjectCount` remains additive compatibility and equals
their sum. The grouped status split stays inside the existing fourth sequential
dashboard query; it does not add a per-Workspace query. Counts are derived only
after the canonical Project visibility predicate. An authoritative zero remains
numeric zero, while absent/malformed frontend data maps to unavailable rather
than fabricated zero.

Workspace card/header actions remain backend projections. In particular,
Members appears only when `canOpenMembers` is true. UI visibility does not grant
access to the destination and no server authorization boundary is weakened.

## Header and accessibility contract

The authenticated header has three distinguishable regions:

- Workspace context: labelled native Workspace selector and selection state;
- Research state: textual `N Running · M Needs review`, or `Status unavailable`;
- actions: capability-derived Workspace actions separated from global
  Notifications, Account, and Logout actions.

The Research text maps the backend's visible Active/Review Project aggregates;
it is not a separate execution-job status contract. State is conveyed in text,
not color alone. Zero and unavailable have different presentations.

The header does not add Share or Settings without an authoritative capability
and destination, and it does not place Create Workspace or another competing
primary CTA in the header. The obsolete page-search control is removed.

The native selector and actions have visible focus treatment and a minimum
44-pixel target. At narrow widths the three-column header becomes one column;
the action groups wrap, the global separator changes orientation, and controls
remain reachable. Component and Playwright source cover keyboard selection,
focus traversal, 320-pixel reachability, and horizontal-overflow detection;
their final execution is a merge gate below.

## Issue #328 acceptance mapping

| Acceptance criterion | Candidate evidence |
| --- | --- |
| Workspace name/context and Workspace actions are grouped | Labelled selector/status region plus a separate `Workspace actions` navigation group |
| Global and Workspace actions are visually separated | Separate accessible navigation landmarks and a bordered group boundary that becomes a top separator when narrow |
| Research state is understandable without color | `Running`, `Needs review`, and unavailable text with `aria-live="polite"` |
| Equivalent-strength CTAs do not compete in the header | No create/share/settings primary CTA; only scoped navigation and global utility actions |
| Primary actions remain available at narrow width | Wrapping 44-pixel controls plus 320-pixel visibility, keyboard, and overflow regression source |

## Verification inventory and remaining gates

The candidate adds or extends focused source coverage for:

- Tenant/user preference partitioning and storage-denial behavior;
- route/preference/sole/explicit precedence and stale-ID rejection;
- clear-before-activate ordering, resource-subscription removal, and late
  authorization cancellation;
- Workspace, Files, Messaging, My Tasks, notification/right-panel, and active
  request-generation clearing;
- additive DTO serialization and Active/Review/legacy aggregate semantics;
- grouped header actions, explicit Research text, zero versus unavailable,
  capability failure, keyboard operation, and 320-pixel layout.

Local verification on the candidate worktree completed as follows:

- focused backend Auth/Workspace tests: 34 passed, 0 failed;
- full backend suite: 873 passed, 237 skipped, 0 failed (1,110 total);
- focused Workspace/messaging/navigation regression set: 86 passed, 0 failed;
- focused realtime synchronization regression set: 49 passed, 0 failed;
- focused notification current-authorization regression set: 34 passed, 0 failed;
- full Angular suite: 62 files / 556 tests passed;
- Angular production build: passed (existing bundle/style budget warnings);
- frontend architecture check: passed;
- pinned Linux Angular Playwright: 78 passed, 6 intentional skips, with the
  approved screenshot baselines unchanged by the final run;
- `git diff --check`: no whitespace errors (Windows CRLF conversion notices
  only).

The Linux baseline update performed during implementation changed only
`mobile-shell-workspaces-drawer.png`; it was visually inspected as the intended
header projection behind the unchanged drawer and then passed a clean pinned
Linux rerun without update mode.

Provider-backed coverage includes the conditional
`Ws01WorkspaceDashboardProjectionPostgreSqlTests` split-count assertions. On
the documentation-edit host, `POSTGRES_TEST_CONNECTION_STRING` was absent.
Those cases therefore cannot be counted as locally executed PostgreSQL
evidence; the 237 skipped backend tests include environment-conditional cases.
CI or another disposable PostgreSQL run must execute the required provider
coverage before merge.

## Scope confirmation

- No schema, migration, authorization-policy, create command, or destructive
  data change is part of WS-02.
- The DTO change is additive and retains the legacy aggregate field.
- Workspace creation UI remains Issue #408; Project creation and Task creation
  remain their respective downstream issues.
- Share and Settings are not fabricated without capability/route contracts.
- Local preference, counts, hidden actions, and realtime group membership are
  never treated as server authorization.
