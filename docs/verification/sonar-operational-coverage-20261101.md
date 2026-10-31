# Original native coverage: diagnostic evidence and import boundary

Related: #1507, #614 and #615. **IMPLEMENTATION_IN_PROGRESS / RELEASE_NO_GO.**

## Observed Main baseline

Product Main `876396f0f0944280023209c1788aca5431c26235`, tree
`4898d68505e49cd3e49c3c16cf4f2c21fca89b91`, and Spec Main
`850a5d9376db11e75235cf55e03ebed4de9c0510` were read from GitHub again.
Sonar analysis `99376a5a-5a31-4aed-b1c0-cad7eff479af` reports the same Product
revision. Official check `124629129101` fails 72.8% new coverage and Security E;
the successful analysis workflow `41229863147/1` does not override that failure.
The unchanged previous-version period starts on 2026-10-17.

All 1,862 file-measure records were paginated. They contain 11,577 new lines to
cover, 4,026 uncovered lines, 5,700 conditions and 674 uncovered conditions:
12,577/17,277 covered points; 13,822 are needed for 80%, a gap of 1,245.
These are provider counts, not a forecast of this patch's effect.

The largest uncovered operational UI owners include `group-dm-real.ts` (924
new lines), `readonly-roster-real.browser.ts` (295) and
`student-council-pilot.browser.ts` (263). They remain in analysis and coverage.
No blanket test/helper exclusion is proposed. Original browser-callback range
attribution uncertainty under #1507 remains unresolved.

## Change and actual execution

Existing source-inventory and npm-audit regression suites already exercise two
tracked modules omitted from the imported native report. Measure those unchanged
suites in a separate diagnostic cohort and retain its original LCOV. The source-inventory
checks validate the maintained Angular route/owner contracts; this adds no
Avalonia implementation or platform promotion.

The cohort includes exactly `frontend/scripts/check-av-mig-source.mjs` and
`scripts/ci/check-npm-audit-baseline.mjs`. The existing read-only validator must
accept both and reject overlap with either original Node report. Original report
scope, source bytes, zeros, thresholds, exclusions and toolchain pins remain.
The new step runs before scanner credentials and retains native V8 files as
well as LCOV, including failures. Its LCOV is **not imported into Sonar**:
independent review found that the pinned Node reporter can lose never-run
nested ranges when it combines subprocesses. Serial test scheduling does not
repair that merge. No custom merger, count rewriting or coverage credit is
introduced. The existing three imported JavaScript reports remain unchanged.
The existing Functional/roster cohort additionally retains original raw V8
files in a separate artifact; its source/test selection and report are preserved.

Local Linux execution used the repository's Node 24 image digest
`sha256:e7add2a490bb17d97b485b8641bd88455fff7839ade0f5eba493c3508124d413`
(Node 24.22.2) and locked TypeScript 6.0.3 installed with npm 11.17.0 and the
strict repository install policy. Both suites passed: **41 tests, zero skips,
cancellations or retries**. Original LCOV SHA-256:
`ac76aa752b39d6e9370a28051e8f1d6baa6972afa3df4028b0e58517ba1c65e9`.
Unqualified native records: source inventory 421/426 lines, 135/140 branches; npm audit
137/164 lines, 24/39 branches. These are complete-file native counts, not Sonar
new-code counts. Fifty-three raw V8 files were produced. No hit/coordinate
rewriting or execution of serialized browser callbacks was used. These counts
are retained diagnostic output, not qualified coverage: the current npm tests
exercise advisory-source normalization but do not exercise URL/title fallback.
Those missing ranges must not be credited through a faulty process merge.

Downloaded exact-Main Node artifacts `13053411625` and `13053321648` retain
40/3 source records and 1,854/244 branches respectively. Independent original
report validation confirms the new two-source set is disjoint from both.

## Result Main and independent review checkpoint

Result Main `086d685acedb127154150effe459cdc8ef48db43`, official analysis
`c0fd6888-66cc-4155-b85e-d54c62bb5eae`, reports Security E and 72.5% new
coverage. The same 11,577 lines and 5,700 conditions now contain 4,085 uncovered
lines and 674 uncovered conditions: 12,518/17,277, leaving 1,304 units to 80%.
The only changed file measure across the two analyses is the source-identical
Search drawer observer: its native LCOV lines 62–120 change from positive to
zero. Earlier positive counts are not proof of callback execution. Raw V8 was
not retained by that historical cohort; this patch adds future retention.

CodeRabbit reviewed exact head `4ba52e761edfa4128e79d11ae8fa0bbbe0eafad4`
and identified the process-merge risk, an excluded-versus-held wording error,
and missing immutable Sonar evidence in the audit JSON. The wording/evidence
are corrected; the proposed new import is withdrawn pending valid attribution.
Existing Main sources and coverage imports are not reduced. See the upstream
[Node process-merge report](https://github.com/nodejs/node/issues/66860).
This candidate does not fix the reporter, establish 80%, or qualify the existing
Main report's uncertain ranges. No runtime/toolchain upgrade is proposed.

An additional Python measurement was investigated separately: the existing 22
environment-contract tests pass on Linux; the measured verifier has 144
statements, 98 misses and 76 branches (28% aggregate). Its existing exact-source
guard requires a separately reviewed test-branch update before import. That
import is therefore deferred: the Python cohort and source configuration remain
unchanged in the final candidate. Uncalled CLI and numerical/performance paths
stay uncovered; #1128's numerical suspension is unchanged.

## Retained limitations and failures

An initial Node invocation omitted report-directory preparation and exited with
ENOENT before tests; after creating the directory, the above real measurement
passed. This is setup correction, not a flaky-test waiver.
Windows Python trials retain cp932/subprocess environment failures. A separate
Linux run of the 22 environment controls passed. Initial WSL source-guard trials
could not use Windows worktree Git metadata and lacked jq; they are not passes.
The initial Python-scope extension also failed the exact-source-set guard and a
receipt control's unchanged 15-second bound. That failed candidate was narrowed
to the independent Node import; no guard, deadline or expected-source set was
weakened. Fresh native-checkout validation, review, hosted exact-head analysis, official
PR Gate, ordinary merge and resulting-Main validation remain required.

The independent channel-invitation regression is in test-only PR #1610, based on
`SecurityTestImplimation`; it is not imported here or promoted to Main.
No existing HOLD, #1502, runtime-upgrade experiment, approval, deployment,
school data or release permission is changed.
