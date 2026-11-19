"""Portable verifier controls; synthetic reports and disposable Git, never native C# evidence."""
from __future__ import annotations

import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import unittest
from unittest.mock import patch
from uuid import UUID
import xml.etree.ElementTree as ET


RUNNER_PATH = Path(__file__).with_name("compose_and_verify.py")
SPEC = importlib.util.spec_from_file_location("history_candidate_runner", RUNNER_PATH)
assert SPEC is not None and SPEC.loader is not None
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)
NAMESPACE = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
IDENTITIES = sorted(RUNNER.EXPECTED_IDENTITIES)


def native_fixture() -> ET.Element:
    root = ET.Element(NAMESPACE + "TestRun")
    summary = ET.SubElement(root, NAMESPACE + "ResultSummary", outcome="Completed")
    ET.SubElement(summary, NAMESPACE + "Counters", total="20", executed="20",
                  passed="20", failed="0", notExecuted="0")
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
        temporary = tempfile.TemporaryDirectory(prefix="history-native-controls-")
        self.addCleanup(temporary.cleanup)
        self.report = Path(temporary.name) / "synthetic.trx"
        self.assertEqual(20, len(IDENTITIES))
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
        for field, value in (("total", "19"), ("passed", "19"), ("notExecuted", "1"), ("inconclusive", "1")):
            with self.subTest(field=field):
                root = native_fixture()
                self.assertEqual(IDENTITIES, self.read(root))
                root.find(NAMESPACE + "ResultSummary/" + NAMESPACE + "Counters").set(field, value)
                self.reject("Native TRX must contain exactly twenty complete passing candidate facts", lambda: self.read(root))

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

    def test_information_run_message_location_must_be_the_native_summary(self) -> None:
        root = native_fixture()
        infos = ET.SubElement(root.find(NAMESPACE + "ResultSummary"), NAMESPACE + "RunInfos")
        ET.SubElement(infos, NAMESPACE + "RunInfo", outcome="Information")
        self.assertEqual(IDENTITIES, self.read(root))
        for location in ("root", "case", "definition", "direct-summary", "foreign-summary", "empty-case"):
            with self.subTest(location=location):
                root = native_fixture()
                summary = root.find(NAMESPACE + "ResultSummary")
                if location == "direct-summary":
                    ET.SubElement(summary, NAMESPACE + "RunInfo", outcome="Information")
                else:
                    parent = {"root": root, "case": root.find(NAMESPACE + "Results")[0],
                              "definition": root.find(NAMESPACE + "TestDefinitions")[0],
                              "foreign-summary": summary,
                              "empty-case": root.find(NAMESPACE + "Results")[0]}[location]
                    namespace = "{urn:untrusted}" if location == "foreign-summary" else NAMESPACE
                    container = ET.SubElement(parent, namespace + "RunInfos")
                    if location != "empty-case":
                        ET.SubElement(container, namespace + "RunInfo", outcome="Information")
                self.reject("Native TRX contains run-level errors or incomplete outcomes", lambda: self.read(root))

    def test_duplicate_or_unknown_native_run_infos_are_rejected(self) -> None:
        for kind in ("duplicate", "unexpected-child"):
            with self.subTest(kind=kind):
                root = native_fixture()
                summary = root.find(NAMESPACE + "ResultSummary")
                infos = ET.SubElement(summary, NAMESPACE + "RunInfos")
                ET.SubElement(infos, NAMESPACE + "RunInfo", outcome="Information")
                if kind == "duplicate":
                    other = ET.SubElement(summary, NAMESPACE + "RunInfos")
                    ET.SubElement(other, NAMESPACE + "RunInfo", outcome="Information")
                else:
                    ET.SubElement(infos, "{urn:untrusted}Unexpected")
                self.reject("Native TRX contains run-level errors or incomplete outcomes", lambda: self.read(root))

    def test_foreign_alias_and_whitespace_discovery_are_rejected(self) -> None:
        variants = {
            "foreign": [*IDENTITIES, "Foreign.Namespace.OtherFact"],
            "alias": ["Alias." + IDENTITIES[0], *IDENTITIES[1:]],
            "whitespace": [IDENTITIES[0] + " ", *IDENTITIES[1:]],
        }
        for name, identities in variants.items():
            with self.subTest(name=name):
                self.assertEqual(IDENTITIES, RUNNER.compiled_inventory(discovery_fixture(IDENTITIES)))
                self.reject("Compiled discovery does not match the exact twenty-fact candidate roster",
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
         "-c", "user.name=History verifier controls", "-c", "user.email=history-controls@example.invalid", *args],
        cwd=repository, env=setup_environment(), input=data, capture_output=True, check=True)
    return completed.stdout.decode("utf-8").strip()


