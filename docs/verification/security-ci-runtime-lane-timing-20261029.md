# Security CI parallel-lane timing qualification — 2026-10-29

Target integration branch: `SecurityTestImplimation`. Diagnostic-only change, not release approval.

## Baseline

Main CI run [40649034671](https://github.com/NYGsatoshi/Coglatas/actions/runs/40649034671) spent approximately 38.3 minutes in Main Security; its parallel security runtime step took about 33.9 minutes. Grouped worker logs did not indicate which of Core, Schemathesis, or ZAP dominated elapsed time. Qodana Cloud (~26.6 min) and Community (~25.0 min) also warrant separate equivalence analysis, but are unchanged by this candidate.

## Bounded diagnostic contract

- `ci_parallel_start` tracks elapsed seconds inside each child process, independently of sequential `wait` ordering. `ci_parallel_wait` retains native exit statuses and the existing fail-closed aggregate decision.
- GitHub Step Summary shows only allowlisted worker names, native PASS/FAIL status, and numeric elapsed seconds. It does not publish command lines, tokens, actor identities, headers, URLs, environment values, or security runtime logs.
- Existing grouped logs remain available. Each Security Runtime lane emits an additional stage-level elapsed time and native status for `prepare`, `sec03`, `sec05`, `aud02`, `sec04`, and `sec06`; ordering and cleanup are unchanged.
- Missing/malformed measurements are labeled `unavailable`, not silently replaced with zero or declared successful. Integer-second resolution is advisory.
- The no-network regression script tests success, fail propagation, preserved logs, duplicate/path injection rejection, and missing durations; GitHub Actions Static Analysis runs the self-test when these scripts change.

## Qualification before optimization

1. Require all six existing PR check contexts and actionlint/zizmor to pass against the exact candidate SHA; continue to reject the true underlying worker failure.
2. Read comparable per-lane and per-stage timings before deciding whether to reduce duplicated setup. Do not suppress SEC-03 baseline, authenticated tenant tests, Schemathesis or ZAP coverage.
3. Community/Cloud Qodana consolidation is **not** implemented by this change. Only consider it after matching scan coverage, SARIF evidence, project-model checks, publication permissions and fallback behavior.
4. No merge to `main` without explicit authorization. The integration branch does not establish a student-council or production release decision.

## Live verification and correction: nested EXIT trap

In PR [#1499](https://github.com/NYGsatoshi/Coglatas/pull/1499), the exact-SHA real security runtime passed, but `CI parallel lane` durations were `unavailable`. Stage durations were recorded:

- Core: `prepare` 33s; `sec03` 26s; `sec05` 59s; `aud02` 20s.
- Schemathesis: `prepare` 32s; `sec03` 26s; `sec04` 727s.
- ZAP: `prepare` 32s; `sec03` 26s; `sec06` 658s.

The lane runner's own `EXIT` trap for cleanup replaced the timing trap. The corrected timing implementation runs the command in an inner process with its original `errexit` semantics and records elapsed seconds from an **outer** process that is not affected by the inner cleanup trap. It captures the command's original exit code and never treats a missing measurement as a pass. The no-network tests now include a real shell function that installs `EXIT` cleanup, including a failing command immediately before a false-green marker.

This instrument-only change is still not a performance speedup, nor a release qualification. Compare multiple exact-run measurements before changing scan coverage.
