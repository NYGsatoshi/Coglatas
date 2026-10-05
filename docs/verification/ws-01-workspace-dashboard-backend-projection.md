# WS-01 Workspace dashboard projection verification

Status: the scoped WS-01 implementation and its authoritative Linux visual
baselines are complete on draft PR #403. The repository merge gate remains
NO-GO until every required check is green on the exact final PR head. The
remaining conditions are recorded under **Remaining merge gates**.

## Change identity

- Resolved implementation `origin/main` at the start of this repair:
  `eb42c46f2994eea9f5f1de06dd0a408763991f6b`.
- Starting branch/PR head:
  `abe99cd20ba6e9f1d086f9d4a4d686d2a2a7274d`.
- Specification repository `main` verified during preflight:
  `38339ba2964587f225c4c4151f643abb5523e862`.
- PR #324 was already merged as
  `79197305feb9889f7d442797ec3562e6bb5a077a`.
- The branch was merged normally with the successive live `main` tips. The
  latest integrated base while this evidence was prepared was
  `42dcc9f12fa4b2f7f1dee8eaa7f962690ccc5efa`.
- The complete runtime/test correction was tested at
  `b68773a710cf2b4e0614a4eb193396ebfe9059bd`. The following integration merge,
  `07b3d4367101f1fc461cd6ffe6c5cc1b54959b71`, has the same source/test tree and
  adds the latest `main` ancestry.
- PR #403 targets `main` and remains Draft. It must not be merged while any
  required check is red.

The exact live PR head and final GitHub check results are maintained in the PR
body because an evidence file cannot contain the SHA of the commit that first
introduces that same file content.

## Canonical API and product decisions

`GET /api/workspaces` remains the raw array of active Workspaces visible to the
current user in the resolved Tenant. The additive item fields are:

- `id`, `name`, `description`, `icon`, `status`, `createdAt`, `updatedAt`;
- `currentUserRole`, `accessSource`;
- `canOpenWorkspace`, `canOpenMembers`, `canOpenProjects`;
- `unreadAnnouncementCount`, `unreadConversationCount`,
  `inProgressProjectCount`.

The canonical membership roles are `Owner`, `Admin`, `Adviser`, `Member`, and
`ReadOnly`. `Manager` and `Guest` are not accepted or produced. A SystemAdmin
without active Workspace membership receives `currentUserRole = null` and
`accessSource = SystemAdmin`; a real membership wins when both access paths
exist.

Archived Workspaces remain separate from the normal active list.
`GET /api/workspaces/capabilities` remains the backend authority for page-level
`canCreate`. WS-01 does not infer create authority from a role label.

`updatedAt` is `Workspace.UpdatedAt ?? Workspace.CreatedAt`; it does not claim
cross-resource activity semantics. A successfully evaluated empty aggregate is
numeric zero. A failed projection fails the list closed rather than fabricating
zero or a partial card.

## Backend authorization and query architecture

The implementation preserves the existing authorization relations:

- current-Tenant query filters and active Workspace authorization;
- the distinct SystemAdmin access source;
- shared SQL-translatable `VisibleAnnouncementsFor` for list, detail, Search,
  dashboard, and target-resolution parity;
- canonical `QueryReadableConversationIds` for Conversation visibility;
- canonical `VisibleProjectsFor` followed by `Active + Review` status filtering;
- provider capability checks that fail closed outside PostgreSQL;
- aggregate-only queries that do not select Announcement or Conversation title,
  body, participant, or author data.

For a non-empty list, `WorkspaceDashboardQuery` executes four sequential EF
queries on the scoped `AppDbContext`:

1. active authorized Workspace cards and exact role/access source;
2. grouped authorized unread Announcement counts;
3. grouped authorized unread Conversation counts;
4. grouped authorized visible Active/Review Project counts.

The command-interceptor regression observes four reader commands for both five
cards and one card. No parallel EF operation or per-card query is introduced.

