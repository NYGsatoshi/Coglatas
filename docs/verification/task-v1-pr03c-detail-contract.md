# TASK-V1-PR03C final-head acceptance verification

Verification date: 2026-07-28

- Branch: `task/v1-pr03c-task-detail-ui`
- Acceptance code head: `a9b66e6c067a363d9846330f82751257a61c309c`
- Base: `dc91f3064549fc70a625c2b5b00c51731a022d65`
- Pull request: `NYGsatoshi/Coglatas#250`
- Pull-request commits at the acceptance code head: 76
- Pull-request commits after the documentation-only finalization commit: 77
- Changed files (paginated GitHub API and local three-dot diff): 101
- Latest migration: `20260728020000_CreateMissingTenantPlatformTables`

The commit that finalizes this document necessarily follows the acceptance
code head. That commit is documentation-only. The PR body records its exact
SHA and the final CI, Code Quality, and Real Backend Browser Smoke runs against
that SHA. Applicability is checked by proving that the post-evidence diff
contains this document only; a committed document cannot contain the hash of
the commit that contains it.

## Historical state

The old document referred to `66f8723e0a61fdcfdaa0ac8496269a81fc9f7ee1`,
51 commits, 84 files, CI `30267407949`, and Code Quality `30267408182`.
Those values are historical only and are not final evidence.

The 2026-07-28 final-head remediation retained the failure history:

| Run | Head | Result | Disposition |
| --- | --- | --- | --- |
| Real Backend Browser Smoke `30334976154` | `6238000013f69e04b88d0b62c7afb94972c20c93` | Failed | AG Grid action column was virtualized; historical only. |
| Real Backend Browser Smoke `30354127379` | `b97e384aea3a20f85e3c2533ca7b78ced33a6691` | Failed, 1 passed / 2 failed / 0 skipped | Exposed missing canonical seed assignment and intentional 400 responses recorded as console errors. |
| Real Backend Browser Smoke `30355307189` | `fd68f4beae7620e19b1b6237f907d88f7923154b` | Failed, 2 passed / 1 failed / 0 skipped | PR03C scenario passed; broad scenario used the obsolete AG Grid locator on the canonical My Tasks list. |
| Real Backend Browser Smoke `30356762326` | `9dac0d1dccc76d7d5b963194f75f7e4f93fbc5d2` | Cancelled as obsolete | Branch advanced before the run could be final evidence. |

No failed run, stale head, ordinary Angular Playwright run, or mocked API is
used as final Real Backend Browser Smoke evidence.

## Final scope and contract

PR03C completes the canonical
`/app/projects/{projectId}/tasks/{taskId}` detail contract: Task identity,
stage, priority, version, relationships, permissions, Subtasks, Checklist,
Comments/Mentions, Labels, the current actor's Watch state, and safe Task/File
associations and grants. It does not implement PR04, Prompt 3, or a subsequent
migration.

`ProjectsController` remains the HTTP route authority. The aggregate is
`GET /api/tasks/{taskId}`. Mutations preserve optimistic concurrency,
server-side tenant/workspace/project authorization, audit/outbox atomicity,
and safe 403/404 behavior. File DTOs exclude storage keys, file paths, grant
tokens, hashes, signed URLs, and internal paths.

## Migration audit

### Entity and chain correspondence

| Migration | Model entities | Finding |
| --- | --- | --- |
| `20260728010000_CreateMissingTenantSettingsTable` | `TenantSettings` | `tenant_settings` existed in every earlier model snapshot but no earlier `Up` created it. This is a production-schema repair, not smoke-only DDL. |
| `20260728020000_CreateMissingTenantPlatformTables` | `ApiToken`, `ExportJob`, `IntegrationAccount`, `Subscription`, `UsageRecord`, `WebhookEndpoint` | All six tables existed in earlier snapshots/configurations but were absent from the physical migration chain. This is a production-schema repair. |

The two migrations:

