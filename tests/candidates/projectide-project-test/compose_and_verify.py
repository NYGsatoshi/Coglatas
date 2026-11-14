#!/usr/bin/env python3
"""Compose independent Security Project Test assertions over an exact immutable Product."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
SOURCE = "tests/candidates/projectide-project-test/SecurityProjectTestCandidateTests.cs"
DESTINATION = "tests/Coglatas.Tests/ProjectIde/SecurityProjectTestCandidateTests.cs"
TEST_CLASS = "SecurityProjectTestCandidateTests"
EXPECTED = 12


def command(args: list[str], cwd: Path, log: Path | None = None) -> str:
    completed = subprocess.run(args, cwd=cwd, capture_output=True, text=True, encoding="utf-8",
                               errors="replace", check=False)
    if log is not None:
        log.write_text(completed.stdout + completed.stderr, encoding="utf-8")
    if completed.returncode:
        raise RuntimeError("Candidate command failed: " + args[0] + "; inspect retained log")
    return completed.stdout.strip()


def git(cwd: Path, *args: str) -> str:
    return command(["git", "-c", "core.autocrlf=false", *args], cwd)


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--product-head", required=True)
    parser.add_argument("--worktree", required=True, type=Path)
    parser.add_argument("--evidence", required=True, type=Path)
    arguments = parser.parse_args()
    if not re.fullmatch(r"[0-9a-f]{40}", arguments.product_head):
        raise ValueError("Full immutable Product SHA required")
    worktree, evidence = arguments.worktree.resolve(), arguments.evidence.resolve()
    if (worktree.exists() or evidence.exists() or worktree == evidence
            or worktree in evidence.parents or evidence in worktree.parents):
        raise ValueError("Fresh, disjoint Product worktree and evidence directories required")
    if git(ROOT, "status", "--porcelain", "--untracked-files=all"):
        raise ValueError("Independent Security source checkout must be clean")
    test_head = git(ROOT, "rev-parse", "HEAD")
    if test_head == arguments.product_head:
        raise ValueError("Security Test HEAD must differ from Product HEAD")
    git(ROOT, "cat-file", "-e", arguments.product_head + "^{commit}")
    evidence.mkdir(parents=True)
    receipt: dict[str, object] = {
        "testHead": test_head,
        "productHead": arguments.product_head,
        "executionScope": "SYNTHETIC_LOCAL_PROJECT_TEST_CORE_ONLY",
        "requiredCheckQualified": False,
        "independentReviewComplete": False,
        "nativeUiQualified": False,
        "productionQualified": False,
        "trustQualified": False,
        "releaseQualified": False,
        "status": "NOT_EXECUTED",
    }
    try:
        command(["git", "-c", "core.autocrlf=false", "worktree", "add", "--detach",
                 str(worktree), arguments.product_head], ROOT, evidence / "worktree.log")
        output = worktree / DESTINATION
        if output.exists():
            raise ValueError("Product already contains candidate destination; refuse overwrite")
        raw = subprocess.run(["git", "show", test_head + ":" + SOURCE], cwd=ROOT,
                             check=True, capture_output=True).stdout
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_bytes(raw)
        source_blob = git(ROOT, "rev-parse", test_head + ":" + SOURCE)
        git(worktree, "add", "--", DESTINATION)
        if git(worktree, "rev-parse", ":" + DESTINATION) != source_blob:
            raise ValueError("Composed candidate differs from original committed Git blob")
        def verify_working_tree() -> None:
            # The index identity is insufficient: inspect actual compiler input bytes.
            if output.read_bytes() != raw:
                raise ValueError("Composed working-tree Test source changed from the committed blob")
            changed = git(worktree, "diff", "--name-only", "HEAD", "--").splitlines()
            if changed != [DESTINATION]:
                raise ValueError("Tracked Product source changed during independent composition")

        verify_working_tree()
        receipt.update(sourcePath=SOURCE, destination=DESTINATION,
                       sourceBlob=source_blob, sourceSha256=sha256(output),
                       productTree=git(worktree, "rev-parse", "HEAD^{tree}"),
                       composedTree=git(worktree, "write-tree"))
        (evidence / "source-receipt.json").write_text(
            json.dumps(receipt, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        sdk = command(["dotnet", "--version"], worktree, evidence / "sdk.log")
        if sdk != "10.0.403":
            raise ValueError("Actual SDK does not match Product global.json pin")
        project = "tests/Coglatas.Tests/Coglatas.Tests.csproj"
        command(["dotnet", "restore", project, "--nologo"], worktree, evidence / "restore.log")
        command(["dotnet", "test", project, "-c", "Release", "--no-restore", "-warnaserror",
                 "--filter", "FullyQualifiedName~" + TEST_CLASS,
                 "--logger", "trx;LogFileName=candidate.trx",
                 "--results-directory", str(evidence)], worktree, evidence / "xunit.log")
        trx = ET.parse(evidence / "candidate.trx").getroot()
        summary = trx.find("{*}ResultSummary")
        counters = trx.find("{*}ResultSummary/{*}Counters")
        cases = trx.findall("{*}Results/{*}UnitTestResult")
        names = [item.get("testName", "") for item in cases]
        if (summary is None or summary.get("outcome") != "Completed" or
                counters is None or any(counters.get(k) != v for k, v in
                                         (("total", str(EXPECTED)), ("passed", str(EXPECTED)),
                                          ("failed", "0"), ("notExecuted", "0"))) or
                len(names) != EXPECTED or len(set(names)) != EXPECTED or
                any(TEST_CLASS not in name for name in names) or
                any(item.get("outcome") != "Passed" for item in cases)):
            raise ValueError("Native TRX must prove exactly twelve distinct passing Project Test candidate facts")
        discovery = command(["dotnet", "test", project, "-c", "Release", "--no-build",
                             "--list-tests", "--filter", "FullyQualifiedName~" + TEST_CLASS],
                            worktree, evidence / "compiled-discovery.log")
        if any(name.rsplit(".", 1)[-1] not in discovery for name in names):
            raise ValueError("A passed TRX identity was absent from compiled test discovery")
        if git(worktree, "rev-parse", ":" + DESTINATION) != source_blob:
            raise ValueError("Composed source identity drifted after real compilation")
        verify_working_tree()
        receipt["nativeXunit"] = {"total": EXPECTED, "passed": EXPECTED,
                                   "failed": 0, "skipped": 0, "identities": sorted(names)}
        receipt["status"] = "SYNTHETIC_PROJECT_TEST_PASSED"
    except Exception as error:
        receipt["status"] = "FAILED_OR_INCOMPLETE"
        receipt["error"] = type(error).__name__ + ": " + str(error)
        raise
    finally:
        receipt["retainedSha256"] = {
            path.name: sha256(path) for path in sorted(evidence.iterdir())
            if path.is_file() and path.name != "receipt.json"
        }
        (evidence / "receipt.json").write_text(
            json.dumps(receipt, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        print(json.dumps(receipt, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
