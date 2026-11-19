"""Portable verifier controls; synthetic reports and disposable Git, never native C# evidence."""
from __future__ import annotations

import importlib.util
import base64
import copy
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
from uuid import UUID
import xml.etree.ElementTree as ET


RUNNER_PATH = Path(__file__).with_name("compose_and_verify.py")
SPEC = importlib.util.spec_from_file_location("test_discovery_candidate_runner", RUNNER_PATH)
assert SPEC is not None and SPEC.loader is not None
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)
NAMESPACE = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
IDENTITIES = sorted(RUNNER.EXPECTED_IDENTITIES)


class WorkflowPythonIsolationControls(unittest.TestCase):
    """Pin executable source and prove isolated interpretation independently."""

    def test_execution_has_original_code_bytes_and_explicit_refusal_on_each_host(self):
        workflow = (Path(__file__).resolve().parents[3] / ".github/workflows/projectide-test-discovery-advisory.yml").read_text(encoding="utf-8")
        self.assertEqual(8, workflow.count('git rev-parse "$($env:TEST_HEAD):$($script.Path)"'))
        self.assertEqual(8, workflow.count('git hash-object --no-filters -- $script.Path'))
        self.assertEqual(8, workflow.count("throw 'Pre-execution committed Python identity mismatch'"))
        self.assertEqual(8, workflow.count("throw 'Pre-execution working Python bytes mismatch'"))
        self.assertEqual(8, workflow.count("Python bytecode cache is not admitted"))
        self.assertEqual(12, workflow.count("@{ Path = 'tests/candidates/"))
        self.assertEqual(2, workflow.count("runpy.run_path('tests/candidates/workflow-provenance/test_workflow_provenance.py'"))
        self.assertEqual(2, workflow.count("runpy.run_path(p,run_name='__main__')"))
        self.assertEqual(2, workflow.count("python -I -B tests/candidates/projectide-test-discovery/compose_and_verify.py"))
        self.assertNotIn("python -B ", workflow)

    def test_casefolded_cache_and_uppercase_bytecode_names_are_refused(self):
        workflow = (Path(__file__).resolve().parents[3] / ".github/workflows/projectide-test-discovery-advisory.yml").read_text(encoding="utf-8")
        programs = [line.split('python -I -B -c "',1)[1].split('"',1)[0]
                    for line in workflow.splitlines()
                    if 'python -I -B -c "from pathlib import Path; import sys; roots=' in line]
        self.assertEqual(8, len(programs))
        self.assertEqual(1, len(set(programs)))
        with tempfile.TemporaryDirectory(prefix="test-discovery-cache-") as tmp:
            root = Path(tmp)
            def scan():
                return subprocess.run([sys.executable, "-I", "-B", "-c", programs[0], str(root)],
                                      capture_output=True, text=True, timeout=15)
            self.assertEqual(0, scan().returncode)
            cache = root / "__PYCACHE__"
            cache.mkdir()
            self.assertNotEqual(0, scan().returncode)
            cache.rmdir()
            for suffix in (".PYC", ".PYO"):
                with self.subTest(suffix=suffix):
                    injected = root / ("untrusted" + suffix)
                    injected.write_bytes(b"synthetic")
                    self.assertNotEqual(0, scan().returncode)
                    injected.unlink()
                    self.assertEqual(0, scan().returncode)

    def test_pythonpath_startup_import_shadow_excluded_by_isolation(self):
        with tempfile.TemporaryDirectory(prefix="test-discovery-shadow-") as tmp:
            marker = Path(tmp) / "imported"
            (Path(tmp) / "sitecustomize.py").write_text(
                "from pathlib import Path\nPath(" + repr(str(marker)) + ").touch()\n",
                encoding="utf-8")
            env = {**os.environ, "PYTHONPATH": tmp}
            plain = subprocess.run([sys.executable, "-B", "-c", "print('plain')"],
                                   env=env, capture_output=True, text=True, timeout=15)
            self.assertEqual(0, plain.returncode, plain.stderr)
            self.assertTrue(marker.exists())
            marker.unlink()
            isolated = subprocess.run([sys.executable, "-I", "-B", "-c",
                                       "import sys; assert sys.flags.isolated == 1"],
                                      env=env, capture_output=True, text=True, timeout=15)
            self.assertEqual(0, isolated.returncode, isolated.stderr)
            self.assertFalse(marker.exists())