- follow `20260726150000_EnforceManualWatchOptOutExclusivity` in ID order;
- do not rewrite an earlier migration;
- use PostgreSQL types, maximum lengths, nullability, TenantId fields,
  primary/foreign keys, indexes, and unique indexes matching the EF model and
  `AppDbContextModelSnapshot`;
- validate the shape of pre-existing tables before adding missing constraints
  and indexes;
- use `CREATE TABLE/INDEX IF NOT EXISTS`, so an expected model-shaped
  out-of-band or partially introduced schema does not collide;
- contain no seed data;
- use a deliberately non-destructive `Down`, because every earlier snapshot
  already owned these tables and the repair cannot know whether a deployment
  created them out of band. Rollback/reapply therefore preserves data rather
  than dropping a production table of ambiguous ownership.

### Executed PostgreSQL 18.4 scenarios

Connection evidence:
`Host=127.0.0.1;Port=55432;Database=postgres;Username=postgres;Password=<none>`.
The isolated container was `pr250-acceptance-pg-20260728`; no existing
PostgreSQL container or volume was modified.

| Scenario | Test | Result |
| --- | --- | --- |
| Empty PostgreSQL database to latest | `CleanDatabaseMigratesToLatestWithTaskV1DatabaseContracts` | Passed |
| PR03C pre-repair schema to latest | `Pr03cBaseSchemaUpgradesToLatestWithAllHistoricallyMissingTables` | Passed |
| `20260728010000` already applied to latest | `TenantTableRepairSchemaUpgradesThroughTheFollowingPlatformTableRepair` | Passed |
| Model-shaped pre-existing tables, rollback, and reapply | `RepairMigrationsPreservePreExistingTablesAndDataAcrossRollbackAndReapply` | Passed; rows preserved |

`dotnet ef migrations list` showed both repair migrations applied and no
`(Pending)` entry. `dotnet ef migrations has-pending-model-changes` reported:
`No changes have been made to the model since the last migration.`

Rollback assessment: safe and non-destructive for expected clean, historical,
and model-shaped partial schemas. A corrupt or shape-incompatible out-of-band
table is rejected instead of being silently altered or overwritten.

## Final local evidence

The focused and backend commands were executed at
`fd68f4beae7620e19b1b6237f907d88f7923154b`. The only changes from that SHA to
the acceptance code head are deterministic assertions/locators in
`tests/ui/real-backend-smoke.spec.ts`; no product, migration, backend test, or
Angular source changed. `git diff --name-only fd68f4b..a9b66e6` proves this
single-file evidence applicability.

### Focused suites

Exact command:

```powershell
$env:POSTGRES_TEST_CONNECTION_STRING='Host=127.0.0.1;Port=55432;Database=postgres;Username=postgres'
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj --filter "Scope=TaskV1PR03C" --no-restore --logger "console;verbosity=minimal"
```

| Run | Start UTC | Finish UTC | Total | Passed | Failed | Skipped |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| PR03C focused run 1 | `2026-07-28T11:34:20.7764635Z` | `2026-07-28T11:35:21.8667234Z` | 29 | 29 | 0 | 0 |
| PR03C focused run 2 | `2026-07-28T11:35:21.8672667Z` | `2026-07-28T11:36:05.6040541Z` | 29 | 29 | 0 | 0 |

| Check | Start UTC | Finish UTC | Passed | Failed | Skipped |
| --- | --- | --- | ---: | ---: | ---: |
| `Scope=TaskV1Prompt2C` | `2026-07-28T11:36:17.7795391Z` | `2026-07-28T11:37:05.3817480Z` | 35 | 0 | 0 |
| `Scope=TaskV1Prompt2D` | `2026-07-28T11:37:05.3817480Z` | `2026-07-28T11:37:14.5762578Z` | 19 | 0 | 0 |
| `Category=PostgreSQLIntegration` | `2026-07-28T11:37:14.5775031Z` | `2026-07-28T11:38:18.9071417Z` | 63 | 0 | 0 |
| Full backend, `dotnet test Coglatas.slnx --no-restore` | `2026-07-28T11:38:18.9081485Z` | `2026-07-28T11:39:26.6230113Z` | 403 | 0 | 0 |

