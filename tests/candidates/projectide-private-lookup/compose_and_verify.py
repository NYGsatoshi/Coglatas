#!/usr/bin/env python3
"""Compose independent retained private-index lookup assertions over immutable Product."""
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
SOURCE = "tests/candidates/projectide-private-lookup/SecurityPrivateLookupCandidateTests.cs"
DESTINATION = "tests/Coglatas.Tests/ProjectIde/SecurityPrivateLookupCandidateTests.cs"
TEST_CLASS = "SecurityPrivateLookupCandidateTests"
TEST_CLASS_FQN = "Coglatas.Tests.ProjectIde." + TEST_CLASS
CACHE_METHODS = (
    'ColdWarmAndUncachedIndexesHaveEqualBytesButCurrentRows',
    'ReusedPositionsResolveRenamedDeclarationsFromCurrentRows',
    'ReusedPositionsReadCurrentPortTypeAndCompletionKinds',
    'ReusedPositionsReadCurrentDocumentMetadata',
    'ReusedOriginPositionsReadNewTargetsAndReferenceInstances',
    'UnresolvedCurrentTargetsCannotBorrowPreviousResolution',
    'WarmIndexesRejectReferenceCursorsFromEarlierInstances',
    'WarmIndexesRejectCompletionCursorsFromEarlierInstances',
    'PublicCollectionsAndDetachedBytesCannotPoisonRetainedPositions',
    'StaleAndForeignQueriesExposeNoBorrowedRowsOrInputStamp',
    'EveryTenantProjectAndBranchSwitchInvalidatesRetentionBeforeStaleBuild',
    'PreCancelledOwnerChangePreservesTheExistingPublicCache',
    'LateDerivedBudgetRefusalCannotCommitColdOrReusedTables',
    'ClearBeforePrivatePreparationCannotRepopulateAnInvalidatedLifetime',
    'NewOwnerBeforePrivatePreparationCannotReplaceItsRetainedTable',
    'FreshPrivateLifetimeStillPublishesAndReusesItsActualTables',
    'ClearAfterPreparationPreventsLatePrivateCommit',
    'NewOwnerAfterPreparationPreventsLatePrivateCommit',
    'CancelledPrivateCommitCannotPublishPreparedPositions',
    'BoundedEvictionKeepsCurrentResultsAndReusesOnlyTheRetainedRoster',
    'OversizedRosterKeysDeclineRetentionWithoutChangingQueries',
    'OverlappingPublicBuildersAndClearsKeepOriginalInputOwnership',
    'SeparateCompilationsExportOriginalReferenceIdentitiesAndEqualBytes',
    'InvalidRetentionBoundsAreRefusedBeforeAUsableCacheExists',
)
EXPECTED_IDENTITIES = frozenset(
    TEST_CLASS_FQN + "." + method
    for method in CACHE_METHODS
)
EXPECTED = 24
TRX_NAMESPACE = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
PRODUCT_HEAD = "1948e1834f79dfcf10bad5f0308546b2bcd939ae"
PRODUCT_TREE = "5f8e0ce0b56ee5812277f5582de41ef9ee85fedd"
EXECUTION_SCOPE = "SYNTHETIC_PRIVATE_LOOKUP_CORE_ONLY"
HISTORICAL_HEAD = "1696603417073e21db216244aabe72427fd61ced"
HISTORICAL_TREE = "77611a2465d30637abac525a9ee43d4009578254"
HISTORICAL_FAILURE_METHODS = (
    "ClearBeforePrivatePreparationCannotRepopulateAnInvalidatedLifetime",
    "NewOwnerBeforePrivatePreparationCannotReplaceItsRetainedTable",
)
HISTORICAL_POSITIVE_METHOD = "FreshPrivateLifetimeStillPublishesAndReusesItsActualTables"
HISTORICAL_IDENTITIES = frozenset(TEST_CLASS_FQN + "." + name
    for name in (*HISTORICAL_FAILURE_METHODS, HISTORICAL_POSITIVE_METHOD))
HISTORICAL_FAILURES = frozenset(TEST_CLASS_FQN + "." + name for name in HISTORICAL_FAILURE_METHODS)
HISTORICAL_INVARIANT = "Invalidated private lookup work must preserve current owner retention."
GENERATED_PROJECT_ROOTS = (
    "tests/Coglatas.Tests",
    "src/Coglatas.Domain",
    "src/Coglatas.Application",
    "src/Coglatas.Infrastructure",
    "src/Coglatas.Web",
    "src/Coglatas.UI.Core",
    "tools/Coglatas.SecurityArchitecture",
    "tools/ProjectIde.Workbench.Synthetic",
    "tools/ProjectIde.Tests",
)
GENERATED_OUTPUT_PREFIXES = tuple(root + "/" + directory + "/"
    for root in GENERATED_PROJECT_ROOTS for directory in ("bin", "obj"))