def native_fixture() -> ET.Element:
    root = ET.Element(NAMESPACE + "TestRun")
    summary = ET.SubElement(root, NAMESPACE + "ResultSummary", outcome="Completed")
    ET.SubElement(summary, NAMESPACE + "Counters", total="24", executed="24",
                  passed="24", failed="0", notExecuted="0")
    results = ET.SubElement(root, NAMESPACE + "Results")
    definitions = ET.SubElement(root, NAMESPACE + "TestDefinitions")
    entries = ET.SubElement(root, NAMESPACE + "TestEntries")
    for index, identity in enumerate(IDENTITIES, 1):
        test_id, execution_id = str(UUID(int=index)), str(UUID(int=1000 + index))
        ET.SubElement(results, NAMESPACE + "UnitTestResult", testName=identity,
                      testId=test_id, executionId=execution_id, outcome="Passed")
        definition = ET.SubElement(definitions, NAMESPACE + "UnitTest", id=test_id, name=identity)
        ET.SubElement(definition, NAMESPACE + "Execution", id=execution_id)
        class_name, method = identity.rsplit(".", 1)
        ET.SubElement(definition, NAMESPACE + "TestMethod", className=class_name, name=method)
        ET.SubElement(entries, NAMESPACE + "TestEntry", testId=test_id, executionId=execution_id)
    return root


def discovery_fixture(names: list[str], newline: str = "\n") -> str:
    return newline.join(["The following Tests are available:", *["    " + name for name in names], ""])


class ExactRejection(unittest.TestCase):
    def reject(self, reason: str, action) -> None:
        with self.assertRaises(ValueError) as caught:
            action()
        self.assertEqual(reason, str(caught.exception))


