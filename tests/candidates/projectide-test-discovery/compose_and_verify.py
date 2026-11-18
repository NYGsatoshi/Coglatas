#!/usr/bin/env python3
"""Compose independent public Test discovery and real portable CLI assertions over immutable Product."""
from __future__ import annotations

import argparse
import base64
from datetime import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
SOURCE = "tests/candidates/projectide-test-discovery/SecurityTestDiscoveryCandidateTests.cs"
DESTINATION = "tests/Coglatas.Tests/ProjectIde/SecurityTestDiscoveryCandidateTests.cs"
TEST_CLASS = "SecurityTestDiscoveryCandidateTests"
TEST_CLASS_FQN = "Coglatas.Tests.ProjectIde." + TEST_CLASS
DISCOVERY_METHODS = (
    'ExactInventoryBindsInputAndPlanWhileKnownCounterexamplesStillFail',
    'SourceContextAndSettingsDriftCannotDiscloseAnEarlierRosterOrStamps',
    'EvaluatorMismatchRemainsAnIndependentAxisAndNeverExecutesAssertions',
    'EmptySuiteAndKnownEmptyAssertionListStayDistinctWithoutTestPass',
    'InvalidAndUnsupportedDefinitionsRetainUnknownCountsInsteadOfZero',
    'TestCountLimitRejectsTheWholeInventoryAndAnUnboundedPositiveRestoresIt',
    'AggregateAssertionLimitRejectsAllDefinitionsWithoutAPrefix',
    'BindingBudgetAndDeclarationBoundsPublishUnavailableWithNoPriorIdentities',
    'PartialSourceInventoryRetainsItsIncompleteBindingAndMissingExecutionEvidence',
    'CandidateLocationsPreserveProposalBaseAndCapturedTargetWithoutPromotingAuthority',
    'CancelledDiscoveryPublishesNothingAndLeavesSubsequentPositiveBytesUnchanged',
    'CallerNodesReturnedCollectionsAndDetachedBytesCannotRewriteRetainedDiscovery',
    'ConcurrentQueriesOwnStableSortedResultsWithoutChangingLaterExecution',
    'UnavailableForeignTargetsCannotConvertInventoryIntoAuthorizedExecution',
    'RealCliReadyInventoryBindsActualEvaluatorBinaryAndExactPlanWithoutTestPass',
    'RealCliCounterexampleDiscoveryExitsZeroWhileItsSeparateActualRunExitsOne',
    'RealCliZeroUnsupportedAndKnownEmptyAssertionsRemainDifferentNonExecutionResults',
    'RealCliMixedInvalidDefinitionAndEvaluatorMismatchKeepsInvalidExitThree',
    'RealCliValidEvaluatorMismatchKeepsExitTwoAndNeverClaimsMissingAssertionsExecuted',
    'RealCliSourceAndEnvelopeDriftFailBeforeDisclosingRosterOrBoundDigests',
    'RealCliCreateNewProtectsExistingOutputAndBothInputPathsByteForByte',
    'RealCliMalformedOversizedAndFutureSourceRefuseWithoutBoundInventory',
    'RealCliPartialBindingAndBudgetExhaustionCannotInventCompleteInventory',
    'RealCliArtifactByteBoundRefusesTheWholeProjectionWithoutTruncatingNames',
)
EXPECTED_IDENTITIES = frozenset(
    TEST_CLASS_FQN + "." + method
    for method in DISCOVERY_METHODS
)
EXPECTED = 24
TRX_NAMESPACE = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
PRODUCT_HEAD = "070ceb7f49e1e990a8395ea2e3a558a7185e1865"
PRODUCT_TREE = "c0f6cfbd7560cb2d4a3910aaafaffd4c04e36026"
EXECUTION_SCOPE = "SYNTHETIC_TEST_DISCOVERY_CORE_AND_CLI_ONLY"
MSBUILD_IMPORT_PROPERTIES = ("-p:ImportDirectoryBuildProps=false", "-p:ImportDirectoryBuildTargets=false")


def single_element(parent: ET.Element, name: str) -> ET.Element:
    elements = parent.findall("{*}" + name)
    if len(elements) != 1 or elements[0].tag != TRX_NAMESPACE + name:
        raise ValueError("Native TRX requires one " + name + " element")
    return elements[0]


