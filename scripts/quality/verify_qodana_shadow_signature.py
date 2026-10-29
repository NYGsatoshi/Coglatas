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

class ShadowVerificationError(ParityError):
    """Machine-bounded, non-secret diagnosis of failed signed-shadow validation."""

    def __init__(self, code: str):
        if not re.fullmatch(r"[A-Z][A-Za-z0-9_]{2,60}", code):
            raise ValueError("Invalid verification diagnostic code")
        self.code = code
        super().__init__(code)


def deny(code: str) -> None:
    raise ShadowVerificationError(code)


def reject(message: str) -> None:
    raise ParityError(message)


def verified_statement(blob: bytes, digest: str, sha: str, run_id: str, attempt: int) -> None:
    if not isinstance(blob, bytes) or not 0 < len(blob) <= MAX_GH_BYTES:
        deny("VERIFIED_OUTPUT_SIZE")
    try:
        result = json.loads(blob.decode("utf-8"), object_pairs_hook=unique_object, parse_constant=reject_constant)
    except (ValueError, UnicodeError, RecursionError) as exc:
        raise ShadowVerificationError("VERIFIED_JSON_INVALID") from exc
    if not isinstance(result, list) or len(result) != 1 or not isinstance(result[0], dict):
        deny("VERIFIED_RESULT_COUNT")
    verification = result[0].get("verificationResult")
    if not isinstance(verification, dict):
        deny("VERIFIED_RESULT_MISSING")
    signature = verification.get("signature")
    cert = signature.get("certificate") if isinstance(signature, dict) else None
    ts = verification.get("verifiedTimestamps")
    statement = verification.get("statement")
    if not isinstance(cert, dict) or not isinstance(ts, list) or not ts:
        deny("VERIFIED_CERTIFICATE_OR_TIMESTAMP")
    if not isinstance(statement, dict) or statement.get("predicateType") != PREDICATE:
        deny("VERIFIED_PREDICATE")
    subjects = statement.get("subject")
    if not isinstance(subjects, list) or len(subjects) != 1 or not isinstance(subjects[0], dict):
        deny("VERIFIED_SUBJECT_COUNT")
    if subjects[0].get("name") != "qodana.sarif.json" or subjects[0].get("digest") != {"sha256": digest}:
        deny("VERIFIED_SUBJECT_DIGEST")
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
            deny("CERT_" + field)


def verify(
    report: Path, sha: str, run_id: str, attempt: int, *,
    cli: str | None = None,
    runner: Callable[..., subprocess.CompletedProcess[bytes]] | None = None,
) -> dict[str, Any]:
    if not re.fullmatch(r"[a-f0-9]{40}", sha) or not re.fullmatch(r"[1-9][0-9]{0,18}", run_id):
        deny("SOURCE_ID_INVALID")
    if type(attempt) is not int or not 1 <= attempt <= 100:
        deny("RUN_ATTEMPT_INVALID")
    if report.is_symlink() or not report.is_file() or report.name != "qodana.sarif.json":
        deny("REPORT_PATH_INVALID")
    if report.stat().st_size <= 0 or report.stat().st_size > MAX_BYTES:
        deny("REPORT_SIZE_INVALID")
    try:
        _, digest, _, counts = read_report(report, sha, run_id, BRANCH)
    except (ParityError, OSError, ValueError, RecursionError) as exc:
        raise ShadowVerificationError("REPORT_PROVENANCE_REJECTED") from exc
    binary = cli if cli is not None else shutil.which("gh")
    if not binary or not Path(binary).is_file():
        deny("GH_CLI_UNAVAILABLE")
    command = [
        str(binary), "attestation", "verify", str(report),
        "--repo", REPO, "--signer-repo", REPO,
        "--signer-workflow", WORKFLOW, "--signer-digest", sha,
        "--source-digest", sha, "--source-ref", f"refs/heads/{BRANCH}",
        "--predicate-type", PREDICATE, "--format", "json",
    ]
    try:
        # Resolve the process runner at call time: the production default is
        # subprocess.run, while no-network tests inject a deterministic runner.
        execute = runner if runner is not None else subprocess.run
        result = execute(command, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                         check=False, timeout=45)
    except (subprocess.TimeoutExpired, OSError, ValueError) as exc:
        raise ShadowVerificationError("GH_CLI_LAUNCH_FAILED") from exc
    if result.returncode != 0:
        deny("GH_CLI_VERIFICATION_REJECTED")
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
    except ShadowVerificationError as exc:
        # The code is selected only from literal policy branches and fixed
        # certificate field names. Never print the original report, gh stderr,
        # verification JSON, token, or potentially untrusted exception details.
        print(f"Qodana shadow signature REJECTED (stage={exc.code}).", file=sys.stderr)
        return 1
    except (ParityError, OSError, ValueError, TypeError, RecursionError):
        print("Qodana shadow signature REJECTED (stage=UNCLASSIFIED_INPUT_OR_IO).", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
