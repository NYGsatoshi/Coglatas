# Student Council pilot scope

Status: implementation and qualification in progress; **NO_GO for real users**.
Target date: 2026-10-22 to 2026-10-23. The date does not waive any gate.

## Baseline and authority

- Product Main inspected on 2026-10-19: `6f940987f8df746736b6df5ea7371ab698028f5e`.
- Private specification Main identity only: `6a0491c925b4980ce737dfbdf775816c237e44fe`.
- Product Main CI [39222064110](https://github.com/NYGsatoshi/Coglatas/actions/runs/39222064110) completed successfully. This is baseline evidence, not qualification of pilot changes or the target deployment.
- Live Penpot source: `CoglatasUI`, file/library `52a5041f-08a1-80f1-8008-c5a09f8b5f61`; read-only connection confirmed on 2026-10-19. See [design traceability](student-council-penpot-traceability.md).
- Current source, tests, CI and deployment configuration take precedence over historical implementation claims.

Main is protected by the active **Coglatas Main Protection - Owner Only** ruleset (24643016). The legacy branch-protection endpoint returns 404; this does not mean the ruleset is absent. Its current effective rules prohibit deletion and non-fast-forward updates, require a PR and resolved review threads, and require strict current-base checks: `build-test`, `frontend-test`, `security-scan`, `publication-readiness`, `functional-fast`, `performance-fast`. The ruleset requires zero GitHub approvals; this pilot additionally requires independent review. CODEOWNERS assigns the repository to `@NYGsatoshi`. No policy changes are authorized.

Open Product PRs at discovery: #1269 Shared, #1270 Filters, #1271 Artifacts, and #1249 test-fixture accessor cleanup. The three review-only feature candidates remain outside this pilot's runtime scope. Their review, provider, activation and merge holds remain intact. Private specification holds remain unchanged; no private specification content is reproduced here.

## Feature contract

| Area | Pilot behavior | Current qualification boundary |
| --- | --- | --- |
| Workspace | List, explicit selection, authorized create, member and permission display, existing-user admission, switch and revocation | Use current server capabilities and canonical create. Verify all layers; do not describe existing-user addition as delivered email invitation. |
| Channel Chat | Conversation-backed timeline, send/edit/delete, unread, mention, participants, reconnect and failure recovery | Distinguish Conversation/Message from Group/Channel Posts; do not duplicate persistence. |
| Thread | Current Message Thread API and session-local thread workspace | Session memory is not server persistence or cross-device synchronization. |
| DM | Current direct-conversation endpoint, recipient search, message lifecycle and private read state | Verify PostgreSQL persistence, reauthentication, revocation, IME and stale-response boundaries. |
| Group DM | Existing Group identity/routing, recipient selection and lifecycle where an approved runtime contract exists | `ConversationType.Group = 7` exists, but current `ConversationService.IsSupportedMvpType` and `ConversationAuthorizationService.IsSupportedMvpType` exclude it. A structural enum is not an enabled contract. New activation/authorization semantics require an Owner decision. |
| Files | Existing Workspace list/search/folders, inspector/preview/download and permission denial | Shared/Filters/Artifacts review APIs stay inert. General-user upload must be denied server-side unless its complete intake safety gate is qualified. Hiding a button is insufficient. |

## Implementation ownership

Inspected versions are Angular 22.2.3, Angular CLI 22.2.2, NgRx 22,
SignalR 10.0.11, .NET SDK 10.0.402 / EF Core 10, and PostgreSQL 18.6.
Syncfusion packages retain the existing mixed 34.2.8 / 35.1 series and license
workflow. No package, lockfile, database schema or runtime feature hold changed.

Each workstream uses a separate branch and worktree based on the inspected Main.
Concurrency is Coordinator plus three workers; D/E/F replace completed A/B/C workers.

| Workstream | Ownership |
| --- | --- |
| A | Approved Dark tokens, AppShell, Home, Workspace, global navigation and responsive layout |
| B | Channel timeline, Conversation Pane, composer, Message Thread and reconnect behavior |
| C | DM/Group identity, directory, recipient selection and supported membership commands |
| D | Files/preview/download, upload review and cross-screen visual QA |
| E | Backend authorization, HTTP negatives and actual PostgreSQL evidence |
| F | Canonical Sakura validation, synthetic real-backend browser gate, recovery and release operations |
| Coordinator | Integration, independent source review, CI, scope and final decision records |

## Preserved exclusions

Project/Task, Gantt, ProjectIDE, Avalonia, durable Draft/Links/Starred, advanced global search, External Execution, production Kafka, Kubernetes, general SaaS and new SSO/MFA are not acceptance targets. Their reachable APIs still require authorization checks. This scope does not authorize new production migrations, RLS/roles/grants, security-contract activation, security HOLD removal, secret/key changes, destructive data operations, school-user data or public release.

## Evidence rules

Use the exact candidate/Main SHA, command, provider, environment, result and limitations for every claim. Mocked browser tests prove frontend behavior only; EF InMemory proves no PostgreSQL behavior. Local Windows screenshots are diagnostic. Pinned Linux Docker comparison is required for approved visual parity at widths 1536, 1366, 768, 390 and 320 CSS pixels. Existing #481 remains unchanged and cannot be marked passed by this narrower pilot gate.

The final per-feature and deployment decision belongs in [the release gate](student-council-release-gate.md). Until those acceptance records exist, no real-user feature receives GO.

## Implementation checkpoint, 2026-10-19

| Workstream PR | Implemented candidate | Independent review |
| --- | --- | --- |
| [#1275](https://github.com/NYGsatoshi/Coglatas/pull/1275) | Live revision-441 tokens/logo, one navigation rail, native Workspace selection, dark members, responsive density and correct scoped navigation selection | Agent E; header status, container reflow and keyboard focus findings repaired |
| [#1274](https://github.com/NYGsatoshi/Coglatas/pull/1274) | Channel/Thread density, IME guard, stable retry identity, protected recovery and scoped draft capture during routine catch-up | Agent F, then E after catch-up repair; no remaining source finding |
| [#1273](https://github.com/NYGsatoshi/Coglatas/pull/1273) | Group-to-DM routing, current-page directory, multi-recipient selection, explicitly unavailable Group submission and single main landmark | Agent D; inaccurate read-only wording and nested landmark repaired |
| [#1278](https://github.com/NYGsatoshi/Coglatas/pull/1278) | Dense Files hub/preview, explicit inventory states, unavailable intake, CSRF fixture and obsolete global layout removal | Coordinator; browser fixture and layout corrections included |
| [#1277](https://github.com/NYGsatoshi/Coglatas/pull/1277) | Current Tenant/account/Workspace participant admission and existing-Direct reuse checks | Coordinator; dedicated reuse bypass was reproduced and repaired before candidate freeze |
| [#1276](https://github.com/NYGsatoshi/Coglatas/pull/1276) | Separate partial-only HTTPS gate, known-target negatives, actual Direct body revocation controls, redirect-aware Chromium transport guard and isolated recovery procedure | Agent E; missing Direct controls and redirect escape repaired |

These candidates are integrated together for final source and CI qualification.
No draft candidate or planning document authorizes Group activation or public
release. The shared implementation remains a modular monolith using existing
Conversation/Message, Workspace and File contracts. Members are added through
existing-user APIs; no delivered email invitation is claimed.

The shared integration PR is [#1279](https://github.com/NYGsatoshi/Coglatas/pull/1279).
A fresh reviewer who authored none of its changes reviewed the complete
`f491f779` source and the subsequent theme/Files test amendment. The amendment
repair includes the reviewer's Files cleanup finding; no source finding remains.
Approval is conditional on exact-head checks and visual qualification. The
real-user verdict remains NO_GO independently of code-merge qualification.

The [Workspace membership scope audit](student-council-workspace-membership-scope.md)
separates supported read/selection and exact role rendering from missing
add/change/remove/leave presentation contracts. Existing management commands do
not themselves project operation authority or eligible candidates to Angular.
No client-derived role authorization or self-leave policy is introduced.