def selected_product(product_head: str, historical: bool) -> str:
    if product_head != (HISTORICAL_HEAD if historical else PRODUCT_HEAD):
        raise ValueError("Only the independently reviewed immutable Product commit is admitted")
    return HISTORICAL_TREE if historical else PRODUCT_TREE


def single_element(parent: ET.Element, name: str) -> ET.Element:
    elements = parent.findall("{*}" + name)
    if len(elements) != 1 or elements[0].tag != TRX_NAMESPACE + name:
        raise ValueError("Native TRX requires one " + name + " element")
    return elements[0]


def native_inventory(path: Path, *, historical: bool = False) -> list[str]:
    """Admit the frozen raw fact identities and their complete native TRX binding."""
    trx = ET.parse(path).getroot()
    if trx.tag != TRX_NAMESPACE + "TestRun":
        raise ValueError("Native TRX root or namespace is invalid")
    summary = single_element(trx, "ResultSummary")
    counters = single_element(summary, "Counters")
    if summary.find(".//{*}ErrorInfo") is not None:
        raise ValueError("Native TRX contains a run-level error")
    run_infos = summary.findall("{*}RunInfos")
    if len(run_infos) > 1 or any(item.tag != TRX_NAMESPACE + "RunInfos" for item in run_infos):
        raise ValueError("Native TRX contains unsupported run-level information")
    identities = HISTORICAL_IDENTITIES if historical else EXPECTED_IDENTITIES
    expected = len(identities)
    failures = len(HISTORICAL_FAILURES) if historical else 0
    if (summary.get("outcome") != ("Failed" if historical else "Completed")
            or expected != (3 if historical else EXPECTED)
            or any(counters.get(key) != str(expected) for key in ("total", "executed"))
            or counters.get("passed") != str(expected - failures)
            or counters.get("failed") != str(failures)
            or counters.get("notExecuted") != "0"
            or any(value != "0" for key, value in counters.attrib.items()
                   if key not in ("total", "executed", "passed", "failed"))):
        if historical:
            raise ValueError("Historical TRX must contain exactly two failed and one passing executed controls")
        raise ValueError("Native TRX must contain exactly twenty-four complete passing candidate facts")
    containers = []
    for container_name, child_name in (("Results", "UnitTestResult"),
                                       ("TestDefinitions", "UnitTest"), ("TestEntries", "TestEntry")):
        children = list(single_element(trx, container_name))
        if len(children) != expected or any(child.tag != TRX_NAMESPACE + child_name for child in children):
            raise ValueError("Native TRX contains missing or unsupported inventory records")
        containers.append(children)
    cases, definitions, entries = containers
    if any(item.get("outcome") == "Passed" and item.find(".//{*}ErrorInfo") is not None for item in cases):
        raise ValueError("Native TRX passing result contains error information")
    names = [item.get("testName", "") for item in cases]
    if (set(names) != identities or len(set(names)) != expected
            or any(item.get("outcome") != ("Failed" if historical and item.get("testName") in HISTORICAL_FAILURES else "Passed") for item in cases)):
        raise ValueError("Native TRX does not match the exact private-lookup fact roster")
    verified_failures = set()
    if historical:
        for item in cases:
            if item.get("testName") not in HISTORICAL_FAILURES:
                continue
            output = single_element(item, "Output")
            error = single_element(output, "ErrorInfo")
            message = single_element(error, "Message").text or ""
            stack = single_element(error, "StackTrace").text or ""
            exact_frame = (r"[ \t]*at " + re.escape(item.get("testName", ""))
                           + r"\(\)(?: in [^\r\n]+:line [1-9][0-9]*)?[ \t]*")
            if message != HISTORICAL_INVARIANT or not any(re.fullmatch(exact_frame, line) for line in stack.splitlines()):
                raise ValueError("Historical native failure does not identify the retained-lifetime assertion")
            verified_failures.add(item.get("testName"))
    result_pairs = [(item.get("testId"), item.get("executionId")) for item in cases]
    entry_pairs = [(item.get("testId"), item.get("executionId")) for item in entries]
    definition_pairs = []
    for item in definitions:
        execution = single_element(item, "Execution")
        method = single_element(item, "TestMethod")
        if (method.get("className") != TEST_CLASS_FQN or method.get("name") not in CACHE_METHODS
                or TEST_CLASS_FQN + "." + method.get("name", "") != item.get("name")):
            raise ValueError("Native TRX method metadata disagrees with its frozen fact identity")
        definition_pairs.append((item.get("id"), execution.get("id")))
    for pairs in (result_pairs, entry_pairs, definition_pairs):
        if (any(not all(pair) for pair in pairs) or len(set(pairs)) != expected
                or len({pair[0] for pair in pairs}) != expected
                or len({pair[1] for pair in pairs}) != expected):
            raise ValueError("Native TRX contains missing or duplicate test/execution identities")
    if set(result_pairs) != set(entry_pairs) or set(result_pairs) != set(definition_pairs):
        raise ValueError("Native TRX result, definition and entry identities are not bijective")
    definition_names = {item.get("id"): item.get("name") for item in definitions}
    if any(definition_names[item.get("testId")] != item.get("testName") for item in cases):
        raise ValueError("Native TRX definition names disagree with their original results")
    # xUnit reports each real failed fact a second time as an Error RunInfo.
    # Admit only its exact adapter announcement, joined to the fully verified
    # failed result above. This does not admit an unrelated test-host error.
    announcements = []
    adapter_pattern = (r"\[xUnit\.net [0-9]{2}:[0-5][0-9]:[0-5][0-9]\.[0-9]{2}\]     ("
                       + re.escape(TEST_CLASS_FQN) + r"\.[A-Za-z][A-Za-z0-9_]*) \[FAIL\]")
    for container in run_infos:
        for item in container:
            if item.tag != TRX_NAMESPACE + "RunInfo":
                raise ValueError("Native TRX contains failed or unsupported run-level result information")
            if item.get("outcome") in ("Passed", "Completed", "Warning", "Information"):
                continue
            if not historical or item.get("outcome") != "Error":
                raise ValueError("Native TRX contains failed or unsupported run-level result information")
            children = list(item)
            if (len(children) != 1 or children[0].tag != TRX_NAMESPACE + "Text"
                    or children[0].attrib or list(children[0])
                    or (item.text or "").strip() or (children[0].tail or "").strip()):
                raise ValueError("Native TRX contains failed or unsupported run-level result information")
            match = re.fullmatch(adapter_pattern, children[0].text or "")
            if match is None or match[1] not in verified_failures:
                raise ValueError("Native TRX contains failed or unsupported run-level result information")
            announcements.append(match[1])
    if len(announcements) != len(verified_failures) or set(announcements) != verified_failures:
        raise ValueError("Historical adapter failure announcements must match the two verified failures exactly")
    return sorted(names)


