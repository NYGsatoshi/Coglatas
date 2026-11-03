# Offline, queue-excluded PR CI job profiling

This diagnostic reads the **unaltered GitHub REST workflow-run and first-page jobs JSON** for a
completed successful \`.github/workflows/ci.yml\` pull request. It never calls GitHub,
changes a required check, executes a scanner, uploads an artifact, or creates a runner.
It accepts only the **same exact source SHA, run ID and run attempt** on every job.

To collect the input from an approved source-matched PR CI run:

~~~sh
RUN_ID=41697464219
SOURCE_SHA=6b720a9b96f14d8c28ac42763887f032c726e56a
gh api "repos/NYGsatoshi/Coglatas/actions/runs/$RUN_ID" > run.json
gh api "repos/NYGsatoshi/Coglatas/actions/runs/$RUN_ID/jobs?per_page=100" > jobs.json
python3 -B scripts/ci/profile-pr-ci-jobs.py \
  --run-json run.json --jobs-json jobs.json --sha "$SOURCE_SHA"
~~~

The parser rejects malformed/symlinked/unbounded input, duplicate JSON fields,
duplicate/foreign job identities, wrong SHA/attempt, partial pagination, failed
jobs, skipped **owning test/scanner steps**, missing production/test owners,
invalid timestamps, and a run that is not an exactly completed PR CI SUCCESS.

Its stdout JSON contains only fixed job categories and durations, source SHA,
run ID, attempt and counts. Job/step names, logs, runner metadata and HTTP
payloads are not echoed. It reports:

- \`totalRunnerSeconds\`: sum of individual completed native PR CI job durations
  from \`started_at\` to \`completed_at\`, excluding runner assignment/queue.
- \`ownedJobsSeconds\` and \`securityStepsSeconds\`: measured per-job and
  selected original scanner/contract step durations.
- \`pathDurationSumsSeconds\`: *illustrative* sums of fixed preflight + producer
  + Security or Frontend job durations, based on the current expected topology.
  They are **not** a full DAG critical-path or end-to-end wall-clock claim.
- \`status=JOB_TIMING_ONLY\`: the tool **never** qualifies the six cross-workflow
  Required Checks, test counts, SEC-04 operation/HTTP evidence, ZAP or retries.

Check original six required statuses, per-role scanner evidence, source-matched
test inventories and artifacts separately. Compare multiple matched A/B samples;
do not claim speedups from a changed Hypothesis seed or runner load. This is a
diagnostic supporting issue #1691, and may only be integrated into
\`SecurityTestImplimation\`. Main must not be changed or merged.

## Fixed native regression samples

`tests/ci/pr-ci-profile-public-fixtures.json` contains **selected nonsecret
public Actions REST job metadata** for completed runs
[41690765308](https://github.com/NYGsatoshi/Coglatas/actions/runs/41690765308),
[41697464219](https://github.com/NYGsatoshi/Coglatas/actions/runs/41697464219),
and [41697643513](https://github.com/NYGsatoshi/Coglatas/actions/runs/41697643513).
The offline test replays 17 original jobs each and asserts total Runner seconds
of **4070, 3994, 4374**, plus the respective Security path sums
**1115, 996, 1592** and Frontend sums **1105, 1224, 1157**.
These samples are regression fixtures, **not independent API attestation** or
proof of native test/scan-count parity. They deliberately exclude logs,
runner names, auth values, HTTP requests and browser content.
