# Angular integration qualification — 2026-10-18

Scope: existing-contract Angular Files and Dedicated Thread Workspace. Related:
[#1244](https://github.com/NYGsatoshi/Coglatas/issues/1244),
[#1239](https://github.com/NYGsatoshi/Coglatas/issues/1239),
[#1242](https://github.com/NYGsatoshi/Coglatas/issues/1242), and the separate
[#481 release gate](p0-public-https-golden-path.md).
This record is synthetic frontend qualification, not a production release
approval or proof of real-backend authorization.

## Source binding and retained first failures

- Initial frozen integration: `28484c4d32116d2622325ec0a5dd3886fb1b1f7b`.
  It includes Files `9cd36566ac67ad9d2a645cc53f30e1c5b76d99fa` and
  Thread `5fac1230f83aebc9ada1c49c99e3072f67e1a6fd`.
- The first desktop matrix executed **12 cases: 0 passed, 12 failed, 0 skipped**.
  Ten width/theme cases reached the strict scoped axe assertion and failed
  `aria-required-children`; two authoritative 403 cases failed because the
  Conversation timeline and main draft remained rendered.
- A second execution against the same frozen product source retained the same
  **12 failures** with explicit test-source binding. Test SHA-256:
  `cef706b98debb35b2cac40ff3d421fcf694628ad48a6c5dc742cf54529bc6cab`.
  JUnit SHA-256:
  `e69db1d7a0b9e1393588b57f24a4664bc6217d2dd3801c0bd6f7930a9a710fb5`.
  Console SHA-256:
  `3a184ff0146e89f7332dfb334a446bdf13b08d4de87ea85a59c2ee912293b0bc`.
  The local `work/qa-initial-28484c4d/` retains the executed test copy, report,
  console, screenshots and traces. The first console is distinct from this
  retained reproduction; neither is rewritten as a passing attempt.
- Feature repair `c98819a9` uses the existing normalized HTTP-status helper at
  all three Thread failure callbacks, puts only tabs inside the ARIA tablist,
  moves the active-tab Close control outside it, and fixes English/Japanese
  interpolation. The repair belongs to the feature branch, not this QA patch.
- Repaired integration source:
  `2977cab01f36bdce506bca2dc6c42a09f7afd79c`, merging feature branches
  `701d155e217a79971a75e03e171dc13471a4b00f` and
  `fb81cc63b09e717e373955037590573926f78f77`.
  Runtime files remain unchanged by Agent E.
- Final actor-boundary integration:
  `16bc00399c5c29231576e10a10e3f4ca2f7e5194`, after merging feature repair
  `c60b5b3a6194b305bc3fa1ccf7ebd4eb485c6b0c`. QA source commits:
  Thread `d2092cd180062a6b598fe0e47b22f2edc808134c` and
  Files `aecb2a527faae182298b8f034f5224c556f9ef1a`.


The ordinary mocked 403 body is a fixed safe error. The real Angular
`AuthSessionInterceptor` normalizes it to `ApiError.httpStatus`; the old
Thread callbacks incorrectly read only `status`. This was a client protected-
state retention defect. No backend authorization bypass was demonstrated.

The later feature-owned actor repair immediately redacts protected Messaging
views and clears cached text when the authenticated Tenant/actor changes,
including before deferred response/command handling. Its focused tests use
the actual AuthSession/Workspace/Realtime enclosing services and preserve
same-actor refresh. The independent security review of immutable `c60b5b3a`
reported no blocking issue. The original frozen snapshot remains a separate
finding record; these repaired-source checks do not relabel it clean.
Real cookie/AppShell session replacement is still NOT_EXECUTED locally.

## Executed coverage

Execution host: Windows, Node 24.19.0, repository-pinned Playwright 1.64.0
Chromium and axe. Production builds run from the integration worktree's
`frontend/`, so dependency junctions cannot select a baseline configuration.
Initial and repaired production builds passed; each initial bundle is
927.20 kB. Existing warning budgets remain visible and unchanged. Repaired
theme sanitization passed.

The first repaired-source browser selection passed **38/38, zero failures or
skips**. The repeat after template-only test cleanup also passed **38/38** on
the same product source. These are intermediate runs before the independent
actor-boundary repair; final qualification must bind that later source.
Results are not inferred from the test source.

Final source `16bc00399c5c29231576e10a10e3f4ca2f7e5194` passed **38/38 browser
cases, zero failed/skipped, 1.3 minutes**, after rebuilding the actual integration
frontend and sanitizing its theme assets. The edited UI standalone TypeScript
and single canonical Functional owner discovery also passed. Exact test-source,
report, console and 20 screenshot SHA-256 bindings are recorded in
[the machine-readable evidence](assets/web-integration-20261018.json). These are
mock browser results. They do not establish real backend, Linux, Main-CI or
release qualification.

- Dedicated Thread widths: 320, 390, 768, 1366 and 1536 CSS pixels, both
  light/dark themes. English runs at 320/768/1536 and Japanese at 390/1366.
- Open two distinct canonical roots in the same Conversation; retain separate
  in-memory drafts; require a new authorized GET before a draft reappears;
  hide protected root/draft controls while that GET is pending.
- ArrowLeft/ArrowRight, Home/End, Delete, Escape and focus restoration; reopening
  a closed tab starts with an empty draft. Closing issues no Thread, Message
  or Conversation deletion and no new Draft API call.
- Synthetic Japanese composition events prevent Enter sends and Escape close.
  This verifies browser event handlers, not a native OS keyboard/IME session.
- Strict scoped axe, document/main-content horizontal containment, theme
  attributes, localized numbered tab/close names, and absence of Thread text
  from localStorage/sessionStorage.
- Ordinary authoritative 403 at 320/1366 purges all tab identities, protected
  bodies, drafts and selection; hides the main composer and checks both browser
  storage buckets for absence of all three synthetic draft texts.
- Existing 320-pixel Message thread send/tombstone/focus/CSRF behavior and
  Channel-settings draft/focus behavior remain in the selection.
- Files: existing 320-pixel Preview/Details/Activity and axe assertions plus
  explicit loading, safe 503, current-Workspace-only Retry, root collapse/
  expansion, tree keyboard focus, and independent Move-button keyboard entry.
  The folder sidebar is hidden by the existing `max-width: 860px` rule, so its
  interaction regression uses 960/1366. Mobile folder navigation remains a
  presentation gap; no mobile sidebar interaction pass is claimed.

The existing static runner already includes both edited UI test files.
No runner selection, skip, screenshot baseline, accessibility rule, threshold,
library, backend route, DTO, fixture schema or database migration is changed.

The existing canonical real owner `FUNC-MSG-001` now contains a bounded
`MSG-THREAD` step. It creates two synthetic roots through existing APIs/UI,
checks fresh authoritative Thread GETs and separate memory-only drafts,
closes a tab without deleting its root, submits one reply and re-reads its
persisted canonical identity/body, then verifies reload discards local tabs.
Its existing journey ID, gate metadata and owner identity are unchanged.
Playwright discovery returns exactly one owner. It is automatically selected
by the existing collaboration domain lane on the final hosted candidate;
discovery is not execution credit.

| Check | Local state |
| --- | --- |
| Final actor-repaired build/theme sanitization | PASS |
| Final mock browser selection | 38 passed, 0 failed/skipped at exact source `16bc0039` |
| Edited UI test standalone TypeScript | PASS |
| Canonical Functional owner discovery | PASS; 1 owner |
| Functional harness self-tests | 39 passed, 0 failed/skipped after selecting the installed Git Bash |
| Real ASP.NET Core/PostgreSQL Thread journey | NOT_EXECUTED; Docker daemon unavailable |
| Protected staging E2E | NOT_EXECUTED; isolated target not configured |
| Public HTTPS Golden Path | **BLOCKED_ENV_NOT_CONFIGURED** |
| Native OS IME / screen reader | NOT_EXECUTED |
| Authoritative Linux screenshots | NOT_EXECUTED locally; no Windows baseline update |
| Exact-head hosted checks / SonarQube | UNVERIFIED; Coordinator owns final run/merge binding |

The first harness self-test attempt was **38 passed / 1 failed**, because
WindowsApps Bash could not execute the existing syntax check. Repeating the
unchanged 39-case suite with installed Git Bash first on the process PATH
passed all 39 with no skipped cases.

The full local Windows inspection command returned exit 1 with 56,495 ESLint
and 12,878 Stylelint findings, 129 debt-ceiling rule groups and zero parser
fatal errors. Inspection of its output shows that existing ignore patterns
did not exclude generated frontend dist and Playwright report/trace assets on
this host; vendor CSS/report assets dominate the extra Stylelint findings.
This noisy local result is retained as a failed check, not authoritative
Linux source-debt evidence or a passing quality gate. No baseline, ceiling,
rule or ignore configuration was changed. The edited canonical Functional
owner's only added targeted lint finding was prefer-template, corrected using
that existing rule. The existing inspection policy still reports inherited
and new broad all-rule warnings in UI test files; hosted quality review is
required.

An exploratory ad hoc strict compilation of the Functional owner reached
pre-existing `.mjs` declaration-resolution and nullable optional evidence-field
errors. It is not recorded as a compilation pass. The repository Playwright
loader parses and discovers the edited owner. Linux hosted checks remain
required; the local harness reporter tests are not real-backend evidence.

## Screenshot review against the supplied design

The supplied native Penpot export identifies file
`52a5041f-08a1-80f1-8008-c5a09f8b5f61`, revision 339. Its archive SHA-256 is
`847be6abd1669353a2f8f67e1a518e21ac09851b820167df0de7d47564c4580c`.
The inspected reference is the 1536 x 1024 Dedicated Thread Channel frame.
The latest live Penpot revision remains **UNVERIFIED**; no connected instance
was available. No new design parity is inferred from the export revision.

Captured screen:

1. Open the dedicated workspace with two canonical tabs and the second draft
   active. Health: functional/keyboard/containment checks pass; visual parity
   remains partial. Twenty current-run screenshots cover the width/theme/
   language combinations in both Chromium contexts. A saved contact sheet was
   inspected, along with a full 1536-pixel rendered screenshot and a combined
   dark-theme reference/rendered comparison. These are Windows diagnostics,
   not authoritative Linux screenshot baselines.

The implementation preserves the existing shell, one active Thread pane and
a bounded current-Conversation root list. The reference shows two simultaneous
Thread panes, a wider Thread hub/catalog, custom topics/statuses and different
navigation density. Those differences are visible and are not presented as
completed parity. A numerical pixel-diff score would be misleading because the
fixture content and approved capability scope differ.

The inherited Thread Preview desktop header hides Back while retaining a
three-column grid. Its Close button therefore stretches into a wide horizontal
bar. The control remains named and keyboard accessible, but its geometry needs
a later presentation repair. Mobile minimum pane height also leaves whitespace
before the loaded-root list; the page scrolls without horizontal overflow.
These gaps remain explicit rather than being silently treated as design PASS.

Screenshots, contact sheet and combined reference comparison remain local
diagnostic artifacts. This evidence PR commits no binary screenshot, vendor
asset, trace or generated report.

## Isolated environment configuration packet

**State: prepared, not provisioned.** Remote SSH and fixed public HTTPS have
not been established. No Sakura host, path, database, credentials or network
target is guessed. The existing `/srv/coglatas/app` and
`/srv/coglatas/deploy/.env` receive no test deployment, migration, seed or reset.
Do not run destructive test operations there until complete database, storage,
volume and network isolation has been independently confirmed.

Use the existing entry points `npm run test:ui:real-backend` and
`npm run test:functional`; use the existing protected workflow for public
HTTPS. Reuse `infra/compose/test/real-backend-smoke.yml` and the existing
Functional domain overlay. This packet creates no infrastructure and changes
no deployment configuration.

Before a Docker-capable executor runs the candidate, record these exact
identities in its protected execution evidence:

| Boundary | Required configuration / evidence |
| --- | --- |
| Source | exact 40-character candidate commit, clean reviewed source tree, lockfile digests, image digest and build metadata |
| Smoke Compose project | explicit `REAL_BACKEND_SMOKE_COMPOSE_PROJECT_NAME`, unique to candidate/run/attempt, e.g. `coglatas-p4-smoke-<candidate12>-<run>-<attempt>` |
| Functional Compose project | explicit `FUNCTIONAL_COMPOSE_PROJECT_NAME`, distinct from smoke/demo/production, e.g. `coglatas-p4-collaboration-<candidate12>-<run>-<attempt>` |
| Network | only the chosen project's `<project>_default`; verify Compose project labels and container network membership |
| Database volume | `<project>_real_backend_smoke_pgdata`; fresh synthetic database `coglatas_smoke`, never an existing production database |
| Other volumes | same project prefix for `real_backend_smoke_uploads`, `real_backend_smoke_data_protection`, `real_backend_smoke_node_modules`, and `real_backend_smoke_nuget`; verify all mount sources/labels |
| Host environment | app and migration services exactly `ASPNETCORE_ENVIRONMENT=Test`; synthetic smoke seed explicitly enabled only in that environment |
| Fixture | dedicated `@example.test` actors, separate synthetic Workspace/Project/Task/File/Conversation data, no school/user records |
| Build secret names | `SYNCFUSION_LICENSE`; synthetic account names `COGLATAS_BROWSER_SMOKE_EMAIL` and `COGLATAS_BROWSER_SMOKE_PASSWORD`; values remain outside records |
| Artifact reuse | exact `TARGET_SHA` and verified main-build metadata; reject artifact/source mismatch before migration or app startup |
| Source mounts | only the isolated reviewed checkout mounted as `/src` and `/work`; no Sakura production source, `.env`, DB, upload or key directory mounts |
| Reachability | isolated Compose app/Playwright namespace; no public user enablement or production routing change |

Run `docker compose ... config --quiet` without printing expanded secrets,
then inspect project labels, mounts and networks before seeding or applying
migrations. Configuration validation alone is not startup or isolation proof.
The current Docker CLI cannot connect to `dockerDesktopLinuxEngine`; therefore
no local Compose database, application or migration was started by this pass.

Migration boundary: the existing harness may apply only the reviewed
candidate's existing migrations to its fresh isolated synthetic database.
This QA patch adds no migration. Draft Persistence, new Files projections,
RLS, roles, grants, SEC-ARCH mappings and real-data migration remain on HOLD.
If any proposed candidate includes such a change without its separate exact
Owner approval, stop that part before invoking the migration service.

The optional demo tool is separate: `scripts/demo/reset.ps1` with
`infra/compose/demo/dataset.yml`, as documented in [demo dataset](../demo-dataset.md).
It requires `COGLATAS_DEMO_MODE=1`, `COGLATAS_DEMO_PASSWORD` and synthetic
`COGLATAS_DEMO_EMAIL`; it fixes project `coglatas-issue483-demo`, uses `Test`,
and binds loopback only. `Provision` reconciles reserved demo records.
`Reset` removes only that fixed project's isolated volumes. Verify those
volumes contain only the demo fixture before Reset. Do not repurpose the
fixed demo project name or its reset tool against a staging/production
database. No demo Provision/Reset was executed here.

## Protected public HTTPS configuration

**Current state: BLOCKED_ENV_NOT_CONFIGURED.** The Owner must configure the
`public-https-gate` GitHub environment with an isolated synthetic fixture and
the following secret names, without committing their values:

- `COGLATAS_PUBLIC_SMOKE_URL`
- `COGLATAS_PUBLIC_SMOKE_EMAIL`
- `COGLATAS_PUBLIC_SMOKE_PASSWORD`
- `COGLATAS_PUBLIC_SMOKE_WORKSPACE_ID`
- `COGLATAS_PUBLIC_SMOKE_PROJECT_ID`
- `COGLATAS_PUBLIC_SMOKE_TASK_ID`
- `COGLATAS_PUBLIC_SMOKE_UNAUTHORIZED_WORKSPACE_ID`
- `COGLATAS_PUBLIC_SMOKE_UNAUTHORIZED_PROJECT_ID`
- `COGLATAS_PUBLIC_SMOKE_UNAUTHORIZED_TASK_ID`
- `COGLATAS_PUBLIC_SMOKE_REVOKED_FILE_ID`

Use `.github/workflows/public-https-golden-path.yml` only after the exact
candidate is deployed to that approved isolated route. Record the deployed
image/commit binding alongside the workflow `github.sha`; the workflow source
checkout alone does not attest which application commit the remote URL runs.
The current workflow has no automatic remote deployed-commit attestation.

The existing gate enforces public HTTP-to-HTTPS redirect, HSTS, Secure/HttpOnly
cookies, cookie/CSRF authentication, synthetic positive and inaccessible-scope
negative journeys, durable Task result rereads, and safe logout denial.
A ready health probe alone cannot pass it. Its public mode intentionally
emits no authenticated traces, screenshots, videos, JUnit/HTML or attachments.
No public fixture is deleted/reset by the gate. Do not change these retention
or denial boundaries for debugging.

## Release and contract boundaries

This work can qualify the bounded Angular behavior and prepare exact-candidate
hosted integration execution. Real-backend authorization, PostgreSQL effects,
permission-revocation timing, SignalR reconnect, Linux visual parity and public
HTTPS remain separate evidence gates. A protected staging E2E pass, if later
obtained, still does not authorize real-user release.

PRE-AVALONIA SEC-ARCH remains **BLOCKED**. THREAD-04 B is the selected server
Draft completion goal, but the concrete API, persistence, optimistic concurrency,
authorization and RLS contracts require the private Owner Decision Packet and
exact approval. No server saving, browser-restart restoration or cross-device
synchronization is implemented or claimed by this QA patch.

The separate [environment decision packet](p4-isolated-environment-owner-packet-20261018.md) binds the missing target/isolation inputs and leaves all approval fields null.

## Hosted real-backend receipt before feature merge

PR #1246 head `c876479bc1eae3836f2d7c2a8ee0f071f0619b72` was tested as
GitHub's merge candidate `ef96dced4d68a6466a5ec6270a1004a9cf43494e` in
[PR CI run 39097280839](https://github.com/NYGsatoshi/Coglatas/actions/runs/39097280839),
attempt 1. The isolated Functional collaboration
[job 117587126358](https://github.com/NYGsatoshi/Coglatas/actions/runs/39097280839/job/117587126358)
executed both canonical owners successfully: **2 passed, 0 failed/skipped**.
`FUNC-MSG-001 / MSG-THREAD` executed against the real ASP.NET Core/PostgreSQL
Test application and passed. The log binds `TARGET_SHA` to the merge candidate;
this is executed backend evidence, distinct from local mocked UI results and
from a future Main merge SHA. Its scenario checks authorized Thread rereads,
independent volatile drafts, canonical root retention on tab close, reply POST
and persisted reread, and volatile tab discard on reload. It does not test a
server Draft API or cross-device persistence.

The unchanged server contract also has actual negative/postgreSQL coverage in
Main `798aadc833b84dce7d584cd584397e05ccca2b44`,
[run 39096374924 / Main Test job 117584357702](https://github.com/NYGsatoshi/Coglatas/actions/runs/39096374924/job/117584357702):
2,453 Product tests passed. Executed cases include parent-Conversation Thread
access, corrupt cross-Conversation/Tenant links, read-only/removed participants,
bodyless tombstones, scoped PostgreSQL reply/author projections and concurrent
client-request winner/rollback behavior. These are existing Message Thread
contracts, not the proposed server Draft contract. The broader run was still
in progress when this receipt was written; a successful job does not assert
whole-run or final-Main success. Coordinator qualification remains separate.


## Added real denied-read integration candidate

The existing `FUNC-MSG-001` owner also includes `MSG-THREAD-DENIAL`, first
committed as `378e30d7e186db9a3cb710d3e67280be702bfdbc` and integrated as
`8124e3bacd0bcb94cb9a2b3a48bba69c06179ebb`. It retains the authorized Thread
root, draft and selected tab, directly logs in the existing synthetic
non-member through the browser context's shared cookie jar, and requires a
fresh real Thread GET without reload/navigation. The unchanged server's
safe HTTP 400 response must contain only `error: Message thread not found.`,
without protected IDs, bodies or projections. The scenario then requires
protected UI, volatile draft, tabs and selection to disappear and restores
the original actor in `finally`. No response mocks or conditional already-
cleared pass are used. This is denied-read integration, not a complete
membership-revocation or AuthSession/AppShell replacement scenario.

Local discovery selects the one existing owner; targeted lint passes with
zero warnings/errors. Strict TypeScript retains the same two existing
TS2322 diagnostics and adds none. Actual execution is NOT_EXECUTED locally
because Docker is unavailable. Hosted execution must bind the exact tested
candidate and report this executed step; discovery and test source alone
are not execution credit. The new Functional source digest is separate from
the preserved earlier frozen-browser source digests in the evidence JSON.

At 2026-10-18 05:44 UTC, Thread merge Main
`1c07df714a19fcbe104b93c6c8af074a916e1897`,
[run 39100585232](https://github.com/NYGsatoshi/Coglatas/actions/runs/39100585232),
was still in progress. Its completed Frontend job `117596621404` passed
1,314 tests in 132 Angular spec files and 187 static UI cases; 13 existing
conditional UI skips are recorded separately. Full Functional collaboration
job `117596650478` passed, including the positive `MSG-THREAD` step against
its real Test backend. These completed jobs do not attest the still-running
whole Main run or the later QA merge. The Coordinator's exact-SHA final
receipt and PR/check history must establish those subsequent conclusions.
