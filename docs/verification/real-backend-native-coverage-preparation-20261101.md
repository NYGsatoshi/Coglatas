# Real-backend native coverage preparation

This Draft prepares conditional, provenance-checked P0 coverage import. It is not
Main test promotion authority or evidence that the current Main gate passes.

Product baseline: `49962886a875d30dd16c822e4b25b1dccd205ae6`.
Independent test-management source: `524a002555fabbcbe0a6b987192ba9904108b32d`, based on
`SecurityTestImplimation` at `b18939f9104d52ae33e32a7f9b15a6e1b52892f1`.
The four test files in the qualification checkout must match those Git blobs exactly.
Neither the management PR nor this Draft is eligible to merge without the applicable
explicit test promotion authority. In particular, do not merge the management PR
into `SecurityTestImplimation` or update #1502.

The existing protected `Licensed Real Backend Acceptance` workflow gains an optional
`native_coverage` boolean, default false and admitted only with `suite=mobile-mvp0`
or the original Desktop `suite=p0`.
The existing clean-source, first-attempt, single-case/four-actor and source-built-image
checks remain. Within the existing isolated Compose browser container, a native
Node test delegates to the original real-backend runner with identical Mobile
configuration, project, retry-zero and one-worker arguments. A child failure or
signal fails the outer test. The existing Compose wrapper still owns cleanup.
Without the opt-in, its original command is unchanged.

The workflow retains the original native LCOV and raw V8 reports together with the
existing source/execution receipts for seven days. The conditional consumer below
adds reports only after qualification. Source scope, exclusions, thresholds,
runtimes, package dependencies, browser assertions, retries and timeouts remain
unchanged. No production, cloud-host, firewall, DNS,
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

Actual new-head protected execution and independent latest-head review remain to
be recorded. Historical trials below are not successes for the latest SHA. A successful diagnostic
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

Candidate `3532f560bf2789bacaf8fe9939c30d7e91b19621`, tree
`ce9154b45cf9c859ccda57df90f7bba8f3030d73`, completed run
`41252074764/attempt 1`, job124687496635 successfully. The unchanged real case
passed with four actors and no retries; image
`sha256:28bb71d77c02a7b853196bea2e9d52b05d911382a90ed07b2b798b1a4f5c2380`.
The native artifact13061284847 ZIP SHA256 is
`6661480832dfb2990a8b6db2f535b224e28f1947a7ccbf52cc557aa31d54e60c`;
the execution artifact13061702766 ZIP SHA256 is
`aa828c2dd85ebe754bd917dab54a631499e51813ae3d6f0f1a1756b106cd6ca3`.
The browser-container runtime remained Node24.21.0.

Successful artifact publication did not qualify coverage. Exact Git/source-map
comparison for Group source blob`5a62b7cb770bcf7df74e1eb6e486deaff15ab288`
found separate discovery and executing-worker module records. The browser geometry
callback at original line971/column37 has native count0, while original LCOV
lines972-973 have positive counts. The real browser geometry receipt is retained,
but it does not turn that into Node callback execution. This original report is
not imported. Cleanup returned success, with the existing Compose diagnostic
`Error while Stopping` retained as an observation rather than silently erased.

The next candidate moves only the Group helper import from module initialization
to its unchanged existing callsite in the actual scenario. This preserves its
arguments, assertions, selection, retries and deadlines and leaves the Group
helper source unchanged. Worker-only loading is a source-supported correction
hypothesis for the discovery/worker merge; fresh raw ranges and original LCOV
must prove its effect. It does not authorize a general converter or report edit.