def compiled_inventory(discovery: str, *, historical: bool = False) -> list[str]:
    # Remove only the CLI's four-space presentation prefix; preserve each raw FQN.
    # Foreign namespaces must also be counted so an unexpected test cannot disappear.
    names = [line[4:] for line in discovery.splitlines() if line.startswith("    ") and line[4:]]
    identities = HISTORICAL_IDENTITIES if historical else EXPECTED_IDENTITIES
    if len(names) != len(identities) or len(set(names)) != len(identities) or set(names) != identities:
        raise ValueError("Compiled discovery does not match the exact twenty-four-fact candidate roster")
    return sorted(names)


def local_git_environment() -> dict[str, str]:
    return {key: value for key, value in os.environ.items() if not key.upper().startswith("GIT_")}


def command(args: list[str], cwd: Path, log: Path | None = None, *, preserve_output: bool = False, expected_exit: int = 0) -> str:
    completed = subprocess.run(args, cwd=cwd, capture_output=True, text=True, encoding="utf-8",
                               errors="replace", check=False,
                               env=local_git_environment() if args[0] == "git" else None)
    if log is not None:
        log.write_text(completed.stdout + completed.stderr, encoding="utf-8")
    if completed.returncode != expected_exit:
        raise RuntimeError("Candidate command failed: " + args[0] + "; inspect retained log")
    return completed.stdout if preserve_output else completed.stdout.strip()


