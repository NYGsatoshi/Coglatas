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


## Exact validator security findings and candidate boundary

Original0978 native40830501461/attempt2/job123305560485 completed, but official
Gate123312325910 FAIL63.0%/SecurityC. ProviderOPEN findings are
AaGGe5XStqzpX64Gak4T/jssecurity:S8707/High/line43 (CLI-selected report read)
andAaGGe5XStqzpX64Gak4R/javascript:S4036/Low/line11 (bare Git inventory).
No confidential-content disclosure or higher-privilege attacker invocation was
established. No Product authorization vulnerability or formal Security approval
is inferred. Original attempt1 analysisSKIP remains NOT_RUN.

Parent and fresh read-only investigator traced CLI, library, lexical/source
symlink and invocation-alias representations. The sole production workflow's
exact artifacts/sonar-node/lcov.info argument is retained. All other/missing/extra
arguments fail before report selection; argv is never used as a filesystem path.
Default root comes from the canonical module location, not caller cwd. The CLI
entry guard canonicalizes both module and invoked script so a directory alias
cannot silently exit0 without validation. Generic errors/count-only success
output retain no arbitrary report/error content.

The shared report/source reader confines the canonical repository path, rejects
nonregular files and symlinked components, opens the regular file, compares
opened dev/ino against pre-open metadata and rechecks canonical identity before
reading the descriptor. The descriptor is always closed. Linux additionally uses
O_NOFOLLOW; Windows uses identity checks and does not claim that POSIX flag.
This closes named argv/source alias routes without filtering, rewriting or
fabricating LCOV data. Test-only root/tracked/read seams remain trusted library
inputs, not exposed CLI options. Filesystem installation/workspace ownership and
inherited Git repository/configuration environment remain trust assumptions.

Git inventory now selects an intended fully qualified executable with no PATH
fallback: standard Windows C:/Program Files/Git/cmd/git.exe or hosted Linux
/usr/bin/git. Alternate/per-user/portable installs use explicit owner-trusted
COGLATAS_TEST_GIT_EXECUTABLE; empty/relative paths fail. An independent candidate
review identified Windows single-rooted paths as drive-dependent despite
path.isAbsolute. Parent reproduced acceptance of /ApprovedTools/git.exe, then
required a drive/UNC root, preserving fully qualified UNC syntax without executing
network binaries. This same correction is applied to the sibling1515fixture.
Alternate installs need the documented explicit setting; no arbitrary PR/request
command is accepted and no global environment setting is written.

Local syntax/import and12focused controls PASS,0skip: fixed producer positive;
missing/extra/absolute/traversal argument negatives; malformed report/private-text
no-echo; real Git inventory with hostile PATH; regular source/parent junction/
traversal/directory negatives; canonical module root and actual CLI alias denial;
raw-versus-mapped actual Playwright regressions and hit/uncovered retention.
Final-file symlink branch is NOT_RUN locally on Windows and required on Ubuntu;
Windows directory-junction behavior is exercised. The full producer cohort becomes
87old+12controls=99, awaiting hosted source-bound execution and official rescan.
The single candidate review cycle is complete; its concrete Windows concern was
confirmed and revised, with focused revalidation. Do not report fixed until all
relevant current-source hosted gates pass. #1520 separately proposes test-body
classification; it does not suppress these Security findings or change80%/A/A.