The worker-only Group trial at `587687abbed273aee4b6f9f41275a5e4cac9532d`, tree
`cd2b676e5537740f0c916ff8758dc329c79899bf`, passed actual Mobile run
`41253001312/attempt 1`, job124690237041, with four actors and no retries. Image
`sha256:f16560829b9e110961e9a00e0ea339f6e4b20bbb79f83aa5c78dd9d05492c359`;
native artifact13062286882 ZIP SHA256
`b6fc5fe3debf38c2666cda7d4a1a375473a31c40418ca4bb7f5819bbd89e4eae`;
execution artifact13062570982 ZIP SHA256
`b57a6860db1ef218fd34a23f3d203900e61187f61356dd102f6e7568d6aa06c3`.
Exact raw/source-map comparison confirmed only one Group module record, but its
geometry callback remained native count0 while pure body line972 remained
positive in native LCOV. Worker-only loading did not repair that source-map
attribution. The trial was withdrawn as a complete fix, and no report imported.

The current preparation also measures the existing Desktop P0 owner. Its Mobile
geometry helper is not invoked. The focused browser report covers only unchanged
Group and Workspace-member helpers; other files remain in the existing Sonar
analysis with their existing reports or uncovered state. Both helpers load only
in their executing worker to avoid the separately established process-merge bug.
This does not assert the Mobile source-map problem is fixed. The original P0
host entry point also runs directly as a native Node test file, with its unchanged
arguments and required-test manifest; no new host test wrapper is introduced.
Original host/browser raw reports and LCOV are retained separately. A clean,
exact-source/attempt-one receipt and actual running backend image/PostgreSQL
server_version_num are required. The database query is read-only and synthetic;
it makes no application-role or RLS claim. The base acceptance image remains
floating postgres:18-alpine, not the separate Sonar PostgreSQL18.6 digest pin.
No original native LCOV from these trials is imported.

The Desktop P0 diagnostic at `d95acf64b0dfdf8a48e76844a800caa6bedc01b4`, tree
`2c7dd43fef2def7fb2a6f3cde035dbd5e55c72e6`, passed run41254537206/attempt1,
job124694797882: 8 required / 8 JUnit cases, no retry or skip. Native
artifact13062414217 ZIP SHA256
`9964bb69c803429cfdf7ce60bd1f4cfd2f365274125d184100b7a99f21725d10`
retains original bytes. Host Node24.22.1 and browser Node24.21.0 differ; actual
PostgreSQL server_version_num was180006 and backend image was
`sha256:8556fa7d4c84aef10503193e8c71367bbff156f5b0f37ffae998a6263ac62f1d`.
Original native LCOV still overcredits the uncalled Mobile geometry callback.
Standard locked c8, scoped to these two TypeScript sources, leaves lines969-975
zero. Read-only comparison finds one raw module per source, 38/1 zero ranges and
zero pure-interior mismatches. Separate standard conversion of the two host
entry points finds one module each, 2/55 zero ranges and zero mismatches across
2/71 pure interior lines. This historical replay qualifies that approach, not a
future SHA or a custom counter adjustment. It cannot satisfy the new receipt
schema retrospectively: that archive lacks browser.json and execution.json.

The new consumer requires one exact-SHA, same-repository, Owner-dispatched P0
run of the existing protected workflow. It rejects multiple candidate runs,
attempts other than one, failures, changed tree, missing required cases, retries,
unbound runtime/image receipts, expired artifacts and wrong ZIP digests. GitHub
metadata is read with actions:read. The initial API request carries authorization;
the separate allowlisted HTTPS Azure Blob request never does. Signed URLs and
responses are excluded from diagnostic output. ZIP paths, file types, entry
counts and expanded sizes are bounded before original bytes are retained.
Run/job/artifact authority is rechecked after download.

