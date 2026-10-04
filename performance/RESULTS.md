# PERF-03 result comparator contract

PERF-03 is the single decision layer for performance evidence. Benchmark adapters (k6, DB probes, browser/Playwright collectors, build-size collectors, and later runtime probes) produce raw samples; they do not implement their own pass/fail thresholds.

## Inputs

`python3 scripts/performance/compare.py` consumes:

- one current measurement document (`scenario`, `metric`, `unit`, `headSha`, numeric `samples`, `attempt`, and the PERF-02 measurement envelope),
- one approved-main baseline artifact with raw baseline samples and the exact PERF-02 environment/fixture compatibility identity,
- the current PERF-02 environment fingerprint,
- `performance/scenarios.json`, `budgets.json`, `environment.json`, and `comparison-policy.json`.

Baseline artifacts must identify `sourceRef: refs/heads/main` and `approved: true`. A PR head cannot be its own baseline. Blocking metrics must use the baseline SHA named by the PERF-01 budget contract.

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

The first approved DB duration document has no previous baseline SHA. Its ledger
entry must explicitly use `changeType: "initial-baseline"`, `oldBaselineSha: null`,
`budgetChanged: false`, and the canonical
`baselinePath: performance/baselines/db/<small|medium>/<scenario>.json`, or the
exact environment variant path
`performance/baselines/db/<small|medium>/<64-character-environment-key>/<scenario>.json`.
The existing reason, accepted cause, before/after evidence and new SHA fields
remain required. Ordinary replacements still require distinct old/new full SHAs;
an introduction cannot authorize replacement of an existing budget baseline.
For each newly added introduction, the validator proves the document was absent
in the Git base tree, binds the new approved document to an earlier main SHA,
and rejects using the candidate itself. Historical introduction entries remain
valid without asserting absence again on later commits. Their records remain
immutable, and their approved documents, source ancestry, provenance and complete
sample digests are revalidated; historical approval never permits silent sample
replacement or deletion of its ledger entry.
Every DB baseline document in the candidate tree, including recursively nested
environment variants, must have its
introduction record; adding an approved document without any ledger entry fails
closed, including when the legacy ledger has no baseline records.
Running the validator without base/head arguments also revalidates current
documents against the current ledger and HEAD history; it does not skip integrity
checks after initial registration.
This DB support covers immutable first introductions. A future DB document
replacement needs explicit replacement governance that is not implemented here;
the existing ordinary budget baseline replacement policy remains separate.

The ledger entry and baseline document must carry the same `provenance` object:
`sourceRef`, `headSha`, `workflowPath`, `workflowRunId`, `workflowRunAttempt`,
`artifactId`, `artifactName`, `artifactDigest`, `artifactUrl`, `profile`,
`pageSize`, `sampleCount`, `samplesSha256`, `samplesEvidence`,
`environmentCompatibilityKey`, `fixtureHash` and `fixtureVersion`.
The workflow must be the main-only DB baseline capture lane; artifact identity
uses its `perf05-<profile>` name and GitHub `sha256:<digest>` value.
`samplesEvidence` identifies the complete ordered raw stream inside that artifact,
for example `db.json#/measurements/<index>/samples`. `samplesSha256` is SHA-256
of the UTF-8 JSON array serialized with `separators=(",", ":")`, without rounding,
filtering or non-finite values. The validator matches the complete sample count
and digest, environment and fixture identities, and current DB fixture version.
This is local integrity validation: independent review must verify the recorded
GitHub run, attempt and artifact metadata and all raw samples before approval.
It neither authenticates GitHub metadata offline nor grants duration acceptance.
No budget, scenario, threshold or comparator policy changes are implicit in a
first introduction.

DB duration selection recomputes the complete PERF-03 environment compatibility
key from the current fingerprint. A variant directory must match that full key;
CPU model alone, a nearby key, or a favorable observed duration cannot select a
baseline. Existing canonical paths remain supported. A canonical document and
variant for the same profile, scenario and environment key are ambiguous and
rejected. An unknown key still fails closed without a compatible approved
document. CPU model/count, runtime, package/configuration and fixture checks are
unchanged.

Each variant profile/key must introduce all nine current duration scenarios
together from one exact approved main source, run, attempt, artifact and fixture.
Every scenario retains its complete ordered samples and identifies a distinct
`db.json#/measurements/<index>/samples` stream in that artifact. A partial group,
mixed source/artifact/fixture, forged directory key or unledgered document fails
governance. Historical variant introductions and their samples remain immutable.
Captures are declared before a candidate duration decision; independent approval
uses the first chronological capture observed for each complete key and retains
all captured artifacts and samples, including unstable measurements. A later
stable or faster capture cannot replace an earlier capture merely to obtain a
passing comparison. Additional runs are not retries to obtain a favorable host.

## Adapter rule

Downstream benchmark jobs should stop at two boundaries:

1. collect raw numeric samples + PERF-02 envelope/fingerprint;
2. invoke this comparator and publish its v1 result unchanged.

This keeps k6/DB/browser/build/runtime jobs on one decision schema and one threshold implementation.
