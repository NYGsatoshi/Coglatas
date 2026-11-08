"""Exact, read-only Pure Core composition for the ordinary ReSharper PR gate."""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
from urllib.parse import unquote

ROOT = Path(__file__).resolve().parents[2]
PROJECT = "tests/Coglatas.ProjectIde.Entry.Tests/Coglatas.ProjectIde.Entry.Tests.csproj"
PRODUCT = "94ed17f8d22c0d9bb2ed2bdd149a5c8357b654f3"


def execute(args: list[str], root: Path, log: Path | None = None) -> str:
    result = subprocess.run(args, cwd=root, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                            text=True, encoding="utf-8", errors="replace")
    if log:
        log.write_text(result.stdout, encoding="utf-8")
    if result.returncode:
        raise ValueError(f"Command failed with exit {result.returncode}: {args[:3]}")
    return result.stdout.strip()


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load_gate():
    spec = importlib.util.spec_from_file_location("sarif_gate", ROOT / "scripts/quality/check-resharper-pr-sarif.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def validate_report(path: Path, root: Path, revision: str) -> dict:
    value = json.loads(path.read_text(encoding="utf-8"))
    load_gate().validate_sarif(value)
    if len(value["runs"]) != 1:
        raise ValueError("Expected one complete InspectCode invocation")
    run = value["runs"][0]
    if run["tool"]["driver"].get("fullName") != "JetBrains Inspect Code 2026.2.2":
        raise ValueError("Unexpected InspectCode version")
    if [item.get("revisionId") for item in run.get("versionControlProvenance", [])] != [revision]:
        raise ValueError("SARIF revision does not match the exact composition")
    expected_uri = unquote(root.resolve().as_uri()).rstrip("/") + "/"
    actual_uri = unquote(run.get("originalUriBaseIds", {}).get("solutionDir", {}).get("uri", ""))
    if (actual_uri.casefold() if os.name == "nt" else actual_uri) != (expected_uri.casefold() if os.name == "nt" else expected_uri):
        raise ValueError("SARIF solution root mismatch")
    return run


def prepare(args) -> None:
    sys.path.insert(0, str(ROOT / "tests/ci"))
    from projectide_entry_isolated import (PRODUCT_BASE, TEST_BASE, TEST_PATHS, REUSED_BASE,
                                           REUSED_HEAD, REUSED_PATHS, git, owned_commits)
    selected = args.projects.read_text().splitlines()
    # Once the independent Entry project belongs to the reviewed integration solution,
    # inspect that exact checkout rather than cherry-picking an older test branch.
    # Preserve the historical isolated-composition path below for the original PR.
    import xml.etree.ElementTree as ET
    installed = {
        element.get("Path")
        for element in ET.parse(ROOT / "Coglatas.slnx").iter("Project")
    }
    if PROJECT in installed:
        gate = load_gate()
        changed = gate.read_changed_files(args.changed)
        owned = (
            "tests/Coglatas.ProjectIde.Entry.Tests/",
            "scripts/quality/projectide_entry_resharper.py",
            "scripts/quality/check-resharper-pr-sarif.py",
            ".github/workflows/resharper_pr.yml",
            "Coglatas.slnx",
        )
        entry_touched = any(path.startswith(owned[0]) or path in owned[1:] for path in changed)
        if entry_touched:
            source_paths = sorted(path for path in changed
                                  if path.startswith(owned[0]) and path.endswith(".cs"))
            if not source_paths:
                source_paths = [
                    "tests/Coglatas.ProjectIde.Entry.Tests/EntryContractTests.cs",
                    "tests/Coglatas.ProjectIde.Entry.Tests/SourceAndRunEntryTests.cs",
                ]
            if any(not (ROOT / path).is_file() for path in source_paths):
                raise ValueError("Integrated Entry source absent from checked-out project")
            candidate = git(ROOT, "rev-parse", "HEAD")
            receipt = dict(compositionCommit=candidate, integratedEntry=True,
                           projects=selected,
                           changedCompiledSources={path: digest(ROOT / path) for path in source_paths})
            (args.report / "composition.json").write_text(json.dumps(receipt, indent=2) + "\n",
                                                            encoding="utf-8")
        with Path(os.environ["GITHUB_ENV"]).open("a", encoding="utf-8") as output:
            output.write(f"RESHARPER_ROOT={ROOT}\n")
        return
    if "Coglatas.ProjectIde.Entry.Tests" not in selected:
        # All unrelated PRs retain the existing shared-build inspection path.
        with Path(os.environ["GITHUB_ENV"]).open("a") as output:
            output.write(f"RESHARPER_ROOT={ROOT}\n")
        return
    if os.environ.get("GITHUB_HEAD_REF") != "test/projectide-pure-core-entry-20261107" or os.environ.get("GITHUB_BASE_REF") != "SecurityTestImplimation":
        raise ValueError("Independent entry composition is restricted to its existing Draft PR")
    head = os.environ["HEAD_SHA"]
    owned_commits(ROOT, PRODUCT_BASE, PRODUCT,
                  ("src/Coglatas.Domain/ProjectIde/", "tools/Coglatas.SecurityArchitecture/", "docs/verification/"))
    tests = owned_commits(ROOT, TEST_BASE, head, TEST_PATHS)
    reused = owned_commits(ROOT, REUSED_BASE, REUSED_HEAD, REUSED_PATHS)
    target = args.worktree.resolve()
    if target.exists():
        raise ValueError("Fresh analysis worktree required")
    git(ROOT, "worktree", "add", "--detach", str(target), PRODUCT)
    for commit in tests + reused:
        git(target, "-c", "commit.gpgsign=false", "-c", "user.name=Pure Core quality verification",
            "-c", "user.email=quality@example.invalid", "cherry-pick", commit)
    if git(target, "diff", PRODUCT, "HEAD", "--", "src", "tools"):
        raise ValueError("Independent tests changed Product source")
    # Settings and all ordinary solution projects must remain in scope.
    for path in (".editorconfig", "global.json", "Coglatas.slnx.DotSettings"):
        if git(ROOT, "ls-tree", "HEAD", "--", path) != git(target, "ls-tree", "HEAD", "--", path):
            raise ValueError("Composition changed shared inspection settings")
    import xml.etree.ElementTree as ET
    baseline = ET.parse(ROOT / "Coglatas.slnx")
    solution = ET.parse(target / "Coglatas.slnx")
    if not {p.get("Path") for p in baseline.iter("Project")} <= {p.get("Path") for p in solution.iter("Project")}:
        raise ValueError("Composition lost ordinary solution projects")
    receipt = dict(productHead=PRODUCT, testHead=head, reusedTestHead=REUSED_HEAD,
                   analyzedRevision=git(ROOT, "rev-parse", "HEAD"),
                   compositionCommit=git(target, "rev-parse", "HEAD"),
                   compositionTree=git(target, "rev-parse", "HEAD^{tree}"), projects=selected)
    (args.report / "composition.json").write_text(json.dumps(receipt, indent=2) + "\n")
    execute(["dotnet", "sln", "Coglatas.slnx", "add", PROJECT], target)
    receipt["analysisSolutionSha256"] = digest(target / "Coglatas.slnx")
    execute(["dotnet", "restore", "Coglatas.slnx", "--disable-parallel"], target, args.report / "composition-restore.log")
    execute(["dotnet", "build", "Coglatas.slnx", "-c", "Release", "--no-restore", "-warnaserror"], target,
            args.report / "composition-build.log")
    compiled = json.loads(execute(["dotnet", "msbuild", PROJECT, "-getItem:Compile", "-p:Configuration=Release"], target))
    sources = {Path(item["FullPath"]).resolve().relative_to(target).as_posix()
               for item in compiled["Items"]["Compile"]}
    gate = load_gate()
    changed = gate.read_changed_files(args.changed)
    expected = {p for p in changed if p.startswith("tests/Coglatas.ProjectIde.Entry.Tests/") and p.endswith(".cs")}
    if not expected or not expected <= sources:
        raise ValueError("Changed independent source missing from compiled project model")
    receipt["changedCompiledSources"] = {p: digest(target / p) for p in sorted(expected)}
    (args.report / "composition.json").write_text(json.dumps(receipt, indent=2) + "\n")
    with Path(os.environ["GITHUB_ENV"]).open("a") as output:
        output.write(f"RESHARPER_ROOT={target}\n")


def verify(args) -> None:
    receipt_path = args.report / "composition.json"
    if not receipt_path.exists():
        if "Coglatas.ProjectIde.Entry.Tests" in args.projects.read_text().splitlines():
            raise ValueError("Required independent composition receipt is missing")
        return
    receipt = json.loads(receipt_path.read_text())
    root = Path(os.environ["RESHARPER_ROOT"])
    normal = validate_report(args.report / "inspectcode.sarif.json", root, receipt["compositionCommit"])
    paths = receipt["changedCompiledSources"]
    originals = {p: (root / p).read_bytes() for p in paths}
    # A real WARNING in every changed source proves InspectCode actually visits
    # the files, including a clean report with zero findings. Never gate probes.
    try:
        for index, path in enumerate(paths):
            with (root / path).open("a", encoding="utf-8") as source:
                source.write(f"\ninternal sealed class InspectionScopeProbe{index} {{ private int scopeProbe = 1; public int Value => scopeProbe; }}\n")
        probe = args.report / "scope-probe.sarif.json"
        command = [os.environ["RESHARPER_JB"], "inspectcode", "Coglatas.slnx", f"--output={probe}",
                   "--severity=WARNING", "--no-swea", "--no-build", "--no-updates",
                   "--properties:Configuration=Release", "--properties:RunAnalyzers=false", "--verbosity=WARN"]
        command += [f"--project={project}" for project in receipt["projects"]]
        execute(command, root, args.report / "scope-probe.log")
        report = validate_report(probe, root, receipt["compositionCommit"])
        gate = load_gate()
        visited = {location[0] for item in report["results"]
                   if item.get("ruleId") == "InconsistentNaming" and "scopeProbe" in item.get("message", {}).get("text", "")
                   and (location := gate.first_location(item, root)) is not None}
        if not set(paths) <= visited:
            raise ValueError("InspectCode did not report the WARNING probe in every changed source")
    finally:
        for path, content in originals.items():
            (root / path).write_bytes(content)
    if any(digest(root / p) != expected for p, expected in paths.items()):
        raise ValueError("Analysis source restoration failed")
    receipt.update(scopeProbe="Passed", normalResultCount=len(normal["results"]),
                   sarifSha256=digest(args.report / "inspectcode.sarif.json"),
                   probeSarifSha256=digest(probe))
    receipt_path.write_text(json.dumps(receipt, indent=2) + "\n")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("prepare", "verify"))
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--projects", type=Path)
    parser.add_argument("--changed", type=Path)
    parser.add_argument("--worktree", type=Path)
    arguments = parser.parse_args()
    (prepare if arguments.mode == "prepare" else verify)(arguments)
