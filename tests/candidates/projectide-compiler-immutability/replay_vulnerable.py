#!/usr/bin/env python3
"""Independent historical RED replay of the three original Compiler SyncRoot cases."""
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
VULNERABLE = "2b2c86a447779c2d9d19e4b88f1274a70737d54e"
NAMES = [
    "LockedManifestRejectsPublicBackingMutationAndRetainsCompilerIdentity",
    "EvidencePremisesPreserveCanonicalBasisAgainstSyncRootAndCallerArrayMutation",
    "TypedRuleOutcomeCannotChangeRetainedVerdictOrResultDigest",
]
TEST_CLASS_FQN = "Coglatas.Tests.ProjectIde.SecurityCompilerImmutableBoundaryCandidateTests"
EXPECTED_METHODS = tuple(NAMES)
EXPECTED_IDENTITIES = frozenset(TEST_CLASS_FQN + "." + name for name in NAMES)
EXPECTED = 3
TRX_NAMESPACE = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
REFUSAL = "A legacy synchronization root must not expose mutable storage."


def single_element(parent: ET.Element, name: str) -> ET.Element:
    elements = parent.findall("{*}" + name)
    if len(elements) != 1 or elements[0].tag != TRX_NAMESPACE + name:
        raise ValueError("Native TRX requires one " + name + " element")
    return elements[0]


def native_inventory(path: Path) -> list[str]:
    """Admit only the three exact original assertion failures and their native identities."""
    trx = ET.parse(path).getroot()
    if trx.tag != TRX_NAMESPACE + "TestRun":
        raise ValueError("Native TRX root or namespace is invalid")
    summary = single_element(trx, "ResultSummary")
    counters = single_element(summary, "Counters")
    if (summary.get("outcome") != "Failed" or len(EXPECTED_IDENTITIES) != EXPECTED
            or any(counters.get(key) != str(EXPECTED) for key in ("total", "executed", "failed"))
            or any(counters.get(key) != "0" for key in ("passed", "notExecuted"))
            or any(value != "0" for key, value in counters.attrib.items()
                   if key not in ("total", "executed", "failed"))):
        raise ValueError("Native TRX must prove exactly three complete historical failing facts")
    containers = []
    for container_name, child_name in (("Results", "UnitTestResult"),
                                       ("TestDefinitions", "UnitTest"), ("TestEntries", "TestEntry")):
        children = list(single_element(trx, container_name))
        if len(children) != EXPECTED or any(child.tag != TRX_NAMESPACE + child_name for child in children):
            raise ValueError("Native TRX contains missing or unsupported inventory records")
        containers.append(children)
    cases, definitions, entries = containers
    if any(element.tag.rsplit("}", 1)[-1] in ("InnerResults", "TestResultAggregation")
           or element.tag.rsplit("}", 1)[-1].endswith("TestResult")
           for element in trx.iter() if element not in cases):
        raise ValueError("Native TRX contains nested result records")
    if any(element.tag.rsplit("}", 1)[-1] == "ErrorInfo" for element in summary.iter()):
        raise ValueError("Historical TRX contains an unrelated run-level error")
    run_infos = list(single_element(summary, "RunInfos"))
    all_run_infos = [element for element in trx.iter() if element.tag.rsplit("}", 1)[-1] == "RunInfo"]
    if (len(run_infos) != EXPECTED or any(info.tag != TRX_NAMESPACE + "RunInfo" for info in run_infos)
            or all_run_infos != run_infos):
        raise ValueError("Historical TRX requires exactly the original xUnit failure notifications")
    notified = []
    for info in run_infos:
        text = single_element(info, "Text").text or ""
        notification = re.fullmatch(r"\[xUnit\.net [0-9]+:[0-9]{2}:[0-9]{2}\.[0-9]+\] +([^\r\n]+) \[FAIL\]", text)
        if info.get("outcome") != "Error" or notification is None:
            raise ValueError("Historical TRX contains an unrelated run-level error")
        notified.append(notification.group(1))
    if len(set(notified)) != EXPECTED or set(notified) != EXPECTED_IDENTITIES:
        raise ValueError("Historical TRX failure notifications differ from the original fact roster")
    names = [item.get("testName", "") for item in cases]
    if (len(set(names)) != EXPECTED or set(names) != EXPECTED_IDENTITIES
            or any(item.get("outcome") != "Failed" for item in cases)):
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
    helper_frame = re.compile(
        r"\s*at " + re.escape(TEST_CLASS_FQN)
        + r"\.AssertSealed(?:\[[^\]\r\n]+\])?\([^\)\r\n]*\)(?: in [^\r\n]+:line [0-9]+)?\s*")
    case_errors = []
    for result in cases:
        output = single_element(result, "Output")
        error = single_element(output, "ErrorInfo")
        case_errors.append(error)
        message = single_element(error, "Message").text or ""
        stack = single_element(error, "StackTrace").text or ""
        caller_frame = re.compile(r"\s*at " + re.escape(result.get("testName", ""))
                                  + r"\(\)(?: in [^\r\n]+:line [0-9]+)?\s*")
        frames = stack.splitlines()
        helper_positions = [index for index, frame in enumerate(frames) if helper_frame.fullmatch(frame)]
        caller_positions = [index for index, frame in enumerate(frames) if caller_frame.fullmatch(frame)]
        if (not message.splitlines() or message.splitlines()[0] != REFUSAL
                or len(helper_positions) != 1 or len(caller_positions) != 1
                or helper_positions[0] >= caller_positions[0]):
            raise ValueError("Historical case did not fail at the exact original SyncRoot invariant and native frames")
    if [element for element in trx.iter() if element.tag.rsplit("}", 1)[-1] == "ErrorInfo"] != case_errors:
        raise ValueError("Historical TRX contains error records outside the original assertion failures")
    return sorted(names)