class NativeInventoryControls(ExactRejection):
    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="test-discovery-native-controls-")
        self.addCleanup(temporary.cleanup)
        self.report = Path(temporary.name) / "synthetic.trx"
        self.assertEqual(24, len(IDENTITIES))
        self.assertEqual(IDENTITIES, self.read(native_fixture()))
        self.assertEqual(IDENTITIES, RUNNER.compiled_inventory(discovery_fixture(IDENTITIES)))

    def read(self, root: ET.Element) -> list[str]:
        ET.ElementTree(root).write(self.report, encoding="utf-8", xml_declaration=True)
        return RUNNER.native_inventory(self.report)

    def test_complete_native_shape_and_discovery_match(self) -> None:
        self.assertEqual(IDENTITIES, self.read(native_fixture()))
        for newline in ("\n", "\r\n"):
            with self.subTest(newline=repr(newline)):
                self.assertEqual(IDENTITIES, RUNNER.compiled_inventory(discovery_fixture(IDENTITIES, newline)))

    def test_counter_mismatch_and_incomplete_counter_are_rejected(self) -> None:
        for field, value in (("total", "23"), ("passed", "23"), ("notExecuted", "1"), ("inconclusive", "1")):
            with self.subTest(field=field):
                root = native_fixture()
                self.assertEqual(IDENTITIES, self.read(root))
                root.find(NAMESPACE + "ResultSummary/" + NAMESPACE + "Counters").set(field, value)
                self.reject("Native TRX must contain exactly twenty-four complete passing candidate facts", lambda: self.read(root))

    def test_missing_execution_is_rejected(self) -> None:
        root = native_fixture()
        definition = root.find(NAMESPACE + "TestDefinitions")[0]
        definition.remove(definition.find(NAMESPACE + "Execution"))
        self.reject("Native TRX requires one Execution element", lambda: self.read(root))

    def test_nonbijective_and_duplicate_ids_are_rejected(self) -> None:
        for duplicate in (False, True):
            with self.subTest(duplicate=duplicate):
                root = native_fixture()
                self.assertEqual(IDENTITIES, self.read(root))
                entries = root.find(NAMESPACE + "TestEntries")
                entries[0].set("executionId", entries[1].get("executionId") if duplicate else str(UUID(int=9000)))
                reason = ("Native TRX contains missing or duplicate test/execution identities" if duplicate else
                          "Native TRX result, definition and entry identities are not bijective")
                self.reject(reason, lambda: self.read(root))

    def test_wrong_class_or_dotted_method_is_rejected(self) -> None:
        class_name, bare_method = IDENTITIES[0].rsplit(".", 1)
        namespace, class_leaf = class_name.rsplit(".", 1)
        for label, class_value, method_value in (
            ("wrong-class", "Foreign.Class", bare_method),
            ("dotted-method", class_name, IDENTITIES[0]),
            ("split-class-method", namespace, class_leaf + "." + bare_method),
        ):
            with self.subTest(label=label):
                root = native_fixture()
                self.assertEqual(IDENTITIES, self.read(root))
                method = root.find(NAMESPACE + "TestDefinitions")[0].find(NAMESPACE + "TestMethod")
                method.set("className", class_value)
                method.set("name", method_value)
                if label == "split-class-method":
                    self.assertEqual(IDENTITIES[0], class_value + "." + method_value)
                self.reject("Native TRX method metadata disagrees with its frozen fact identity", lambda: self.read(root))

    def test_extra_results_container_is_rejected(self) -> None:
        root = native_fixture()
        ET.SubElement(root, NAMESPACE + "Results")
        self.reject("Native TRX requires one Results element", lambda: self.read(root))

    def test_nested_results_cannot_hide_behind_valid_outer_counters_and_ids(self) -> None:
        for container_name, outcome in (("InnerResults", "NotExecuted"), ("InnerResults", "Passed"),
                                        ("UnexpectedWrapper", "NotExecuted")):
            with self.subTest(container=container_name, outcome=outcome):
                root = native_fixture()
                self.assertEqual(IDENTITIES, self.read(root))
                outer = root.find(NAMESPACE + "Results")[0]
                inner = ET.SubElement(outer, NAMESPACE + container_name)
                ET.SubElement(inner, NAMESPACE + "UnitTestResult", testName="Foreign.Nested.Fact",
                              testId=str(UUID(int=9001)), executionId=str(UUID(int=9002)), outcome=outcome)
                self.assertIsNone(inner.find(".//{*}ErrorInfo"))
                self.assertEqual(24, len(root.find(NAMESPACE + "Results")))
                self.reject("Native TRX must contain only flat candidate results", lambda: self.read(root))

    def test_contradictory_native_errors_refuse_an_apparently_passing_summary(self) -> None:
        root = native_fixture()
        output = ET.SubElement(root.find(NAMESPACE + "ResultSummary"), NAMESPACE + "Output")
        ET.SubElement(output, NAMESPACE + "ErrorInfo")
        self.reject("Native TRX contains a run-level error", lambda: self.read(root))
        for outcome in ("Error", "Failed", "Aborted", "NotExecuted"):
            root = native_fixture()
            infos = ET.SubElement(root.find(NAMESPACE + "ResultSummary"), NAMESPACE + "RunInfos")
            ET.SubElement(infos, NAMESPACE + "RunInfo", outcome=outcome)
            self.reject("Native TRX contains failed or unsupported run-level result information", lambda: self.read(root))
        root = native_fixture()
        output = ET.SubElement(root.find(NAMESPACE + "Results")[0], NAMESPACE + "Output")
        ET.SubElement(output, NAMESPACE + "ErrorInfo")
        self.reject("Native TRX passing result contains error information", lambda: self.read(root))

    def test_all_non_direct_result_types_are_rejected(self) -> None:
        for name in ("GenericTestResult", "CustomTestResult", "TestResultAggregation"):
            for namespace in (NAMESPACE, "{urn:extension}", ""):
                for location in ("summary", "case", "definitions"):
                    with self.subTest(name=name, namespace=namespace, location=location):
                        root = native_fixture()
                        parent = (root.find(NAMESPACE + "ResultSummary") if location == "summary" else
                                  root.find(NAMESPACE + "Results")[0] if location == "case" else
                                  root.find(NAMESPACE + "TestDefinitions")[0])
                        ET.SubElement(parent, namespace + name)
                        self.reject("Native TRX contains nested result records", lambda: self.read(root))

    def test_only_native_summary_run_info_is_admitted(self) -> None:
        root = native_fixture()
        infos = ET.SubElement(root.find(NAMESPACE + "ResultSummary"), NAMESPACE + "RunInfos")
        info = ET.SubElement(infos, NAMESPACE + "RunInfo", outcome="Information")
        self.assertEqual(IDENTITIES, self.read(root))
        for outcome in ("Passed", "Completed", "Warning", "Error"):
            with self.subTest(outcome=outcome):
                info.set("outcome", outcome)
                self.reject("Native TRX contains failed or unsupported run-level result information",
                            lambda: self.read(root))
        for location in ("case", "definitions", "root"):
            with self.subTest(location=location):
                root = native_fixture()
                parent = (root.find(NAMESPACE + "Results")[0] if location == "case" else
                          root.find(NAMESPACE + "TestDefinitions")[0] if location == "definitions" else root)
                nested = ET.SubElement(parent, NAMESPACE + "RunInfos")
                ET.SubElement(nested, NAMESPACE + "RunInfo", outcome="Information")
                self.reject("Native TRX contains errors or unsupported run information", lambda: self.read(root))

    def test_error_info_outside_a_counted_result_is_rejected(self) -> None:
        for namespace in (NAMESPACE, "{urn:extension}", ""):
            for location in ("root", "definitions"):
                with self.subTest(namespace=namespace, location=location):
                    root = native_fixture()
                    parent = root if location == "root" else root.find(NAMESPACE + "TestDefinitions")[0]
                    ET.SubElement(parent, namespace + "ErrorInfo")
                    self.reject("Native TRX contains errors or unsupported run information", lambda: self.read(root))

    def test_foreign_alias_and_whitespace_discovery_are_rejected(self) -> None:
        variants = {
            "foreign": [*IDENTITIES, "Foreign.Namespace.OtherFact"],
            "alias": ["Alias." + IDENTITIES[0], *IDENTITIES[1:]],
            "whitespace": [IDENTITIES[0] + " ", *IDENTITIES[1:]],
        }
        for name, identities in variants.items():
            with self.subTest(name=name):
                self.assertEqual(IDENTITIES, RUNNER.compiled_inventory(discovery_fixture(IDENTITIES)))
                self.reject("Compiled discovery does not match the exact twenty-four-fact candidate roster",
                            lambda: RUNNER.compiled_inventory(discovery_fixture(identities)))


