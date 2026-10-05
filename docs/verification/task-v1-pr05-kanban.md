# TASK-V1-PR05 verification record

## Candidate identity

- Implementation base:
  `b6c848025f64918ad622fde7433ba91f1c36789f`
- Specification:
  `20aa5a2e015ae8fb68e5ba2b257a416dfcad5c3f`
- Branch: `task/v1-pr05-kanban-adapter`
- Scope: TASK-V1-PR05 only

The exact final candidate SHA and final-head command results are recorded in
the draft pull request because committing this document assigns that SHA.

## Verification boundaries

The local acceptance set covers:

- A non-mock hosted ASP.NET Core and PostgreSQL journey through the real
  controller routes, cookie authentication, Tenant resolution, Project
  authorization, CSRF middleware, EF persistence, optimistic concurrency,
  audit, transactional Outbox, and HTTP reload.
- Application authorization, privacy, Task/board concurrency, transition
  guards, WIP warnings, stable reorder/rebalance, audit, and atomic rollback.
- PostgreSQL 18 empty migration, upgrade from the PR04 migration, additive
  down migration, pending-model check, expected Kanban indexes, bounded
  projection, stable reload, fixed six-statement query shape, and atomic
  Task/board and Stage/board concurrency rollback.
- Angular DTO mapping, Project Detail state, optimistic move, denial rollback,
  conflict refetch, queued realtime reconciliation, reconnect catch-up,
  authorization clearing, authorization-epoch response discard, configuration,
  feature fallback, and stale My Tasks preference normalization.
- Adapter keyboard movement, Escape/cancel, logical focus restoration,
  permission-hidden actions, swimlanes, WIP text, and narrow presentation.
- Desktop and mobile Chromium flows for pointer move, keyboard move, conflict,
  denial rollback, WIP warning, Task Detail activation, narrow layout,
  permission denial, and feature fallback.
- Release build, Angular production build, architecture import checks, license
  policy tests, initial-bundle inspection, and complete forbidden-path audit.

Mocked Angular Playwright proves browser behavior against the public wire
shape; it does not replace controller/ASP.NET Core integration evidence.
PostgreSQL tests use real EF migrations and Npgsql but do not represent
production data volume.

The pinned Linux screenshot command and real-backend browser stack remain
environment-dependent. Exact final-head results and any environmental blockers
are recorded in the draft pull request without converting them into passing
evidence.

## Migration evidence

Migration: `20260729140506_AddProjectKanbanDefaultSwimlane`

- Empty PostgreSQL apply: covered.
- Upgrade from `20260729010000_AddMyTasksEffectiveWatchIndex`: covered with a
  pre-existing Workflow Definition row.
- Existing-data result: `KanbanDefaultSwimlane = None`.
- Additive down migration: covered; the pre-existing board definition and
  version remain.
- Model snapshot/pending changes: covered.

The migration does not rewrite Task Stage or order data. Disabling
`tasks.kanbanV1` is the presentation rollback; it preserves canonical HTTP
state and returns Project Detail to the maintained Task List.

## Security and architecture assertions

- Feature flags do not participate in backend authorization.
- Tenant query filters and Project authorization precede board projection.
- Unknown, revoked, and unauthorized Project access use the same safe
  not-found result.
- Cross-Project and unknown neighbor IDs use the same position error.
- Counts, warnings, parent context, and filter results contain only authorized
  Project data.
- Unsafe browser commands use the existing CSRF interceptor.
- Transactional Outbox rows and audit records share the mutation commit.
- HTTP command/snapshot results are authoritative; realtime carries
  invalidation only.
- Project feature code uses Coglatas-owned models and adapter contracts.
- No package, lockfile, Angular config, route, AppShell, global style, CI,
  Workspace, Messaging, Gantt command, or legacy `wwwroot` file is changed.

## Acceptance remediation

The PR05 acceptance remediation adds three focused corrections:

- A pointer drop into a reason-required Stage opens the shared move form and
  does not emit a command or begin optimistic state until a reason is
  submitted. Escape and Cancel restore focus without issuing a command.
- Keyboard `End of stage` and pointer column-end drops both emit the canonical
  empty neighbor pair. The backend resolves that pair against the complete
  persisted Stage, so a bounded or truncated snapshot cannot change its
  meaning.
- `TaskV1Pr05KanbanHostedHttpTests` exercises snapshot, configuration, and
  movement through HTTP and a temporary migrated PostgreSQL database. It
  covers safe non-disclosure, membership revocation, CSRF, Manager/member
  authorization, Task and board version conflicts, canonical ordering,
  cancellation-reason persistence, audit and Outbox rows, reload, and forced
  Outbox-save failures that prove business data and side effects roll back
  together.

Mocked Playwright remains frontend interaction and state-transition evidence.
The hosted ASP.NET Core and PostgreSQL tests are the non-mock route, security,
persistence, concurrency, audit, and Outbox evidence. Real-backend Playwright
is reported separately and is not counted as passing when its environment or
Syncfusion license is unavailable.

## Final real-backend browser acceptance remediation

### Candidate identity

- Pull request: `#258`
- Branch: `task/v1-pr05-kanban-adapter`
- Original implementation base:
  `b6c848025f64918ad622fde7433ba91f1c36789f`
- Audit start head:
  `694dd3cb5106e7ff5a84204090df4f417c792969`
- Main incorporated by merge:
  `70d1f71a966529c9b751502a104bc115424aa80e`
- Final code-bearing implementation head:
  `24a19d1c7502cab76378e519e836b5a2eb18f3c7`
- Code-bearing candidate relation to main: 17 ahead, 0 behind.

The documentation-bearing final pull-request head and its repeated hosted
results are recorded in the pull-request body. This document cannot contain
the SHA assigned by the commit that contains the document itself.

### Acceptance gap and remediation

The retained Angular Playwright scenario intercepts `**/api/**` and remains
valid interaction/state evidence. The hosted HTTP suite remains valid
controller, authentication, authorization, CSRF, PostgreSQL, concurrency,
audit, Outbox, rollback, and reload evidence. Neither one previously proved
the integrated browser-to-database path.

The final remediation adds:

- deterministic `@example.test` Manager/member actors, Tenant, Workspace,
  Project, Workflow Definition, Todo/Done/Cancelled Stages, and five Task
  cards to the existing synthetic browser-smoke seed;
- one PR05 scenario in the existing real-backend Playwright suite, without
  `page.route`, API fulfilment, fake controller/repository, InMemory, SQLite,
  or an authorization bypass;
- narrowly gated browser-smoke helpers for real concurrent mutation,
  membership revocation, and a held real HTTP response, all disabled unless
  the existing real-backend smoke environment opt-in is present;
- logical focus restoration after an authoritative conflict refetch; and
- active parent-Workspace enforcement in Project list projection so a
  `ProjectMember` or `GroupMember` row cannot continue granting visibility
  after Workspace revocation.

No additional Kanban UI route, package, lockfile, Angular configuration,
AppShell, global style, legacy `wwwroot`, Qodana profile, or shared
heavy-concurrency contract was changed.

### Real browser evidence

Hosted run `30528978089`, job `90826523817`, executed the exact code-bearing
head `24a19d1c7502cab76378e519e836b5a2eb18f3c7` and completed successfully.

- Artifact ID: `8753968367`
- Artifact name: `real-backend-browser-smoke-artifacts`
- Artifact digest:
  `sha256:7d448a9c290f92e611091c0375879927ee6b14ead91e6eb97ec4baa1b786a7e2`
- JUnit result: 5 passed, 0 failed, 0 skipped, 0 errors.
- PR05 browser scenario: 1 passed, 0 failed, 0 skipped.
- Existing PR03C and PR04 scenarios: passed.
- Kanban API interception: `none`.
- Secret-pattern scan of the extracted evidence: 0 matches.

