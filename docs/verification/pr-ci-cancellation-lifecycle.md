# PR CI cancellation lifecycle (P0 #1974)

## Problem and existing evidence

[Issue #1974](https://github.com/NYGsatoshi/Coglatas/issues/1974) records
repeated obsolete PR CI runs that retained their long-running `frontend-test`
or `security-scan` job after cancellation. Their corrected HEAD could remain
pending in the same PR concurrency group. Historical failures, cancellations
and ReSharper reports remain evidence of those original sources; they are not
converted into successful checks.

The [read-only observer](projectide-obsolete-run-readonly.md) remains separate.
It does not authorize or invoke cancellation. This change adds no GitHub
cancel/force-cancel API, write token, cross-PR controller or branch-rule change.

## Source behavior

The long-running `build-test`, `frontend-test` and `security-scan` jobs use
`${{ !cancelled() }}`. GitHub's
[expression reference](https://docs.github.com/en/actions/reference/workflows-and-actions/expressions#always)
recommends that condition for work that must run after success or failure but
remain cancellable. Its
[cancellation reference](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-cancellation)
explains that job conditions are reevaluated on cancellation; job-level
`always()` can keep the entire job running.

Each consumer still starts with its original exact producer-result check.
A failed, cancelled, skipped, missing or unknown producer result fails that
admission step. The three job names, producer dependencies, 120/120/90-minute
execution limits, test selections, retry policies and assertions are unchanged.
The admission steps pin Bash and an empty `BASH_ENV` startup file so inherited
shell defaults cannot replace the checked command.
The short `functional-fast` and `performance-fast` aggregates retain `always()`
and their existing failure/exemption rules.

The required-check source validator accepts the cancellation-only expression
only for these three registered consumers in `ci.yml`, and only with the
unaltered first-step producer refusal. Additional selection predicates,
failure masking, a different producer or an inserted earlier step are rejected.
The evidence evaluator still accepts only terminal `success`; cancelled,
skipped, neutral, unfinished or missing checks cannot qualify a source. A
cancelled workflow may contain a job skipped before dispatch, which is not a
successful required execution.

## Bounded resource finalization

The frontend and Security jobs have explicit `always()` finalizers after their
own test/scanner step has actually started. Each finalizer is limited to two
minutes. It runs after successful completion, failure or cancellation without
changing the preceding step's result. The helper also has its own finite
command and total execution budgets.

`scripts/ci/cleanup-pr-ci-resources.sh` requires the native Actions marker,
current job identity and numeric run/attempt identities. It handles only the
derived frontend Compose project or the three derived Security lane projects
and their own temporary credential state. It preserves unrelated projects,
external cache volumes and collected artifacts. It emits no raw scanner logs
or credential contents. Cleanup failure remains a failure.

Docker's automatic `--rm` behavior and existing worker cleanup can remove a
resource between listing, inspection and removal. A failed Docker inspection
or removal alone is not proof of disappearance. The helper accepts that race
only after a successful complete inventory proves the exact resource absent;
container inventories include stopped containers and full IDs. Failed or
malformed inventories, retained targets, ownership mismatches, timeouts and
residual resources remain failures. Diagnostics identify only fixed Docker
resource/operation classes, never raw arguments, IDs, paths or daemon stderr.

This step is a fallback for process-tree termination before the existing
Compose runner or worker `EXIT` cleanup can finish. It does not promise that
GitHub can execute finalizers after a force termination, runner loss or host
failure. Hosted cancellation and teardown evidence must be recorded separately.

## Validation and acceptance boundary

### Initial hosted source and transition

The initial candidate is
`831b88b5a6a68ecdc952047adb445ca6359efc8d`, tree
`be67785b99f5503f32f4903ee3b719f2c2f8842f`, in
[PR #2034](https://github.com/NYGsatoshi/Coglatas/pull/2034).
[PR CI 43899063679](https://github.com/NYGsatoshi/Coglatas/actions/runs/43899063679),
attempt 1, passed both build producers and ReSharper. Its `frontend-test`
job `133413025526` entered `Run frontend checks in parallel` at
2026-11-16 15:45:54 UTC while that candidate remained the current open PR HEAD.

This follow-up documentation commit records the observed active-source state
and exercises the existing same-PR concurrency transition. The old run's
terminal outcome, finalizer result and the successor's fresh checks must be
recorded in the PR after they are actually observed. The active snapshot alone
does not prove cancellation, cleanup, queue release or a successful test suite.

### Observed Security cancellation failure and follow-up

The first frontend transition completed with a cancelled lane and a successful
bounded finalizer. Its successor, source
`9cf1a7023ad9b90b61baa2afad2146c3d95e5186`, ran
[PR CI 43900140097](https://github.com/NYGsatoshi/Coglatas/actions/runs/43900140097),
attempt 1. The frontend job and finalizer passed on that source.

A later normal, non-force synchronization with merged PR #2033 occurred while
Security runtime job `133416629864` was active. That runtime lane was cancelled
at 2026-11-16 16:07:01 UTC, but its finalizer failed at 16:07:06 UTC with the
generic diagnostic `docker-failed`. A subsequent sanitized artifact upload
also failed during cancellation. Those results remain failures; they do not
qualify successful Security cleanup.

The original log does not identify the failing Docker operation, so its precise
cause is unknown. The separately reproducible disappearance race described
above is repaired with independent absence verification, and finite operation
diagnostics now distinguish future failures. The added controls cover all
three resource kinds at inspect/remove, retained resources, failed/malformed
inventories and disappearing standalone scanner endpoints. This source-level
repair still requires a fresh native Security cancellation and successful
successor checks before its hosted behavior can be claimed.

### Native Security race-fix candidate checkpoint

The reviewed race-fix source is
`e4a9f87e38953f3147af3bf28821781ee515b9c1`, tree
`bca6b1b8f4c5463ff3e6c820707bdb6c727dceab`.
[PR CI 43905078796](https://github.com/NYGsatoshi/Coglatas/actions/runs/43905078796),
attempt 1, ran Security job `133433985793`. Its actual
`Run isolated security runtime lanes in parallel` step started at
2026-11-16 16:21:13 UTC after the runtime-image build and preceding contracts
passed. At 2026-11-16 16:22:36 UTC, the step was still in progress
and the authoritative open PR HEAD still matched that source.

Public logs were unavailable while the job was running, so this observation
does not establish a live resource census or quantify containers removed.
This documentation checkpoint records the active source before ordinary
same-PR concurrency supersedes it. The cancellation result, bounded finalizer
outcome and successor's current-source checks must be retained in PR #2034
after they are observed. No cancellation API or force-cancel action is used.

### Local controls and completion criteria

Focused local verification uses:

```bash
python3 -B scripts/ci/check-required-pr-checks.py
python3 -B scripts/ci/test-required-pr-checks.py
python3 -B scripts/ci/test-functional-fast-gate.py
python3 -B scripts/ci/test-pr-ci-resource-cleanup.py
python3 -B scripts/ci/check-publication-readiness.py
```

The producer tests execute the actual workflow admission shell. Negative source
mutations check producer changes, failure masking, skipped admission, unrelated
job conditions and preserved timeouts. Resource controls exercise the actual
helper against an isolated Docker command double; they are not evidence of
GitHub runner cancellation or real Docker teardown.

Before #1974 is closed, retain a real same-PR sequence in which an in-progress
obsolete source ends `CANCELLED` through normal concurrency, its bounded
finalizers are observed, and the corrected source completes all six current
required checks and applicable reviews. Record both run IDs, attempts, source
SHAs and terminal outcomes. A force-cancelled experiment does not establish
the repaired native cancellation behavior. Source tests and an ordinary green
run alone are insufficient to establish queue release.

Integration targets `SecurityTestImplimation`. #1144 remains BLOCKED,
#1128 remains SUSPENDED/NOT_EVALUATED, and existing Native, Production and
Release holds remain unchanged.
