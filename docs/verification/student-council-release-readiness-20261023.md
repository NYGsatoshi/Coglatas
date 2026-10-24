# Student Council Web release readiness — 2026-10-23

## Exact resulting-Main / release blocker checkpoint — 2026-10-25 JST

**Release: NO_GO / Owner HOLD BOTH.** This section supersedes the *current-state interpretation* of earlier checkpoints below, not their original source-bound results.

- [PR #1388](https://github.com/NYGsatoshi/Coglatas/pull/1388) was normally merged on Product Main as `cad010ef47e6563460a26929c8f307a32cfa62ff`. Its six-file current-scope Direct/Group creation guard prevents stale recipient and creation callbacks from rendering or navigating after authenticated Tenant/actor/Workspace changes. [Exact-head PR CI #40103508493](https://github.com/NYGsatoshi/Coglatas/actions/runs/40103508493) succeeded. This is a browser-side stale-response guard, **not** a server authorization or RLS change. Resulting [Main CI #40136984323](https://github.com/NYGsatoshi/Coglatas/actions/runs/40136984323) was **IN_PROGRESS at this checkpoint**; PR success is not new-Main success.
- The *preceding* exact Main `bf1e589dbaeacc6140dfe72e84a5bc01012ed467`, [Main CI #40103424066](https://github.com/NYGsatoshi/Coglatas/actions/runs/40103424066), attempt **1**, is **terminal FAILURE**, despite both full Qodana consumers, Fast/Full real-backend collaboration and licensed P0 browser lanes reporting source-specific success. [Extended Collaboration job #120902872078](https://github.com/NYGsatoshi/Coglatas/actions/runs/40103424066/job/120902872078) failed canonical `FUNC-MSG-001 / MSG-THREAD` at the unchanged composer value assertion `collaboration-owner-journey.spec.ts:248`: after native fill, the editable second-root draft remained empty through its 15-second deadline. The lane had **1 FAIL / 2 PASS / 0 SKIP / 0 test retries**; the [Extended aggregate #120903611190](https://github.com/NYGsatoshi/Coglatas/actions/runs/40103424066/job/120903611190) correctly failed. The canonical second-root send and all subsequent Thread denial steps were not reached on this failing execution. [Issue #1360](https://github.com/NYGsatoshi/Coglatas/issues/1360) retains this additional first-attempt recurrence and its artifact references. Never relabel this attempt as PASS because a different lane or source passed.
- **Causal reliability blockers:** #1360 intermittent pre-send empty input, [#1365](https://github.com/NYGsatoshi/Coglatas/issues/1365) distinct authorized Thread root intermittency after 200, [#1342](https://github.com/NYGsatoshi/Coglatas/issues/1342) restricted mobile retained-Thread activation, and [#1346](https://github.com/NYGsatoshi/Coglatas/issues/1346) ReadOnly roster keyboard/scroll first-attempt behavior. Held composer [PR #1361](https://github.com/NYGsatoshi/Coglatas/pull/1361) is a controlled **candidate**, not a demonstrated explanation of this hosted recurrence; it must not merge without its independent hold being lifted.
- **Security/release blockers:** approved concrete SEC-ARCH mapping and SQL roles/grants/policies and effective PostgreSQL RLS qualification; complete residual SEC-16 and [SEC-17 #690](https://github.com/NYGsatoshi/Coglatas/issues/690) role/scope/route and live subscription/revoke/reconnect coverage; approved stable private SSH/HTTPS target and source/image identity; strict restored-app, backup and rollback acceptance [#1355](https://github.com/NYGsatoshi/Coglatas/issues/1355); school privacy/operator permission, physical IME/device acceptance, and actual restricted HTTPS 17-flow pilot. Current operational scope and exact target must be independently qualified.
- **Hold preservation:** #1320, #1326, #1361, Draft physical recovery #1379 and review-only Files/Artifacts #1269–#1271 are not implicitly mergeable. Private Coglatas-Spec owner review artifacts must stay private. No deployment, school-user invitation, destructive data operation, backup-strategy switch, schema/RLS activation, security waiver or RELEASE_GO is authorized.

Next engineering order: establish a source-bound causal/deterministic Composer reproduction **without changing assertions/retries**, independently qualify the proposed repair and all initial-attempt Messaging owners; then clear remaining authorization/recovery/device controls. Actual staging/17-flow execution remains after the separately approved environment and SEC-ARCH gates.


## Current quality blocker: nested File privacy patterns — 2026-10-24 UTC

**RELEASE NO_GO / Owner HOLD BOTH.** Resulting Product Main
`4c4720f77226fa496136a79c17b0b4c3ac948669`, [CI #40076025574](https://github.com/NYGsatoshi/Coglatas/actions/runs/40076025574),
has an original first-attempt Qodana Cloud FAILURE: the fixed
`MergeIntoPattern` debt is exactly **132/132**, but newly surfaced
`MergeNestedPropertyPatterns` debt is **2/0** at
`FileSharingReadOnlyMutationHostedHttpTests.cs:71:37` and `:72:46`.
The original full SARIF artifact ID is `12553668214`, preserved with
the failed Main source. Other Main contexts require their own terminal
qualification; no passing PR context can override the failing one.

[Draft #1384](https://github.com/NYGsatoshi/Coglatas/pull/1384)
makes a two-line, one-test-file syntax-only conversion to C# extended
property patterns. Its exact-head CI and resulting Main Cloud and
Community are **PENDING**, not PASS. [Review candidate #1381](https://github.com/NYGsatoshi/Coglatas/pull/1381)
now at `72f4301ce606b768069ac2136cb3b9545aec3575` applies
the corresponding flat property-pattern style to two *added*
SEC-16 tests, retaining original negative/positive authorization assertions.
Those edits supersede `760335de...`, whose PR CI success cannot qualify
the new head. Neither quality change permits security baseline relaxation.

On Main4c, first-attempt Messaging Fast and Full collaboration
(including authorized Thread and denied-access cleanup), and 8/8 licensed
real-backend P0 cases passed; original intermittent defects remain OPEN.
SEC-ARCH/RLS formal approval, stable staging target authority, full SEC-16/17,
current strict recovery/rollback, physical IME and actual school/privacy and
public 17-flow pilot remain independent blockers.



## Current Main after Qodana correction — 2026-10-24 UTC

**RELEASE NO_GO / Owner HOLD BOTH**.
Main `48cf0b397122fb3cadcf18926a1891e7bbf80a5d` failed
[CI #40073017091](https://github.com/NYGsatoshi/Coglatas/actions/runs/40073017091)
at first attempt in both full Qodana consumers: `MergeIntoPattern` 135
versus unchanged ratchet 132. Narrow assertion-equivalent test-code PR
[#1382](https://github.com/NYGsatoshi/Coglatas/pull/1382) merged normally
to current Product Main **`4c4720f77226fa496136a79c17b0b4c3ac948669`**.
[Resulting-Main CI #40076025574](https://github.com/NYGsatoshi/Coglatas/actions/runs/40076025574)
is **NOT yet terminal-qualified** at this checkpoint. Full Cloud/Community
Qodana must both pass on this exact SHA; a prior PR PASS cannot qualify it.

| Candidate/owner | Exact-source observation | Unresolved gate |
| --- | --- | --- |
| Main4c Fast Collaboration [job 120820390086](https://github.com/NYGsatoshi/Coglatas/actions/runs/40076025574/job/120820390086) | `FUNC-MSG-001 PASS`, 6.5s, first attempt, FLAKY=0, retry=0, owner evidence COMPLETE | Prior #1360 composer first-failure cause still UNKNOWN |
| Main4c Full Collaboration [job 120820378807](https://github.com/NYGsatoshi/Coglatas/actions/runs/40076025574/job/120820378807) | `FUNC-MSG-001 PASS`, 14.0s, first attempt, FLAKY=0, retry=0; Thread and denied-read controls complete | Prior #1365 authorized Thread disappearance cause still UNKNOWN |
| Main4c licensed P0 [job 120820356467](https://github.com/NYGsatoshi/Coglatas/actions/runs/40076025574/job/120820356467) | Eight required tests / eight JUnit cases passed on actual Main | Not a public HTTPS or complete SEC-16/17 acceptance |
| Scoped SEC-16/17 [PR #1381](https://github.com/NYGsatoshi/Coglatas/pull/1381) | Original source `1342ce0...` had PR CI 16/16 SUCCESS and three named real PostgreSQL/HTTP/SignalR facts (16s/42s/16s); predecessor `04f436bb...` was superseded | Current HEAD `760335de...` includes two assertion-only quality amendments, separate source/CI/review required; no test credit transfers |
| Strict recovery [Draft #1379](https://github.com/NYGsatoshi/Coglatas/pull/1379) | Previous source synthetic physical recovery and exact-head redirect guard success are bounded historical facts | Formal recovery strategy, target HTTPS restore and rollback not approved |

All requested detailed SEC-ARCH Mapping/SQL Role/Grant/Policy/RLS activation
and staging SSH/stable HTTPS environment authorities remain unsigned.
Remaining test matrices, unreproduced first-attempt reliability failures,
actual external 17-flow pilot, school/privacy/operator permission, physical
IME and restoration/rollback retain independent NO_GO gates.
No real credentials, school files, active production target or security
policy was touched by this documentation update.



## Post-merge current-source validation — 2026-10-24 UTC

**Failed current-Main quality gate:** [CI #40073017091](https://github.com/NYGsatoshi/Coglatas/actions/runs/40073017091)
has a first-attempt **Qodana Cloud FAILURE** in project-model ratchet
(`MergeIntoPattern` 135 / frozen budget 132, three new findings).
Explicit SARIF cross-source comparison: original Main0c5=132,
Main0dd=133 (one File selection test conjunct), Main48cf=135
(two additional File sharing negative metadata conjuncts). No historical
failure has been waived. [Draft correction #1382](https://github.com/NYGsatoshi/Coglatas/pull/1382)
changes two existing test files only and is not yet independently qualified
or merged. All security baselines, required jobs and Owner HOLDs remain.


**Current Main** `48cf0b397122fb3cadcf18926a1891e7bbf80a5d`
is the normal merge of [#1380](https://github.com/NYGsatoshi/Coglatas/pull/1380).
Its pre-merge PR-head CI [#40069554136](https://github.com/NYGsatoshi/Coglatas/actions/runs/40069554136)
passed all 16 ordinary jobs. **Current resulting-Main
[#40073017091](https://github.com/NYGsatoshi/Coglatas/actions/runs/40073017091)
is not a terminal qualified SUCCESS at this checkpoint.**

| Exact Main test | Observed first-attempt evidence | Limit |
| --- | --- | --- |
| [Fast Collaboration #120811151987](https://github.com/NYGsatoshi/Coglatas/actions/runs/40073017091/job/120811151987) | `FUNC-MSG-001` passed in 8.1s; attempts=1, FLAKY=0, browser retry=0, owner evidence COMPLETE | First positive occurrence is not the initiating cause of historical #1360 |
| [Full Collaboration #120811156445](https://github.com/NYGsatoshi/Coglatas/actions/runs/40073017091/job/120811156445) | `FUNC-MSG-001` passed in 9.9s, attempts=1, FLAKY=0, browser retry=0; MSG-THREAD and MSG-THREAD-DENIAL completed | Intermittent authorized Thread loss #1365 remains causally UNKNOWN |
| [Licensed Real Backend Acceptance #120811125132](https://github.com/NYGsatoshi/Coglatas/actions/runs/40073017091/job/120811125132) | Eight required real backend tests passed, eight discovered JUnit cases, tested on this Main | Not the complete public staging, SEC-16/17, rollback or external pilot gate |
| [SEC-16/17 ordinary regression Draft #1381](https://github.com/NYGsatoshi/Coglatas/pull/1381) | Three source-identical PostgreSQL/HTTP/SignalR facts composed with original parent commit ancestry; new branch from Main48cf | Current integration source and named non-skipped runtime tests require their own CI and review; not merged |
| [Recovery alternative Draft #1379](https://github.com/NYGsatoshi/Coglatas/pull/1379) | HEAD `463708c947b652819de70dd9da9239761fbd1784`: PR CI 16/16 passed; local redirect guard 2/2 passed | Formal backup strategy and exact-target restore/rollback not approved; prior full synthetic execution is bound to an older source |

The [issue #1360](https://github.com/NYGsatoshi/Coglatas/issues/1360)
and [issue #1365](https://github.com/NYGsatoshi/Coglatas/issues/1365)
original first-failure evidence is retained. New success does not close either,
weaken first-attempt enforcement, or change the security/RLS baseline.

SEC-16 remaining role/route/placement/history applicability, full
[SEC-17 #690](https://github.com/NYGsatoshi/Coglatas/issues/690),
formal mapping and RLS, approved SSH/stable HTTPS target, current strict
recovery/rollback, student privacy and school operator conditions, physical
IME and public 17-flow validation remain independent blockers.
**Both Owner HOLDs / release NO_GO / no school-user exposure.**


## Engineering candidate checkpoint — 2026-10-24, post-#1373

**Source-bound status: VALIDATING / RELEASE NO_GO / Owner HOLD BOTH.**
This replaces neither previous failure evidence nor the two independent
Owner decisions. Product Main is currently
`0dd39000b38dd047e0a35513e2b7403d3bc5cd06` (normal Merge #1373).
A PR success is not a resulting-Main success or a target acceptance.

| Surface | Exact source and observed execution | Remaining release boundary |
| --- | --- | --- |
| Thread / Files / CI ordinary integration [#1380](https://github.com/NYGsatoshi/Coglatas/pull/1380) | HEAD `e9e8a45a5fe0f646372b5f08d099c74264dce121`; [PR CI 40069554136](https://github.com/NYGsatoshi/Coglatas/actions/runs/40069554136) **SUCCESS 16/16**. Browser lane: 411 PASS / 13 configured SKIP. Exact-head automated review requested; PR is Ready for Review, still not merged at this checkpoint | Separate required review and post-Merge Main CI; first-attempt hosted #1360/#1365 root causes unknown |
| Separate diagnostic acceptance [#1378](https://github.com/NYGsatoshi/Coglatas/pull/1378) | HEAD `a9fa3d923e8766254e8eb193246aee8dc45d2eb7`; ordinary [PR CI 40068531068](https://github.com/NYGsatoshi/Coglatas/actions/runs/40068531068) SUCCESS, protected [run 40068617000](https://github.com/NYGsatoshi/Coglatas/actions/runs/40068617000) SUCCESS with 8/8 required P0 browser cases verified in log | Distinct source includes a bounded diagnostic, deliberately review-only/non-merge; not acceptance for #1380/Main or public HTTPS |
| Recovery candidate [#1379](https://github.com/NYGsatoshi/Coglatas/pull/1379) | HEAD `463708c947b652819de70dd9da9239761fbd1784`; [PR CI 40071883520](https://github.com/NYGsatoshi/Coglatas/actions/runs/40071883520) SUCCESS 16/16; [HTTP redirect guard 40071883389](https://github.com/NYGsatoshi/Coglatas/actions/runs/40071883389) 2/2 PASS | Draft/non-merge physical backup alternative; historical full synthetic restore receipt belongs to earlier source; formal strategy, target/rollback/full matrix NOT_EXECUTED |
| SEC-16 [#1349](https://github.com/NYGsatoshi/Coglatas/issues/1349) / SEC-17 [#690](https://github.com/NYGsatoshi/Coglatas/issues/690) | Existing subset HTTP/File and real transport controls remain source-bound; held [#1320](https://github.com/NYGsatoshi/Coglatas/pull/1320), [#1326](https://github.com/NYGsatoshi/Coglatas/pull/1326) excluded from #1380 | Complete owner/route/lifecycle and realtime reconnect/authority matrix unqualified; Owner no-merge restrictions remain |
| SEC-ARCH / RLS + environment | Architecture-direction evidence and preparation documents only; no new immutable exact Policy/Role/Grant approval or approved stable HTTPS/SSH target reference identified | Formal mapping/RLS activation and actual restricted staging qualification blocked under separate Owner HOLDs |

**Operational disposition:** No Student Council public opening, real-school
account provisioning, unreviewed File intake, target SSH/deployment, schema
migration, policy activation, or backup strategy switch. Physical IME,
current-source full recovery, privacy/school approvals and the public external
17-flow test remain explicit missing evidence. Required CI and source-bound
P0 success do **not** automatically change RELEASE NO_GO.


## Main reliability follow-up — 2026-10-24, after Qodana integration

**Main SHA: `0c5f9baace186940f948758b1731fd3ec891c2f3`.**
This section supersedes Main identities and execution outcomes in earlier
2026-10-24 records without rewriting those original checkpoints.

| Area | Source-bound outcome | Remaining blocker |
| --- | --- | --- |
| [Qodana-only #1363](https://github.com/NYGsatoshi/Coglatas/pull/1363) | Merged. The exact one-test-file `UseCollectionExpression` rewrite was normal-Merged with six required PR contexts passing. [Main CI #40056196940](https://github.com/NYGsatoshi/Coglatas/actions/runs/40056196940): both **Qodana Cloud** and **Qodana Community** PASS, and Fast Functional PASS | No further Qodana ratchet issue demonstrated on this Main; full Main remains FAILURE for unrelated reason |
| Full collaboration, original attempt | [job #120760727090](https://github.com/NYGsatoshi/Coglatas/actions/runs/40056196940/job/120760727090): `FUNC-MSG-001` FAILED at `MSG-THREAD-DENIAL` before restricted actor. Selected current authenticated GET was HTTP 200; a second 200 was recorded. Six DTO root/identity/count checks passed, but Thread panel/root was absent at the unchanged 15s timeout | **P0 OPEN [#1365](https://github.com/NYGsatoshi/Coglatas/issues/1365)**. No security-denial or release-pass inference. Sanitized diagnostic artifact 12545438209 is preserved |
| Full collaboration, same SHA job-only rerun | [job #120773901268](https://github.com/NYGsatoshi/Coglatas/actions/runs/40056196940/job/120773901268) SUCCESS: 3/3 owners PASS; no test retries/skips within this rerun | Intermittency only. Attempt-1 FULL / aggregate failure and issue OPEN remain. Do not launder the original result |
| [Thread race probe #1366](https://github.com/NYGsatoshi/Coglatas/pull/1366) | Draft `df7f6308438bb1493f5b6f1fb1621599dfc6bdbd`; [PR CI #40061370815](https://github.com/NYGsatoshi/Coglatas/actions/runs/40061370815) **16/16 PASS**, Angular 1,737/1,737. Narrow mocked-HTTP same-scope overlapping Thread GET/catch-up control requires new authoritative GET and visible mounted root | Test-only bounded mechanism, NOT the hosted cause/fix, not a real-backend acceptance waiver |
| [Composer candidate #1361](https://github.com/NYGsatoshi/Coglatas/pull/1361) | Held Draft HEAD `d55f679cac19b422f6f0c410d79d8cc3699a4938`; [PR CI #40056059298](https://github.com/NYGsatoshi/Coglatas/actions/runs/40056059298) **16/16 PASS**, CodeRabbit's DM verification-message comment resolved | Neither #1360 hosted cause nor real-backend deployment boundary cleared; explicit Owner NO_MERGE remains |
| [Docs review #1364](https://github.com/NYGsatoshi/Coglatas/pull/1364) | This additive public source-only Owner handoff | No environment, security policy, actual approval or target access in this PR |

The external `upload-artifact` transient failure in the **preceding Main**
[CI #40041324753](https://github.com/NYGsatoshi/Coglatas/actions/runs/40041324753)
was remedied only in its single-job attempt 2, with **metadata artifacts
successfully uploaded**. That does not erase attempt-1 failure, qualify
future external storage reliability, or permit diagnostics to be skipped.

Existing release blockers are unchanged: unapproved concrete SEC-ARCH
mapping/SQL Role/GRANT/Policy and RLS activation, approved isolated
SSH/stable HTTPS target identity, strict backup/restored-app/rollback
[#1355](https://github.com/NYGsatoshi/Coglatas/issues/1355), residual
SEC-16/17 [#1349](https://github.com/NYGsatoshi/Coglatas/issues/1349) /
[#690](https://github.com/NYGsatoshi/Coglatas/issues/690), historical
mobile/keyboard [#1342](https://github.com/NYGsatoshi/Coglatas/issues/1342) /
[#1346](https://github.com/NYGsatoshi/Coglatas/issues/1346), and both
held [#1320](https://github.com/NYGsatoshi/Coglatas/pull/1320) and
[#1326](https://github.com/NYGsatoshi/Coglatas/pull/1326).
**Owner Hold Both / RELEASE NO_GO**.

## Execution checkpoint — 2026-10-24

**Status: VALIDATING / BLOCKED_BY_OWNER_AUTHORIZATION; release NO_GO.**
This is a source-bound additive handoff. It supersedes stale Main/PR identities
in older checkpoints, but does not rewrite the corresponding historical evidence.
It does not authorize Merge of held candidates, RLS activation, provisioning,
migration, deployment, use of school data, or an actual release.

| Boundary | Reviewed source / evidence | Decision |
| --- | --- | --- |
| Product Main | `2ec94d76dc8dfcc7d6b34ff2245651e720f81329` | Main is installed source; no full release qualification |
| Private Spec Main | `6a0491c925b4980ce737dfbdf775816c237e44fe` | Architecture direction only, **not** exact SQL, formal mapping, RLS activation |
| [Main CI #40041324753](https://github.com/NYGsatoshi/Coglatas/actions/runs/40041324753) | **FAILURE**: both full Qodana consumers reject `UseCollectionExpression` 38 vs baseline 37; Fast Files owner passed but its required validated structural artifact upload failed with external `The server is busy` | Blocked; no green Main claim. Evidence failure separately recorded under [#611](https://github.com/NYGsatoshi/Coglatas/issues/611) |
| [Main mobile #40041639428](https://github.com/NYGsatoshi/Coglatas/actions/runs/40041639428) | `suite=mobile` success: four required real-backend owners PASS, no skipped or retried journeys | **Not** the regular protected eight-case P0 or public HTTPS gate |
| [Pilot-01 #1320](https://github.com/NYGsatoshi/Coglatas/pull/1320) | HEAD `a5470fa7c26820615322683b29df3d88eb3229c6`; [PR CI #40043103565](https://github.com/NYGsatoshi/Coglatas/actions/runs/40043103565) 16/16 SUCCESS | Explicit Owner **NO_MERGE** hold; earlier protected P0 successes are retained only at their tested SHAs |
| [SEC-17 #1326](https://github.com/NYGsatoshi/Coglatas/pull/1326) | HEAD `9408614e3f3d3e6b573a596f0a348eddc0fdaa23`; [PR CI #40043120104](https://github.com/NYGsatoshi/Coglatas/actions/runs/40043120104) 16/16 SUCCESS | Owner **NO_MERGE** hold; [#690](https://github.com/NYGsatoshi/Coglatas/issues/690) broader acceptance OPEN |
| [Composer #1361](https://github.com/NYGsatoshi/Coglatas/pull/1361) | Held Draft HEAD `cb81d02db49ee8c7128fba02e1655c5e3a2a255d`; [PR CI #40045776008](https://github.com/NYGsatoshi/Coglatas/actions/runs/40045776008) 16/16 SUCCESS | Controlled native UI/mocked transport mechanism; no definitive hosted causal reproduction, real-candidate P0 or release approval. [#1360](https://github.com/NYGsatoshi/Coglatas/issues/1360) still OPEN |
| Narrow Qodana repair [#1363](https://github.com/NYGsatoshi/Coglatas/pull/1363) | Isolated one-file/two-line collection-expression equivalent at `635ee9d0af50e260912c5861a5ec504f57f731fa`; same file blob as held #1361 | Draft and PR CI pending; **full resulting Main Qodana** and owner-protected CI must run before Main GREEN |
| Security / reliability | [#1349](https://github.com/NYGsatoshi/Coglatas/issues/1349), [#614](https://github.com/NYGsatoshi/Coglatas/issues/614), [#1342](https://github.com/NYGsatoshi/Coglatas/issues/1342), [#1346](https://github.com/NYGsatoshi/Coglatas/issues/1346) | Residual full File/SignalR/security and first-attempt mobile/keyboard gate remains open |
| Synthetic restore / rollback | [#1355](https://github.com/NYGsatoshi/Coglatas/issues/1355) | Strict PostgreSQL 18.6 restore/schema/data and same-artifact rollback **not qualified** |

### Owner HOLD 1 — exact authorized Staging destination

The [immutable reviewed environment questionnaire](p4-isolated-environment-owner-packet-20261018.md)
and [manifest](assets/p4-isolated-environment-review-20261018.json) still show
`remoteSshAlias`, `publicHttpsOrigin`, `syntheticFixtureReceipt`,
`storageIsolationReceipt`, and `approval` as **null**.
The protected workflow
[public-https-golden-path.yml](../../.github/workflows/public-https-golden-path.yml)
names the required `public-https-gate` environment and secret **names**, but
neither their existence nor permission to contact a remote host or use a stable
HTTPS origin is established by the workflow. No provided, verified approved
SSH alias, host identity, fixed HTTPS origin, isolated database/keys/files
ownership, environment authorization receipt, or immutable deployed SHA/image
was found in the reviewed approval references. **Do not guess, connect, seed,
provision, expose, or redeploy the historical Sakura target.**

### Owner HOLD 2 — exact SEC-ARCH and RLS approvals

At immutable private Spec revision
`6a0491c925b4980ce737dfbdf775816c237e44fe`,
[SA8-02 v45 owner direction](https://github.com/NYGsatoshi/Coglatas-Spec/blob/6a0491c925b4980ce737dfbdf775816c237e44fe/docs/specs/coglatas-core-v4/17-governance/decisions/2026-10-14-sec-arch-sa8-02-owner-direction-v45/sa8-02-owner-architecture-v45.review.json)
records only high-level Option A architecture direction. It explicitly lists
`AP01/SA02_EXACT_CANONICAL_MAP`,
`SA8-02_EXACT_ROLE_GRANT_POLICY_PROOF` and
`SA8-04_PRODUCT_RLS_ACTIVATION` as `approved: null` with
`exactCandidate: null`. See [RLS inventory #1156](https://github.com/NYGsatoshi/Coglatas/issues/1156),
[migration #1157](https://github.com/NYGsatoshi/Coglatas/issues/1157),
[nonowner positive/negative proof #1158](https://github.com/NYGsatoshi/Coglatas/issues/1158).
Draft contracts, digest/source pins and old broad directions **do not**
constitute exact owner-approved Mapping/SQL/Role/GRANT/policy/activation or
permit unapproved product migration. The prior **Hold Both** decision remains
in force. Do not silently re-request it or choose a target/policy on behalf
of the Owner.

### Execution order without expanding authorization

1. Review narrow Qodana [#1363](https://github.com/NYGsatoshi/Coglatas/pull/1363) and prove exact-head CI; retain held #1361 separately. A future **authorized** Merge must pass resulting Main *full* Qodana; no ratchet increase or gate weakening.
2. Diagnose/retain the independent `upload-artifact` storage-busy incident under #611. Require a successful authenticated, validated metadata-only artifact publication in a fresh Main Fast Files execution; no skip or `continue-on-error`.
3. Independently review composer #1361; qualify actual hosted real-backend authority, route transitions and physical IME in synthetic scope. Preserve #1360 and earlier reliability failures until their cause or formally authorized disposition is established.
4. Advance non-privileged SEC-16/17 traceability, isolated nonowner RLS test proposal, clean synthetic restore and rollback diagnosis. No concrete SQL/role/policy activation without the separately immutable approved exact subject.
5. Keep Staging target access and detailed RLS approval tracks at **HOLD**; only after explicit complete authorization execute protected HTTPS acceptance, synthetic restore and operational gates. No real user admission without distinct Owner release GO.

**Readiness conclusion: NO_GO.** No recorded success or planning document
overrides an unqualified Owner safety gate.

## Current integration checkpoint — 2026-10-23

Current Status: **VALIDATING**. Release decision: **OWNER_DECISION_REQUIRED / NO_GO**.
This checkpoint supersedes the dated historical inventory below where identities
or outcomes changed. It does not lift any approval, intake or deployment hold.

- Main is `4ae574ab567f003b91ce2e467f0fd3681880b918`, normal merge of
  [#1333](https://github.com/NYGsatoshi/Coglatas/pull/1333), including source PRs
  #1327, #1329, #1330, #1331 and #1332. Its tree equals reviewed candidate
  `8ac4f562e64dfec264d4af6b6f59d8092f728259`; no held PR was included.
- [Main CI 39941859233](https://github.com/NYGsatoshi/Coglatas/actions/runs/39941859233)
  completed SUCCESS. Independent audits bind backend, licensed acceptance,
  security, structural and frontend results to this exact Main.
- Backend: 2,595/2,595 cases and architecture 12/12 passed with TRX verification,
  real PostgreSQL 18.6, four HTTP grant lifecycle facts and fourteen broader
  SignalR/Event methods. Nine diagnostic privacy controls and the live probe pass;
  retained teardown logs contain zero failing-row details, statements or protected
  probe canaries while preserving the safe primary-error positive control.
- Licensed acceptance: P0 eight required cases, FCI-04/05 owners, My Tasks,
  authorization and MBJ session/invite/restart controls passed. Four actual image
  continuity receipts match the Main producer; this proves continuity, not signing.
  Success-path log/verifier accounting is distinct from unavailable raw JUnit.
- Frontend: 1,730 unit cases in 153 files and 411 browser cases passed. Thirteen
  existing skips remain explicitly accounted for: two retired non-P0 placeholders
  and eleven viewport exclusions with all active counterparts passing. These
  mocked/static results do not qualify real-backend mobile or physical IME.
- Desktop WebKit attempt 1 was cancelled during dependency installation before
  any tests ran. Only that job was retried; attempt 2 job `120388757482` passed
  four expected cases, zero failures/errors/skips. Independently downloaded
  artifact `12499941424` matches SHA-256
  `6e14540b274e2ec3a4e2bc5528b68dd3e65eb544e0874413b69b6296f0f06aa5`.
  Other successful jobs retain their attempt-1 execution identities.
- Structural Small and Medium each passed nine scenarios, 165 HTTP captures and
  112 validators; aggregate 28/28 invariants passed. Numerical performance
  assurance remains suspended and is not supplied by these structural checks.
- Security HTTP controls, five-role bounded Main schema exercise, authenticated
  ZAP and configured dependency/secret scans passed within their declared scope.
  They do not resolve the complete open scanning backlog or approve SEC-ARCH.

| Candidate / requirement | Latest source and evidence | Remaining condition / status |
| --- | --- | --- |
| #1334 / selected SEC-16 sharing lifecycle | `75c40d48fd0bf844161c840458f0c50a85537a63`; PR CI 39945361039 passes 443 scoped PostgreSQL cases, including six lifecycle facts; exact-source local six-case TRX and independent review pass | Unmerged; future resulting-Main evidence required. Full SEC-16 remains incomplete |
| #1335 / reproducible desktop WebKit provisioning | `9a7eebd631347422d0b33d90a70c969477be79fa`; independent source review and 24 Linux controls pass; actual standard producer at test merge `a2e5347469420c5463831e4314e29a27dff93c28` passes four static cases without skips/retries; real SIGTERM exits 143 and removes only the owned container; PR CI 39949046741 SUCCESS | Review-ready, unmerged; standard unlicensed/static scope only. Resulting-Main qualification remains separate |
| #1336 / real-backend mobile profile | Current `689d0a29244425e370e4a328e79fa57db98d1542` corrects the new test's Thread reload to the existing two-parameter route contract, validates exact reply identity and preserves the primary failure diagnostic; eight focused controls pass | Draft; current-source review and runtime pending. Both earlier runs FAILED. Replay 39953523941 at `2c44deedacf45a8f0760be1c719049ab3bbeb6d5` has three complete passing owners, zero retries and no permission errors, but channel failure means no suite PASS. Initial `b1a477afdffc301899de1b933d8b51efb423ad08` run 39951320069 remains FAIL with unknown browser cause. API-prepared Files fixture is not mobile UI upload |
| #1337 / selected SEC-16 File deletion | `c5c49abf107ec68179ba045afc5dacc3699b283b`, stacked on #1334; exact clean eight-fact HTTP/PostgreSQL replay passes without skips; independent source, loaded DLL identity, TRX and cleanup audit pass. Hosted CI 39952303076 tests merge `e9496e5cc724246539d9e54fabb975cca6aae042`, passing 445 scoped cases with both deletion facts and TRX verification | Review-ready, unmerged; resulting-Main evidence pending. Initial incorrect audit-reason expectation and dirty-wrapper qualification failure remain separately retained |
| #1338 / SEC-16 traceability and finding processor | `9ef4106b08909ccfc912b76b9af2a9c3f762e570`; nineteen exact-source native controls and nineteen independent pinned-Linux controls pass; source/evidence review approved | Unmerged; current hosted/resulting-Main checks remain separate. These controls expose the missing #582 policy rather than fixing or accepting it |
| #1320 / held Pilot-01 | New head `b53ff3e3cdb831a0b5c3e2f06491c1314fa8d4a5` merges Main; PR CI 39944599234 passes | No-merge hold retained; protected 8/8 result 39933087203 belongs to prior head `3327194ac0429b0ae17a78ec83e94cf9599a1006`, not this merge |
| #1326 / held SEC-17 | New head `4ea7d80787c8d96e31a6c4591e708dae72225432`; CI 39944613051 tests merge `9f8ee55960b8c52ba696a1be307ecda744126453`, 442/442 and named private-channel WebSocket test pass | No-merge hold retained; #690 full catalog remains incomplete |
| SEC-16 residual tracking | #1328 was closed/completed at 10:50 UTC; its residual checklist has no complete acceptance receipt | Closure metadata is preserved. Unmet #689 criteria remain release gates; neither #1329 nor #1334 completes them |

The Owner's **Hold Both** keeps target access/deployment and detailed formal
mapping/RLS policy/activation approval on HOLD. RLS is not activated or approved
by successful application-layer tests. Full HTTPS staging, key/login/file restore,
rollback, physical IME, latest approved Penpot revision and other original matrix
requirements remain unqualified. No real users, invitations, public access or
release GO are authorized.

File intake follows the existing
[pilot scope](student-council-pilot-scope.md),
[release gate](student-council-release-gate.md),
[preview boundary](student-council-files-preview.md), and
[FCI-05 owner contract](functional-ci/fci05-files-owner.md).
Those records preserve disabled arbitrary intake; they do not approve a safe
scanner/admission policy. Issue #604 concerns runtime credentials and is not the
normative intake-policy reference.

The existing SEC-06 ZAP implementation blocks High findings and reports Medium;
this does not satisfy #582's required new-unapproved-Medium ratchet, bounded
expiring suppression and common normalized finding model. Existing scanner
success is not acceptance of that incomplete policy requirement. The independent
scenario/lane and normalized-failure reconciliation is in progress; no exception
or policy change has been approved here.

The Penpot connector was rechecked during this update and reported no connected
Penpot instance. The latest approved revision remains unverified; historical
Rev 442 evidence is not relabeled as current approval.

Fresh isolated PostgreSQL 18.6 diagnosis reproduced the logical restore mismatch:
the dump's outer array cast is reparsed into per-element casts, changing stored
CHECK/partial-index expression form. Custom restore and plain SQL replay both
retain strict source/restored schema comparison failure; the second logical
roundtrip is stable. Four isolated databases pass equal row manifests, actual
constraint rejection and valid-write controls. Those bounded controls do not
override the strict failure or qualify application/target restore. The original
diagnostic with missing rows for a UNIQUE control also remains retained. No backup
strategy, schema equality oracle or security approval was changed.

## SEC-17 acceptance reconciliation at Main 4ae574ab

This public source/evidence inventory is separate from private formal SEC-ARCH
mapping. The implemented endpoint is `/hubs/app` (`Program.cs`, `AppHub`). Its
server-authorized subscription classes are User, Tenant, Workspace, Conversation
and Project; three resource unsubscribe methods affect the calling connection.
`RealtimeEventCatalog` declares fifteen event types with payload schema 1.
`HubSubscriptionAuthorizer` and `RealtimeDispatchAuthorizer` own current authority;
`OutboxDispatcher` validates the envelope/catalog and reauthorizes delivery.
No internal group names, routing keys or protected payloads are reproduced here.

[Main backend job 120376665722](https://github.com/NYGsatoshi/Coglatas/actions/runs/39941859233/job/120376665722)
explicitly records all fourteen methods in `SecurityArchitectureSignalRTests`
and `SecurityArchitectureSignalREventTests`, plus the two Group/Workspace transport
methods below, passing on exact Main `4ae574ab567f003b91ce2e467f0fd3681880b918`.
This named-case accounting supplements the separately verified full 2,595-case
TRX total; it does not infer execution solely from that total.

| Original #690 criterion | Current source/control and observed evidence | Residual / status |
| --- | --- | --- |
| Current Hub/event inventory | Public endpoint, five subscription classes and fifteen declared event types above; `EveryDeclaredEventHasLiveTenantAndCurrentMembershipControls` and `CurrentResourceReadChangesPreventEveryApplicableCatalogueDeliveryAndRestore` pass | Selected current catalog is exercised. Complete applicability/formal architecture mapping remains distinct and held |
| Real connect/subscription authorization | `ProductTransportRejectsUnapprovedOriginsWithAuthenticatedLiveControls`, `ProductTransportApprovedOriginRetainsSessionAndResourceAuthorization`, `ProductTransportRejectsForeignSubscriptionsAndDeliveryWithLiveControls` pass; authenticated live peers accompany anonymous/origin/subscription denials | Bounded real WebSocket/backend evidence PASS; target HTTPS/session configuration unqualified |
| Blocking cross-scope non-delivery | The catalog controls, `SameTenantHiddenResourcesRejectSubscriptionAndDeliveryWithLivePeers`, and `ProjectAndWorkspaceUnsubscriptionOnlyRemovesCallingConnection` pass with live authorized delivery controls | Representative Tenant/resource/multi-client boundaries PASS; no zero-event-only isolation claim |
| Revoke/downgrade/reconnect current authority | Named methods for revoked Conversation replay, Workspace-scoped Direct delayed replay/reconnect, session invalidation, session expiry, role downgrade, HTTP catch-up and Tenant-cookie switch all pass | Bounded controls PASS; held #1326 adds the named private-channel invitation sequence outside Main. Concurrent/churn expansion remains incomplete |
| Protected payload/routing/auth artifact minimization | `SecurityArchitectureSignalRControlRecorder` emits logical control categories and bounded observed outcomes; diagnostic privacy probe and current Main hygiene controls pass | Selected evidence boundary verified; complete success/failure artifact inventory is not implied |
| PR/Main/deep separation | PR scoped backend selection is distinct from full Main backend execution. `GroupBusinessDeliveryReconnectAndReplayUseCurrentWorkspaceAndParticipantAuthority` and `CanonicalWorkspaceChannelDeliversToCurrentPeersAndReauthorizesReconnectAndReplay` both explicitly pass on Main | No complete scheduled/manual long churn, concurrent transition and regression-replay matrix receipt exists; IN_PROGRESS |
| Finding connection to #582 | Assertions block failures directly; current normalized scanner path is separately reconciled in the SEC-16 document | Common security-finding linkage and new-Medium ratchet remain BLOCKED; direct assertions are not that integration |
| Capacity responsibility separate from #619 | These tests assert authorization and event delivery, not load/capacity certification | Capacity belongs to its separate approved contract; no capacity assurance claimed |

#690 remains open. Neither these sixteen named passing methods nor the held
private-channel subset constitutes all acceptance criteria, approval to merge
#1326, RLS approval or release readiness.

## Historical inventory — 10:03 UTC

Current Status: **IMPLEMENTING / VALIDATING; release NO_GO**.
Snapshot: 2026-10-23, 10:03 UTC (19:03 JST). Later candidates require their own
source-bound updates; this report does not predict their outcomes.
This report is an evidence inventory, not a declaration of RELEASE_READY.
Its mission-local identifiers below do not introduce normative SPEC IDs.
No school accounts, real data, public ingress, or production environment were used.

## Reviewed test/CI integration candidate

The integration branch combines these independently reviewed source slices by
normal Git merges from Main `babcf80f7babafe4087fb7cb5742edb2b98868c2`:

| Source PR | Integrated head | Qualification and remaining integration work |
| --- | --- | --- |
| #1327 | `63357fa5cbb9492f721dcd8837202f0176bb18e1` | Hosted backend 2,591/2,591, architecture 12/12 and privacy probe pass; observed raw failing-row/statement lines are zero. Its separate Functional failure remains retained |
| #1329 | `9fdf5194009ce31b7dd9e78c7527ca8754e4936e` | Current test merge `73fd68ded149da31936d0da1c08c83ba27e3e2fc` passes 441 scoped provider cases, including all four new HTTP lifecycle facts; ReSharper zero new warnings. External Medium fingerprint failure remains retained |
| #1331 | `3f1f9ba3d4ede62d4c2622eee917529348706dcc` | Independently reviewed fresh restricted-request correlation; four deterministic controls pass. Real Functional owner must prove strict 400 and UI clearing on the combined candidate |
| #1332 | `122519ebd4aec8d3ab7cc90df53f1d08d8e5432b` | Independently reviewed explicit tooling preparation; 279 Linux controls, validators and cached real probes pass. Both structural profiles require fresh execution on the combined candidate |
| #1330 | `07ac1cea9bb00f12247befcfc1a24eea4c3e93c1` | Independently reviewed evidence inventory and explicit Owner holds; this integration checkpoint updates that historical snapshot |

The combined candidate has no product authorization, API, schema, migration,
dependency, image-version, numerical-threshold or public-deployment changes.
#1320/#1326 and all other held candidates are not integrated. Their historical
merge holds remain in force. Source-PR failures are not converted into passes;
the combined source needs complete current-source checks, resolved reviews and
independent review before any normal merge, followed by fresh resulting-Main
qualification. All source review threads were checked and none were unresolved
when the integration was assembled; recheck before merging.

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
| FILE-INTAKE / P0 | Owner/Security | Current pilot explicitly enforces FileSharing=false; arbitrary unscanned intake stays disabled | Mission requires upload; approved safe intake/scan policy and scope reconciliation required | Existing pilot scope, release gate, preview boundary and FCI-05 owner contract | OWNER_DECISION_REQUIRED |
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
