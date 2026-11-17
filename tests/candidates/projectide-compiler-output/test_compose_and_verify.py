#!/usr/bin/env python3
"""Admission regressions over retained native inputs; controlled commands are not native execution."""
from __future__ import annotations

import argparse
from contextlib import redirect_stdout
import copy
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("compiler_output_candidate_runner", HERE / "compose_and_verify.py")
assert SPEC is not None and SPEC.loader is not None
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)
NAMESPACE = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
SOURCE_BYTES = (HERE / "SecurityCompilerOutputSnapshotCandidateTests.cs").read_bytes()
NATIVE_TRX_BYTES = b""
NATIVE_DISCOVERY = ""


def validate_retained_inputs() -> None:
    """Admit the authentic positive before any temporary Git fixture is initialized."""
    if not NATIVE_TRX_BYTES:
        raise ValueError("Retained native TRX input is required; no synthetic positive is substituted")
    if not NATIVE_DISCOVERY:
        raise ValueError("Retained compiled discovery input is required")
    with tempfile.TemporaryDirectory(prefix="retained-input-admission-") as temporary:
        path = Path(temporary) / "candidate.trx"
        path.write_bytes(NATIVE_TRX_BYTES)
        try:
            native_names = RUNNER.native_inventory(path)
        except (ET.ParseError, ValueError) as error:
            raise ValueError("Retained native TRX input does not match the frozen candidate") from error
    try:
        compiled_names = RUNNER.compiled_inventory(NATIVE_DISCOVERY)
    except ValueError as error:
        raise ValueError("Retained compiled discovery input does not match the frozen candidate") from error
    if native_names != compiled_names:
        raise ValueError("Retained native TRX and compiled discovery identities differ")


COMPOSITION_ERRORS = {
    "source": "Actual compiler-input bytes differ from the committed candidate",
    "product": "Unrelated tracked Product source changed during composition",
    "index": "Composed candidate index differs from its committed Git blob",
    "head": "Composed Product HEAD changed",
}


def native_fixture() -> ET.Element:
    if not NATIVE_TRX_BYTES:
        raise ValueError("Retained native TRX input is required; no synthetic positive is substituted")
    return ET.fromstring(NATIVE_TRX_BYTES)


def split_method_identity(root: ET.Element) -> None:
    method = root.find(NAMESPACE + "TestDefinitions")[0].find(NAMESPACE + "TestMethod")
    method.set("className", "Coglatas.Tests.ProjectIde")
    method.set("name", RUNNER.TEST_CLASS + "." + method.get("name"))


def append_failed_results(root: ET.Element) -> None:
    results = ET.SubElement(root, NAMESPACE + "Results")
    ET.SubElement(results, NAMESPACE + "GenericTestResult", testName="FOREIGN_CONTROL", outcome="Failed")


