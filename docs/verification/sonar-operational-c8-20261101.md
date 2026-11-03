# Qualified operational raw-range conversion

The existing 41 source-inventory and npm-audit controls run unchanged. Their
original native LCOV and raw V8 remain retained, with native LCOV diagnostic only.
A separately locked c8 12.0.0 converts the original raw files into an additional
report for exactly the same two untransformed sources. No existing report, test,
source/coverage exclusion, threshold, new-code definition or runtime is changed.

The converter is installed before analysis credentials, with lifecycle scripts
disabled and engine checks enforced. All 56 packages have registry URLs and
integrity hashes. Overrides affect only this new tool's test-exclude dependency:
glob14.0.3 and minimatch11.0.3. The initial tool graph introduced four Moderate
audit records. The patched graph has zero audit findings; the existing
new-advisory gate is unchanged. Root/frontend dependencies are untouched.

Independent investigation reproduced the upstream cancellation and expansion
defects, checked supported Node24 engines and both test-exclude APIs, and observed
no glob call or attacker-controlled pattern in this configured report route.
This is dependency hardening, not evidence of an exploitable Product path or a
provider exception. See [GHSA-3g3m-2fjr-6pw4](https://github.com/advisories/GHSA-3g3m-2fjr-6pw4)
and [GHSA-g3hv-75wg-m249](https://github.com/advisories/GHSA-g3hv-75wg-m249).

Only the two configured sources pass the filter before remapping. The runtime
guard rejects transformed target sources, verifies raw module lengths against
current source, validates tracked LCOV source/line/branch bounds, and requires
actual raw records. Unreachable npm URL/title/unknown returns must have zero
counts in every raw record and their own LCOV source record. The advisory-ID
return supplies a positive execution control. No counter is rewritten.

Local replay used original Main eb18fb3c5b82c05ca4cdc8b68b59276732eb2fef artifact
13058643055, ZIP SHA256
51313678327a8be95eda18408961f3329a9b101ae05102e28c5c2c91c9641741.
The exact workflow guard passed: two sources, 213 branches, 21 uncovered branches;
all 16 npm raw module records leave returns at lines141/144/147 uncovered.
Patched/unpatched dependencies and before/after-remap filtering produced
byte-identical 11,205-byte LCOV, SHA256
47eb93b06c10712d628922269207bc508e824a4897984f758460049213392205.
Existing Node24.22.2/npm11.17.0 performed local qualification; actual CI versions
are recorded by each new execution, not inferred from this replay.

This scope does not qualify transformed Playwright helpers. Separate trials
showed unexecuted callback bodies can remain overcredited there, including with
other converters. Their reports are not imported here. See upstream
[nodejs/node#66860](https://github.com/nodejs/node/issues/66860).
Actual original callbacks in the separate three-test promotion candidate remain
subject to their own raw proof and Owner Main-promotion authority.

The official exact-head PR/Main analyses must determine the resulting coverage
and gate status. No global80%, Main SecurityA, historical reliability closure,
physical/HTTPS/Pilot acceptance or release authorization is claimed.
