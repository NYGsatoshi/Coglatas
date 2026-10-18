# Acceptance wrapper prebuilt image continuity

Date: 2026-10-19  
Base: `a7c0b9cb658f41fddfcf8cefb13ee984c400c14b`  
Scope: MVP-A AuthZ and MBJ-01/02/03 isolated acceptance wrappers.

## Observed problem

The [independent Main audit](https://github.com/NYGsatoshi/Coglatas/pull/1255#pullrequestreview-5564029666)
records a provenance gap in Main run [39147128487, attempt 1](https://github.com/NYGsatoshi/Coglatas/actions/runs/39147128487),
[real-backend job 117733942233](https://github.com/NYGsatoshi/Coglatas/actions/runs/39147128487/job/117733942233).
The producer image was restored correctly, but inherited acceptance wrappers then
passed `--build` despite `COGLATAS_REUSE_PREBUILT_APP_IMAGE=1`. Subsequent builds
replaced the restored tag and emitted a license validation diagnostic from the
nonsecret reuse sentinel. Those later functional results do not establish reuse
of the licensed producer image. The original failure record remains valid;
this change does not relabel it as a successful continuity check.

Raw logs and actual before/after image receipts remain in the independent audit
evidence outside Git. Docker image IDs are public content digests, not credentials.

## Change and boundaries

`restore-main-build-artifacts.sh` exports its already validated image ID as
`COGLATAS_REUSE_PREBUILT_APP_IMAGE_ID`. Its existing source stamp, build stamp,
image family, archive load and loaded image checks remain intact.

The four wrappers use the small sourced `acceptance-image-reuse.sh` helper:

- Reuse requires the producer ID, a locally available matching image and the
  exact configured Compose app image before startup. Missing or mismatched
  inputs fail before any startup or probe.
- Every initial start and app recreation uses `up --no-build --pull never`.
  Probe runs omit `--build` and use `--no-deps` after the existing app readiness
  checks. Compose `run` has no `--no-build` flag. The migration dependency still
  starts with the initial app, and the MBJ-02 SDK test run remains unchanged.
- Tag and running app-container image IDs must equal the producer ID around
  startup, recreation and probes, and before writing a completion receipt.
  Build/pull overrides, including boolean flag forms, fail closed.
- Unset or disabled reuse retains the original local build, dependency and app
  recreation behavior. Existing SQL assertions, browser phases, health waits,
  synthetic data, exception handling, teardown and diagnostic collection remain.

The licensed workflow uploads only these four sanitized receipt paths under
`acceptance-image-reuse-${{ github.sha }}` with seven-day retention:

1. `test-results/mvp-a-authz-image-reuse.json`
2. `test-results/mbj01-image-reuse.json`
3. `test-results/mbj02-image-reuse.json`
4. `test-results/mbj03-image-reuse.json`

The upload runs in reuse mode even after failure, allowing partial successful
wrapper evidence. A successful full reuse job is expected to have all four.
Each receipt contains only its schema version, expected producer image ID,
successful tag/container match and `within-this-acceptance-wrapper` scope.
It contains no Compose configuration, environment dump or authentication data.
This establishes a bounded continuity observation; it does not attest to image
contents, every other CI lane, all runtime behavior or global build provenance.

No producer license handling, custody policy, Dockerfile, Compose definition,
product implementation, dependency lock, database contract or approval policy
changes. SEC-ARCH, Mapping, RLS, deployment and Avalonia HOLDs remain unchanged.

## Verification

The existing `performance-contract.yml` invocation of
`python3 scripts/ci/test-main-runtime-artifact.py` includes the new controls.
Its PR path filter explicitly includes the helper, restore script, four
wrappers, test file and licensed workflow, so these controls run for this scope.
Fake Docker commands execute the actual four shell wrappers with synthetic
responses; they are harness tests, not HTTP, PostgreSQL or browser integration.

The matrix covers all startup/restart/probe counts, ordinary builds with reuse
unset and `0`, missing/invalid/wrong producer bindings, missing images, wrong
Compose references, retagging during startup/probes/restarts, app-container
mismatch, prohibited flag forms, partial failing container lookup in a shell
conditional, failure status preservation and bounded upload paths. The reuse
trace rejects every build command and verifies the sentinel is never passed as
a build input. Existing artifact restore negative controls remain present.

Local commands use Git Bash and the existing bundled Python, without dependency
installation:

```powershell
$env:PATH='C:/Program Files/Git/bin;C:/Program Files/Git/usr/bin;'+$env:PATH
& 'C:/Users/Hirota Kaname/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' scripts/ci/test-main-runtime-artifact.py -v
```

Each of the six changed/new shell scripts is also checked separately with
`bash -n`. The workflow is parsed with the existing `js-yaml` dependency;
parsing is not an execution or a complete Actions semantic check.

Historical local attempts are retained: the initial suite reached all new
controls successfully, but the existing restore-positive method failed its two
subcases because GNU tar interpreted Windows `C:` arguments as a remote host.
Setting local `TAR_OPTIONS=--force-local` still left backslash directory failures.
The fixture now invokes the same restore script from its temporary directory
with relative artifact/workspace paths. All original assertions remain, and the
final command uses no tar override or skipped tests.

The final local suite completed **15 methods PASS / 0 FAIL / 0 SKIP**. It includes
58 full-wrapper executions, eleven direct helper cases, the existing 13 artifact
restore cases and two workflow source checks. Shell syntax is **6 PASS**;
workflow YAML parsing is **2 PASS**. These counts describe local harness/static
verification, not executed runtime journeys. The immutable commit is recorded in
the PR receipt; the machine-local transcript is retained outside Git as
`reuse-artifact-harness-final.log`.
The policy method was rerun separately after adding the two dependency-override
cases: **1 method PASS / 0 FAIL / 0 SKIP**, including all nine override inputs;
its transcript is `reuse-boolean-policy-final.log`. Gitleaks v8.24.3 scanned the
ten changed files before commit: **PASS**, no leaks found, no new ignores.
Actual Docker startup and real-backend reruns are **NOT_EXECUTED** locally:
the Docker Desktop Linux engine pipe is unavailable. Compose CLI help confirms
the used options; it does not establish startup. Exact-head hosted CI and
inspection of all four uploaded receipts remain required before claiming
licensed producer continuity for a hosted run.