def file_snapshot(directory: Path) -> dict[str, bytes]:
    return {str(path.relative_to(directory)): path.read_bytes()
            for path in directory.rglob("*") if path.is_file()}


class WorkingTreeControls(ExactRejection):
    RAW = b"// Disposable source-binding fixture; not executable native evidence.\n"

    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="history-git-controls-")
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        self.repository, self.head, self.blob = self.make_repository("actual")
        product = patch.object(RUNNER, "PRODUCT_HEAD", self.head)
        product.start()
        self.addCleanup(product.stop)
        self.admit()

    def make_repository(self, name: str) -> tuple[Path, str, str]:
        repository = self.directory / name
        repository.mkdir()
        fixture_git(repository, "init", "--quiet", "--template=")
        (repository / "product.txt").write_text(name + "\n", encoding="utf-8")
        fixture_git(repository, "add", "-f", "--", "product.txt")
        fixture_git(repository, "commit", "--quiet", "-m", "Disposable product fixture")
        head = fixture_git(repository, "rev-parse", "HEAD")
        destination = repository / RUNNER.DESTINATION
        destination.parent.mkdir(parents=True)
        destination.write_bytes(self.RAW)
        fixture_git(repository, "add", "-f", "--", RUNNER.DESTINATION)
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
        fixture_git(self.repository, "add", "-f", "--", RUNNER.DESTINATION)
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