def compiled_inventory(discovery: str) -> list[str]:
    # Remove only the CLI presentation prefix; retain foreign entries and raw FQN whitespace.
    names = [line[4:] for line in discovery.splitlines() if line.startswith("    ") and line[4:]]
    if len(names) != EXPECTED or len(set(names)) != EXPECTED or set(names) != EXPECTED_IDENTITIES:
        raise ValueError("Compiled discovery does not match the exact three-fact historical roster")
    return sorted(names)


def local_git_environment() -> dict[str, str]:
    return {key: value for key, value in os.environ.items() if not key.upper().startswith("GIT_")}


def source_only_dotnet(subcommand: str, *args: str) -> list[str]:
    if subcommand not in ("restore", "test"):
        raise ValueError("Unsupported native SDK action")
    return ["dotnet", subcommand, "-p:ImportDirectoryBuildProps=false",
            "-p:ImportDirectoryBuildTargets=false", *args]


def command(args: list[str], cwd: Path, log: Path | None = None,
            success: bool = True) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(args, cwd=cwd, capture_output=True, text=True,
                            encoding="utf-8", errors="replace", check=False,
                            env=local_git_environment() if args[0] == "git" else None)
    if log is not None:
        log.write_text(result.stdout + result.stderr, encoding="utf-8")
    if success and result.returncode:
        raise RuntimeError("Historical replay command failed: " + args[0])
    return result