def native_inventory(path: Path) -> list[str]:
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
    for container in run_infos:
        if any(item.tag != TRX_NAMESPACE + "RunInfo" or item.get("outcome") not in
               ("Passed", "Completed", "Warning", "Information") for item in container):
            raise ValueError("Native TRX contains failed or unsupported run-level result information")
    if (summary.get("outcome") != "Completed"
            or len(EXPECTED_IDENTITIES) != EXPECTED
            or any(counters.get(key) != str(EXPECTED) for key in ("total", "executed", "passed"))
            or any(counters.get(key) != "0" for key in ("failed", "notExecuted"))
            or any(value != "0" for key, value in counters.attrib.items()
                   if key not in ("total", "executed", "passed"))):
        raise ValueError("Native TRX must contain exactly twenty-four complete passing candidate facts")
    containers = []
    for container_name, child_name in (("Results", "UnitTestResult"),
                                       ("TestDefinitions", "UnitTest"), ("TestEntries", "TestEntry")):
        children = list(single_element(trx, container_name))
        if len(children) != EXPECTED or any(child.tag != TRX_NAMESPACE + child_name for child in children):
            raise ValueError("Native TRX contains missing or unsupported inventory records")
        containers.append(children)
    cases, definitions, entries = containers
    if trx.find(".//{*}InnerResults") is not None or trx.findall(".//{*}UnitTestResult") != cases:
        raise ValueError("Native TRX must contain only flat candidate results")
    if any(item.find(".//{*}ErrorInfo") is not None for item in cases):
        raise ValueError("Native TRX passing result contains error information")
    names = [item.get("testName", "") for item in cases]
    if (set(names) != EXPECTED_IDENTITIES or len(set(names)) != EXPECTED
            or any(item.get("outcome") != "Passed" for item in cases)):
        raise ValueError("Native TRX does not match the exact test-discovery fact roster")
    result_pairs = [(item.get("testId"), item.get("executionId")) for item in cases]
    entry_pairs = [(item.get("testId"), item.get("executionId")) for item in entries]
    definition_pairs = []
    for item in definitions:
        execution = single_element(item, "Execution")
        method = single_element(item, "TestMethod")
        if (method.get("className") != TEST_CLASS_FQN or method.get("name") not in DISCOVERY_METHODS
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
    # Remove only the CLI's four-space presentation prefix; preserve each raw FQN.
    # Foreign namespaces must also be counted so an unexpected test cannot disappear.
    names = [line[4:] for line in discovery.splitlines() if line.startswith("    ") and line[4:]]
    if len(names) != EXPECTED or len(set(names)) != EXPECTED or set(names) != EXPECTED_IDENTITIES:
        raise ValueError("Compiled discovery does not match the exact twenty-four-fact candidate roster")
    return sorted(names)


def local_git_environment() -> dict[str, str]:
    return {key: value for key, value in os.environ.items() if not key.upper().startswith("GIT_")}


def command(args: list[str], cwd: Path, log: Path | None = None, *, preserve_output: bool = False,
            environment: dict[str, str] | None = None) -> str:
    completed = subprocess.run(args, cwd=cwd, capture_output=True, text=True, encoding="utf-8",
                               errors="replace", check=False,
                               env=local_git_environment() if args[0] == "git" else environment)
    if log is not None:
        log.write_text(completed.stdout + completed.stderr, encoding="utf-8")
    if completed.returncode:
        raise RuntimeError("Candidate command failed: " + args[0] + "; inspect retained log")
    return completed.stdout if preserve_output else completed.stdout.strip()


def git(cwd: Path, *args: str) -> str:
    return command(["git", "--no-replace-objects", "-c", "core.autocrlf=false", *args], cwd)


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


SDK_PROJECT_ROOTS = (
    "src/Coglatas.Application", "src/Coglatas.Domain", "src/Coglatas.Infrastructure",
    "src/Coglatas.UI.Core", "src/Coglatas.Web", "tests/Coglatas.Tests",
    "tools/Coglatas.SecurityArchitecture", "tools/ProjectIde.Workbench.Synthetic",
    "tools/ProjectIde.Tests",
)


def verify_working_tree(worktree: Path, raw: bytes, source_blob: str, *, allow_generated: bool = False) -> None:
    # The index identity is insufficient: inspect actual compiler input bytes.
    output = worktree / DESTINATION
    if output.is_symlink() or not output.is_file() or output.read_bytes() != raw:
        raise ValueError("Composed working-tree Test source changed from the committed blob")
    if git(worktree, "rev-parse", "HEAD") != PRODUCT_HEAD:
        raise ValueError("Composed Product HEAD changed")
    if git(worktree, "rev-parse", ":" + DESTINATION) != source_blob:
        raise ValueError("Composed candidate index identity changed")
    if git(worktree, "ls-files", "--stage", "--", DESTINATION) != "100644 " + source_blob + " 0\t" + DESTINATION:
        raise ValueError("Composed candidate index mode or stage changed")
    # Preserve raw NUL records: path bytes may contain whitespace or newlines.
    # Lowercase status tags mark assume-unchanged; S marks skip-worktree.
    index_records = subprocess.run(
        ["git", "--no-replace-objects", "ls-files", "-v", "-z"], cwd=worktree,
        capture_output=True, check=True, env=local_git_environment()).stdout.split(b"\0")
    if any(record[:1].islower() or record[:1] == b"S" for record in index_records):
        raise ValueError("Composed Git index flags can hide tracked working-tree edits")
    changed = git(worktree, "diff", "--cached", "--name-only", "HEAD", "--").splitlines()
    if changed != [DESTINATION] or git(worktree, "diff", "--name-only", "--"):
        raise ValueError("Tracked Product source changed during independent composition")
    # Ignored C# files still enter SDK compile globs. Only the frozen test
    # project's actual reference graph may produce bin/obj after restore.
    generated = tuple(root + "/" + output + "/" for root in SDK_PROJECT_ROOTS
                      for output in ("bin", "obj")) if allow_generated else ()
    untracked = command(["git", "--no-replace-objects", "ls-files", "--others", "-z"], worktree,
                        preserve_output=True).split("\0")
    if any(path and not path.startswith(generated) for path in untracked):
        raise ValueError("Unexpected untracked source appeared during independent composition")



# Each entry is one real child process, not another xUnit fact. The final native
# fixture has fourteen Core facts and ten CLI facts; these calls stay separate.
PROCESS_CASES = {
    ("RealCliReadyInventoryBindsActualEvaluatorBinaryAndExactPlanWithoutTestPass/default", "evaluator"): 0,
    ("RealCliReadyInventoryBindsActualEvaluatorBinaryAndExactPlanWithoutTestPass/default", "ready"): 0,
    ("RealCliCounterexampleDiscoveryExitsZeroWhileItsSeparateActualRunExitsOne/default", "evaluator"): 0,
    ("RealCliCounterexampleDiscoveryExitsZeroWhileItsSeparateActualRunExitsOne/default", "counterexample-discovery"): 0,
    ("RealCliCounterexampleDiscoveryExitsZeroWhileItsSeparateActualRunExitsOne/default", "counterexample-execution"): 1,
    ("RealCliZeroUnsupportedAndKnownEmptyAssertionsRemainDifferentNonExecutionResults/zero", "evaluator"): 0,
    ("RealCliZeroUnsupportedAndKnownEmptyAssertionsRemainDifferentNonExecutionResults/zero", "zero"): 2,
    ("RealCliZeroUnsupportedAndKnownEmptyAssertionsRemainDifferentNonExecutionResults/unsupported", "evaluator"): 0,
    ("RealCliZeroUnsupportedAndKnownEmptyAssertionsRemainDifferentNonExecutionResults/unsupported", "unsupported"): 2,
    ("RealCliZeroUnsupportedAndKnownEmptyAssertionsRemainDifferentNonExecutionResults/empty-assertions", "evaluator"): 0,
    ("RealCliZeroUnsupportedAndKnownEmptyAssertionsRemainDifferentNonExecutionResults/empty-assertions", "empty-assertions"): 0,
    ("RealCliMixedInvalidDefinitionAndEvaluatorMismatchKeepsInvalidExitThree/default", "evaluator"): 0,
    ("RealCliMixedInvalidDefinitionAndEvaluatorMismatchKeepsInvalidExitThree/default", "invalid-and-mismatched-evaluator"): 3,
    ("RealCliValidEvaluatorMismatchKeepsExitTwoAndNeverClaimsMissingAssertionsExecuted/default", "evaluator"): 0,
    ("RealCliValidEvaluatorMismatchKeepsExitTwoAndNeverClaimsMissingAssertionsExecuted/default", "mismatched-evaluator"): 2,
    ("RealCliSourceAndEnvelopeDriftFailBeforeDisclosingRosterOrBoundDigests/source", "evaluator"): 0,
    ("RealCliSourceAndEnvelopeDriftFailBeforeDisclosingRosterOrBoundDigests/source", "source-drift"): 3,
    ("RealCliSourceAndEnvelopeDriftFailBeforeDisclosingRosterOrBoundDigests/envelope", "evaluator"): 0,
    ("RealCliSourceAndEnvelopeDriftFailBeforeDisclosingRosterOrBoundDigests/envelope", "envelope-drift"): 3,
    ("RealCliCreateNewProtectsExistingOutputAndBothInputPathsByteForByte/default", "evaluator"): 0,
    ("RealCliCreateNewProtectsExistingOutputAndBothInputPathsByteForByte/default", "existing-inventory.json"): 3,
    ("RealCliCreateNewProtectsExistingOutputAndBothInputPathsByteForByte/default", "existing-source.json"): 3,
    ("RealCliCreateNewProtectsExistingOutputAndBothInputPathsByteForByte/default", "existing-input.json"): 3,
    ("RealCliMalformedOversizedAndFutureSourceRefuseWithoutBoundInventory/malformed", "evaluator"): 0,
    ("RealCliMalformedOversizedAndFutureSourceRefuseWithoutBoundInventory/malformed", "malformed"): 3,
    ("RealCliMalformedOversizedAndFutureSourceRefuseWithoutBoundInventory/oversized", "evaluator"): 0,
    ("RealCliMalformedOversizedAndFutureSourceRefuseWithoutBoundInventory/oversized", "oversized"): 3,
    ("RealCliMalformedOversizedAndFutureSourceRefuseWithoutBoundInventory/future", "evaluator"): 0,
    ("RealCliMalformedOversizedAndFutureSourceRefuseWithoutBoundInventory/future", "future"): 2,
    ("RealCliPartialBindingAndBudgetExhaustionCannotInventCompleteInventory/partial", "evaluator"): 0,
    ("RealCliPartialBindingAndBudgetExhaustionCannotInventCompleteInventory/partial", "partial"): 2,
    ("RealCliPartialBindingAndBudgetExhaustionCannotInventCompleteInventory/budget", "evaluator"): 0,
    ("RealCliPartialBindingAndBudgetExhaustionCannotInventCompleteInventory/budget", "budget"): 2,
    ("RealCliArtifactByteBoundRefusesTheWholeProjectionWithoutTruncatingNames/default", "evaluator"): 0,
    ("RealCliArtifactByteBoundRefusesTheWholeProjectionWithoutTruncatingNames/default", "artifact-byte-bound"): 2,
}


def unique_json(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate JSON field in actual process evidence")
        result[key] = value
    return result


def process_inventory(path: Path, cli_sha256: str, domain_sha256: str, evaluator_digest: str) -> list[str]:
    rows = [json.loads(line, object_pairs_hook=unique_json) for line in path.read_text(encoding="utf-8").splitlines()]
    keys = [(row.get("caseId"), row.get("invocation")) for row in rows]
    if len(rows) != 35 or len(set(keys)) != 35 or set(keys) != set(PROCESS_CASES):
        raise ValueError("Actual CLI evidence must match the exact thirty-five-process roster")
    for row, key in zip(rows, keys, strict=True):
        if (row.get("executable") != "dotnet" or row.get("cliPath") != "tools/ProjectIde.Tests/bin/Release/net10.0/ProjectIde.Tests.dll"
                or row.get("ExitCode") != PROCESS_CASES[key] or type(row.get("ExitCode")) is not int
                or type(row.get("processId")) is not int or row["processId"] < 1):
            raise ValueError("Actual CLI invocation identity or exit result is invalid")
        start, end = datetime.fromisoformat(row["startedUtc"]), datetime.fromisoformat(row["completedUtc"])
        if start.tzinfo is None or end.tzinfo is None or end < start or (end - start).total_seconds() > 65:
            raise ValueError("Actual CLI execution timestamps are invalid")
        for prefix, expected in (("cli", cli_sha256), ("domain", domain_sha256)):
            if (not re.fullmatch(r"[0-9a-f]{64}", expected) or row.get(prefix + "Sha256Before") != expected
                    or row.get(prefix + "Sha256After") != expected):
                raise ValueError("Actual CLI evidence differs from the built binary identities")
        evaluator = key[1] == "evaluator"
        existing = key[1].startswith("existing-")
        output_name = None if evaluator else key[1].removeprefix("existing-") if existing else "run.json" if key[1] == "counterexample-execution" else "inventory.json"
        expected_args = ["--evaluator"] if evaluator else ["source.json", "input.json", output_name]
        if not evaluator and key[1] != "counterexample-execution":
            expected_args.insert(0, "--discover")
        if row.get("arguments") != expected_args or row.get("outputName") != output_name:
            raise ValueError("Actual CLI arguments do not match the intended process boundary")
        inputs = row.get("inputs")
        if (not isinstance(inputs, list)
                or sorted(item.get("name", "") for item in inputs) != ([] if evaluator else ["input.json", "source.json"])):
            raise ValueError("Actual CLI input inventory is invalid")
        for item in inputs:
            if (type(item.get("length")) is not int or item["length"] <= 0
                    or not re.fullmatch(r"[0-9a-f]{64}", item.get("sha256Before", ""))
                    or item.get("sha256After") != item["sha256Before"]):
                raise ValueError("Actual CLI changed its retained input bytes")
        if evaluator:
            if any(row.get(field) is not None for field in ("outputName", "outputSha256Before", "outputSha256After", "outputBytes")) or row.get("outputExisted") is not False:
                raise ValueError("Evaluator inspection unexpectedly published a file")
            identity = json.loads(row["Stdout"], object_pairs_hook=unique_json)
            if (identity.get("artifactId") != "coglatas.local-project-test-evaluator"
                    or identity.get("digest") != {"algorithm": "sha-256", "value": evaluator_digest}
                    or row.get("Stderr") != ""):
                raise ValueError("Actual evaluator identity is not bound to the compiled Domain binary")
            continue
        raw = base64.b64decode(row["outputBytes"], validate=True)
        if hashlib.sha256(raw).hexdigest() != row.get("outputSha256After"):
            raise ValueError("Actual CLI output bytes disagree with their retained digest")
        if existing:
            if row.get("outputExisted") is not True or row.get("outputSha256Before") != row["outputSha256After"]:
                raise ValueError("Actual CLI overwrote an existing output or input")
            if output_name in ("source.json", "input.json"):
                matching_input = next(item for item in inputs if item["name"] == output_name)
                if (row["outputSha256Before"] != matching_input["sha256Before"]
                        or row["outputSha256After"] != matching_input["sha256After"]):
                    raise ValueError("Actual CLI input-alias output differs from its matching input receipt")
        elif row.get("outputExisted") is not False or row.get("outputSha256Before") is not None:
            raise ValueError("Fresh CLI output is not proven to have used CreateNew")
    return sorted(case + ":" + invocation for case, invocation in keys)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--product-head", required=True)
    parser.add_argument("--worktree", required=True, type=Path)
    parser.add_argument("--evidence", required=True, type=Path)
    arguments = parser.parse_args()
    if not re.fullmatch(r"[0-9a-f]{40}", arguments.product_head):
        raise ValueError("Full immutable Product SHA required")
    if arguments.product_head != PRODUCT_HEAD:
        raise ValueError("Only the independently reviewed immutable Product commit is admitted")
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
    if git(ROOT, "rev-parse", arguments.product_head + "^{tree}") != PRODUCT_TREE:
        raise ValueError("Immutable Product tree mismatch")
    evidence.mkdir(parents=True)
    receipt: dict[str, object] = {
        "testHead": test_head,
        "productHead": arguments.product_head,
        "executionScope": EXECUTION_SCOPE,
        "expectedDiscoveryFacts": len(DISCOVERY_METHODS),
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
        command(["git", "--no-replace-objects", "-c", "core.autocrlf=false", "worktree", "add", "--detach",
                 str(worktree), arguments.product_head], ROOT, evidence / "worktree.log")
        output = worktree / DESTINATION
        if output.exists():
            raise ValueError("Product already contains candidate destination; refuse overwrite")
        raw = subprocess.run(["git", "--no-replace-objects", "show", test_head + ":" + SOURCE], cwd=ROOT,
                             check=True, capture_output=True, env=local_git_environment()).stdout
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_bytes(raw)
        (evidence / "candidate-source.cs").write_bytes(raw)
        source_blob = git(ROOT, "rev-parse", test_head + ":" + SOURCE)
        git(worktree, "add", "--", DESTINATION)
        verify_working_tree(worktree, raw, source_blob)
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
        command(["dotnet", "restore", project, "--nologo", *MSBUILD_IMPORT_PROPERTIES],
                worktree, evidence / "restore.log")
        # Restore must not change the actual compiler input or any tracked Product source.
        verify_working_tree(worktree, raw, source_blob, allow_generated=True)
        command(["dotnet", "build", project, "-c", "Release", "--no-restore", "-warnaserror", "--nologo",
                 *MSBUILD_IMPORT_PROPERTIES],
                worktree, evidence / "build.log")
        verify_working_tree(worktree, raw, source_blob, allow_generated=True)
        binary_paths = {
            "cli": worktree / "tools/ProjectIde.Tests/bin/Release/net10.0/ProjectIde.Tests.dll",
            "domain": worktree / "tools/ProjectIde.Tests/bin/Release/net10.0/Coglatas.Domain.dll",
            "runtimeconfig": worktree / "tools/ProjectIde.Tests/bin/Release/net10.0/ProjectIde.Tests.runtimeconfig.json",
            "deps": worktree / "tools/ProjectIde.Tests/bin/Release/net10.0/ProjectIde.Tests.deps.json",
            "testAssembly": worktree / "tests/Coglatas.Tests/bin/Release/net10.0/Coglatas.Tests.dll",
        }
        binary_before = {name: sha256(path) for name, path in binary_paths.items()}
        domain_evaluator = hashlib.sha256(b"coglatas.project-test-evaluator-binary/1\0" + binary_paths["domain"].read_bytes()).hexdigest()
        process_evidence = evidence / "cli-processes.jsonl"
        process_evidence.touch(exist_ok=False)
        environment = {**os.environ, "COGLATAS_DISCOVERY_PROCESS_EVIDENCE": str(process_evidence)}
        command(["dotnet", "test", project, "-c", "Release", "--no-restore", "--no-build", "-warnaserror",
                 *MSBUILD_IMPORT_PROPERTIES,
                 "--filter", "FullyQualifiedName~" + TEST_CLASS,
                 "--logger", "trx;LogFileName=candidate.trx",
                 "--results-directory", str(evidence)], worktree, evidence / "xunit.log", environment=environment)
        binary_after = {name: sha256(path) for name, path in binary_paths.items()}
        if binary_before != binary_after:
            raise ValueError("Built CLI or Test binary changed during actual process execution")
        processes = process_inventory(process_evidence, binary_before["cli"], binary_before["domain"], domain_evaluator)
        (evidence / "binary-binding.json").write_text(json.dumps({
            "sha256Before": binary_before, "sha256After": binary_after,
            "domainEvaluatorDigest": domain_evaluator,
            "paths": {name: path.relative_to(worktree).as_posix() for name, path in binary_paths.items()},
            "processes": processes,
        }, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        verify_working_tree(worktree, raw, source_blob, allow_generated=True)
        names = native_inventory(evidence / "candidate.trx")
        discovery = command(["dotnet", "test", project, "-c", "Release", "--no-build",
                             "--list-tests", "--filter", "FullyQualifiedName~" + TEST_CLASS, *MSBUILD_IMPORT_PROPERTIES],
                            worktree, evidence / "compiled-discovery.log", preserve_output=True)
        if compiled_inventory(discovery) != names:
            raise ValueError("Original native TRX and compiled discovery identities differ")
        verify_working_tree(worktree, raw, source_blob, allow_generated=True)
        receipt["nativeXunit"] = {"total": EXPECTED, "passed": EXPECTED,
                                   "failed": 0, "skipped": 0,
                                   "discoveryFacts": len(DISCOVERY_METHODS), "identities": names}
        receipt["actualCliProcesses"] = {"total": len(processes), "identities": processes}
        receipt["status"] = "SYNTHETIC_TEST_DISCOVERY_PASSED"
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
