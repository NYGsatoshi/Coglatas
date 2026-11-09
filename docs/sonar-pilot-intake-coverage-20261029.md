# Native coverage for the local intake capture validator

Tracking: #615. Baseline: Main `2b52a6355844ef4aae33c58e2016336f361bf24d`.

## Problem and change

The existing operational `deploy/sakura/validate-pilot-intake-policy.py` appeared at zero coverage with 144 uncovered new lines in the official Main file inventory. Its existing capture and file-boundary controls were outside the curated native Python measurement invocation.

Add only that operational file to the exact coverage subjects and run the existing `deploy/sakura/test_validate_pilot_intake_policy.py` in the pre-credential loop. Extend the exact subject-set guard. No validator implementation, capture shape, flag interpretation, time limit, file-size bound, source/tenant binding, Owner approval requirement, Quality Gate threshold, rule or exclusion changes.

## Verification

- Current exact process/subject guards: 7 PASS.
- Existing intake suite: 13 PASS on Ubuntu / Python 3.14.4, under coverage.py 7.17.0 with the Python tracer, in 6.458 seconds.
- The validator, control file and process bootstrap were compared byte-for-byte with latest Main Git blobs before execution; the measurement configuration was overlaid into the LF source fixture.
- Operational module: 144 statements / 3 missed, 24 branches / 1 partial, combined coverage.py result 98%. This is not Sonar New Code Coverage or an official Quality Gate result.
- Tests exercise positive local-only captures, shape/type/duplicate/ambiguous flag rejection, tenant/source/time boundaries, invalid UTF-8, size bounds, FIFO/symlink/open-time replacement and growth rejection. POSIX file controls ran on Linux rather than being skipped or weakened for Windows.
- Existing positive controls retain `UNQUALIFIED`, `liveStateVerified=false` and omission of captured settings/features from public output. All data is synthetic; no deployment or network target is contacted.

Hosted Linux checks, independent review, official analysis and a new resulting Main qualification are still required. These synthetic local capture tests grant no live policy qualification, staging authorization, school admission, security approval or release GO. All formal Owner holds and `STUDENT_COUNCIL_PILOT = RELEASE_NO_GO` remain.
