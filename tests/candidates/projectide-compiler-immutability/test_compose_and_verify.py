#!/usr/bin/env python3
"""Regression controls for admission only; controlled dotnet output is never native evidence."""
from __future__ import annotations

from contextlib import redirect_stdout
import copy
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import uuid
import xml.etree.ElementTree as ET

WORKFLOW_PATH = ".github/workflows/projectide-compiler-immutability-advisory.yml"
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

    def test_nested_native_results_cannot_hide_behind_the_original_outer_roster(self) -> None:
        for kind in ("UnitTestResult", "GenericTestResult"):
            for outcome in ("Passed", "NotExecuted", "Failed"):
                with self.subTest(kind=kind, outcome=outcome):
                    root = native_fixture()
                    inner = ET.SubElement(root.find(NAMESPACE + "Results")[0], NAMESPACE + "InnerResults")
                    ET.SubElement(inner, NAMESPACE + kind, outcome=outcome, testName="NESTED_CONTROL")
                    with self.assertRaisesRegex(ValueError, "nested result records"):
                        self.admit(root)

    def test_results_hidden_outside_the_native_result_container_are_rejected(self) -> None:
        for container in ("TestDefinitions", "TestEntries", "ResultSummary"):
            with self.subTest(container=container):
                root = native_fixture()
                ET.SubElement(root.find(NAMESPACE + container)[0], NAMESPACE + "UnitTestResult", outcome="Passed")
                with self.assertRaisesRegex(ValueError, "nested result records"):
                    self.admit(root)

    def test_run_level_error_cannot_hide_behind_passing_counters(self) -> None:
        for outcome in ("Error", "Failed", "Aborted", "Unexpected"):
            for location in ("summary", "root", "result"):
                with self.subTest(outcome=outcome, location=location):
                    root = native_fixture()
                    parent = (root.find(NAMESPACE + "ResultSummary") if location == "summary" else
                              root.find(NAMESPACE + "Results")[0] if location == "result" else root)
                    infos = ET.SubElement(parent, NAMESPACE + "RunInfos")
                    ET.SubElement(infos, NAMESPACE + "RunInfo", outcome=outcome)
                    with self.assertRaisesRegex(ValueError, "run-level error"):
                        self.admit(root)

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
                if stage in ("restore", "test", "discovery"):
                    self.assertIn("-p:ImportDirectoryBuildProps=false", args)
                    self.assertIn("-p:ImportDirectoryBuildTargets=false", args)
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



