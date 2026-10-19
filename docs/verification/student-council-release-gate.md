# Student Council pilot release gate

Current overall verdict: **NO_GO**. Qualification is in progress.
This record defines the separate Student Council Pilot Golden Path; it neither
changes nor satisfies the broader #481 product release contract.

Checkpoint: 2026-10-20 JST, after the 2026-10-19T17:55:34Z normal merge.
Product Main `a7a82cce` contains pilot integration #1279, follow-ups #1280,
#1281/#1282, the separately approved bounded read-lifecycle repair #1283 and
expanded preview #1284. Its own run `39298258521` is in progress. Predecessor
`12d84a56` qualified independently in run `39287466687`, attempt 1. Main699d
run `39294650950` passed backend execution but failed Qodana Cloud's strict
UseUtf8StringLiteral ratchet; that failure is retained and a test-only correction
is under review. Successful predecessor/PR checks do not automatically qualify
a later Main or the target deployment.

## Decision vocabulary

- `GO`: all applicable UI, API, PostgreSQL, security, E2E and operational gates passed for the exact deployed revision.
- `GO_READ_ONLY`: qualified reads/downloads only, with prohibited writes denied by the server.
- `INTERNAL_SYNTHETIC_ONLY`: isolated synthetic evaluation; no school accounts or real-user data.
- `NO_GO`: missing or failed required evidence, unresolved authorization contract, or unsafe shared foundations.

## Feature acceptance

| Feature | UI | API | PostgreSQL | Security | Real-backend E2E | Verdict |
| --- | --- | --- | --- | --- | --- | --- |
| Workspace | Dark shell, selection and canonical role display; management UI incomplete | Existing read/create contract; mutation authority/candidate projection absent | Provider controls passed; see checkpoint | Full pilot matrix incomplete | Target pending | NO_GO |
| Channel Chat | Dense timeline/composer; six-width diagnostic passed | Candidate restores existing WorkspaceChannel send/reply/polling | Canonical provider and participant HTTP controls; integration amendments pending | Current contribution/posting/root snapshots; full pilot matrix incomplete | Full candidate and target pending | NO_GO |
| Thread | Existing workspace and keyboard/IME controls retained | Existing Message Thread contract | Existing provider controls passed | Full revocation matrix incomplete | Target pending | NO_GO |
| Group DM | Routing and multi-recipient UI; submission unavailable | Group runtime unsupported | Unsupported-type rejection tested | Activation contract unresolved | Not enabled | NO_GO |
| DM | Directory/composer and header theme placement implemented | Existing direct endpoint; admission and approved read-lifecycle repair merged | Named provider/transport controls and exact-source default hosted suite passed | Current source repair qualified; full target matrix incomplete | Target pending | NO_GO |
| Files | Hub/preview, image viewport controls and expanded surface merged | Existing Files contract only | Named provider/HTTP controls passed in isolated synthetic runs | Configured FileSharing intake denial verified locally; scanner, full mutation matrix and target policy remain unqualified | Target pending | NO_GO |

## Mandatory public HTTPS journey

Run with synthetic accounts against the exact proposed deployment, without API
mocks. Record the external URL privately, deployment receipt, exact Product SHA,
database version, artifact identity, timestamp and test report. Keep credentials,
cookie values, protected bodies and private infrastructure out of public reports.

1. Reach the application through external HTTPS and log in.
2. Select an authorized Workspace and show its Team/Channel conversation.
3. Send a Channel Message; receive it in a second user's browser.
4. Reply through the current Message Thread API and UI.
5. Send and receive a one-to-one DM.
6. Create, route and exchange Group DM messages, only after its runtime contract is approved and qualified.
7. Verify unread/mention state for the correct user.
8. Browse and download an authorized prevalidated synthetic File.
9. Reload and prove persisted Messages, Thread replies, membership and File state.
10. Log out and prove authenticated resources are inaccessible.
11. Reject DM/Group/Channel/File reads by unauthorized users without protected content.
12. Revoke membership and show immediate safe clearing, denied fresh reads and rejection of late/reconnected stale data.
13. Restart the isolated qualified stack and verify retained data and key continuity.

Each step must be reported as passed, failed or not executed. A missing Group DM
contract cannot be replaced with a mock success or a Channel masquerading as a DM.

## Security negative matrix

| Boundary | Required controls |
| --- | --- |
| Tenant A to Tenant B | Lists, detail, body, search, membership, SignalR, File preview/download and modifying APIs |
| Workspace A to Workspace B | Actor and every proposed recipient must satisfy the current approved scope; no client-derived authority |
| Nonparticipant | Channel, Group DM, DM and inherited Thread body/participant access denied |
| Account/member state | Invalid, disabled, suspended, deleted, removed and revoked identities; stale subordinate membership |
| Session | Logout, persisted revocation, expired cookie, fresh login and protected-cache clearing |
| Request | Missing/invalid CSRF; duplicate submissions; safe fixed error handling |
| Realtime | Disconnect/reconnect, authorization events, delayed HTTP responses and old Workspace subscriptions |
| Files | Size, MIME, extension, content, quarantine/scan status, ownership and prohibited writes |