The initial authoritative response was:

- URL:
  `http://app:8080/api/projects/e1ab9790-d732-42d6-820a-98f240931950/kanban`
- Status: `200`
- Synthetic Tenant:
  `11111111-1111-1111-1111-111111111111`
- Synthetic Workspace:
  `48afdf64-cd87-4af4-8752-33260338f8d1`
- Synthetic Project:
  `e1ab9790-d732-42d6-820a-98f240931950`
- Initial board version: `1`
- Authorized card count: `5`
- Snapshot truncated: `false`
- Backend-computed `canConfigure`: `true`

The browser sent and verified these real command intents:

| Flow | Task | Expected Task / board | Neighbor intent | HTTP | Authoritative board | CSRF |
|---|---|---|---|---:|---:|---|
| Stable reorder | `9a283902-cc7b-4649-9c4a-a18eb4cb8858` | `1 / 1` | before `9deed61a-690c-4035-9d50-e56efbdf19e5` | 200 | 2 | present |
| Move to Done | `9deed61a-690c-4035-9d50-e56efbdf19e5` | `1 / 2` | canonical end `null / null` | 200 | 3 | present |
| Reason-required Cancelled | `1b682a07-f171-495d-a356-f03b7444d1bb` | `1 / 3` | canonical end `null / null` | 200 | 4 | present |
| Concurrent reorder | `a89dafc3-3203-4b02-8774-130229133a0a` | `1 / 4` | canonical end `null / null` | 200 | 5 | present |
| Stale board conflict | `2768f172-3443-4414-bdfa-fd002cb98e9c` | `1 / 4` | canonical end `null / null` | 409 | refetched 5 | present |

Reload verified persisted order after the stable reorder, persisted Done Stage,
Task version `2`, board version `3`, and persisted order `1000` after the Stage
move. Escape and Cancel issued no command in the reason-required flow, focus
returned to the Task card, the real POST contained the submitted synthetic
reason, and the Cancelled state survived reload. Hosted HTTP/audit coverage
independently verifies persisted transition reasons.

The real stale request returned `KANBAN_STALE_BOARD`; optimistic Stage state
went Todo to Done and back to Todo, an authoritative GET returned board version
`5`, and focus returned to the conflicted Task.

Membership revocation used a real CSRF-protected DELETE and returned `200`.
Protected board data cleared before revalidation; the held authorized `200`
could not restore it; the denial and subsequent GET were both safe `404`
responses. Page errors were zero. The only captured non-2xx responses were the
expected conflict and safe-denial probes.

Feature fallback remains covered by the retained mocked browser scenario:
Project Detail stays on its existing route and Task List, no Kanban GET is
sent, authorization is unchanged, and My Tasks remains List-only. Backend
authorization is not feature-flag controlled.

### Final code-bearing verification matrix

| Gate | Run or local result | Head | Result |
|---|---|---|---|
| Documentation CI | `30527137657` / job `90820591964` | `24a19d1c` | success |
| CI | `30527140401` | `24a19d1c` | success |
| CI `build-test` | job `90820599830` | `24a19d1c` | success |
| CI `security-scan` | job `90820599926` | `24a19d1c` | success |
| CI `frontend-test` | job `90822053525` | `24a19d1c` | success |
| Code Quality | `30527137510` | `24a19d1c` | success |
| Qodana Community / .NET | job `90820591554` | `24a19d1c` | success |
| Angular / TypeScript / JavaScript / HTML / SCSS / CSS | job `90824320180` | `24a19d1c` | success |
| npm Security Audit | `30527137721` / job `90820591611` | `24a19d1c` | success |
| Real Backend Browser Smoke | `30528978089` / job `90826523817` | `24a19d1c` | 5 / 0 / 0 |
| Release build | local, merge-updated candidate | `24a19d1c` | 0 warnings / 0 errors |
| Full backend tests | local PostgreSQL 18 | `24a19d1c` | 442 / 0 / 0 |
| PR05 focused backend | local PostgreSQL 18 | `24a19d1c` | 32 / 0 / 0 |
| Hosted HTTP PR05 | local PostgreSQL 18 | `24a19d1c` | 3 / 0 / 0 |
| Angular unit | local | `24a19d1c` | 270 / 0 / 0 |
| Mocked Playwright | local | `24a19d1c` | 59 / 0 / 3 intentional |
| Real-backend runner helpers | local | `24a19d1c` | 6 / 0 / 0 |
| Migration model check | local PostgreSQL 18 | `24a19d1c` | no pending changes |

