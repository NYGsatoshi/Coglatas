"""Verify a Test-only port against its exact dedicated Product base."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import subprocess

from projectide_core_isolated import verify
from projectide_slice2_isolated import git, sha


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", required=True)
    parser.add_argument("--head", required=True)
    parser.add_argument("--receipt", required=True, type=Path)
    args = parser.parse_args()
    base, head = sha(args.base), sha(args.head)
    root = Path(__file__).resolve().parents[2]
    receipt = args.receipt.resolve()
    if receipt.parent.exists():
        raise ValueError("A fresh evidence directory is required")
    if git(root, "rev-parse", "HEAD") != head or git(root, "status", "--porcelain"):
        raise ValueError("Expected a clean checkout at the exact Test head")
    git(root, "merge-base", "--is-ancestor", base, head)
    allowed = (
        "tests/Coglatas.Tests/ProjectIde/",
        "tests/ci/projectide_",
    )
    allowed_files = {
        ".github/workflows/projectide-core-isolated.yml",
        "frontend/scripts/av-mig-source.node-test.mjs",
        "tests/Coglatas.Tests/Coglatas.Tests.csproj",
        "tests/ci/test_projectide_integration_isolated.py",
        "tests/ui/projectide-workbench.spec.ts",
        "tests/ui/run-angular-playwright.mjs",
    }
    angular_test_dir = Path("frontend/src/app/features/projectide")
    paths = subprocess.check_output(
        ["git", "diff", "--no-renames", "--name-only", "-z", base, head],
        cwd=root,
    ).decode("utf-8").split("\0")[:-1]
    if not paths or any(
        path not in allowed_files
        and not path.startswith(allowed)
        and not (Path(path).parent == angular_test_dir and path.endswith(".spec.ts"))
        for path in paths
    ):
        raise ValueError("Effective diff must contain only integration Test and verifier files")
    value = {
        "schemaVersion": 1,
        "productHead": base,
        "productTree": git(root, "rev-parse", base + "^{tree}"),
        "testHead": head,
        "testTree": git(root, "rev-parse", head + "^{tree}"),
        "combinedCommit": head,
        "combinedTree": git(root, "rev-parse", head + "^{tree}"),
        "testPaths": sorted(paths),
        "verification": {"build": "NotRun", "projectIde": "NotRun", "architecture": "NotRun"},
    }
    receipt.parent.mkdir(parents=True)
    verify(root, receipt, value, Path(__file__).with_name("projectide_integration_inventory.json"))
    print(json.dumps(value["verification"], indent=2))


if __name__ == "__main__":
    main()
