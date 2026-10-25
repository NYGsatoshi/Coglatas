# Student Council source-bound readiness — 2026-10-26 JST

**RELEASE_NO_GO.** This record describes verified source and execution, not
Owner approval or permission to deploy, activate RLS, expose an endpoint, invite
students or process school data. The current machine-readable snapshot is
[student-council-readiness.json](student-council-readiness.json). Earlier
checkpoints retain their original SHAs, attempts and failures.

## Refreshed baseline

Product Main is `f31b15f2b42d3b83b5c1ffae5ea304922e10cc16`.
Its [Main CI 40193736229](https://github.com/NYGsatoshi/Coglatas/actions/runs/40193736229)
is completed **FAILURE on attempt 1**: all 41 jobs enumerated, 36 successful,
three configured skips, two failures, no cancellations and no diagnostic rerun.
The originating failure is
[Extended Collaboration 121172675286](https://github.com/NYGsatoshi/Coglatas/actions/runs/40193736229/job/121172675286);
the other failure is its required Extended aggregate. Fast and Full
Collaboration, Main Security and full Qodana success are independent evidence;
they cannot waive Extended. Separate exact-Main Publication Readiness and
CodeQL workflows succeeded. The previous Docker 503 belongs to Main
`2bed0036df645d254cc4a75882325c188d8a03cb`; it is not the initiating failure
of the current run.

Specification Main was independently refreshed as
`6a0491c925b4980ce737dfbdf775816c237e44fe`. Relevant Messaging and school
operations requirements were read in the authorized private repository. No
private architecture or normative text is reproduced here, and Draft private
security mappings are not treated as approved contracts.

The active Main ruleset requires `build-test`, `frontend-test`, `security-scan`,
`publication-readiness`, `functional-fast` and `performance-fast`, a current
merge base and resolved review threads. It requires zero formal GitHub approving
reviews. This does not waive independent engineering review, held-candidate
Owner decisions, Full/Extended qualification or separate release gates.

## Thread: immutable failure and bounded controls

The actual first-attempt owner ran `FUNC-MSG-001` once, with browser retry zero,
and timed out at its unchanged 120-second deadline in `MSG-THREAD-DENIAL` while
waiting for a distinct fresh Thread request. The fresh denial response and
subsequent protected-state assertions were not reached.

GitHub artifact `12599865031` was independently downloaded and verified:
ZIP SHA-256
`c4e2f7447ba8326cda9d0338d057806d9a955ebf58141747e4f58735681ef2a0`
matches the upload digest. Its strict structural receipt records this order:

1. Selected authorized GET 1 returns 200; six DTO consistency flags are true.
2. GETs 2 and 3 start during cookie-session replacement, before the later tab action.
3. Session replacement and verification complete; GET 2 returns 200.
4. The fresh-request observer registers and records `ACTION_STARTED`.
5. Previously started GET 3 returns 400. No distinct new GET is recorded.
6. The owner times out; failure DOM/target sampling is unavailable and the
   secondary `RESTORE` failure is retained.

All 19 bounded observer records are present without truncation/orphans, but
`freshRequest=null` and classification is `INCOMPLETE`. Initial tab actionability
booleans do not prove eventual native click-handler dispatch. Neither the older
400 nor an earlier 200 proves application of a current authorized projection.
**Historical initiating cause remains UNKNOWN; #1365 and #1342 stay open.**

Test-only [Draft #1404](https://github.com/NYGsatoshi/Coglatas/pull/1404) now
contains six controls at `26ec10a19f67b55da3b4039e255ad1c9f922e33d`:
old denial before/after new selection; old denial before/after a successful
new response; and actual Angular tab-listener behavior before/after view
destruction. Clean lockfile dependencies, Vitest 5.0.5 and the owning unit
selection passed **89/89**. A separate agent independently reviewed the delta
without a blocking finding. Programmatic activation of a disconnected DOM node
is a controlled lifecycle observation, not a real-browser reproduction of the
hosted native-click failure. Current-head PR CI is tracked independently;
the earlier `ca2183d8...` head's 16/16 success does not transfer. Draft/no-merge
restrictions remain.

## Composer independent review

Held [#1361](https://github.com/NYGsatoshi/Coglatas/pull/1361) remains Draft at
`3de9eb782cdbc1f795f8c10ceebab4cf4b73ab10`. An independent reviewer reconciled
[PR CI 40174839257](https://github.com/NYGsatoshi/Coglatas/actions/runs/40174839257)
attempt 1, all 16 jobs successful, actual tested merge
`411c75c2d79f5ba6d6ecc50b0ebd8906bf9e7637`, identical candidate tree,
1,777 frontend tests and the real canonical Collaboration owner. Backend unit
execution/TRX was unselected, so no earlier backend count is inherited.

The controlled RED comparator #1402 and GREEN #1361 have the identical canonical
owner blob. The original product disconnected the Composer during deliberately
held authorized catch-up; the candidate retained its local input node, kept
Send disabled until fresh authority and then restored sending. Current scope,
actor/Tenant/Workspace/Conversation, stale-binding, denial/ReadOnly and synthetic
IME guards were reviewed with no new blocking finding. Separate local 62/62
controls used preinstalled Vitest 5.0.3 and remain diagnostic. These observations
do not establish the initiating trigger of every historical empty-fill failure,
qualify physical Japanese IME, or lift **OWNER_HOLD**. #1360 remains open.

## Files implementation and SEC-17 verification

[Draft Files #1405](https://github.com/NYGsatoshi/Coglatas/pull/1405),
`076362738f6aa4b3b8eee66bccbec307b13a48fc`, corrects two demonstrated client
operation-lifetime defects: a refreshed idle row no longer permits duplicate
grant dispatch while its download is live, and explicit page reauthorization
cancels the parent operation and releases ownership. Independently repeated
clean-dependency RED was 23 PASS / four expected FAIL; final owning GREEN was
29/29 PASS, no skips/retries. Prior broad Files 169/169 and build evidence retain
their own source attribution. See the candidate's
`docs/verification/pilot-files-download-operation-lifetime-20261026.md`.
Exact-head CI and resulting Main qualification remain separate. No upload gate,
server grant/byte authorization, audit, API or schema is changed.

Private-channel tests [#1399](https://github.com/NYGsatoshi/Coglatas/pull/1399)
at `758d722aa56fe724a498dbb47a2a2ba09a12fb2d` and
[#1326](https://github.com/NYGsatoshi/Coglatas/pull/1326) at
`2bdce7581349037d9a96ba784239515d70773332` retain successful source-bound PR
checks but remain **OWNER_HOLD**, including non-Draft #1326. New isolated
Thread/reply/replay/reinvitation [Draft #1406](https://github.com/NYGsatoshi/Coglatas/pull/1406)
is review-only at `172925c7d831013967a8fe6c988e3948b6bea658`. Its test-bearing
source `e4fbed38400794ac1d489e0b5b327612d2e069d8` executed the new fact
(20.57 seconds) and existing canonical control (14.29 seconds), **2/2 PASS**,
zero skips/failures on PostgreSQL 18.6 with SDK 10.0.402. An independent reviewer
matched the actual TRX digest
`727ab20f9dd2e8b7b8c5cf4e61fb60ece6fac1d298da446af2a30b63879c6e6d`,
the eight-observation artifact and loaded assembly digests. The original test
trial failed because a helper incorrectly expected a reply in root-only history;
that failure remains attributed to its own source. Hosted qualification is
pending. Settled synthetic
revocation and repository replay cannot establish the full SEC-17 concurrency,
operator API or security-contract matrix. #690 stays open.

## Dependency queue and remaining decisions

| Track | Next dependency | Disposition |
| --- | --- | --- |
| Thread #1365/#1342 | Exact-head controls; additional bounded actual interleaving evidence under held #1367 | REVIEW_ONLY; historical cause UNKNOWN |
| Composer #1360/#1361 | Explicit Owner decision on independently reviewed held candidate | OWNER_HOLD |
| Files #1405 | Exact-head CI/review; eligible normal merge followed by independent Main qualification | IMPLEMENTED_NOT_VERIFIED |
| SEC-17 #690 | New real transport execution/review/CI; complete residual catalog and formal approval | REVIEW_ONLY; held parents unmerged |
| School pilot | Formal security/RLS, exact target, recovery, devices and school admission | BLOCKED / RELEASE_NO_GO |

Existing Workspace switching clears registered protected state before selecting
the replacement scope and performs fresh HTTP authorization. Separate Teams and
DM navigation, current scoped Channel APIs, Thread HTTP authority and Files
grant/byte reauthorization exist in source. This is not full G01 acceptance.
The Files upload chooser is deliberately unavailable until intake/scanner/privacy
qualification; it must not be enabled as a routine UI fix. #1391 already has
merged click/catch-up work and positive real-stack evidence; #1346 lacks the
equivalent initiating-failure chronology needed for a causal correction. Neither
historical issue is closed from a later green control.

The consolidated Owner packet remains: review held Composer/diagnostic/transport/
cross-session/recovery candidates individually; approve exact private formal
security mappings, roles/grants/policies and activation separately; identify
the authorized Staging/HTTPS target and operator; approve the recovery strategy;
then supply physical-device/IME and school privacy/admission acceptance. Approval
must bind the relevant current revision/digest. Silence is not approval. No
real school data, target access or security activation was used in this work.