Run provider-sensitive and HTTP controls with actual PostgreSQL. Preserve failed
attempts and exact source revisions. No skipped/unconfigured provider test earns
qualification credit.

## Visual and interaction gate

Compare the live Approved Dark boards recorded in
[Penpot traceability](student-council-penpot-traceability.md) against Linux Docker
screenshots at 1536, 1366, 768, 390 and 320 CSS pixels. Verify layout, spacing,
density, typography, palette, surface hierarchy, selection/focus, dialogs,
overflow, status visibility and keyboard operation. Exercise IME composition
without accidental send. Document responsive adaptations and remaining defects.

## Deployment qualification

| Gate | Current evidence/status |
| --- | --- |
| Host access | Needs verification; do not infer access from a checked-in runbook |
| Canonical entrypoint | `deploy/sakura/deploy.sh`; explicit `caddy` or `trycloudflare` state |
| Non-mutating validation | Target-host `COGLATAS_DEPLOY_VALIDATE_ONLY=true` execution pending |
| Origin | Loopback/private network only; explicit trusted forwarded-header boundary |
| HTTPS | Target certificate, host allowlist, HSTS and stable URL pending |
| Auth/CSRF | Local cookie/CSRF and hosted synthetic controls exist; target HTTPS/Secure-cookie/session qualification pending |
| PostgreSQL | Actual target health, migrations and retained volume pending |
| Storage/keys | Retained upload and Data Protection volumes pending |
| Intake policy | Local server-side FileSharing=false denial passed; target flag/policy and all reachable modifying routes still require qualification |
| Bootstrap | Existing explicit bootstrap only; disable after initial provisioning |
| Backup/restore | Local mechanics and bounded SQL controls are partial evidence; strict schema comparison did not pass, and target complete DB/bytes/keys restore remains unqualified |
| Rollback | Prior immutable artifact, schema compatibility and data preservation pending |
| Logs/secrets | No credentials, tokens or file content; owner-only external `.env`/license |
| Public pilot | Owner release approval and school/privacy/operator prerequisites pending |

A Quick Tunnel URL is temporary and cannot be reported as stable production
hosting. Compose rendering is not startup evidence. A backup file without a
successful isolated restore is not recovery qualification.

## Owner decisions

A required protected-content revocation control failed during preceding
qualification. Evidence and the approved lifecycle decision are retained in
the private managed review collection for the Owner. The admission repair does
not qualify already-persisted read access. No real-user pilot is permitted
until the approved repair and all applicable provider/HTTP controls qualify.
Public reports omit the private finding, reproducer and protected payload.

The Owner approved the separately scoped persisted-read lifecycle repair on
2026-10-20 JST. PR #1283 merged that bounded change with no new schema, role,
RLS or grant. Exact head `4c42cf59` passed build, the private HTTP acceptance
control, 61 focused controls and all 320 declared SecurityArchitecture identities
across thirty disjoint class invocations. Hosted default execution independently
passed 2,492 backend/twelve architecture cases and all required/quality checks.
The original local full/namespace failures remain retained; separate completed
executions do not turn them into passes. One bounded local session stopped
nonzero before the next invocation; a second ran only the unexecuted classes.
Root independently reconciled all thirty raw TRX files and satisfied its local
test-qualification condition. No Owner, private Spec or SEC-ARCH HOLD was waived.
Formal architecture attestation remains UNVERIFIED/Advisory and pre-Avalonia
remains BLOCKED. The read-lifecycle decision is already approved and must not
be requested again; target deployment and school-user release remain separate.

Only newly required decisions should be requested. Prepare concrete reviewable
options before asking. Current candidate: Group DM requires review of the
unsupported runtime type and its authorization/membership contract. Public
release, real-user data and production security/schema/key changes remain
explicit Owner gates. Existing approved decisions and private HOLDs are not
reopened by this work.

Workspace member-management/self-leave presentation also lacks a server-owned
authority/candidate contract. Keep the existing authorized member list until
the concrete new contract is approved and qualified; do not infer mutation
authority from client role labels. A disabled upload control or intake flag
alone cannot establish a globally read-only Files product.

## Final receipt

The following checkpoints preserve historical source-specific execution and
failure records. Pending statements inside those checkpoints describe that
revision's state; the current disposition is recorded above and in the final
local exact-Main receipt. Later passes do not relabel earlier failures.