The PostgreSQL category covers clean and upgrade migrations, Task, label,
Watch, Task/File migrations, tenant isolation, HTTP contract, current File
grant authorization, concurrency, audit, and outbox behavior.

### Frontend

| Command | Result |
| --- | --- |
| `npm --prefix frontend test -- --watch=false` | 37 files, 238 passed / 0 failed |
| `npm --prefix frontend run build` | Success; existing bundle/style budget warnings only |
| `npm --prefix frontend run check:architecture` | Success |
| `npm --prefix frontend run build-storybook` | Success; existing no-MDX and asset-size warnings only |
| `npm run test:ui:angular` | 52 passed / 0 failed; 2 obsolete legacy-static desktop/mobile tests skipped |
| `npm run test:ui:real-backend:runner` | 6 passed / 0 failed / 0 skipped |

The two legacy-static skips are outside the PR03C acceptance count. They are
not skipped Task, File, authorization, migration, or real-backend scenarios.

## Real Backend Browser Smoke

Final code-head run: `30356917325`, head
`a9b66e6c067a363d9846330f82751257a61c309c`, `SUCCESS`.

- Workflow created/started: `2026-07-28T11:57:54Z`
- Real-backend job: `2026-07-28T11:58:24Z` through
  `2026-07-28T12:23:23Z`
- Playwright suite timestamp: `2026-07-28T12:13:38.440Z`
- Playwright: 3 total / 3 passed / 0 failed / 0 skipped
- Artifact: `real-backend-browser-smoke-artifacts`
- Environment: Chromium desktop, synthetic data, ASP.NET Core, PostgreSQL
  latest migrations, and built Angular
- API interception: none for Task, Project, File, auth, or download-grant APIs
- Secret exposure: none found in source, logs reviewed during remediation, or
  the final HTML/JUnit artifact

The workflow is `workflow_dispatch` only and uses:

- a self-hosted runner and clean exact-commit checkout;
- Node setup and `npm ci`;
- the repository `SYNCFUSION_LICENSE` secret, with explicit failure when
  absent;
- `npm run test:ui:real-backend`;
- real ASP.NET Core, PostgreSQL latest migrations, and built Angular;
- synthetic tenant, workspace, project, users, task, and physical synthetic
  file bytes;
- cookie authentication, CSRF, real Project/Task/File APIs, authorization
  services, and download grants;
- bounded timeouts and `always()` cleanup;
- Playwright HTML report, JUnit, trace, screenshot, and video artifacts, with
  no license file or secret included.

`tests/ui/real-backend-smoke.spec.ts` contains no `page.route`,
`route.fulfill`, or `route.abort`. The Hub-unavailable scenario uses a browser
fetch fault only for `/hubs/app`; Task and File APIs are never intercepted.

The three desktop scenarios cover:

1. login/bootstrap, cookie session, current user, tenant context, CSRF,
   SignalR reconnect, missing-CSRF rejection, primary product navigation,
   canonical Project/Task detail, canonical My Tasks list, validation failure,
   logout, and post-logout denial;
2. authenticated HTTP availability when the Hub cannot connect;
3. PR03C aggregate rendering, safe project mismatch without protected flash,
   Checklist create/refetch/delete/refetch, backend Mention candidates,
   canonical Comment/Mention create/refetch/delete/refetch, Label/Watch read,
   safe File DTO, actual file open and one-time grant download, no token in
   local/session storage, membership revocation, cleared Task/File state,
   safe Task/re-grant/open/retained-grant denial, zero My Tasks rows/counts,
   and no protected file bytes after revocation.

Expected negative HTTP responses are checked by exact method/path/status.
Chromium network console entries are correlated one-for-one by status with
those already validated expected failures; an extra console error or an
unknown failed API response still fails the run.

## Security and realtime audit

- CSP: `script-src 'self'`; no `unsafe-eval`; the existing style
  `unsafe-inline` was not broadened. `connect-src` permits only same-origin
  HTTPS plus the exact request-host `ws://` and `wss://` origins.
