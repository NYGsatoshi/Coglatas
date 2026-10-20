# Student Council pilot scope

This is the invitation-only Student Council pilot scope for the target dates
2026-10-22 to 2026-10-23. A date does not waive security or release conditions.
The public release verdict is **NO_GO** until the separate
[release gate](student-council-release-gate.md) is satisfied for the exact target.
Implementation, synthetic qualification, protected merge and public opening
are separate decisions.

## Source and authority

The mission started from Product Main
`6f940987f8df746736b6df5ea7371ab698028f5e` and private Spec Main
`6a0491c925b4980ce737dfbdf775816c237e44fe`. At the 2026-10-19T22:00:08Z
normal merge, Product Main became `7bba2d00481fb8093ef87790f5b4fb2553856ba6`
and the 22:02:16Z read confirmed unchanged Spec Main. These are checkpoint
identities, not a claim
about a later resulting Main or deployment. The current verification receipt
must record its actual candidate, tested merge and Main separately.

The 2026-10-20T02:16:16Z read confirms Product Main
`1e2177e2197ae2507b0b009545a86b94b3ab1b1c`, after normal merge #1289,
and unchanged Spec Main. The former checkpoint remains historical.
The Owner has separately approved Group DM G1–G3 v2 implementation and
synthetic verification, and Workspace management Option 2 with qualification
of existing command authority. These approvals do not open either feature
to school users. Implementation candidates and their qualification remain
separate from this Main.

Current source, tests, CI and root deployment configuration take precedence
over active status documentation, then roadmap intentions and historical
archives. The external private specification defines requirements; an enum,
entity, route or mock is not proof of implementation or approval. Private Spec
content and held drafts must not be published.

`CoglatasUI` file `52a5041f-08a1-80f1-8008-c5a09f8b5f61`, revision 441,
is the read-only UI reference. Current Approved Dark boards take precedence
over Legacy comparison boards and the older revision 339 local references.
See [the source-to-component mapping](student-council-penpot-traceability.md).

## Feature contract

The table distinguishes recorded Main behavior from approved implementation
candidates. Combined source `0695c2404e7c04c395371d5df340f94db081c5a2`
assembles Workspace and Group work for synthetic evaluation; it has no combined
runtime qualification at this checkpoint. Later Group UI follow-up `09e58d4a`
is under QA. Neither source changes the qualified Main or public verdict.

| Area | Implemented or intended pilot surface | Boundary that must remain explicit |
| --- | --- | --- |
| Workspace | Authorized list, explicit selection, canonical create, members/search, minimal member detail, role/status display, scope switching and revocation clearing; Option 2 management implemented in separate candidates | Ordinary detail GET grants no management capability. Approved Option 2 adds bounded server-owned management reads and presentation of existing commands. Existing-user addition sends no email. Existing Owner-changing/self-removal authority is preserved; ordinary member self-leave remains deferred. Backend PR #1290 and draft UI PR #1291 require successor CI and combined integration qualification. |
| Channel Chat | Existing Conversation/Message timeline, composer, edit/delete, mentions, unread, participant projection, Thread, reconnect and failed-send recovery | Use the existing authorized WorkspaceChannel contract; Group/Channel Posts are a separate model. Do not duplicate persistence or infer authority from the UI. |
| Thread | Existing Message Thread API, one active authorized body, session-local tabs/drafts and loaded-root navigation | Inactive tabs do not cache independent protected bodies. No server-saved tabs, cross-device synchronization, global directory, additional history, topic or status contract is claimed. |
| DM | Existing direct-conversation endpoint, recipient search, message lifecycle, Thread, unread, reconnect and error recovery | Require current authorized participant/Tenant/account/Workspace state according to the existing approved contract. Recheck after revocation; clear confidential state on logout/scope changes. |
| Group DM | G1–G3 backend creation, membership and retained-history implementation in PR #1292; separate frontend candidate supplies creation, routing, member controls and current-state clearing | Group type 7 remains unsupported on recorded Main. Candidates use existing routes and exact current account/Tenant/Workspace admission, existing Admin OR CanManageMembers management authority and full retained prejoin/rejoin/absence history with existing historical participant/email visibility. Sole Admin leave/self-removal has no last-admin guard. Group threads, moderation and metadata changes remain deferred. UI QA and actual two-user Angular/wire integration are incomplete; no school-user activation is authorized. |
| Files | Existing Workspace inventory, search/folders, details, preview/download, denied states and safe revocation clearing | UI upload intake is disabled; qualified target server-side intake denial remains required. Scanner models do not establish a scanning pipeline. Intake denial alone does not establish that every mutation is disabled. Shared/Filters/Artifacts review APIs remain inactive. |

The separately approved Direct read-lifecycle repair is merged in #1283.
It requires current Workspace membership for Project-unbound Direct reads and
derived delivery while preserving existing Project-bound and authorized
Active/Archived rules. It changes no schema, role, GRANT or RLS. Do not ask
for that approval again or treat it as Group activation approval.

## Candidate evidence checkpoint

These source-specific results do not accumulate into a combined or deployed
pass. The [release gate](student-council-release-gate.md) retains the failures,
execution classifications and outstanding target conditions.