class CompositionBoundaryTests(unittest.TestCase):
    """Exercise actual Git masks, hidden inputs and independent staged/working drift."""

    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="compiler-composition-controls-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.repository = self.root / "product"
        self.repository.mkdir()
        self.git("init", "--quiet", "--template=")
        self.git("config", "user.name", "Admission fixture")
        self.git("config", "user.email", "admission-fixture@example.invalid")
        self.git("config", "commit.gpgsign", "false")
        (self.repository / ".gitignore").write_text("ignored/\nbin/\nobj/\n", encoding="utf-8")
        (self.repository / "product with spaces.cs").write_text("// Frozen Product.\n", encoding="utf-8")
        project = self.repository / "tests/Coglatas.Tests/Coglatas.Tests.csproj"
        project.parent.mkdir(parents=True)
        project.write_text("<Project />", encoding="utf-8")
        self.git("add", "--all")
        self.git("commit", "--quiet", "-m", "Frozen disposable Product")
        self.head = self.git("rev-parse", "HEAD")
        self.output = self.repository / RUNNER.DESTINATION
        self.output.parent.mkdir(parents=True, exist_ok=True)
        self.raw = b"// Composed independent candidate.\n"
        self.output.write_bytes(self.raw)
        self.git("add", "--", RUNNER.DESTINATION)
        self.blob = self.git("rev-parse", ":" + RUNNER.DESTINATION)

    def git(self, *arguments: str) -> str:
        completed = subprocess.run(["git", "-c", "core.autocrlf=false", *arguments], cwd=self.repository,
                                   capture_output=True, text=True, check=True,
                                   env=RUNNER.local_git_environment())
        return completed.stdout.strip()

    def verify(self, allow_generated: bool = False) -> None:
        RUNNER.verify_composition(self.repository, self.raw, self.blob, self.head,
                                  allow_generated=allow_generated)

    def test_unmodified_composition_is_admitted(self) -> None:
        self.verify()

    def test_index_masks_refuse_hidden_working_bytes_with_unchanged_blob(self) -> None:
        relative = "product with spaces.cs"
        path = self.repository / relative
        original = path.read_bytes()
        original_blob = self.git("rev-parse", ":" + relative)
        for flag in ("assume-unchanged", "skip-worktree"):
            with self.subTest(flag=flag):
                self.git("update-index", "--" + flag, "--", relative)
                path.write_bytes(original + b"// Hidden compiler input.\n")
                self.assertEqual(original_blob, self.git("rev-parse", ":" + relative))
                self.assertEqual("", self.git("diff", "--name-only", "--", relative))
                with self.assertRaisesRegex(ValueError, "Git index flags hide"):
                    self.verify()
                self.git("update-index", "--no-" + flag, "--", relative)
                path.write_bytes(original)
                self.verify()

    @unittest.skipIf(os.name == "nt", "Windows filenames cannot contain line breaks")
    def test_index_mask_records_preserve_nul_delimited_newline_paths(self) -> None:
        for relative in (b"product\nwith-line-break.cs", b"product-\xff.cs"):
            with self.subTest(path=repr(relative)):
                path = os.fsencode(self.repository) + b"/" + relative
                with open(path, "wb") as handle:
                    handle.write(b"// Tracked raw pathname.\n")
                self.git("add", "--", relative)
                self.git("commit", "--quiet", "-m", "Additional disposable tracked raw path")
                records = RUNNER.git_records(self.repository, "ls-files", "-z", "--")
                self.assertIn(relative, records)
                self.git("update-index", "--skip-worktree", "--", relative)
                with self.assertRaisesRegex(ValueError, "Git index flags hide"):
                    RUNNER.verify_index_flags(self.repository)
                self.git("update-index", "--no-skip-worktree", "--", relative)
                RUNNER.verify_index_flags(self.repository)

    def test_index_only_product_change_cannot_hide_behind_original_working_bytes(self) -> None:
        relative = "product with spaces.cs"
        path = self.repository / relative
        original = path.read_bytes()
        path.write_bytes(original + b"// Unreviewed staged Product.\n")
        self.git("add", "--", relative)
        path.write_bytes(original)
        self.assertEqual("", self.git("diff", "HEAD", "--", relative))
        with self.assertRaisesRegex(ValueError, "Unrelated tracked Product"):
            self.verify()

    def test_ignored_and_untracked_inputs_are_refused_at_each_output_phase(self) -> None:
        for relative in ("tests/Coglatas.Tests/Injected.cs", "tests/Coglatas.Tests/ignored/Injected.cs",
                         "tests/Coglatas.Tests/ProjectIde/obj/Injected.cs", "unregistered/bin/Injected.cs"):
            with self.subTest(path=relative):
                injected = self.repository / relative
                injected.parent.mkdir(parents=True, exist_ok=True)
                injected.write_text("// Unreviewed source input.\n", encoding="utf-8")
                for allow_generated in (False, True):
                    with self.assertRaisesRegex(ValueError, "Unexpected untracked source"):
                        self.verify(allow_generated)
                injected.unlink()
                self.verify()

    def test_only_registered_sdk_output_roots_are_admitted_after_restore(self) -> None:
        for relative in ("tests/Coglatas.Tests/obj/Release/AssemblyInfo.cs",
                         "tests/Coglatas.Tests/bin/Release/result.dll"):
            path = self.repository / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"Disposable SDK output")
        with self.assertRaisesRegex(ValueError, "Unexpected untracked source"):
            self.verify()
        self.verify(allow_generated=True)

    def test_git_child_environment_refuses_repository_and_index_redirects(self) -> None:
        with patch.dict(os.environ, {"GIT_DIR": "unreviewed", "GIT_INDEX_FILE": "unreviewed",
                                     "GIT_CONFIG_COUNT": "1", "KEEP_HOST_SETTING": "preserved"}):
            self.assertEqual("preserved", RUNNER.local_git_environment()["KEEP_HOST_SETTING"])
            self.assertFalse(any(key.upper().startswith("GIT_") for key in RUNNER.local_git_environment()))
            self.verify()