class NestedEvidenceControls(unittest.TestCase):
    """Malformed copies retain the original outer counters and identity roster."""

    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="nested-native-controls-")
        self.addCleanup(temporary.cleanup)
        self.path = Path(temporary.name) / "copied.trx"
        self.assertEqual(sorted(RUNNER.EXPECTED_IDENTITIES), self.read(self.original()))

    def original(self) -> ET.Element:
        return native_fixture()

    def read(self, root: ET.Element) -> list[str]:
        ET.ElementTree(root).write(self.path, encoding="utf-8", xml_declaration=True)
        return RUNNER.native_inventory(self.path)

    def test_nested_results_refuse_passed_and_unexecuted_copies(self) -> None:
        for kind in ("UnitTestResult", "GenericTestResult"):
            for outcome in ("Passed", "NotExecuted"):
                for location in ("InnerResults", "Output", "case", "summary", "definition"):
                    for namespace in (NAMESPACE, "{urn:foreign-trx}"):
                        with self.subTest(kind=kind, outcome=outcome, location=location, namespace=namespace):
                            root = self.original()
                            case = root.find(NAMESPACE + "Results")[0]
                            if location in ("InnerResults", "Output"):
                                parent = ET.SubElement(case, namespace + location)
                            elif location == "case":
                                parent = case
                            elif location == "summary":
                                parent = root.find(NAMESPACE + "ResultSummary")
                            else:
                                parent = root.find(NAMESPACE + "TestDefinitions")[0]
                            ET.SubElement(parent, namespace + kind, outcome=outcome,
                                          testName="Unreviewed.InnerFact", testId="inner", executionId="inner")
                            with self.assertRaisesRegex(ValueError, "^Native TRX contains nested result records$"):
                                self.read(root)

    def test_empty_aggregation_records_cannot_hide_behind_the_direct_roster(self) -> None:
        for outcome in (None, "Failed", "Passed", "NotExecuted"):
            for location in ("output", "summary", "definition"):
                for namespace in (NAMESPACE, "{urn:foreign-trx}"):
                    with self.subTest(outcome=outcome, location=location, namespace=namespace):
                        root = self.original()
                        if location == "output":
                            parent = ET.SubElement(root.find(NAMESPACE + "Results")[0], NAMESPACE + "Output")
                        elif location == "summary":
                            parent = root.find(NAMESPACE + "ResultSummary")
                        else:
                            parent = root.find(NAMESPACE + "TestDefinitions")[0]
                        aggregation = ET.SubElement(parent, namespace + "TestResultAggregation")
                        if outcome is not None:
                            aggregation.set("outcome", outcome)
                        self.assertEqual([], list(aggregation))
                        with self.assertRaisesRegex(ValueError, "^Native TRX contains nested result records$"):
                            self.read(root)

    def test_run_level_errors_cannot_hide_behind_complete_passing_counters(self) -> None:
        for kind in ("ErrorInfo", "RunInfo"):
            with self.subTest(kind=kind):
                root = self.original()
                summary = root.find(NAMESPACE + "ResultSummary")
                if kind == "ErrorInfo":
                    output = ET.SubElement(summary, NAMESPACE + "Output")
                    ET.SubElement(ET.SubElement(output, NAMESPACE + "ErrorInfo"),
                                  NAMESPACE + "Message").text = "Unreviewed run-level failure"
                else:
                    ET.SubElement(ET.SubElement(summary, NAMESPACE + "RunInfos"),
                                  NAMESPACE + "RunInfo", outcome="Error")
                with self.assertRaisesRegex(ValueError, "^Native TRX contains run-level errors or incomplete outcomes$"):
                    self.read(root)

    def test_only_information_run_messages_satisfy_the_admission_policy(self) -> None:
        for outcome in ("Information", "Warning", "Passed", "Completed", "Error", "Failed", None):
            with self.subTest(outcome=outcome):
                root = self.original()
                infos = ET.SubElement(root.find(NAMESPACE + "ResultSummary"), NAMESPACE + "RunInfos")
                info = ET.SubElement(infos, NAMESPACE + "RunInfo")
                if outcome is not None:
                    info.set("outcome", outcome)
                if outcome == "Information":
                    self.assertEqual(sorted(RUNNER.EXPECTED_IDENTITIES), self.read(root))
                else:
                    with self.assertRaisesRegex(ValueError, "^Native TRX contains run-level errors or incomplete outcomes$"):
                        self.read(root)


