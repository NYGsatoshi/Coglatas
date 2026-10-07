# PERF-03 result comparator contract

PERF-03 is the single decision layer for performance evidence. Benchmark adapters (k6, DB probes, browser/Playwright collectors, build-size collectors, and later runtime probes) produce raw samples; they do not implement their own pass/fail thresholds.

## Inputs

`python3 scripts/performance/compare.py` consumes:

- one current measurement document (`scenario`, `metric`, `unit`, `headSha`, numeric `samples`, `attempt`, and the PERF-02 measurement envelope),
- one approved-main baseline artifact with raw baseline samples and the versioned hard EnvironmentClass/fixture compatibility identity,
- the current PERF-02 environment fingerprint,
- `performance/scenarios.json`, `budgets.json`, `environment.json`, and `comparison-policy.json`.

Baseline artifacts must identify `sourceRef: refs/heads/main` and `approved: true`. A PR head cannot be its own baseline. Blocking metrics must use the baseline SHA named by the PERF-01 budget contract.

See [EnvironmentClass and hardware evidence](ENVIRONMENT_COMPATIBILITY.md).
Exact Git SHA and artifact digests remain required. Exact CPU SKU does not:
hardware differences are retained and reported as `HARDWARE_VARIANT` within a
compatible class, without pooling hardware cohorts or weakening variance tests.

Example current measurement:

```json
{
  "schemaVersion": 1,
  "scenario": "workspace.list",
  "metric": "api.latency.p95_ms",
  "unit": "ms",
  "headSha": "1111111111111111111111111111111111111111",
  "samples": [101, 99, 103, 100, 102],
  "attempt": 1,
  "measurementEnvelope": {
    "warmupSamplesExcluded": true,
    "environmentStable": true,
    "benchmarkExitCode": 0,
    "timedOut": false
  }
}
```

## Output and exit status

Every adapter can publish the same `performance/performance-result.schema.json` v1 document. The decision is one of:

- `pass`: valid evidence is within a blocking budget, or a valid trend/extended result was recorded;
- `regression`: hard ceiling or relative-regression budget exceeded;
- `unstable`: current or baseline variability exceeds the versioned policy;
- `insufficient-data`: current or baseline sample count is below PERF-02 `minimumSamples`;
- `invalid`: baseline, fixture, environment, schema, budget, process, or identity contract is invalid.

CLI exit codes are `0` for `pass`, `1` for `regression|unstable|insufficient-data`, and `2` for `invalid`. Therefore noisy, incomplete, or malformed evidence cannot become Green.

Statistics are deterministic: min/max, median, linear-interpolated p50/p95/p99, MAD, CV, and a policy-selected variability indicator. Tail metric IDs (`*.p50*`, `*.p95*`, `*.p99*`) compare the corresponding percentile; other metrics compare medians.

## Bounded rerun

Only `unstable` attempt 1 may request a rerun, and at most one. Attempt 2 must name `previousAttemptArtifact`, so both artifacts remain independently reviewable. Another unstable result is still `unstable`; it is never rounded to pass.

## Baseline updates and budget relaxation

`performance/baseline-updates.json` is the review ledger. `scripts/ci/verify-performance-baseline-updates.py` compares the PR copy of `performance/budgets.json` with the base branch and rejects:

- a baseline SHA change without old/new SHA, scenario, metric, reason, cause, before/after evidence, and budget-change declaration;
- a hard-ceiling increase or relative-regression tolerance increase without explicit relaxation evidence;
- changing a baseline to the PR head itself;
- unclassified "regression happened, therefore raise the baseline" updates.

Accepted causes are versioned and intentionally narrow: fixture/environment change, accepted product change, measurement correction, or contract recalibration. A regression by itself is not an accepted cause.

Active DB duration baselines use the approved EnvironmentClass enrollment ledger
and complete nine-document catalogs described in
[EnvironmentClass and hardware evidence](ENVIRONMENT_COMPATIBILITY.md).
Original legacy duration catalogs are retained as historical evidence and are
not an active selector or a fallback. The superseded introduction validator and
its equivalence to current source/approval/integrity checks are recorded in
[Main duration integration](../docs/verification/perf05-main-duration-integration.md).

## Adapter rule

Downstream benchmark jobs should stop at two boundaries:

1. collect raw numeric samples + PERF-02 envelope/fingerprint;
2. invoke this comparator and publish its v1 result unchanged.

This keeps k6/DB/browser/build/runtime jobs on one decision schema and one threshold implementation.
