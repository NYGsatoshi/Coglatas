# Student Council Web release readiness — 2026-10-23

Current Status: **IMPLEMENTING / VALIDATING; release NO_GO**.
Snapshot: 2026-10-23, 10:03 UTC (19:03 JST). Later candidates require their own
source-bound updates; this report does not predict their outcomes.
This report is an evidence inventory, not a declaration of RELEASE_READY.
Its mission-local identifiers below do not introduce normative SPEC IDs.
No school accounts, real data, public ingress, or production environment were used.

## Source and authority snapshot

- Product Main: `babcf80f7babafe4087fb7cb5742edb2b98868c2`, verified from GitHub.
- Private Spec Main: `6a0491c925b4980ce737dfbdf775816c237e44fe`; accessible, contents stay private.
- Original local checkout: `b95ed8ec918d3bbeb9ac36f95034e23a2e0959fd`.
  Its unrelated IDE deletion and untracked attachments are preserved.
- Isolated coordinator/security/operations worktrees start at verified Product Main.
- Scope: Angular Web, Workspace, channels/Threads, Direct/Group chat, Files,
  security and operations. Avalonia, ProjectIDE and Gantt do not block this pilot.
- Accepted Direct lifecycle, Group G1–G3 v2, Workspace Option 2 and DR-A-88–93
  architecture directions stay accepted. They do not approve detailed RLS policies,
  initial formal mapping, production activation or real-user opening.

## PR and Main evidence

| PR/source | Current identity and disposition | Evidence and limit |
| --- | --- | --- |
| #1325 SEC-16 | Merged as Main `babcf80`; source head `256d1447ac77d1e908dff55506737ad897247bf1` | Bounded grant HTTP positives/negatives; full #689 acceptance is not established by closure |
| #1320 Pilot-01 | Open, head `3327194ac0429b0ae17a78ec83e94cf9599a1006`; historical explicit no-merge hold retained | PR CI 39924748318 and P0 preflight 39924748284 succeed. Protected current-head P0 [39933087203](https://github.com/NYGsatoshi/Coglatas/actions/runs/39933087203) completed SUCCESS; independent logs confirm checkout/TARGET_SHA, eight distinct required cases and 8/8 JUnit verifier totals, with no selected failure/skip |
| #1326 SEC-17 | Open, head `f47e4fb044dd2449016066bf08240438104242eb` | [PR CI 39924730456](https://github.com/NYGsatoshi/Coglatas/actions/runs/39924730456) succeeds on test merge `0d5ead3cd9286af1849bab8830d9d62ef327d0fc`; 438 backend cases and named private-channel test pass. Not resulting-Main evidence |
| #1327 CI diagnostics | Review-ready after independent review; head `63357fa5cbb9492f721dcd8837202f0176bb18e1` | Nine local controls, live pinned PostgreSQL positive/unsafe-default negative, YAML, publication and 54 topology controls pass; PR CI 39932755002 has a failed Functional collaboration/aggregate lane under investigation; other lanes still running |
| #1329 SEC-16 HTTP lifecycle | Review-ready, latest head `9fdf5194009ce31b7dd9e78c7527ca8754e4936e` | Prior `727513e9` has independent source/TRX review, exact-source Release build and four real HTTP/PostgreSQL facts PASS/zero skips. Its PR CI found one ReSharper multiple-enumeration warning, corrected at `9fdf5194` with unchanged assertions; fresh qualification pending. Separate structural performance 39934416085 failed before Small DB measurements; Medium passed nine scenarios |
| Current Main | [Main CI 39924684501](https://github.com/NYGsatoshi/Coglatas/actions/runs/39924684501) succeeds | Backend 2,591/2,591; architecture 12/12; both TRX verifiers; all fourteen broader SignalR/Event methods pass. Licensed, Fast/Full/Extended Functional and Qodana succeed within their executed scope |

GitHub returned all eight pre-existing open PRs in one page: #1326, #1320,
#1317, #1315, #1271, #1270, #1269 and #1249. This task added #1327.
This task also added #1329 for the selected HTTP lifecycle residuals.
Held #1269/#1270/#1271 remain independent review-only work; dependency PRs
are not automatically authorized upgrades. #1249 remains an unresolved inspection
candidate. The two most recent preceding merges are #1323 (`fccf6f9f`,
dependency updates) and #1324 (`109c35bb`, Task/DM regression repairs);
their integration is exercised by current Main, with target qualification separate.

