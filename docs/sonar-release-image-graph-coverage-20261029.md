# Native coverage for existing release image-graph controls

Tracking: #615. Baseline Main `7ee07ac0166f540282b62dca94a359585d4a5779`, tree `ce2bb4e5b061244383a4d57c2306db6c35149784`.

## Problem and change

The operational `scripts/ci/release_image_graph.py` and its existing `tests/ci/test_release_image_graph.py` were absent from the exact native Python coverage subjects and curated control loop. Existing controls therefore did not contribute this operational module to that coverage report.

Add the operational module, invoke its existing 39 controls in the pre-credential loop, and extend the exact subject-set guard. These are three one-line instrumentation changes plus this evidence document. The graph implementation, test cases, workflow output/privilege policy, cryptographic requirements, release approval and deployment behavior are unchanged. No source/test exclusion, rule, New Code definition or 80%/A/A threshold changes.

## Verification

- Unchanged graph suite before instrumentation: 39/39 PASS, 1.558 seconds on Windows/Python3.12.
- Exact process/subject guards after instrumentation: 7/7 PASS, 1.735 seconds. Existing parent/child, branch, opt-in, source-copy denial and missing-root controls remain.
- Same graph suite with coverage.py7.17.0 Python tracer and repository bootstrap: 39/39 PASS, 2.501 seconds, including real Python CLI subprocesses. Five source-bound process data files combined with originals retained.
- Operational graph module: 252 statements / 7 missed, 40 branches / 2 partial, combined coverage.py result97%. This is a local measurement, not official Sonar New Code Coverage or a Quality Gate result.
- The inherited startup path emits an already-imported warning for `sitecustomize.py`; it is retained, not suppressed. It does not identify the graph module as already imported. No full analysis-completeness claim is made.
- Existing positive controls cover OCI index, direct manifest and legacy image transport. Negative controls reject subject/config/layer digest inconsistencies, platform/diff-ID substitution, scope shrinkage, ambiguous/attestation selection, archive path escape/symlink/duplicate entries, size/member/PAX/sparse limits, invalid strict JSON and overwrite of an existing output.
- All image/archive/JSON data is synthetic and local. No registry authentication, image publishing, signing, release workflow dispatch, live environment, protected account or actual deployment is used. This is ordinary coverage QA, separate from static scanner-finding triage, and supplies no runtime validation of a scanner claim.

Fresh hosted Linux controls, protected checks, independent review, official analysis and resulting Main qualification are required. Local frontend dependencies currently reflect the separate Lucide1.64 candidate; no frontend qualification is claimed here.

All historical failures and Owner holds remain. `STUDENT_COUNCIL_PILOT = RELEASE_NO_GO`.