class SdkImportControls(unittest.TestCase):
    """Exercise real SDK import behavior with a disposable project, never Product tests."""

    def test_restore_test_and_discovery_exclude_external_props_and_targets(self) -> None:
        if shutil.which("dotnet") is None:
            if os.environ.get("GITHUB_ACTIONS") == "true":
                self.fail("Hosted SDK import controls require dotnet")
            self.skipTest("dotnet is unavailable; hosted SDK import execution remains required")
        with tempfile.TemporaryDirectory(prefix="source-only-sdk-controls-") as temporary:
            parent = Path(temporary)
            product = parent / "product"
            product.mkdir()
            project = product / "Control.csproj"
            project.write_text(
                '<Project><Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />'
                '<PropertyGroup><TargetFramework>net10.0</TargetFramework>'
                '<EnableDefaultItems>false</EnableDefaultItems></PropertyGroup>'
                '<Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" />'
                '<Target Name="VSTest"><Message Text="SOURCE_ONLY_SDK_CONTROL" Importance="high" />'
                '</Target></Project>', encoding="utf-8")
            (product / "NuGet.Config").write_text(
                '<configuration><packageSources><clear /></packageSources></configuration>', encoding="utf-8")
            for filename in ("Directory.Build.props", "Directory.Build.targets"):
                external = parent / filename
                external.write_text(
                    '<Project><Target Name="UnreviewedAncestorInput" BeforeTargets="Restore;VSTest">'
                    '<Error Text="UNREVIEWED_ANCESTOR_IMPORT" /></Target></Project>', encoding="utf-8")
                for action, arguments in (
                    ("restore", (str(project), "--nologo")),
                    ("test", (str(project), "--no-restore", "--nologo")),
                    ("test", (str(project), "--no-build", "--list-tests", "--nologo")),
                ):
                    with self.subTest(filename=filename, action=action, arguments=arguments):
                        unprotected = subprocess.run(["dotnet", action, *arguments], cwd=product,
                                                     capture_output=True, text=True, timeout=90)
                        self.assertNotEqual(0, unprotected.returncode)
                        self.assertIn("UNREVIEWED_ANCESTOR_IMPORT", unprotected.stdout + unprotected.stderr)
                        protected = subprocess.run(RUNNER.source_only_dotnet(action, *arguments), cwd=product,
                                                   capture_output=True, text=True, timeout=90)
                        self.assertEqual(0, protected.returncode, protected.stdout + protected.stderr)
                        self.assertNotIn("UNREVIEWED_ANCESTOR_IMPORT", protected.stdout + protected.stderr)
                external.unlink()