class EnvironmentControls(ExactRejection):
    def test_git_environment_removes_overrides_and_preserves_other_settings(self) -> None:
        supplied = {
            "GIT_DIR": "/foreign/repository", "git_work_tree": "/foreign/tree",
            "GIT_INDEX_FILE": "/foreign/index", "GIT_OBJECT_DIRECTORY": "/foreign/objects",
            "GIT_ALTERNATE_OBJECT_DIRECTORIES": "/foreign/alternate", "GIT_COMMON_DIR": "/foreign/common",
            "GIT_CONFIG_COUNT": "1", "GIT_CONFIG_KEY_0": "core.worktree", "GIT_CONFIG_VALUE_0": "/foreign/tree",
            "GIT_CONFIG_PARAMETERS": "'core.worktree=/foreign/tree'",
            "PATH": "synthetic-host-path", "DOTNET_CLI_UI_LANGUAGE": "en-US",
            "COGLATAS_CONTROL_SENTINEL": "retained", "GITHUB_ACTIONS": "true",
        }
        expected = {name: supplied[name] for name in
                    ("PATH", "DOTNET_CLI_UI_LANGUAGE", "COGLATAS_CONTROL_SENTINEL", "GITHUB_ACTIONS")}
        with patch.dict(os.environ, supplied, clear=True):
            original = dict(os.environ)
            self.assertEqual(expected, RUNNER.local_git_environment())
            self.assertEqual(original, dict(os.environ))


def setup_environment() -> dict[str, str]:
    # Independent of the function under test: inherited Git overrides must never
    # redirect fixture initialization or writes into a caller's real repository.
    return {key: value for key, value in os.environ.items() if not key.upper().startswith("GIT_")}


def fixture_git(repository: Path, *args: str, data: bytes | None = None) -> str:
    completed = subprocess.run(
        ["git", "-c", "core.autocrlf=false", "-c", "commit.gpgsign=false",
         "-c", "core.hooksPath=" + str(repository / "empty-hooks"),
         "-c", "user.name=Test discovery verifier controls", "-c", "user.email=test-discovery-controls@example.invalid", *args],
        cwd=repository, env=setup_environment(), input=data, capture_output=True, check=True)
    return completed.stdout.decode("utf-8").strip()


def file_snapshot(directory: Path) -> dict[str, bytes]:
    return {str(path.relative_to(directory)): path.read_bytes()
            for path in directory.rglob("*") if path.is_file()}


