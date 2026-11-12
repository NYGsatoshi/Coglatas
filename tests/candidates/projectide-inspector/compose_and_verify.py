#!/usr/bin/env python3
"""Verify synthetic Inspector candidates in a fresh, exact Product composition."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
SOURCES = {
    "tests/candidates/projectide-inspector/InspectorIdentityCandidateTests.cs":
        "tests/Coglatas.Tests/ProjectIde/InspectorIdentityCandidateTests.cs",
    "tests/candidates/projectide-inspector/projectide-inspector-identity.candidate.spec.ts":
        "frontend/src/app/features/projectide/projectide-inspector-identity.candidate.spec.ts",
}


def run(command: list[str], cwd: Path, log: Path | None = None) -> str:
    result = subprocess.run(command, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                            text=True, encoding="utf-8", errors="replace", check=False)
    if log is not None:
        log.write_text(result.stdout, encoding="utf-8")
    if result.returncode:
        raise RuntimeError(f"Command failed ({result.returncode}): {command[:3]}; inspect retained log")
    return result.stdout.strip()


def git(cwd: Path, *arguments: str) -> str:
    return run(["git", "-c", "core.autocrlf=false", *arguments], cwd)


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--product-head", required=True)
    parser.add_argument("--worktree", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    args = parser.parse_args()
    if not re.fullmatch(r"[0-9a-f]{40}", args.product_head):
        raise ValueError("A full immutable Product SHA is mandatory")
    target, evidence = args.worktree.resolve(), args.evidence.resolve()
    if target.exists() or evidence.exists() or target == evidence or target in evidence.parents or evidence in target.parents:
        raise ValueError("Distinct, fresh, non-nested composition and evidence paths are mandatory")
    if git(ROOT, "status", "--porcelain", "--untracked-files=normal"):
        raise ValueError("Commit the independent sources before qualification")
    test_head = git(ROOT, "rev-parse", "HEAD")
    if test_head == args.product_head:
        raise ValueError("Test and Product source owners must be separate commits")
    git(ROOT, "cat-file", "-e", args.product_head + "^{commit}")
    evidence.mkdir(parents=True)
    receipt: dict[str, object] = {
        "productHead": args.product_head, "testHead": test_head,
        "executionScope": "SYNTHETIC_HEADLESS_ONLY", "requiredCheckQualified": False,
        "independentReviewComplete": False, "nativeUiQualified": False,
        "productionQualified": False, "status": "NOT_EXECUTED",
    }
    try:
        git(ROOT, "worktree", "add", "--detach", str(target), args.product_head)
        sources = {}
        for original, destination in SOURCES.items():
            output = target / destination
            if output.exists():
                raise ValueError(f"Unexpected existing candidate destination: {destination}")
            raw = subprocess.run(["git", "show", f"{test_head}:{original}"], cwd=ROOT,
                                 stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True).stdout
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_bytes(raw)
            git(target, "add", "--", destination)
            source_blob = git(ROOT, "rev-parse", f"{test_head}:{original}")
            if git(target, "rev-parse", f":{destination}") != source_blob:
                raise ValueError("Composition changed committed test source bytes")
            sources[destination] = {"source": original, "gitBlob": source_blob, "sha256": digest(output)}
        receipt.update(productTree=git(target, "rev-parse", "HEAD^{tree}"),
                       composedTree=git(target, "write-tree"), sources=sources)
        (evidence / "source-receipt.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")

        project = "tests/Coglatas.Tests/Coglatas.Tests.csproj"
        run(["dotnet", "restore", project, "--nologo"], target, evidence / "restore.log")
        run(["dotnet", "test", project, "-c", "Release", "--no-restore", "-warnaserror",
             "--filter", "FullyQualifiedName~InspectorIdentityCandidateTests",
             "--logger", "trx;LogFileName=inspector.trx", "--results-directory", str(evidence)],
            target, evidence / "xunit.log")
        trx = ET.parse(evidence / "inspector.trx").getroot()
        counters = trx.find("{*}ResultSummary/{*}Counters")
        results = trx.findall("{*}Results/{*}UnitTestResult")
        names = [case.get("testName") for case in results]
        if (counters is None or counters.get("total") != "12" or counters.get("passed") != "12" or
                len(names) != 12 or len(set(names)) != 12 or any(case.get("outcome") != "Passed" for case in results)):
            raise ValueError("Expected all twelve distinct xUnit candidate cases to execute and pass")
        discovery = run(["dotnet", "test", project, "-c", "Release", "--no-build", "--list-tests",
                         "--filter", "FullyQualifiedName~InspectorIdentityCandidateTests"],
                        target, evidence / "compiled-discovery.log")
        if any(not name or name not in discovery for name in names):
            raise ValueError("A TRX case is absent from actual compiled discovery")
        receipt["xunit"] = {"total": 12, "passed": 12, "failed": 0, "skipped": 0, "identities": sorted(names)}

        npm = "npm.cmd" if os.name == "nt" else "npm"
        run([npm, "ci", "--ignore-scripts", "--no-audit", "--no-fund"], target / "frontend", evidence / "npm.log")
        report = evidence / "vitest.json"
        run([npm, "test", "--", "--include=src/app/features/projectide/projectide-inspector-identity.candidate.spec.ts",
             "--reporters=json", "--output-file=" + str(report)], target / "frontend", evidence / "vitest.log")
        parsed = json.loads(report.read_text(encoding="utf-8"))
        assertions = [case for suite in parsed.get("testResults", []) for case in suite.get("assertionResults", [])]
        names = [case.get("fullName") for case in assertions]
        if (parsed.get("success") is not True or len(names) != 11 or len(set(names)) != 11 or
                any(case.get("status") != "passed" for case in assertions)):
            raise ValueError("Expected all eleven distinct Angular candidate cases to execute and pass")
        receipt["vitest"] = {"total": 11, "passed": 11, "failed": 0, "skipped": 0, "identities": sorted(names)}
        receipt["status"] = "SYNTHETIC_HEADLESS_PASSED"
    except Exception as error:
        receipt["status"] = "FAILED_OR_INCOMPLETE"
        receipt["error"] = str(error)
        raise
    finally:
        receipt["retainedSha256"] = {
            path.name: digest(path) for path in sorted(evidence.iterdir())
            if path.is_file() and path.name != "receipt.json"
        }
        (evidence / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(receipt, indent=2))


if __name__ == "__main__":
    main()
