# Native coverage for runtime image integrity

Tracking: #615. Baseline: Main `653760b17ebf15f7a3b8921bd7ab5d7cfbce2f0c`.

## Problem and change

The official Main Sonar file list reported zero coverage and 225 uncovered new lines for `scripts/ci/runtime_image_identity.py`. Its existing positive and negative controls in `scripts/ci/test-main-runtime-artifact.py` ran outside the curated native coverage invocation.

Add that operational module to the existing narrow Python coverage subjects and execute the existing 45-control file in the pre-credential measurement loop. Extend the exact subject-set regression accordingly. Child Python processes continue to use the existing process-coverage bootstrap. No new production logic, archive limit, identity assertion, timeout, retry, exception waiver, exclusion, rule, New Code definition or Quality Gate threshold is introduced.

The existing controls cover valid archive/source/engine binding, modified or malformed archives, unsupported headers, incorrect source stamps and tags, legacy auxiliary identity, altered proof, descriptor/platform mismatch, retagging, container continuity, downstream receipts and functional harness verification. These are synthetic archive and CLI tests with controlled Docker substitutes; they are not actual Docker/backend acceptance evidence.

## Local verification

- Exact coverage subject/process guards: 7 PASS.
- Ubuntu / Python 3.14.4, immutable Git LF export plus the three measurement changes: all 45 existing controls PASS in 241.628 seconds under coverage.py 7.17.0 with its Python tracer.
- Measured operational module: 228 statements, 3 missed; 44 branches, 2 partial; combined coverage.py result 98%. This is not Sonar New Code Coverage or an official Gate result.
- Existing runtime image implementation is unchanged.
- Additional routing suite remains locally unqualified: Windows 23 tests produced 10 failures / 2 errors with cp932 decoding exceptions; Linux 23 tests produced 9 failures because jq was absent (exit127). Preserve these environment failures. Hosted Linux routing/required checks remain mandatory; no mock jq or expectation change was used.

Preserve preparation failures: native Windows execution ran 45 tests and failed with 31 failures / 83 errors, including cp932 decoding errors. Two WSL executions against CRLF-converted exports failed with 52 subtest failures each, including Bash rejecting the CR in `pipefail`. Windows Git's archive conversion retained CRLF despite the repository blob being LF. The qualifying export explicitly disables autocrlf and sets LF; the shell source was compared byte-for-byte with its immutable Git blob before execution. No Product shell logic, control, assertion or budget was changed to obtain success.

Hosted Linux execution and source-bound native artifacts must independently qualify the eventual PR HEAD and resulting Main. Local results and earlier Main results cannot be promoted to a new source. Main's existing official Gate failure, historical failures, formal Owner holds and `STUDENT_COUNCIL_PILOT = RELEASE_NO_GO` remain in force.
