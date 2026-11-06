# Original MJS coordinates in Functional coverage

The existing Functional Node coverage producer runs synthetic Playwright
reporter and observation controls as child processes. Several controls also
import the same operational MJS helpers directly in Node. Playwright's default
transpilation previously produced a second representation under the same source
URL. The native Node mapper could combine those transformed and original ranges
in an order-dependent way, even when the source map's original content matched
Git. Sorting input files would select a result rather than fix collection.

The generated Playwright configurations in the six affected Node control suites
set `build.external` to `['**/*.mjs']`. This is the documented
[Playwright transpilation setting](https://playwright.dev/docs/api/class-testconfig#test-config-build),
not a coverage exclusion. Those files already run as native MJS in the parent
controls. TypeScript fixtures still use the existing transpiler. Existing test
cases, assertions, intentional first-failure/retry controls, privacy checks, source
scope, native reporters and the scanner's single existing import are preserved.
No actual browser acceptance configuration or product module changes.

The first expanded hosted execution passed all 388 tests but failed the guard:
three Thread matrix child records still transformed four helpers. The matrix
suite's generated configuration now uses the same native-MJS setting. Its valid,
incomplete and intentional-failure controls and privacy assertions are preserved.

The existing native-mapping regression deliberately retains both transformed
modes using `COGLATAS_TEST_FORCE_MJS_TRANSFORM=1` only in its nested Thread
fixture invocation. That control explicitly sets `NODE_V8_COVERAGE` to separate
owned temporary directories for each mode, preventing automatic propagation
of the producer directory
([Node CLI contract](https://nodejs.org/docs/latest-v24.x/api/cli.html#node_v8_coveragedir)).
Deleting the property alone still propagated the original directory in the first
expanded execution; the six-source guard rejected those transformed records.
An initial empty-value correction stopped propagation but caused Node 24's
experimental test coverage cleanup to exit with an empty-directory error.
Separate directories preserve both independent native executions and isolate
their intentional invalid-coordinate evidence from producer raw input.
Its original undefined-coordinate rejection, mapped report validation, source
retention and covered/uncovered branch assertions remain unchanged. Ordinary
Thread controls use native MJS; the six-source guard rejects transformed input.

Before scanner credentials, the producer verifies six affected helpers against
their exact Git bytes using the existing trusted Git executable. Every retained
raw record for those URLs must have valid ranges within the original UTF-16
source extent, nonnegative hit counts and no transformed source map. All six
must be present in raw coverage and LCOV; mixed representations or missing sources fail collection. The
read-only verifier does not instrument itself, alter raw records, remap reports
or filter findings. It retains a companion `representation.json` with the Git
SHA, Node version, source identities, raw-file digests and original LCOV digest.
The LCOV and companion receipt share the existing `sonar-node-coverage` artifact;
the separately retained original raw files remain available for inspection.

A bounded initial experiment on Main `7fb26c69` kept the complete existing
Search Drawer control suite and its assertions. Both executions passed. The
baseline had ten original records of 8,814 UTF-16 units and three transformed
records of 35,949 units for one helper. With the generated configuration changed,
all thirteen records used the original 8,814 units. Native LCOV retained all
147 source lines while covered lines changed from 147 to 88. The lower result
retains unexecuted callbacks instead of treating the earlier inflation as test
coverage. Original reports and raw bytes from both executions are preserved.

That focused result is not full-producer or Main qualification. The exact-head
hosted producer must execute all existing suites, pass the six-source guard,
retain the original raw/LCOV bytes, and pass the original required checks and
official PR Gate. Independent order diagnostics and full source mapping are
separate from repository test results. Only a newly evaluated resulting Main
establishes its actual coverage, denominator and Gate; no local gains are added
together, and no improvement is promised by correcting an inflated measurement.