- UI preferences: only the fixed `coglatas.ui.theme.v1` localStorage key is
  read; only `dark`/`light` is accepted; values are assigned to fixed
  `dataset` properties, never HTML.
- Auth cleanup: the anonymous CSRF token is discarded after login; logout and
  session expiry clear tenant/protected state.
- SignalR: negotiate obtains CSRF; reconnect resubscribes through fresh
  authorization; logout/session expiry stops the connection and clears state.
- Hub authorization: connection principal/session/current tenant are checked;
  every subscription is reauthorized; tenant comes from server-resolved
  context; opaque groups cannot be joined cross-tenant.
- Files/Tasks: stale requests are generation-guarded or cancelled; membership
  revocation clears protected caches; grant tokens remain ephemeral and are
  excluded from DTOs, storage, logs, artifacts, and evidence.
- No exception, token, secret, license, storage key, internal path, or
  connection ID was newly exposed.

Blocking security/realtime findings: none.

## Additional commit audit after `90ebdbf`

Twenty-four commits were reviewed from
`90ebdbf96461f005ee5aa980d037f334fb0582e8` through the acceptance code head.

| Commit(s) | Classification | Assessment |
| --- | --- | --- |
| `f020ddf`, `537d57d` | B, with production-schema C relevance | Required migration-chain/startup repair; independently validated. |
| `2055fd7`, `28f179c`, `1f14f6`, `5c6a7af`, `4a1a89d`, `6238000`, `9dac0d1`, `a9b66e6` | B | Real-backend evidence, safety assertions, and deterministic locator/console correlation. |
| `358f97c` | B | Clean self-hosted smoke checkout support. |
| `5bf4d33` | C | Qodana runner cleanup needed for the final Code Quality gate. |
| `98bf470`, `59e1386`, `5d0eaa4`, `e7852c7`, `53579bd` | A | Cookie/CSRF/CSP/Hub tenant and reconnect authorization required by acceptance. |
| `8bbb1ef`, `0c870fa`, `2a0ffbc`, `d4534e9` | A | Protected Task/File state cleanup and request scoping required by revocation acceptance. |
| `7e2f9c1` | A/B/C | Bounded acceptance fixes across smoke, Task/File authorization, CSP bootstrap, and workflow quality. |
| `b97e384` | A/B/C and D removal | Hardened repair migrations and smoke evidence, tightened CSP, added tests, and forward-removed the unrelated analytics, lock metadata, and ConversationService-only cleanup diffs. |
| `fd68f4b` | B | Canonical synthetic primary assignment and console-clean API-context revocation probes. |

Final D classification: none. Final E classification: none.

The three D findings discovered during remediation were
`frontend/angular.json`, root `package-lock.json`, and
`ConversationService.cs`. A normal forward commit restored their base content;
they are absent from the final base-to-head diff. No history was rewritten.

## Full PR independent audit

The paginated GitHub file list and
`git diff --name-only dc91f306...HEAD` both produced 101 files. All were
reviewed. Classification totals are:

- Required: 90
- Supporting: 10
- Generated: 1
- Unrelated: 0
- Risky: 0

Audit-area totals are Angular 31, Application/authorization/DTO 15,
Domain 2, EF/repository/infrastructure 7, migrations 11, Web/controllers/
realtime/security 6, backend tests 21, browser tests 1, workflows 2, and docs
5.

### File ledger

