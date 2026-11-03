"""Additional evidence for exact independent Core candidates; never replaces required PR CI."""
from __future__ import annotations

import argparse
import hashlib
import json
import platform
from pathlib import Path
import subprocess
import sys
sys.dont_write_bytecode = True
import xml.etree.ElementTree as ET
import unittest

from projectide_slice2_isolated import git, run, sha
from projectide_dependency_continuations import PRODUCT_MERGES, apply_test_changes, product_merge, test_changes as continuation_test_changes


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


def product_changes(repository: Path, base: str, head: str) -> list[str]:
    """Permit only the audited Slice 7 reconciliation; never accept arbitrary merges."""
    allowed = ("src/Coglatas.Domain/ProjectIde/", "src/Coglatas.Application/ProjectIde/", "docs/verification/")
    git(repository, "merge-base", "--is-ancestor", base, head)
    commits = git(repository, "rev-list", "--reverse", f"{base}..{head}").splitlines()
    for commit in commits:
        parents = git(repository, "rev-list", "--parents", "-n", "1", commit).split()[1:]
        if len(parents) == 2:
            if commit in PRODUCT_MERGES:
                product_merge(repository, commit, allowed)
                continue
            if (commit != "610f9911f9ce31739188437972f7eeb018f611da" or parents != [
                    "e7a0b7b9da9a9f761b9c018c62b72e0fe7e901a4", "5ef8b9caa4cbb0228a48b5c13b4f74bb07837547"] or
                    git(repository, "diff", parents[0], commit)):
                raise ValueError("Unapproved Product dependency merge or resolution")
            continue
        paths = git(repository, "diff-tree", "--no-commit-id", "--name-only", "-r", commit).splitlines()
        if len(parents) != 1 or not paths or any(not path.startswith(allowed) for path in paths):
            raise ValueError("Product change crosses its ownership boundary")
    return commits


def compose(repository: Path, target: Path, receipt: Path, product: str, test: str,
            review_product_base: str = "585727a9b480e61d08cbeeb3d996904b5f1f4b5e",
            review_test_base: str = "6edddea75a8dfd0ecaca8559827ffb92ba06a365") -> dict:
    sha(product); sha(test)
    sha(review_product_base); sha(review_test_base)
    git(repository, "merge-base", "--is-ancestor", review_product_base, product)
    git(repository, "merge-base", "--is-ancestor", review_test_base, test)
    if target.exists() or receipt.exists():
        raise ValueError("Fresh worktree and receipt paths required")
    test_base = "b18939f9104d52ae33e32a7f9b15a6e1b52892f1"
    prior_test = "15ae613d8db1397eb105a6753798afb948ea7758"
    product_base = "da112be55ea35794fe4416ba856ba35628a9fe53"
    git(repository, "merge-base", "--is-ancestor", prior_test, test)
    selected_product_changes = product_changes(repository, product_base, product)
    test_changes, test_merges = continuation_test_changes(repository, test_base, test, ("tests/", ".github/workflows/projectide-slice2-isolated.yml",
                                                       ".github/workflows/projectide-core-isolated.yml", ".github/workflows/projectide-value-consumers.yml"))
    git(repository, "worktree", "add", "--detach", str(target), product)
    apply_test_changes(target, test_changes, test_merges)
    value = dict(schemaVersion=1, productHead=product, productBase=product_base, productChanges=selected_product_changes,
                 productTree=git(repository, "rev-parse", product + "^{tree}"), testHead=test, testBase=test_base,
                 testChanges=test_changes, testTree=git(repository, "rev-parse", test + "^{tree}"),
                 testDependencyMerges=test_merges,
                 productContinuationMerges=[product_merge(repository, commit, ("src/Coglatas.Domain/ProjectIde/", "src/Coglatas.Application/ProjectIde/", "docs/verification/"))
                                            for commit in selected_product_changes if commit in PRODUCT_MERGES],
                 reviewProductBase=review_product_base, reviewTestBase=review_test_base, combinedCommit=git(target, "rev-parse", "HEAD"),
                 combinedTree=git(target, "rev-parse", "HEAD^{tree}"), verification={"build": "NotRun"})
    if git(target, "diff", product, "HEAD", "--", "src"):
        raise ValueError("Test composition changed Product code")
    if git(target, "diff", test, "HEAD", "--", "tests/Coglatas.Tests/ProjectIde", "tests/ci/projectide*", "tests/ci/test_projectide*", ".github/workflows/projectide-*.yml"):
        raise ValueError("Composition differs from exact independent Test source")
    receipt.parent.mkdir(parents=True, exist_ok=True)
    receipt.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")
    return value


def assert_candidate(target: Path, value: dict) -> None:
    if git(target, "rev-parse", "HEAD") != value["combinedCommit"] or git(target, "rev-parse", "HEAD^{tree}") != value["combinedTree"]:
        raise ValueError("Exact candidate commit/tree mismatch")
    if git(target, "status", "--porcelain", "--untracked-files=all"):
        raise ValueError("Candidate contains tracked or untracked changes")