The bounded WorkspaceChannel candidate uses the existing Conversation/Message
model. Its source `72fb55a0` passed 73 selected checks with zero failures/skips:
57 actual PostgreSQL/Kestrel cases and sixteen unit/InMemory cases. These
selections overlap previous evidence. Canonical Workspace creation/send/reply,
same-target replay, unread/update polling, current Workspace/Tenant/account
authority and six committed posting/root changes are covered. The initial
unpublished stale-state candidate and its six failing reproductions remain
retained. The final integration adds attachment metadata/new-grant HTTP probes
and an equivalent UTF-8 test-literal quality correction; neither their exact
execution nor required CI is inferred from the predecessor's result.

### Local qualification checkpoint

- Coordinator integrated source `9728f32d`: **552/552 frontend tests**, 51
  files, pinned Linux image, two workers; production build passed. This
  checkpoint precedes subsequent small navigation/catch-up/landmark/layout
  repairs, whose final combined build and rendered evidence are recorded in
  the integration PR receipt.
- Coordinator actual PostgreSQL 18.6: **21/21 admission tests**, including a
  real Kestrel/cookie/CSRF HTTP test; **14/14 existing Workspace capability,
  dashboard, inbox and Message Thread provider tests**. All ran with the
  connection configured and **zero skips**. These do not erase the separate
  failed revocation control retained privately.
- Agent E owning selection: **65/65**, then clean rebuilt **22/22** at
  `159a1fc8`. The coordinator's 21-test admission filter and the owning 22-test
  selection have different membership and are not cumulative totals.
- Shell diagnostics passed five requested widths, including panel-open
  component overflow, close focus and visible Research status. Final combined
  diagnostics add 960px and correct scoped navigation state.
- The first combined visual run retained **11 passed / 10 failed**: five DM
  cases exposed a nested main landmark, and five Files cases lacked a
  synthetic CSRF endpoint. The interceptor denied preview before dispatch;
  server authorization was not relaxed. Both causes are corrected. Files
  fixture-only reruns passed five widths twice; final global-layout changes
  require a fresh integrated build.
- Messaging catch-up fix: new regression first reproduced two failures;
  final owning **215/215** tests passed. A pre-existing inbox test failed at
  the same precondition on the exact baseline overlay; settling its initial
  fixture before the original assertion repaired setup without weakening any
  denial check. Full failure history remains in the workstream receipt/CI.
- HTTPS gate helpers: **14/14** passed; actual Chromium 307/308 transport
  regression **1/1** passed with both forwarding counterexamples and blocked
  foreign-body controls. These are runner tests, not public pilot evidence.
- Local canonical Caddy and tunnel Compose configurations rendered with
  synthetic values. No target-host validation, startup, migration, TLS,
  backup, restore, rollback or restart pass is claimed. No Sakura alias or
  stable target URL was available; the request for references remains pending.

Pinned visual references and before/after PNGs are retained under ignored
`artifacts/student-council-pilot/visual/`; the reference manifest binds all
five conversation/Files exports to live Penpot revision 441. The integration
PR and final local receipt carry the exact final candidate/Main CI and visual
results, without requiring a self-referential commit identity in this file.

The later integrated `f491f779` candidate passed **569/569** frontend tests,
production build and **22/22** Linux width diagnostics. Independent PNG review
found the theme/Send overlap despite those passes. Header placement and pointer
hit-target controls repair it; standalone theme transitions are also covered.
Final regeneration passed **23/23** in its own artifact directory after preserving
a concurrent test-directory collision. The source amendment passed **12/12**
focused tests, **12/12** Functional diagnostic/reporter contracts and a fresh
production build. These browser runs use synthetic HTTP fixtures.