class NativeInventoryTests(unittest.TestCase):
    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="compiler-output-inventory-")
        self.addCleanup(temporary.cleanup)
        self.path = Path(temporary.name) / "candidate.trx"

    def admit(self, root: ET.Element | None = None) -> list[str]:
        self.path.write_bytes(NATIVE_TRX_BYTES if root is None else ET.tostring(root))
        return RUNNER.native_inventory(self.path)

    def test_original_source_and_thirty_five_fact_identities_are_frozen(self) -> None:
        blob = hashlib.sha1(b"blob " + str(len(SOURCE_BYTES)).encode() + b"\0" + SOURCE_BYTES).hexdigest()
        self.assertEqual("b13bc41f398ca0877d1d9505a2899fd7d835b657", blob)
        methods = re.findall(r"\[Fact\]\s+public (?:async )?(?:Task|void) (\w+)\(", SOURCE_BYTES.decode())
        self.assertEqual(list(RUNNER.EXPECTED_METHODS), methods)
        self.assertEqual(35, len(set(methods)))
        self.assertEqual(11, len(RUNNER.ORIGINAL_CORE_METHODS))
        self.assertEqual(24, len(RUNNER.VIEWMODEL_METHODS))

    def test_retained_native_trx_and_compiled_discovery_are_admitted(self) -> None:
        expected = sorted(RUNNER.EXPECTED_IDENTITIES)
        self.assertEqual(expected, self.admit())
        self.assertEqual(expected, RUNNER.compiled_inventory(NATIVE_DISCOVERY))

    def test_wrong_class_and_dotted_method_cannot_preserve_admission(self) -> None:
        root = native_fixture()
        definition = root.find(NAMESPACE + "TestDefinitions")[0]
        split_method_identity(root)
        method = definition.find(NAMESPACE + "TestMethod")
        self.assertEqual(definition.get("name"), method.get("className") + "." + method.get("name"))
        with self.assertRaisesRegex(ValueError, "method metadata"):
            self.admit(root)

    def test_second_results_container_cannot_hide_a_failed_generic_result(self) -> None:
        root = native_fixture()
        append_failed_results(root)
        self.assertEqual(35, len(root.find(NAMESPACE + "Results")))
        with self.assertRaisesRegex(ValueError, "one Results"):
            self.admit(root)

    def test_missing_duplicate_substituted_and_nonpassing_results_are_rejected(self) -> None:
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

    def test_duplicate_or_missing_inventory_containers_and_counters_are_rejected(self) -> None:
        for name in ("ResultSummary", "Results", "TestDefinitions", "TestEntries", "Counters"):
            for mode in ("missing", "duplicate-empty", "duplicate-original"):
                with self.subTest(container=name, mode=mode):
                    root = native_fixture()
                    parent = root.find(NAMESPACE + "ResultSummary") if name == "Counters" else root
                    node = parent.find(NAMESPACE + name)
                    if mode == "missing":
                        parent.remove(node)
                    else:
                        duplicate = copy.deepcopy(node)
                        if mode == "duplicate-empty":
                            duplicate.clear()
                        parent.append(duplicate)
                    with self.assertRaises(ValueError):
                        self.admit(root)

    def test_unsupported_inventory_records_cannot_be_ignored_or_replace_native_records(self) -> None:
        for container, unexpected in (("Results", "GenericTestResult"),
                                      ("TestDefinitions", "GenericTest"), ("TestEntries", "UnknownEntry")):
            for mode in ("extra", "replacement"):
                with self.subTest(container=container, mode=mode):
                    root = native_fixture()
                    node = root.find(NAMESPACE + container)
                    if mode == "replacement":
                        node.remove(node[0])
                    ET.SubElement(node, NAMESPACE + unexpected, outcome="Failed")
                    with self.assertRaisesRegex(ValueError, "unsupported inventory"):
                        self.admit(root)

    def test_method_metadata_requires_one_exact_class_and_bare_method(self) -> None:
        for mode in ("missing", "duplicate", "wrong-class", "wrong-method"):
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
                else:
                    method.set("name", "DifferentFact")
                with self.assertRaises(ValueError):
                    self.admit(root)

    def test_result_definition_and_entry_identities_remain_unique_and_bijective(self) -> None:
        for mode in ("missing-test-id", "duplicate-test-id", "duplicate-execution-id", "wrong-entry",
                     "missing-execution", "duplicate-execution", "wrong-definition-name", "swapped-names"):
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
                    root.find(NAMESPACE + "TestEntries")[0].set("executionId", "foreign-execution")
                elif mode == "missing-execution":
                    definition.remove(execution)
                elif mode == "duplicate-execution":
                    definition.append(copy.deepcopy(execution))
                elif mode == "wrong-definition-name":
                    definition.set("name", "UnexpectedDefinition")
                else:
                    first, second = results[0].get("testName"), results[1].get("testName")
                    results[0].set("testName", second)
                    results[1].set("testName", first)
                with self.assertRaises(ValueError):
                    self.admit(root)

    def test_incomplete_nonzero_and_noncanonical_native_counters_are_rejected(self) -> None:
        for counter in ("total", "executed", "passed", "failed", "notExecuted"):
            for mode in ("missing", "wrong"):
                with self.subTest(counter=counter, mode=mode):
                    root = native_fixture()
                    counters = root.find(NAMESPACE + "ResultSummary").find(NAMESPACE + "Counters")
                    if mode == "missing":
                        del counters.attrib[counter]
                    else:
                        counters.set(counter, "1")
                    with self.assertRaises(ValueError):
                        self.admit(root)
        for value in ("1", "-0", "00", " 0", "not-a-counter"):
            with self.subTest(error=value):
                root = native_fixture()
                root.find(NAMESPACE + "ResultSummary").find(NAMESPACE + "Counters").set("error", value)
                with self.assertRaises(ValueError):
                    self.admit(root)

    def test_root_and_every_native_inventory_namespace_are_exact(self) -> None:
        selectors = (".", "ResultSummary", "ResultSummary/Counters", "Results", "Results/UnitTestResult",
                     "TestDefinitions", "TestDefinitions/UnitTest", "TestDefinitions/UnitTest/Execution",
                     "TestDefinitions/UnitTest/TestMethod", "TestEntries", "TestEntries/TestEntry")
        for selector in selectors:
            for namespace in ("", "{urn:foreign-trx}"):
                with self.subTest(selector=selector, namespace=namespace):
                    root = native_fixture()
                    node = root if selector == "." else root.find("/".join(NAMESPACE + part for part in selector.split("/")))
                    node.tag = namespace + node.tag.rsplit("}", 1)[-1]
                    with self.assertRaises(ValueError):
                        self.admit(root)
        self.path.write_text("<broken", encoding="utf-8")
        with self.assertRaises(ET.ParseError):
            RUNNER.native_inventory(self.path)

    def test_discovery_requires_exact_raw_fqns_on_both_line_endings(self) -> None:
        expected = sorted(RUNNER.EXPECTED_IDENTITIES)
        normalized = NATIVE_DISCOVERY.replace("\r\n", "\n")
        self.assertEqual(expected, RUNNER.compiled_inventory(normalized))
        self.assertEqual(expected, RUNNER.compiled_inventory(normalized.replace("\n", "\r\n")))
        variants = (
            normalized + "\n    Foreign.Namespace.ExtraFact\n",
            normalized.replace(expected[0], expected[1]),
            normalized.replace("    " + expected[0] + "\n", ""),
            normalized.replace(expected[0], expected[0].rsplit(".", 1)[-1]),
            normalized.replace(expected[0], expected[0] + " "),
        )
        for number, discovery in enumerate(variants):
            with self.subTest(variant=number):
                with self.assertRaises(ValueError):
                    RUNNER.compiled_inventory(discovery)

    def test_command_preserves_discovery_whitespace_and_nonzero_exit(self) -> None:
        value = NATIVE_DISCOVERY.replace("\r\n", "\n").rstrip("\n") + " "
        log = self.path.with_suffix(".log")
        actual = RUNNER.command([sys.executable, "-c", "import sys; sys.stdout.write(" + repr(value) + ")"],
                                self.path.parent, log, preserve_output=True)
        self.assertEqual(value, actual)
        self.assertEqual(value, log.read_text(encoding="utf-8"))
        with self.assertRaises(RuntimeError):
            RUNNER.command([sys.executable, "-c", "import sys; sys.stderr.write('controlled failure'); sys.exit(1)"],
                           self.path.parent, log)
        self.assertEqual("controlled failure", log.read_text(encoding="utf-8"))


