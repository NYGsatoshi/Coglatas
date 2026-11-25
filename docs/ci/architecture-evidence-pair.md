# Same-job Architecture source/native evidence pair (non-required)

## Purpose and scope

The Architecture advisory in the unmerged candidate #1882 already
independently rejects missing/duplicate/failed native Architecture results,
discovers 13 bounded C# Fact definitions, and verifies exact source/TRX
testName-set equality. The original native receipt was retained as a GitHub
Artifact, but source/native name-set parity was printed only to the job log.
That made the source/native content result harder to inspect together with
the uploaded native receipt.

This change adds a sanitized source/native parity sidecar to the SAME
existing Architecture advisory Artifact. An independent data-only pair
guard checks the two source/run/attempt claims, exact schema, 13-case
counts, the native receipt's original names digest, and the source/native
domain-separated name-set digest before the original upload step.
The original architecture test execution, six protected Required Checks,
test outcomes, security scanners, runner allocation, trust permissions and
product behavior remain unchanged. There is no second Artifact owner.

## Evidence schema and native output

The existing local parity report adds one independently recomputed field:
nativeReceiptCaseIdsSha256. It is copied from the original native verifier
after all native result identities and counters have been validated. The
parity CLI has a new optional --output path, restricted to an exclusively
created regular file named architecture-source-native-parity.json directly
under the producing runner's RUNNER_TEMP. Default stdout JSON remains
available for read-only local use.

The pair verifier strictly parses two local inputs:
- architecture-advisory-receipt.json
- architecture-source-native-parity.json

They must be bounded, regular and in the same trusted runner temporary
directory. The reader checks the leaf and opened descriptor identity, reads
through that one bounded descriptor and rechecks its identity, size, mtime
and ctime afterward. It rejects same-size path replacements, symlinked leaves,
and changed file metadata rather than treating a preliminary pathname check as
proof of the bytes actually consumed. Unknown schema fields, duplicate JSON
keys, malformed encodings, non-finite values, inconsistent source SHA/run ID/run
attempt claims, failed test states, unauthorized success/producer claims and
mismatched digests all fail closed. The ten existing synthetic Python methods
now also require a genuine symlink control: a platform that cannot create the
test symlink fails the control rather than silently reporting a skip/pass.
These controls still cannot authenticate producer identity or artifact bytes.

The existing upload-artifact job publishes both sanitized JSON documents
under its existing uniquely named Artifact. Neither report contains raw
source code, test titles, browser content, secrets or tokens. Both outputs
are bounded, but their JSON fields are still self-declared workflow
observations, not independent GitHub API attestation.

The pair verifier reports SAME_JOB_RECEIPT_CONTENT_MATCH only if both
documents agree. This is a content correlation of the local data produced
by a workflow. It does not independently authenticate the artifact archive
bytes, job producer identity, source revision, reviewed head/merge
association, or any compiled .NET SDK TestCase IDs.

All wider qualification flags remain false:

- producerAuthenticated=false
- nativeSdkCaseIdsVerified=false
- artifactProvenanceVerified=false
- requiredCheckQualified=false

## Validation and refusal controls

Run the focused controls:

    python3 -B scripts/ci/test_architecture_evidence_pair.py

The producing Architecture advisory runs the existing native TRX
validation, the existing source/native name-set verification with
--output $RUNNER_TEMP/architecture-source-native-parity.json, then the
new evidence-pair verifier using the same GITHUB_SHA, GITHUB_RUN_ID
and GITHUB_RUN_ATTEMPT arguments before uploading.
The pair verifier refuses inconsistent metadata/digests and prints
only ARCHITECTURE_EVIDENCE_PAIR_REFUSED on error.

The exact reviewed PR head, tested merge revision and GitHub Artifact
ID/ZIP digest must be authenticated separately under D10. Capturing these
two JSON files together is not that authentication. A completed job,
negative-control test pass, or manual review cannot replace CodeRabbit
independent review or Owner governance authorization.

## Dependencies and rollback

This is a stacked candidate on #1882, with the original #1882 and its
preceding #1880/#1878 source identities retained. It is not a merge
request. Do not treat the prior #1882 native success as success on the
modified head; require fresh native execution.

Rollback removes the new pair checker, focused tests and documentation,
returns the parity CLI to its prior stdout-only usage, and restores the
original one-file upload path. Preserve original unsuccessful attempts
and all separate PR evidence.

PROGRAM_STATUS=INCOMPLETE
SECURITY_BRANCH_INTEGRATION=HOLD
MAIN_UNCHANGED=true
GOVERNANCE_IDENTITY_DRIFT=OWNER_HOLD
INDEPENDENT_REVIEW_COMPLETE=false
ARTIFACT_PROVENANCE_VERIFIED=false
REQUIRED_GATE_QUALIFIED=false
