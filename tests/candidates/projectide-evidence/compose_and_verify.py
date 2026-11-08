#!/usr/bin/env python3
"""Run independent ProjectIDE evidence tests only against an exact Product candidate."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
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


def original_git_blob(repo: Path, revision: str, path: str) -> bytes:
    """Read committed raw bytes instead of OS-dependent checkout line endings."""
    result = subprocess.run(
        ["git", "-c", "core.autocrlf=false", "show", f"{revision}:{path}"],
        cwd=repo, stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False,
    )
    if result.returncode or not result.stdout:
        raise ValueError(f"Exact independent test Git blob unavailable: {path}")
    return result.stdout


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
            canonical_bytes = original_git_blob(ROOT, test_head, original)
            source_blob = git(ROOT, "rev-parse", f"{test_head}:{original}")
            output.write_bytes(canonical_bytes)
            if sha256(output) != hashlib.sha256(canonical_bytes).hexdigest():
                raise ValueError("Committed source bytes changed in test composition")
            hashes[destination] = {
                "source": original, "gitBlob": source_blob, "sha256": sha256(output),
            }
        git(target, "add", "--", *SOURCES.values())
        for destination, identity in hashes.items():
            # Enforce index blob equality as well as the raw byte SHA-256.
            # Working-copy CRLF conversion must not masquerade as source parity.
            staged_blob = git(target, "rev-parse", f":{destination}")
            if staged_blob != identity["gitBlob"]:
                raise ValueError(f"Staged test Git blob diverges from original: {destination}")
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
        results = ET.parse(result_file).getroot().findall("{*}Results/{*}UnitTestResult")
        names = [item.get("testName") for item in results]
        if len(names) != 17 or len(set(names)) != 17:
            raise ValueError("Compiled xUnit result identities are incomplete or duplicated")
        discovery = execute(["dotnet", "test", project, "-c", "Release", "--no-build",
                             "--list-tests", "--filter", "FullyQualifiedName~LocalEvidenceDisplayCandidateTests"],
                            target, evidence / "compiled-discovery.log")
        if any(name not in discovery for name in names):
            raise ValueError("TRX identity absent from compiled discovery")
        record["compiledXunitIdentities"] = sorted(names)
        if counts["total"] != 17 or counts["passed"] != 17 or any(
                counts[key] != 0 for key in ("failed", "notExecuted", "skipped")):
            raise ValueError(f"Independent xUnit identities or outcomes incorrect: {counts}")
        record["xunit"] = counts

        frontend = target / "frontend"
        # Python subprocess on Windows cannot resolve npm's .cmd shim by the
        # extensionless Unix name. Do not use shell=True or bypass tool policy.
        npm = "npm.cmd" if os.name == "nt" else "npm"
        record["npmExecutable"] = npm
        execute([npm, "ci", "--ignore-scripts", "--no-audit", "--no-fund"],
                frontend, evidence / "npm-install.log")
        report_path = evidence / "vitest-results.json"
        try:
            execute([npm, "exec", "--", "vitest", "run",
                     "src/app/features/projectide/projectide-evidence-display.candidate.spec.ts",
                     "--config", "vitest.config.ts", "--reporter=json",
                     "--outputFile=" + str(report_path)], frontend, evidence / "vitest.log")
        except RuntimeError:
            if report_path.is_file():
                failed_report = json.loads(report_path.read_text(encoding="utf-8"))
                for suite in failed_report.get("testResults", []):
                    print("Vitest suite status:", suite.get("name"), suite.get("status"), file=sys.stderr)
                    for assertion in suite.get("assertionResults", []):
                        if assertion.get("status") != "passed":
                            print("Vitest assertion:", assertion.get("fullName"), file=sys.stderr)
                            print(str(assertion.get("failureMessages", []))[-3500:], file=sys.stderr)
                    print(str(suite.get("message", ""))[-3500:], file=sys.stderr)
            raise
        if not report_path.is_file():
            raise ValueError("Actual independent Vitest JSON report missing")
        test_report = json.loads(report_path.read_text(encoding="utf-8"))
        suites = test_report.get("testResults", [])
        assertions = [case for suite in suites for case in suite.get("assertionResults", [])]
        names = [case.get("fullName") for case in assertions]
        if (test_report.get("success") is not True or len(assertions) != 10 or len(set(names)) != 10 or
                any(case.get("status") != "passed" for case in assertions)):
            raise ValueError("Independent Angular evidence tests were not all discovered and passed")
        record["vitest"] = {"total": len(assertions), "passed": len(assertions)}
        record["vitestIdentities"] = sorted(names)
        record["status"] = "PURE_SOURCE_COMPOSITION_PASSED"
    except Exception as exc:
        record["status"] = "FAILED_OR_INCOMPLETE"
        record["error"] = str(exc)
        # A failing candidate must expose its bounded actual diagnostic in CI,
        # not only preserve it in an expiring artifact.
        for name in ("vitest.log", "dotnet-test.log", "npm-install.log"):
            diagnostic = evidence / name
            if diagnostic.is_file():
                print(f"Recorded {name} tail (synthetic candidate only):", file=sys.stderr)
                print(diagnostic.read_text(encoding="utf-8", errors="replace")[-5000:], file=sys.stderr)
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
