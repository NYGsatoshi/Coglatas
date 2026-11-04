"""Compose fixed Product/Test commits in an isolated worktree and retain exact evidence."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET


def run(arguments: list[str], cwd: Path, *, capture: bool = True) -> str:
    environment = os.environ.copy()
    # Reproducible composition commit identities across local and hosted verification.
    environment.update(GIT_COMMITTER_DATE="2026-11-01T00:00:00+00:00",
                       GIT_COMMITTER_NAME="Coglatas isolated verification",
                       GIT_COMMITTER_EMAIL="isolated-verification@example.invalid")
    result = subprocess.run(arguments, cwd=cwd, env=environment, check=True,
                            text=True, encoding="utf-8", stdout=subprocess.PIPE if capture else None)
    return result.stdout.strip() if capture else ""


def git(cwd: Path, *arguments: str) -> str:
    return run(["git", *arguments], cwd)


def sha(value: str) -> str:
    if not re.fullmatch(r"[0-9a-f]{40}", value):
        raise ValueError("An exact lowercase commit SHA is required")
    return value


def changes(repository: Path, base: str, head: str, allowed: tuple[str, ...]) -> list[str]:
    git(repository, "merge-base", "--is-ancestor", base, head)
    commits = git(repository, "rev-list", "--reverse", "--topo-order", f"{base}..{head}").splitlines()
    for commit in commits:
        if len(git(repository, "rev-list", "--parents", "-n", "1", commit).split()) != 2:
            raise ValueError("Only independent single-parent change commits may be applied")
        files = git(repository, "diff-tree", "--no-commit-id", "--name-only", "-r", commit).splitlines()
        if not files or any(not name.startswith(allowed) for name in files):
            raise ValueError("A change commit crosses its Product/Test ownership boundary")
    return commits


def compose(repository: Path, target: Path, receipt_path: Path, candidates: Path, test_head: str) -> dict:
    pins = json.loads(candidates.read_text(encoding="utf-8"))
    if pins.get("schemaVersion") != 1:
        raise ValueError("Unsupported candidate manifest schema")
    for key in ("productBase", "productC", "productD", "productE", "testBase"):
        sha(pins[key])
    sha(test_head)
    if target.exists():
        raise ValueError("The isolated target must be a new directory; existing work is never removed")
    product_paths = ("src/Coglatas.Domain/ProjectIde/", "docs/verification/")
    product_changes = changes(repository, pins["productBase"], pins["productC"], product_paths)
    product_changes += changes(repository, pins["productBase"], pins["productD"], product_paths)
    product_changes += changes(repository, pins["productBase"], pins["productE"], product_paths)
    test_changes = changes(repository, pins["testBase"], test_head,
                           ("tests/", ".github/workflows/projectide-slice2-isolated.yml"))
    git(repository, "worktree", "add", "--detach", str(target), pins["productBase"])
    for commit in product_changes:
        git(target, "cherry-pick", commit)
    product_head = git(target, "rev-parse", "HEAD")
    product_tree = git(target, "rev-parse", "HEAD^{tree}")
    for commit in test_changes:
        git(target, "cherry-pick", commit)
    receipt = {**pins, "testHead": test_head, "productChanges": product_changes, "testChanges": test_changes,
               "productHead": product_head, "productTree": product_tree,
               "combinedCommit": git(target, "rev-parse", "HEAD"),
               "combinedTree": git(target, "rev-parse", "HEAD^{tree}"),
               "verification": {"build": "NotRun", "projectIde": "NotRun", "architecture": "NotRun"}}
    receipt_path.parent.mkdir(parents=True, exist_ok=True)
    receipt_path.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    return receipt


def counters(path: Path) -> dict[str, int]:
    root = ET.parse(path).getroot()
    element = root.find("{*}ResultSummary/{*}Counters")
    if element is None:
        raise ValueError("Missing TRX counters")
    result = {key: int(element.get(key, "0")) for key in ("total", "executed", "passed", "failed", "notExecuted")}
    if result["total"] == 0 or result["total"] != result["passed"] or result["failed"] or result["notExecuted"] or result["executed"] != result["total"]:
        raise ValueError(f"Empty, failed, skipped or incomplete verification: {result}")
    return result


def verify(target: Path, receipt_path: Path, receipt: dict) -> None:
    artifacts = receipt_path.parent
    try:
        receipt["verification"]["build"] = "Failed"
        run(["dotnet", "build", "Coglatas.slnx", "-c", "Release", "--nologo", "-warnaserror"], target, capture=False)
        receipt["verification"]["build"] = "Passed"
        for name, project, filter_value in (
            ("projectIde", "tests/Coglatas.Tests/Coglatas.Tests.csproj", "FullyQualifiedName~ProjectIde"),
            ("architecture", "tests/Coglatas.Architecture.Tests/Coglatas.Architecture.Tests.csproj", None),
        ):
            command = ["dotnet", "test", project, "-c", "Release", "--no-build", "--nologo",
                       "--logger", f"trx;LogFileName={name}.trx", "--results-directory", str(artifacts)]
            if filter_value:
                command += ["--filter", filter_value]
            receipt["verification"][name] = "Failed"
            run(command, target, capture=False)
            receipt["verification"][name] = counters(artifacts / f"{name}.trx")
    finally:
        receipt_path.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--worktree", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--test-head")
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()
    repository = Path(__file__).resolve().parents[2]
    target, receipt_path = args.worktree.resolve(), args.receipt.resolve()
    receipt = compose(repository, target, receipt_path, Path(__file__).with_name("projectide-slice2-candidates.json"),
                      args.test_head or git(repository, "rev-parse", "HEAD"))
    if args.verify:
        verify(target, receipt_path, receipt)
    print(json.dumps(receipt, indent=2))


if __name__ == "__main__":
    main()
