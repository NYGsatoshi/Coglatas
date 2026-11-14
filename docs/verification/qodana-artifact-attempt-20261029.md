# Qodana artifact and run-attempt correlation — 2026-10-29

Integration branch: `SecurityTestImplimation`. Issue [#1500](https://github.com/NYGsatoshi/Coglatas/issues/1500). This is **advisory provenance verification**, not a Qodana analyzer or release gate replacement.

## Independent, live GitHub metadata

`scripts/quality/verify_qodana_artifact_provenance.py` reads the fixed `NYGsatoshi/Coglatas` REST endpoints using the caller's `GITHUB_TOKEN` with **Actions: read** access. The production CLI never accepts saved JSON metadata as an authenticity source. Only `/actions/runs/{id}`, and that run's paginated `jobs` and `artifacts` responses, are queried. A failure or missing token rejects the qualification, without printing untrusted SARIF contents or token values.

The verifier requires a completed Main CI push run with the exact workflow ID, repository ID, run attempt, Main source SHA, two successful distinct QDNETC jobs on that attempt, their successful inventory-upload steps, unique matching artifact IDs, unexpired artifacts attached to the same run/SHA/repository, and artifact creation within the matching job's execution window. It permits an overall Main workflow failure from an unrelated job **only** when both Qodana jobs individually succeeded.

Each supplied downloaded artifact ZIP is checked against the live GitHub artifact metadata's `sha256:` archive digest and size. ZIP entries are checked for duplicates, traversal, symlinks and bounded sizes. The canonical `qodana.sarif.json` inside each ZIP is passed through the existing strict full-document comparator, which permits only distinct `automationDetails.guid` fields to differ. Report files are not extracted into untrusted paths.

## Result semantics

The sanitized receipt declares `status=ATTEMPT_CORRELATED_NOT_PUBLISHER_ATTESTED`, `runAttemptVerified=true`, `artifactDigestVerified=true`, and `artifactCreationWindowsMatched=true`. Critically, `producerCryptographicallyAttested=false`, `cloudReplayVerified=false` and `qualifiedForGateReuse=false` remain hardcoded. Matching API job identity and upload time **does not cryptographically prove the uploading job**. A valid result cannot waive or replace either existing Qodana scanner and cannot authorize a Main or release decision.

## Example for a separately authorized, read-only verification

Use the genuine GitHub Actions artifact ZIP files and the read-only Actions token; do not put tokens in arguments, logs or uploaded reports.

```bash
python3 -B scripts/quality/verify_qodana_artifact_provenance.py \
  --run-id 40649034671 --run-attempt 1 \
  --source-sha cc83b0b6f2607e4705885ac6c1a0fed995dcfc0b \
  --community-artifact-id 12800071093 \
  --cloud-artifact-id 12799926762 \
  --community-zip /path/to/community.zip \
  --cloud-zip /path/to/cloud.zip \
  --output /path/to/new/advisory-receipt.json
```

Run 40649034671 was overall FAIL from another integration lane, while both Qodana jobs succeeded; this does not imply a release PASS. Its Community ZIP SHA-256 was `78452cab4511d79d327c64e242595348185b2c0c603a215adc3c30eced4158c2` and Cloud ZIP SHA-256 was `1933a55e4631f4e2c57cdd8ca31a731cc4e60f2bcaf7d0b50aff4bb9da568d6b` as recorded by GitHub Actions.

## Verification and next gates

- The no-network synthetic regression tests run from the existing GitHub Actions Static Analysis workflow for PRs targeting `SecurityTestImplimation` or `main` and relevant pushes. They test API metadata drift, stale attempts, failed jobs, ZIP digest/size corruption, unsafe ZIP entries, mismatched SARIF and absent tokens, using mocked responses only.
- Real ZIP files from Main run 40649034671 were checked with the local fixture adapter before opening this candidate PR. This **does not** replace a credentialed live-API validation by a trusted runner; that qualification is still pending.
- Next: attest or strongly bind the actual publishing job, validate replay into Qodana Cloud with the same original report, preserve existing Code Scanning categories and project-model ratchet, then evaluate cost. Do not change Qodana scan cadence or merge anything to Main without explicit owner approval.
