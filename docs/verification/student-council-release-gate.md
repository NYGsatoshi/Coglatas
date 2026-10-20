# Student Council pilot release gate

The public pilot verdict is **NO_GO**. The target dates, 2026-10-22 through
2026-10-23, do not waive authorization, deployment or school approval.
This gate defines the separate Student Council Pilot Golden Path. It does not
change or satisfy the broader Project/Task contract in #481.

## Current source and evidence checkpoint

At 2026-10-19T22:00:08Z, PR [#1288](https://github.com/NYGsatoshi/Coglatas/pull/1288)
normally merged as Product Main
`7bba2d00481fb8093ef87790f5b4fb2553856ba6`, tree
`d6943b0cd4b48c6be82c30493b3a4aa6d9b2bc12`, with ordered parents
`713fc9672a88a2752098ce07f9f3dd2225ce621e` and
`5003cfa549073d33ad5051a6f77a7bbfeadc919d`.
Its own full Main qualification is pending at this checkpoint.
Private Spec Main remains `6a0491c925b4980ce737dfbdf775816c237e44fe`
at the last recorded read; private approval/HOLD records remain authoritative.

The combined UI candidate is
`89fe1c5422b62092f520ea60cf9a30dca9f3337c`, tree
`d7ecec57b8e20d43a0278eab7920025b01fa73e7`.
Its frontend tree is `eaf28378d9757fc6bb3780593d7cc5329fa65c26` and
browser-test tree is `c0012ec62a3f68d4dedb30f228d5552afb7791bb`.
The later release integration merges Main's two CI/test-only paths without
changing those two trees. This records source equivalence; it does not relabel
an earlier execution as a later commit's execution. PR #1287's earlier
`66fb0d8e` qualification is also distinct from the expanded candidate.

| Execution | Actual result and limit |
| --- | --- |
| UI candidate licensed real-backend [39328164824](https://github.com/NYGsatoshi/Coglatas/actions/runs/39328164824), attempt 1 | Exact `89fe1c54`; all eight required legacy P0 cases passed without retries. The Workspace case includes the new ordinary member-detail HTTP/UI assertions. FCI04 fast and Files functional-full also passed as two separate owner executions. They are not two extra P0 cases. |
| UI candidate controlled Linux V2 | Fresh production build and source/index/build guards passed. All 1,562 units executed: 1,540 passed and 22 timed out across fifteen files. Exit 1; no later architecture/browser stage ran. No assertion or timeout was relaxed. |
| UI candidate controlled Linux V3 | A new isolated source volume, fresh build and measured four-CPU context passed initialization/build. All 1,562 units across 143 files passed with zero skips. Architecture, browser and capture stages remain pending at this checkpoint. The resource context changes; causation of the earlier timeouts is unproven. |
| PR #1288 [39327700542](https://github.com/NYGsatoshi/Coglatas/actions/runs/39327700542), attempt 1 | All sixteen jobs succeeded; 36 successful checks plus five finite source-configured skips; all six strict required contexts succeeded. Actual 379 PostgreSQL-selected tests include both owning HTTP controls; SQL 18.6 was observed. Six Functional owners passed once each. Routed-out frontend, licensed P0, full Qodana and security runtime earn no execution credit. |
| Main `713fc967` [39324537324](https://github.com/NYGsatoshi/Coglatas/actions/runs/39324537324), attempt 1 | Terminal failure: 31 successful, three failed and three skipped jobs. The licensed fixture failed before browsers; both full Qodana lanes found the two private test-accessor ratchets repaired by #1288. All required contexts being green did not qualify this Main. |
| Main `713fc967` completed-producer DB [39330416938](https://github.com/NYGsatoshi/Coglatas/actions/runs/39330416938), attempt 1 | New supported dispatch after the producer completed. Four jobs succeeded, PostgreSQL 18.6 observed, nine scenarios in each Small/Medium profile and all 28 structural decisions passed. Numerical assurance remains SUSPENDED/NOT_EVALUATED under #1128. |

The licensed `89fe1c54` job built a new application image and ran the canonical
PostgreSQL/Npgsql stack. It did not publish an actual SQL-version read, running
container-image inspection or served-asset manifest. Exact PostgreSQL minor
version and strict image continuity are therefore unobserved for that run.
Its success is not external HTTPS, complete Group lifecycle, full pilot matrix
or target-host qualification.

## Decision vocabulary and feature acceptance

- `GO`: every applicable UI, API, PostgreSQL, security, E2E and operational gate passed for the exact deployed revision.
- `GO_READ_ONLY`: qualified reads/downloads, with every prohibited operation denied by the server.
- `INTERNAL_SYNTHETIC_ONLY`: isolated synthetic evaluation without school accounts or real-user data.
- `NO_GO`: missing or failed required evidence, unresolved authorization contract or unsafe shared foundations.

The following verdicts concern real-user opening. Source implementation and
isolated synthetic passes remain useful evidence without changing these verdicts.

| Feature | UI | API | PostgreSQL | Security | Real-backend E2E | Verdict |
| --- | --- | --- | --- | --- | --- | --- |
| Workspace | Approved Dark list/selection/create; read-only detail candidate; management/self-leave incomplete | Existing authorized reads and canonical create; no invented management capability | Named provider/HTTP controls and Main suites passed at their recorded revisions | Current scope clearing and approved lifecycle controls; full target matrix pending | Eight-case candidate P0 passed, including member detail; external target pending | NO_GO |
| Channel Chat | Dense Conversation timeline/composer, current participant projection and responsive disclosure candidate | Existing Conversation/Message source; no duplicate Posts model | Canonical send/reply/replay and current-authority controls passed at recorded revisions | Admission, persisted posting and transport controls; complete target matrix pending | Candidate channel send/receive/reload P0 passed; target pending | NO_GO |
| Thread | Existing Message Thread contract; compact composer, local tabs and loaded-root navigation candidate | Same-Conversation root/reply APIs | Named provider and source-bound Functional controls | Active authorized body; delayed/denied clearing tested within covered cases; full matrix pending | Candidate P0 passed; final source and target remain separate | NO_GO |
| Group DM | DM classification and multi-recipient selection; unsupported submission unavailable | Group type 7 remains unsupported | Rejection controls are not activation qualification | Owner creation/membership/history/last-admin contract pending | Not enabled; no completed lifecycle E2E | NO_GO |
| DM | Separate directory, composer, Thread, read/reconnect/error handling | Existing dedicated Direct endpoint and separately approved read-lifecycle repair | Named real provider/HTTP/transport controls and hosted Main suites | Current authorized participation/Tenant/account/Workspace scope; target matrix pending | Existing required P0 passed at candidate source; target pending | NO_GO |
| Files | Approved Dark Hub, authorized preview/download and denied states | Existing APIs; UI intake disabled | Named file/provider/HTTP controls passed at their recorded revisions | Scanner pipeline incomplete; target intake denial and complete mutation matrix pending | Synthetic owner passed; target pending | NO_GO |

Files is not declared globally read-only from a disabled upload button or one
intake flag. `FileSharing=false` locally denied intake with no persisted upload/file metadata or storage change in that control;
all other reachable modifying routes and the target policy still require review.
Only prevalidated synthetic samples may be used during isolated qualification.

## Student Council public HTTPS Golden Path

Run against the exact proposed deployment with synthetic accounts and the real
backend. Record Product SHA, immutable artifact, database version, UTC, external
endpoint and provider/test reports. Keep credentials, cookie values, protected
bodies and private infrastructure out of public artifacts. Every step needs
passed, failed or not-executed status.

1. Connect through external HTTPS with valid TLS and the intended host.
2. Log in using the supported production authentication flow.
3. Select an authorized Workspace.
4. Show its Team/Channel conversation.
5. Send a Channel Message.
6. Receive it in a second user's browser.
7. Send and receive a Thread reply.
8. Send and receive a one-to-one DM.
9. Create and exchange a Group DM only after its contract and runtime qualification.
10. Verify user-specific unread and mention state.
11. Browse and download an authorized prevalidated synthetic File.
12. Reload and prove persisted Messages, replies, membership and File state.
13. Log out and clear protected browser state.
14. Reject unauthenticated access.
15. Reject unauthorized DM, Group, Channel and File access without protected content.
16. Revoke membership and reject fresh, delayed and reconnected access while clearing the UI.
17. Restart the isolated stack and verify retained DB, files and key continuity.

An unsupported Group cannot be substituted with a mock success or a Channel.
A partial pilot requires an explicit reduced scope and independent safe
server-side separation; a shared authentication or isolation defect blocks all
real-user features. The broader #481 gate remains unchanged and unpassed here.

## Security negative matrix

| Boundary | Required controls |
| --- | --- |
| Tenant A to Tenant B | Lists, detail, message body, search, membership, SignalR, preview/download and modifying APIs |
| Workspace A to Workspace B | Actor and every proposed participant satisfy current approved scope; no client-inferred authority |
| Nonparticipant | Channel, Group DM, DM and inherited Thread body/participant reads denied |
| Account/member state | Invalid, disabled, suspended, deleted, removed and revoked identities; stale subordinate membership |
| Session | Logout, persisted revocation, expired cookie, re-login and protected-cache clearing |
| Request | Missing/invalid CSRF, duplicate submission and existing masked denial semantics |
| Realtime | Disconnect/reconnect, authorization events, delayed HTTP and old Workspace subscriptions |
| Files | Size, MIME, extension, content, scan/quarantine state, ownership and every prohibited write |

Provider-sensitive cases require actual PostgreSQL and HTTP. InMemory, mocks,
skips and configuration alone earn no provider qualification. Authorization
queries observe committed revocation when evaluated; no linearizable
cancellation after the final authorization query is promised.

## Visual and interaction gate

Use the read-only revision-441 Approved Dark boards in
[Penpot traceability](student-council-penpot-traceability.md). Independently
inspect original pinned-Linux captures at 1536, 1366, 768, 390 and 320 CSS
pixels, plus explicit 960px and 320-by-420 keyboard-height controls where used.
Check layout, spacing/density, typography, color, surface hierarchy, navigation,
dialog placement, overflow, status visibility, keyboard focus, touch targets and
IME composition. Mobile must adapt panes rather than inherit fixed desktop widths.
Full font/design parity, physical IME and full WCAG conformance remain unqualified.

## Deployment qualification

| Gate | Current evidence/status |
| --- | --- |
| Host access | Sakura SSH host/user and stable HTTPS references are unavailable; no target access claimed |
| Canonical configuration | Existing `deploy/sakura/deploy.sh`, Compose and README retained; local synthetic Caddy/tunnel validation is configuration evidence only |
| Non-mutating target validation | Target `COGLATAS_DEPLOY_VALIDATE_ONLY=true` execution pending |
| Origin | Preserve loopback/private boundary and explicitly trusted forwarding peers |
| HTTPS | Stable URL, DNS/TLS, AllowedHosts, HSTS and redirects pending |
| Auth/CSRF | Local/hosted synthetic cookie/CSRF controls exist; target Secure cookies, sessions and missing-token rejection pending |
| PostgreSQL | Target version, health, migration state and persistent volume pending |
| Files and keys | Target retained file storage and Data Protection key continuity pending |
| Bootstrap | Use only existing explicit bootstrap, then disable unnecessary seed; no development fixed account |
| Upload policy | UI intake disabled; target server-side denial and all modifying routes must qualify before a read-only claim |
| Backup | Local synthetic DB/files/key mechanics are partial; target backup and retention pending |
| Restore | Local 114-table/79-row/74-migration comparison and two sample-byte controls are partial; strict schema comparison failed and full target restore/application decryption is unqualified |
| Rollback | Prior immutable artifact, schema compatibility, retained data and verified procedure pending |
| Logs/secrets | Requirement: external ignored `.env` and protected license; no tokens/passwords/file contents in logs or PRs. Target configuration/log validation is pending. |
| School opening | School/privacy/operator prerequisites and final Owner release approval pending |

No target database, volume, keys, account, migration or public route was changed.
A Quick Tunnel is temporary, not a stable endpoint. Compose rendering is not
startup, and a backup file without isolated restore is not recovery qualification.

## Owner decisions still needed

The separately scoped Direct read-lifecycle decision is already approved and
merged in #1283. Its bounded source/provider qualification does not authorize
Group activation. Do not request that same decision again.

Only these new or outstanding decisions remain:

- Group DM creation, participant admission/removal, history visibility and last-admin/leave semantics, as the separately prepared private proposal requires.
- Workspace management operation/candidate/allowed-role presentation and last-Owner/self-leave policy. Ordinary read-only member detail does not require or grant these powers.
- Existing Sakura target references and, after technical qualification, formal public-opening approval, school privacy conditions and an accountable operator.

New production schema, RLS, roles, GRANT, key custody, destructive changes or
unapproved dependencies still require their explicit Owner decision if needed.
Product #1269/#1270/#1271 and private Spec #287 through #299 retain their
independent review/HOLD status. Nothing here publishes or reapproves private Spec.

## Retained failures and separate successes

Git history and immutable source-bound receipts preserve the full earlier records.
These checkpoints remain material; later isolated passes do not erase them.

| Earlier execution | Retained disposition |
| --- | --- |
| Initial integrated visual run | Eleven passed, ten failed: nested DM landmark and missing synthetic Files CSRF setup. Bounded repairs and subsequent independent evidence are recorded separately. |
| Integrated `3e739872` | Units 1,440 passed; architecture 57 passed/two failed; browser 237 passed/fourteen failed/thirteen skipped; one changed-file InspectCode finding. Later owner inventory, accessibility, role/fixture and disposal repairs were independently reviewed. |
| `b8c187ee` licensed P0 | Workspace send response was cancelled after DB persistence; Thread was not reached and later cleanup cases failed. The Test-only Outbox barrier later passed at `f4fd7aea`; immediate create/send recovery was not thereby qualified. |
| `f4fd7aea` primary | Two architecture inventory failures and Functional Thread-open timeout before denial. Separate bounded fixes do not establish the timeout's cause. |
| Historical Main `699d`, `a7a8`, `f282`, `713f` | Full Qodana failures remained blockers despite green required contexts. UTF-8 literal and private-helper/accessor repairs kept quality budgets unchanged. |
| `ecb602bd` full static | 286 passed/thirteen skipped/three failed after configured attempts; separate mobile compatibility failures. Disclosure/preconditions and one Linux drawer baseline were independently reviewed without threshold relaxation. |
| `83be8d4d` full static | 302 selected: 283 passed/thirteen skipped/five timeout errors/one Files vertical-scroll assertion failure. One unchanged-source six-case controlled diagnostic passed; the full failure and unproven cause remain. |
| `f66ef72a` Thread | Build, 296 units and 59 architecture checks passed; browser 33 passed/three timeout errors. Separate header geometry source `eebaa8df` passed all 36 selected cases and 39 captures; timeout causation remains unproven. |
| `9b55` combined local sessions | Stopped with exit 137 for the resource queue before their remaining tests; no pass credit. |
| UI candidate V1 | Linux source initialization failed on Windows-index stat metadata despite matching bytes; private-index successor uses exact Git blob/mode checks. No application tests ran in V1. |
| UI candidate V2 | All 1,562 units ran, with 22 timeouts and exit 1. V3 is a distinct measured four-CPU execution, not a relabelled V2 pass. |
| Main `713f` scheduled DB [39329724884](https://github.com/NYGsatoshi/Coglatas/actions/runs/39329724884) | Resolver ran before the producer completed; no collector ran. Later supported dispatch passed structural controls without changing the resolver or rerunning that failed attempt. |

Three high-severity development-dependency `braces` Dependabot alerts across the
existing lockfiles remain needs-review; no upgrade, dismissal or exploitability
claim is made here. Numerical performance assurance remains suspended under #1128.

## Final receipt requirements

Before GO, publish a safe exact-revision A-G receipt covering Overall, Agent
Results, Feature Acceptance, Penpot Parity, Deployment, Owner Decisions and Next.
Record Product/Spec before and after, actual PR/head/tested-merge/Main identities,
independent reviews, source-bound execution, screenshots, target and recovery
evidence. A PR pass does not qualify its resulting Main; Main success does not
qualify the public deployment. No real accounts or data may be introduced until
every applicable technical and Owner condition is satisfied.
