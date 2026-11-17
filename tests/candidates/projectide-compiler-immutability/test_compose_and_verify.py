#!/usr/bin/env python3
"""Regression controls for admission only; controlled dotnet output is never native evidence."""
from __future__ import annotations

from contextlib import redirect_stdout
import copy
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import uuid
import xml.etree.ElementTree as ET

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("immutability_candidate_runner", HERE / "compose_and_verify.py")
assert SPEC is not None and SPEC.loader is not None
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)
NAMESPACE = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
SOURCE_BYTES = (HERE / "SecurityCompilerImmutableBoundaryCandidateTests.cs").read_bytes()


def native_fixture() -> ET.Element:
    root = ET.Element(NAMESPACE + "TestRun")
    summary = ET.SubElement(root, NAMESPACE + "ResultSummary", outcome="Completed")
    ET.SubElement(summary, NAMESPACE + "Counters", total="9", executed="9", passed="9", failed="0",
                  notExecuted="0", error="0", timeout="0", aborted="0")
    results = ET.SubElement(root, NAMESPACE + "Results")
    definitions = ET.SubElement(root, NAMESPACE + "TestDefinitions")
    entries = ET.SubElement(root, NAMESPACE + "TestEntries")
    for number, method in enumerate(RUNNER.EXPECTED_METHODS):
        name = RUNNER.TEST_CLASS_FQN + "." + method
        test_id, execution_id = str(uuid.UUID(int=number + 1)), str(uuid.UUID(int=number + 100))
        ET.SubElement(results, NAMESPACE + "UnitTestResult", testId=test_id, executionId=execution_id,
                      testName=name, outcome="Passed")
        definition = ET.SubElement(definitions, NAMESPACE + "UnitTest", id=test_id, name=name)
        ET.SubElement(definition, NAMESPACE + "Execution", id=execution_id)
        ET.SubElement(definition, NAMESPACE + "TestMethod", className=RUNNER.TEST_CLASS_FQN, name=method)
        ET.SubElement(entries, NAMESPACE + "TestEntry", testId=test_id, executionId=execution_id)
    return root


def discovery_fixture() -> str:
    return "The following Tests are available:\n" + "".join(
        "    " + name + "\n" for name in sorted(RUNNER.EXPECTED_IDENTITIES))


