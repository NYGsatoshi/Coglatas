# Real-backend native coverage preparation

This is a diagnostic candidate, not a coverage import or Main test promotion authorization.

Product baseline: `eb18fb3c5b82c05ca4cdc8b68b59276732eb2fef`.
Independent test-management source: `8eaccb54e1946ec1880af5a25b385e99a615c4fb`, based on
`SecurityTestImplimation` at `b18939f9104d52ae33e32a7f9b15a6e1b52892f1`.
The two test files in the qualification checkout must match those Git blobs exactly.
Neither the management PR nor this Draft is eligible to merge without the applicable
explicit test promotion authority. In particular, do not merge the management PR
into `SecurityTestImplimation` or update #1502.

The existing protected `Licensed Real Backend Acceptance` workflow gains an optional
`native_coverage` boolean, default false and admitted only with `suite=mobile-mvp0`.
The existing clean-source, first-attempt, single-case/four-actor and source-built-image
checks remain. Within the existing isolated Compose browser container, a native
Node test delegates to the original real-backend runner with identical Mobile
configuration, project, retry-zero and one-worker arguments. A child failure or
signal fails the outer test. The existing Compose wrapper still owns cleanup.
Without the opt-in, its original command is unchanged.

The workflow retains the original native LCOV and raw V8 reports together with the
existing source/execution receipts for seven days. It does not change a Sonar
report path, source scope, exclusion, threshold, runtime, package dependency,
browser assertion, retry or timeout. No production, cloud-host, firewall, DNS,
school-data or user operation is involved. The runtime version must be retained
from the actual container log; the host setup version is not substituted for it.

Before any later proposal to import coverage, review actual source hashes,
transpilation source maps, process identities and nested zero ranges. Browser
`evaluate` callbacks do not execute in Node merely because their enclosing helper
does. Do not credit such bodies without separate actual execution evidence.
Node's process merge can overstate unexecuted blocks; see upstream
https://github.com/nodejs/node/issues/66860. No custom count rewrite or runtime
upgrade is introduced here.

Local qualification used the unchanged pinned Node 24.22.2 image
`node@sha256:e7add2a490bb17d97b485b8641bd88455fff7839ade0f5eba493c3508124d413`.
The existing Compose helper and Mobile runner/profile suites passed 44/44.
Three separate disposable delegation controls proved exact argument forwarding,
child exit-97 propagation, and rejection of an unapproved URL before delegation.
These controls use a stub child and do not establish real-backend acceptance.
The first delegation fixture correctly exposed a mistaken exact raw-directory
assertion: `node:test` substitutes an owned temporary directory. The test now
requires native collection without changing Node's directory or report behavior.

Actual protected-workflow execution, artifact integrity, source/range comparison
and independent latest-head review remain to be recorded. A successful diagnostic
run does not by itself establish valid Sonar attribution, 80% Main coverage,
physical-device acceptance, public HTTPS or Pilot admission.
