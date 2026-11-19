#!/usr/bin/env python3
"""Compose exact Security-owned Compiler immutability candidates over an immutable Product."""
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
SOURCE = "tests/candidates/projectide-compiler-immutability/SecurityCompilerImmutableBoundaryCandidateTests.cs"
DESTINATION = "tests/Coglatas.Tests/ProjectIde/SecurityCompilerImmutableBoundaryCandidateTests.cs"
TEST_CLASS = "SecurityCompilerImmutableBoundaryCandidateTests"
TEST_CLASS_FQN = "Coglatas.Tests.ProjectIde." + TEST_CLASS
EXPECTED_METHODS = (
    "LockedManifestRejectsPublicBackingMutationAndRetainsCompilerIdentity",
    "EvidencePremisesPreserveCanonicalBasisAgainstSyncRootAndCallerArrayMutation",
    "TypedRuleOutcomeCannotChangeRetainedVerdictOrResultDigest",
    "ActualDiagnosticAndArtifactViewsCannotExposeWritableStorage",
    "RealCompilationStagesAndDerivedIrNodesAreIndependentOfPublicCollections",
    "CurrentStaleAndAbsentFreshnessViewsRemainImmutable",
    "RealHostProfileCannotLoseCompatibilityThroughPublicCollectionAccess",
    "WindowsPurePreparationRetainsRealCompiledResultAfterMutationProbes",
    "EmptyResultAndMissingEvidenceCannotExposeMutableStorage",
)
EXPECTED_IDENTITIES = frozenset(TEST_CLASS_FQN + "." + method for method in EXPECTED_METHODS)
EXPECTED = 9
TRX_NAMESPACE = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"


def single_element(parent: ET.Element, name: str) -> ET.Element:
    elements = parent.findall("{*}" + name)
    if len(elements) != 1 or elements[0].tag != TRX_NAMESPACE + name:
        raise ValueError("Native TRX requires one " + name + " element")
    return elements[0]


def native_inventory(path: Path) -> list[str]:
    """Admit the original nine facts with complete native method and execution binding."""
    trx = ET.parse(path).getroot()
    if trx.tag != TRX_NAMESPACE + "TestRun":
        raise ValueError("Native TRX root or namespace is invalid")
    summary = single_element(trx, "ResultSummary")
    counters = single_element(summary, "Counters")
    if (summary.get("outcome") != "Completed" or len(EXPECTED_IDENTITIES) != EXPECTED
            or any(counters.get(key) != str(EXPECTED) for key in ("total", "executed", "passed"))
            or any(counters.get(key) != "0" for key in ("failed", "notExecuted"))
            or any(value != "0" for key, value in counters.attrib.items()
                   if key not in ("total", "executed", "passed"))):
        raise ValueError("Native TRX must prove exactly nine complete passing candidate facts")
    containers = []
    for container_name, child_name in (("Results", "UnitTestResult"),
                                       ("TestDefinitions", "UnitTest"), ("TestEntries", "TestEntry")):
        children = list(single_element(trx, container_name))
        if len(children) != EXPECTED or any(child.tag != TRX_NAMESPACE + child_name for child in children):
            raise ValueError("Native TRX contains missing or unsupported inventory records")
        containers.append(children)
    cases, definitions, entries = containers
    if any(element.tag.rsplit("}", 1)[-1] == "InnerResults"
           or element.tag.rsplit("}", 1)[-1].endswith("TestResult")
           for element in trx.iter() if element not in cases):
        raise ValueError("Native TRX contains nested result records")
    if any(element.tag.rsplit("}", 1)[-1] == "ErrorInfo"
           or (element.tag.rsplit("}", 1)[-1] == "RunInfo"
               and element.get("outcome") not in ("Passed", "Completed", "Warning", "Information"))
           for element in trx.iter()):
        raise ValueError("Native TRX contains a run-level error or unsupported outcome")
    names = [item.get("testName", "") for item in cases]
    if (len(set(names)) != EXPECTED or set(names) != EXPECTED_IDENTITIES
            or any(item.get("outcome") != "Passed" for item in cases)):
        raise ValueError("Native TRX does not match the exact original candidate fact roster")
    result_pairs = [(item.get("testId"), item.get("executionId")) for item in cases]
    entry_pairs = [(item.get("testId"), item.get("executionId")) for item in entries]
    definition_pairs = []
    for item in definitions:
        execution = single_element(item, "Execution")
        method = single_element(item, "TestMethod")
        if (method.get("className") != TEST_CLASS_FQN or method.get("name") not in EXPECTED_METHODS
                or TEST_CLASS_FQN + "." + method.get("name", "") != item.get("name")):
            raise ValueError("Native TRX method metadata disagrees with its frozen fact identity")
        definition_pairs.append((item.get("id"), execution.get("id")))
    for pairs in (result_pairs, entry_pairs, definition_pairs):
        if (any(not all(pair) for pair in pairs) or len(set(pairs)) != EXPECTED
                or len({pair[0] for pair in pairs}) != EXPECTED
                or len({pair[1] for pair in pairs}) != EXPECTED):
            raise ValueError("Native TRX contains missing or duplicate test/execution identities")
    if set(result_pairs) != set(entry_pairs) or set(result_pairs) != set(definition_pairs):
        raise ValueError("Native TRX result, definition and entry identities are not bijective")
    definition_names = {item.get("id"): item.get("name") for item in definitions}
    if any(definition_names[item.get("testId")] != item.get("testName") for item in cases):
        raise ValueError("Native TRX definition names disagree with their original results")
    return sorted(names)