Local `npm run test:ui:real-backend` was not counted as passing: the licensed
frontend image stopped before application startup because
`SYNCFUSION_LICENSE` is not configured locally. No secret was generated,
printed, stored, or committed. The hosted run above used the existing secret
and is the authoritative real-backend browser evidence.

### Qodana triage

Code Quality run `30527137510` reported no Critical or Error severity result.
The complete inventory contains 2,224 existing and PR-intersecting inspection
items (1,399 High and 825 Moderate); it was not mass-suppressed or mass-fixed.
Blame and base-ancestry triage identified 73 PR-introduced inspection items
(62 High and 11 Moderate). They are serializer-visible positional DTO/read
model properties, null-defense/qualifier suggestions, equivalent LINQ or
private-pattern proposals, EF expression advisory, and test/test-support
style suggestions.

There are no PR-introduced security or concurrency rules, no
use-after-dispose/`AccessToDisposedClosure` result in PR files, and no material
PR05 logic warning. The redundant identical conflict-code ternary was replaced
by the existing safe `KANBAN_CONFLICT` constant and focused tests cover both
save-failure variants; no new error code was introduced.

### Historical candidate evidence

Historical run `30521971077` at
`518c290fb4148c9fe8ecf8c3e0ff60d06e0bd4c2` is retained as failed candidate
evidence: 3 passed, 2 failed, 0 skipped. Its artifact was `8751593148`,
digest
`sha256:42f3f1f9ee92e43529861453309c48823b55df192be68ffe4b8ff268c4142c67`.
It exposed two real integration defects: focus was not re-applied after an
authoritative conflict refetch, and Project list projection retained Project
rows after parent Workspace revocation. Both defects are covered by the final
changes and the successful final code-bearing run.

At the same `518c290f` candidate, Code Quality run `30521949901` attempt 1
was not passing evidence: Qodana .NET job `90804142578` succeeded, while
Angular quality job `90806553295` was cancelled by the shared-concurrency
pending-slot replacement. Attempt 2 later completed successfully with Qodana
job `90811658536` and Angular job `90811658415`. The cancellation is retained
as history and is not counted as a pass.

Candidate `10829b6bd731d9c3d17350cdbeb1de3db4ed2745` was superseded when main
advanced. Its Documentation run `30524998439` succeeded, while CI
`30524998468` and Code Quality `30524998448` were cancelled by the subsequent
merge-updated candidate. Those cancelled runs are not final evidence.

### Review and finalization state

- Unresolved review threads at document synchronization: `0`.
- Verification document synchronized: yes, for the code-bearing candidate.
- Pull-request body synchronization: performed after this document commit so
  it can name the exact documentation-bearing final SHA and repeated run IDs.
- Draft state: retained at this checkpoint.
- Remaining implementation/test blockers: none.
- Remaining acceptance gate: repeat Documentation CI, CI, Code Quality, and
  Real Backend Browser Smoke on the documentation-bearing final head, then
  re-audit branch/review state.
- Final verdict: the exact final-head verdict is recorded in the pull-request
  body. It is `Complete / Go` only if every repeated gate succeeds with the
  PR05 scenario executed and zero skips; otherwise it is
  `Incomplete / No-Go`.

No merge, auto-merge, PR06, Gantt, notification, or cutover action is
authorized by this record.