Main has 67 check rows: 63 SUCCESS and four configured SKIPPED at the snapshot.
The skips are separate browser/mobile-engine routes and suspended API numerical
performance routes. They do not supply the missing mobile or numerical evidence.
The successful `frontend-static-analysis` marker is a documented no-op and does
not mean ESLint/Stylelint ran.

The active ruleset 24643016 has no bypass actors, requires up-to-date
`build-test`, `frontend-test`, `security-scan`, `publication-readiness`,
`functional-fast`, `performance-fast`, resolved review threads, and a PR.
Required approving-review count is zero; CODEOWNERS still identifies the Owner.
No checks, reviews, rulesets or protection were weakened.

## Release blocking matrix and gate inventory

Each row gives implementation, required evidence and the current blocking reason.
PASS qualifies only the named bounded gate, never a complete feature by implication.

| Requirement ID / severity | Owner | Current implementation / required evidence | Blocking reason / dependency | PR / CI | Status |
| --- | --- | --- | --- | --- | --- |
| REL-CI / P0 | Coordinator | Current Main required checks succeed at the recorded SHA | New integrations need their own resulting-Main run | Main 39924684501 | PASS |
| WS-CORE / P0 | Functional | Canonical Workspace create/select, management and role controls exist; current-head protected Pilot-01 eight-case evidence reconciled | Resulting-Main and target lifecycle evidence pending; historical merge hold | #1320 / 39933087203 | IN_PROGRESS |
| CHAT-PRIVATE / P0 | Functional | Public/private channel, Message and Thread source/provider/browser controls exist; current-head protected Pilot-01 eight-case evidence reconciled | Resulting-Main and target admission/invite/revoke evidence; historical merge hold | #1320 | IN_PROGRESS |
| DM-GROUP / P0 | Functional | Existing Direct/Group creation, persistence, live receive, read/unread, removal/re-entry/leave helpers | Complete target negative/cache/reconnect matrix remains missing | Main licensed owner | IMPLEMENTED_NOT_VERIFIED |
| FILE-READ / P0 | Security/Files | Authorized metadata/preview/download and opaque POST-body grants exist; four expiry/revocation HTTP facts pass locally | Complete applicable lifecycle matrix, hosted candidate and target proof | #1325 / #1328 / #1329 | IN_PROGRESS |
| FILE-INTAKE / P0 | Owner/Security | Current pilot explicitly enforces FileSharing=false; arbitrary unscanned intake stays disabled | Mission requires upload; approved safe intake/scan policy and scope reconciliation required | Existing pilot scope / #604 | OWNER_DECISION_REQUIRED |
| SEC16-FULL / P0 | Security | Reuse FCI-07, unit grant boundaries and persisted Application lifecycle controls | #689 was closed on a bounded subset; lifecycle HTTP/lane/#582 evidence remains | #1328 | IN_PROGRESS |
| SEC17-FULL / P0 | Security | Fourteen broad Main controls plus named-private PR journey | Complete #690 criteria and final event/coverage reconciliation remain | #1326 / #690 / #1150 | IMPLEMENTED_NOT_VERIFIED |
| SEC-MAP / P0 | Owner | Private formal mapping tooling/worksheet exists | Exact immutable personal approval receipt not supplied | #1145 / #1146 / #1152 | OWNER_DECISION_REQUIRED |
| SEC-RLS / P0 | Owner/Security | Product RLS is not activated; representative isolated controls exist | Approved concrete table/operation/role/policy/runtime/migration design, actual full matrix and separate activation | #1148 / #1156–#1158 | OWNER_DECISION_REQUIRED |
| SEC-API-SESSION / P0 | Security | Cookie/CSRF/revocation, current-authority API and SignalR tests execute on Main | Complete approved mapping and target parity remain | #1147 / #1150 / #614 | IMPLEMENTED_NOT_VERIFIED |
| SEC-EVIDENCE / P0 | Security/CI | Two backend CI service diagnostic fixes implemented with live canary guard | Hosted Functional collaboration failed; root-cause correction and resulting-Main audit pending; other logging surfaces remain separate | #1327 | IN_PROGRESS |
| SEC-FINAL / P0 | Security/Auditor | Existing final/advisory contracts distinguish runtime and provenance | Required advanced scanner/normalization/exact image assurance remains open | #582 / #614 / #688 / #695 / #1152 | BLOCKED |
| QUALITY-FINDINGS / P0 | Auditor | Complete open Code Scanning metadata inventory obtained | Mandatory disposition gate remains unresolved; this does not classify every Qodana note as P0 | #862 / #935 / #1306 | BLOCKED |
| UX-DESIGN / P0 | Frontend/Owner | Retained design evidence references Penpot Rev442; Rev339 is historical | Live Penpot tab suspended; current approval is unverified | Existing Penpot traceability | BLOCKED |
| UX-DEVICE / P0 | Frontend | IME source guards and responsive/browser tests exist | Current mobile/tablet, physical IME, keyboard and permission/error/reconnect evidence incomplete | Main browser routes partly skipped | IMPLEMENTED_NOT_VERIFIED |
| OPS-TARGET / P0 | Owner/Operator | Existing provider-neutral and Sakura tooling exists | Approved SSH alias, stable HTTPS origin and current environment authority references missing | #467 / #481 | OWNER_DECISION_REQUIRED |
| OPS-CONFIG / P0 | Operations | Onprem, CI migration override and restore Compose render with synthetic values | Config parsing is not clean startup; actual selected topology unknown | Current deploy tooling | IMPLEMENTED_NOT_VERIFIED |
| OPS-HTTPS / P0 | Operations | Protected #481 and separate pilot seventeen-flow runners exist | Target TLS/redirect/HSTS/cookie/CSRF/WebSocket execution not performed | #481 | BLOCKED |
| OPS-PERSIST / P0 | Operations | DB/files/Data Protection volume paths exist in configuration | Same-SHA restart, host reboot, key/session/file continuity, restart policy must qualify on selected target | Existing runbooks | IMPLEMENTED_NOT_VERIFIED |
| OPS-RECOVERY / P0 | Operations | Historical partial backup/restore mechanics retained | Strict raw schema restore failed; full key unprotect/login recovery and rollback unqualified | Existing recovery record | BLOCKED |
| OPS-OBSERVE / P0 | Operations | Health/logging/operating instructions exist | Actual target monitoring/alerts, retention, named operator and rehearsed incident process missing | Existing operations docs | IMPLEMENTED_NOT_VERIFIED |
| PERF-STRUCTURAL / P0 | Operations | Main DB structural checks execute | Keep numerical latency/throughput SUSPENDED / NOT_EVALUATED under #1128; never relabel PASS | Main DB structural / #1128 | PASS |
| SCOPE-DESKTOP / — | Coordinator | Avalonia/advanced ProjectIDE/Gantt excluded from this Web pilot | No authorization to activate conditional infrastructure | Mission scope | NOT_APPLICABLE |

