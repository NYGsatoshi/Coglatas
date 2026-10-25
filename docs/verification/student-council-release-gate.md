# Student Council pilot release gate

## Current source-bound baseline — 2026-10-26 JST

**RELEASE_NO_GO / Owner HOLD BOTH.** Current Product Main
`0149d93d7c1e98613639ca81046b14c6ece322f5` is the normal merge of unheld
Files #1405 after exact-head PR CI 16/16 and independent review. Its
[Main CI 40214273861](https://github.com/NYGsatoshi/Coglatas/actions/runs/40214273861)
attempt 1 has a required **Fast Collaboration FAILURE** at the unchanged
authorized-root assertion before restricted-cookie replacement. Other jobs
remain in progress; the new Main is **MERGED_NOT_MAIN_VERIFIED**, and PR
success does not qualify it. The independently verified failure ZIP digest
and [issue record](https://github.com/NYGsatoshi/Coglatas/issues/1365#issuecomment-6400635806)
are retained in the supplement. Further unrelated Main merges are paused.
Previous Product Main
`f31b15f2b42d3b83b5c1ffae5ea304922e10cc16` retains completed
[Main CI 40193736229](https://github.com/NYGsatoshi/Coglatas/actions/runs/40193736229)
**FAILURE on attempt 1: 36 successful jobs, three configured skips and two
failures** across all 41 jobs. Extended Collaboration's distinct fresh Thread
GET was not observed before the original 120-second owner deadline; the other
failure is its aggregate. Fast/Full and Security success do not waive it. No
rerun, timeout increase, assertion weakening or historical causal closure follows.

The [current machine-readable twelve-gate matrix](student-council-readiness.json)
records source, all individual jobs, holds, dependencies, evidence and approval
authorities: **zero complete gates PASS**, G02/G03 FAIL on the new Main,
the previous two failed gates retained historically, and physical
acceptance NOT_EVALUATED. The
[independent implementation/review supplement](student-council-readiness-20261026.md)
records the verified failure-artifact digest, six controlled Thread cases in
Draft #1404, held Composer review, focused Files repair #1405 and bounded
SEC-17 work. PR qualification is distinct from resulting Main qualification.
All earlier checkpoints below retain their historical source/attempt attribution.

## Latest verified Main and controlled Composer RED/GREEN — 2026-10-25 JST

**Student Council RELEASE NO_GO / Owner HOLD BOTH.** The latest checked Product Main is
`4776053c56746a1fe79e0449648ab0d6d153c908`, after the strictly test/CI
diagnostic [PR #1401](https://github.com/NYGsatoshi/Coglatas/pull/1401).
Its independent [Main CI #40172129661](https://github.com/NYGsatoshi/Coglatas/actions/runs/40172129661)
completed **SUCCESS on attempt 1: 40 PASS, two configured SKIP, zero FAIL**, verified
across all 42 jobs. Real protected
[licensed E2E #121105966428](https://github.com/NYGsatoshi/Coglatas/actions/runs/40172129661/job/121105966428)
executed **8/8** browser P0 tests with mandatory JUnit verification. Fast, Full
and Extended real-backend Collaboration `FUNC-MSG-001` each passed on the first
attempt with zero browser retries and retained Thread/denial assertions. Main
Security, Qodana Cloud/Community, CodeQL and Publication Readiness succeeded.
This is source-specific product and CI qualification, **not** deployment approval
or proof that all historical intermittent failures are gone.

The preceding Main `2bed0036df645d254cc4a75882325c188d8a03cb` retains
[Main CI #40166620509 first-attempt FAILURE](https://github.com/NYGsatoshi/Coglatas/actions/runs/40166620509/attempts/1):
Docker HTTP 503 prevented the Fast authz-negative migrator from starting, so
both required Authz owners were **BLOCKED** and the dependent Fast gates failed.
Its single diagnostic job rerun executed both owners successfully on attempt 2
but did **not** retroactively pass the Main aggregate; the later Main success
does not erase either result. The bounded Docker migration/image probe in #1401
does not identify or cure the origin of the earlier 503.

**Composer P0 [#1360](https://github.com/NYGsatoshi/Coglatas/issues/1360):**
the unmodified product was tested by deliberately never-merge
[negative comparator #1402](https://github.com/NYGsatoshi/Coglatas/pull/1402)
on Main 477605. The test-only 44-line real Angular/ASP.NET Core/PostgreSQL/SignalR
reconnect/catch-up owner observed the previous textarea
`isConnected === false` while a genuine authorized GET was held:
[first-attempt PR CI #40175154052](https://github.com/NYGsatoshi/Coglatas/actions/runs/40175154052)
**FAILURE, 13 PASS / three dependent FAIL jobs**, with the unchanged other
owner's positive control PASS and no browser retry. The **identical test blob**
on the held product
[Composer PR #1361](https://github.com/NYGsatoshi/Coglatas/pull/1361),
HEAD `3de9eb782cdbc1f795f8c10ceebab4cf4b73ab10`, passed the same
real-stack case, preserving the live textarea, draft continuation and
disabled-send boundary until the fresh HTTP projection. Its independent
[PR CI #40174839257](https://github.com/NYGsatoshi/Coglatas/actions/runs/40174839257)
completed **16/16 first-attempt PASS**; 1,777 Angular unit assertions,
Angular 22 acceptance, strict TS and CodeQL passed. This is a controlled
mechanism-level RED/GREEN, **not** proof of the initiating trigger in all
historical Composer failures or a physical IME acceptance. #1361 remains
**Draft / Owner-held / unmerged** and #1360 remains **OPEN**; comparator
#1402 was closed **without merging**.

**SEC-17 [#690](https://github.com/NYGsatoshi/Coglatas/issues/690):**
held [Draft #1399](https://github.com/NYGsatoshi/Coglatas/pull/1399),
HEAD `758d722aa56fe724a498dbb47a2a2ba09a12fb2d`, passed
[PR CI #40166770523](https://github.com/NYGsatoshi/Coglatas/actions/runs/40166770523)
**16/16**, including the independently observed real PostgreSQL 18.6 private
Conversation member revocation integration test (25 seconds) and 467/467
selected backend assertions. This proves that bounded actor/revocation/socket
scenario, **not** the entire authorization event, tenant and reconnect matrix
or approved RLS. #1399 and the distinct held #1326 remain **unmerged**.

**Thread [#1365](https://github.com/NYGsatoshi/Coglatas/issues/1365):**
the held test-only [Draft #1367](https://github.com/NYGsatoshi/Coglatas/pull/1367)
was synchronized onto current Main as
`dfae273a0af7f2d55afc40e80f04dea3af51af35`, preserving its original
11-file bounded Thread chronology along with Main's Composer v3 observations.
The current-head strict Thread Functional Typecheck completed SUCCESS;
[PR CI #40193197561](https://github.com/NYGsatoshi/Coglatas/actions/runs/40193197561)
was still executing at this checkpoint and is **not prequalified**. No
demonstrated resolution of the original authorized Thread disappearance or
mobile activation [#1342](https://github.com/NYGsatoshi/Coglatas/issues/1342)
is claimed. Channel dialog reliability
[#1391](https://github.com/NYGsatoshi/Coglatas/issues/1391) and ReadOnly
roster keyboard [#1346](https://github.com/NYGsatoshi/Coglatas/issues/1346)
remain separate historical first-attempt P0s.

**Non-negotiable pilot release blockers remain:** signed private SEC-ARCH
TMT/SPEC mappings, approved SQL roles/grants and concrete all-table RLS
policies plus authorized activation; isolated and explicitly approved
staging SSH configuration and stable private HTTPS origin; strict tested
physical/logical backup and rollback with Audit/Files continuity
([#1355](https://github.com/NYGsatoshi/Coglatas/issues/1355) and held #1379);
complete SEC-16/SEC-17 denial, revoked delivery and reconnect matrix; operator,
school and privacy authorization; real devices with IME/keyboard/accessibility;
and the **17 actual externally authorized HTTPS pilot flows**. No live student
data, invitation, production/public disclosure, security exception, RLS
activation, destructive migration or Staging access is authorized by these
test outcomes. All earlier checkpoints below are historical, immutable
source-specific records, not present-tense release GO claims.

## Verified post-Channel Main and Thread guard checkpoint — 2026-10-25 JST

**RELEASE NO_GO / Owner HOLD BOTH.** Product Main `90487620417c707f3f9961a71fb7bf3b969abcc2` (normally merged [Channel refresh-click fix #1396](https://github.com/NYGsatoshi/Coglatas/pull/1396)) completed [Main CI #40153965293](https://github.com/NYGsatoshi/Coglatas/actions/runs/40153965293) **SUCCESS, attempt 1, 45 PASS / 1 policy SKIP / 0 FAIL**. Its actual protected [licensed real-backend P0 #121052122774](https://github.com/NYGsatoshi/Coglatas/actions/runs/40153965293/job/121052122774) passed **8/8** first-attempt browser cases with required JUnit presence verified. Exact-source Fast, Full and Extended Collaboration `FUNC-MSG-001` all first-attempt PASS with zero owner retries, plus CodeQL, Qodana Cloud/Community and Publication Readiness success. This is a **bounded clean Main acceptance at one SHA**, not proof that historical intermittent Composer #1360, authorized Thread #1365, Channel #1391, mobile #1342 or ReadOnly keyboard #1346 P0s were causally repaired or may be closed. Original genuine [Main #40141833874](https://github.com/NYGsatoshi/Coglatas/actions/runs/40141833874) Thread and Channel first-attempt failures remain immutable.

Latest Main `f6f895f2599760513de2af45b6ab4d63f991c27f` normally merged **only the additional Thread two-GET/Realtime catch-up regression** [#1397](https://github.com/NYGsatoshi/Coglatas/pull/1397) after its exact-head [PR CI #40159488014](https://github.com/NYGsatoshi/Coglatas/actions/runs/40159488014) completed **16/16 SUCCESS** and 1,754 Angular unit assertions passed. That regression changes no production Thread behavior. The independent resulting [Main CI #40160060841](https://github.com/NYGsatoshi/Coglatas/actions/runs/40160060841) was **IN_PROGRESS at this source-bound checkpoint** and cannot inherit earlier Main acceptance. Held diagnostic Thread #1367, Composer #1361, Pilot/SEC17 #1320/#1326, and physical recovery #1379 remain Draft/unmerged, subject to their original approval scope.

**Independent release blockers remain:** signed private SEC-ARCH mappings and exact SQL roles/grants/concrete RLS evidence; formally authorized isolated SSH/stable private HTTPS origin; strict physical/logical restore+rollback and Audit/Files continuity (#1355); complete SEC-16/17 subscribe/revoke/reconnect authorization matrix (#690); school/operator/privacy approval; physical IME/device and ReadOnly keyboard reliability; actual school-authorized external HTTPS **17-flow** pilot. No target operation, real account invitation, production/public disclosure, RLS activation, policy waiver or irreversible migration authorized. Earlier source-specific paragraphs below are historical checkpoints, not contradictory current claims.


## Post-diagnostic merge boundary — 2026-10-25 JST

**RELEASE NO_GO / Owner HOLD BOTH.** Test-only [PR #1394](https://github.com/NYGsatoshi/Coglatas/pull/1394) normally merged as Product Main `ae6ea52c9fe74f8078562f1416c91e1647095ad0`, after its exact-head [PR CI #40142116400](https://github.com/NYGsatoshi/Coglatas/actions/runs/40142116400) completed **16/16 SUCCESS**. Mocked-HTTP Chromium desktop/mobile evidence included safe observer-schema rejection and positive click/dialog tokens (415 PASS, 13 existing configured SKIP). This does **not** establish a product fix or qualify the separately initiated resulting [Main CI](https://github.com/NYGsatoshi/Coglatas/actions?query=branch%3Amain), its real-backend P0 first attempt, external HTTPS target or school-user release. The preceding exact Main `2e260516...` remains [FAILURE](https://github.com/NYGsatoshi/Coglatas/actions/runs/40141833874) from both canonical Fast Thread-root and licensed Channel-modal P0, preserved independently below. The newly instrumented Main must supply its own protected failure/positive traces before causal closure. Historical held PRs, SEC-ARCH/RLS, approved staging, strict recovery/rollback, school privacy/operator, physical IME and full external pilot remain blocking.


## Latest bounded pilot decision — 2026-10-25 JST, Main `2e260516`

**RELEASE NO_GO / Owner HOLD BOTH.** This checkpoint supersedes the current-source interpretation of earlier headings, **not** their immutable historical execution records. Product Main is `2e260516f31bfda5194194338fafd093845bb416` after normally merged test-only [#1393](https://github.com/NYGsatoshi/Coglatas/pull/1393). PR #1393 had [16/16 exact-head checks PASS](https://github.com/NYGsatoshi/Coglatas/actions/runs/40140803159) and 1,749 Angular units PASS, but the resulting [Main CI #40141833874](https://github.com/NYGsatoshi/Coglatas/actions/runs/40141833874), attempt 1, has independent real-stack P0 **FAILURES** and cannot be treated as a complete accepted Main, regardless of remaining running checks.

- [Fast Collaboration #121016046362](https://github.com/NYGsatoshi/Coglatas/actions/runs/40141833874/job/121016046362) **FAIL** at canonical `FUNC-MSG-001 / MSG-THREAD-DENIAL`: the authorized root UI element was absent at the existing 15-second expectation, *before* the restricted actor stage. `FUNC-NOTIF-001` passed. Full Collaboration on the same Main succeeded, but does not waive Fast's first-attempt failure; [P0 #1365](https://github.com/NYGsatoshi/Coglatas/issues/1365) remains open.
- [Licensed P0 #121016020083](https://github.com/NYGsatoshi/Coglatas/actions/runs/40141833874/job/121016020083) **FAIL**: **7/8** passed; the remaining browser owner failed the unchanged `channel-create-dialog` 15-second visibility assertion before any create POST. A previous distinct Main had the same failure. [P0 #1391](https://github.com/NYGsatoshi/Coglatas/issues/1391) remains open; authorization invalidation is a hypothesis, **not established root cause**.
- Current test-only [PR #1394](https://github.com/NYGsatoshi/Coglatas/pull/1394) adds fixed-token, privacy-bounded browser click/dialog chronology and synthetic observer controls. It has **not** merged at this checkpoint. A successful premerge PR lane cannot qualify resulting-Main protected P0, and the new instrumented source has not yet reproduced the original failure.
- Previously held #1320/#1326/#1361 and independent Thread diagnostic [#1367](https://github.com/NYGsatoshi/Coglatas/pull/1367) remain unmerged/review only. No Source or RLS/SEC-ARCH contract is promoted by diagnostics.

The signed concrete SEC-ARCH Mapping/SQL Roles/Grants/Policies and effective RLS test receipts, approved isolated staging SSH/stable HTTPS environment identity, strict backup/restore and rollback [#1355](https://github.com/NYGsatoshi/Coglatas/issues/1355), complete SEC-16/17, actual school privacy/operator authorization, physical IME and external restricted HTTPS 17-flow pilot are still separate blockers. **No school-user admission, target operations, security threshold waiver, destructive schema operation or public release authorization.**


## Current source-bound release decision — 2026-10-25 JST

**NO_GO / Owner HOLD BOTH.** Product Main after normal [PR #1388](https://github.com/NYGsatoshi/Coglatas/pull/1388) merge: `cad010ef47e6563460a26929c8f307a32cfa62ff`. Its [Main CI #40136984323](https://github.com/NYGsatoshi/Coglatas/actions/runs/40136984323) was still **IN_PROGRESS at this checkpoint**; exact-head PR checks do not transfer. The previous Main `bf1e589dbaeacc6140dfe72e84a5bc01012ed467` ended [Main CI #40103424066](https://github.com/NYGsatoshi/Coglatas/actions/runs/40103424066) in **FAILURE**, because first-attempt Extended Collaboration [job #120902872078](https://github.com/NYGsatoshi/Coglatas/actions/runs/40103424066/job/120902872078) reproduced the empty second-root Composer input at the canonical unchanged `MSG-THREAD` assertion (1 FAIL / 2 PASS; zero test retries). Fast/Full and Qodana source-specific successes do not waive it. See the detailed [release readiness checkpoint](student-council-release-readiness-20261023.md#exact-resulting-main--release-blocker-checkpoint--2026-10-25-jst) and [P0 #1360](https://github.com/NYGsatoshi/Coglatas/issues/1360).

Owner approval is still needed for exact SEC-ARCH/RLS policies/role grants and approved staging SSH/stable HTTPS identity. Restore/rollback [#1355](https://github.com/NYGsatoshi/Coglatas/issues/1355), SEC-16/17 residual authorization, first-attempt Composer/Thread/device reliability, school/operator/privacy, physical IME and actual 17-flow target acceptance are not qualified. Explicitly held #1320/#1326/#1361 and physical recovery #1379 remain unmerged. No live school-user access, target deployment, security or backup-strategy change is authorized. Earlier sections below preserve their original source-specific history.


## Latest Main4c Qodana nested-pattern failure — 2026-10-24 UTC

**NO_GO / unchanged Owner HOLD BOTH.** The actual resulting-Main
`4c4720f77226fa496136a79c17b0b4c3ac948669`
[CI #40076025574](https://github.com/NYGsatoshi/Coglatas/actions/runs/40076025574)
failed the [full Qodana Cloud check #120820356682](https://github.com/NYGsatoshi/Coglatas/actions/runs/40076025574/job/120820356682)
on first attempt. Prior `MergeIntoPattern` is back to frozen budget
**132/132**; new `MergeNestedPropertyPatterns` is **2/0** at the
two nested ReadOnly recipient-count patterns. The exact full SARIF
artifact is `12553668214`; previous Main48cf reported zero under that rule.
Do not treat PR #1382's successful tests as a full Main quality PASS.

[Scoped repair PR #1384](https://github.com/NYGsatoshi/Coglatas/pull/1384)
changes precisely two syntactic C# nested property patterns into equivalent
extended member paths with **no** assertion or policy change. Exact PR
CI and subsequent Main Cloud/Community checks remain mandatory.
[SEC-16/17 candidate #1381](https://github.com/NYGsatoshi/Coglatas/pull/1381)
also groups pre-existing conditions using flat extended C# patterns; its
current HEAD `72f4301ce606b768069ac2136cb3b9545aec3575` needs its own
[PR CI #40077918899](https://github.com/NYGsatoshi/Coglatas/actions/runs/40077918899).
Older source PR passes must not be transferred.

The new Main4c first-attempt real-backend Fast/Full collaboration,
Thread denial and licensed P0 browser controls passed in their selected
scopes, but historic #1360/#1365 causes remain OPEN. The formal
SEC-ARCH mapping/RLS, authorized SSH/stable HTTPS staging, strict restore
and rollback, school/privacy/operator and external 17-flow Pilot gates
are still unsigned or NOT_EXECUTED. No public school-user access is authorized.



## Main Qodana repair and scoped SEC-16/17 verification — 2026-10-24 UTC

**Public Student Council release remains NO_GO / Owner HOLD BOTH.**
The first-attempt Main `48cf0b397122fb3cadcf18926a1891e7bbf80a5d`
[CI #40073017091](https://github.com/NYGsatoshi/Coglatas/actions/runs/40073017091)
ended FAILURE from Qodana Cloud and Community `MergeIntoPattern` 135
against unchanged baseline 132. Preserve that failed-source record.
The two-test assertion-only repair [#1382](https://github.com/NYGsatoshi/Coglatas/pull/1382)
normally merged to **Main `4c4720f77226fa496136a79c17b0b4c3ac948669`**;
its [own Main CI #40076025574](https://github.com/NYGsatoshi/Coglatas/actions/runs/40076025574)
is separately executing at this checkpoint. Do **not** infer current
full Qodana success from passing PR fast-lane checks.

On this new Main, real-backend Fast and Full `FUNC-MSG-001` owners
passed on first attempt (retry=0, FLAKY=0), including `MSG-THREAD`
and `MSG-THREAD-DENIAL`, and the independent licensed P0 test job
reported eight required browser cases / eight JUnit cases passed.
The historical Composer [#1360](https://github.com/NYGsatoshi/Coglatas/issues/1360)
and Thread [#1365](https://github.com/NYGsatoshi/Coglatas/issues/1365)
failures remain causally unresolved despite these successful positive observations.

[Integration PR #1381](https://github.com/NYGsatoshi/Coglatas/pull/1381)
has been rebased by normal ancestry-preserving merge onto Main4c4720,
then received two narrow test-only C# property-pattern amendments to
avoid another Qodana ratchet regression without weakening File eligibility,
folder placement or negative authorization assertions. Current source
`760335de1a5c5cf4736694031f4850c1001b5970` has its own
[PR CI #40076797412](https://github.com/NYGsatoshi/Coglatas/actions/runs/40076797412)
running/pending at this checkpoint. Prior 3-fact PostgreSQL successes
on older source do **not** qualify the amended current head.
No held #1320/#1326/#1361, formal RLS, HTTPS target or recovery strategy
is approved or merged by these changes. Do not admit real school users.



## Main #1380 integration checkpoint — 2026-10-24 UTC

**New Main quality regression observed:** [Main CI #40073017091](https://github.com/NYGsatoshi/Coglatas/actions/runs/40073017091),
attempt 1, failed the Qodana Cloud project-model ratchet
(`MergeIntoPattern` 135 against frozen 132). Exact original Main0c5,
intermediate Main0dd, and current Main48cf SARIF inventories isolate
three new occurrences in two existing File regression test files;
no Critical finding or threshold change is implied. The narrow
[test-only repair Draft #1382](https://github.com/NYGsatoshi/Coglatas/pull/1382)
expresses unchanged assertions using C# property patterns. Repair PR CI
and resulting-Main full Qodana must pass separately. At this checkpoint
the current Main cannot be called green, regardless of its Functional successes.


**RELEASE NO_GO / both Owner HOLDs remain.** Product Main is
`48cf0b397122fb3cadcf18926a1891e7bbf80a5d` after normal
[PR #1380](https://github.com/NYGsatoshi/Coglatas/pull/1380) merge.
Exact PR-head [CI #40069554136](https://github.com/NYGsatoshi/Coglatas/actions/runs/40069554136)
passed 16/16 jobs, but [resulting-Main CI #40073017091](https://github.com/NYGsatoshi/Coglatas/actions/runs/40073017091)
was still executing when this checkpoint was recorded. Do not transfer PR
success or call the resulting Main green before the actual terminal checks.

On the new Main, original-attempt Fast and Full Collaboration passed
`FUNC-MSG-001`, including canonical Thread and Thread-denial stages,
with zero observed test retries; [Full job #120811156445](https://github.com/NYGsatoshi/Coglatas/actions/runs/40073017091/job/120811156445)
reported three owners passing and complete safe evidence. The protected
[licensed P0 job #120811125132](https://github.com/NYGsatoshi/Coglatas/actions/runs/40073017091/job/120811125132)
passed the eight required real-backend browser checks. These results do
**not** prove historical intermittent Composer/Thread causes resolved;
[#1360](https://github.com/NYGsatoshi/Coglatas/issues/1360) and
[#1365](https://github.com/NYGsatoshi/Coglatas/issues/1365)
remain open.

New scoped [Draft PR #1381](https://github.com/NYGsatoshi/Coglatas/pull/1381)
carries exact original File eligibility/placement and Direct self-leave
regressions from #1370/#1371/#1368 with no product runtime changes.
Its combined-source CI, independent review, and resulting-Main controls are
separate; it is **not merged**. Recovery alternative
[Draft #1379](https://github.com/NYGsatoshi/Coglatas/pull/1379)
is likewise held and does not approve a formal backup strategy.

Other mandatory release gates remain: actual approved restricted HTTPS
staging and stable origin, detailed SEC-ARCH mapping/RLS activation Owner
approval, full Files and realtime denial matrices, strict backup/restore and
rollback, school/privacy/operator approval, physical IME, and the public
17-flow pilot. No school identities, live target connection, unsafe intake,
RLS activation or public admission is authorized. Previous checkpoints
below retain their historical exact-source outcomes.


## Review-ready engineering checkpoint — 2026-10-24, after Main #1373

**Release decision remains NO_GO / Owner HOLD BOTH.** Product Main is
`0dd39000b38dd047e0a35513e2b7403d3bc5cd06` (normal Merge #1373).
Private Spec Main remains separately governed; nothing in this checkpoint
approves detailed SEC-ARCH Mapping/SQL Role/Grant/Policy or RLS activation.

[Product PR #1380](https://github.com/NYGsatoshi/Coglatas/pull/1380)
HEAD `e9e8a45a5fe0f646372b5f08d099c74264dce121`
is Ready for Review with [exact-head CI #40069554136](https://github.com/NYGsatoshi/Coglatas/actions/runs/40069554136)
**SUCCESS (16/16 jobs)**. The frontend browser lane reports 411 passed and
13 explicit existing skips. Review/normal-Merge and resulting-Main qualification
are separate; this PR has not been integrated into Main at this checkpoint.
The protected [#1378 synthetic acceptance #40068617000](https://github.com/NYGsatoshi/Coglatas/actions/runs/40068617000)
verified eight required P0 browser cases on **different** exact HEAD
`a9fa3d923e8766254e8eb193246aee8dc45d2eb7`, not on #1380 or Main.
Historical first-attempt Thread failures [#1360](https://github.com/NYGsatoshi/Coglatas/issues/1360)
and [#1365](https://github.com/NYGsatoshi/Coglatas/issues/1365)
remain open.

[Review-only recovery PR #1379](https://github.com/NYGsatoshi/Coglatas/pull/1379)
HEAD `463708c947b652819de70dd9da9239761fbd1784` has
[PR CI #40071883520](https://github.com/NYGsatoshi/Coglatas/actions/runs/40071883520)
**SUCCESS (16/16 jobs)** and a separate loopback redirect-guard test
[2/2 PASS](https://github.com/NYGsatoshi/Coglatas/actions/runs/40071883389).
Its earlier source-bound isolated physical restore receipt does not become a
full exact-head restore/target rollback PASS, formal backup strategy approval,
or permission to merge the candidate.

Outstanding release gates include SEC-16 full matrix
[#1349](https://github.com/NYGsatoshi/Coglatas/issues/1349), SEC-17
[#690](https://github.com/NYGsatoshi/Coglatas/issues/690),
strict backup/restored-app/rollback [#1355](https://github.com/NYGsatoshi/Coglatas/issues/1355),
verified restricted HTTPS/SSH/environment authorization, school/privacy
operator controls, actual 17-flow external pilot execution and the two
independent Owner HOLDs. Keep student identities, files and public admission
disabled. Previous dated checkpoints below remain historical evidence.


## Main QA follow-up — 2026-10-24 later exact-source checkpoint

**Release still NO_GO.** Main is now
`0c5f9baace186940f948758b1731fd3ec891c2f3` after normal Merge
[Qodana-only #1363](https://github.com/NYGsatoshi/Coglatas/pull/1363).
At this Main, both full Qodana and Fast Functional **PASS**, but
[Main CI #40056196940](https://github.com/NYGsatoshi/Coglatas/actions/runs/40056196940)
remains **FAILURE**: Full collaboration `FUNC-MSG-001 / MSG-THREAD-DENIAL`
lost its authorized Thread panel after HTTP 200 in attempt 1. A
single-job attempt 2 passed but does not waive the original P0.
See [#1365](https://github.com/NYGsatoshi/Coglatas/issues/1365) and
[test-only Draft #1366](https://github.com/NYGsatoshi/Coglatas/pull/1366).
The previous checkpoint below is immutable history; the RLS/Staging
**Hold Both** remains active, and no real-user/public release permission exists.

## Current release decision — 2026-10-24 (evidence checkpoint)

**NO_GO / OWNER HOLD.** Product Main `2ec94d76dc8dfcc7d6b34ff2245651e720f81329`; private Spec Main
`6a0491c925b4980ce737dfbdf775816c237e44fe`. The
[2026-10-24 execution checkpoint](student-council-release-readiness-20261023.md#execution-checkpoint--2026-10-24)
reconciles the actual Main CI failure, reviewed but held messaging PRs,
the narrow Qodana repair candidate and the still-missing Staging/RLS
authorities. Historical success is source-bound, not a later-head release PASS.
No previously recorded **Hold Both** decision is implicitly lifted.

## Historical release decision — 2026-10-23

**NO_GO; RELEASE_READY is not established.** The refreshed
[release readiness report](student-council-release-readiness-20261023.md)
records Main `babcf80f7babafe4087fb7cb5742edb2b98868c2`, current PR and CI
identities, requirement-to-test evidence, owner decisions and the blocking matrix.
Main CI succeeds within its recorded scope. Detailed SEC-ARCH/RLS approval and
qualification, complete acceptance, safe upload policy, approved target HTTPS,
strict restore and rollback remain outstanding. Historical checkpoints below
retain their original source identities, failures and qualifications.

## Historical release decision — 2026-10-21 JST, 15:24 UTC

**NO_GO for school-user access.** Product Main is now
`7c1301bbdeb80a44d1d586a0642b4ec80fa65cb1` after normal merge #1300;
private Spec Main remains `6a0491c925b4980ce737dfbdf775816c237e44fe`.
The local Home revision-442 follow-up passed ten owning units, production build,
fourteen mocked browser cases and both unchanged shared-shell golden cases.
Final-head CI, independent follow-up review and licensed Home acceptance remain
pending. No local source bridge or prior-head pass qualifies the resulting Main.
Sakura/stable HTTPS, the seventeen-flow pilot gate and all school/operator
conditions remain unexecuted or unresolved. The earlier checkpoint below is
preserved with its original source and outcomes.

## Historical release checkpoint — 2026-10-21 JST, 15:11 UTC

**NO_GO for school-user access.** At 2026-10-20T15:11Z, Product Main is
`f9294cf5142d43b6fd93df5406d69f5d63c32255`; private Spec Main remains
`6a0491c925b4980ce737dfbdf775816c237e44fe`. Main, PR, synthetic, design
and target execution evidence retain separate source identities.

| Evidence | Current disposition | Limit |
| --- | --- | --- |
| Group/Task #1297 | Normally merged to `068b7f14` after independent exact-head native and eight-owner licensed acceptance | Subsequent Main licensed attempt failed; its failure is retained. |
| Task response lifetime #1301 | Normally merged to `f9294cf5`; head `574c2c23` native CI, four-language CodeQL, six Fast owners and eight licensed P0 owners passed | Native test-only routing ran no new Angular unit/browser or backend/provider tests. Current Main automatic qualification is pending. |
| Home #1298 | `155ce453` native CI passed: 1,696 units, 61 architecture controls, 16 helpers, 397 mocked browser passes/thirteen existing skips | Detailed Sonar was queued at the sealed snapshot. No licensed Home or target acceptance; revision 442 follow-up is local. |
| Files #1300 | Original `d9136011` one-Fact PostgreSQL 18.6/HTTP qualification and independent review accepted | `e4693a0a` ReSharper failure preserved; one-line correction and `5c12155f` composition need final-head CI. Not a complete Files matrix. |
| Penpot | Read-only revision 442 generated CSS obtained for all ten priority Approved Dark boards; current G04 structure and original PNG obtained | Acquisition is not visual parity. Historical revision 339 fallback is not relabeled as current. |
| Previous Main `068b7f14` | Mixed backend 2,567 plus twelve architecture rows, Angular 1,686/149 files and 383 mocked browser passes/thirteen existing skips; selected security lanes passed | Its licensed P0 remains seven PASS/one ERROR. PostgreSQL namespace counts are not a pure provider total. |
| Current Main `f9294cf5` | [Main CI 39444445994](https://github.com/NYGsatoshi/Coglatas/actions/runs/39444445994) and [CodeQL 39444445801](https://github.com/NYGsatoshi/Coglatas/actions/runs/39444445801) run automatically on the actual Main | Pending at this checkpoint. No PR success or source-tree equality substitutes for the resulting-Main result. |

The #1301 licensed run is
[39442289702/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/39442289702):
eight required owners passed with zero retries or skips. The mandatory owner
completed unchanged Group lifecycle/revocation controls and the Task focus
assertion. Successful raw helper values, focus snapshots, running-container
image identity, PostgreSQL minor, loaded DLL stamps and served assets were not
retained for that P0 run. Do not infer them from its fresh-build image output.

All six feature verdicts remain NO_GO for school users. Files intake denial
does not make existing folder/move/delete operations globally read-only and
does not provide malware scanning. Arbitrary unscanned upload remains closed.
Held Shared/Filters/Artifacts and private SEC-ARCH contracts remain inactive.
Historical ZAP 40018 and dependency findings retain their separate disposition;
current non-reproduction or an image SBOM with no High findings does not close
those records. Numerical assurance remains suspended under #1128.

The separate Student Council seventeen-flow public HTTPS gate is NOT_EXECUTED.
Sakura host qualification, stable TLS origin, current negative matrix,
backup/strict restore, rollback and school/privacy/operator approvals remain
release conditions. The broader #481 contract is not modified or marked passed.
The historical checkpoints below preserve original failures and source limits.

## Historical PR qualification checkpoint — 2026-10-20

The last remotely verified Product Main is
`3816908c6a1bae3cb79128af9f9dd3c6633cf50f`; Spec Main remains
`6a0491c925b4980ce737dfbdf775816c237e44fe`. PR
[#1295](https://github.com/NYGsatoshi/Coglatas/pull/1295) normally advanced through
`d8803f746d1d49e889edda23518a4eceed608eeb` to
`096e4b4852065b9841f6214f67d171bb87752b8a` after Owner publication approval
and independent reviews. The following checks retain their executed source;
they do not transfer to a later successor or resulting Main.

| Source | Executed qualification | Limit |
| --- | --- | --- |
| Thread/Files UI `949ce755` | Fresh production build; 35 distinct pinned-Linux Chromium cases at 1536/1366/768/390/320px, zero failure/skip/retry; independent source and evidence review | Mocked UI only. The preceding source `28f02cbf` passed 75 owning units; the subsequent product delta is inset focus-ring CSS. |
| SEC-06 module loading `9dd866c7` | Original-package Node 22 contract and pinned Graal clean/high/query-high fixtures; independent review | Both synthetic High cases remain rejected. Hosted Security passed on the integrated `096e`; historical alert disposition remains separate. |
| Runtime quality `6630f743` | Strict TypeScript, seven sequential-order checks, 3,122 Python differential cases, full Node 22 SEC-06 contract and pinned Graal fixtures | Both Sonar checks passed on `096e`. Production authorization, scanner policy, budgets and targets remain unchanged. |
| Task focus diagnostics `c7677219` | 22 helper tests and strict declaration/actual Playwright compatibility checks; independent review | No product focus intervention. Full-owner strict compilation retains the same 23 diagnostics reproduced on the published base. The original focus failure remains unexplained. |
| Group persistence oracle `f7951cfc` | Twelve compiled helper invariant methods and two strict checks passed; independent source and evidence review | Test-only. The previous full P0 failure remains failed; this successor requires fresh CI and all eight licensed owners. |

The UI repair keeps selected Thread tabs visible and their focus ring inside the
scroll boundary. Files retains native grid scrolling and keyboard access at the
affected desktop width. Screenshot and build results are attributed to their
executed sources; later integration alone does not rerun those checks.

Published-source licensed run
[39390821744/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/39390821744)
ended with seven owners passing and one failing, with zero retries. The Group
helper completed first-add, retained-history, re-entry and leave controls before
the mandatory owner failed at Task title focus. Native run
[39390656044/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/39390656044)
completed with fifteen jobs passing and Security failing before runtime security
scanning. Its 53 head checks contain 44 successes, three failures (Security,
SonarCloud Code Analysis and licensed acceptance), and six configured skips.
ReSharper passed. This terminal read supersedes the earlier nonterminal snapshot;
it does not change the failed results. Fresh successor CI, all eight
licensed owners, protected merge and independent resulting-Main qualification
remain required.

Integrated `096e` native run
[39396278105/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/39396278105)
completed successfully: all sixteen native jobs and all six strict contexts.
Its 55 head checks include 48 successes, one licensed failure and six configured
skips. Backend 2,566 mixed tests plus twelve architecture checks passed; actual
PostgreSQL 18.6 was observed in that native lane. Frontend 1,677 units in 149
files and selected browsers 383 PASS/thirteen configured SKIP were recorded.
The three PR ZAP roles reported zero High/Medium/Low alerts, with rule 40018
started and completed without skipping. This is current-source non-reproduction,
not a disposition of the historical alert or complete pilot security coverage.

The separate fresh-image licensed run
[39396441400/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/39396441400)
ended seven PASS/one FAIL, zero skip/retry. The mandatory owner stopped at the
first-add member persistence assertion: the immediate POST retained 200ns absent
from the persisted value returned by the fresh GET. All ten other fields, one-row cardinality and
three historical member rows matched. The Group result and Task-focus
diagnostics were not completed in this owner; separate Workspace and Files
owners passed.

An isolated Npgsql 10.0.3/PostgreSQL 18.6 experiment verified 240 prepared binary
timestamp writes and fresh reads, including every 100ns residue, pre-2000 values
and 1µs distinction controls. Npgsql truncates signed microseconds relative to
the PostgreSQL epoch toward zero. The test-only successor uses that exact
conversion and requires persisted alignment; every other field, uniqueness,
history and admission-chronology check stays intact. Invalid UTC calendar values
are rejected. No tolerance, product behavior, API/DB contract, permission or
scanner change is introduced. Provider/pure checks do not establish a new P0 pass.

**NO_GO** remains. Dedicated Home parity, complete revocation/reconnect controls,
historical security evidence, target HTTPS, strict restore and school/operator
conditions remain unresolved. No school-user release follows from these local
results.

## Main checkpoint: normal merge and fresh Main failure

PR [#1294](https://github.com/NYGsatoshi/Coglatas/pull/1294) merged normally on
2026-10-20 at 05:50:39Z after its exact-head checks and independent review.
Product Main became `3816908c6a1bae3cb79128af9f9dd3c6633cf50f`, with the same
tree `1360ea5e7729bfa696b64ee5ca5c5248a20b9986` as reviewed source `2cc08380`.
Spec Main remains `6a0491c925b4980ce737dfbdf775816c237e44fe`.
GitHub also marked #1290–#1293 merged through that integration commit; no
separate merge of those worker PRs or Draft-state change was performed.

Fresh Main run [39377066283/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/39377066283)
has failures in Fast collaboration, its aggregate/strict gate, and both full
Qodana analyses. Source-tree equality does not transfer the preceding PR's pass.
The terminal provider inventory has 67 checks: 60 SUCCESS, five FAILURE and
two configured SKIP. Of the six strict required contexts, five succeeded and
`functional-fast` failed. The run itself concluded FAILURE.

| Current Main evidence | Observation and limit |
| --- | --- |
| Backend/native PostgreSQL | 2,561 mixed backend tests and twelve architecture checks passed; actual PostgreSQL 18.6 and the owning intake fact were observed. This mixed total is not a pure PostgreSQL count. |
| Frontend | 1,647 units in 147 files, 59 Node contracts and selected browser 369 PASS/thirteen existing SKIP. Success-policy omissions mean no new Main screenshot comparison is claimed. |
| Licensed acceptance | Eight required owners passed with zero observed retry/skip, including bounded Group and Workspace helpers. This is not the full pilot negative matrix or target qualification. |
| Functional | Full and Extended passed; Fast failed after second-root draft fill at the unchanged value assertion. Send/POST was not reached. The hosted cause remains unproven. |
| Qodana | Both analyses reported 2,299 findings and failed three unchanged rule budgets: collection expressions 41/37, concrete default values 3/0, UTF-8 literal 1/0. Syntax-only corrections preserve authority and test oracles; fresh checks are required. |

The optional-storage draft repair and bounded diagnostics are separately scoped.
No timeout, retry, required check, scanner, budget or authorization contract is
relaxed. Earlier failed attempts remain failed. Any successor requires its own
checks and independent review, followed by fresh resulting-Main qualification.

The seventeen-flow external HTTPS gate remains NOT_EXECUTED. Sakura host
qualification, complete revocation/delay/reconnect controls, backup/strict restore,
rollback and school/operator conditions remain unresolved. **NO_GO** remains.
No school account/data or public opening is authorized by these engineering checks.

## Historical qualification checkpoints

The following statements retain their original source and execution dates;
their old Main/candidate status is superseded by the checkpoint above.

## Qualification update — 2026-10-20, source 955581c0

This timestamped supplement supersedes only the older execution-status claims
below. Those checkpoints and failures remain historical evidence; they are not
relabeled as current passes. Product Main remains
`1e2177e2197ae2507b0b009545a86b94b3ab1b1c`; Spec Main remains
`6a0491c925b4980ce737dfbdf775816c237e44fe`. Candidate
`955581c0fca8f9363fb3b16f392f62662e5f38c5`, tree
`f4283c5314adf18e0aef345e7a062b0bca4942e6`, has not merged.

| Source/run | Actual evidence | Remaining limit |
| --- | --- | --- |
| 955 licensed 39370694359/1 | Eight unique required owners PASS; zero failure/skip/retry; mandatory owner 35.7s and Workspace owner 13.1s | Success native rows/verifier and bound unconditional helper establish completion; detailed helper/wire attachments and legacy JUnit were not published. No target qualification. |
| Group helper at 955 | One create, two sends, two live wire/UI receives; third unread 0→2→actual UI read→0, other-actor privacy; history/reload, one removal/redaction and explicit recipient refresh clearing | Not UI add/readd/leave, mentions, immediate send, unsolicited clearing or complete revoked/delay/reconnect matrix. Existing LastReadAt semantics remain; no strict loaded-prefix-only claim. |
| Workspace helper at 955 | Four writes (add/two role changes/remove), persistence/reload, exact generic 400/body/ID redaction and management denials | No recipient Workspace protected UI-cache or unsolicited clearing proof. Ordinary self-leave remains deferred. |
| Native 39370686941/1 at955 | Completed lanes: 1,647 units/59 Node, 369 browser PASS/thirteen existing SKIP; 2,561 mixed backend/twelve architecture; native PostgreSQL 18.6 observed. All six strict contexts individually SUCCESS, including security118460142300. | Terminal FAILURE: 49 checks = 42 SUCCESS/six configured SKIP/one ReSharper FAIL (two test-quality findings). Cannot qualify merge. Native PG version does not transfer to licensed run; mixed totals are not pure provider counts. |
| Root successor 17273cf6 | One test file/four additions/two deletions. Local Release exit 0; one owning HTTP fact PASS/zero skips on fresh owned PostgreSQL 18.6; source/assembly hashes unchanged. | Independent source and bounded execution reviews accepted. No native CI/P0, whole-suite, merge or target credit. |

The failed f4e licensed 39366803248/1 (six/two) and cecc 39369387994/1 (seven/one)
remain failed. The 955 addition is public Connected readiness before Group
creation in a settled live journey, not a protected-state/cancellation change or
immediate-race qualification. The cecc native cancellation is not a pass.

Current licensed PostgreSQL minor, actual running container.Image, native DLL
stamps and served manifest were unobserved. The separate public HTTPS seventeen-
flow gate remains NOT_EXECUTED. Target security negatives, host/persistence,
backup/full restore/rollback and school/operator decisions remain unresolved;
selected restore mechanics never establish strict schema/key/login recovery.
**NO_GO** remains; no protected merge until current checks/review qualify, and
any resulting Main needs fresh independent qualification. Prior approvals do
not need reapproval; §11-reserved production changes/school opening remain separate.

### Test-quality successor — local evidence only

Root's `17273cf642c1a7e3bf7790904b4ae61219e73a78` changes one test file
(four additions/two deletions). Local Release completed with exit 0 and one owning
HTTP fact passed/zero skips against a fresh owned PostgreSQL 18.6 fixture
(Debian 18.6-1.pgdg13+2), from 2026-10-20T05:04:14Z to 05:05:56Z. All 2,794 tracked
source hashes and seven actual assembly hashes were unchanged before/after;
only the fresh fixture was removed. Independent source and execution reviews
accepted this bounded fact; fresh hosted qualification remains required.
No native CI or licensed P0 execution is yet recorded for 17273cf6. This local
fact does not qualify a whole suite, resulting Main, target or public opening.

## Historical source and evidence checkpoints retained

Statements below such as f5e 'not built/executed' describe that earlier source,
not955. Their failure, source/mock/provider and target limits remain intact.

The public pilot verdict is **NO_GO**. The target dates, 2026-10-22 through
2026-10-23, do not waive authorization, deployment or school approval.
This gate defines the separate Student Council Pilot Golden Path. It does not
change or satisfy the broader Project/Task contract in #481.

## Current source and evidence checkpoint

The 2026-10-20T02:16:16Z read confirms Product Main
`1e2177e2197ae2507b0b009545a86b94b3ab1b1c`, tree
`00a426da650d8dba17c659f4c232d2ec324c231b`, after normal merge #1289.
Its Main [39348320756](https://github.com/NYGsatoshi/Coglatas/actions/runs/39348320756)
attempt 1 succeeded: 40 successful jobs and two configured skips, all six
strict required contexts, and 63 head checks with 59 successes and four
configured skips. The source-bound packet separately records 2,526 backend
tests, 1,562 frontend units, 309 browser passes with thirteen unchanged skips,
actual PostgreSQL 18.6 and all 28 structural DB decisions. Numerical
performance assurance remains suspended. Full Qodana and deep Security passed;
their scope does not establish complete pilot or target-host security.
This is engineering qualification of that Main, not public-pilot acceptance.
Product Main and private Spec Main remain unchanged at this checkpoint.

### Approved implementation candidates

The following candidates remain separate from Main. Historical source-only
checkpoint `2c5d8db22eb33999829696940b867290df9b8c90` includes Workspace
UI/backend, Group backend/UI, both native quality successors, explicit read UI
`5933f8f9` and unread helper `276e8194`. It normally merges the reviewed worker
commits without replacing their frozen packets. Older `4d5b4b59` lacked Group
UI and remains historical. Its documentation successor f4e subsequently ran
and failed the licensed P0 selection as recorded below.

Current repair checkpoint `dffad96b87250c108f0164c998acf05fac23bafc` normally
merges accepted `6ca0a095` Files intake controls, `51fa5216` Workspace harness
and `556982e7` scoped native reliability correction. Its successor source-only
checkpoint `f5e03a763f7ac4665b05ca41b77a3a23510cbe18` also normally merges
reviewed first-empty live-message repair `488f9046` and target-gate successor
`8543099a`. No combined build/runtime has executed. The target gate remains
unexecuted and cannot grant a full-pilot or public pass.
The Owner already approved implementation, synthetic verification and normal
protected merge subject to technical qualification; no renewed permission is
implied. School-user opening and deployment acceptance remain separate gates.

| Source | Actual evidence | Outstanding limit |
| --- | --- | --- |
| Workspace backend PR [#1290](https://github.com/NYGsatoshi/Coglatas/pull/1290), `8101e2a55722d625b258a5c9f9678b6ce0de71ad` | Earlier 4ccc local selection passed 143: 18 actual PostgreSQL, 124 InMemory and one metadata check. Current test-only quality repair preserves production/assertions; Coordinator-retained current hosted logs show 2,543 mixed backend and twelve architecture passes, zero changed ReSharper findings. | Original hosted 126 failures and later three quality findings remain retained. Mixed execution does not replace PostgreSQL evidence for all cases. The 383 anonymous HTTP 401 checks establish anonymous rejection, not authenticated resource acceptance. Final combined execution remains pending. |
| Workspace UI draft PR [#1291](https://github.com/NYGsatoshi/Coglatas/pull/1291), `ab6fa6ea60ca5e20eaff510a2279727b02bbffa8` | Unicode search successor `87e842ab` plus named/focusable shared-dialog reading region and visible focus. Local production build, 79 focused units and 32 selected browser cases passed; the repaired two Announcement cases also passed separately at the unchanged default 30-second timeout. Current worker checks were reported terminal green by the Coordinator. Existing assertions and IME guards remain. | The two Announcement cases overlap the 32 and are not additional unique coverage. Local 32-case selection used a 120-second CLI timeout. The 60-file evidence inventory was generated after execution; there is no captured pre-run source manifest or compiled-build archive. Final combined integration remains pending. |
| Group backend PR [#1292](https://github.com/NYGsatoshi/Coglatas/pull/1292), `dc68be5d6e8e29b0fc10a4154eceba597e1a82b2` | Earlier 1cf owning/adjacent selection passed 79 actual PostgreSQL and two fallback controls, zero skips. Reviewed quality repair has separate Release/17-PostgreSQL evidence. Coordinator-retained current hosted logs show 2,543 mixed backend and twelve architecture passes, zero changed ReSharper findings. | Earlier nine ReSharper findings remain historical failures. Not merged. Bounded provider, HTTP and transport controls do not prove final Angular integration, complete security coverage, target HTTPS or school release. |
| Group UI `c8d889f57bbd59b78f615072467b305164f3edae` | Fresh production build, 305 units, 59 architecture checks and four license checks passed. Browser selection: 22 executions, 21 passed and one End-key scroll-timing failure. Independent read-only review covered all 21 changed paths without a confirmed actionable source finding and opened all thirty historical c5 PNGs plus the Approved DM export. | Historical normal-roster captures establish only partial parity. The c8 browser selection is not a pass; its build/unit/architecture/license evidence remains specific to c8 and was not freshly executed at 09e. |
| Group UI draft PR [#1293](https://github.com/NYGsatoshi/Coglatas/pull/1293), `09e58d4ab105898dc22667da31a24f5aba96ff09` | Sealed 375-file packet: fresh 81-file build, 24 distinct mocked browser executions passed with zero skips/retries, 34 original frames. Independent source/rendered review accepted the scoped candidate. Coordinator-retained current hosted logs record 1,580 units/144 files and 331 browser passes with thirteen configured skips. | qa-v9 and c8 failures remain retained separately; c8's local units/architecture/license checks were not rerun at 09e. Mocked browser success/current worker CI is not final Angular/backend integration, complete parity or public-pilot qualification. |
| Explicit read UI `5933f8f980c6958e63af0a0017b2583b8432d437` | Fresh 80-file build, eighteen distinct mocked browser passes with zero skips/retries, ten original five-width frames. Earlier identical facade/class at 6978 passed 328 units, 59 architecture and four license-guard contracts; those checks were not rerun at 5933. | Existing LastReadAt-based server counting can include messages not loaded before the command. No strict cursor-only prefix or participant privacy/runtime proof is inferred from mocks. |
| f4e licensed P0 [39366803248](https://github.com/NYGsatoshi/Coglatas/actions/runs/39366803248), attempt 1 | Actual checkout f4e, fresh protected licensed build and observed PostgreSQL 18.6. Raw JUnit: eight required cases, six passed/two failed/zero skipped. Separate canonical Task and Files owners each passed. | Group manager send and selected recipient wire receipt passed, then recipient UI body count0 failed; later Group send/history/removal/unread/privacy controls were not reached. Workspace's post-removal General status expected404/actual400 failed after four persisted commands; later checks did not execute. No overall P0 or target pass. |
| Workspace harness `51fa5216` and native reliability `556982e7` | Source-only correction requires existing exact400 generic Conversation error, preserved expected-body exclusion and three opaque ID exclusions. Explicit comparators preserve exact key/member assertions; public method parameter now matches its existing interface. | Current source has only noEmit/discovery/source checks. Six native Sonar reliability failures from f4e remain historical and require fresh current CI; no suppression, budget, status set, oracle, timeout or retry relaxation. |
| Empty-conversation receive repair `488f9046` | The scoped main-event path advances only an empty status to ready. Corrected baseline retained three rendered failures/ten negative passes; current selected frontend units 341, architecture 59, license guards four and standard build passed. | Initial five-failure run included two test-oracle mistakes. HTTP/realtime stubs and a standard build are not a successor licensed receive run. Combined actual receive/lifecycle/unread remains unexecuted. |
| Target gate `8543099a` | Reviewed supported Group/unread assertions and sixteen pure-helper passes; startup invokes only the two literal helper test files before the existing Angular runner. | Source/pure checks establish no target or full seventeen-flow pass. The current combined runner hook has not executed; explicit missing controls and partial/public-false receipts remain. |
| Files intake `6ca0a095` | One actual PostgreSQL/Kestrel HTTP fact passed/zero skips, with enabled intake positives followed by persisted false, all three valid multipart denials, unchanged storage/metadata and authorized exact-byte readback. Seven corrected offline guard methods passed; actual synthetic native DTO replay and two derived negatives were independently checked. Five native assembly snapshots and loaded Web hash bind the local Debug run. | Two earlier HTTP failures remain retained. No captured complete pre-run source inventory/hosted producer provenance, scanner, global read-only, target TLS/full Program startup/student-role parity or target closure. This is one test with many subassertions. |
| Prepared Group unread and later lifecycle oracles | Reviewed Group helper retains both opposite live UI/wire, known authors, history/removal and third-cookie unread0→2→one Angular read→0 with unchanged other-user state. | They remain required, not substituted. Only the first selected wire receipt executed before f4e's UI failure; third-user read/isolation was NOT_REACHED. Repair runtime is NOT_EXECUTED. Workspace recipient protected UI was not loaded; Group mentions remain unqualified. |

For this exact canonical P0, the serialized legacy/Task/Files runner plans pass
explicit `--retries=0`. The global Playwright CI default remains two, but is
overridden by those arguments. Actual f4e execution contains one attempt for
each of the eight required cases. No retry policy or configuration was changed.

The Coordinator's 2026-10-20T03:46:34Z native read reported current #1290–#1293
worker checks terminal green and merge state CLEAN. These are separate PR merge
checkouts, not this combined candidate or its resulting Main. UI PRs remain
drafts until final combined qualification; no merge or release is inferred.

All counts refer to their own selection and source. No sum across overlapping
executions is a full-suite count. Runtime evidence still needs exact final
source/build binding; a post-run inventory does not retrospectively establish
what bytes were compiled or present before execution.

The following older checkpoints retain their original outcomes.

At 2026-10-19T22:00:08Z, PR [#1288](https://github.com/NYGsatoshi/Coglatas/pull/1288)
normally merged as Product Main
`7bba2d00481fb8093ef87790f5b4fb2553856ba6`, tree
`d6943b0cd4b48c6be82c30493b3a4aa6d9b2bc12`, with ordered parents
`713fc9672a88a2752098ce07f9f3dd2225ce621e` and
`5003cfa549073d33ad5051a6f77a7bbfeadc919d`.
Its own full Main qualification is pending at this checkpoint.
Private Spec Main remains `6a0491c925b4980ce737dfbdf775816c237e44fe`
at the last recorded read; private approval/HOLD records remain authoritative.

The historical combined UI candidate is
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
| Historical UI candidate `89fe1c54`, controlled Linux V3 | A new isolated source volume, fresh build and measured four-CPU context passed all 1,562 units/143 files, 59 architecture checks, four license guards and 322 static executions: 309 passed/thirteen existing skips, zero failures/retries. Forty-nine original frames were bound to that source/build. These are historical executions, not current combined-source evidence. Causation of the earlier V2 timeouts remains unproven. |
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
| Workspace | Approved Dark list/selection/create/detail; implemented Option 2 management candidate; ordinary self-leave deferred | Existing reads/create plus approved management reads/commands; current #1290/#1291 worker checks remain separate from combined qualification | Local management selection is 18 PostgreSQL/124 InMemory/one metadata; Main evidence remains distinct | Current scope clearing and approved lifecycle controls; full target matrix pending | f4e persisted four Angular commands, then failed the removed General status oracle; stricter harness and repaired runtime remain unexecuted, target pending | NO_GO |
| Channel Chat | Dense Conversation timeline/composer, current participant projection and responsive disclosure candidate | Existing Conversation/Message source; no duplicate Posts model | Canonical send/reply/replay and current-authority controls passed at recorded revisions | Admission, persisted posting and transport controls; complete target matrix pending | Candidate channel send/receive/reload P0 passed; target pending | NO_GO |
| Thread | Existing Message Thread contract; compact composer, local tabs and loaded-root navigation candidate | Same-Conversation root/reply APIs | Named provider and source-bound Functional controls | Active authorized body; delayed/denied clearing tested within covered cases; full matrix pending | Candidate P0 passed; final source and target remain separate | NO_GO |
| Group DM | Creation/routing/member UI at 09e plus explicit read action are included; first actual opposite body rendering failed at f4e | Type 7 unsupported on recorded Main; #1292 implements approved G1–G3 through existing routes | Earlier 79 PostgreSQL/two fallback controls and separate quality regression/worker CI; not merged or combined qualification | Current membership, retained history and derived-path controls reviewed at candidate source; complete target matrix pending | First selected wire receipt passed but UI failed; full lifecycle/unread/privacy NOT_REACHED and repaired runtime NOT_EXECUTED; mentions/target pending | NO_GO |
| DM | Separate directory, composer, Thread, read/reconnect/error handling | Existing dedicated Direct endpoint and separately approved read-lifecycle repair | Named real provider/HTTP/transport controls and hosted Main suites | Current authorized participation/Tenant/account/Workspace scope; target matrix pending | Existing required P0 passed at candidate source; target pending | NO_GO |
| Files | Approved Dark Hub, authorized preview/download and denied states | Existing APIs; UI intake disabled | Named file/provider/HTTP controls passed at their recorded revisions | Scanner pipeline incomplete; target intake denial and complete mutation matrix pending | Synthetic owner passed; target pending | NO_GO |

Files is not declared globally read-only from a disabled upload button or one
intake flag. `FileSharing=false` locally denied intake with no persisted upload/file metadata or storage change in that control;
all other reachable modifying routes and the target policy still require review.
Only prevalidated synthetic samples may be used during isolated qualification.
The bounded 6ca local fact covers three existing byte-intake paths and retained
authorized reads; it does not close delete/sharing/metadata/association writes,
generated Audit ZIPs or readiness storage probes. Existing Announcement
attachment resolution also requires FileSharing and becomes unavailable while
false. Target captures and route execution remain mandatory.

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

Group DM G1–G3 v2 implementation/synthetic verification and Workspace Option 2
with qualification of existing authority were approved on 2026-10-20.
Do not request those decisions again. They preserve the approved existing
history and hierarchy-free membership behavior; Group threads/moderation and
ordinary Workspace member self-leave remain deferred. No release approval,
new schema, role, RLS or last-Owner/last-admin policy is implied.

Only these new or outstanding decisions remain:

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
| Workspace backend #1290 original hosted [39358672592](https://github.com/NYGsatoshi/Coglatas/actions/runs/39358672592) | Merge checkout `14f6ea65`: 2,543 executions, 2,417 passed and 126 failed. Of the failures, 124 were HTTP-host DI construction (25 Auth and 99 Tenant), and two were inventory counts (381 expected/383 actual). The `4cccb0cf` local 143-pass mixed-provider repair, later three native quality findings and current `8101e2a5` hosted success are separate records. |
| Workspace UI #1291 original `6f9259c6`, hosted [39358707109](https://github.com/NYGsatoshi/Coglatas/actions/runs/39358707109), job 118420515086 | Announcement publish-validation accessibility failed in both desktop/mobile profiles and their retries: the shared dialog content scrolled without a keyboard-focusable region. Successor `ab6fa6ea` local focused passes and later current worker checks do not erase that hosted failure or qualify the final combined source. |
| Group UI original overflow, c8 and qa-v9 browser selections | The 43-row predecessor passed Chromium keyboard checks but failed the roster UL axe check in two profiles. c8 adds named UL keyboard access; its fresh 22-case run retained one End-key timing failure (21 passed). qa-v9 retained one pass and three failures. Final `09e58d4a` mocked browser 24/24 success is a separate execution and does not erase these failures. |
| f4e licensed run `39366803248` | Six required cases passed/two failed/zero skips/retries: first Group recipient UI body and removed Workspace General status oracle. Neither later unread nor a complete Group lifecycle passed. Source-only repair does not erase it. |
| PR #1294 f4e SonarCloud Code Analysis | Six reliability failure annotations and forty warnings were retained. Five failures concern comparatorless helper sorting; one public parameter name mismatches its interface. Scoped source corrections require fresh native checks; warnings, rules and budgets are not waived. |
| Files intake local `6cc2ebc8` and `96c158b9` | Each executed one failed HTTP fact/zero skips: first fixture omitted Activated Project timestamp/version; second incorrectly read nullable canonical redaction into a nonnullable DTO before closure. Separate 6ca one-pass evidence preserves both failures. |
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