class InputAdmissionControls(unittest.TestCase):
    """Real disposable Git records; no fixture is native Product execution."""

    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="source-input-controls-")
        self.addCleanup(temporary.cleanup)
        self.repository = Path(temporary.name)
        self.git("init", "--quiet", "--template=")
        self.path = self.repository / "product input ü.txt"
        self.path.write_bytes(b"Original tracked input.\n")
        project = self.repository / "src/Control/Control.csproj"
        project.parent.mkdir(parents=True)
        project.write_text('<Project Sdk="Microsoft.NET.Sdk" />\n', encoding="utf-8")
        self.git("add", "-f", "--", "product input ü.txt", "src/Control/Control.csproj")
        self.git("commit", "--quiet", "-m", "Disposable source admission fixture")
        self.product = self.git("rev-parse", "HEAD").decode().strip()
        self.verify()

    def git(self, *args: str) -> bytes:
        environment = {key: value for key, value in os.environ.items()
                       if not key.upper().startswith("GIT_")}
        result = subprocess.run(
            ["git", "-c", "core.autocrlf=false", "-c", "commit.gpgsign=false",
             "-c", "core.hooksPath=" + str(self.repository / "empty-hooks"),
             "-c", "user.name=Source admission controls",
             "-c", "user.email=source-controls@example.invalid", *args],
            cwd=self.repository, env=environment, capture_output=True, check=True)
        return result.stdout

    def verify(self, *, generated: bool = False) -> None:
        RUNNER.verify_product_inputs(self.repository, self.product, allow_generated=generated)

    def reject(self, reason: str, *, generated: bool = False) -> None:
        with self.assertRaises(ValueError) as caught:
            self.verify(generated=generated)
        self.assertEqual(reason, str(caught.exception))

    def test_staged_rename_cannot_hide_deleted_product_input(self) -> None:
        relative = self.path.relative_to(self.repository).as_posix()
        raw = self.path.read_bytes()
        blob = self.git("rev-parse", "HEAD:" + relative).decode().strip()
        (self.repository / RUNNER.DESTINATION).parent.mkdir(parents=True, exist_ok=True)
        self.git("config", "diff.renames", "true")
        self.git("mv", "--", relative, RUNNER.DESTINATION)
        self.assertEqual([RUNNER.DESTINATION.encode("utf-8")], RUNNER.git_records(
            self.repository, "diff", "--cached", "--name-only", "-z", "HEAD", "--"))
        self.assertEqual({os.fsencode(relative), RUNNER.DESTINATION.encode("utf-8")}, set(RUNNER.git_records(
            self.repository, "diff", "--cached", "--no-renames", "--name-only", "-z", "HEAD", "--")))
        with patch.object(RUNNER, "PRODUCT_HEAD", self.product):
            with self.assertRaisesRegex(ValueError, "^Tracked Product source changed during independent composition$"):
                RUNNER.verify_working_tree(self.repository, raw, blob)

    def test_both_index_masks_refuse_changed_bytes_with_unchanged_index(self) -> None:
        relative = self.path.relative_to(self.repository).as_posix()
        original = self.path.read_bytes()
        original_blob = self.git("rev-parse", ":" + relative)
        for flag, undo in (("--assume-unchanged", "--no-assume-unchanged"),
                           ("--skip-worktree", "--no-skip-worktree")):
            with self.subTest(flag=flag):
                self.verify()
                self.git("update-index", flag, "--", relative)
                self.path.write_bytes(original + b"Masked unreviewed source.\n")
                self.assertEqual(original_blob, self.git("rev-parse", ":" + relative))
                self.assertEqual(b"", self.git("diff", "--name-only", "--"))
                self.reject("Git index flags hide tracked Product source")
                self.path.write_bytes(original)
                self.git("update-index", undo, "--", relative)
                self.verify()

    def test_index_masks_refuse_even_before_the_file_changes(self) -> None:
        relative = self.path.relative_to(self.repository).as_posix()
        self.git("update-index", "--skip-worktree", "--", relative)
        self.reject("Git index flags hide tracked Product source")
        self.git("update-index", "--no-skip-worktree", "--", relative)
        self.verify()

    def test_raw_path_records_preserve_unicode_spaces_and_posix_newlines(self) -> None:
        names = ["product input ü.txt"]
        if os.name != "nt":
            names.append("line\nbreak.txt")
            (self.repository / names[-1]).write_bytes(b"Raw path fixture.\n")
            self.git("add", "-f", "--", names[-1])
        for name in names:
            with self.subTest(name=repr(name)):
                self.assertIn(b"H " + os.fsencode(name),
                              RUNNER.git_records(self.repository, "ls-files", "-v", "-z", "--"))
                self.git("update-index", "--assume-unchanged", "--", name)
                self.reject("Git index flags hide tracked Product source")
                self.git("update-index", "--no-assume-unchanged", "--", name)
                self.verify()

    def test_untracked_and_ignored_source_refuse_before_and_after_restore(self) -> None:
        injected = self.repository / "src/Control/Unreviewed.cs"
        for ignored in (False, True):
            with self.subTest(ignored=ignored):
                if ignored:
                    (self.repository / ".git/info").mkdir(exist_ok=True)
                    (self.repository / ".git/info/exclude").write_text("*.cs\n", encoding="utf-8")
                injected.write_text("// Unreviewed compiler input.\n", encoding="utf-8")
                if ignored:
                    self.assertEqual(b"src/Control/Unreviewed.cs\n",
                                     self.git("check-ignore", "--", "src/Control/Unreviewed.cs"))
                    self.assertEqual(b"", self.git("ls-files", "--others", "--exclude-standard"))
                for generated in (False, True):
                    self.reject("Unexpected untracked source appeared during independent composition",
                                generated=generated)
                injected.unlink()
                self.verify()

    def test_only_registered_sdk_output_roots_are_admitted_after_restore(self) -> None:
        output = self.repository / "src/Control/obj/project.assets.json"
        output.parent.mkdir(parents=True)
        output.write_text("{}\n", encoding="utf-8")
        self.reject("Unexpected untracked source appeared during independent composition")
        self.verify(generated=True)
        for name in ("src/Control/nested/obj/Unreviewed.cs", "unregistered/bin/Unreviewed.cs",
                     "src/Control/bin-extra/Unreviewed.cs"):
            with self.subTest(path=name):
                injected = self.repository / name
                injected.parent.mkdir(parents=True, exist_ok=True)
                injected.write_text("// A directory name is not SDK provenance.\n", encoding="utf-8")
                self.reject("Unexpected untracked source appeared during independent composition", generated=True)
                injected.unlink()
                self.verify(generated=True)


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
            (product / "global.json").write_text(
                json.dumps({"sdk": {"version": "10.0.403", "rollForward": "disable", "allowPrerelease": False}}) + "\n",
                encoding="utf-8")
            selected_sdk = subprocess.run(["dotnet", "--version"], cwd=product,
                                          capture_output=True, text=True, timeout=30)
            self.assertEqual(0, selected_sdk.returncode, selected_sdk.stdout + selected_sdk.stderr)
            self.assertEqual("10.0.403", selected_sdk.stdout.strip())
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


