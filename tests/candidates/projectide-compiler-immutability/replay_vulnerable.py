#!/usr/bin/env python3
"""Independent historical RED replay of the three original Compiler SyncRoot cases."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
SOURCE = "tests/candidates/projectide-compiler-immutability/SecurityCompilerImmutableBoundaryCandidateTests.cs"
DESTINATION = "tests/Coglatas.Tests/ProjectIde/SecurityCompilerImmutableBoundaryCandidateTests.cs"
VULNERABLE = "2b2c86a447779c2d9d19e4b88f1274a70737d54e"
NAMES = [
    "LockedManifestRejectsPublicBackingMutationAndRetainsCompilerIdentity",
    "EvidencePremisesPreserveCanonicalBasisAgainstSyncRootAndCallerArrayMutation",
    "TypedRuleOutcomeCannotChangeRetainedVerdictOrResultDigest",
]
REFUSAL = "A legacy synchronization root must not expose mutable storage."


def command(args: list[str], cwd: Path, log: Path | None = None,
            success: bool = True) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(args, cwd=cwd, capture_output=True, text=True,
                            encoding="utf-8", errors="replace", check=False)
    if log is not None:
        log.write_text(result.stdout + result.stderr, encoding="utf-8")
    if success and result.returncode:
        raise RuntimeError("Historical replay command failed: " + args[0])
    return result


def git(cwd: Path, *args: str) -> str:
    return command(["git", "-c", "core.autocrlf=false", *args], cwd).stdout.strip()


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--vulnerable-head", required=True)
    parser.add_argument("--worktree", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    args = parser.parse_args()
    if not re.fullmatch(r"[0-9a-f]{40}", args.vulnerable_head) or args.vulnerable_head != VULNERABLE:
        raise ValueError("Historical RED Product commit must be the exact recorded vulnerable SHA")
    target, evidence = args.worktree.resolve(), args.evidence.resolve()
    if target.exists() or evidence.exists() or target == evidence or target in evidence.parents or evidence in target.parents:
        raise ValueError("Fresh disjoint replay and evidence directories are required")
    if git(ROOT, "status", "--porcelain", "--untracked-files=all"):
        raise ValueError("Committed independent Security Test source required")
    test_head = git(ROOT, "rev-parse", "HEAD")
    git(ROOT, "cat-file", "-e", VULNERABLE + "^{commit}")
    evidence.mkdir(parents=True)
    record: dict[str, object] = {
        "testHead": test_head, "vulnerableProductHead": VULNERABLE,
        "executionScope": "HISTORICAL_COMPILER_SYNCROOT_RED_ONLY",
        "requiredCheckQualified": False, "independentReviewComplete": False,
        "nativeUiQualified": False, "productionQualified": False,
        "trustQualified": False, "releaseQualified": False,
        "status": "NOT_EXECUTED",
    }
    try:
        command(["git", "-c", "core.autocrlf=false", "worktree", "add",
                 "--detach", str(target), VULNERABLE], ROOT, evidence / "worktree.log")
        dest = target / DESTINATION
        if dest.exists():
            raise ValueError("Historical Product already contains the independent test destination")
        raw = subprocess.run(["git", "show", test_head + ":" + SOURCE], cwd=ROOT,
                             check=True, capture_output=True).stdout
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(raw)
        source_blob = git(ROOT, "rev-parse", test_head + ":" + SOURCE)
        git(target, "add", "--", DESTINATION)
        if git(target, "rev-parse", ":" + DESTINATION) != source_blob:
            raise ValueError("Historical candidate source index is not the committed Test blob")
        record.update(sourceBlob=source_blob, sourceSha256=digest(dest),
                      productTree=git(target, "rev-parse", "HEAD^{tree}"),
                      composedTree=git(target, "write-tree"))
        (evidence / "source-receipt.json").write_text(
            json.dumps(record, sort_keys=True, indent=2) + "\n", encoding="utf-8")
        sdk = command(["dotnet", "--version"], target, evidence / "sdk.log").stdout.strip()
        if sdk != "10.0.403":
            raise ValueError("Historical Product SDK 10.0.403 policy not satisfied")
        project = "tests/Coglatas.Tests/Coglatas.Tests.csproj"
        command(["dotnet", "restore", project, "--nologo"], target, evidence / "restore.log")
        expression = "|".join("FullyQualifiedName~" + name for name in NAMES)
        executed = command(["dotnet", "test", project, "-c", "Release", "--no-restore", "-warnaserror",
                            "--filter", expression, "--logger", "trx;LogFileName=historical-red.trx",
                            "--results-directory", str(evidence)], target, evidence / "xunit.log", success=False)
        if executed.returncode != 1:
            raise ValueError("Expected actual native xUnit assertions to fail, not pass or abort")
        trx = ET.parse(evidence / "historical-red.trx").getroot()
        summary = trx.find("{*}ResultSummary")
        counts = trx.find("{*}ResultSummary/{*}Counters")
        results = trx.findall("{*}Results/{*}UnitTestResult")
        names = [case.get("testName", "") for case in results]
        if (summary is None or summary.get("outcome") != "Failed" or counts is None or
                any(counts.get(key) != value for key, value in
                    (("total", "3"), ("passed", "0"), ("failed", "3"), ("notExecuted", "0"))) or
                len(results) != 3 or len(set(names)) != 3 or
                {name.rsplit(".", 1)[-1] for name in names} != set(NAMES)):
            raise ValueError("Expected exactly the three named historical failing xUnit cases")
        for result in results:
            message = result.findtext("{*}Output/{*}ErrorInfo/{*}Message", default="")
            if result.get("outcome") != "Failed" or REFUSAL not in message:
                raise ValueError("Historical case did not fail at the exact original SyncRoot invariant")
        discovery = command(["dotnet", "test", project, "-c", "Release", "--no-build",
                             "--list-tests", "--filter", expression], target,
                            evidence / "compiled-discovery.log").stdout
        if any(name not in discovery for name in NAMES):
            raise ValueError("Historical failing case missing from compiled discovery")
        if git(target, "rev-parse", ":" + DESTINATION) != source_blob:
            raise ValueError("Historical replay source identity drifted")
        record["historicalXunit"] = {
            "total": 3, "passed": 0, "failed": 3, "notExecuted": 0,
            "expectedFailureInvariant": REFUSAL, "identities": sorted(names),
        }
        record["status"] = "EXPECTED_HISTORICAL_RED_VERIFIED"
    except Exception as error:
        record["status"] = "FAILED_OR_INCOMPLETE"
        record["error"] = type(error).__name__ + ": " + str(error)
        raise
    finally:
        record["retainedSha256"] = {
            path.name: digest(path) for path in sorted(evidence.iterdir())
            if path.is_file() and path.name != "receipt.json"
        }
        (evidence / "receipt.json").write_text(
            json.dumps(record, sort_keys=True, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(record, sort_keys=True, indent=2))


if __name__ == "__main__":
    main()