class WorkingTreeControls(ExactRejection):
    RAW = b"// Disposable source-binding fixture; not executable native evidence.\n"

    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="test-discovery-git-controls-")
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        self.repository, self.head, self.blob = self.make_repository("actual")
        product = patch.object(RUNNER, "PRODUCT_HEAD", self.head)
        product.start()
        self.addCleanup(product.stop)
        self.admit()

    def make_repository(self, name: str, product_path: str = "product.txt") -> tuple[Path, str, str]:
        repository = self.directory / name
        repository.mkdir()
        fixture_git(repository, "init", "--quiet", "--template=")
        (repository / product_path).write_text(name + "\n", encoding="utf-8")
        (repository / ".gitignore").write_text("**/data/\n**/bin/\n**/obj/\n**/node_modules/\nartifacts/\n", encoding="utf-8")
        project = repository / "tests/Coglatas.Tests/Coglatas.Tests.csproj"
        project.parent.mkdir(parents=True)
        project.write_text('<Project Sdk="Microsoft.NET.Sdk" />\n', encoding="utf-8")
        fixture_git(repository, "add", "--", product_path, ".gitignore", str(project.relative_to(repository)))
        fixture_git(repository, "commit", "--quiet", "-m", "Disposable product fixture")
        head = fixture_git(repository, "rev-parse", "HEAD")
        destination = repository / RUNNER.DESTINATION
        destination.parent.mkdir(parents=True)
        destination.write_bytes(self.RAW)
        fixture_git(repository, "add", "--", RUNNER.DESTINATION)
        return repository, head, fixture_git(repository, "rev-parse", ":" + RUNNER.DESTINATION)

    def admit(self) -> None:
        self.assertIsNone(RUNNER.verify_working_tree(self.repository, self.RAW, self.blob))

    def verify(self) -> None:
        RUNNER.verify_working_tree(self.repository, self.RAW, self.blob)

    def test_disposable_composition_has_expected_single_delta(self) -> None:
        self.assertEqual([RUNNER.DESTINATION], fixture_git(self.repository, "diff", "--name-only", "HEAD", "--").splitlines())
        self.admit()

    def test_changed_or_missing_working_source_is_rejected(self) -> None:
        destination = self.repository / RUNNER.DESTINATION
        for missing in (False, True):
            with self.subTest(missing=missing):
                self.admit()
                if missing:
                    destination.unlink()
                else:
                    destination.write_bytes(self.RAW + b"// different bytes\n")
                self.reject("Composed working-tree Test source changed from the committed blob", self.verify)
                destination.write_bytes(self.RAW)
                self.admit()

    def test_changed_index_identity_is_rejected(self) -> None:
        other_blob = fixture_git(self.repository, "hash-object", "-w", "--stdin", data=self.RAW + b"// other index\n")
        fixture_git(self.repository, "update-index", "--cacheinfo", "100644", other_blob, RUNNER.DESTINATION)
        self.reject("Composed candidate index identity changed", self.verify)
        fixture_git(self.repository, "add", "--", RUNNER.DESTINATION)
        self.admit()

    def test_mode_only_index_change_is_refused_even_when_worktree_mode_is_ignored(self) -> None:
        fixture_git(self.repository, "config", "core.filemode", "false")
        fixture_git(self.repository, "update-index", "--chmod=+x", "--", RUNNER.DESTINATION)
        self.assertEqual(self.blob, fixture_git(self.repository, "rev-parse", ":" + RUNNER.DESTINATION))
        self.assertEqual(self.RAW, (self.repository / RUNNER.DESTINATION).read_bytes())
        self.reject("Composed candidate index mode or stage changed", self.verify)
        fixture_git(self.repository, "update-index", "--chmod=-x", "--", RUNNER.DESTINATION)
        self.admit()

    def test_changed_head_is_rejected(self) -> None:
        product_tree = fixture_git(self.repository, "rev-parse", "HEAD^{tree}")
        changed_head = fixture_git(self.repository, "commit-tree", product_tree, "-p", self.head,
                                   "-m", "Different identity for the same disposable Product tree")
        fixture_git(self.repository, "update-ref", "HEAD", changed_head)
        self.assertNotEqual(self.head, fixture_git(self.repository, "rev-parse", "HEAD"))
        self.assertEqual(product_tree, fixture_git(self.repository, "rev-parse", "HEAD^{tree}"))
        self.assertEqual(self.blob, fixture_git(self.repository, "rev-parse", ":" + RUNNER.DESTINATION))
        self.assertEqual(self.RAW, (self.repository / RUNNER.DESTINATION).read_bytes())
        self.reject("Composed Product HEAD changed", self.verify)
        fixture_git(self.repository, "update-ref", "HEAD", self.head)
        self.admit()

    def test_unrelated_tracked_drift_is_rejected(self) -> None:
        tracked = self.repository / "product.txt"
        original = tracked.read_bytes()
        tracked.write_bytes(original + b"unexpected Product change\n")
        self.reject("Tracked Product source changed during independent composition", self.verify)
        tracked.write_bytes(original)
        self.admit()

    def reject_masked_product_edit(self, flag: str) -> None:
        tracked = self.repository / "product.txt"
        original = tracked.read_bytes()
        index_blob = fixture_git(self.repository, "rev-parse", ":product.txt")
        index_tree = fixture_git(self.repository, "write-tree")
        fixture_git(self.repository, "update-index", "--" + flag, "--", "product.txt")
        tracked.write_bytes(original + b"Hidden working-tree compiler input drift.\n")
        self.assertEqual(index_blob, fixture_git(self.repository, "rev-parse", ":product.txt"))
        self.assertEqual(index_tree, fixture_git(self.repository, "write-tree"))
        self.assertEqual("", fixture_git(self.repository, "diff", "--name-only", "--"))
        self.reject("Composed Git index flags can hide tracked working-tree edits", self.verify)
        tracked.write_bytes(original)
        fixture_git(self.repository, "update-index", "--no-" + flag, "--", "product.txt")
        self.admit()

    def test_assume_unchanged_cannot_mask_product_working_bytes(self) -> None:
        self.reject_masked_product_edit("assume-unchanged")

    def test_skip_worktree_cannot_mask_product_working_bytes(self) -> None:
        self.reject_masked_product_edit("skip-worktree")

    def test_index_flag_scan_preserves_raw_nul_path_boundaries(self) -> None:
        # A pathname may itself look like the next status line. Windows forbids
        # control characters, so its positive retains spaces and a non-ASCII name.
        relative = "Product space \u03a9" + ("\nS apparent flag\n" if os.name != "nt" else " S apparent flag ") + ".txt"
        repository, head, blob = self.make_repository("unusual-path", relative)
        original = (repository / relative).read_bytes()
        with patch.object(RUNNER, "PRODUCT_HEAD", head):
            self.assertIsNone(RUNNER.verify_working_tree(repository, self.RAW, blob))
            fixture_git(repository, "update-index", "--assume-unchanged", "--", relative)
            (repository / relative).write_bytes(original + b"Masked edit with an unusual pathname.\n")
            self.assertEqual("", fixture_git(repository, "diff", "--name-only", "--"))
            self.reject("Composed Git index flags can hide tracked working-tree edits",
                        lambda: RUNNER.verify_working_tree(repository, self.RAW, blob))
            (repository / relative).write_bytes(original)
            fixture_git(repository, "update-index", "--no-assume-unchanged", "--", relative)
            self.assertIsNone(RUNNER.verify_working_tree(repository, self.RAW, blob))

    def test_index_only_product_drift_is_rejected_when_net_diff_hides_it(self) -> None:
        tracked = self.repository / "product.txt"
        original = tracked.read_bytes()
        tracked.write_bytes(original + b"unexpected staged-only Product change\n")
        fixture_git(self.repository, "add", "--", "product.txt")
        tracked.write_bytes(original)
        # The old net HEAD-to-working-tree comparison missed this changed index.
        self.assertEqual([RUNNER.DESTINATION],
                         fixture_git(self.repository, "diff", "--name-only", "HEAD", "--").splitlines())
        self.assertEqual(sorted([RUNNER.DESTINATION, "product.txt"]),
                         fixture_git(self.repository, "diff", "--cached", "--name-only", "HEAD", "--").splitlines())
        self.reject("Tracked Product source changed during independent composition", self.verify)
        fixture_git(self.repository, "add", "--", "product.txt")
        self.admit()

    def test_untracked_compile_input_is_rejected_and_removal_readmits(self) -> None:
        relative = "tests/Coglatas.Tests/Injected.cs"
        injected = self.repository / relative
        injected.write_text("// Unexpected untracked compiler input.\n", encoding="utf-8")
        self.assertEqual([RUNNER.DESTINATION],
                         fixture_git(self.repository, "diff", "--name-only", "HEAD", "--").splitlines())
        self.assertEqual([relative],
                         fixture_git(self.repository, "ls-files", "--others", "--exclude-standard").splitlines())
        self.reject("Unexpected untracked source appeared during independent composition", self.verify)
        injected.unlink()
        self.admit()

    def test_hostile_git_dir_redirects_only_unprotected_read(self) -> None:
        foreign, foreign_head, _ = self.make_repository("foreign")
        self.assertNotEqual(self.head, foreign_head)
        originals = file_snapshot(self.directory)
        hostile = {**setup_environment(), "GIT_DIR": str(foreign / ".git")}
        with patch.dict(os.environ, hostile, clear=True):
            # Deliberately omit sanitization only for this read-only counterexample.
            unprotected = subprocess.run(["git", "rev-parse", "HEAD"], cwd=self.repository,
                                         capture_output=True, text=True, check=True).stdout.strip()
            self.assertEqual(foreign_head, unprotected)
            self.assertEqual(self.head, RUNNER.git(self.repository, "rev-parse", "HEAD"))
            self.admit()
        self.assertEqual(originals, file_snapshot(self.directory))

    def test_ignored_compile_inputs_are_rejected_before_and_after_restore(self) -> None:
        for relative in (
            "tests/Coglatas.Tests/data/Injected.cs",
            "tests/Coglatas.Tests/ProjectIde/obj/Injected.cs",
            "tests/Coglatas.Tests/node_modules/Injected.cs",
            "artifacts/Injected.cs",
        ):
            with self.subTest(path=relative):
                injected = self.repository / relative
                injected.parent.mkdir(parents=True, exist_ok=True)
                injected.write_text("// Ignored file still inside potential compile inputs.\n", encoding="utf-8")
                self.assertEqual("", fixture_git(self.repository, "ls-files", "--others", "--exclude-standard"))
                for generated in (False, True):
                    self.reject("Unexpected untracked source appeared during independent composition",
                                lambda: RUNNER.verify_working_tree(self.repository, self.RAW, self.blob,
                                                                   allow_generated=generated))
                injected.unlink()
                self.admit()

    def test_known_sdk_outputs_are_admitted_only_after_restore(self) -> None:
        for relative in ("tests/Coglatas.Tests/bin/Release/net10.0/Coglatas.Tests.dll",
                         "tests/Coglatas.Tests/obj/Release/net10.0/Generated.AssemblyInfo.cs"):
            with self.subTest(path=relative):
                generated = self.repository / relative
                generated.parent.mkdir(parents=True, exist_ok=True)
                generated.write_bytes(b"disposable generated output\n")
                self.reject("Unexpected untracked source appeared during independent composition", self.verify)
                self.assertIsNone(RUNNER.verify_working_tree(self.repository, self.RAW, self.blob,
                                                            allow_generated=True))
                generated.unlink()
                self.admit()


