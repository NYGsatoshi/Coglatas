# Angular Main CI reliability investigation — 2026-10-18

Tracking: [Issue #1242](https://github.com/NYGsatoshi/Coglatas/issues/1242).
This record concerns the existing Angular Web test runner. It grants no security,
API, database, deployment, or release approval.

## Exact observed runs

| Commit | Workflow / attempt | Frontend job | Observed result |
| --- | --- | --- | --- |
| `cb2805bca02cfcf75f4370d167baeb41fc024a9c` | [39079363181, attempt 1](https://github.com/NYGsatoshi/Coglatas/actions/runs/39079363181/attempts/1) | [117533833612](https://github.com/NYGsatoshi/Coglatas/actions/runs/39079363181/job/117533833612) | FAIL: 128/130 spec files passed; 1,240/1,242 unit tests passed; two first-fixture hooks exceeded 30,000 ms |
| `50c3242950d22105063ec3b97a90adf7b5b34b0d` | [39088191297, attempt 1](https://github.com/NYGsatoshi/Coglatas/actions/runs/39088191297) | [117559759098](https://github.com/NYGsatoshi/Coglatas/actions/runs/39088191297/job/117559759098) | PASS: 131/131 spec files and 1,249/1,249 unit tests; frontend lane exit 0 |
| `cb2805bca02cfcf75f4370d167baeb41fc024a9c` | [39079363181, attempt 2](https://github.com/NYGsatoshi/Coglatas/actions/runs/39079363181/attempts/2) | [117580097092](https://github.com/NYGsatoshi/Coglatas/actions/runs/39079363181/job/117580097092) | PASS: 130/130 spec files; 1,242/1,242 unit tests; targeted Frontend job completed at 2026-10-18T04:03:48Z |

Attempt 1 reached terminal `failure` at 2026-10-18T00:14:40Z before the targeted
rerun. Its failure remains evidence. GitHub's attempt-2 jobs response also carries
forward successful jobs from attempt 1; their old start times do not establish
fresh execution. The new Frontend job started at 2026-10-18T03:53:40Z.

Latest Main at audit start was `50c3242`; its workflow was `completed / success`,
attempt 1. Its success is independent evidence at a different commit, not a
retroactive pass for `cb2805b`.

## Failure boundary and measurements

The original failed hooks are:

- `frontend/src/app/layout/app-shell/app-shell.component.spec.ts:22`, first
  scenario "separates Projects and My Tasks into Pinned for normal users with
  projects:view".
- `frontend/src/app/layout/feature-menu/feature-menu.component.spec.ts:20`, first
  scenario "emits a collapse request from the desktop menu toggle".

Both fail while awaiting `TestBed.configureTestingModule(...).compileComponents()`.
The later test assertions are not reported as failures. Both hooks already have
an explicit 30,000 ms budget. Retained logs have no individual successful hook
timings, CPU utilization, memory pressure, or Vitest worker timeline.

| Measurement | Older attempt 1 | Latest Main attempt 1 |
| --- | --- | --- |
| Runner image | Ubuntu 24.04, `20261012.334` | Same image revision |
| Runner version | `2.337.0` | `2.337.0` |
| Unit suite duration | 472.42 s | 349.26 s |
| Vitest reported distribution | import 61%; tests 15%; environment 14%; setup 6%; transform 4% | import 59%; environment 15%; tests 15%; setup 6%; transform 4% |
| Static Playwright | 159 passed; 13 existing skipped; 1 worker; 10.5 min | 159 passed; 13 existing skipped; 1 worker; 7.8 min |

The original unit run was approximately 35% slower overall. This aggregate
difference is not a measured cause. Vitest phase percentages are suite summaries,
not per-hook profiling. Static Playwright success does not override failed unit
setup; its 13 skips are not passes. Mocked APIs do not prove backend compatibility.

## Source audit

Audited source: exact Main `50c3242`. Compared with `cb2805b`, these relevant
inputs are unchanged:

- `frontend/vitest.config.ts`, `frontend/vitest.setup.ts`, `frontend/angular.json`;
- `frontend/package.json`, lockfile, and `frontend/src/app/layout/`;
- `.github/workflows/main-validation.yml`;
- `scripts/ci/run-frontend-parallel.sh` and `scripts/ci/parallel-lane-lib.sh`.

The Vitest configuration enables file isolation and vendor-scoped Syncfusion
inlining. It does not explicitly bound workers. Main Frontend launches unit,
static, and Docker Playwright lanes concurrently. Angular and Storybook rebuild
lanes are disabled because exact-commit producer artifacts are restored.
Playwright explicitly uses one worker. Its container can still consume host CPU
and I/O concurrently with Angular unit setup and root dependency installation.
Distinct GitHub jobs have distinct hosted runners; simultaneous workflow jobs
alone do not prove within-host contention.

`FeatureMenuComponent` references `FeatureMenuIconComponent` inside immediate
defer blocks. The icon component uses the existing `@lucide/angular`
`LucideDynamicIcon` renderer and data-only navigation icons. Cold deferred import
resolution can lie inside compilation setup, consistent with existing comments.
The logs do not isolate Lucide transformation, Angular compilation, scheduling,
or another delay. None is established as the root cause.

## Disposition and remaining verification

Current Main has **no reproduced failure** at the newer SHA. Historical runner
reliability remains **UNSTABLE / cause UNVERIFIED**; the retry did not reproduce the timeout.
No timeout, test count, skip, reporter, dependency, worker, workflow, quality
threshold, or required-check change is justified by the evidence so far.

Both attempts and exact totals/timing are retained. One successful retry does not
demonstrate deterministic remediation. If the timeout recurs, profile the
configured Linux environment and compare cold-hook timing with bounded
worker/lane scheduling before proposing a narrow correction; retain the existing
hook budget and assertions.

Local Angular, Docker, and backend tests are **NOT_EXECUTED** for this
documentation-only audit. Hosted outcomes were read directly from exact job logs.
Local runner configuration is not equivalent to Linux CI. No production behavior
changed; Issue #482 was not reopened or modified. PRE-AVALONIA SEC-ARCH remains
BLOCKED. The terminal targeted retry result is retained below; no deterministic repair is claimed.

## Quality and protection boundary

The Coordinator bound SonarQube Cloud analysis
`ba97c7fe-1cba-4b84-ad50-f3b106baf7be` (2026-10-18T01:56:39Z) to exact Main
`50c3242`. This audit independently read the [analysis-specific Quality Gate
API](https://sonarcloud.io/api/qualitygates/project_status?analysisId=ba97c7fe-1cba-4b84-ad50-f3b106baf7be):
`OK`, with new duplicated-lines density **0.0%**, threshold **3%**. This is
baseline analysis evidence, not a new PR's quality result. Overall duplication
and a future candidate must be verified separately against the correct project.

The Coordinator also read active [ruleset
24643016](https://api.github.com/repos/NYGsatoshi/Coglatas/rulesets/24643016),
confirming the six required checks (`build-test`, `frontend-test`, `security-scan`,
`publication-readiness`, `functional-fast`, `performance-fast`), required PR and
resolved review threads, with no bypass actor. The separate branch-protection
endpoint returned 403; it is not claimed readable. This audit changes none of
these controls. Frontend static-analysis no-op markers remain SKIPPED, not lint
execution PASS.

## Terminal targeted retry receipt

The exact same-SHA Frontend job completed successfully at 2026-10-18T04:03:48Z.
Its decoded job log reports 130 spec files, **1,242 unit tests passed**, duration
**370.81 seconds** (import 58%, environment 15%, tests 15%, setup 6%, transform 4%).
Static Playwright reports **159 passed and 13 existing skipped**, in 8.4 minutes.
All selected parallel frontend lanes exited successfully. The original two
30-second hooks did not fail in this retry; no source or timeout changed.

The workflow remains `completed / failure` at attempt 2. Although only the failed
Frontend job was requested for rerun, dependent jobs did execute afterward:
`build-test` (117581817895), `security-scan` (117581817903), and `frontend-test`
(117581817942) all succeeded. The non-required `specification-contract` consumer
[117581817901](https://github.com/NYGsatoshi/Coglatas/actions/runs/39079363181/job/117581817901)
executed from 2026-10-18T04:03:51Z to 04:03:59Z and failed. The complete attempt-2
[jobs response](https://api.github.com/repos/NYGsatoshi/Coglatas/actions/runs/39079363181/attempts/2/jobs?per_page=100)
contains 46 entries and identifies that consumer as the sole failed job.
Earlier wording that attributed the final failure to aggregate jobs not being
re-executed was incorrect; this is the corrected terminal evidence.

The successful Frontend retry is **NON-REPRODUCED**, not proof of a root cause or
deterministic stabilization. Initial attempt 1 remains FAIL; the overall retry
workflow is not reported as PASS. Issue #1242 remains open; #482 is unchanged.

## Advisory receipt provenance failure

The consumer log confirms a successful artifact download and SHA-256 verification
before the failing Python step. Artifact
[12118178631](https://github.com/NYGsatoshi/Coglatas/actions/runs/39079363181/artifacts/12118178631)
is `main-specification-contract-advisory` from the same workflow. Independently
downloading its ZIP yielded SHA-256
`b30288973430b14b2d5a286cc0d0e4a633228b926e16d7c1c39a60854880ff9f`, matching
the consumer log. The extracted, unmodified `advisory.json` has SHA-256
`1ab930c00c3ea68012fd6e75713ad75b5f5e2471d5bbaf2e36d845cc1457a891` and records:

- `candidateSha`: `cb2805bca02cfcf75f4370d167baeb41fc024a9c`;
- `runId`: `39079363181`;
- `runAttempt`: string `1`;
- `capturedAtUtc`: `2026-10-18T08:56:39.440446+09:00`.

The [unchanged consumer](https://github.com/NYGsatoshi/Coglatas/blob/cb2805bca02cfcf75f4370d167baeb41fc024a9c/scripts/ci/specification_contract_check.py#L22)
requires the receipt's run ID and attempt to match the consuming job's
`GITHUB_RUN_ID` and `GITHUB_RUN_ATTEMPT`. Attempt 2 therefore rejects the attempt-1
producer receipt. The stable sanitized error at 2026-10-18T04:03:56Z is
`specification-contract: ERROR; sanitized same-run receipt unavailable or inconsistent`.
It does not mean the downloaded artifact was absent.

A bounded local replay called the unmodified `consume` function on the exact
artifact bytes, candidate and run ID, with observation time
`2026-10-18T04:03:56Z`. Expected attempt `1` was accepted as a non-authoritative
Advisory observation; expected attempt `2` raised
`ValueError: Unqualified provenance or authority.` No receipt field was edited.
This isolates the attempt mismatch and rules out another receipt validation
failure for that replay. The existing
`python -B scripts/ci/test_specification_contract_check.py` suite also passed
**12/12**, including wrong-candidate/run/attempt and stale-receipt rejection.
These are local provenance checks, not backend or architecture qualification.

This is expected fail-closed provenance behavior for a partial retry that did not
refresh the producer receipt. It is independent of the Frontend hook timeout.
No attempt binding, Advisory authority, workflow dependency, or required check
was weakened; the full workflow was not rerun merely to obtain green status.
A future independently authorized CI change addressing partial retries must keep
explicit producer and consumer attempts and current exact-candidate qualification;
relabeling old evidence as fresh execution is not an acceptable repair.
