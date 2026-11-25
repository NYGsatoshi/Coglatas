# Architecture GitHub API metadata witness (offline read-only)

## Goal

This standalone Security-targeted Draft adds a bounded metadata consistency
auditor for the Architecture advisory. It complements the existing ZIP-byte
reader from #1884 and the native paired receipts from #1886 without changing
their source identities. This is not D10 producer authentication or release
authorization. It never calls GitHub APIs, requests tokens, publishes
artifacts, changes Main or activates new CI permissions.

The auditor accepts a JSON collection of operator-supplied GitHub API
responses from these read-only resources:

- One Pull Request (#1886 in the synthetic example)
- Its current SecurityTestImplimation base branch
- Its reviewed-head and tested-merge Git commit objects
- Its Architecture advisory workflow run
- The run's full, single-job listing
- The corresponding GitHub Actions check-run item
- The run's complete Artifact listing

The top-level JSON object has exactly these keys:
pr, base_branch, head_commit, merge_commit, run, jobs, check_run, artifacts.

Keep each response as the underlying GitHub API JSON object/list. Do not copy
Authorization headers, credentials, unrelated personal data, full logs, ZIP
bytes or user-sensitive fields into the snapshot; never commit a live
API snapshot to source control.

## Refusals and qualification

The verifier needs independently provided PR number, reviewed head SHA,
Security base SHA, tested merge SHA, Run ID, attempt, Job ID, Artifact ID,
API-reported ZIP SHA-256 and ZIP byte length.

It rejects mismatched PR/base/branch association, incorrect exact merge
parent ordering or Git tree, foreign repo IDs, changed workflow path/ID,
missing or ambiguous run PR associations, reruns, non-success states,
multiple jobs/artifacts, unsupported check app/check suite identity, stale
or foreign Artifact ownership claims, contradictory timestamps and an
Artifact digest that differs from the expected independent value.

This profile deliberately supports only the ordinary single-job
Architecture advisory, attempt 1. A reusable-workflow reference must be
absent. This ordinary workflow has no independent server Run binding
from its reviewed head to its actual checked-out merge revision. The
local git rev-parse HEAD == GITHUB_SHA execution check helps but cannot
be promoted to an independent GitHub API producer attestation.

The snapshot is a bounded regular JSON file. The reader checks the file
identity before opening, requests `O_NOFOLLOW` and `O_NONBLOCK` where
available (to avoid hanging if a checked regular file becomes a named pipe
before `open`), reads bounded bytes from that single descriptor, and
rechecks the descriptor identity, size, mtime and ctime after the read.
It rejects same-sized pathname replacement, detectable in-place mutations,
symlinked endpoints and ancestors, invalid UTF-8, duplicate JSON properties,
non-finite JSON and unsupported top-level keys. The existing eleven-method
synthetic suite covers the nonblocking open flag with a simulated FIFO
substitution, plus swap and post-read mutation controls, **without opening
an actual blocking pipe**. A platform unable to qualify the nonblocking or
symlink checks must fail rather than silently report a skip.
Metadata checks are not cryptographic file-content authentication. Only
the fixed error ARCHITECTURE_GITHUB_METADATA_REFUSED is printed on refusal.

A successful result is SUPPLIED_GITHUB_METADATA_CONTENT_MATCH only.
All wider authority flags remain false:

- gitHubResponseOriginAuthenticated
- testedMergeExecutionAuthenticated
- artifactBytesVerified
- producerAuthenticated
- artifactProvenanceVerified
- requiredCheckQualified

An attacker-controlled snapshot and attacker-controlled expectation
arguments cannot authenticate themselves. The checker never downloads or
compares ZIP bytes. #1884's separate ZIP reader does not establish live
metadata authority. Full D10 needs independently authenticated GitHub
API provenance, producer/job and tested merge execution binding, ZIP bytes,
approved review, governed Required Check identity and owner adoption.

## Example and commands

The synthetic fixture is shaped after actual read-only #1886 evidence:
reviewed head 88a7e54a021062ae0f7444ac8e47b1fc26cc3036, base
47dffab4ba29b451c56881c698a932ffeb97bc57, merge
1151920bea5c8280f86cad0336fbf111a0ba94f9,
Run 42846167969 attempt 1, Job 129934084581,
check suite 116036818242 and Artifact 13777452374.
The observed 1,136-byte ZIP SHA-256 was
9d6c564e556d523fc4dbd9876f7e3da01aae8dfbd2fc38e35f0d9f9d943b9ac1.

These are historical inputs, not a frozen ongoing trust anchor.
Neither live API response data nor the downloaded ZIP is committed.

Run the eleven focused synthetic controls:

    python3 -B scripts/ci/test_architecture_github_metadata.py

The optional read-only CLI accepts independently exported JSON:

    python3 -B scripts/ci/architecture_github_metadata.py \
      --snapshot /private/operator/architecture-api.json \
      --pr-number 1886 \
      --head-sha 88a7e54a021062ae0f7444ac8e47b1fc26cc3036 \
      --base-sha 47dffab4ba29b451c56881c698a932ffeb97bc57 \
      --merge-sha 1151920bea5c8280f86cad0336fbf111a0ba94f9 \
      --run-id 42846167969 --attempt 1 --job-id 129934084581 \
      --artifact-id 13777452374 --artifact-size 1136 \
      --artifact-sha256 9d6c564e556d523fc4dbd9876f7e3da01aae8dfbd2fc38e35f0d9f9d943b9ac1

The additive non-required advisory step runs only the eleven synthetic
offline controls. It makes no network request and does not modify
the existing native .NET tests, TRX reader, artifact upload,
required-check topology, permissions or runner count.

## Governance and rollback

Keep this Draft until exact-head native CI and prescribed independent
review are observed. #1886 successes belong solely to its previous
head. Preserve unsuccessful, skipped and rate-limited histories.

Rollback removes the witness, synthetic tests, documentation,
and its additive advisory step and path triggers, without altering
the original source PRs or Main.

PROGRAM_STATUS=INCOMPLETE
SECURITY_BRANCH_INTEGRATION=HOLD
MAIN_UNCHANGED=true
GOVERNANCE_IDENTITY_DRIFT=OWNER_HOLD
INDEPENDENT_REVIEW_COMPLETE=false
ARTIFACT_PROVENANCE_VERIFIED=false
REQUIRED_GATE_QUALIFIED=false