## Requirement-to-test traceability: mission P0 acceptance

| Mission step | Existing test owner / source | Current proof and remaining gap |
| --- | --- | --- |
| 1–2 Synthetic Workspace and two independent sessions | Mandatory Workspace P0 / Pilot-01 | Current-head protected eight-case workflow and JUnit verifier succeed; no school users; target remains separate |
| 3 Public and private channels | Workspace channel provider/transport controls; Pilot-01 private path | Public/current target create/select controls must be explicitly reconciled |
| 4 Persist private Message and Thread | Pilot-01; WorkspaceChannelMessaging/TransportPostgreSqlTests | Real existing Main controls; current-head protected eight-case evidence reconciled; target remains separate |
| 5 Deny before invitation/admission | Pilot-01 bounded 4xx/body checks | Current-head protected eight-case evidence reconciled; 500 is never successful denial |
| 6 Workspace admission alone does not grant private-channel read | Pilot-01 and named-private SignalR test | #1326 test-merge runtime passes; current browser run pending |
| 7 Current private-channel invitation enables read | Real channel-member HTTP/CSRF picker and SignalR positive | #1326 test-merge passes; current browser/target still separate |
| 8–9 ReadOnly downgrade/restore | Pilot-01 HTTP post/read controls | Existing assertions preserve current role semantics; protected run pending |
| 10 Revoke Message/Thread/Files/Grant/SignalR | Pilot-01 + FCI-07 SEC-16 + #1326 + broad Main SignalR | These are separate bounded owners; combine exact-candidate evidence without implying full race/target coverage |
| 11 DM/Group persistence and authorization | Direct/Group hosted HTTP/provider/transport and licensed helpers | Existing Main runtime controls; complete target/cache/lifecycle traceability still missing |
| 12 SignalR failure and HTTP catch-up | Fourteen broad SignalR/Event tests; channel/group transport controls | Main methods pass; selected positives do not prove every delayed/reconnected UI state |
| 13 HTTPS Cookie/CSRF/realtime | #481 and separate pilot HTTPS runner | NOT EXECUTED on approved target; local HTTP cannot substitute |
| 14 Restart/recover same SHA | Deployment, persistence and recovery runbooks | NOT QUALIFIED; immutable source/image, full restore/login and rollback receipts required |