class NativeInventoryTests(unittest.TestCase):
    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="compiler-inventory-")
        self.addCleanup(temporary.cleanup)
        self.path = Path(temporary.name) / "candidate.trx"

    def admit(self, root: ET.Element) -> list[str]:
        self.path.write_bytes(ET.tostring(root))
        return RUNNER.native_inventory(self.path)

    def test_original_source_and_nine_fact_identities_are_frozen(self) -> None:
        blob = hashlib.sha1(b"blob " + str(len(SOURCE_BYTES)).encode() + b"\0" + SOURCE_BYTES).hexdigest()
        self.assertEqual("067043361a2e7a83613886234e1afc0b499d70e9", blob)
        methods = re.findall(r"\[Fact\]\s+public (?:async )?(?:Task|void) (\w+)\(", SOURCE_BYTES.decode())
        self.assertEqual(list(RUNNER.EXPECTED_METHODS), methods)
        self.assertEqual(9, len(set(methods)))

    def test_complete_original_native_inventory_is_admitted(self) -> None:
        self.assertEqual(sorted(RUNNER.EXPECTED_IDENTITIES), self.admit(native_fixture()))

    def test_missing_duplicate_and_substituted_results_are_rejected(self) -> None:
        for mode in ("missing", "duplicate", "substituted", "failed", "not-executed"):
            with self.subTest(mode=mode):
                root = native_fixture()
                results = root.find(NAMESPACE + "Results")
                if mode == "missing":
                    results.remove(results[0])
                elif mode == "duplicate":
                    results[0].set("testName", results[1].get("testName"))
                elif mode == "substituted":
                    results[0].set("testName", RUNNER.TEST_CLASS_FQN + ".DifferentFact")
                else:
                    results[0].set("outcome", "Failed" if mode == "failed" else "NotExecuted")
                with self.assertRaises(ValueError):
                    self.admit(root)

    def test_duplicate_inventory_containers_and_counter_sets_are_rejected(self) -> None:
        for name in ("ResultSummary", "Results", "TestDefinitions", "TestEntries", "Counters"):
            with self.subTest(container=name):
                root = native_fixture()
                parent = root.find(NAMESPACE + "ResultSummary") if name == "Counters" else root
                duplicate = copy.deepcopy(parent.find(NAMESPACE + name))
                if name == "ResultSummary":
                    duplicate.set("outcome", "Failed")
                elif name in ("Results", "TestDefinitions", "TestEntries"):
                    # Keep the original nine records intact: count checks alone miss this container.
                    duplicate.clear()
                parent.append(duplicate)
                with self.assertRaises(ValueError):
                    self.admit(root)

    def test_missing_containers_and_unsupported_inventory_children_are_rejected(self) -> None:
        for container in ("Results", "TestDefinitions", "TestEntries"):
            for mode in ("missing", "unsupported"):
                with self.subTest(container=container, mode=mode):
                    root = native_fixture()
                    node = root.find(NAMESPACE + container)
                    if mode == "missing":
                        root.remove(node)
                    else:
                        ET.SubElement(node, NAMESPACE + "UnexpectedRecord")
                    with self.assertRaises(ValueError):
                        self.admit(root)

    def test_method_metadata_must_bind_exact_class_and_bare_method(self) -> None:
        for mode in ("missing", "duplicate", "wrong-class", "wrong-method", "split-fqn"):
            with self.subTest(mode=mode):
                root = native_fixture()
                definition = root.find(NAMESPACE + "TestDefinitions")[0]
                method = definition.find(NAMESPACE + "TestMethod")
                if mode == "missing":
                    definition.remove(method)
                elif mode == "duplicate":
                    definition.append(copy.deepcopy(method))
                elif mode == "wrong-class":
                    method.set("className", "Foreign.Namespace.Type")
                elif mode == "wrong-method":
                    method.set("name", "DifferentFact")
                else:
                    method.set("className", "Coglatas.Tests.ProjectIde")
                    method.set("name", RUNNER.TEST_CLASS + "." + RUNNER.EXPECTED_METHODS[0])
                with self.assertRaises(ValueError):
                    self.admit(root)

    def test_native_identities_must_be_present_unique_and_bijective(self) -> None:
        for mode in ("missing-test-id", "duplicate-test-id", "duplicate-execution-id", "wrong-entry",
                     "missing-execution", "duplicate-execution", "wrong-definition-name"):
            with self.subTest(mode=mode):
                root = native_fixture()
                results = root.find(NAMESPACE + "Results")
                definition = root.find(NAMESPACE + "TestDefinitions")[0]
                execution = definition.find(NAMESPACE + "Execution")
                if mode == "missing-test-id":
                    del results[0].attrib["testId"]
                elif mode == "duplicate-test-id":
                    results[0].set("testId", results[1].get("testId"))
                elif mode == "duplicate-execution-id":
                    results[0].set("executionId", results[1].get("executionId"))
                elif mode == "wrong-entry":
                    root.find(NAMESPACE + "TestEntries")[0].set("executionId", str(uuid.UUID(int=999)))
                elif mode == "missing-execution":
                    definition.remove(execution)
                elif mode == "duplicate-execution":
                    definition.append(copy.deepcopy(execution))
                else:
                    definition.set("name", "UnexpectedDefinition")
                with self.assertRaises(ValueError):
                    self.admit(root)

    def test_incomplete_or_nonzero_native_counters_are_rejected(self) -> None:
        for counter in ("total", "executed", "passed", "failed", "notExecuted", "error", "aborted"):
            with self.subTest(counter=counter):
                root = native_fixture()
                root.find(NAMESPACE + "ResultSummary").find(NAMESPACE + "Counters").set(counter, "1")
                with self.assertRaises(ValueError):
                    self.admit(root)

    def test_malformed_xml_and_foreign_namespaces_are_rejected(self) -> None:
        self.path.write_text("<broken")
        with self.assertRaises(ET.ParseError):
            RUNNER.native_inventory(self.path)
        root = native_fixture()
        root.tag = "TestRun"
        with self.assertRaises(ValueError):
            self.admit(root)

    def test_discovery_requires_exact_raw_fqns_on_both_line_endings(self) -> None:
        expected = sorted(RUNNER.EXPECTED_IDENTITIES)
        self.assertEqual(expected, RUNNER.compiled_inventory(discovery_fixture()))
        self.assertEqual(expected, RUNNER.compiled_inventory(discovery_fixture().replace("\n", "\r\n")))
        variants = (
            discovery_fixture() + "    Foreign.Namespace.ExtraFact\n",
            discovery_fixture().replace(expected[0], expected[1]),
            discovery_fixture().replace("    " + expected[0] + "\n", ""),
            discovery_fixture().replace(expected[0], RUNNER.EXPECTED_METHODS[0]),
            discovery_fixture().rstrip("\n") + " ",
        )
        for discovery in variants:
            with self.subTest(discovery=variants.index(discovery)):
                with self.assertRaises(ValueError):
                    RUNNER.compiled_inventory(discovery)

    def test_command_preserves_discovery_bytes_in_return_and_log(self) -> None:
        text = discovery_fixture().rstrip("\n") + " "
        log = self.path.with_suffix(".log")
        actual = RUNNER.command([sys.executable, "-c", "import sys; sys.stdout.write(" + repr(text) + ")"],
                                self.path.parent, log, preserve_output=True)
        self.assertEqual(text, actual)
        self.assertEqual(text, log.read_text())