| File | Area | Classification |
| --- | --- | --- |
| `.github/workflows/qodana_code_quality.yml` | CI/workflow | Supporting |
| `.github/workflows/real-backend-smoke.yml` | CI/workflow | Supporting |
| `docs/AI_CONTEXT.md` | docs | Supporting |
| `docs/KNOWN_ISSUES.md` | docs | Supporting |
| `docs/TASK_V1_PR02.md` | docs | Supporting |
| `docs/TESTING.md` | docs | Supporting |
| `docs/verification/task-v1-pr03c-detail-contract.md` | docs | Supporting |
| `frontend/public/coglatas-ui-preferences.js` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/core/api/api-error.adapter.spec.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/core/api/api-error.adapter.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/core/auth/auth-session.facade.spec.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/core/auth/auth-session.facade.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/core/realtime/signalr-realtime.transport.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/features/files/attachment-picker-dialog/attachment-picker-dialog.component.html` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/features/files/attachment-picker-dialog/attachment-picker-dialog.component.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/features/files/files-page/files-page.component.spec.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/features/files/files-page/files-page.component.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/features/files/files.api.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/features/files/files.facade.spec.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/features/files/files.facade.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/features/files/files.types.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/features/projects/projects-ui.spec.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/features/projects/projects.api.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/features/projects/projects.facade.spec.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/features/projects/projects.facade.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/features/projects/projects.mapper.spec.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/features/projects/projects.mapper.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/features/projects/projects.mock.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/features/projects/projects.types.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/features/projects/task-detail-page/task-detail-page.component.html` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/features/projects/task-detail-page/task-detail-page.component.scss` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/features/projects/task-detail-page/task-detail-page.component.spec.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/features/projects/task-detail-page/task-detail-page.component.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/features/projects/task-detail-page/task-detail-page.stories.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/features/projects/task-editor/task-editor.component.spec.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/features/projects/task-editor/task-editor.component.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/app/shared/mention-input/app-mention-input.component.ts` | Angular DTO/state/UI/auth | Required |
| `frontend/src/index.html` | Angular DTO/state/UI/auth | Required |
| `src/Coglatas.Application/Common/Interfaces/IAuthRepositories.cs` | application | Required |
| `src/Coglatas.Application/DependencyInjection.cs` | application | Required |
| `src/Coglatas.Application/Files/FileService.cs` | File grant | Required |
| `src/Coglatas.Application/Projects/ITaskCommandService.cs` | application/authorization/DTO | Required |
| `src/Coglatas.Application/Projects/ITaskSubresourceService.cs` | application/authorization/DTO | Required |
| `src/Coglatas.Application/Projects/ProjectAuthorizationService.cs` | application/authorization/DTO | Required |
| `src/Coglatas.Application/Projects/ProjectDtos.cs` | application/authorization/DTO | Required |
| `src/Coglatas.Application/Projects/ProjectService.cs` | application/authorization/DTO | Required |
| `src/Coglatas.Application/Projects/TaskCommandConstraintNames.cs` | application/authorization/DTO | Required |
| `src/Coglatas.Application/Projects/TaskCommandDtos.cs` | application/authorization/DTO | Required |
| `src/Coglatas.Application/Projects/TaskCommandService.cs` | application/authorization/DTO | Required |
| `src/Coglatas.Application/Projects/TaskDerivedValues.cs` | application/authorization/DTO | Required |
| `src/Coglatas.Application/Projects/TaskSubresourceDtos.cs` | application/authorization/DTO | Required |
| `src/Coglatas.Application/Projects/TaskSubresourceService.cs` | application/authorization/DTO | Required |
| `src/Coglatas.Application/Realtime/BusinessInvalidationPublisher.cs` | application | Required |
| `src/Coglatas.Domain/Entities/ProductionEntities.cs` | Domain model | Required |
| `src/Coglatas.Domain/Entities/WorkspaceEntities.cs` | Domain model | Required |
| `src/Coglatas.Infrastructure/DependencyInjection.cs` | EF/repository | Required |
| `src/Coglatas.Infrastructure/Persistence/AppDbContextSeed.cs` | EF/repository | Required |
| `src/Coglatas.Infrastructure/Persistence/AuthRepositories.cs` | EF/repository | Required |
| `src/Coglatas.Infrastructure/Persistence/Configurations/ProductionConfigurations.cs` | EF/repository | Required |
| `src/Coglatas.Infrastructure/Persistence/Configurations/SystemConfigurations.cs` | EF/repository | Required |
| `src/Coglatas.Infrastructure/Persistence/Configurations/WorkspaceConfigurations.cs` | EF/repository | Required |
| `src/Coglatas.Infrastructure/Persistence/Migrations/20260725050000_TaskV1WatchAndLabelConcurrency.cs` | migration | Required |
| `src/Coglatas.Infrastructure/Persistence/Migrations/20260725060000_AddWorkspaceTimeZone.cs` | migration | Required |
| `src/Coglatas.Infrastructure/Persistence/Migrations/20260725070000_EnforceUniqueActiveTaskFileAssociations.cs` | migration | Required |
| `src/Coglatas.Infrastructure/Persistence/Migrations/20260726010000_EnforceNormalizedTaskLabelNames.cs` | migration | Required |
| `src/Coglatas.Infrastructure/Persistence/Migrations/20260726130000_AddManualWatchIntent.cs` | migration | Required |
| `src/Coglatas.Infrastructure/Persistence/Migrations/20260726140000_NormalizeWatchStateAfterManualIntent.cs` | migration | Required |
| `src/Coglatas.Infrastructure/Persistence/Migrations/20260726150000_EnforceManualWatchOptOutExclusivity.cs` | migration | Required |
| `src/Coglatas.Infrastructure/Persistence/Migrations/20260728010000_CreateMissingTenantSettingsTable.cs` | migration | Required |
| `src/Coglatas.Infrastructure/Persistence/Migrations/20260728020000_CreateMissingTenantPlatformTables.cs` | migration | Required |
| `src/Coglatas.Infrastructure/Persistence/Migrations/AppDbContextModelSnapshot.cs` | migration | Generated |
| `src/Coglatas.Infrastructure/Persistence/Migrations/TaskV1WatchBackfillScript.cs` | migration | Required |
| `src/Coglatas.Infrastructure/Persistence/ProjectRepository.cs` | EF/repository | Required |
| `src/Coglatas.Web/Controllers/AttachmentsController.cs` | controllers/HTTP | Required |
| `src/Coglatas.Web/Controllers/ProjectsController.cs` | controllers/HTTP | Required |
| `src/Coglatas.Web/Middleware/SecurityHeadersMiddleware.cs` | Web security/composition | Required |
| `src/Coglatas.Web/Program.cs` | Web security/composition | Required |
| `src/Coglatas.Web/Realtime/AppHub.cs` | auth/SignalR | Required |
| `src/Coglatas.Web/Realtime/HubSubscriptionAuthorizer.cs` | auth/SignalR | Required |
| `tests/Coglatas.Tests/Auth/SecurityHeadersMiddlewareTests.cs` | backend tests | Required |
| `tests/Coglatas.Tests/Files/FileDownloadGrantBoundaryTests.cs` | backend tests | Required |
| `tests/Coglatas.Tests/PostgreSql/PostgreSqlIntegrationTests.cs` | backend tests | Required |
| `tests/Coglatas.Tests/PostgreSql/PostgreSqlMigrationTestDatabase.cs` | backend tests | Supporting |
| `tests/Coglatas.Tests/PostgreSql/PostgreSqlTestEnvironment.cs` | backend tests | Supporting |
| `tests/Coglatas.Tests/PostgreSql/TaskV1CoreConcurrencyPostgreSqlTests.cs` | backend tests | Required |
| `tests/Coglatas.Tests/PostgreSql/TaskV1FileAssociationMigrationPostgreSqlTests.cs` | backend tests | Required |
| `tests/Coglatas.Tests/PostgreSql/TaskV1FileOpenDownloadReauthorizationPostgreSqlTests.cs` | backend tests | Required |
| `tests/Coglatas.Tests/PostgreSql/TaskV1LabelMigrationPostgreSqlTests.cs` | backend tests | Required |
| `tests/Coglatas.Tests/PostgreSql/TaskV1LegacyCommentMigrationPostgreSqlTests.cs` | backend tests | Required |
| `tests/Coglatas.Tests/PostgreSql/TaskV1MigrationPostgreSqlTests.cs` | backend tests | Required |
| `tests/Coglatas.Tests/PostgreSql/TaskV1MigrationRawSqlSeed.cs` | backend tests | Supporting |
| `tests/Coglatas.Tests/PostgreSql/TaskV1PostgreSqlAcceptanceTests.cs` | backend tests | Required |
| `tests/Coglatas.Tests/PostgreSql/TaskV1WatchBackfillPostgreSqlTests.cs` | backend tests | Required |
| `tests/Coglatas.Tests/Projects/ProjectServiceTests.cs` | backend tests | Required |
| `tests/Coglatas.Tests/Projects/ProjectsControllerTests.cs` | backend tests | Required |
| `tests/Coglatas.Tests/Projects/TaskCommandServiceTests.cs` | backend tests | Required |
| `tests/Coglatas.Tests/Projects/TaskDerivedValuesTests.cs` | backend tests | Required |
| `tests/Coglatas.Tests/Projects/TaskWorkspaceTimeZoneResolverTests.cs` | backend tests | Required |
| `tests/Coglatas.Tests/Tenancy/HttpTenantIsolationTests.cs` | backend tests | Required |
| `tests/Coglatas.Tests/Tenancy/TenantIsolationTestData.cs` | backend tests | Required |
| `tests/ui/real-backend-smoke.spec.ts` | browser tests | Required |