def compiled_inventory(discovery: str) -> list[str]:
    # Remove only the CLI presentation prefix; retain foreign entries and raw FQN whitespace.
    names = [line[4:] for line in discovery.splitlines() if line.startswith("    ") and line[4:]]
    if len(names) != EXPECTED or len(set(names)) != EXPECTED or set(names) != EXPECTED_IDENTITIES:
        raise ValueError("Compiled discovery does not match the exact nine-fact candidate roster")
    return sorted(names)


def local_git_environment() -> dict[str, str]:
    # Git children are local and must not inherit repository/index/config redirects.
    return {key: value for key, value in os.environ.items() if not key.upper().startswith("GIT_")}


def command(args: list[str], cwd: Path, log: Path | None = None, *, preserve_output: bool = False) -> str:
    completed = subprocess.run(args, cwd=cwd, capture_output=True, text=True, encoding="utf-8",
                               errors="replace", check=False,
                               env=local_git_environment() if args[0] == "git" else None)
    if log is not None:
        log.write_text(completed.stdout + completed.stderr, encoding="utf-8")
    if completed.returncode:
        raise RuntimeError("Candidate command failed: " + args[0] + "; inspect retained log")
    return completed.stdout if preserve_output else completed.stdout.strip()


def source_only_dotnet(subcommand: str, *args: str) -> list[str]:
    """Suppress ancestor build imports outside the immutable Product checkout."""
    if subcommand not in ("restore", "test"):
        raise ValueError("Unsupported native SDK action")
    return ["dotnet", subcommand, "-p:ImportDirectoryBuildProps=false",
            "-p:ImportDirectoryBuildTargets=false", *args]


def git_records(worktree: Path, *args: str) -> list[bytes]:
    """Keep Git paths as raw NUL-delimited bytes, including non-UTF-8 names."""
    completed = subprocess.run(["git", "-c", "core.autocrlf=false", *args], cwd=worktree,
                               capture_output=True, check=False, env=local_git_environment())
    if completed.returncode:
        raise RuntimeError("Git path inventory failed")
    if completed.stdout and not completed.stdout.endswith(b"\0"):
        raise ValueError("Git path inventory is not NUL-terminated")
    return completed.stdout.split(b"\0")[:-1] if completed.stdout else []


