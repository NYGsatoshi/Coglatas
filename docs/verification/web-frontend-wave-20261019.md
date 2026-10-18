# Web frontend P0/P1 wave, 2026-10-19

Status at assembly: **REVIEW_READY**, subject to exact-head hosted checks recorded in the integration PR. This is a bounded local
frontend implementation wave, not completion of held server features or a release.

## Source and repository state

Product Main at discovery: `9b44d631df88cdc2f15a45b6278b3e1b2552a8df`.
Spec Main: `6a0491c925b4980ce737dfbdf775816c237e44fe`.
Both match the reported starting pins. The original checkout remains on its
unrelated branch with its original IDE deletion and untracked attachments.
All six workstreams used separate branches and worktrees; three subagents ran
concurrently because the session supports four active agents including Coordinator.

Product #1234/#1243/#1245/#1246/#1247 are merged. #1248 remains closed and
unmerged; this wave did not reuse or reopen it. Spec #282 through #290 remain
open Drafts. No approval receipt was created or altered.

Product Main protection comes from ruleset 24643016. It requires an up-to-date
branch, resolved review threads, and `build-test`, `frontend-test`,
`security-scan`, `publication-readiness`, `functional-fast`, `performance-fast`.
Its current required approving-review count is zero; independent review is still
required by this wave. CODEOWNERS remains `@NYGsatoshi`. No bypass, protection, inspection threshold, dependency, lockfile, migration
or deployment change was made. One exact historical false-positive scanner
fingerprint is independently reviewed under the existing repository policy.
The final policy guard passes, and pinned Gitleaks 8.24.3 reports zero findings
across the complete Main-to-candidate range. An isolated new-origin control
still produces exactly one detected finding with exit 1; the exception does
not suppress new content. The historical failed jobs remain failed.

## Workstreams and implemented boundaries

