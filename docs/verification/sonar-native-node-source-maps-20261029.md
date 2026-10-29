# Native Node original-source coverage mapping

Main47f0e5058c3e82cfdc83b4cc501bad84ebf1c0e7 Sonar40824288315/1
completed, but official Gate123291104969 failed47.5%/D/E. The scanner's
six JS/TS coverage inconsistencies correspond exactly to six BRDA:undefined
records in Node artifact12875517900 (ZIP SHA256
91cb17a88ac705e626af8d4ed9420a5233c2aa721f6fdd62a57de7a5f4845efb):
four in thread-probe-observation.mjs, one in mobile/outcome.mjs, one in
mobile/reporter.mjs. Existing Product assertions and all15 operational/config
sources must remain measured. This is not a Product authorization repair.

## Reproduced mechanism

Local Node24.22.1, the same existing six suites, and repository-pinned
Playwright1.64.1 produce87/87PASS and seven invalid branch positions on Windows.
The Linux Main artifact has six; platform-specific counts are not conflated.
Official npm tarballs for @playwright/test, playwright and playwright-core were
verified against the committed SHA512 lock integrity and isolated in task tools.
The first local run lacked that dependency and failed one reporter control;
it is not passing evidence. A third diagnostic reporter caused a local listener
warning; no listener limit was raised and production retains its two reporters.

Raw V8 metadata contains both original and Playwright-transformed modules under
the original module URL. For example, thread-probe-observation spans12334 original
UTF16 offsets versus48437 generated offsets locally, with inline source maps.
Node24.22.2's official coverage implementation explicitly ignores cached source
maps unless options.sourceMaps is enabled, merges the resulting ranges by URL,
and emits range.lines[0]?.line. The LCOV reporter serializes that missing line.
Enabling Node's official --enable-source-maps maps generated ranges before the
merge. No custom range attribution, fabricated hits or LCOV rewriting is used.

Primary source: https://github.com/nodejs/node/blob/v24.22.2/lib/internal/test_runner/coverage.js
and https://github.com/nodejs/node/blob/v24.22.2/lib/internal/test_runner/reporter/lcov.js .

## Source scope and controls

Source maps also expose the synthetic temporary reporter config/spec embedded
by existing tests. Only the exact owned OS-temp thread-secondary-reporter-* tree
is excluded from this producer. Explicit *.node-test.mjs exclusion preserves the
existing test-body coverage policy when adding explicit producer exclusions;
it does not exclude operational fixtures/helpers. Sonar sources, rules, coverage
exclusions, New Code definition and80% threshold are unchanged. Every original
15 repository source is required to persist; the new validator makes16. No
coverage percentage from this local tooling cohort is Main New Code coverage.

The original LCOV is validated before scanner credentials: nonempty complete
records, exactly one unique tracked repository source per record, no test body,
valid positive original-source line/function/branch coordinates, and consistent
branch totals. The report is read, never rewritten. Actual transformed-module
controls run the unchanged observer/reporter suite in both modes: baseline must
reproduce an undefined branch; mapped output must retain every repository source,
retain hit and uncovered branches in all three affected modules, and validate.
CLI positive/negative controls verify successful import, malformed/missing-report
failure and no arbitrary report-content echo. Nine new controls augment the
existing87; no old suite is removed, skipped or relaxed. Child coverage controls
clear only their copied runner-context/options/coverage environment to create
independent native collectors, preventing parent test context from suppressing
fresh output. The initial self-test correctly failed6/7 when inherited context
produced no fresh LCOV; no empty-report acceptance was introduced.

Focused Windows source guards pass6/6. The full routing suite is not supported
by this local Windows Bash environment: its attempted21-test run failed11 with
2errors (including the initial missing validator invocation, then corrected).
Hosted Ubuntu routing21/21 and source-bound native PR/Main analysis remain
required. No local full CI, Security/Taint completeness or official GatePASS is
claimed. Historical Main failures and remaining parser/CFG/CPD warnings remain.

STUDENT_COUNCIL_PILOT = RELEASE_NO_GO.