The seventeen-flow pilot runner deliberately returns `PILOT_PARTIAL` /
`fullPilotGo:false`. Its residual list includes unread/mention, expired-cookie,
delayed-response, reconnect/stale-UI, prohibited File mutations, target provenance,
backup/restore, physical IME and school/operator conditions. A future success
from that runner alone cannot satisfy the full mission.

Current-head Pilot-01 qualification comes from successful job logs and the
required-case verifier. Successful raw JUnit/trace files were not separately
published/downloaded; sanitized FCI-04 completion is the success artifact.
Separate FCI-04 and FCI-05 each pass one owner and are not counted among the eight.

The new #1327 failure remains failed: Functional collaboration recorded one
passing notification owner and one failing message owner, with no retry/skip.
The latter expected HTTP 400 after restricted login but observed 200. Sanitized
request ordering shows overlapping reads and a broad response waiter; an older
authorized response being selected is plausible, not proven. A repair must verify
current restricted identity and bind its assertion to a newly emitted request,
retaining every denial/redaction/UI-clearing assertion. Fresh unauthorized 200
must still fail. No product authorization weakness is dismissed by this hypothesis.

The separate #1329 Small performance failure occurs during tooling fingerprinting:
the first browser `node --version` command timed out after 60 seconds, before DB
measurements. Browser image acquisition is implicit inside that command; a cold
pull is plausible but unproven because partial child output was not retained.
Explicit preparation and no-pull probes can be reviewed independently without
increasing that deadline, changing DB thresholds or resuming numerical assurance.

## Security architecture and finding disposition

Private SEC-ARCH initial mapping and concrete policy/activation receipts must be
reviewed in their private location. The existing
[preflight exit contract](security-architecture-preflight.md) and #1144–#1152
remain authoritative. No private mapping, row content or unpublished topology
is reproduced here. Previously accepted architecture choices are not reopened.

Code Scanning pagination returned 1,285 open records: Qodana 205 warning and
1,010 note; SonarCloud 37 Low, 23 Medium and ten High. These are scanner
classifications, not 1,285 proven exploitable vulnerabilities. A static review of
the ten High hotspots found eight supported-context claims not actionable and
two image-context claims needing further review. All source alerts remain open;
the remaining sixty Sonar records and Qodana inventory are not fully triaged.
The High instances refer to historical `995a4d6e`; current Main was inspected
separately. Current scanner success does not dispose of earlier findings.
Detailed static triage is retained privately in the local security artifact collection.

