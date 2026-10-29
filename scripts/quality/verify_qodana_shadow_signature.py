#!/usr/bin/env python3
"""Verify the integration-branch Qodana signed subject, never a Main release gate.

The GitHub CLI performs actual Sigstore verification; JSON from a sidecar is
not an authority. Only the repo's fixed SecurityTestImplimation push workflow
may qualify this **shadow** subject. It cannot qualify Main/Cloud reuse.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import Any, Callable

from qodana_sarif_parity import MAX_BYTES, ParityError, REPOSITORY, read_report, unique_object, reject_constant

REPO = REPOSITORY.removeprefix("https://github.com/").removesuffix(".git")
REPO_ID = "1261244608"
BRANCH = "SecurityTestImplimation"
WORKFLOW = f"{REPO}/.github/workflows/qodana-signed-shadow.yml"
WORKFLOW_REF = f"https://github.com/{WORKFLOW}@refs/heads/{BRANCH}"
PREDICATE = "https://slsa.dev/provenance/v1"
MAX_GH_BYTES = 2 * 1024 * 1024


def reject(message: str) -> None:
    raise ParityError(message)


def verified_statement(blob: bytes, digest: str, sha: str, run_id: str, attempt: int) -> None:
    if not isinstance(blob, bytes) or not 0 < len(blob) <= MAX_GH_BYTES:
        reject("Absent or oversized signature verification output")
    try:
        result = json.loads(blob.decode("utf-8"), object_pairs_hook=unique_object, parse_constant=reject_constant)
    except (ValueError, UnicodeError, RecursionError) as exc:
        raise ParityError("Malformed signature verification result") from exc
    if not isinstance(result, list) or len(result) != 1 or not isinstance(result[0], dict):
        reject("Expected one verified signed statement")
    verification = result[0].get("verificationResult")
    if not isinstance(verification, dict):
        reject("No verified signature result")
    signature = verification.get("signature")
    cert = signature.get("certificate") if isinstance(signature, dict) else None
    ts = verification.get("verifiedTimestamps")
    statement = verification.get("statement")
    if not isinstance(cert, dict) or not isinstance(ts, list) or not ts:
        reject("Missing verified certificate or timestamp")
    if not isinstance(statement, dict) or statement.get("predicateType") != PREDICATE:
        reject("Unexpected attestation predicate")
    subjects = statement.get("subject")
    if not isinstance(subjects, list) or len(subjects) != 1 or not isinstance(subjects[0], dict):
        reject("Not one signed SARIF subject")
    if subjects[0].get("name") != "qodana.sarif.json" or subjects[0].get("digest") != {"sha256": digest}:
        reject("Original SARIF digest mismatch")
    claims = {
        "sourceRepositoryIdentifier": REPO_ID,
        "sourceRepositoryDigest": sha,
        "githubWorkflowSHA": sha,
        "githubWorkflowRef": WORKFLOW_REF,
        "githubWorkflowTrigger": "push",
        "runnerInvocationURI": f"https://github.com/{REPO}/actions/runs/{run_id}/attempts/{attempt}",
        "runnerEnvironment": "github-hosted",
    }
    for field, expected in claims.items():
        actual = cert.get(field)
        if not isinstance(actual, str) or actual != expected:
            reject("Unsigned or mismatching workflow identity")


def verify(
    report: Path, sha: str, run_id: str, attempt: int, *,
    cli: str | None = None,
    runner: Callable[..., subprocess.CompletedProcess[bytes]] = subprocess.run,
) -> dict[str, Any]:
    if not re.fullmatch(r"[a-f0-9]{40}", sha) or not re.fullmatch(r"[1-9][0-9]{0,18}", run_id):
        reject("Invalid source SHA or run ID")
    if type(attempt) is not int or not 1 <= attempt <= 100:
        reject("Invalid Actions attempt")
    if report.is_symlink() or not report.is_file() or report.name != "qodana.sarif.json":
        reject("Missing original SARIF")
    if report.stat().st_size <= 0 or report.stat().st_size > MAX_BYTES:
        reject("Oversized or empty SARIF")
    _, digest, _, counts = read_report(report, sha, run_id, BRANCH)
    binary = cli if cli is not None else shutil.which("gh")
    if not binary or not Path(binary).is_file():
        reject("Missing trusted GitHub attestation verifier")
    command = [
        str(binary), "attestation", "verify", str(report),
        "--repo", REPO, "--signer-repo", REPO,
        "--signer-workflow", WORKFLOW, "--signer-digest", sha,
        "--source-digest", sha, "--source-ref", f"refs/heads/{BRANCH}",
        "--predicate-type", PREDICATE, "--format", "json",
    ]
    try:
        result = runner(command, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                        check=False, timeout=45)
    except (subprocess.TimeoutExpired, OSError, ValueError) as exc:
        raise ParityError("Unable to verify signed SARIF") from exc
    if result.returncode != 0:
        reject("Signature verification failed")
    verified_statement(result.stdout, digest, sha, run_id, attempt)
    return {
        "schemaVersion": 1,
        "status": "SECURITY_BRANCH_SHADOW_SIGNATURE_VERIFIED",
        "sourceSha": sha,
        "runId": run_id,
        "runAttempt": attempt,
        "originalSarifSha256": digest,
        "resultCount": counts[0],
        "ruleCount": counts[1],
        "artifactCount": counts[2],
        "signerWorkflow": WORKFLOW,
        "signedSubjectVerified": True,
        "producerJobAttested": False,
        "mainProducerAttested": False,
        "cloudReplayVerified": False,
        "qualifiedForGateReuse": False,
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Strict test-branch Qodana signature verification")
    parser.add_argument("--report", required=True, type=Path)
    parser.add_argument("--source-sha", required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--run-attempt", required=True, type=int)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args(argv)
    try:
        if args.output.exists() or args.output.is_symlink():
            reject("Refusing to overwrite earlier shadow receipt")
        evidence = verify(args.report, args.source_sha, args.run_id, args.run_attempt)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.NamedTemporaryFile("w", encoding="utf-8",
                                         dir=args.output.parent, prefix=".qodana-shadow-", delete=False) as f:
            temporary = Path(f.name)
            try:
                json.dump(evidence, f, indent=2, sort_keys=True)
                f.write("\n")
            except BaseException:
                temporary.unlink(missing_ok=True)
                raise
        temporary.replace(args.output)
        print("Signed Qodana SARIF verified on integration shadow; Main/Cloud reuse remains forbidden.")
        return 0
    except (ParityError, OSError, ValueError, TypeError, RecursionError):
        print("Qodana shadow signature REJECTED (fail closed).", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