| Agent | Review artifact | Result and scope |
| --- | --- | --- |
| A | [Product #1252](https://github.com/NYGsatoshi/Coglatas/pull/1252) | Separate DM/Teams routes and navigation, existing inbox paging, explicit current-page directory scope, recipient cancellation/focus, normalized authorization-denial cleanup. Existing dark-theme tokens retained. |
| B | [Product #1251](https://github.com/NYGsatoshi/Coglatas/pull/1251) | NgRx local Draft workbench, tab-memory checkpoints, retry/restore/discard, immediate identity/scope redaction and stale-response rejection. Opt-in synthetic Links search/add/remove only. |
| C | [Product #1250](https://github.com/NYGsatoshi/Coglatas/pull/1250) | Local opaque-ID Starred preference, explicit unsynced label, bounded fresh existing File GETs, denial/offline recovery, mobile shortcuts and fresh Recent/picker reauthorization. |
| D | [Product #1254](https://github.com/NYGsatoshi/Coglatas/pull/1254), targeting the integration branch | Immutable source reviews, mock browser/axe/keyboard/IME/Unicode/held-response regressions, diagnostic screenshots and retained failures. |
| E | [Product #1253](https://github.com/NYGsatoshi/Coglatas/pull/1253) | Actual-controller direct/grant download revocation regressions, safe responses/storage/audit/log controls, and a narrow Unicode scalar-boundary filename fix. |
| F | Private [Spec #291](https://github.com/NYGsatoshi/Coglatas-Spec/pull/291) | Additive source/digest/approval-boundary audit and one unsigned Owner queue. Original subjects and later evidence digests remain distinct; no private packet content is copied here. |

Coordinator also repaired the existing AG fallback action renderer after an
independent browser failure: Enter/Space now activate one enabled action without
grid interception, repeated activation or native double dispatch. Composition,
legacy key code 229, disabled actions and ordinary Tab behavior are covered.

The actual stack remains Angular 22.2.2, NgRx 22.0.1, SignalR client 10.0.11,
RxJS 7.8.2, Vitest 5.0.3, Playwright 1.64.0 and .NET SDK 10.0.401. Installed
libraries were reused. Existing signal facades were not migrated wholesale.
No live Penpot connection or newly supplied home-screen reference was available;
matching an unseen reference is not claimed.

## Verification and retained failures

Raw local evidence is retained outside Git under
`$env:LOCALAPPDATA/Temp/coglatas-webwave-20261019/`. Feature records provide
commands, immutable code pins and exact scope. Overlapping runs are not summed.

| Selection | Executed outcome |
| --- | --- |
| Main baseline HTTP/cursor selection | 123 PASS: 122 Kestrel/EF InMemory HTTP cases plus one polling-cursor unit case; zero FAIL/SKIP |
| Main baseline runner controls | 22 PASS, zero FAIL/SKIP |
| Main baseline frontend selection | 322 PASS / 3 FAIL / 0 SKIP: two cold 30-second hooks and one 5-second test timeout; #1242 remains open |
| Main narrower follow-up | 83 PASS / 0 FAIL / 0 SKIP; does not erase the first baseline failures |
| Main Thread mock browser matrix | 12 PASS / 0 FAIL / 0 SKIP; Windows Chromium, not Linux baseline approval |
| First combined full Angular suite | 1,384 PASS / 0 FAIL / 0 SKIP; 136 files, before the final picker/grid repairs |
| Full Angular suite after picker/grid repair | 1,387 PASS / 0 FAIL / 0 SKIP; 136 files at frontend tree `29abf4f37fa1255dfa719979b4c47c888dd3af02`, before the final focus/spacing follow-up |
| Final Files repair selection | 148 PASS / 0 FAIL / 0 SKIP |
| Final scoped preview-focus component selection | 43 PASS / 0 FAIL / 0 SKIP; the shared row-identity follow-up separately passes 4 adapter tests |
| Grid action regression | Before: 2 PASS / 2 FAIL. After: 4 PASS / 0 FAIL / 0 SKIP, including IME/repeat/disabled guards |
| Final D mock browser matrix | 20 PASS / 0 FAIL / 0 SKIP / 0 retries; frontend tree `12bc68fbce2707c654faa20a7fb09781a355594e`, Windows Chromium desktop/mobile, light/dark, 320/1366 widths |
| Combined pinned Linux visual selection | 1 PASS / 0 FAIL / 0 SKIP; the generated mobile baseline was independently inspected before adoption |
| Final combined keyboard traversal | 2 PASS / 0 FAIL / 0 SKIP; all six focus assertions preserved after deriving one actual focus-cycle bound |
| E final runtime selection | 165 PASS / 0 FAIL / 0 SKIP: 124 HTTP plus 41 unit/fake-service cases; EF InMemory is not PostgreSQL |
| Unicode regression before E repair | 12 PASS / 1 FAIL / 0 SKIP; failure retained at its original commit |
| F independent integrity controls | 122 PASS / 0 FAIL / 0 SKIP; Coordinator separately executed the final verifier and negative controls |
| F additive source review | Final verifier and 13 negative controls PASS independently; original private evidence, manifests and unsigned decisions retained |
| Local architecture helper suite | 35 PASS / 1 FAIL / 0 SKIP; Windows symlink creation returned EPERM. No test was skipped or weakened |
| Current-Web inventory follow-up | 58 PASS / 1 FAIL / 0 SKIP of 59, independently reproduced; only the unchanged Windows symlink fixture failed with EPERM. Source verifier and architecture checker PASS; hosted Linux verification remains required |

Later hosted architecture checks exposed a separate implementation omission:
the historical inventory guard compared its immutable 38-route snapshot with
today's route AST, which now includes `/dm`, `/teams` and `/drafts`. A source
`e71650c6` produced 1,323 Angular PASS and 191 Linux browser PASS / 13 existing
conditional SKIP, but architecture 34 PASS / 2 FAIL in run `39143086624`,
attempt 1, job `117722081022`. B source `88bfb3ca` produced 1,345 Angular PASS,
architecture 34 PASS / 2 FAIL and browser 186 PASS / 1 FAIL / 13 conditional
SKIP in run `39142527733`, attempt 1, job `117721613374`; its browser failure
is the older mobile navigation PNG, repaired by the combined baseline above.
These jobs remain failed. The current-Web supplement is reviewed separately
from frozen migration target mappings; no historical snapshot or Owner
approval is rewritten. Its final guard results are recorded in the PR ledger.
The repair is [Product #1256](https://github.com/NYGsatoshi/Coglatas/pull/1256),
source `afa00db7587923028e9bbafafb6108346aaabbb8`. Its separate
[manifest and evidence](current-web-wave-inventory-20261019.md) record the exact
component, owner, local-state and NgRx checks, preserved historical blobs and
new negative controls. Independent policy checks additionally passed twelve
pinned-inventory cases and seven mutation controls. No target PNL, migration
owner, approval field or cutover disposition was added.

The browser and Linux-image evidence binds compiled UI source tree
`e6c28e8a705a08482f7d2155ee8066df2471c391` under `frontend/src`. Guard-only
script changes can alter the complete frontend tree without altering those
UI bytes or build inputs; the final CI still runs against its own latest SHA.
The complete frontend tree after guard maintenance is
`f1dd6bbc090f4f5ea34e3dc1c1f56ce95205bdb8`; its change is limited to guard
scripts and tests. Do not attribute the earlier whole-tree browser pin to this
later tree without that explicit unchanged compiled-source boundary.

The original D axe list-semantics failure, hidden mobile Starred entry point,
whole-page stale picker filename and desktop Enter failure remain separately
recorded. Product fixes and corrected test-only selector assumptions are
distinguished in D's report. No test, accessibility rule or timeout was disabled.
The final twenty-case browser matrix passed, including whole-page denied-name
removal, held Recent-response cache controls, preview focus after grid rerender,
IME composition, navigation memory, explicit reload loss, and absence of Draft,
Links or server Starred HTTP requests. The bottom notice clears the existing
fixed theme control in the final narrow visual inspection.

Baseline initial production output was 927.20 kB. Intermediate combined output
was 929.74 kB, a roughly 0.274% raw-size increase, without a budget increase.
Final build measurements belong to D's exact-source record. Existing warnings
remain visible. Low-spec hardware latency, memory and scrolling qualification
were not executed; bundle measurements alone do not establish those guarantees.

## Hosted evidence and merge boundary

Discovery confirmed Product Main CI run `39103294285`, attempt 1, succeeded at
the starting Product SHA. Its job ledger contains two conditional skipped
browser jobs. This is prior Main evidence, not proof of a new candidate.
The historical private Spec 42 runs / 58 successful jobs and ten limited
PostgreSQL provider results were independently reinspected, not rerun or
promoted into Product runtime assurance.

Current hosted Product evidence was also audited on E head `52eebf80`, tested
merge `d90007aa7c0981838150b73447a5334d9d5aee52`, attempt 1. Run `39142132084`
job `117718541022` passed 20 cases, of which exactly two use PostgreSQL 18.6;
18 are unit/InMemory/redaction cases. Run `39142132071` passed nine synthetic
scenarios each for Small (120 tasks, 80 files, 400 messages) and Medium (3,000
tasks, 3,000 files, 20,000 messages), with 28 structural decisions. Exactly one
Medium Task-ID equality probe recorded an Index Scan after `ANALYZE`, using
plain `EXPLAIN (FORMAT JSON)`, not `EXPLAIN ANALYZE`. No broad query-plan or
performance qualification follows: the assurance receipt is still
`SUSPENDED` / `NOT_EVALUATED`, with no numerical acceptance credit. Run
`39142132063` passed two isolated startup/fixture/teardown cycles. Detailed
source/artifact binding is in Product #1253 review `5563845177`. These are
bounded E-head results, not combined-candidate or complete SEC-ARCH evidence.

Retained new-candidate quality failures include C SonarCloud check
`117713015649` (schema key sorting), A check `117713818392` (locale-key
duplication), and E ReSharper job `117716572275` in run `39141268947`, attempt 1
(two redundant qualifiers). Repairs do not relabel these original checks as
passed. A hosted frontend selection at A source `461d82c47d4f743fc73797b80a19fc5b143894c1`
(run `39140992654`, attempt 1) retained 188 PASS / 3 FAIL / 13 conditional SKIP.
Two failures exposed the old fixed twelve-tab test bound after navigation grew;
the repair derives one actual visible enabled focus cycle and retains all six
focus assertions. The third failure is the intended Linux navigation baseline
change. The repaired keyboard selection passes both Chromium projects locally.
D hosted Gitleaks jobs `117721079698` and `117720537647` retain their failed
outcomes; their sole finding is the public Git commit SHA beside the phrase
describing keyboard activation. History remains immutable and the exact
false-positive fingerprint is handled through existing policy.
Agent D's follow-up is `9d4d7a5b550eec59d18f7351ac771d37c4e4a87a`.
Coordinator independently repeated the complete historical scan and new-origin
control. No scanner rule, workflow, threshold or scan range was changed.

Final exact-head run/attempt/SHA/check outcomes are posted in each PR
and the integration PR. Existing Main dependency alerts remain separate #804
work; this wave changes no dependency manifest or lockfile.

The combined mobile PNG comes from hosted Linux run `39142869092`, attempt 1,
on the isolated evidence branch `72017fe7d4bf7fe8bbfe0a97e6283658a7b9460a`.
It checked out candidate `a269944331863da1a22925eea702c83cbd34bd17`, with the same
frontend tree as the final D browser matrix and the integrated source.
Artifact `12141409405` has ZIP SHA-256
`f94a835621616a909362d54d854240f78c439c4c5cea99501cee4258f52e10b7`.
The adopted 390 by 844 PNG has SHA-256
`41f08ec97dee961beb0ba6f0e2a497b21a097b93d36f4ae8bc68adc34210c797`.
Coordinator and independent quality review inspected the menu change: DM,
Teams and Local drafts shift following menu items; other screenshot baselines
are unchanged. This uses the existing Playwright 1.64.0 Noble Docker runner.
Independent quality review is recorded on Product #1255 as review
`5563807750`, anchored to the reviewed candidate before the scanner follow-up.
The read-only capture workflow exists only on its separate evidence branch,
uses no secrets and performs no publication or repository writeback. It is not
part of the Product integration patch. Windows images are diagnostic only.

Merge requires the actual latest required checks, independent review and current
ruleset policy. No merge, exact-Main qualification, deployment or public HTTPS
pass follows from this document alone. Local Docker's Linux daemon is absent.
No unknown database, remote server, production data or public endpoint was used.

## Owner decisions and next wave

| Decision | Subject | Recommended bounded next step | Work continuing without it |
| --- | --- | --- | --- |
| WEB-DRAFT-R2 | Spec #287 | Review the exact private composed contract; select A/revision/defer explicitly | Local memory-only editor and recovery tests |
| WEB-LINKS-IMPLEMENTATION | Spec #288 | Review the exact private implementation direction separately from activation | Synthetic opt-in flow and unavailable adapter |
| WEB-SHARED-DISCLOSURE | Spec #284 | Review the exact original inert disclosure subject separately from later evidence | Existing Files APIs and local preferences |
| WEB-FILTERS-DISCLOSURE | Spec #286 | Same separate exact-subject disclosure review | Existing approved search/filter controls |
| WEB-ARTIFACTS-DISCLOSURE | Spec #285 | Same separate exact-subject disclosure review | Existing per-Project Artifact routes |
| SEC-ARCH-REMAINING | Product #1144–#1158 and private authority records | Retain HOLD; resolve the exact outstanding Owner/mapping/runtime gates | Isolated, bounded tests and source review |

The complete private queue preserves options, reasons, security, compatibility,
DB impact and continuable work. No decision is selected here. All proposed
reviews leave existing Product behavior compatible and grant no DB change.

Next priorities: finish exact-head hosted checks;
continue #1242 cold-fixture reproducibility; obtain approved
Draft/Links and Files disclosure decisions; then implement only newly approved
adapters. #481/#482 retain isolated/public-environment and terminal regression
responsibilities. SQL plans, operational cursor-key continuity, roles/GRANT,
issuer, RLS/mapping/custody/migration activation and public exposure remain
unqualified or held. **PRE-AVALONIA SEC-ARCH remains BLOCKED.**
