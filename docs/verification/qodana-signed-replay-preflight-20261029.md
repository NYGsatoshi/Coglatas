# Qodana signed provenance and Cloud replay preflight — 2026-10-29

Target: **`SecurityTestImplimation`** only. Related tracking [Issue #1500](https://github.com/NYGsatoshi/Coglatas/issues/1500). This is **strict shadow qualification**, not a live Cloud upload or an authorized change to Main CI.

## Why the existing ZIP correlation is insufficient

The Qodana run-attempt/ZIP integrity validator introduced in PR #1509 proves that two artifact ZIPs match the digest and metadata returned by GitHub for a given Main run and attempt. It **cannot cryptographically attribute the report to the actual successful producer job**. Until a trusted workflow signs the original SARIF itself, the report may not be reused as a replacement for the independently executed Cloud analyzer.

GitHub supports [artifact attestations](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations) with a short-lived Sigstore signature and [CLI verification](https://cli.github.com/manual/gh_attestation_verify). The CLI offers `--signer-workflow`, `--signer-digest`, `--source-digest`, and `--source-ref` restrictions. The verifier must use these restrictions rather than accepting unsigned JSON or ordinary artifact filenames as provenance.

## Implemented (no additional privileges)

- `scripts/quality/verify_qodana_signed_subject.py` validates the unchanged original `qodana.sarif.json` for the expected Main source SHA, Actions run ID and SARIF provenance. It **invokes** `gh attestation verify` on that exact file; does not load an unverified attestation sidecar. GitHub's CLI checks the signed signer workflow, source and signer SHA, Main ref, SLSA predicate and repository. An additional policy checks the parsed certificate's stable repository ID, digest, workflow ref, trigger, exact run/attempt invocation URI, hosted runner identity, signed SHA-256 file subject and signed timestamp presence.
- `scripts/quality/qodana_cloud_send_preflight.py` checks that the same existing Qodana report directory contains the expected SARIF and HTML/report supporting files, all nonempty, bounded and not symlinked. It intentionally **cannot invoke `qodana send`**, even if an ambient `QODANA_TOKEN` is present. It emits only a sanitized, explicitly non-qualifying JSON layout receipt.
- Independent offline negative-control test suites exercise tampered certificate identities, wrong signed subject, invalid JSON, missing CLI, failures, wrong report SHA/run/attempt, symlinks, missing report files, and ambient token isolation. Tests are invoked by the existing GitHub Actions Static Analysis workflow with `contents: read` and no `id-token`, `attestations` or Cloud project token permissions.

## Expected current behavior

These historical Main reports are **not presently signed by the proposed producer workflow**. Consequently a live call to the signed-subject verifier is expected to reject the report until a correctly approved trusted signature exists. The offline synthetic positive cases only validate the verifier's strict parsing/argument contract; they are **not successful cryptographic attestations**. The receipt retains `producerJobAttested=false`, `cloudReplayVerified=false`, `qualifiedForGateReuse=false` even after successful signed-subject verification.

Official Qodana CLI documentation provides [`qodana send --results-dir`](https://github.com/JetBrains/qodana-cli#send) to send an existing report to Cloud. Its real invocation is **not included in this PR**. Before enabling, require an approved immutable Qodana CLI binary/version with reviewed SHA-256, trust-bound token delivery only from an authorized Main Cloud publisher, actual Cloud ingestion confirmation, and all current SARIF GitHub Code Scanning category/project-model and ratchet behavior.

## How the read-only checks are intended to be used

```bash
# Offline, always non-publishing; use a locally restored original Qodana results directory.
python3 -B scripts/quality/qodana_cloud_send_preflight.py \
  --results-dir /path/to/original-results --source-sha <main-commit-sha> \
  --run-id <main-workflow-run-id> --run-attempt 1 \
  --output /path/to/new/replay-layout.json

# Read-only GH signed-subject verification: fails closed without a real matching signature.
python3 -B scripts/quality/verify_qodana_signed_subject.py \
  --report /path/to/original-results/qodana.sarif.json \
  --source-sha <main-commit-sha> --run-id <main-workflow-run-id> \
  --run-attempt 1 --output /path/to/new/verified-subject.json
```

## Authorization and remaining gates

Current `governance/workflow-trust-policy.json` permits only narrowly enumerated write scopes. The existing approved Action SHA list does **not** include `actions/attest` or a Qodana CLI publisher binary. Therefore this PR intentionally does not add `id-token: write`, `attestations: write`, a new signer, Cloud secrets, `pull_request_target`, a changed Main workflow trigger, or an upload step.

To qualify real producer provenance, a separate reviewed PR must pin the signing Action, amend the narrow trust-policy exception for a trusted Main producer, sign the **original report before artifact upload** under the exact runner identity, and verify the result from a different job. A signed but otherwise untrusted workflow is not sufficient: the signer must enforce the checked, unmodified analyzer and exact SHA. Only after a separately verified and reviewed Cloud replay may the second Qodana analyzer be considered for retirement. No Main merge is authorized.
