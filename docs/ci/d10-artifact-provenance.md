# D10-A: independent native capture artifact verification

This candidate implements a bounded N04 verifier. It does not publish artifacts,
admit capture, change workflows, modify governance, merge a PR or qualify a
Required Check. D01–D15 and [the #1747 PR-N contract](https://github.com/NYGsatoshi/Coglatas/pull/1747)
remain binding. #1717 and #1755 are unmerged dependencies; their local content
results and #1755's original rejection remain independently preserved.

## Reuse and independent authority

The implementation reuses `sec_arch_github_provenance.LiveGitHub` and its bounded
pagination, strict JSON and fixed HTTPS origin. The binary transport adds a bounded
artifact download with one validated storage redirect and no credential forwarding.
It reuses `sec_arch_reconcile.verified_zip`, bounded member reads and duplicate-key
checks. The shared ZIP helper gains optional tighter limits; its historical
512 MiB / 1 GiB / 256-member defaults remain unchanged and cannot be expanded.
Existing release/MVP-A and Qodana verifiers were inspected; their domain-specific
run/receipt policies are not substituted for the new Playwright contract.

The [GitHub artifact API](https://docs.github.com/en/rest/actions/artifacts) exposes
run ownership, not an upload job or upload attempt. Therefore **v1 supports only
attempt 1 with exactly one successful API-enumerated job**. Multi-job runs and
reruns are rejected rather than authenticated from candidate-controlled receipts,
artifact names or console text. This is an explicit admission restriction, not a
claim that the existing multi-job PR CI already satisfies it. Future broader
admission needs a separately verified job/attempt binding, potentially an approved
attestation mechanism; no OIDC, signing or publication permissions are added here.

The expected policy comes from the independent verifier operator, with its exact
SHA-256 supplied separately. Never obtain that expected hash or expected source
roster/applicability digests from the artifact being verified. A policy in the
candidate repository does not approve itself. Owner admission and independent
review remain separate; the verifier never grants either.

`coglatas.d10-policy.v1` has exactly these fields:

```text
schema, repository, repositoryId, prNumber, reviewedHeadSha, baseSha,
testedMergeSha, sourceTreeSha, workflowId, workflowPath, workflowSourceSha256,
producerWorkflowSourceSha256,
runId, attempt, jobId, artifactId, sourceRosterSha256, applicabilitySha256
```

Repository ID is pinned to Coglatas, the base to SecurityTestImplimation, the
workflow path to `.github/workflows/playwright-native-capture-shadow.yml`, producer
name to `Shadow Playwright native capture / Native capture`, role to `PLAYWRIGHT_STATIC_SHADOW_V1`
and artifact name to `playwright-native-shadow-v1`. That workflow is not activated
or registered by this PR. Unknown policy fields and unsupported versions reject.

V1 requires the complete tested Git tree to equal the independently expected
reviewed-head tree. It separately verifies the tested merge's two exact parents,
current PR head/base/merge and current Security base. Nonidentical trees reject;
there is no implicit subset-equivalence waiver. Reviewed head and tested merge
remain distinct fields even when their trees are identical.

The run's server-provided `referenced_workflows` must contain exactly the local
reusable `.github/workflows/playwright-native-capture-producer.yml`, with the exact
tested merge SHA in both its path suffix and SHA and `refs/pull/<number>/merge`
as its ref. The run's PR head/base association must also match. Absent, ambiguous,
foreign or unsupported revision bindings reject. Neither run `head_sha`, today's
PR merge ref nor a candidate receipt proves the actual tested merge. The independent
policy additionally pins `producerWorkflowSourceSha256`; both caller and reusable
producer bytes are independently read at the server-bound revision and hashed.
Operator admission must review their exact local-call and checkout behavior.

Live API checks cover repository IDs, PR/source identity, workflow ID/path/state
and immutable workflow bytes (SHA-256 plus Git blob identity), Run ID/attempt,
successful job identity and timestamps, GitHub Actions check application and check
suite, exact artifact ID/name/run/ownership, complete listings, expiry, size and
digest. Standard vendor REST ancillary fields are excluded from the authoritative
projection; they cannot supply provenance claims. All versioned policy and report
schemas are strict. Missing critical API fields, partial pages and unknown critical
identity/outcome values reject. Authority is read again after content verification;
a changed or unavailable final snapshot invalidates the qualification.

## Archive and report contract

ZIP downloads are bounded to 16 MiB. Expanded data is bounded to 8 MiB, with four
nonempty JSON files of at most 2 MiB each. Unknown/duplicate members, path traversal,
directories, links, encryption, unsupported compression, excessive compression
ratios, trailing archive data, malformed JSON and duplicate JSON keys reject.
Members are read without extraction from an owned, hashed snapshot.

The only members are:

- `source.json`: unchanged #1717 source-discovery v1 record.
- `applicability.json`: #1717's separately versioned source-applicability sidecar.
- `native.json`: #1755's original native case snapshot, using GITHUB_ACTIONS mode.
- `capture-receipt.json`: the strict capture identity receipt below.

The receipt has exactly `schema`, `repository`, `reviewedHeadSha`, `testedMergeSha`,
`workflowId`, `runId`, `attempt`, `jobId`, `producerRole` and `reportDigests`.
Its schema is `coglatas.playwright-capture-receipt.v1`; reportDigests maps the other
three exact filenames to independently recomputed raw-byte SHA-256 values. Receipt
fields are only claims to corroborate against the independent API observations.
They never authenticate the producer. Artifact ID and archive digest come from
the server; they are not circular self-declarations inside the ZIP.

Source, applicability and native reports must identify the actual tested merge.
The reviewed head is retained separately in the receipt and result. Original native
run/attempt, case statuses and retries remain intact. Independent expected roster
and applicability digests, exact ID sets and complete status semantics are checked
through the original pinned #1755/#1717 content routines.

No implementation is copied from those unmerged PRs. `--content-root` supplies
their two original Python modules, each checked against a fixed canonical source
digest before import into an owned snapshot. The native guard is pinned to
#1755@4ca1c2834e17f9da623ac0520291fdf1b97383b5 and applicability policy to
#1717@37e7b381c382b6207412db4c42cb948e79c3f526. Missing, changed or unavailable
dependencies fail closed. Canonical Python code handles Windows CRLF/LF; artifact
and report byte digests never undergo text normalization.

## Result states and use

The deterministic `coglatas.d10-verification.v1` result separates METADATA_VERIFIED,
ARCHIVE_BYTES_VERIFIED, REPORT_IDENTITY_VERIFIED, PRODUCER_AUTHENTICATED,
CONTENT_RECONCILED and PROVENANCE_VERIFIED. A single-job, first-attempt server
association is required for producer authentication. All mandatory checks must
succeed before ARTIFACT_PROVENANCE_VERIFIED becomes true. REQUIRED_GATE_QUALIFIED
always remains false. Owner approval, governance, review, security, Nightly and
release states are not promoted by any result. Synthetic test APIs and archives
qualify implementation controls only; they never prove native artifact production.

```sh
python3 -B scripts/ci/d10_provenance.py --policy /trusted/policy.json --policy-sha256 TRUSTED_SHA256 --content-root /trusted/composed/scripts/ci
```

Policy files and content module paths must be bounded regular files with no
symlink/reparse ancestors. Output is sanitized JSON on stdout; the caller owns
safe, exclusive retention. Invalid arguments and failures emit only fixed codes.
The existing GitHub token is read from GH_TOKEN/GITHUB_TOKEN and is never printed.

Availability can be observed without admitting anything:

```sh
python3 -B scripts/ci/d10_provenance.py --audit-pr 1717 --audit-head 37e7b381c382b6207412db4c42cb948e79c3f526 --audit-run 42190254168
```

An absent artifact is explicitly NATIVE_CAPTURE_ARTIFACT_ABSENT, with no Producer
Job ID, Artifact ID, archive/report digests or case-count qualification. A name
match is only CANDIDATE_PRESENT_NOT_AUTHENTICATED. Both observations exit nonzero
with every provenance state false.

Run focused controls with D10_CONTENT_ROOT explicitly pointing to the two pinned
modules, then `python3 -B scripts/ci/test_d10_provenance.py`. No module absence may
be converted to a skip or a synthetic substitute content verifier. Original
SEC-ARCH archive/provenance controls must also pass after the tighter-profile API
extension. Preserve failed/cancelled attempts and unsuccessful reviews separately.

Phase B may only be prepared after this contract and its negative controls are
technically qualified. A new runner or publication requires separate approval;
an inert candidate does not establish authorized execution. Phase C remains HOLD
until authentic native source/capture/artifact evidence exists. Rollback reverts
the additive verifier and optional ZIP-profile extension; original evidence is
retained and existing ZIP defaults are restored without a policy change.

```text
PROGRAM_STATUS=INCOMPLETE
SECURITY_BRANCH_INTEGRATION=HOLD
MAIN_UNCHANGED=true
GOVERNANCE_IDENTITY_DRIFT=OWNER_HOLD
TRUSTED_SONAR_IMPORT=NOT_VERIFIED
PR_J_TO_N_ACCEPTANCE=OPEN
REQUIRED_GATE_QUALIFIED=false
```