The authenticated Dependabot inventory separately returned three open High
`braces` alerts (#121–#123), in the active frontend, inactive frontend and
inspection-tool lockfiles. The API provides no first patched version. #1306
remains the existing upstream/update/exposure blocker. Development-scope metadata
alone does not establish safe runtime/build exposure; no override, forced fork,
alert dismissal or security-gate relaxation was applied.

#1328 preserves the full #689 acceptance gap without rewriting its closed history.
#690 remains open. Missing normalization, coverage, expired exceptions and
unreviewed mandatory security records remain blockers, not PASS.

## Operations, staging and runbook handoff

Current target access is missing: no SSH alias/host/user or approved stable HTTPS
origin was supplied. Closed #467 defines a provider-neutral external TLS contract;
closed #481 has a later owner record `BLOCKED_ENV_NOT_CONFIGURED`. Neither closure
proves target acceptance. Do not select a historical provider by inference.

Operations locally passed thirteen Node pilot helper controls, seven Python
intake controls and three Compose config validations with synthetic non-build
values. No target startup/migration/deployment follows from them.

Retain the prior strict raw-schema restore failure even though selected table/row,
sample-object and historical c7 stored-object controls passed. A fresh isolated
component diagnostic also failed before schema capture: socket-only readiness
admitted PostgreSQL's temporary initialization server before the fixture database
existed. Its stopped owned fixture and failure receipt are retained; no recovery
credit is claimed. A fresh v2 fixture then verified final TCP-server/database
readiness and passed a component diagnostic: logical schema bytes still differ,
physical-copy schema bytes and row manifests match, all three synthetic databases
enforce the positive/negative constraints, and the retained container stopped
with exit zero. The v2 driver/harness digests and both failure/success receipts
are retained locally. This does not qualify full application restore or replace
the approved backup strategy. The recorded cast
reformatting explanation is a diagnostic classification, not proof of complete
restore equivalence or permission to relax the oracle. Full application key
decryption/login, file/DB continuity, restart and rollback remain unqualified.

Use the existing [operations guide](../OPERATIONS.md),
[deployment guide](../DEPLOYMENT.md), pilot scope and selected deployment README.
For the approved target, record in order:

1. Non-mutating inventory, authorization, restriction and immutable artifact check.
2. Secret references, persisted DB/files/keys, trusted proxy/AllowedHosts/TLS and
   server-side upload policy validation without exposing secret values.
3. Clean synthetic migration/startup and explicit health/WebSocket/cookie/CSRF checks.
4. All applicable positive/negative browser journeys and source/image/test identity.
5. Backup to a protected destination; restore to a separate owned isolated stack;
   compare required schema/data/files and demonstrate key unprotect plus login.
6. Same-artifact restart/recovery, previous-artifact rollback with compatible schema,
   retained data verification, monitoring/alert receipt and operator handover.

No destructive rollback/migration or target access is implied by this checklist.

## Owner decision register

No selection alone is an approval receipt. Supplied records must be checked for
exact source, artifact, environment, scope and authority before dependent actions.

| Decision ID | A / B / C | Recommendation and impact | Default / dependencies |
| --- | --- | --- | --- |
| STAGE-01 | A: supply existing approved restricted synthetic target references; B: explicitly approve a new environment/cost; C: retain HOLD | A avoids inferring topology or spending funds; target execution effort depends on observed state | HOLD; #467/#481, all target/recovery rows |
| SEC-OWNER-01 | A: supply existing immutable detailed approval receipt; B: complete the outstanding private worksheet review; C: retain HOLD | A if already approved; otherwise B with exact mapping/policy review. No architecture approval is inferred from broad directions | HOLD; #1145/#1148/#1152/#1156–#1158 |
| INTAKE-01 | A: identify approved safe upload/scan policy and qualify it; B: explicitly resolve the mission's upload requirement against a reduced pilot scope; C: retain HOLD | A meets the stated full MVP without silently enabling unsafe intake; B changes release scope and cannot be adopted by engineering alone | HOLD, upload remains disabled; File intake and security gates |
| EXISTING-PR-01 | A: identify later explicit merge delegation for held #1320/#1326; B: retain review-only state; C: withdraw those candidates with a replacement plan | Verify recorded delegation before merge; green CI does not expand earlier holds | HOLD; #1320/#1326 |
| RELEASE-GO | A: GO after all mandatory gates; B: NO_GO; C: defer | No GO request yet: mandatory gates remain unresolved | NO_GO; all rows |

The Owner explicitly answered **Hold Both** for target-environment references
and pending detailed security approval references. Both dependent tracks remain
on HOLD; independent test, CI and evidence work continues. No approval is inferred
and the held decisions will not be requested again unless the Owner resumes them.
No passwords, tokens, private-key material or real school identities are needed
in chat or public evidence.

## Autonomous next actions

1. Complete independent current-head protected P0 and #1327 CI/review; fix actual
   failures without weakening tests, then merge only within verified delegation.
2. Complete hosted qualification and integration of the four real HTTP
   expiry/revoked-grant facts in #1329 under #1328, preserving their positives.
3. Reconcile complete SEC-16/17 and final-security traceability with existing tests;
   preserve remaining owner/policy/activation holds.
4. Refresh approved Penpot evidence when its suspended connection is restored;
   complete current mobile/keyboard/IME/error/reconnect evidence.
5. Keep target and detailed-security-approval work on the Owner's explicit HOLD;
   continue independent synthetic verification and evidence diagnosis.

Completion artifacts are collected here and in linked feature/security/operations
records. Current successful target qualification receipts for staging, full
restore and rollback are absent pending execution; historical partial and failed
restore records remain retained.

