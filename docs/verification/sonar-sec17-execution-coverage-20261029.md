# Native coverage for existing SEC-17 execution verifiers

## Scope

Baseline Main `38b598f89a4bb705396df2e7bc269d547e4aacd4`; tracking #615 / #1507. Add the existing `test_sec17_deep_execution.py` and `test_sec17_file_policy_execution.py` suites to native process-aware Python measurement and add only their two unchanged operational modules to the exact subject set and its guard. No Product/verifier/existing-20-control-suite body change; the exact-subject guard gains only those two explicit entries.

## Existing controls

Each ten-control suite verifies exact native-owner identity and case count; missing/skipped/failed/aborted/unknown/duplicate/substitute executions; malformed/unsupported XML and aliases; stale/out-of-run execution; failure versus selection distinction; closed capture failures; actual temporary Git/build-stamp/source/loaded-assembly-copy binding. The fixtures are synthetic. These unit controls do not execute either actual native SEC-17 owner or grant formal SEC-ARCH/TMT/Spec, authorization-model or transport acceptance.

## Local evidence

The unchanged operational/test files exactly match Main38b. Ubuntu Python3.14.4 and pinned coverage7.17.0 (pytrace), explicit two-module measurement: first combined execution **20 PASS**. Each operational module has 78 statements / 9 missed, 16 branches / 3 partial; combined branch coverage reported **87%**. This is local measured Python coverage, not Sonar New Code coverage or an official Quality Gate. Missing source-change/entry-wrapper paths remain visible.

The temporary Git default-branch hint remains visible; no global Git configuration was changed. No rule, threshold, source/test classification, coverage exclusion, authorization, RLS, role, seed, policy, recovery, target or release condition changes. Existing process bootstrap, failure propagation and all other controls remain.

Fresh exact-scope process guards, native CI, retained XML source/line/counter audit, official Gate, independent current-HEAD review and resulting-Main qualification remain required. All held PRs, shadow-only work and FCI06 Default C remain.

`STUDENT_COUNCIL_PILOT = RELEASE_NO_GO`.

## Current Main reconciliation

Normally merged Main `c9c16772d3f5326753408e53ff8b4ed3fb2f390b` into the
existing candidate, preserving all four Main-relative files and both unchanged
operational verifier and test-suite bodies. The final comparison adds two suite
invocations, two exact operational measurement entries, their matching guard
entries, and this record. No coverage/source/security exclusion changes.

At reconciliation commit `db72aae8760845dd27fcbc1f4b55c64994939dfa`, an isolated
Linux Python 3.13 container with the repository checksum-pinned coverage 7.17.0
passed all seven process controls and both ten-control suites, zero skips.
Actual combined XML retains each operational subject's 78 statements (69 hit),
16 branch exits (13 hit), and uncovered points. Each subject reports 87% combined
coverage; this is diagnostic local measurement, not Sonar New Code coverage.
The existing bootstrap already-imported warning and temporary Git default-branch
hint remain visible. Fresh hosted qualification and original artifact inspection
must bind the final published source; earlier PR or Main success is not reused.