class WorkflowBootstrapControls(unittest.TestCase):
    """Execute unchanged workflow launch blocks against disposable pinned Python files."""

    def setUp(self) -> None:
        self.pwsh = shutil.which("pwsh")
        if self.pwsh is None:
            if os.environ.get("GITHUB_ACTIONS") == "true":
                self.fail("Hosted executable bootstrap controls require PowerShell")
            self.skipTest("PowerShell unavailable; hosted executable bootstrap remains required")
        temporary = tempfile.TemporaryDirectory(prefix="compiler-bootstrap-controls-")
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        self.repository = self.directory / "security"
        self.repository.mkdir()
        self.git("init", "--quiet", "--template=")
        self.git("config", "user.name", "Bootstrap fixture")
        self.git("config", "user.email", "bootstrap-fixture@example.invalid")
        self.git("config", "commit.gpgsign", "false")
        workflow = Path(__file__).resolve().parents[3] / WORKFLOW_PATH
        blocks = re.findall(r"        run: \|\n((?:          [^\n]*\n)+)", workflow.read_text(encoding="utf-8"))
        self.scripts = sorted(set(block for block in blocks if "python -I -B " in block))
        self.assertEqual(2, len(self.scripts), "Both runner and controls launch blocks must be checked")
        self.paths = sorted(set(re.findall(r"git rev-parse 'HEAD:([^']+)'", "".join(self.scripts))))
        self.assertEqual(2, len(self.paths))
        self.raw = (b"import json, os, sys\nfrom pathlib import Path\n"
                    b"Path(os.environ['BOOTSTRAP_RESULT']).write_text(json.dumps({'isolated': sys.flags.isolated}))\n")
        for relative in self.paths:
            path = self.repository / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(self.raw)
        self.git("add", "--all")
        self.git("commit", "--quiet", "-m", "Disposable original Python executables")
        self.blob = self.git("rev-parse", "HEAD:" + self.paths[0])
        self.result = self.directory / "executed.json"
        self.shadow = self.directory / "shadow-executed.txt"
        for context in ("compiler-output-qualification-context", "historical-compiler-context"):
            (self.directory / context).mkdir()

    def git(self, *arguments: str) -> str:
        return subprocess.run(["git", "-c", "core.autocrlf=false", *arguments], cwd=self.repository,
                              capture_output=True, text=True, check=True,
                              env=RUNNER.local_git_environment()).stdout.strip()

    def execute(self, block: str) -> subprocess.CompletedProcess:
        script = self.directory / "bootstrap.ps1"
        script.write_text("\n".join(line[10:] for line in block.splitlines()) + "\n", encoding="utf-8")
        environment = RUNNER.local_git_environment()
        environment.update(RUNNER_BLOB=self.blob, REGRESSION_BLOB=self.blob, CONTROLS_BLOB=self.blob,
                           RED_RUNNER_BLOB=self.blob, RED_CONTROLS_BLOB=self.blob,
                           RUNNER_TEMP=str(self.directory), BOOTSTRAP_RESULT=str(self.result),
                           BOOTSTRAP_SHADOW=str(self.shadow))
        return subprocess.run([self.pwsh, "-NoLogo", "-NoProfile", "-NonInteractive", "-File", str(script)],
                              cwd=self.repository, env=environment, capture_output=True, text=True, timeout=30)

    def test_changed_working_and_committed_bytes_never_execute(self) -> None:
        for relative in self.paths:
            path = self.repository / relative
            for kind in ("working", "normalized-working", "committed"):
                with self.subTest(path=relative, kind=kind):
                    path.write_bytes(self.raw.replace(b"\n", b"\r\n") if kind == "normalized-working"
                                     else self.raw + b"# Changed executable.\n")
                    if kind == "normalized-working":
                        self.assertEqual(self.blob, self.git("-c", "core.autocrlf=true", "hash-object", "--", relative))
                    if kind == "committed":
                        self.git("add", "--", relative)
                        self.git("commit", "--quiet", "-m", "Different committed executable")
                    for block in self.scripts:
                        if "'HEAD:" + relative + "'" not in block:
                            continue
                        result = self.execute(block)
                        self.assertNotEqual(0, result.returncode)
                        reason = "committed blob mismatch" if kind == "committed" else "working bytes mismatch"
                        self.assertIn(reason, result.stdout + result.stderr)
                        self.assertFalse(self.result.exists(), "Altered executable reached its first instruction")
                    path.write_bytes(self.raw)
                    if kind == "committed":
                        self.git("add", "--", relative)
                        self.git("commit", "--quiet", "-m", "Restore disposable original executable")

    def test_original_executable_ignores_neighboring_import_shadow(self) -> None:
        for parent in {Path(relative).parent for relative in self.paths}:
            shadow = self.repository / parent / "json.py"
            shadow.write_text("import os\nfrom pathlib import Path\n"
                              "Path(os.environ['BOOTSTRAP_SHADOW']).write_text('executed')\n"
                              "raise RuntimeError('Neighboring import shadow executed')\n", encoding="utf-8")
        for block in self.scripts:
            result = self.execute(block)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertEqual({"isolated": 1}, json.loads(self.result.read_text(encoding="utf-8")))
            self.assertFalse(self.shadow.exists())
            self.result.unlink()

if __name__ == "__main__":
    unittest.main()
