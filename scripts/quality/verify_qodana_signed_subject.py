#!/usr/bin/env python3
"""Gate a Qodana SARIF subject against a real GitHub-signed attestation.

The production verifier INVOKES "gh attestation verify" itself. It does not
trust a submitted JSON sidecar or merely inspect an unsigned predicate.
Successful subject verification does not certify the upload job, the existing
artifact-ZIP correlation, or a Cloud replay; no gate reuse is authorized.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import Any, Callable

from qodana_sarif_parity import MAX_BYTES, ParityError, REPOSITORY, read_report

REPO = REPOSITORY.removeprefix("https://github.com/").removesuffix(".git")
REPO_ID = "1261244608"
SIGNER_WORKFLOW = f"{REPO}/.github/workflows/qodana_trusted_gate.yml"
SIGNER_REF = f"https://github.com/{SIGNER_WORKFLOW}@refs/heads/main"
SIGNER_BRANCH_REF = "refs/heads/main"
PROVENANCE_TYPE = "https://slsa.dev/provenance/v1"
SHA = re.compile(r"^[0-9a-f]{40}$")
RUN = re.compile(r"^[1-9][0-9]{0,18}$")
MAX_VERIFICATION_BYTES = 2 * 1024 * 1024


def reject(reason: str) -> None:
    raise ParityError(reason)


def _unique(fields: list[tuple[str, Any]]) -> dict[str, Any]:
    value: dict[str, Any] = {}
    for key, item in fields:
        if key in value:
            reject("Duplicate verification JSON field")
        value[key] = item
    return value


def _nonfinite(_value: str) -> None:
    reject("Non-finite verification field")


def verified_statement(blob: bytes, expected_digest: str, sha: str, run_id: str, attempt: int) -> None:
    if not isinstance(blob, bytes) or not 0 < len(blob) <= MAX_VERIFICATION_BYTES:
        reject("Missing or oversized verified-attestation result")
    try:
        data = json.loads(blob.decode("utf-8"), object_pairs_hook=_unique, parse_constant=_nonfinite)
    except (UnicodeError, json.JSONDecodeError, RecursionError) as exc:
        raise ParityError("Malformed verification response") from exc
    if not isinstance(data, list) or len(data) != 1 or not isinstance(data[0], dict):
        reject("Expected exactly one verified attestation")
    verification = data[0].get("verificationResult")
    if not isinstance(verification, dict):
        reject("Missing verified attestation")
    signature = verification.get("signature")
    cert = signature.get("certificate") if isinstance(signature, dict) else None
    timestamps = verification.get("verifiedTimestamps")
    statement = verification.get("statement")
    if not isinstance(cert, dict) or not isinstance(timestamps, list) or not timestamps:
        reject("Missing signed certificate or timestamp")
    if not isinstance(statement, dict) or statement.get("predicateType") != PROVENANCE_TYPE:
        reject("Unexpected provenance predicate")
    subjects = statement.get("subject")
    if not isinstance(subjects, list) or len(subjects) != 1 or not isinstance(subjects[0], dict):
        reject("Expected one exact signed file subject")
    subject = subjects[0]
    if subject.get("name") != "qodana.sarif.json" or subject.get("digest") != {"sha256": expected_digest}:
        reject("Signed subject is not the inspected original SARIF")

    expected_invocation = f"https://github.com/{REPO}/actions/runs/{run_id}/attempts/{attempt}"
    expected_claims = {
        "sourceRepositoryIdentifier": REPO_ID,
        "sourceRepositoryDigest": sha,
        "githubWorkflowSHA": sha,
        # Sigstore certificates distinguish the ref from the signer workflow URI.
        "githubWorkflowRef": SIGNER_BRANCH_REF,
        "githubWorkflowTrigger": "push",
        "buildSignerURI": SIGNER_REF,
        "runInvocationURI": expected_invocation,
        "runnerEnvironment": "github-hosted",
    }
    for field, expected in expected_claims.items():
        actual = cert.get(field)
        if not isinstance(actual, str) or actual != expected:
            reject("Signed source, workflow or execution identity mismatch")
    # Current sigstore-go certificate summary emits SAN as a URI string,
    # not a historic object. The caller-supplied predicate cannot override it.
    signer_san = cert.get("subjectAlternativeName")
    if not isinstance(signer_san, str) or signer_san != SIGNER_REF:
        reject("Signed workflow certificate SAN mismatch")
    # The GitHub CLI additionally enforces --source-ref, source-digest,
    # signer-workflow, signer-digest and repository against its trust roots.


def verify_signed_subject(
    report: Path,
    source_sha: str,
    run_id: str,
    attempt: int,
    *,
    cli: str | None = None,
    runner: Callable[..., subprocess.CompletedProcess[bytes]] = subprocess.run,
) -> dict[str, Any]:
    if not SHA.fullmatch(source_sha) or not RUN.fullmatch(str(run_id)):
        reject("Invalid source revision or run ID")
    if type(attempt) is not int or not 1 <= attempt <= 100:
        reject("Invalid workflow attempt")
    if report.is_symlink() or not report.is_file() or report.name != "qodana.sarif.json":
        reject("Expected an original, independent Qodana SARIF file")
    if not 0 < report.stat().st_size <= MAX_BYTES:
        reject("Missing or oversized original SARIF")
    _, original_sha, _, counts = read_report(report, source_sha, str(run_id), "main")
    binary = cli if cli is not None else shutil.which("gh")
    if not binary or not Path(binary).is_file():
        reject("GitHub attestation verifier is unavailable")

    command = [
        str(binary), "attestation", "verify", str(report),
        "--repo", REPO,
        # --signer-repo and --signer-workflow are mutually exclusive.
        # The full exact workflow path also binds the owning repository.
        "--signer-workflow", SIGNER_WORKFLOW,
        "--signer-digest", source_sha,
        "--source-digest", source_sha,
        "--source-ref", SIGNER_BRANCH_REF,
        "--predicate-type", PROVENANCE_TYPE,
        "--format", "json",
    ]
    # No shell; keep sensitive verification diagnostics out of logs.
    try:
        completed = runner(command, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                           timeout=45, check=False)
    except (OSError, subprocess.TimeoutExpired, ValueError) as exc:
        raise ParityError("Attestation verification failed to run") from exc
    if completed.returncode != 0:
        reject("No matching cryptographically verified attestation")
    verified_statement(completed.stdout, original_sha, source_sha, str(run_id), attempt)
    return {
        "schemaVersion": 1,
        "status": "SIGNED_SARIF_SUBJECT_VERIFIED_ONLY",
        "sourceSha": source_sha,
        "runId": str(run_id),
        "runAttempt": attempt,
        "originalSarifSha256": original_sha,
        "resultCount": counts[0],
        "ruleCount": counts[1],
        "artifactCount": counts[2],
        "signerWorkflow": SIGNER_WORKFLOW,
        "signerWorkflowDigest": source_sha,
        "signedSubjectVerified": True,
        "producerJobAttested": False,
        "cloudReplayVerified": False,
        "qualifiedForGateReuse": False,
    }


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description="Fail-closed signed Qodana SARIF verification")
    p.add_argument("--report", required=True, type=Path)
    p.add_argument("--source-sha", required=True)
    p.add_argument("--run-id", required=True)
    p.add_argument("--run-attempt", required=True, type=int)
    p.add_argument("--output", required=True, type=Path)
    args = p.parse_args(argv)
    try:
        if args.output.exists() or args.output.is_symlink():
            reject("Cannot overwrite signed report receipts")
        summary = verify_signed_subject(args.report, args.source_sha, args.run_id, args.run_attempt)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.NamedTemporaryFile("w", encoding="utf-8", dir=args.output.parent,
                                         prefix=".qodana-subject-", delete=False) as handle:
            temporary = Path(handle.name)
            try:
                json.dump(summary, handle, sort_keys=True, indent=2)
                handle.write("\n")
            except BaseException:
                temporary.unlink(missing_ok=True)
                raise
        temporary.replace(args.output)
        print("Qodana signed SARIF subject VERIFIED; producer job and Cloud replay NOT qualified.")
        return 0
    except (ParityError, OSError, ValueError, TypeError, RecursionError):
        # Never print token, command arguments, SARIF contents, or CLI stderr.
        print("Qodana signed-subject verification REJECTED (fail closed).", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
