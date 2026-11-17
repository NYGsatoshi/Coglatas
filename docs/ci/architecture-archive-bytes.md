# Architecture advisory ZIP bytes and paired report admission (offline)

## Purpose and authority

This is a Security-only, read-only offline verifier for the pair of sanitized
JSON files produced by the still-unmerged Architecture advisory candidate
[#1883](https://github.com/NYGsatoshi/Coglatas/pull/1883).
It does not run a browser, add a runner, publish an artifact, change any
workflow, modify protected Required Check names, or alter Main.

The verifier accepts a downloaded GitHub Actions ZIP archive plus
**externally supplied** expected archive SHA-256, archive byte length,
tested merge revision, Run ID, Run Attempt and source-roster SHA-256.
It recomputes the ZIP digest, validates exact ZIP member identities and
bounds, then checks that both versioned JSON receipts match all provided
identities, each other, the exact 13-case count and their separate
source/native roster digests.

### Archive limits and refusal

The supported archive is at most 64 KiB, with exactly two nonempty,
regular, unencrypted JSON members of at most 8 KiB each:

- architecture-advisory-receipt.json
- architecture-source-native-parity.json

Unexpected/duplicate/path traversal/linked members, alternate
compression, suspicious ratios, invalid ZIP central directory, malformed
or duplicated JSON properties, non-finite values and wrong schemas fail
closed. The local input is checked with `lstat`, then opened through
one bounded file descriptor (with no-follow where supported); the opened
regular-file identity and byte length must match the checked leaf. The
descriptor's identity, size, mtime and ctime are checked **again after the
bounded read** to reject same-size path swaps and detectable in-place
mutations. These metadata checks are not a cryptographic anti-race guarantee:
the independently supplied ZIP SHA-256 is still required. Symlinked input,
oversized archives and unsupported symlink test environments fail closed
rather than silently granting a skipped-positive result. No extraction occurs.

Receipt identity claims are strict. An exact ZIP-byte match is not
sufficient when its SHA, Run, Attempt, count, digest or status claims
contradict the externally supplied inputs. The result is
ARCHIVE_BYTES_AND_PAIR_CONTENT_MATCH. It never emits raw test names,
tokens, file contents or untrusted exception messages.

### Evidence limits

**This verifier does not call GitHub APIs.** The API-reported digest
and other values must be retrieved independently by its operator
and cannot be taken from the candidate ZIP. Even a caller's pinned
metadata is not independently authenticated by this command.

The CLI output *always* preserves the following false statuses:

- gitHubMetadataAuthenticated
- producerAuthenticated
- nativeSdkCaseIdsVerified
- artifactProvenanceVerified
- requiredCheckQualified

A full D10 admission still requires server-bound reviewed-head/tested
merge identity, workflow and Run/Job provenance, Artifact ID and producer
association, check-suite identity, independent source expectations,
native SDK Case ID coverage and appropriate governance/review approval.
An offline matching ZIP does not qualify PR-J-to-N acceptance.

## Actual source observation (separate, historical)

The Architecture advisory from #1883, source HEAD
4cc9cbec4526dfa84be30e9517659b182ab72e26,
was executed by GitHub Actions Run 42815795815, Attempt 1,
using tested merge 29ab41a60931b960e06a67465586afd6a2d034ae.

The independently downloaded, exact 1,136-byte Artifact ZIP
(Artifact ID 13763474889) had the same SHA-256 as the GitHub API
reported:

d70606586200dcec2f3f407f7915abb46f820b551d83cee7ae56d5eda1b0b5a4

The two contained JSON receipts both reported 13 cases and the
source/native name-roster digest:

31fbe9183aeb2fa6fca17a9af1fc5618e157922858acf446a1de623201d40d84

The git merge object had exact parents
47dffab4ba29b451c56881c698a932ffeb97bc57
(Security base) and 4cc9cbec4526dfa84be30e9517659b182ab72e26
(candidate head). This evidence applies to the above exact Run
and Artifact, not to this new verifier's own source HEAD.

These observations are not a D10 signed provenance record or an
Owner release decision. No GitHub API responses, tokens or ZIP bytes
are committed into this PR.

## Commands and validation

Focused isolated controls:

    python3 -B -m unittest discover -v -s scripts/ci -p test_architecture_archive_bytes.py

The offline command requires a separately downloaded local ZIP:

    python3 -B scripts/ci/architecture_archive_bytes.py \
      --archive /private/operator/download.zip \
      --api-digest d70606586200dcec2f3f407f7915abb46f820b551d83cee7ae56d5eda1b0b5a4 \
      --api-size 1136 \
      --tested-merge-sha 29ab41a60931b960e06a67465586afd6a2d034ae \
      --run-id 42815795815 --attempt 1 \
      --expected-roster-sha256 31fbe9183aeb2fa6fca17a9af1fc5618e157922858acf446a1de623201d40d84

For the original candidate revision, the twelve synthetic positive/negative
tests were run locally, 12/12 passed. The current source-owner revision adds
descriptor post-read and file-swap negative assertions **inside the same
twelve test method identities**; their current-head CI has not yet qualified
this change. The previously observed exact downloaded ZIP
also returned ARCHIVE_BYTES_AND_PAIR_CONTENT_MATCH with all five
authority/gate flags false.

These local results do not count as native GitHub Actions CI,
current-head CodeRabbit review or protected-check acceptance for
this PR. Keep it Draft until the actual exact-head native checks
and required independent review are qualified.

Rollback removes only the offline verifier, its test file and
this documentation. Retain all prior native failures, skips,
CI attempts, source snapshots and Artifact metadata.

PROGRAM_STATUS=INCOMPLETE
SECURITY_BRANCH_INTEGRATION=HOLD
MAIN_UNCHANGED=true
GOVERNANCE_IDENTITY_DRIFT=OWNER_HOLD
INDEPENDENT_REVIEW_COMPLETE=false
ARTIFACT_PROVENANCE_VERIFIED=false
REQUIRED_GATE_QUALIFIED=false
