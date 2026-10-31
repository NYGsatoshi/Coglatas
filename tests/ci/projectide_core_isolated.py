"""Additional evidence for exact independent Core candidates; never replaces required PR CI."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

from projectide_slice2_isolated import changes, git, run, sha


def inventory(path: Path) -> list[str]:
    root = ET.parse(path).getroot()
    counters = root.find("{*}ResultSummary/{*}Counters")
    if counters is None:
        raise ValueError("Missing TRX summary")
    count = {key: int(counters.get(key, "0")) for key in
             ("total", "executed", "passed", "failed", "notExecuted", "error", "timeout", "aborted", "inconclusive", "notRunnable")}
    if count["total"] <= 0 or count["executed"] != count["total"] or count["passed"] != count["total"] or any(
            value for key, value in count.items() if key not in ("total", "executed", "passed")):
        raise ValueError("Empty, failed, skipped or incomplete TRX")
    results = root.findall("{*}Results/{*}UnitTestResult")
    definitions = root.findall("{*}TestDefinitions/{*}UnitTest")
    entries = root.findall("{*}TestEntries/{*}TestEntry")
    if any(len(items) != count["total"] for items in (results, definitions, entries)):
        raise ValueError("TRX counters, results, definitions and entries disagree")
    result_ids = [(item.get("testId"), item.get("executionId")) for item in results]
    entry_ids = [(item.get("testId"), item.get("executionId")) for item in entries]
    definition_ids = [(item.get("id"), item.find("{*}Execution").get("id")) for item in definitions]
    if len(set(result_ids)) != len(result_ids) or set(result_ids) != set(entry_ids) or set(result_ids) != set(definition_ids):
        raise ValueError("TRX identity mapping is incomplete or duplicated")
    if any(not all(pair) for pair in result_ids) or any(item.get("outcome") != "Passed" for item in results):
        raise ValueError("Non-Passed or unbound result")
    names = sorted(item.get("testName", "") for item in results)
    if not all(names) or len(set(names)) != len(names):
        raise ValueError("Missing or duplicate test names")
    return names


def compose(repository: Path, target: Path, receipt: Path, product: str, test: str) -> dict:
    sha(product); sha(test)
    if target.exists() or receipt.exists():
        raise ValueError("Fresh worktree and receipt paths required")
    test_base = "b18939f9104d52ae33e32a7f9b15a6e1b52892f1"
    prior_test = "15ae613d8db1397eb105a6753798afb948ea7758"
    product_base = "da112be55ea35794fe4416ba856ba35628a9fe53"
    git(repository, "merge-base", "--is-ancestor", prior_test, test)
    product_changes = changes(repository, product_base, product,
                              ("src/Coglatas.Domain/ProjectIde/", "src/Coglatas.Application/ProjectIde/", "docs/verification/"))
    test_changes = changes(repository, test_base, test, ("tests/", ".github/workflows/projectide-slice2-isolated.yml",
                                                       ".github/workflows/projectide-core-isolated.yml"))
    git(repository, "worktree", "add", "--detach", str(target), product)
    for commit in test_changes:
        git(target, "cherry-pick", commit)
    value = dict(schemaVersion=1, productHead=product, productBase=product_base, productChanges=product_changes,
                 productTree=git(repository, "rev-parse", product + "^{tree}"), testHead=test, testBase=test_base,
                 testChanges=test_changes, combinedCommit=git(target, "rev-parse", "HEAD"),
                 combinedTree=git(target, "rev-parse", "HEAD^{tree}"), verification={"build": "NotRun"})
    if git(target, "diff", product, "HEAD", "--", "src"):
        raise ValueError("Test composition changed Product code")
    receipt.parent.mkdir(parents=True, exist_ok=True)
    receipt.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")
    return value


def verify(target: Path, receipt: Path, value: dict, baseline: Path) -> None:
    output = receipt.parent
    try:
        value["sdk"] = run(["dotnet", "--version"], target)
        expected_sdk = json.loads((target / "global.json").read_text())["sdk"]["version"]
        if value["sdk"] != expected_sdk:
            raise ValueError("SDK is not the exact repository pin")
        value["verification"]["build"] = "Failed"
        run(["dotnet", "build", "Coglatas.slnx", "-c", "Release", "--nologo", "-warnaserror"], target, capture=False)
        value["verification"]["build"] = "Passed"
        for name, project, filter_value in (
            ("projectIde", "tests/Coglatas.Tests/Coglatas.Tests.csproj", "FullyQualifiedName~ProjectIde"),
            ("architecture", "tests/Coglatas.Architecture.Tests/Coglatas.Architecture.Tests.csproj", None),
        ):
            path = output / f"{name}.trx"
            value["verification"][name] = "Failed"
            command = ["dotnet", "test", project, "-c", "Release", "--no-build", "--nologo", "--logger",
                       f"trx;LogFileName={name}.trx", "--results-directory", str(output)]
            if filter_value:
                command += ["--filter", filter_value]
            discovery_command = command[:command.index("--logger")] + ["--list-tests"]
            if filter_value:
                discovery_command += ["--filter", filter_value]
            discovered = sorted(line.strip() for line in run(discovery_command, target).splitlines()
                                if line.startswith("    Coglatas."))
            if not discovered or len(set(discovered)) != len(discovered):
                raise ValueError("Test discovery was empty or duplicated")
            (output / f"{name}-discovery.json").write_text(json.dumps(discovered, indent=2) + "\n", encoding="utf-8")
            run(command, target, capture=False)
            names = inventory(path)
            if discovered != names:
                raise ValueError("Discovered tests and executed TRX inventory disagree")
            previous = json.loads(baseline.read_text(encoding="utf-8"))[name]
            if not set(previous).issubset(names):
                raise ValueError("A previously verified test was removed, renamed or not run")
            if name == "projectIde" and not any("TypedRelationTests" in item for item in names):
                raise ValueError("Slice 3 tests did not run")
            if name == "projectIde" and (target / "src/Coglatas.Domain/ProjectIde/CompilerInputManifest.cs").exists() and not any("CompilerEnvelopeTests" in item for item in names):
                raise ValueError("Slice 4 tests did not run")
            if name == "projectIde" and (target / "src/Coglatas.Domain/ProjectIde/CompilerBinding.cs").exists() and not any("CompilerBindingTests" in item for item in names):
                raise ValueError("Slice 5 tests did not run")
            (output / f"{name}-inventory.json").write_text(json.dumps(names, indent=2) + "\n", encoding="utf-8")
            value["verification"][name] = dict(total=len(names), failed=0, skipped=0, added=sorted(set(names) - set(previous)))
        value["artifactSha256"] = {path.name: hashlib.sha256(path.read_bytes()).hexdigest()
                                   for path in sorted(output.iterdir()) if path.is_file() and path != receipt}
        if git(target, "status", "--porcelain", "--untracked-files=no"):
            raise ValueError("Verification changed tracked files")
    finally:
        receipt.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--product-head", required=True)
    parser.add_argument("--test-head", required=True)
    parser.add_argument("--worktree", required=True, type=Path)
    parser.add_argument("--receipt", required=True, type=Path)
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()
    repository = Path(__file__).resolve().parents[2]
    value = compose(repository, args.worktree.resolve(), args.receipt.resolve(), args.product_head, args.test_head)
    if args.verify:
        verify(args.worktree.resolve(), args.receipt.resolve(), value, Path(__file__).with_name("projectide-core-baseline-inventory.json"))
    print(json.dumps(value, indent=2))


if __name__ == "__main__":
    main()
