# Student Council pilot release gate

Current overall verdict: **NO_GO**. Qualification is in progress.
This record defines the separate Student Council Pilot Golden Path; it neither
changes nor satisfies the broader #481 product release contract.

## Decision vocabulary

- `GO`: all applicable UI, API, PostgreSQL, security, E2E and operational gates passed for the exact deployed revision.
- `GO_READ_ONLY`: qualified reads/downloads only, with prohibited writes denied by the server.
- `INTERNAL_SYNTHETIC_ONLY`: isolated synthetic evaluation; no school accounts or real-user data.
- `NO_GO`: missing or failed required evidence, unresolved authorization contract, or unsafe shared foundations.

## Feature acceptance

| Feature | UI | API | PostgreSQL | Security | Real-backend E2E | Verdict |
| --- | --- | --- | --- | --- | --- | --- |
| Workspace | Dark shell, selection and members implemented | Existing canonical contract | Provider controls passed; see checkpoint | Full pilot matrix incomplete | Target pending | NO_GO |
| Channel Chat | Dense timeline/composer; six-width diagnostic passed | Existing Conversation/Message source | Existing Workspace Channel controls passed | Full pilot matrix incomplete | Target pending | NO_GO |
| Thread | Existing workspace and keyboard/IME controls retained | Existing Message Thread contract | Existing provider controls passed | Full revocation matrix incomplete | Target pending | NO_GO |
| Group DM | Routing and multi-recipient UI; submission unavailable | Group runtime unsupported | Unsupported-type rejection tested | Activation contract unresolved | Not enabled | NO_GO |
| DM | Directory and composer implemented; final visual correction pending | Existing direct endpoint; admission repaired | Current admission controls passed | Private persisted-read decision blocks release | Target pending | NO_GO |
| Files | Hub/preview implemented; final layout check pending | Existing Files contract only | No pilot Files provider qualification claimed | Server intake flag and negative upload evidence pending | Target pending | NO_GO |

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
| Auth/CSRF | Secure cookies, CSRF, logout and session invalidation pending |
| PostgreSQL | Actual target health, migrations and retained volume pending |
| Storage/keys | Retained upload and Data Protection volumes pending |
| Intake policy | General-user upload denied unless complete safety gate qualified |
| Bootstrap | Existing explicit bootstrap only; disable after initial provisioning |
| Backup/restore | Complete DB + bytes + key backup and isolated restore drill pending |
| Rollback | Prior immutable artifact, schema compatibility and data preservation pending |
| Logs/secrets | No credentials, tokens or file content; owner-only external `.env`/license |
| Public pilot | Owner release approval and school/privacy/operator prerequisites pending |

A Quick Tunnel URL is temporary and cannot be reported as stable production
hosting. Compose rendering is not startup evidence. A backup file without a
successful isolated restore is not recovery qualification.

## Owner decisions

Current security qualification failed a required protected-content revocation
control. Evidence and the proposed lifecycle decision are retained in the
private managed review collection for the Owner. The admission repair does not
qualify already-persisted read access. No real-user pilot is permitted until
the lifecycle decision and all applicable provider/HTTP controls pass. Public
reports omit the private finding, reproducer and protected payload.

Owner review must approve the persisted-read lifecycle boundary before a
separate change can qualify it. The proposed read fence
must cover application reads, repository projections/recursive SQL, Message
Thread, saved/search, attachment/download, unread/notification and realtime
delivery, with positive and immediate-revocation controls. It requires no
assumed new schema, role, RLS or grant. Keeping the current lifecycle leaves
the requested pilot acceptance condition unsatisfied.

Only newly required decisions should be requested. Prepare concrete reviewable
options before asking. Current candidate: Group DM requires review of the
unsupported runtime type and its authorization/membership contract. Public
release, real-user data and production security/schema/key changes remain
explicit Owner gates. Existing approved decisions and private HOLDs are not
reopened by this work.

## Final receipt

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

Before GO, replace the provisional rows with exact evidence and record Product
Main before/after, Spec Main before/after, PR and merge identities, independent
reviews, candidate/Main CI, design comparisons, provider results, target-host
receipt, recovery drill and per-feature verdict. A critical shared authentication
or isolation failure blocks every real-user feature.