def git(cwd: Path, *args: str) -> str:
    return command(["git", "-c", "core.autocrlf=false", *args], cwd).stdout.strip()


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


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
    changed = git_records(worktree, "diff", "--cached", "--no-renames", "--name-only", "-z", "HEAD", "--")
    unstaged = git_records(worktree, "diff", "--no-renames", "--name-only", "-z", "--")
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
    parser.add_argument("--vulnerable-head", required=True)
    parser.add_argument("--worktree", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    args = parser.parse_args()
    if not re.fullmatch(r"[0-9a-f]{40}", args.vulnerable_head) or args.vulnerable_head != VULNERABLE:
        raise ValueError("Historical RED Product commit must be the exact recorded vulnerable SHA")
    target, evidence = args.worktree.resolve(), args.evidence.resolve()
    if target.exists() or evidence.exists() or target == evidence or target in evidence.parents or evidence in target.parents:
        raise ValueError("Fresh disjoint replay and evidence directories are required")
    verify_index_flags(ROOT)
    if git(ROOT, "status", "--porcelain", "--untracked-files=all"):
        raise ValueError("Committed independent Security Test source required")
    test_head = git(ROOT, "rev-parse", "HEAD")
    git(ROOT, "cat-file", "-e", VULNERABLE + "^{commit}")
    evidence.mkdir(parents=True)
    record: dict[str, object] = {
        "testHead": test_head, "vulnerableProductHead": VULNERABLE,
        "executionScope": "HISTORICAL_COMPILER_SYNCROOT_RED_ONLY",
        "requiredCheckQualified": False, "independentReviewComplete": False,
        "nativeUiQualified": False, "productionQualified": False,
        "trustQualified": False, "releaseQualified": False,
        "status": "NOT_EXECUTED",
    }
    try:
        command(["git", "-c", "core.autocrlf=false", "worktree", "add",
                 "--detach", str(target), VULNERABLE], ROOT, evidence / "worktree.log")
        dest = target / DESTINATION
        if dest.exists():
            raise ValueError("Historical Product already contains the independent test destination")
        raw = subprocess.run(["git", "show", test_head + ":" + SOURCE], cwd=ROOT,
                             check=True, capture_output=True, env=local_git_environment()).stdout
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(raw)
        source_blob = git(ROOT, "rev-parse", test_head + ":" + SOURCE)
        git(target, "add", "--", DESTINATION)
        verify_composition(target, raw, source_blob, VULNERABLE)
        record.update(sourceBlob=source_blob, sourceSha256=digest(dest),
                      productTree=git(target, "rev-parse", "HEAD^{tree}"),
                      composedTree=git(target, "write-tree"))
        (evidence / "source-receipt.json").write_text(
            json.dumps(record, sort_keys=True, indent=2) + "\n", encoding="utf-8")
        sdk = command(["dotnet", "--version"], target, evidence / "sdk.log").stdout.strip()
        if sdk != "10.0.403":
            raise ValueError("Historical Product SDK 10.0.403 policy not satisfied")
        project = "tests/Coglatas.Tests/Coglatas.Tests.csproj"
        command(source_only_dotnet("restore", project, "--nologo"), target, evidence / "restore.log")
        verify_composition(target, raw, source_blob, VULNERABLE, allow_generated=True)
        expression = "|".join("FullyQualifiedName~" + name for name in NAMES)
        executed = command(source_only_dotnet("test", project, "-c", "Release", "--no-restore", "-warnaserror",
                            "--filter", expression, "--logger", "trx;LogFileName=historical-red.trx",
                            "--results-directory", str(evidence)), target, evidence / "xunit.log", success=False)
        if executed.returncode != 1:
            raise ValueError("Expected actual native xUnit assertions to fail, not pass or abort")
        verify_composition(target, raw, source_blob, VULNERABLE, allow_generated=True)
        names = native_inventory(evidence / "historical-red.trx")
        discovery = command(source_only_dotnet("test", project, "-c", "Release", "--no-build",
                             "--list-tests", "--filter", expression), target,
                            evidence / "compiled-discovery.log").stdout
        if compiled_inventory(discovery) != names:
            raise ValueError("Historical native and compiled discovery identities differ")
        verify_composition(target, raw, source_blob, VULNERABLE, allow_generated=True)
        record["historicalXunit"] = {
            "total": 3, "passed": 0, "failed": 3, "notExecuted": 0,
            "expectedFailureInvariant": REFUSAL, "identities": sorted(names),
        }
        record["status"] = "EXPECTED_HISTORICAL_RED_VERIFIED"
    except Exception as error:
        record["status"] = "FAILED_OR_INCOMPLETE"
        record["error"] = type(error).__name__ + ": " + str(error)
        raise
    finally:
        record["retainedSha256"] = {
            path.name: digest(path) for path in sorted(evidence.iterdir())
            if path.is_file() and path.name != "receipt.json"
        }
        (evidence / "receipt.json").write_text(
            json.dumps(record, sort_keys=True, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(record, sort_keys=True, indent=2))


if __name__ == "__main__":
    main()
