"""Compose exact separate Pure Core candidates; retain source identities and actual results."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys

TEST_BASE = "3865b09a2d3ea3cb82f450c6d19ec643ffbae01f"
PRODUCT_BASE = "3aebedb3d975b04070262751d4dc0a187e0df89b"
REUSED_BASE = "b3de1eb90c85a17a02367c7f25522b5266547286"
REUSED_HEAD = "0d4d4bea6cd8bebed9165e43148c8eec98953fe1"
TEST_PATHS = ("tests/Coglatas.ProjectIde.Entry.Tests/", "tests/ci/projectide_entry_isolated.py",
              ".github/workflows/projectide-entry-isolated.yml")
REUSED_PATHS = ("tests/Coglatas.Tests/ProjectIde/VersionedArtifactTests.cs",
                "tests/Coglatas.Tests/ProjectIde/Fixtures/artifact-reader-compat-vectors-v1.json",
                "tests/ci/projectide-artifact-vectors.test.mjs")


def run(args: list[str], cwd: Path, log: Path | None = None) -> str:
    result = subprocess.run(args, cwd=cwd, text=True, encoding="utf-8", errors="replace",
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    if log is not None:
        log.write_text(result.stdout, encoding="utf-8")
    if result.returncode:
        raise RuntimeError(f"Command failed ({result.returncode}): {args[0:3]}; see retained log")
    return result.stdout.strip()


def git(cwd: Path, *args: str) -> str:
    return run(["git", "-c", "core.autocrlf=false", *args], cwd)


def owned_commits(repo: Path, base: str, head: str, paths: tuple[str, ...]) -> list[str]:
    if not re.fullmatch(r"[a-f0-9]{40}", head):
        raise ValueError("Exact SHA required")
    git(repo, "merge-base", "--is-ancestor", base, head)
    commits = git(repo, "rev-list", "--reverse", base + ".." + head).splitlines()
    for commit in commits:
        if len(git(repo, "rev-list", "--parents", "-n", "1", commit).split()) != 2:
            raise ValueError("Only separate non-merge candidate commits may be composed")
        names = git(repo, "diff-tree", "--no-commit-id", "--name-only", "-r", commit).splitlines()
        if not names or any(not any(name == prefix or prefix.endswith("/") and name.startswith(prefix)
                                   for prefix in paths) for name in names):
            raise ValueError("Candidate changes files outside its allowed ownership")
    return commits


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--product-head", required=True)
    parser.add_argument("--test-head", required=True)
    parser.add_argument("--worktree", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--offline-packages", type=Path)
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[2]
    target, output = args.worktree.resolve(), args.evidence.resolve()
    if target.exists() or output.exists():
        raise ValueError("Fresh verification and evidence paths required")
    owned_commits(repo, PRODUCT_BASE, args.product_head,
                  ("src/Coglatas.Domain/ProjectIde/", "tools/Coglatas.SecurityArchitecture/", "docs/verification/"))
    tests = owned_commits(repo, TEST_BASE, args.test_head, TEST_PATHS)
    reused = owned_commits(repo, REUSED_BASE, REUSED_HEAD, REUSED_PATHS)
    output.mkdir(parents=True)
    receipt = dict(productHead=args.product_head, productBase=PRODUCT_BASE,
                   testHead=args.test_head, testBase=TEST_BASE, reusedTestHead=REUSED_HEAD,
                   verification={}, executionScope="PURE_CORE_ONLY", adoptionCredit=False)
    try:
        git(repo, "worktree", "add", "--detach", str(target), args.product_head)
        for commit in tests + reused:
            git(target, "-c", "commit.gpgsign=false", "-c", "user.name=Pure Core isolated verification",
                "-c", "user.email=isolated-verification@example.invalid", "cherry-pick", commit)
        if git(target, "diff", args.product_head, "HEAD", "--", "src", "tools"):
            raise ValueError("Test composition changed product source")
        receipt.update(combinedCommit=git(target, "rev-parse", "HEAD"), combinedTree=git(target, "rev-parse", "HEAD^{tree}"),
                       productTree=git(repo, "rev-parse", args.product_head + "^{tree}"),
                       testTree=git(repo, "rev-parse", args.test_head + "^{tree}"))
        sys.path.insert(0, str(target / "tests/ci"))
        from projectide_core_isolated import inventory
        receipt["sdk"] = run(["dotnet", "--version"], target)
        if receipt["sdk"] != json.loads((target / "global.json").read_text())["sdk"]["version"]:
            raise ValueError("Repository SDK pin differs")
        restore = ["--source", str(args.offline_packages.resolve()), "-p:NuGetAudit=false"] if args.offline_packages else []
        for name, project in (("solution", "Coglatas.slnx"),
                              ("entry", "tests/Coglatas.ProjectIde.Entry.Tests/Coglatas.ProjectIde.Entry.Tests.csproj")):
            run(["dotnet", "restore", project, "--nologo", *restore], target, output / f"{name}-restore.log")
            receipt["verification"][name + "Build"] = "Failed"
            run(["dotnet", "build", project, "-c", "Release", "--no-restore", "--nologo", "-warnaserror"],
                target, output / f"{name}-build.log")
            receipt["verification"][name + "Build"] = "Passed"
        suites = (("projectIde", "tests/Coglatas.Tests/Coglatas.Tests.csproj", "FullyQualifiedName~ProjectIde"),
                  ("entry", "tests/Coglatas.ProjectIde.Entry.Tests/Coglatas.ProjectIde.Entry.Tests.csproj", None),
                  ("architecture", "tests/Coglatas.Architecture.Tests/Coglatas.Architecture.Tests.csproj", None),
                  ("sourceTooling", "tests/Coglatas.Tests/Coglatas.Tests.csproj",
                   "FullyQualifiedName~SecurityArchitectureSpecRegistryTests|FullyQualifiedName~SecurityArchitectureWaveOneTests|FullyQualifiedName~SecurityArchitectureCliTests"))
        baseline = json.loads((target / "tests/ci/projectide_integration_inventory.json").read_text(encoding="utf-8"))
        for name, project, selection in suites:
            receipt["verification"][name] = "Failed"
            command = ["dotnet", "test", project, "-c", "Release", "--no-build", "--no-restore", "--nologo"]
            if selection:
                command += ["--filter", selection]
            discovery = run(command + ["--list-tests"], target, output / f"{name}-discovery.log")
            discovered = sorted(line.strip() for line in discovery.splitlines() if line.startswith("    Coglatas."))
            if not discovered or len(discovered) != len(set(discovered)):
                raise ValueError("Empty or duplicated compiled discovery")
            run(command + ["--logger", f"trx;LogFileName={name}.trx", "--results-directory", str(output)],
                target, output / f"{name}-test.log")
            executed = inventory(output / f"{name}.trx")
            if executed != discovered:
                raise ValueError("Discovery and actual results differ")
            if name in baseline and not set(baseline[name]).issubset(executed):
                raise ValueError("A retained test identity was lost")
            if name == "entry" and len(executed) < 45:
                raise ValueError("Independent entry test inventory shrank")
            if name == "projectIde" and (len(executed) < 1043 or
                    not any("Independent_reader_compat_vectors" in case for case in executed)):
                raise ValueError("Existing reader compatibility cases did not run")
            (output / f"{name}-inventory.json").write_text(json.dumps(executed, indent=2) + "\n", encoding="utf-8")
            receipt["verification"][name] = dict(total=len(executed), failed=0, skipped=0)
        run(["node", "--test", "tests/ci/projectide-artifact-vectors.test.mjs"], target, output / "node-vectors.log")
        receipt["verification"]["nodeVectors"] = "Passed"
        if git(target, "status", "--porcelain", "--untracked-files=no"):
            raise ValueError("Verification changed tracked files")
    finally:
        receipt["artifactSha256"] = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(output.iterdir()) if p.is_file()}
        (output / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(receipt, indent=2))


if __name__ == "__main__":
    main()