def verify(target: Path, receipt: Path, value: dict, baseline: Path) -> None:
    output = receipt.parent
    try:
        assert_candidate(target, value)
        value.update(host=platform.platform(), python=sys.version, cleanBefore=True,
                     productParents=git(target, "show", "-s", "--format=%P", value['productHead']).split(),
                     testParents=git(target, "show", "-s", "--format=%P", value['testHead']).split())
        source_files = git(target, "ls-tree", "-r", "--name-only", value['productHead'], "src/Coglatas.Domain/ProjectIde", "src/Coglatas.Application/ProjectIde").splitlines()
        value['runtimeSource'] = {name: dict(blob=git(target, "rev-parse", value['productHead'] + ':' + name),
                                           sha256=hashlib.sha256((target / name).read_bytes()).hexdigest()) for name in source_files}
        suite = unittest.defaultTestLoader.discover(str(target / 'tests/ci'), pattern='test_projectide*isolated.py')
        def identities(tests):
            return sorted(name for test in tests for name in (identities(test) if isinstance(test, unittest.TestSuite) else [test.id()]))
        control_names = identities(suite)
        with (output / 'controls.log').open('w', encoding='utf-8') as stream:
            controls = unittest.TextTestRunner(stream=stream, verbosity=2).run(suite)
        if not control_names or len(set(control_names)) != len(control_names) or controls.testsRun != len(control_names) or not controls.wasSuccessful() or controls.skipped:
            raise ValueError('Empty, failed or skipped independent controls')
        (output / 'controls-inventory.json').write_text(json.dumps(control_names, indent=2) + '\n', encoding='utf-8')
        value['controls'] = dict(total=controls.testsRun, failed=0, skipped=0)
        value["sdk"] = run(["dotnet", "--version"], target)
        expected_sdk = json.loads((target / "global.json").read_text())["sdk"]["version"]
        if value["sdk"] != expected_sdk:
            raise ValueError("SDK is not the exact repository pin")
        value["verification"]["build"] = "Failed"
        run(["dotnet", "build", "Coglatas.slnx", "-c", "Release", "--nologo", "-warnaserror", "--disable-build-servers", "-m:1"], target, capture=False)
        value["verification"]["build"] = "Passed"
        for name, project, filter_value in (
            ("projectIde", "tests/Coglatas.Tests/Coglatas.Tests.csproj", "FullyQualifiedName~ProjectIde"),
            ("architecture", "tests/Coglatas.Architecture.Tests/Coglatas.Architecture.Tests.csproj", None),
        ):
            path = output / f"{name}.trx"
            value["verification"][name] = "Failed"
            command = ["dotnet", "test", project, "-c", "Release", "--no-build", "--nologo", "--disable-build-servers", "-m:1", "--logger",
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
            if name == "projectIde" and (target / "src/Coglatas.Domain/ProjectIde/DerivedProjectIr.cs").exists() and not any("CompilerIrTests" in item for item in names):
                raise ValueError("Slice 6 tests did not run")
            if name == "projectIde" and (target / "src/Coglatas.Domain/ProjectIde/PureCoreCompiler.cs").exists() and not any("CompilerAnalysisTests" in item for item in names):
                raise ValueError("Slice 7 tests did not run")
            if name == "projectIde" and (target / "src/Coglatas.Application/ProjectIde/Core/CompilerHostAdapters.cs").exists() and not any("CompilerHostTests" in item for item in names):
                raise ValueError("Slice 8 tests did not run")
            if name == "projectIde" and (target / "src/Coglatas.Domain/ProjectIde/RequirementEvidenceValidation.cs").exists():
                for family in ("RequirementEvidenceValidationTests", "ResourceReferenceValidationTests"):
                    if not any(family in item for item in names):
                        raise ValueError("Evidence/Resource tests did not run: " + family)
            if name == "projectIde" and (target / 'tests/Coglatas.Tests/ProjectIde/CompilerMaterializationBindingTests.cs').exists() and not any('CompilerMaterializationBindingTests' in item for item in names):
                raise ValueError('Materialization tests did not run')
            if name == "projectIde" and (target / 'tests/Coglatas.Tests/ProjectIde/CompilerDiagnosticIntegrationTests.cs').exists() and not any('CompilerDiagnosticIntegrationTests' in item for item in names):
                raise ValueError('Diagnostic integration tests did not run')
            if name == "projectIde" and (target / 'tests/Coglatas.Tests/ProjectIde/RelationGraphCancellationTests.cs').exists() and not any('RelationGraphCancellationTests' in item for item in names):
                raise ValueError('Graph cancellation tests did not run')
            (output / f"{name}-inventory.json").write_text(json.dumps(names, indent=2) + "\n", encoding="utf-8")
            value["verification"][name] = dict(total=len(names), failed=0, skipped=0, added=sorted(set(names) - set(previous)))
        value["artifactSha256"] = {path.name: hashlib.sha256(path.read_bytes()).hexdigest()
                                   for path in sorted(output.iterdir()) if path.is_file() and path != receipt}
        assert_candidate(target, value)
        value['cleanAfter'] = True
    finally:
        receipt.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--product-head", required=True)
    parser.add_argument("--test-head", required=True)
    parser.add_argument("--worktree", required=True, type=Path)
    parser.add_argument("--receipt", required=True, type=Path)
    parser.add_argument("--review-product-base", default="585727a9b480e61d08cbeeb3d996904b5f1f4b5e")
    parser.add_argument("--review-test-base", default="6edddea75a8dfd0ecaca8559827ffb92ba06a365")
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()
    repository = Path(__file__).resolve().parents[2]
    value = compose(repository, args.worktree.resolve(), args.receipt.resolve(), args.product_head, args.test_head,
                    args.review_product_base, args.review_test_base)
    if args.verify:
        verify(args.worktree.resolve(), args.receipt.resolve(), value, Path(__file__).with_name("projectide-core-baseline-inventory.json"))
    print(json.dumps(value, indent=2))


if __name__ == "__main__":
    main()