| Candidate | Evidence available | Qualification still required |
| --- | --- | --- |
| Workspace backend PR #1290, `4cccb0cf` | Local 143 passed: 18 PostgreSQL, 124 InMemory and one metadata check. The original hosted run's 126 failures remain recorded. | Fresh successor CI, real resource authorization and combined Angular/backend execution; 383 anonymous HTTP 401 checks establish only anonymous rejection. |
| Workspace UI draft PR #1291, `ab6fa6ea` | Unicode search repair plus shared dialog keyboard-scroll access; 79 units and 32 selected browser cases passed locally. Two overlapping Announcement cases also passed with the unchanged default 30-second timeout. | Fresh hosted CI and combined integration. The retained 60-file inventory was made after execution; no pre-run source manifest or compiled-build archive was captured. |
| Group backend PR #1292, `1cfba188` | 17 owning plus 64 adjacent tests passed: 79 PostgreSQL and two fallback checks, zero skips. Independent review covered all fifteen changed paths without an actionable finding. | Protected merge and resulting-Main qualification; provider/HTTP/transport evidence does not establish actual Angular integration or target acceptance. |
| Group UI `c8d889f5`, followed by `09e58d4a` under QA | Fresh 305 units, production build, 59 architecture and four license checks passed at c8. Its 22 browser executions had 21 passes and one End-key timing failure. Independent source review covered 21 paths; thirty historical c5 captures were opened. | Pending short-dialog and recipient-options accessibility follow-up, final source-specific browser QA and partial visual-parity review. No final UI pass or new Group UI PR is claimed. |
| Real-backend browser extensions | Group two-user Angular/wire journey source is under review; Workspace existing-P0 extension `08325872` is prepared. | Neither extension has executed at this checkpoint. Source and mocks earn no integration pass. |

## Architecture and versions

Keep the Angular frontend in `frontend/`, the existing NgRx/Signal facade
owners, ASP.NET Core modular monolith and project-reference direction.
Inspected source uses Angular 22.2.3, CLI 22.2.2, NgRx 22, SignalR 10.0.11,
.NET SDK 10.0.402, EF Core 10 and PostgreSQL 18.6 in observed test runs.
Syncfusion's existing mixed 34.2.x/35.1.x packages and protected license workflow
remain. Playwright uses the repository's pinned Linux image. These are source
and test checkpoints; the target host must report its actual versions.

Do not add a parallel state owner, second message persistence model, new
dependency, migration, role, RLS, key-custody policy or external service under
this scope. Keep controllers thin and enforce Tenant/resource authorization
server-side. A hidden route or button is not a security boundary.

## Workstream ownership

Coordinator plus three workers run concurrently; completed workers release
slots for the remaining workstreams. Each uses an independent branch/worktree.
Integration preserves worker commits, source identities, failures and reviews.

| Workstream | Responsibility |
| --- | --- |
| A | Approved Dark tokens, AppShell, Home, Workspace, global navigation and responsive layout |
| B | Channel/Thread, composer, loaded-root navigation, unread/mentions and reconnect |
| C | DM/Group identity, recipient picker, routing and supported lifecycle |
| D | Files/preview/download, upload review and visual QA |
| E | Backend authorization, HTTP negatives and real PostgreSQL/provider evidence |
| F | Sakura qualification, real-backend/public HTTPS gates, recovery and operations |
| Coordinator | Integration, independent review, CI/native protection, source evidence and GO/NO-GO |

Initial workstream PRs #1273 through #1278 were superseded by #1279 without
deleting their branches/worktrees. Follow-ups #1280 through #1286 and CI repair #1288 merged by
normal protected merges. Current PRs, candidate identities and results belong
in the release receipt; a historical PR pass does not qualify later source.

## Change and evidence policy

The active Main ruleset 24643016 prohibits deletion/non-fast-forward updates,
requires a PR and resolved review threads, and requires strict current-base
`build-test`, `frontend-test`, `security-scan`, `publication-readiness`,
`functional-fast` and `performance-fast` checks from the pinned provider.
CODEOWNERS remains unchanged. The configured human approval count is zero;
this pilot additionally requires an independent source/evidence review.
Never force-push, use an admin bypass, weaken a check, suppress a quality
failure or turn a skipped test into a pass.

Bind results to exact source, commands, image/provider, UTC and raw artifacts.
PostgreSQL configuration, EF InMemory, a mocked browser, a Compose rendering
and a configured workflow each have distinct limitations. Record actual
execution, skips, errors and cancellation; preserve prior failed attempts.
Use the pinned Linux screenshot runner at 1536/1366/768/390/320 CSS pixels,
with explicit extra keyboard-height/960px controls where needed. Test keyboard,
focus, IME composition, late responses and authorization invalidation.

Project/Task, Gantt, ProjectIDE, Avalonia, durable Draft/Links/Starred,
advanced search, External Execution, production Kafka, Kubernetes, general SaaS
and new SSO/MFA are outside acceptance. Reachable APIs still need authorization
testing. Product #1269/#1270/#1271 and private Spec #287 through #299 retain
their independent holds. This scope does not authorize real school data,
public opening, destructive operations or a change to those approvals.

Sakura uses the existing `deploy/sakura` entry points and Caddy/tunnel choices.
Keep secrets outside Git, preserve existing volumes/keys/DB, and verify the
actual host before mutation. A Quick Tunnel is not a stable pilot endpoint.
The Student Council HTTPS gate is separate from broader #481; neither a local
pass nor this narrower scope changes or satisfies #481.