class MainAdmissionTests(unittest.TestCase):
    """Run actual main and Git worktrees; only dotnet outputs are controlled retained inputs."""

    @classmethod
    def setUpClass(cls) -> None:
        validate_retained_inputs()
        cls.temporary = tempfile.TemporaryDirectory(prefix="compiler-output-main-")
        cls.addClassCleanup(cls.temporary.cleanup)
        cls.root = Path(cls.temporary.name)
        cls.repository = cls.root / "security"
        cls.repository.mkdir()
        cls.real_command = RUNNER.command
        cls.git("init", "--quiet")
        cls.git("config", "user.name", "Qualification fixture")
        cls.git("config", "user.email", "qualification-fixture@example.invalid")
        cls.git("config", "commit.gpgsign", "false")
        (cls.repository / "product.txt").write_text("Immutable Product fixture\n", encoding="utf-8")
        cls.git("add", "--", "product.txt")
        cls.git("commit", "--quiet", "-m", "Product fixture")
        cls.product_head = cls.git("rev-parse", "HEAD")
        cls.product_tree = cls.git("rev-parse", "HEAD^{tree}")
        source = cls.repository / RUNNER.SOURCE
        source.parent.mkdir(parents=True)
        source.write_bytes(SOURCE_BYTES)
        cls.git("add", "--", RUNNER.SOURCE)
        cls.git("commit", "--quiet", "-m", "Independent Test fixture")

    @classmethod
    def git(cls, *args: str) -> str:
        result = subprocess.run(["git", "-c", "core.autocrlf=false", *args], cwd=cls.repository,
                                capture_output=True, text=True, check=True, env=RUNNER.local_git_environment())
        return result.stdout.strip()

    def mutate_composition(self, kind: str, worktree: Path) -> None:
        if kind == "source":
            (worktree / RUNNER.DESTINATION).write_bytes(SOURCE_BYTES + b"\n// Changed compiler input.\n")
        elif kind == "product":
            (worktree / "product.txt").write_text("Changed Product fixture\n", encoding="utf-8")
        elif kind == "index":
            alternate = worktree.parent / "index-only.cs"
            alternate.write_bytes(SOURCE_BYTES + b"\n// Changed index only.\n")
            blob = type(self).real_command(["git", "hash-object", "-w", str(alternate)], worktree)
            type(self).real_command(["git", "update-index", "--cacheinfo", "100644", blob, RUNNER.DESTINATION], worktree)
        elif kind == "head":
            changed = type(self).real_command(["git", "commit-tree", self.product_tree, "-p", self.product_head,
                                              "-m", "Controlled Product HEAD drift"], worktree)
            type(self).real_command(["git", "update-ref", "HEAD", changed], worktree)
        else:
            raise ValueError("Unknown controlled composition mutation")

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
                if stage == "test":
                    trx = native_fixture()
                    if scenario == "split-method":
                        split_method_identity(trx)
                    elif scenario == "second-failed-results":
                        append_failed_results(trx)
                    elif scenario == "substituted-roster":
                        trx.find(NAMESPACE + "Results")[0].set("testName", "UNTRUSTED_CANARY")
                    (evidence / "candidate.trx").write_bytes(ET.tostring(trx))
                if stage == "discovery":
                    output = NATIVE_DISCOVERY
                    if scenario == "foreign-discovery":
                        output += "\n    Foreign.Namespace.ExtraFact\n"
                for kind in ("source", "product", "index", "head"):
                    if scenario == kind + "-after-" + stage:
                        self.mutate_composition(kind, worktree)
                if log is not None:
                    log.write_text(output, encoding="utf-8")
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
                receipt = json.loads((evidence / "receipt.json").read_text(encoding="utf-8"))
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
        self.assertEqual("SYNTHETIC_COMPILER_OUTPUT_PASSED", receipt["status"])
        self.assertEqual(sorted(RUNNER.EXPECTED_IDENTITIES), receipt["nativeXunit"]["identities"])
        self.assertEqual(["--version", "restore", "test", "discovery"], calls)

    def assert_main_refusal(self, scenario: str, message: str, expected_calls: list[str]) -> None:
        error, receipt, calls = self.run_main(scenario)
        self.assertIsInstance(error, ValueError)
        self.assertEqual(message, str(error))
        self.assertEqual("ValueError: " + message, receipt["error"])
        self.assertEqual("FAILED_OR_INCOMPLETE", receipt["status"])
        self.assertNotIn("nativeXunit", receipt)
        self.assertEqual(expected_calls, calls)

    def test_main_refuses_malformed_reports_and_foreign_evidence(self) -> None:
        scenarios = (
            ("split-method", "Native TRX method metadata disagrees with its frozen fact identity", False),
            ("second-failed-results", "Native TRX requires one Results element", False),
            ("substituted-roster", "Native TRX does not match the exact original Core and ViewModel fact roster", False),
            ("foreign-discovery", "Compiled discovery does not match the exact thirty-five-fact candidate roster", True),
        )
        for scenario, message, discovery_reached in scenarios:
            with self.subTest(scenario=scenario):
                calls = ["--version", "restore", "test"] + (["discovery"] if discovery_reached else [])
                self.assert_main_refusal(scenario, message, calls)

    def test_main_rechecks_actual_composition_after_restore_before_compilation(self) -> None:
        for kind, message in COMPOSITION_ERRORS.items():
            with self.subTest(kind=kind):
                self.assert_main_refusal(kind + "-after-restore", message, ["--version", "restore"])

    def test_main_refuses_actual_composition_drift_after_test_and_discovery(self) -> None:
        for stage in ("test", "discovery"):
            for kind, message in COMPOSITION_ERRORS.items():
                with self.subTest(stage=stage, kind=kind):
                    calls = ["--version", "restore", "test"] + (["discovery"] if stage == "discovery" else [])
                    self.assert_main_refusal(kind + "-after-" + stage, message, calls)

    def test_main_preserves_native_command_failure(self) -> None:
        error, receipt, calls = self.run_main("tool-failure")
        self.assertIsInstance(error, RuntimeError)
        self.assertEqual("Controlled dotnet failure", str(error))
        self.assertEqual("RuntimeError: Controlled dotnet failure", receipt["error"])
        self.assertEqual("FAILED_OR_INCOMPLETE", receipt["status"])
        self.assertNotIn("nativeXunit", receipt)
        self.assertEqual(["--version", "restore", "test"], calls)