The PostgreSQL graph proves Tenant isolation, all five roles, SystemAdmin with
and without membership, revoked membership, Announcement read/audience scope,
Conversation authorization/read state, Project visibility/status, bounded query
count, and no restricted title/body selection.

## PostgreSQL regression A: already-read Conversation

### Root cause

The original fixture used a fixed `Now` of `2026-08-22T03:00:00Z` and fabricated
`LastReadAt`/`LastReadSequence` as `Now + 1 day`. `AppDbContext` unconditionally
stamps the `CreatedAt` of an added Message with the real UTC wall clock during
`SaveChanges`. Once wall time passed `2026-08-23T03:00:00Z`, the persisted
"Read Workspace Conversation" Message was later than the fabricated cursor.
Both the test predicate and the production dashboard therefore correctly
classified the Message as unread. The integration with `main`, Tenant filters,
and the production unread predicate were not causal.

The fixture also omitted the canonical cursor Message IDs and the matching
`ConversationMember` cursor written by Messaging's `MarkReadAsync`.

### Correction

The fixture now:

- persists and reloads the read Message before deriving its cursor;
- stores canonical Conversation scope and both `LastReadItemId` and
  `LastReadMessageId`;
- derives `LastReadSequence` from the persisted Message `CreatedAt.UtcTicks`;
- sets a read time after the persisted Message;
- updates and asserts the matching `ConversationMember` cursor;
- proves an authorized own-message-only Conversation remains excluded.

The expected unread titles remain exactly "Private visible title" and "Unread
Workspace Conversation"; the expected card count remains `2`.

Historical reproduction at exact old head `abe99cd...` on PostgreSQL 18.6:
1 failed, 0 passed, 0 skipped. The repaired exact test passes 1/1.

## PostgreSQL regression B: Announcement target translation

The old query projected `AnnouncementTargetUser`, including the correlated
`AnnouncementReads.Any`, then applied DTO-level `Distinct` and ordering. EF Core
10/Npgsql could not translate that shape. One intermediate merge also attempted
`ThenBy(user => user.Id)` after projection, which was a compile error because the
DTO exposes `UserId`.

The final server-side shape:

1. derives authorized target `UserId` values under active Tenant and parent
   Workspace/Group/Channel checks;
2. intersects them with active Tenant users;
3. applies `Distinct` to scalar IDs;
4. queries active, non-deleted Users;
5. orders Users by `DisplayName`, then `Id`;
6. projects the target DTO and authorized `AnnouncementReads.Any` state.

No `AsEnumerable`, unbounded client filtering, stale child-membership authority,
or authorization bypass is used. Retained Group/Channel membership cannot
survive parent Workspace revocation. List, detail, Search, dashboard, and target
resolution remain aligned through the shared visibility relation.

Historical reproduction at exact old head `abe99cd...` on PostgreSQL 18.6:
1 failed, 0 passed, 0 skipped with the translation exception. The repaired
parent-scope test passes 1/1.

## Angular Workspace wiring

The active Angular feature now has an Coglatas-owned DTO/mapper boundary. It does
not bind presentation components directly to backend DTOs.

- Owner/Admin -> `管理者`;
- Adviser -> `先生`;
- Member -> `メンバー`;
- ReadOnly -> `閲覧のみ`;
- SystemAdmin with no membership -> `システム管理者アクセス`;
- missing/unknown role data -> explicit unavailable presentation, never Member.

Authoritative zero/non-zero Announcement, Conversation, and Active/Review
Project counts map to the existing card values. Unavailable remains visually and
textually distinct from zero. Card actions are derived only from the three
backend booleans. Page create visibility is derived only from the enveloped
capability endpoint and fails closed if that request fails. The obsolete
"API未実装" partial-summary banner is removed from a successful complete
projection.

The existing first-card active Workspace selection is intentionally unchanged;
it remains a WS-02 gap.

## Verification on the integrated source tree

PostgreSQL server: PostgreSQL 18.6 (Debian 18.6-1.pgdg13+2).

