# Real-backend native coverage preparation

This is a diagnostic candidate, not a coverage import or Main test promotion authorization.

Product baseline: `eb18fb3c5b82c05ca4cdc8b68b59276732eb2fef`.
Independent test-management source: `c555d4dd311e34bf68ff3867297e3510f77bf191`, based on
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

The first protected diagnostic run, `41251234165/attempt 1` at candidate
`186e9dc676f70cd1536b27578d4d2da50e8d5da5`, failed at artifact publication:
the root container's native JSON files were mode 0600 and the host uploader could
not open them. The actual unchanged Mobile case passed, with four actors, zero
retries and source-built image
`sha256:e93958ef8c885a3c1600ec5b2ea826a27e6cfa7bd2ad038faf629e82064a7393`.
Its execution artifact 13061775376 ZIP digest is
`123e20b7a446c6137d8662746cd6e628ade934595e1b23fa1e8d3d375bedb131`.
No readable raw artifact was retained, so that run does not qualify attribution.
The actual browser container ran Node 24.21.0, distinct from the host and local
24.22.2 runtime; neither was changed.

The revised command scopes native collection to the actual test process,
preserves native file bytes and mode 0600, and changes report ownership to the
bind-mounted checkout owner. It rejects symlinked directories and nonregular raw
entries and requires both LCOV and raw reports. The uploader publishes raw data
only after the actual scenario and handoff succeed; failure retains the existing
closed execution receipt instead. Disposable Linux controls confirm UID/GID1001
with mode0600, child failure propagation, and nonregular-output rejection before
ownership transfer. The original failed diagnostic is retained, not rerun into a
pass. The revised head requires a fresh first-attempt run.
