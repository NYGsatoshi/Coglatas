# Playwright two-shard execution contract (preparation, not enabled)

The required frontend-test still runs the full static suite. This patch does not launch a second runner or change any artifact upload.
The existing frontend-build is the only Angular artifact producer.

An exact 1/2 or 2/2 shard can be requested through COGLATAS_STATIC_PLAYWRIGHT_SHARD only when the dedicated experimental caller
sets COGLATAS_STATIC_PLAYWRIGHT_SHARD_AUTH=1 and reuses the prebuilt static Angular dist under the existing two-worker policy.
An invalid context or value fails closed. The required full frontend-test launcher explicitly rejects all shard requests.

Future CI partitioning must verify the full Playwright case roster equals the disjoint union of two shard manifests,
including all passes, failures, skips and retries; preserve bounded keyboard/search observations, exact SHA/run/attempt provenance,
and source-derived Producer Artifact ID. Each shard publishes only uniquely named sanitized evidence; no duplicate Angular
build, screenshot archive or canonical report. The required frontend-test must be the fail-closed aggregate.

Playwright has fullyParallel true and already runs two hosted workers. Measure queue-excluded Critical Path and runner
seconds before enabling additional hosts; otherwise close the trial. All changes must target SecurityTestImplimation, never Main.

The native Playwright runner executes only portable Node policy tests; the
Bash launcher refusal regressions are executed separately by Linux CI
Preflight. Windows native UI runs never need Bash for these helpers.