class MainAdmissionTests(unittest.TestCase):
    """Run actual main with real Git/worktrees; only the dotnet process is controlled."""

    @classmethod
    def setUpClass(cls) -> None:
        cls.temporary = tempfile.TemporaryDirectory(prefix="compiler-main-")
        cls.addClassCleanup(cls.temporary.cleanup)
        cls.root = Path(cls.temporary.name)
        cls.repository = cls.root / "security"
        cls.repository.mkdir()
        cls.real_command = RUNNER.command
        cls.git("init", "--quiet")
        cls.git("config", "user.name", "Qualification fixture")
        cls.git("config", "user.email", "qualification-fixture@example.invalid")
        cls.git("config", "commit.gpgsign", "false")
        (cls.repository / "product.txt").write_text("Immutable Product fixture\n")
        cls.git("add", "--", "product.txt")
        cls.git("commit", "--quiet", "-m", "Product fixture")
        cls.product_head = cls.git("rev-parse", "HEAD")
        source = cls.repository / RUNNER.SOURCE
        source.parent.mkdir(parents=True)
        source.write_bytes(SOURCE_BYTES)
        cls.git("add", "--", RUNNER.SOURCE)
        cls.git("commit", "--quiet", "-m", "Independent Test fixture")

    @classmethod
    def git(cls, *args: str) -> str:
        result = subprocess.run(["git", "-c", "core.autocrlf=false", *args], cwd=cls.repository,
                                capture_output=True, text=True, check=True)
        return result.stdout.strip()

    def run_main(self, scenario: str) -> tuple[Exception | None, dict, list[str]]:
        with tempfile.TemporaryDirectory(prefix="execution-", dir=self.root) as temporary:
            worktree = Path(temporary) / "product"
            evidence = Path(temporary) / "evidence"
            calls = []
            def controlled_command(args, cwd, log=None, *, preserve_output=False):
                if args[0] != "dotnet":
                    return type(self).real_command(args, cwd, log, preserve_output=preserve_output)
                stage = "discovery" if "--list-tests" in args else args[1]
                calls.append(stage)
                output = "10.0.403\n" if stage == "--version" else ""
                if scenario == "tool-failure" and stage == "test":
                    raise RuntimeError("Controlled dotnet failure")
                if stage == "restore" and scenario == "source-before-compile":
                    (worktree / RUNNER.DESTINATION).write_bytes(SOURCE_BYTES + b"\n// changed compiler input\n")
                elif stage == "restore" and scenario == "product-before-compile":
                    (worktree / "product.txt").write_text("Changed Product fixture\n")
                if stage == "test":
                    trx = native_fixture()
                    if scenario == "substituted-roster":
                        for number, case in enumerate(trx.find(NAMESPACE + "Results")):
                            case.set("testName", RUNNER.TEST_CLASS_FQN + ".UNTRUSTED_CANARY" + str(number))
                    elif scenario == "second-failed-summary":
                        ET.SubElement(trx, NAMESPACE + "ResultSummary", outcome="Failed")
                    (evidence / "candidate.trx").write_bytes(ET.tostring(trx))
                    if scenario == "source-after-compile":
                        (worktree / RUNNER.DESTINATION).write_bytes(SOURCE_BYTES + b"\n// changed compiler input\n")
                    elif scenario == "product-drift":
                        (worktree / "product.txt").write_text("Changed Product fixture\n")
                    elif scenario == "index-drift":
                        type(self).real_command(["git", "rm", "--cached", RUNNER.DESTINATION], worktree)
                    elif scenario == "head-drift":
                        type(self).real_command(["git", "commit", "--quiet", "-m", "Controlled HEAD drift"], worktree)
                if stage == "discovery":
                    output = discovery_fixture()
                    if scenario == "foreign-discovery":
                        output += "    Foreign.Namespace.ExtraFact\n"
                    elif scenario == "discovery-whitespace":
                        output = output.rstrip("\n") + " "
                    elif scenario == "source-after-discovery":
                        (worktree / RUNNER.DESTINATION).write_bytes(SOURCE_BYTES + b"\n// changed compiler input\n")
                if log is not None:
                    log.write_text(output)
                return output if preserve_output else output.strip()
            error = None
            try:
                with patch.object(RUNNER, "ROOT", self.repository), patch.object(RUNNER, "command", controlled_command), \
                        patch.object(sys, "argv", ["compose_and_verify.py", "--product-head", self.product_head,
                                                  "--worktree", str(worktree), "--evidence", str(evidence)]), \
                        redirect_stdout(io.StringIO()):
                    try:
                        RUNNER.main()
                    except Exception as caught:
                        error = caught
                receipt = json.loads((evidence / "receipt.json").read_text())
                self.assertNotIn("UNTRUSTED_CANARY", receipt.get("error", ""))
                for flag in ("requiredCheckQualified", "independentReviewComplete", "nativeUiQualified",
                             "productionQualified", "trustQualified", "releaseQualified"):
                    self.assertIs(False, receipt[flag])
                return error, receipt, calls
            finally:
                if worktree.exists():
                    self.git("worktree", "remove", "--force", str(worktree))

    def test_main_positive_control_uses_real_git_and_preserves_false_flags(self) -> None:
        error, receipt, calls = self.run_main("valid")
        self.assertIsNone(error)
        self.assertEqual("SYNTHETIC_IMMUTABILITY_PASSED", receipt["status"])
        self.assertEqual(sorted(RUNNER.EXPECTED_IDENTITIES), receipt["nativeXunit"]["identities"])
        self.assertEqual(["--version", "restore", "test", "discovery"], calls)

    def test_main_refuses_demonstrated_false_pass_records(self) -> None:
        for scenario in ("substituted-roster", "second-failed-summary", "foreign-discovery", "discovery-whitespace"):
            with self.subTest(scenario=scenario):
                error, receipt, _ = self.run_main(scenario)
                self.assertIsInstance(error, ValueError)
                self.assertEqual("FAILED_OR_INCOMPLETE", receipt["status"])
                self.assertNotIn("nativeXunit", receipt)

    def test_main_rechecks_real_source_after_restore_before_compilation(self) -> None:
        for scenario in ("source-before-compile", "product-before-compile"):
            with self.subTest(scenario=scenario):
                error, receipt, calls = self.run_main(scenario)
                self.assertIsInstance(error, ValueError)
                self.assertEqual("FAILED_OR_INCOMPLETE", receipt["status"])
                self.assertEqual(["--version", "restore"], calls)

    def test_main_refuses_actual_source_and_tracked_product_drift_after_execution(self) -> None:
        for scenario in ("source-after-compile", "source-after-discovery", "product-drift", "index-drift", "head-drift"):
            with self.subTest(scenario=scenario):
                error, receipt, _ = self.run_main(scenario)
                self.assertIsNotNone(error)
                self.assertEqual("FAILED_OR_INCOMPLETE", receipt["status"])
                self.assertNotIn("nativeXunit", receipt)

    def test_main_preserves_native_command_failure(self) -> None:
        error, receipt, calls = self.run_main("tool-failure")
        self.assertIsInstance(error, RuntimeError)
        self.assertEqual("FAILED_OR_INCOMPLETE", receipt["status"])
        self.assertNotIn("discovery", calls)


if __name__ == "__main__":
    unittest.main()