```powershell
dotnet restore Coglatas.slnx
```

Passed; all five projects were restored/up to date.

```powershell
dotnet build Coglatas.slnx --configuration Release --no-restore --disable-build-servers -m:1
```

Passed: 0 errors, 6 known warnings in unrelated WPC tests.

```powershell
$env:POSTGRES_TEST_CONNECTION_STRING = "<PostgreSQL 18.6 test connection>"
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj `
  --configuration Release `
  --no-build `
  --filter "FullyQualifiedName~Ws01WorkspaceDashboardProjectionPostgreSqlTests|FullyQualifiedName~Ws01AnnouncementParentScopePostgreSqlTests"
```

Passed: 3; failed: 0; skipped: 0.

```powershell
$env:POSTGRES_TEST_CONNECTION_STRING = "<PostgreSQL 18.6 test connection>"
dotnet test Coglatas.slnx `
  --configuration Release `
  --no-build `
  --disable-build-servers `
  -m:1 `
  --verbosity normal
```

Passed: 1097; failed: 0; skipped: 0.

```powershell
dotnet ef migrations has-pending-model-changes `
  --project src/Coglatas.Infrastructure `
  --startup-project src/Coglatas.Web `
  --configuration Release `
  --no-build
```

Passed: no pending model changes.

Scoped `dotnet format ... whitespace --verify-no-changes` and
`dotnet format ... analyzers --verify-no-changes` both exited 0 for all changed
C# files. `git diff --check` also passes.

Frontend results at the tested implementation commit:

- `npm ci`: passed; `frontend/package-lock.json` remained byte-identical.
- focused Workspace unit tests: 42 passed, 0 failed, 0 skipped (4 files).
- production Angular build: passed with existing bundle/style budget warnings.
- architecture check: passed; architecture node tests 4/4; license tests 4/4.
- Storybook with `NODE_OPTIONS=--max-old-space-size=4096`: passed.
- real-backend runner helper tests: 6/6.
- full Angular unit suite: 438 passed, 2 failed, 0 skipped. One Files timeout
  passed 6/6 when rerun alone. The deterministic Announcement failure also
  reproduces on clean current `main` (412 passed, 1 failed), so it is not caused
  by WS-01.
- host Playwright: 61 passed, 2 stale snapshot failures, 3 intentionally skipped.
- pinned Linux diagnostic: 62 passed, 1 stale desktop snapshot failure,
  3 intentionally skipped.

The two deterministic Workspace screenshot differences from the integrated
branch were inspected before approval. The desktop change replaces the stale
fabricated partial-summary state with the backend-authoritative Workspace
counts, role, and capability-derived actions while retaining the current
integrated shell. The mobile drawer itself is unchanged; only the obscured
Workspace content and selected navigation state reflect that same intended
projection. The pinned Linux update command regenerated exactly these files:

- `desktop-shell-workspaces.png`;
- `mobile-shell-workspaces-drawer.png`.

An immediate pinned Linux run completed with 63 passed and 3 intentionally
skipped tests. The generated files are byte-identical to the stable actual
images captured on every retry of CI run `32645432984`.

## Scope confirmation

- No migration, entity/schema, configuration, or model-snapshot change.
- No package, dependency, lockfile, hosted `wwwroot`, AppShell, or global
  navigation change.
- No Task, Kanban, Gantt, Project creation/activation, Messaging production,
  realtime, file, WS-02, WS-03, WS-04, or WS-05 implementation.
- Pre-existing local changes to root package files and `frontend/angular.json`
  were not staged.

## Remaining merge gates

1. The exact final PR head must complete CI, PostgreSQL 18 acceptance, security,
   frontend, Storybook/Playwright, and Buildkite checks with no unresolved review
   thread.
2. WS-02 still owns route/last-used active Workspace selection; `cards[0]`
   remains intentionally unchanged.

Until the current-head checks complete, PR #403 remains Draft and is not
merge-ready.
