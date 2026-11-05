# Playwright two-runner shadow A/B (bounded opt-in trial)

This PR targets only SecurityTestImplimation. Existing required frontend-test
continues to run the original complete Playwright and Vitest suites. Nothing
can replace a protected check with half a test inventory.

The two optional shadow jobs run on GitHub-hosted Ubuntu runners only for the
exact head branch ci-opt/playwright-shard-ab-20261105 targeting
SecurityTestImplimation. They use one immutable same-run Angular build producer,
verify the build SHA and invoke the original Docker Compose Playwright entrypoint
with its pre-existing 1/2 and 2/2 opt-in and 2-worker limits. On all other PRs
these jobs are skipped.

No second Angular build is created; no JUnit, HTML, trace, screenshots, logs
or new browser artifacts are uploaded. Local JUnit counts only report PASS/SKIP
numbers, always tagged LOCAL_SHARD_ONLY_NOT_FULL_ROSTER. No real cross-shard
completeness verification or independent GitHub artifact ID attestation occurs
here; Draft PR #1680 remains a separate offline validation preparation.

Qualification before adopting any sharding must compare full protected CI
critical path, all Runner seconds including both extra runners, unchanged test
and Security scanner case/request/operation counts, original evidence retention,
failure/retry rates and artifacts. Compare repeated matched source controls,
excluding Runner assignment time. No speedup is assumed. If benefit is not
reproducible, close unmerged.

Main must not be changed or merged. This trial is never a release gate.