def git(cwd: Path, *args: str) -> str:
    return command(["git", "-c", "core.autocrlf=false", *args], cwd)


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify_working_tree(worktree: Path, raw: bytes, source_blob: str, *, product_head: str | None = None,
                        allow_generated: bool = False) -> None:
    # The index identity is insufficient: inspect actual compiler input bytes.
    output = worktree / DESTINATION
    if output.is_symlink() or not output.is_file() or output.read_bytes() != raw:
        raise ValueError("Composed working-tree Test source changed from the committed blob")
    if git(worktree, "rev-parse", "HEAD") != (product_head or PRODUCT_HEAD):
        raise ValueError("Composed Product HEAD changed")
    if git(worktree, "rev-parse", ":" + DESTINATION) != source_blob:
        raise ValueError("Composed candidate index identity changed")
    if git(worktree, "ls-files", "--stage", "--", DESTINATION) != "100644 " + source_blob + " 0\t" + DESTINATION:
        raise ValueError("Composed candidate index mode or stage changed")
    staged = git(worktree, "diff", "--cached", "--name-only", "HEAD", "--").splitlines()
    unstaged = git(worktree, "diff", "--name-only", "--").splitlines()
    if staged != [DESTINATION] or unstaged:
        raise ValueError("Tracked Product source changed during independent composition")
    # Git ignore rules are not compiler-input exclusions. Only the actual fixed
    # project graph's root SDK outputs may appear after restore/build begins.
    untracked_raw = command(["git", "-c", "core.autocrlf=false", "ls-files", "--others", "-z"],
                            worktree, preserve_output=True)
    untracked = [name for name in untracked_raw.split("\0") if name]
    if any(not (allow_generated and name.startswith(GENERATED_OUTPUT_PREFIXES)) for name in untracked):
        raise ValueError("Untracked Product source appeared during independent composition")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--product-head", required=True)
    parser.add_argument("--worktree", required=True, type=Path)
    parser.add_argument("--evidence", required=True, type=Path)
    parser.add_argument("--historical", action="store_true", help="Require the frozen two native failures and one positive control at the original vulnerable Product")
    arguments = parser.parse_args()
    if not re.fullmatch(r"[0-9a-f]{40}", arguments.product_head):
        raise ValueError("Full immutable Product SHA required")
    product_tree = selected_product(arguments.product_head, arguments.historical)
    identities = HISTORICAL_IDENTITIES if arguments.historical else EXPECTED_IDENTITIES
    selection = "|".join("FullyQualifiedName=" + name for name in sorted(identities))
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
    if git(ROOT, "rev-parse", arguments.product_head + "^{tree}") != product_tree:
        raise ValueError("Immutable Product tree mismatch")
    evidence.mkdir(parents=True)
    receipt: dict[str, object] = {
        "testHead": test_head,
        "productHead": arguments.product_head,
        "executionScope": "SYNTHETIC_PRIVATE_LOOKUP_HISTORICAL_LIFETIME_ONLY" if arguments.historical else EXECUTION_SCOPE,
        "expectedPrivateLookupFacts": len(identities),
        "expectedIdentities": sorted(identities),
        "expectedFailureIdentities": sorted(HISTORICAL_FAILURES) if arguments.historical else [],
        "historicalReplay": arguments.historical,
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
        (evidence / "candidate-source.cs").write_bytes(raw)
        source_blob = git(ROOT, "rev-parse", test_head + ":" + SOURCE)
        git(worktree, "add", "--", DESTINATION)
        verify_working_tree(worktree, raw, source_blob, product_head=arguments.product_head)
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
        # Restore must not change the actual compiler input or any tracked Product source.
        verify_working_tree(worktree, raw, source_blob, product_head=arguments.product_head, allow_generated=True)
        command(["dotnet", "test", project, "-c", "Release", "--no-restore", "-warnaserror",
                 "--filter", selection,
                 "--logger", "trx;LogFileName=candidate.trx",
                 "--results-directory", str(evidence)], worktree, evidence / "xunit.log", expected_exit=1 if arguments.historical else 0)
        verify_working_tree(worktree, raw, source_blob, product_head=arguments.product_head, allow_generated=True)
        names = native_inventory(evidence / "candidate.trx", historical=arguments.historical)
        discovery = command(["dotnet", "test", project, "-c", "Release", "--no-build",
                             "--list-tests", "--filter", selection],
                            worktree, evidence / "compiled-discovery.log", preserve_output=True)
        if compiled_inventory(discovery, historical=arguments.historical) != names:
            raise ValueError("Original native TRX and compiled discovery identities differ")
        verify_working_tree(worktree, raw, source_blob, product_head=arguments.product_head, allow_generated=True)
        receipt["nativeXunit"] = {"total": len(identities), "passed": 1 if arguments.historical else EXPECTED,
                                   "failed": len(HISTORICAL_FAILURES) if arguments.historical else 0, "skipped": 0,
                                   "privateLookupFacts": len(identities), "identities": names}
        receipt["status"] = "HISTORICAL_PRIVATE_LOOKUP_LIFETIME_FAILURES_OBSERVED" if arguments.historical else "SYNTHETIC_PRIVATE_LOOKUP_PASSED"
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
