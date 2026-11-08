#!/usr/bin/env python3
"""Run independent ProjectIDE evidence tests only against an exact Product candidate."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
SOURCES = {
    "tests/candidates/projectide-evidence/LocalEvidenceDisplayCandidateTests.cs":
        "tests/Coglatas.Tests/ProjectIde/LocalEvidenceDisplayCandidateTests.cs",
    "tests/candidates/projectide-evidence/projectide-evidence-display.candidate.spec.ts":
        "frontend/src/app/features/projectide/projectide-evidence-display.candidate.spec.ts",
}


def execute(command: list[str], cwd: Path, log: Path | None = None) -> str:
    result = subprocess.run(command, cwd=cwd, stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, encoding="utf-8", errors="replace",
                            text=True, check=False)
    if log is not None:
        log.write_text(result.stdout, encoding="utf-8")
    if result.returncode:
        raise RuntimeError(f"Command exited {result.returncode}: {command[:3]}; see recorded log")
    return result.stdout.strip()


def git(cwd: Path, *args: str) -> str:
    return execute(["git", "-c", "core.autocrlf=false", *args], cwd)


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--product-head", required=True)
    parser.add_argument("--worktree", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    args = parser.parse_args()
    if not re.fullmatch("[0-9a-f]{40}", args.product_head):
        raise ValueError("Product input must be a full exact SHA")
    target, evidence = args.worktree.resolve(), args.evidence.resolve()
    if target.exists() or evidence.exists() or ROOT == target:
        raise ValueError("Fresh, distinct worktree and evidence paths are mandatory")
    test_head = git(ROOT, "rev-parse", "HEAD")
    if test_head == args.product_head:
        raise ValueError("Independent tests must have a different source HEAD")
    # Exact candidate must already be available in the local checkout of the same repo.
    execute(["git", "cat-file", "-e", args.product_head + "^{commit}"], ROOT)
    evidence.mkdir(parents=True)
    record: dict[str, object] = {
        "productHead": args.product_head,
        "testHead": test_head,
        "executionScope": "READ_ONLY_SYNTHETIC_PURE_CORE",
        "adoptionCredit": False,
        "status": "NOT_EXECUTED",
    }
    try:
        git(ROOT, "worktree", "add", "--detach", str(target), args.product_head)
        hashes: dict[str, dict[str, str]] = {}
        for original, destination in SOURCES.items():
            source = ROOT / original
            output = target / destination
            if not source.is_file() or output.exists():
                raise ValueError(f"Missing independent source or unexpected destination: {original}")
            output.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, output)
            if sha256(source) != sha256(output):
                raise ValueError("Source bytes changed in test composition")
            hashes[destination] = {"source": original, "sha256": sha256(output)}
        git(target, "add", "--", *SOURCES.values())
        record.update(
            composedTree=git(target, "write-tree"),
            originalProductTree=git(target, "rev-parse", "HEAD^{tree}"),
            copiedSourceFiles=hashes,
        )
        (evidence / "source-receipt.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")

        # Only a single synthetic, headless xUnit class runs. No DB or network adapters.
        project = "tests/Coglatas.Tests/Coglatas.Tests.csproj"
        execute(["dotnet", "restore", project, "--nologo"], target, evidence / "dotnet-restore.log")
        execute(["dotnet", "test", project, "-c", "Release", "--no-restore",
                 "-warnaserror", "--filter", "FullyQualifiedName~LocalEvidenceDisplayCandidateTests",
                 "--logger", "trx;LogFileName=evidence-candidate.trx",
                 "--results-directory", str(evidence)], target, evidence / "dotnet-test.log")
        result_file = evidence / "evidence-candidate.trx"
        if not result_file.is_file():
            raise ValueError("Actual xUnit TRX is missing")
        summary = ET.parse(result_file).getroot().find("{*}ResultSummary/{*}Counters")
        if summary is None:
            raise ValueError("TRX outcome counters are missing")
        counts = {key: int(summary.get(key, "0")) for key in
                  ("total", "passed", "failed", "notExecuted", "skipped")}
        if counts["total"] != 3 or counts["passed"] != 3 or any(
                counts[key] != 0 for key in ("failed", "notExecuted", "skipped")):
            raise ValueError(f"Independent xUnit identities or outcomes incorrect: {counts}")
        record["xunit"] = counts

        frontend = target / "frontend"
        execute(["npm", "ci", "--ignore-scripts", "--no-audit", "--no-fund"],
                frontend, evidence / "npm-install.log")
        report_path = evidence / "vitest-results.json"
        execute(["npm", "exec", "--", "vitest", "run",
                 "src/app/features/projectide/projectide-evidence-display.candidate.spec.ts",
                 "--config", "vitest.config.ts", "--reporter=json",
                 "--outputFile=" + str(report_path)], frontend, evidence / "vitest.log")
        if not report_path.is_file():
            raise ValueError("Actual independent Vitest JSON report missing")
        test_report = json.loads(report_path.read_text(encoding="utf-8"))
        suites = test_report.get("testResults", [])
        assertions = [case for suite in suites for case in suite.get("assertionResults", [])]
        if (test_report.get("success") is not True or len(assertions) != 4 or
                any(case.get("status") != "passed" for case in assertions)):
            raise ValueError("Independent Angular evidence tests were not all discovered and passed")
        record["vitest"] = {"total": len(assertions), "passed": len(assertions)}
        record["status"] = "PURE_SOURCE_COMPOSITION_PASSED"
    except Exception as exc:
        record["status"] = "FAILED_OR_INCOMPLETE"
        record["error"] = str(exc)
        raise
    finally:
        record["retainedLogSha256"] = {
            item.name: sha256(item) for item in sorted(evidence.iterdir())
            if item.is_file() and item.name != "receipt.json"
        }
        (evidence / "receipt.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(record, indent=2))


if __name__ == "__main__":
    main()