### Cross-cutting assessments

- Migration: required production-chain repairs; all clean/upgrade/partial
  scenarios green; no pending migration/model delta.
- Authorization: active Workspace access is evaluated in Project, Task, File,
  Hub, and My Tasks paths. Revocation removes Task/File state and My Tasks
  rows/counts.
- DTO: canonical Task detail and Angular mappings align; File internals and
  other actors' Watch intent are not exposed.
- Audit/Outbox: PostgreSQL concurrency tests verify loser-side atomicity and
  event/version alignment; no security-sensitive mutation bypass was found.
- Critical/High: no new Critical or High issue in changed files; no unresolved
  symbol, template error, migration error, disposed captured resource, secret,
  or security finding remains.
- Non-blocking: pre-existing Angular bundle/style budget warnings, Storybook
  size/no-MDX warnings, and repository-wide Qodana warnings are not widened
  into unrelated cleanup.

## Protected user changes

The following remained outside every stage/commit:

- `qodana.yaml`: locally modified, never restored, staged, or committed;
- `.coglatas-spec-source/`: untracked external specification checkout, read-only;
- `.tools/`: untracked local tooling, untouched.

No other user change or untracked file was found.

## Acceptance code-head hosted gates

| Gate | Run | Head | Conclusion |
| --- | --- | --- | --- |
| Real Backend Browser Smoke | `30356917325` | `a9b66e6c067a363d9846330f82751257a61c309c` | SUCCESS |
| CI | `30356912074` | `a9b66e6c067a363d9846330f82751257a61c309c` | SUCCESS |
| Code Quality | `30356912053` | `a9b66e6c067a363d9846330f82751257a61c309c` | SUCCESS |

The commit containing this document is necessarily a documentation-only
successor of the acceptance code head. Its exact SHA cannot be embedded in
itself. The final PR body is therefore the canonical readback for that exact
final SHA and its final Real Backend Browser Smoke, CI, and Code Quality run
IDs. Applicability requires `a9b66e6...<final-head>` to contain only this
document. The final readback must also show the base, evidence counts/run IDs,
open/unmerged state, disabled auto-merge, mergeable state, and zero unresolved
review threads.

## Verdict

The acceptance code head has all required local and hosted evidence:

- Prompt 2-A: Complete
- Prompt 2-B: Complete
- Prompt 2-C: Complete
- Prompt 2-D: Complete
- TASK-V1-PR03C acceptance: Complete
- Prompt 2 overall: Complete

This verdict remains applicable to the documentation-only successor only when
its exact-head Real Backend Browser Smoke, CI, and Code Quality runs are all
successful and the final PR-body/review-thread readback is consistent. No
merge, auto-merge, PR04, Prompt 3, or subsequent migration work has been
performed.
