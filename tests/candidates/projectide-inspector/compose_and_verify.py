#!/usr/bin/env python3
"""Verify frozen Inspector candidates in an isolated, exact Product composition."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
from uuid import UUID
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
PRODUCT_HEAD = "8d2346890f0a89bac4c887dc262d25bc33d11508"
PRODUCT_TREE = "f0102de83b9937e47ad6ba58e214500d5051a946"
EXECUTION_SCOPE = "SYNTHETIC_HEADLESS_ONLY"
SOURCES = {
    "tests/candidates/projectide-inspector/InspectorIdentityCandidateTests.cs":
        "tests/Coglatas.Tests/ProjectIde/InspectorIdentityCandidateTests.cs",
    "tests/candidates/projectide-inspector/projectide-inspector-identity.candidate.spec.ts":
        "frontend/src/app/features/projectide/projectide-inspector-identity.candidate.spec.ts",
}
SOURCE_BLOBS = {
    "tests/candidates/projectide-inspector/InspectorIdentityCandidateTests.cs":
        "58a57ad5afbac3723cac307473f22dea86534b41",
    "tests/candidates/projectide-inspector/projectide-inspector-identity.candidate.spec.ts":
        "e956562c1a1aa160c303bf8827d3c7146e0c4589",
}
RETAINED_SOURCES = dict(zip(SOURCES, ("candidate-source.cs", "candidate-source.spec.ts")))
TEST_CLASS_FQN = "Coglatas.Tests.ProjectIde.InspectorIdentityCandidateTests"
FACT_METHODS = (
    "EqualDiagnosticCloneIsRejectedWhileTheOriginalRemainsSelectable",
    "ReentrantNewRequestCannotPublishTheOldRelatedSelection",
)
FOREIGN_CASES = (
    ("tenantId", "wrong-scope"), ("projectId", "wrong-scope"),
    ("branchId", "wrong-scope"), ("revisionId", "wrong-scope"),
    ("documentId", "unavailable"), ("entityId", "unavailable"),
    ("relationId", "unavailable"),
)
TERMINAL_CASES = ("Cancelled", "Unsupported", "InternalError")
# Native theory display names contain arguments; TestMethod.name remains bare.
EXPECTED_METHODS = {TEST_CLASS_FQN + "." + name: name for name in FACT_METHODS}
EXPECTED_METHODS.update({
    TEST_CLASS_FQN + f'.ForeignLocationCannotReuseTheAcceptedFinding(dimension: "{dimension}", expected: "{expected}")':
        "ForeignLocationCannotReuseTheAcceptedFinding" for dimension, expected in FOREIGN_CASES
})
EXPECTED_METHODS.update({
    TEST_CLASS_FQN + f".TerminalRequestRejectsOldFindingsAndLateCompletion(status: {status})":
        "TerminalRequestRejectsOldFindingsAndLateCompletion" for status in TERMINAL_CASES
})
EXPECTED_IDENTITIES = frozenset(EXPECTED_METHODS)
XUNIT_COUNT = 12
TRX_NAMESPACE = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
ZERO_COUNTERS = (
    "failed", "error", "timeout", "aborted", "inconclusive", "passedButRunAborted",
    "notRunnable", "notExecuted", "disconnected", "warning", "completed", "inProgress", "pending",
)
ANGULAR_DESTINATION = SOURCES["tests/candidates/projectide-inspector/projectide-inspector-identity.candidate.spec.ts"]
ANGULAR_SUITE = "Independent Inspector identity and lifecycle candidates"
ANGULAR_TITLES = (
    "ignores an equal entity UUID in another known document without invalidating the selected record",
    "ignores an equal raw UUID used as a relation identity",
    "preserves compiler order and the exact property location when selecting a related finding",
    "rejects an equal cloned Diagnostic object while retaining the genuine object",
    "rejects an injected diagnostic with a foreign tenantId and clears its previous Source target",
    "rejects an injected diagnostic with a foreign projectId and clears its previous Source target",
    "rejects an injected diagnostic with a foreign branchId and clears its previous Source target",
    "rejects an injected diagnostic with a foreign revisionId and clears its previous Source target",
    "does not expose related findings for Stale results",
    "does not expose related findings for Undetermined results",
    "does not let an ignored-abort older read restore a cleared Inspector",
)
ANGULAR_IDENTITIES = frozenset(ANGULAR_SUITE + " " + title for title in ANGULAR_TITLES)
ANGULAR_COUNT = 11
QUALIFICATION_FLAGS = (
    "requiredCheckQualified", "independentReviewComplete", "nativeUiQualified",
    "productionQualified", "trustQualified", "releaseQualified",
)


def single_element(parent: ET.Element, name: str) -> ET.Element:
    elements = parent.findall("{*}" + name)
    if len(elements) != 1 or elements[0].tag != TRX_NAMESPACE + name:
        raise ValueError("Native TRX requires one " + name + " element")
    return elements[0]


def native_inventory(path: Path) -> list[str]:
    """Admit the original twelve displays and their separate native method identities."""
    trx = ET.parse(path).getroot()
    if trx.tag != TRX_NAMESPACE + "TestRun":
        raise ValueError("Native TRX root or namespace is invalid")
    summary = single_element(trx, "ResultSummary")
    counters = single_element(summary, "Counters")
    if (summary.get("outcome") != "Completed" or len(EXPECTED_IDENTITIES) != XUNIT_COUNT
            or any(counters.get(key) != "12" for key in ("total", "executed", "passed"))
            or any(counters.get(key) != "0" for key in ZERO_COUNTERS)
            or any(value != "0" for key, value in counters.attrib.items()
                   if key not in ("total", "executed", "passed"))):
        raise ValueError("Native TRX must contain exactly twelve complete passing Inspector cases")
    if trx.findall(".//{*}ErrorInfo") or any(
            node.get("outcome") in ("Error", "Failed", "Aborted", "Timeout")
            for node in trx.findall(".//{*}RunInfo")):
        raise ValueError("Native TRX contains contradictory failure information")
    containers = []
    for container_name, child_name in (("Results", "UnitTestResult"),
                                       ("TestDefinitions", "UnitTest"), ("TestEntries", "TestEntry")):
        children = list(single_element(trx, container_name))
        if len(children) != XUNIT_COUNT or any(child.tag != TRX_NAMESPACE + child_name for child in children):
            raise ValueError("Native TRX contains missing or unsupported inventory records")
        containers.append(children)
    cases, definitions, entries = containers
    names = [item.get("testName", "") for item in cases]
    if (set(names) != EXPECTED_IDENTITIES or len(set(names)) != XUNIT_COUNT
            or any(item.get("outcome") != "Passed" for item in cases)):
        raise ValueError("Native TRX does not match the exact Inspector case roster")
    result_pairs = [(item.get("testId"), item.get("executionId")) for item in cases]
    entry_pairs = [(item.get("testId"), item.get("executionId")) for item in entries]
    definition_pairs = []
    for item in definitions:
        execution = single_element(item, "Execution")
        method = single_element(item, "TestMethod")
        expected_method = EXPECTED_METHODS.get(item.get("name", ""))
        if (expected_method is None or method.get("className") != TEST_CLASS_FQN
                or method.get("name") != expected_method):
            raise ValueError("Native TRX method metadata disagrees with its frozen case identity")
        definition_pairs.append((item.get("id"), execution.get("id")))
    for pairs in (result_pairs, entry_pairs, definition_pairs):
        if (any(not all(pair) for pair in pairs) or len(set(pairs)) != XUNIT_COUNT
                or len({pair[0] for pair in pairs}) != XUNIT_COUNT
                or len({pair[1] for pair in pairs}) != XUNIT_COUNT):
            raise ValueError("Native TRX contains missing or duplicate test/execution identities")
        for pair in pairs:
            for identity in pair:
                try:
                    valid = str(UUID(identity)) == identity
                except (ValueError, AttributeError):
                    valid = False
                if not valid:
                    raise ValueError("Native TRX test/execution identity is not a canonical UUID")
    if set(result_pairs) != set(entry_pairs) or set(result_pairs) != set(definition_pairs):
        raise ValueError("Native TRX result, definition and entry identities are not bijective")
    definition_names = {item.get("id"): item.get("name") for item in definitions}
    if any(definition_names[item.get("testId")] != item.get("testName") for item in cases):
        raise ValueError("Native TRX definition names disagree with their original results")
    return sorted(names)


def compiled_inventory(discovery: str) -> list[str]:
    lines = discovery.splitlines()
    marker = "The following Tests are available:"
    if lines.count(marker) != 1:
        raise ValueError("Compiled discovery requires one native inventory header")
    records = [line for line in lines[lines.index(marker) + 1:] if line]
    # Remove only the CLI's four-space presentation prefix. Never trim identities.
    if any(not line.startswith("    ") for line in records):
        raise ValueError("Compiled discovery contains unsupported inventory records")
    names = [line[4:] for line in records]
    if len(names) != XUNIT_COUNT or len(set(names)) != XUNIT_COUNT or set(names) != EXPECTED_IDENTITIES:
        raise ValueError("Compiled discovery does not match the exact twelve-case Inspector roster")
    return sorted(names)


def unique_json_object(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Angular report contains duplicate JSON keys")
        result[key] = value
    return result


def reject_json_constant(value: str) -> None:
    raise ValueError("Angular report contains a non-finite JSON constant")


def has_error_information(record: dict[str, object]) -> bool:
    return any(record.get(key) not in (None, "", []) for key in
               ("error", "errors", "unhandledErrors", "testExecError", "failureMessage", "failureMessages", "failureDetails"))


def angular_inventory(path: Path, expected_source: str | Path) -> list[str]:
    parsed = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique_json_object,
                        parse_constant=reject_json_constant)
    # Vitest counts the file and describe group as two suites, but emits one file.
    counts = {"numTotalTestSuites": 2, "numPassedTestSuites": 2, "numFailedTestSuites": 0,
              "numPendingTestSuites": 0, "numTotalTests": 11, "numPassedTests": 11,
              "numFailedTests": 0, "numPendingTests": 0, "numTodoTests": 0}
    if (not isinstance(parsed, dict) or parsed.get("success") is not True
            or any(type(parsed.get(key)) is not int or parsed[key] != value for key, value in counts.items())
            or has_error_information(parsed)):
        raise ValueError("Angular report must contain exactly eleven complete passing Inspector cases")
    snapshot = parsed.get("snapshot")
    zero_snapshot = {key: 0 for key in ("added", "filesAdded", "filesRemoved", "filesUnmatched", "filesUpdated",
                                       "matched", "total", "unchecked", "unmatched", "updated")}
    if (not isinstance(snapshot, dict)
            or any(type(snapshot.get(key)) is not int or snapshot[key] != value for key, value in zero_snapshot.items())
            or snapshot.get("failure") is not False or snapshot.get("didUpdate") is not False
            or snapshot.get("filesRemovedList") != [] or snapshot.get("uncheckedKeysByFile") != []):
        raise ValueError("Angular report contains unexpected snapshot results")
    suites = parsed.get("testResults")
    if not isinstance(suites, list) or len(suites) != 1 or not isinstance(suites[0], dict):
        raise ValueError("Angular report requires exactly one Inspector source file")
    suite = suites[0]
    expected_path = str(expected_source).replace("\\", "/")
    actual_path = suite.get("name")
    if (not isinstance(actual_path, str) or actual_path.replace("\\", "/") != expected_path
            or not expected_path.endswith("/" + ANGULAR_DESTINATION)):
        raise ValueError("Angular report source filename differs from the composed candidate")
    if suite.get("status") != "passed" or suite.get("message") != "" or has_error_information(suite):
        raise ValueError("Angular source suite contains failure information")
    assertions = suite.get("assertionResults")
    if (not isinstance(assertions, list) or len(assertions) != ANGULAR_COUNT
            or any(not isinstance(case, dict) for case in assertions)):
        raise ValueError("Angular report does not match the exact Inspector assertion roster")
    names = [case.get("fullName") for case in assertions]
    if (any(not isinstance(name, str) for name in names)
            or set(names) != ANGULAR_IDENTITIES or len(set(names)) != ANGULAR_COUNT):
        raise ValueError("Angular report does not match the exact Inspector assertion roster")
    for case in assertions:
        if (case.get("status") != "passed" or case.get("failureMessages") != []
                or has_error_information(case) or case.get("ancestorTitles") != [ANGULAR_SUITE]
                or case.get("title") not in ANGULAR_TITLES
                or ANGULAR_SUITE + " " + case["title"] != case["fullName"]):
            raise ValueError("Angular assertion metadata or failure information is inconsistent")
    return sorted(names)


def local_git_environment() -> dict[str, str]:
    # Preserve SDK/host settings, never inherited repository/index/config overrides.
    return {key: value for key, value in os.environ.items() if not key.upper().startswith("GIT_")}


def run(command: list[str], cwd: Path, log: Path | None = None, *, preserve_output: bool = False) -> str:
    result = subprocess.run(command, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                            text=True, encoding="utf-8", errors="replace", check=False,
                            env=local_git_environment())
    if log is not None:
        log.write_text(result.stdout, encoding="utf-8")
    if result.returncode:
        raise RuntimeError(f"Command failed ({result.returncode}): {command[:3]}; inspect retained log")
    return result.stdout if preserve_output else result.stdout.strip()


def git(cwd: Path, *arguments: str) -> str:
    return run(["git", "-c", "core.autocrlf=false", *arguments], cwd)


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify_working_tree(worktree: Path, raw_sources: dict[str, bytes], source_blobs: dict[str, str]) -> None:
    if set(raw_sources) != set(SOURCES.values()) or set(source_blobs) != set(raw_sources):
        raise ValueError("Composition source inventory differs from the two frozen candidates")
    for destination, raw in raw_sources.items():
        output = worktree / destination
        parents = output.relative_to(worktree).parents
        if (output.is_symlink() or not output.is_file() or output.read_bytes() != raw
                or any((worktree / parent).is_symlink() for parent in parents)):
            raise ValueError("Composed working-tree Test source changed from the committed blob")
        expected_stage = "100644 " + source_blobs[destination] + " 0\t" + destination
        if git(worktree, "ls-files", "--stage", "--", destination) != expected_stage:
            raise ValueError("Composed candidate index identity or mode changed")
    if git(worktree, "rev-parse", "HEAD") != PRODUCT_HEAD:
        raise ValueError("Composed Product HEAD changed")
    expected = sorted(raw_sources)
    if (git(worktree, "diff", "--cached", "--name-only", "HEAD", "--").splitlines() != expected
            or git(worktree, "diff", "--name-only", "--")):
        raise ValueError("Tracked Product source changed during independent composition")
    if git(worktree, "ls-files", "--others", "--exclude-standard"):
        raise ValueError("Unexpected untracked source appeared during independent composition")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--product-head", required=True)
    parser.add_argument("--worktree", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    args = parser.parse_args()
    if not re.fullmatch(r"[0-9a-f]{40}", args.product_head) or args.product_head != PRODUCT_HEAD:
        raise ValueError("Only the frozen immutable Inspector Product SHA is admitted")
    target, evidence = args.worktree.resolve(), args.evidence.resolve()
    if target.exists() or evidence.exists() or target == evidence or target in evidence.parents or evidence in target.parents:
        raise ValueError("Distinct, fresh, non-nested composition and evidence paths are mandatory")
    if git(ROOT, "status", "--porcelain", "--untracked-files=all"):
        raise ValueError("Commit the independent sources before qualification")
    test_head = git(ROOT, "rev-parse", "HEAD")
    if test_head == args.product_head:
        raise ValueError("Test and Product source owners must be separate commits")
    git(ROOT, "cat-file", "-e", args.product_head + "^{commit}")
    if git(ROOT, "rev-parse", args.product_head + "^{tree}") != PRODUCT_TREE:
        raise ValueError("Immutable Inspector Product tree mismatch")
    evidence.mkdir(parents=True)
    receipt: dict[str, object] = {
        "productHead": args.product_head, "testHead": test_head, "executionScope": EXECUTION_SCOPE,
        **dict.fromkeys(QUALIFICATION_FLAGS, False), "status": "NOT_EXECUTED",
    }
    try:
        run(["git", "-c", "core.autocrlf=false", "worktree", "add", "--detach", str(target), args.product_head],
            ROOT, evidence / "worktree.log")
        sources, raw_sources, source_blobs = {}, {}, {}
        for original, destination in SOURCES.items():
            output = target / destination
            if output.exists() or output.is_symlink():
                raise ValueError(f"Unexpected existing candidate destination: {destination}")
            source_blob = git(ROOT, "rev-parse", f"{test_head}:{original}")
            if source_blob != SOURCE_BLOBS[original]:
                raise ValueError("Independent Inspector candidate source changed from its frozen blob")
            raw = subprocess.run(["git", "show", f"{test_head}:{original}"], cwd=ROOT,
                                 stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True,
                                 env=local_git_environment()).stdout
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_bytes(raw)
            (evidence / RETAINED_SOURCES[original]).write_bytes(raw)
            git(target, "add", "--", destination)
            raw_sources[destination], source_blobs[destination] = raw, source_blob
            sources[destination] = {"source": original, "gitBlob": source_blob, "sha256": digest(output),
                                    "retainedSource": RETAINED_SOURCES[original]}
        verify_working_tree(target, raw_sources, source_blobs)
        receipt.update(productTree=git(target, "rev-parse", "HEAD^{tree}"),
                       composedTree=git(target, "write-tree"), sources=sources)
        (evidence / "source-receipt.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")

        sdk = run(["dotnet", "--version"], target, evidence / "sdk.log")
        if sdk != "10.0.403":
            raise ValueError("Actual SDK does not match Product global.json pin")
        verify_working_tree(target, raw_sources, source_blobs)
        project = "tests/Coglatas.Tests/Coglatas.Tests.csproj"
        run(["dotnet", "restore", project, "--nologo"], target, evidence / "restore.log")
        verify_working_tree(target, raw_sources, source_blobs)
        run(["dotnet", "test", project, "-c", "Release", "--no-restore", "-warnaserror",
             "--filter", "FullyQualifiedName~InspectorIdentityCandidateTests",
             "--logger", "trx;LogFileName=inspector.trx", "--results-directory", str(evidence)],
            target, evidence / "xunit.log")
        verify_working_tree(target, raw_sources, source_blobs)
        names = native_inventory(evidence / "inspector.trx")
        discovery = run(["dotnet", "test", project, "-c", "Release", "--no-build", "--list-tests",
                         "--filter", "FullyQualifiedName~InspectorIdentityCandidateTests"],
                        target, evidence / "compiled-discovery.log", preserve_output=True)
        verify_working_tree(target, raw_sources, source_blobs)
        if compiled_inventory(discovery) != names:
            raise ValueError("Native TRX and compiled discovery identities differ")
        receipt["xunit"] = {"total": XUNIT_COUNT, "passed": XUNIT_COUNT, "failed": 0, "skipped": 0,
                            "identities": names}

        npm = "npm.cmd" if os.name == "nt" else "npm"
        run([npm, "ci", "--ignore-scripts", "--no-audit", "--no-fund"], target / "frontend", evidence / "npm.log")
        verify_working_tree(target, raw_sources, source_blobs)
        report = evidence / "vitest.json"
        run([npm, "test", "--", "--include=src/app/features/projectide/projectide-inspector-identity.candidate.spec.ts",
             "--reporters=json", "--output-file=" + str(report)], target / "frontend", evidence / "vitest.log")
        verify_working_tree(target, raw_sources, source_blobs)
        names = angular_inventory(report, target / ANGULAR_DESTINATION)
        receipt["vitest"] = {"total": ANGULAR_COUNT, "passed": ANGULAR_COUNT, "failed": 0, "skipped": 0,
                             "identities": names}
        receipt["status"] = "SYNTHETIC_HEADLESS_PASSED"
    except Exception as error:
        receipt["status"] = "FAILED_OR_INCOMPLETE"
        receipt["error"] = type(error).__name__ + ": " + str(error)
        raise
    finally:
        receipt["retainedSha256"] = {
            path.name: digest(path) for path in sorted(evidence.iterdir())
            if path.is_file() and path.name != "receipt.json"
        }
        (evidence / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(receipt, indent=2))


if __name__ == "__main__":
    main()