def verify_index_flags(worktree: Path) -> None:
    records = git_records(worktree, "ls-files", "-v", "-z", "--")
    if any(len(record) < 3 or record[1:2] != b" " or record[:1] != b"H" for record in records):
        raise ValueError("Git index flags hide tracked Product source")


def git(cwd: Path, *args: str) -> str:
    return command(["git", "-c", "core.autocrlf=false", *args], cwd)


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify_composition(worktree: Path, raw: bytes, source_blob: str, product_head: str, *,
                       allow_generated: bool = False) -> None:
    output = worktree / DESTINATION
    if output.is_symlink() or not output.is_file() or output.read_bytes() != raw:
        raise ValueError("Actual compiler-input bytes differ from the committed candidate")
    if git(worktree, "rev-parse", "HEAD") != product_head:
        raise ValueError("Composed Product HEAD changed")
    if git(worktree, "rev-parse", ":" + DESTINATION) != source_blob:
        raise ValueError("Composed candidate index differs from its committed Git blob")
    verify_index_flags(worktree)
    changed = git_records(worktree, "diff", "--cached", "--name-only", "-z", "HEAD", "--")
    unstaged = git_records(worktree, "diff", "--name-only", "-z", "--")
    if changed != [DESTINATION.encode("utf-8")] or unstaged:
        raise ValueError("Unrelated tracked Product source changed during composition")
    generated = ()
    if allow_generated:
        tracked = git_records(worktree, "ls-tree", "-r", "--name-only", "-z", product_head)
        directories = {path.rpartition(b"/")[0] for path in tracked if path.endswith(b".csproj")}
        generated = tuple((directory + b"/" if directory else b"") + name + b"/"
                          for directory in directories for name in (b"bin", b"obj"))
    untracked = git_records(worktree, "ls-files", "--others", "-z")
    if any(not path.startswith(generated) for path in untracked):
        raise ValueError("Unexpected untracked source appeared during independent composition")


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
    verify_index_flags(ROOT)
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
        "executionScope": "SYNTHETIC_HEADLESS_CORE_WINDOWS_PURE_ONLY",
        "expectedIdentities": sorted(EXPECTED_IDENTITIES),
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
                             check=True, capture_output=True, env=local_git_environment()).stdout
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_bytes(raw)
        source_blob = git(ROOT, "rev-parse", test_head + ":" + SOURCE)
        git(worktree, "add", "--", DESTINATION)
        verify_composition(worktree, raw, source_blob, arguments.product_head)
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
        command(source_only_dotnet("restore", project, "--nologo"), worktree, evidence / "restore.log")
        verify_composition(worktree, raw, source_blob, arguments.product_head, allow_generated=True)
        command(source_only_dotnet("test", project, "-c", "Release", "--no-restore", "-warnaserror",
                 "--filter", "FullyQualifiedName~" + TEST_CLASS,
                 "--logger", "trx;LogFileName=candidate.trx",
                 "--results-directory", str(evidence)), worktree, evidence / "xunit.log")
        verify_composition(worktree, raw, source_blob, arguments.product_head, allow_generated=True)
        names = native_inventory(evidence / "candidate.trx")
        discovery = command(source_only_dotnet("test", project, "-c", "Release", "--no-build",
                             "--list-tests", "--filter", "FullyQualifiedName~" + TEST_CLASS),
                            worktree, evidence / "compiled-discovery.log", preserve_output=True)
        if compiled_inventory(discovery) != names:
            raise ValueError("Native TRX and compiled discovery identities differ")
        verify_composition(worktree, raw, source_blob, arguments.product_head, allow_generated=True)
        receipt["nativeXunit"] = {"total": EXPECTED, "passed": EXPECTED,
                                   "failed": 0, "skipped": 0, "identities": sorted(names)}
        receipt["status"] = "SYNTHETIC_IMMUTABILITY_PASSED"
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