Absent or incomplete matching evidence produces no additional native import.
A failed/cancelled run, repeated attempt or expired artifact also produces no
optional native import. The original acceptance outcome is not relabeled, and
its separate reliability/release condition remains. Every P0 candidate, including
a failed one, is counted before selection: a later pass cannot replace a failed
first run. Ambiguous history, wrong authority/source, malformed archives or
digest mismatches fail analysis. Unavailable optional coverage must not prevent
the existing security analysis from running with its original four reports.
The existing four LCOV paths remain; only successful qualification appends the
two standard c8 reports for the original P0 host entry points and the unchanged
Group/member helpers. Conversion uses the existing locked tool, a pinned Node24
image, read-only original source/raw mounts and no container network. Absolute
producer paths are matched by mount locations, never by rewriting raw data.
Every source must be tracked, source-map contents/module length must match,
there must be exactly one actual source module, and every pure interior of an
original zero range must remain zero. The original report/raw bytes are retained
unchanged. The common Compose helper stays measured by its existing report.

Seventeen synthetic transport/provenance tests cover authority, ambiguity, failure,
attempt, digest, JSON/ZIP paths, metadata, non-overwrite and credential forwarding.
These fixtures are explicitly not coverage for P0 helpers; only execution of
the real artifact reader contributes its separate Python report. The unchanged
44 Compose/Mobile controls pass locally. New-head execution, full source binding,
Sonar ingestion and independent review remain pending. The Draft stays
DO_NOT_MERGE until exact test Main-promotion authority and all normal checks pass.

## Pending-selection availability repair

The full independent review of `13920cc485618e887dd07a9fbd2303025e27f95c`
reproduced an additional availability defect with network-disabled synthetic
GitHub responses: a pending native selection or pending Mobile step raised
`SELECTION` before the existing incomplete-run fallback. This could stop the
security analysis while the optional producer was still running.

The reader now validates the selected owner's run, attempt and source identity
before progress handling, and retains an incomplete run as an unresolved
candidate. A sole incomplete candidate contributes no report; it is not
discarded to select a later success. Completed malformed selections and unsafe
identity still fail. The four canonical Test524 blobs, acceptance behavior,
original reports, HTTP rejection fixture and provider state remain unchanged.
The independent review and fresh-head qualification must be recorded separately;
the previous139 CI, protected execution and Sonar result do not qualify this fix.

The pending-selection repair now has a separate original persistent regression
file, `tests/ci/test_native_p0_pending_selection.py`. Its initial eleven controls preserve
the four frozen Test524 blobs and use distinct run-bound job responses to reject
pending/failed history followed by a later success. They also cover pending
native/Mobile/P0 selection, wrong owner/run/attempt/source, completed malformed
selection and an unchanged positive import. The existing seventeen controls run
first; the new file appends actual reader measurement before standard XML output.
The PR scope is now eleven files. No test-management branch, original coverage
counter, source scope, threshold or provider disposition changes. Earlier a232
qualification remains historical and does not qualify the new combined tree.
At the initial eleven-control stage, an isolated pre-repair139 reader produced
four assertion failures and two errors; the repaired source passed the original17
and new11 controls. These are historical local synthetic results, not fresh
hosted P0, Sonar or Main acceptance.

Further independent schema review identified valid queued GitHub responses with
an absent or empty `steps` field, or no owner job yet. This is an API-shape gap,
not an observed hosted failure. An incomplete run retains those unresolved
states as candidates after the available owner/run/job identity checks. They
contribute no report and cannot be discarded in favor of a later success.
An absent `steps` field is permitted only for an incomplete run; present null or
malformed steps, duplicate selection steps and completed malformed jobs remain
errors. Only one explicit skipped native-selection step identifies a nonnative
run. Completed runs without an owner job retain their existing unavailable
behavior. The additional original controls exercise these shapes, identity
boundaries and correctly mapped two-run/two-job history without changing the
four frozen Test524 blobs. Earlier qualification does not qualify this source.
The expanded file has24 controls: together with the original17, they pass41
local synthetic tests. Against an isolated pre-queued-shape e3a21abc reader,
the expanded controls produce nine subtest failures and three errors, including
selection of a later pass when the earlier owner job is absent. No hosted
failure is claimed from that synthetic control. Fresh source-bound review,
CI, protected execution and official Sonar qualification remain required.