class WorkflowExecutionControls(unittest.TestCase):
    """Run each real workflow admission block against distinct script canaries."""

    SCRIPTS = ("tests/candidates/projectide-history/compose_and_verify.py",
               "tests/candidates/projectide-history/test_compose_and_verify.py")
    SCRIPT_PINS = {SCRIPTS[0]: "RUNNER_BLOB", SCRIPTS[1]: "CONTROLS_BLOB"}

    def setUp(self) -> None:
        if shutil.which("pwsh") is None:
            if os.environ.get("GITHUB_ACTIONS") == "true":
                self.fail("Hosted workflow execution controls require PowerShell")
            self.skipTest("PowerShell is unavailable; hosted execution controls remain required")
        temporary = tempfile.TemporaryDirectory(prefix="workflow-execution-controls-")
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        self.repository = self.directory / "repository"
        self.repository.mkdir()
        self.git("init", "--quiet", "--template=")
        self.canaries = {}
        for relative in self.SCRIPTS:
            raw = ("import json, os, sys\nfrom pathlib import Path\n"
                   "Path(os.environ['CONTROL_OUTPUT']).write_text(json.dumps("
                   "{'isolated': sys.flags.isolated, 'script': " + repr(relative) + "}))\n").encode("utf-8")
            self.canaries[relative] = raw
            target = self.repository / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(raw)
        self.git("add", "-f", "--", *self.SCRIPTS)
        self.git("commit", "--quiet", "-m", "Disposable distinct Python execution canaries")
        self.head = self.git("rev-parse", "HEAD").strip()
        self.blobs = {relative: self.git("rev-parse", "HEAD:" + relative).strip() for relative in self.SCRIPTS}
        self.assertEqual(3, len({self.head, *self.blobs.values()}))
        workflow = RUNNER.ROOT / ".github/workflows/projectide-history-advisory.yml"
        lines = workflow.read_text(encoding="utf-8").splitlines()
        launches = [(index, line) for index, line in enumerate(lines)
                    if line.strip().startswith("python -I -B tests/candidates/projectide-history/")]
        self.blocks = []
        for index, line in launches:
            executable = line.strip().split()[3]
            self.assertIn(executable, self.SCRIPTS)
            required = self.SCRIPTS if executable == self.SCRIPTS[1] else (self.SCRIPTS[0],)
            first = index
            while first and lines[first - 1].strip().startswith(("$committed =", "$working =", "if ($LASTEXITCODE")):
                first -= 1
            block = "\n".join(item[10:] for item in lines[first:index + 1])
            committed = tuple(sorted(re.findall(r'git rev-parse "\$\(\$env:TEST_HEAD\):([^"]+)"', block)))
            working = tuple(sorted(re.findall(r"git hash-object --no-filters '([^']+)'", block)))
            self.assertEqual(tuple(sorted(required)), committed, "Launch block omitted a required committed script binding")
            self.assertEqual(tuple(sorted(required)), working, "Launch block omitted a required working script binding")
            self.blocks.append((block, executable, required))
        self.assertEqual(4, len(self.blocks), "Both controls and runners on both hosts must be exercised")
        for relative in self.SCRIPTS:
            self.assertEqual(2, sum(executable == relative for _, executable, _ in self.blocks))

    def git(self, *args: str) -> str:
        environment = {key: value for key, value in os.environ.items()
                       if not key.upper().startswith("GIT_")}
        result = subprocess.run(
            ["git", "-c", "core.autocrlf=false", "-c", "commit.gpgsign=false",
             "-c", "core.hooksPath=" + str(self.repository / "empty-hooks"),
             "-c", "user.name=Workflow execution controls",
             "-c", "user.email=execution-controls@example.invalid", *args],
            cwd=self.repository, env=environment, capture_output=True, text=True, check=True)
        return result.stdout

    def execute(self, block: str) -> tuple[subprocess.CompletedProcess, Path]:
        marker = self.directory / "executed.json"
        marker.unlink(missing_ok=True)
        script = self.directory / "execute.ps1"
        script.write_text("$ErrorActionPreference = 'Stop'\n"
                          + "$log = Join-Path $env:RUNNER_TEMP 'controlled-output.log'\n"
                          + block + "\nif ($LASTEXITCODE -ne 0) { throw 'Canary execution failed' }\n",
                          encoding="utf-8")
        environment = {key: value for key, value in os.environ.items()
                       if not key.upper().startswith("GIT_")}
        environment.update(TEST_HEAD=self.head, RUNNER_TEMP=str(self.directory), CONTROL_OUTPUT=str(marker),
                           PYTHONPATH=str(self.repository / Path(self.SCRIPTS[0]).parent))
        environment.update({self.SCRIPT_PINS[relative]: blob for relative, blob in self.blobs.items()})
        result = subprocess.run(["pwsh", "-NoProfile", "-NonInteractive", "-File", str(script)],
                                cwd=self.repository, env=environment, capture_output=True,
                                text=True, timeout=60)
        return result, marker

    def test_actual_blocks_use_isolated_python_despite_import_shadow(self) -> None:
        shadow = self.repository / Path(self.SCRIPTS[0]).parent / "json.py"
        shadow.write_text("raise RuntimeError('UNREVIEWED_IMPORT_SHADOW')\n", encoding="utf-8")
        for number, (block, executable, _) in enumerate(self.blocks):
            with self.subTest(block=number, executable=executable):
                result, marker = self.execute(block)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertEqual({"isolated": 1, "script": executable}, json.loads(marker.read_text(encoding="utf-8")))

    def test_changed_execution_bytes_are_rejected_before_canary_runs(self) -> None:
        for number, (block, executable, required) in enumerate(self.blocks):
            for relative in required:
                with self.subTest(block=number, script=relative):
                    target = self.repository / relative
                    target.write_bytes(self.canaries[relative] + b"# Unreviewed execution bytes.\n")
                    result, marker = self.execute(block)
                    self.assertNotEqual(0, result.returncode)
                    self.assertIn("Working execution script identity mismatch", result.stdout + result.stderr)
                    self.assertFalse(marker.exists())
                    target.write_bytes(self.canaries[relative])
                    result, marker = self.execute(block)
                    self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                    self.assertEqual({"isolated": 1, "script": executable}, json.loads(marker.read_text(encoding="utf-8")))


if __name__ == "__main__":
    unittest.main(verbosity=2)
