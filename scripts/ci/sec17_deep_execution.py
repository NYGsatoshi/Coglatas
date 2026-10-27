"""Verify the separate bounded SEC-17 deep execution; this grants no contract approval."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

from sec_arch_evidence import digest, observed_trx
from sec_arch_assembly_binding import capture_assemblies, SIX_ASSEMBLY_SCOPE

METHOD = "Coglatas.Tests.SecurityArchitecture.SecurityArchitectureSignalRDeepChurnTests.ConcurrentIndependentDirectScopeTransitionsReauthorizeEveryBoundedReconnectCycle"
SOURCE = "tests/Coglatas.Tests/SecurityArchitecture/SecurityArchitectureSignalRDeepChurnTests.cs"


def observe(data: bytes, now: datetime) -> dict:
    report = observed_trx(data, now, {METHOD: 1})
    root = ET.fromstring(data)
    counters = root.find("{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}ResultSummary/{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters")
    if counters is None or int(counters.attrib["total"]) != 1:
        raise ValueError("The dedicated deep selection must contain exactly its one owner.")
    return report


def verify(data: bytes, now: datetime) -> dict:
    report = observe(data, now)
    if (report["outcome"] != "PASS" or report["observedCaseCount"] != 1 or
            report["requiredCaseCount"] != 1 or report["cases"][0]["method"] != METHOD):
        raise ValueError("The exact deep owner must execute once and pass without skips.")
    return report


def capture(root: Path, trx: Path, candidate: str, now: datetime) -> dict:
    def git(*args: str) -> str:
        return subprocess.check_output(["git", "-C", str(root), *args], text=True).strip()
    if (not re.fullmatch(r"[a-f0-9]{40}", candidate) or git("rev-parse", "HEAD") != candidate or
            git("status", "--porcelain")):
        raise ValueError("An exact clean candidate checkout is required.")
    stamp = root / "artifacts/ci/dotnet-build-sha"
    if not stamp.is_file() or stamp.read_text().strip() != candidate:
        raise ValueError("The current build stamp must match the candidate.")
    source = (root / SOURCE).read_bytes()
    declaration = rb"public\s+Task\s+" + METHOD.rsplit(".", 1)[1].encode() + rb"\s*\(\s*\)"
    if len(re.findall(declaration, source)) != 1:
        raise ValueError("The exact deep owner source declaration is required.")
    data = trx.read_bytes()
    observed = observe(data, now)
    assemblies = capture_assemblies(root)
    if git("rev-parse", "HEAD") != candidate or git("status", "--porcelain"):
        raise ValueError("The candidate changed during capture.")
    return {
        "schemaVersion": 1, "verifierId": "SEC-17-BOUNDED-DEEP-EXECUTION",
        "outcome": observed["outcome"], "selectionStatus": selection_status(observed),
        "candidateSha": candidate, "sourcePath": SOURCE, "sourceDigest": digest(source),
        "executionDigest": digest(data), "observedExecution": observed,
        "assemblyBindingScope": SIX_ASSEMBLY_SCOPE, "assemblyDigests": assemblies,
        "runId": os.environ.get("GITHUB_RUN_ID", "LOCAL"),
        "runAttempt": os.environ.get("GITHUB_RUN_ATTEMPT", "1"),
        "qualification": "REPRESENTATIVE_SYNTHETIC_POST_COMMIT_DEEP_ONLY",
        "contractCompletion": "UNVERIFIED", "trustedAttestation": "UNVERIFIED", "ownerApproval": None,
        "limits": [
            "The source defines three cycles and two scopes; these are source parameters, not independent per-cycle telemetry.",
            "Fixture transactions change current database authority directly; management endpoint and invalidation side effects are unqualified.",
            "Both independent transactions prepare before release; all asserted message delivery starts after both commits.",
            "No commit/delivery race ordering, exactly-once guarantee, load/capacity or full event/producer matrix is qualified.",
            "Run, source, assembly and artifact bindings require independent hosted provenance reconciliation.",
            "Product RLS, deployment, normative mapping and Owner holds remain unchanged."
        ]
    }


def selection_status(observed: dict) -> str:
    if not observed["cases"]:
        return "NOT_SELECTED"
    return "SKIPPED_OR_NOT_EXECUTED" if observed["cases"][0]["outcome"] == "UNVERIFIED" else "EXECUTED"


def failure_summary(candidate: str, trx: Path) -> dict:
    # No native TestOutput/ErrorInfo, exception text, identifiers or payloads
    # are copied, including when build, selection or evidence capture fails.
    try:
        data = trx.read_bytes() if trx.is_file() and trx.stat().st_size <= 64 * 1024 * 1024 else None
    except OSError:
        data = None
    return {
        "schemaVersion": 1, "verifierId": "SEC-17-BOUNDED-DEEP-EXECUTION",
        "candidateSha": candidate if re.fullmatch(r"[a-f0-9]{40}", candidate) else None,
        "outcome": "ERROR", "selectionStatus": "NO_VALID_NATIVE_EXECUTION",
        "diagnostic": "INVALID_OR_UNAVAILABLE_EXECUTION_EVIDENCE",
        "executionDigest": digest(data) if data is not None else None,
        "requiredCaseCount": 1, "observedCaseCount": None,
        "contractCompletion": "UNVERIFIED", "trustedAttestation": "UNVERIFIED", "ownerApproval": None,
        "runId": os.environ.get("GITHUB_RUN_ID", "LOCAL"),
        "runAttempt": os.environ.get("GITHUB_RUN_ATTEMPT", "1")
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--trx", required=True, type=Path)
    parser.add_argument("--candidate-sha", required=True)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    try:
        report = capture(Path(__file__).resolve().parents[2], args.trx, args.candidate_sha, datetime.now(timezone.utc))
    except (ValueError, KeyError, TypeError, OSError, ET.ParseError, subprocess.SubprocessError):
        report = failure_summary(args.candidate_sha, args.trx)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("x", encoding="utf-8") as output:
        output.write(json.dumps(report, indent=2) + "\n")
    print("SEC-17 bounded deep: " + report["outcome"] + "; contract completion UNVERIFIED.")
    return 0 if report["outcome"] == "PASS" else 1


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (ValueError, KeyError, TypeError, OSError, ET.ParseError, subprocess.SubprocessError):
        print("SEC-17 bounded deep ERROR: invalid or unavailable execution evidence.")
        raise SystemExit(1)