class ProcessEvidenceControls(ExactRejection):
    """Malformed synthetic reader inputs confer no child-process or native credit."""
    CLI = "a" * 64
    DOMAIN = "b" * 64
    EVALUATOR = "c" * 64

    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="test-discovery-process-controls-")
        self.addCleanup(temporary.cleanup)
        self.report = Path(temporary.name) / "synthetic-processes.jsonl"
        self.rows = []
        input_bytes = {name: ("Synthetic " + name + " reader-control input.\n").encode("utf-8")
                       for name in ("source.json", "input.json")}
        for (case, label), exit_code in RUNNER.PROCESS_CASES.items():
            evaluator, existing = label == "evaluator", label.startswith("existing-")
            output = None if evaluator else label.removeprefix("existing-") if existing else "run.json" if label == "counterexample-execution" else "inventory.json"
            arguments = ["--evaluator"] if evaluator else ["source.json", "input.json", output]
            if not evaluator and label != "counterexample-execution":
                arguments.insert(0, "--discover")
            raw = input_bytes.get(output, b"Synthetic reader-control bytes; never actual process output.\n")
            digest = hashlib.sha256(raw).hexdigest()
            self.rows.append({
                "caseId": case, "invocation": label, "ExitCode": exit_code, "processId": 1,
                "startedUtc": "2026-11-18T00:00:00+00:00", "completedUtc": "2026-11-18T00:00:01+00:00",
                "executable": "dotnet", "cliPath": "tools/ProjectIde.Tests/bin/Release/net10.0/ProjectIde.Tests.dll",
                "cliSha256Before": self.CLI, "cliSha256After": self.CLI,
                "domainSha256Before": self.DOMAIN, "domainSha256After": self.DOMAIN,
                "arguments": arguments, "outputName": output,
                "inputs": [] if evaluator else [{"name": name, "length": len(data),
                                                 "sha256Before": hashlib.sha256(data).hexdigest(),
                                                 "sha256After": hashlib.sha256(data).hexdigest()}
                                                for name, data in input_bytes.items()],
                "Stdout": json.dumps({"artifactId": "coglatas.local-project-test-evaluator", "version": "synthetic-control",
                                       "digest": {"algorithm": "sha-256", "value": self.EVALUATOR}}) if evaluator else "",
                "Stderr": "", "outputExisted": existing,
                "outputSha256Before": digest if existing else None,
                "outputSha256After": None if evaluator else digest,
                "outputBytes": None if evaluator else base64.b64encode(raw).decode("ascii"),
            })
        self.assertEqual(35, len(self.read(self.rows)))

    def read(self, rows) -> list[str]:
        self.report.write_text("".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8")
        return RUNNER.process_inventory(self.report, self.CLI, self.DOMAIN, self.EVALUATOR)

    def test_complete_synthetic_process_shape_is_a_reader_control_only(self) -> None:
        self.assertEqual(sorted(case + ":" + label for case, label in RUNNER.PROCESS_CASES), self.read(self.rows))

    def test_missing_duplicate_or_substituted_process_identity_is_refused(self) -> None:
        for kind in ("missing", "duplicate", "substitute"):
            rows = copy.deepcopy(self.rows)
            if kind == "missing": rows.pop()
            elif kind == "duplicate": rows[-1] = copy.deepcopy(rows[0])
            else: rows[-1]["caseId"] = "Foreign.Test/default"
            self.reject("Actual CLI evidence must match the exact thirty-five-process roster", lambda: self.read(rows))

    def test_exit_code_and_real_process_metadata_refuse_false_success(self) -> None:
        for field, value in (("ExitCode", 0), ("processId", 0), ("executable", "reflection-entrypoint")):
            rows = copy.deepcopy(self.rows)
            row = next(row for row in rows if row["invocation"] == "invalid-and-mismatched-evaluator")
            row[field] = value
            self.reject("Actual CLI invocation identity or exit result is invalid", lambda: self.read(rows))
        rows = copy.deepcopy(self.rows); rows[0]["completedUtc"] = "2026-11-17T23:59:59+00:00"
        self.reject("Actual CLI execution timestamps are invalid", lambda: self.read(rows))

    def test_changed_binary_or_evaluator_digest_is_refused(self) -> None:
        for field in ("cliSha256Before", "cliSha256After", "domainSha256Before", "domainSha256After"):
            rows = copy.deepcopy(self.rows); rows[0][field] = "d" * 64
            self.reject("Actual CLI evidence differs from the built binary identities", lambda: self.read(rows))
        rows = copy.deepcopy(self.rows); identity = json.loads(rows[0]["Stdout"]); identity["digest"]["value"] = "e" * 64
        rows[0]["Stdout"] = json.dumps(identity)
        self.reject("Actual evaluator identity is not bound to the compiled Domain binary", lambda: self.read(rows))

    def test_source_output_and_create_new_drift_are_refused(self) -> None:
        rows = copy.deepcopy(self.rows); rows[1]["inputs"][0]["sha256After"] = "d" * 64
        self.reject("Actual CLI changed its retained input bytes", lambda: self.read(rows))
        rows = copy.deepcopy(self.rows); rows[1]["outputBytes"] = base64.b64encode(b"other bytes").decode("ascii")
        self.reject("Actual CLI output bytes disagree with their retained digest", lambda: self.read(rows))
        rows = copy.deepcopy(self.rows); row = next(row for row in rows if row["invocation"] == "existing-source.json")
        row["outputSha256Before"] = "d" * 64
        self.reject("Actual CLI overwrote an existing output or input", lambda: self.read(rows))
        rows = copy.deepcopy(self.rows); rows[1]["outputExisted"] = True
        self.reject("Fresh CLI output is not proven to have used CreateNew", lambda: self.read(rows))

    def test_input_alias_output_must_match_the_same_input_bytes(self) -> None:
        for name in ("source.json", "input.json"):
            with self.subTest(name=name):
                self.assertEqual(35, len(self.read(self.rows)))
                rows = copy.deepcopy(self.rows)
                row = next(row for row in rows if row["invocation"] == "existing-" + name)
                matching_input = next(item for item in row["inputs"] if item["name"] == name)
                raw = b"Different, internally consistent output receipt bytes.\n"
                digest = hashlib.sha256(raw).hexdigest()
                self.assertNotEqual(matching_input["sha256Before"], digest)
                row.update(outputBytes=base64.b64encode(raw).decode("ascii"),
                           outputSha256Before=digest, outputSha256After=digest)
                self.reject("Actual CLI input-alias output differs from its matching input receipt",
                            lambda: self.read(rows))

    def test_wrong_arguments_or_duplicate_json_fields_cannot_alias_a_valid_invocation(self) -> None:
        rows = copy.deepcopy(self.rows); rows[1]["arguments"][0] = "--export-test"
        self.reject("Actual CLI arguments do not match the intended process boundary", lambda: self.read(rows))
        self.read(self.rows)
        text = self.report.read_text(encoding="utf-8"); self.report.write_text(text.replace('"ExitCode": 0', '"ExitCode": 3, "ExitCode": 0', 1), encoding="utf-8")
        self.reject("Duplicate JSON field in actual process evidence",
                    lambda: RUNNER.process_inventory(self.report, self.CLI, self.DOMAIN, self.EVALUATOR))


if __name__ == "__main__":
    unittest.main(verbosity=2)
