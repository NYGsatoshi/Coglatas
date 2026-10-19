# Student Council pilot scope

Status: implementation and qualification in progress; **NO_GO for real users**.
Target date: 2026-10-22 to 2026-10-23. The date does not waive any gate.

## Baseline and authority

- Product Main inspected on 2026-10-19: `6f940987f8df746736b6df5ea7371ab698028f5e`.
- Private specification Main identity only: `6a0491c925b4980ce737dfbdf775816c237e44fe`.
- Product Main CI [39222064110](https://github.com/NYGsatoshi/Coglatas/actions/runs/39222064110) completed successfully. This is baseline evidence, not qualification of pilot changes or the target deployment.
- Live Penpot source: `CoglatasUI`, file/library `52a5041f-08a1-80f1-8008-c5a09f8b5f61`; read-only connection confirmed on 2026-10-19. See [design traceability](student-council-penpot-traceability.md).
- Current source, tests, CI and deployment configuration take precedence over historical implementation claims.
- Resumed checkpoint on 2026-10-20 JST: Product Main `12d84a5625ea5cd3849edb960b1aa5fbfa4c4047` independently qualified in run `39287466687`, attempt 1. Approved read-lifecycle repair #1283 merged normally as `699d23501bf1c806b9923f1af477ea04c62b4784`. Its separate Main run `39294650950` passed backend execution but failed the strict Qodana Cloud rule ratchet; the original failure is retained. Expanded preview #1284 then passed its fresh current-base PR checks and merged normally at 2026-10-19T17:55:34Z as `a7a82cce46c1b78ce909793129869fb43fbd52c5`, whose own run `39298258521` remains separate and in progress. Spec Main and CODEOWNERS are unchanged at the 17:40 UTC recheck. The ten priority Penpot boards were rechecked read-only at revision 441 on 2026-10-19T16:59:14.565Z.

The later 18:25 UTC observation confirms that both Qodana lanes on Main
`a7a82cce` also failed the same unchanged UTF-8 literal rule. Both Main revisions
retain their failed quality results. The integration includes the independently
reviewed byte-equivalent literal repair, real WorkspaceChannel transport
coverage and an extension of the existing real-backend Workspace-create browser
journey. Final current-head and resulting Main executions remain independent;
test source and earlier passes do not establish their qualification.

Main `a7a82cce` run `39298258521` is now terminal with failure: all six required
contexts succeeded, while both Qodana lanes failed the unchanged literal rule.
The extended Workspace-create browser case also has a retained failed licensed
run `39303226114` at `b8c187ee`. A grant invalidation cancelled its send response
after persistence; no Thread assertion was reached. The candidate now settles
the synthetic authorization Outbox before the existing fresh bootstrap.
Immediate create/send recovery is still unqualified. The later settled
`f4fd7aea` candidate passed all eight required licensed P0 cases in run
`39307031309`, attempt 1, without retries. That result does not weaken
authorization invalidation or supersede its separate failing PR run.

The 2026-10-19T19:35Z GitHub recheck confirms Main `a7a82cce`, the same Spec
Main and CODEOWNERS, thirteen unchanged private specification HOLD heads,
and the three Product review-only heads already observed after discovery.
No HOLD identity or approval is inherited from an earlier head. PR #1285
head `f4fd7aea` remains blocked by frontend architecture and Functional
messaging failures in run `39307010109`. The unpublished successor addresses
the architecture owner boundary and separate send/mobile defects and improves
bounded Thread diagnostics; the retained timeout is not claimed repaired.
See [the release gate](student-council-release-gate.md) for the current limits.

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

These workstream candidates were integrated in #1279, normally merged and
closed as superseded while preserving their branches/worktrees. Follow-ups
#1280, #1281 and #1282 also merged through normal protected checks and independent
review. The separately approved read-lifecycle repair #1283 also merged normally
after exact-head CI and independent verification. Expanded image preview #1284
also merged normally after fresh current-base CI and independent review.
No draft candidate or planning document authorizes Group activation or public
release. The shared implementation remains a modular monolith using existing
Conversation/Message, Workspace and File contracts. Members are added through
existing-user APIs; no delivered email invitation is claimed.

The shared integration PR is [#1279](https://github.com/NYGsatoshi/Coglatas/pull/1279).
A fresh reviewer who authored none of its changes reviewed the complete
`f491f779` source and the subsequent theme/Files test amendment. The amendment
repair includes the reviewer's Files cleanup finding; no source finding remains.
Its exact-head checks and independent visual review subsequently completed;
the first merged Main failure and separate quality/UI repairs remain recorded.
The real-user verdict remains NO_GO independently of code-merge qualification.

The [Workspace membership scope audit](student-council-workspace-membership-scope.md)
separates supported read/selection and exact role rendering from missing
add/change/remove/leave presentation contracts. Existing management commands do
not themselves project operation authority or eligible candidates to Angular.
No client-derived role authorization or self-leave policy is introduced.

The current candidate repairs a separate existing-contract Channel gap:
canonical WorkspaceGeneral is WorkspaceChannel(10), which the Message service's
send guard and unread/update polling previously omitted. Existing persisted
channels now use bounded send/canonical-reply/polling paths with current
Workspace contribution, participant and reply-root authority. Independent review
found stale posting flags in the first unpublished implementation; six actual
PostgreSQL reproductions failed there before the fresh snapshot repair.
Source `72fb55a0` subsequently passed 73 selected checks: 57 actual PostgreSQL/
Kestrel cases and sixteen unit/InMemory cases. Integration HTTP amendments and
exact-head CI remain separate. Generic Conversation creation, moderation,
Group activation and the separate Channel/Post model are unchanged.