Hosted run [39244578163](https://github.com/NYGsatoshi/Coglatas/actions/runs/39244578163),
attempt 1, tested merge `adafe853` from PR head `f491f779`. Its backend job
passed **2,482/2,482** tests and **12/12** architecture tests with PostgreSQL
18.6 configured; the total includes non-provider tests. Five Functional owners,
including Message Thread, denial and recovery, passed on real isolated stacks.
The Files owner failed because its setup still expected enabled UI intake.
Its amendment asserts unavailable intake, prepares only the deterministic Test
tenant sample through the existing authenticated API, and retains fresh reads,
download-byte, reload, validation and denial checks. Cleanup now exercises the
real row selection/deletion dialog. Independent review corrected a stale Close
button guard after reload. No skip, retry, timeout or negative oracle changed.
The new head requires its own complete protected CI result; the failed attempt
is retained and cannot be relabelled as qualification of the amendment.

### Full-suite qualification and subsequent corrections

Run [39247077266](https://github.com/NYGsatoshi/Coglatas/actions/runs/39247077266)
attempt 1 executed merge `b757c1d0` from head `3e739872`. It passed five of six
required contexts: actual backend **2,482/2,482**, architecture **12/12**,
configured PostgreSQL 18.6 and **six of six Functional owners**, each executed
once without retries. Frontend units passed **1,440/1,440**. The run still
failed: frontend architecture had **57 passed / 2 failed**, browser cases had
**237 passed / 14 failed / 13 skipped**, and InspectCode found one changed-file
resource-initialization warning. All results remain associated with that source.

The architecture inventory omitted an existing directly injected selection
singleton. Its declared ownership and companion freeze documentation now match
the source; executable checkers and mutation controls are unchanged, and their
Linux **59/59** checks passed. Role presentation now preserves the five existing
canonical roles, displays unknown values as unavailable and localizes the
Syncfusion formatter. Its focused Linux **47/47** checks passed; authority and
disabled live member actions are unchanged.

Browser failures covered seven cases in both profiles. Two Linux snapshots
still represented the old light shell; their replacements were independently
inspected and replayed **2/2** with unchanged thresholds. Workspace metric words
now wrap naturally; **23/23** targeted cases passed in each desktop and mobile
profile. Navigation tests distinguish the brand from the Workspace destination
and exercise real focus wrapping. Announcement navigation checks the actual
internal scroll host while retaining reset, restoration, sticky action and focus
oracles. Accessible contrast failures in Task dependency guidance and the
Announcement audience label are repaired with existing theme tokens, rather
than exclusions. Files tests retain Japanese controls and unavailable intake,
and verify populated responsive columns, geometry, drawer and keyboard behavior;
their Linux **4/4** cases passed.

The request resource is now registered for disposal before assigning JSON
content, closing the initializer-exception gap reported by InspectCode. Its
actual PostgreSQL/Kestrel/cookie/CSRF HTTP test passed **1/1**, zero skips. The
local role/shell unit checkpoint passed **1,471/1,471**, 141 files. After the
final CSS-only contrast correction, the combined production build and **60/60**
targeted browser cases passed in pinned Linux, zero retries. Those cases include
all fourteen previous browser failures and both profiles' pilot diagnostics.
Independent source review found no remaining introduced finding; final PNG
review observed no meaningful regression. Exact-head protected CI is still
required before merge. The thirteen browser skips match existing
Main conditions (retired SPA and alternate-profile cases); they earn no credit.
No skip, retry, accessibility rule, snapshot threshold or checker was weakened.

Workspace member add/change/remove UI lacks a server-owned operation,
candidate and allowed-role projection; ordinary self-leave also lacks a current
command policy. See [the source audit](student-council-workspace-membership-scope.md).
Existing-user admission must not be relabelled as delivered email invitation.
These gaps remain explicit alongside the private persisted-read release blocker.

Before GO, replace the provisional rows with exact evidence and record Product
Main before/after, Spec Main before/after, PR and merge identities, independent
reviews, candidate/Main CI, design comparisons, provider results, target-host
receipt, recovery drill and per-feature verdict. A critical shared authentication
or isolation failure blocks every real-user feature.

### Reviewed source integration

PR [#1279](https://github.com/NYGsatoshi/Coglatas/pull/1279) was normally merged
as `296050fadf380789c60c88742e3f08a28abc206a` after all six required contexts
and applicable quality checks passed. Run
[39252359289](https://github.com/NYGsatoshi/Coglatas/actions/runs/39252359289),
attempt 1, actually checked out `bba237210037de25346b7a2477b10a9f95f6783f`;
its tree matches the reviewed head and merge. Results: backend 2,482/2,482,
architecture 12/12, frontend units 1,471/1,471, static contracts 59/59, Syncfusion
license-guard contracts 4/4, browser 251 passed and 13 unchanged historical skips, plus all six real
Functional owners passing once each. InspectCode had zero changed-file findings.
Configured scanners that did not execute earn no credit. Main CI is a separate
observation, not inherited from the PR.

Final-source local PostgreSQL 18.6 checks also passed 35/35 with zero skips,
including actual Kestrel/cookie/CSRF and existing Workspace/inbox/Thread controls.
These overlap the earlier selections and do not qualify the privately failed
persisted-read control. A separate small follow-up improves mobile header
density and open-panel accessibility; see
[its bounded source and evidence](student-council-mobile-header-polish.md).
All real-user feature verdicts remain NO_GO pending complete target-host and
pilot acceptance evidence, independently of the approved source repair.

### Main quality follow-up

Main run 39255130228 also completed both Qodana analyses but failed the
unchanged inspection ratchet: `MemberCanBePrivate.Local` had two findings
against a zero budget. Community and Cloud independently identified only the
`Tenant` and `Actor` properties in the private hosted-HTTP test helper. Their
visibility is now private; no test assertion, production code, suppression or
budget changed. Independent source review found no implicit consumers.
The original reports and failed jobs remain evidence for that Main revision.
The amendment requires a new exact-head PR result and subsequent Main analysis.