class FixtureBoundaryTests(unittest.TestCase):
    def test_git_child_environment_preserves_all_non_git_settings(self) -> None:
        overrides = {
            "GIT_DIR": "unused-repository-override",
            "GIT_INDEX_FILE": "unused-index-override",
            "GIT_CONFIG_COUNT": "1",
            "GIT_CONFIG_KEY_0": "core.worktree",
            "GIT_CONFIG_VALUE_0": "unused-worktree-override",
            "QUALIFICATION_ENV_CANARY": "host-setting-preserved",
        }
        with patch.dict(os.environ, overrides):
            before = dict(os.environ)
            expected = {key: value for key, value in before.items() if not key.upper().startswith("GIT_")}
            self.assertEqual(expected, RUNNER.local_git_environment())
            self.assertEqual(before, dict(os.environ))
            actual = RUNNER.command(
                [sys.executable, "-c", "import os; print(os.environ['QUALIFICATION_ENV_CANARY'])"], HERE)
            self.assertEqual("host-setting-preserved", actual)

    def test_invalid_retained_inputs_fail_before_main_git_setup(self) -> None:
        foreign = native_fixture()
        foreign.find(NAMESPACE + "Results")[0].set("testName", "Foreign.Namespace.Fact")
        cases = (
            (b"", NATIVE_DISCOVERY,
             "Retained native TRX input is required; no synthetic positive is substituted"),
            (b"<broken", NATIVE_DISCOVERY,
             "Retained native TRX input does not match the frozen candidate"),
            (ET.tostring(foreign), NATIVE_DISCOVERY,
             "Retained native TRX input does not match the frozen candidate"),
            (NATIVE_TRX_BYTES, "", "Retained compiled discovery input is required"),
            (NATIVE_TRX_BYTES, "    Foreign.Namespace.Fact\n",
             "Retained compiled discovery input does not match the frozen candidate"),
        )
        for trx, discovery, message in cases:
            with self.subTest(message=message, bytes=len(trx)):
                class InvalidMainFixture(MainAdmissionTests):
                    pass

                try:
                    with patch.dict(globals(), {"NATIVE_TRX_BYTES": trx, "NATIVE_DISCOVERY": discovery}), \
                            patch.object(InvalidMainFixture, "git", side_effect=AssertionError(
                                "Invalid retained inputs reached Git fixture setup")) as git_command:
                        with self.assertRaisesRegex(ValueError, "^" + re.escape(message) + "$"):
                            InvalidMainFixture.setUpClass()
                        git_command.assert_not_called()
                finally:
                    InvalidMainFixture.doClassCleanups()

    def test_polluted_environment_redirects_unprotected_git_but_isolates_actual_main(self) -> None:
        with tempfile.TemporaryDirectory(prefix="git-caller-isolation-") as temporary:
            caller = Path(temporary) / "caller"
            caller.mkdir()
            for args in (("init", "--quiet"), ("config", "user.name", "Disposable caller"),
                         ("config", "user.email", "disposable-caller@example.invalid"),
                         ("config", "commit.gpgsign", "false")):
                RUNNER.git(caller, *args)
            (caller / "caller.txt").write_text("Untouched disposable caller\n", encoding="utf-8")
            RUNNER.git(caller, "add", "--", "caller.txt")
            RUNNER.git(caller, "commit", "--quiet", "-m", "Disposable caller")

            def snapshot() -> dict[str, bytes]:
                return {str(path.relative_to(caller)): path.read_bytes()
                        for path in caller.rglob("*") if path.is_file()}

            before = snapshot()
            polluted = RUNNER.local_git_environment()
            polluted.update({
                "GIT_DIR": str(caller / ".git"),
                "GIT_COMMON_DIR": str(caller / ".git"),
                "GIT_WORK_TREE": str(caller),
                "GIT_INDEX_FILE": str(caller / ".git" / "index"),
                "GIT_OBJECT_DIRECTORY": str(caller / ".git" / "objects"),
                "GIT_CONFIG_COUNT": "1",
                "GIT_CONFIG_KEY_0": "core.worktree",
                "GIT_CONFIG_VALUE_0": str(caller),
            })

            class PollutedMainFixture(MainAdmissionTests):
                pass

            try:
                with patch.dict(os.environ, polluted, clear=True):
                    PollutedMainFixture.setUpClass()
                    repository = PollutedMainFixture.repository
                    # This read-only probe reproduces the old inherited-environment redirect.
                    unprotected = subprocess.run(["git", "rev-parse", "--show-toplevel"], cwd=repository,
                                                 capture_output=True, text=True, check=True, env=polluted)
                    self.assertEqual(caller.resolve(), Path(unprotected.stdout.strip()).resolve())
                    self.assertEqual(repository.resolve(), Path(
                        RUNNER.git(repository, "rev-parse", "--show-toplevel")).resolve())
                    self.assertEqual(repository.resolve(), Path(
                        PollutedMainFixture.git("rev-parse", "--show-toplevel")).resolve())
                    fixture = PollutedMainFixture("test_main_positive_control_uses_real_git_and_preserves_false_flags")
                    error, receipt, calls = fixture.run_main("valid")
                    self.assertIsNone(error)
                    self.assertEqual("SYNTHETIC_COMPILER_OUTPUT_PASSED", receipt["status"])
                    self.assertEqual(sorted(RUNNER.EXPECTED_IDENTITIES), receipt["nativeXunit"]["identities"])
                    self.assertEqual(["--version", "restore", "test", "discovery"], calls)
            finally:
                PollutedMainFixture.doClassCleanups()
            self.assertEqual(before, snapshot())


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--native-trx", type=Path, required=True)
    parser.add_argument("--compiled-discovery", type=Path, required=True)
    arguments, unittest_arguments = parser.parse_known_args()
    NATIVE_TRX_BYTES = arguments.native_trx.read_bytes()
    NATIVE_DISCOVERY = arguments.compiled_discovery.read_bytes().decode("utf-8")
    validate_retained_inputs()
    print("Admission controls reuse retained TRX SHA256 " + hashlib.sha256(NATIVE_TRX_BYTES).hexdigest()
          + "; they do not execute dotnet.", flush=True)
    unittest.main(argv=[sys.argv[0], *unittest_arguments])
