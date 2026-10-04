# TASK-V1-PR07 CI acceptance gate

## Stacked PR identity

| Field | Value |
| --- | --- |
| Repository | `NYGsatoshi/Coglatas` |
| Stacked PR | [#276: CI: add TASK-V1-PR07-B acceptance gate](https://github.com/NYGsatoshi/Coglatas/pull/276) (Draft) |
| Head branch | `ci/task-pr07b-acceptance-gate` |
| Base branch | `task/v1-pr07-b-immediate-notifications` |
| Starting base SHA | `6f59e133219a736ba7aca432bbb0bdf686ab3371` |
| Latest base SHA | `159b501d7d82416406a74d5f2bbf3fa59e7c87af`, incorporated by normal merge commit `b8fa6ebe68f27f069d6fc50af9e74b02449ff5de` |
| Branch SHA before this correction | `4281d0e177cad1101e22ec1fc1dc63b6822e462f` |
| Draft | Yes, required |
| Merge performed | No |
| PR #275 merge authorization | No |

This is a stacked CI-only Draft PR. It must not be merged before the PR #275
remediation branch is updated. It does not authorize PR #275 merge.

## Acceptance changes

The existing `build-test` job retains its self-hosted runner, PostgreSQL 18
service, persistent .NET caches, Release build, full `Coglatas.slnx` test,
backend TRX artifact, 90-minute timeout, and concurrency policy. No new job,
retry, timeout increase, service, restore, build, or test parallelism is
introduced.

- **Full backend TRX guard:** verifies `artifacts/test-results/backend-tests.trx`
  immediately after the existing full backend test step.
- **PR07-B focused test:** runs the existing Release build against the same
  PostgreSQL service with `Scope=TaskV1PR07B` and writes
  `artifacts/task-pr07b-test-results/task-pr07b-acceptance.trx`.
- **Strict TRX verification:** requires `total >= 1`, `executed == total`,
  `passed == total`, and zero for failures, errors, timeouts, aborts,
  inconclusive, not-runnable, not-executed, disconnected, warning, completed,
  in-progress, pending, and `passedButRunAborted` counters. Missing
  TRX/Counters/required counters also fail. Counter attribute order is not
  significant.
- **Required test manifest:**
  `scripts/ci/task-pr07b-required-tests.txt` requires the 14 TaskComment
  authorization, Important abuse-control, and current-authorized mention
  regression test names to occur as substrings of executed focused-TRX
  `testName` values. Blank and comment lines are ignored, but a specified
  manifest with no active names fails closed.
- **EF model drift gate:** runs `ef migrations has-pending-model-changes` after
  migration application using the existing Release build.
- **Artifact:** an independent
  `task-pr07b-acceptance-test-results` upload contains only the focused TRX.

The verifier adds a GitHub step summary with its label, TRX path, total,
executed, passed, failed, notExecuted, required-test count, and missing-test
count. It does not include test output, connection strings, or other secrets.

## Manifest correction

The manifest retains 14 active required names. It replaces two nonexistent,
duplicative method names with existing stronger PostgreSQL zero-delta evidence:

- `RateLimitedImportantOnlyUpdateCreatesNoNotification` is replaced by
  `RateLimitedImportantOnlyUpdateLeavesNoPersistenceDelta`. The existing unit
  snapshot test `RateLimitedImportantOnlyUpdateMutatesNothing` already covers
  Notification intent, Audit, Outbox-equivalent state, Task/Comment versions,
  and save count; the PostgreSQL test additionally proves no persisted-state
  delta.
- `UnauthorizedMentionStagesNoNotificationIntent` is replaced by
  `RevokedWorkspaceMemberMentionLeavesNoPersistenceDelta`.
  `UnauthorizedDirectMentionReturnsGenericError` already verifies the generic
  error, no Comment/Notification, and no save; the PostgreSQL test proves no
  persisted-state delta across a revoked-member rejection.

No production test is added or changed. This strengthens the required manifest
from nonexistent duplicate names to existing focused PostgreSQL evidence.

## Final evidence

| Field | Status |
| --- | --- |
| Latest base incorporated | Yes: `159b501d7d82416406a74d5f2bbf3fa59e7c87af` via `b8fa6ebe68f27f069d6fc50af9e74b02449ff5de` |
| Pre-correction full backend artifact | 657/657 passed; `failed=0`, `notExecuted=0`, and `passedButRunAborted=0`; rechecked locally with the hardened verifier |
| Pre-correction focused artifact | 96/96 passed; `failed=0`, `notExecuted=0`, and `passedButRunAborted=0`; all 14 corrected manifest names matched locally |
| Pre-correction EF model drift step | Passed in CI run `30755798529` |
| Pre-correction CI run | `30755798529`: build-test failed only at the obsolete required-name check; security-scan passed; frontend-test was skipped because build-test failed |
| Pre-correction npm Security Audit | `30755798545` passed |
| Hosted check IDs | Pending the post-correction push and run; do not treat this pre-push document as success evidence |
| Stacked checks green | Not yet verified for the post-correction branch SHA |
| Merge performed | No |
| PR #275 merge authorization | No |
