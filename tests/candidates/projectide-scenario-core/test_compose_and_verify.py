#!/usr/bin/env python3
"""Retained-report/Git admission and disposable SDK controls; no fresh Product cases."""
from __future__ import annotations

import argparse
from contextlib import redirect_stdout
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import shutil
from uuid import UUID
import xml.etree.ElementTree as ET
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("scenario_core_candidate_runner", HERE / "compose_and_verify.py")
assert SPEC is not None and SPEC.loader is not None
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)
SOURCE_BYTES = (HERE / Path(RUNNER.SOURCE).name).read_bytes()
SOURCE_BLOB = "abdd8eb8d2975567602d22d19f969458587b3a04"
EXPECTED_COUNT = 20
PASS_STATUS = "SYNTHETIC_SCENARIO_CORE_PASSED"
NATIVE_TRX_BYTES = b""
NATIVE_DISCOVERY = ""


def fixture_environment() -> dict[str, str]:
    # Independent of the function under test, including before fixture writes.
    return {key: value for key, value in os.environ.items() if not key.upper().startswith("GIT_")}


def fixture_git(repository: Path, *args: str, data: bytes | None = None) -> str:
    completed = subprocess.run(
        ["git", "-c", "core.autocrlf=false", "-c", "commit.gpgsign=false",
         "-c", "core.hooksPath=" + str(repository / "empty-hooks"),
         "-c", "user.name=Git isolation controls",
         "-c", "user.email=git-isolation-controls@example.invalid", *args],
        cwd=repository, env=fixture_environment(), input=data, capture_output=True, check=True)
    return completed.stdout.decode("utf-8").strip()


def file_snapshot(directory: Path) -> dict[str, bytes]:
    return {str(path.relative_to(directory)): path.read_bytes()
            for path in directory.rglob("*") if path.is_file()}


def validate_retained_inputs() -> None:
    if not NATIVE_TRX_BYTES or not NATIVE_DISCOVERY:
        raise ValueError("Original native TRX and compiled discovery inputs are required")
    with tempfile.TemporaryDirectory(prefix="retained-git-controls-") as temporary:
        path = Path(temporary) / "candidate.trx"
        path.write_bytes(NATIVE_TRX_BYTES)
        native = RUNNER.native_inventory(path)
    if native != RUNNER.compiled_inventory(NATIVE_DISCOVERY):
        raise ValueError("Retained native TRX and compiled discovery identities differ")


class GitIsolationControls(unittest.TestCase):
    """Actual main and real Git; only dotnet returns controlled retained outputs."""

    @classmethod
    def setUpClass(cls) -> None:
        validate_retained_inputs()

    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="scenario_core-git-controls-")
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        self.repository, self.product, self.product_tree, self.head = self.make_repository("intended", SOURCE_BYTES)
        self.foreign, _, _, self.foreign_head = self.make_repository("foreign", b"// Foreign source canary.\n")
        self.assertNotEqual(self.head, self.foreign_head)

    def make_repository(self, name: str, source_bytes: bytes) -> tuple[Path, str, str, str]:
        repository = self.directory / name
        repository.mkdir()
        fixture_git(repository, "init", "--quiet", "--template=")
        (repository / "product.txt").write_text(name + " immutable Product fixture\n", encoding="utf-8")
        fixture_git(repository, "add", "--", "product.txt")
        fixture_git(repository, "commit", "--quiet", "-m", "Disposable Product fixture")
        product = fixture_git(repository, "rev-parse", "HEAD")
        product_tree = fixture_git(repository, "rev-parse", "HEAD^{tree}")
        source = repository / RUNNER.SOURCE
        source.parent.mkdir(parents=True)
        source.write_bytes(source_bytes)
        fixture_git(repository, "add", "--", RUNNER.SOURCE)
        fixture_git(repository, "commit", "--quiet", "-m", "Disposable Test fixture")
        return repository, product, product_tree, fixture_git(repository, "rev-parse", "HEAD")

    def run_main(self, overrides: dict[str, str]) -> None:
        worktree, evidence = self.directory / "composed", self.directory / "evidence"
        original_foreign = file_snapshot(self.foreign)
        original_source = (self.repository / RUNNER.SOURCE).read_bytes()
        real_command = RUNNER.command
        stages = []

        def controlled_command(args, cwd, log=None, *, preserve_output=False):
            if args[0] != "dotnet":
                return real_command(args, cwd, log, preserve_output=preserve_output)
            stage = "discovery" if "--list-tests" in args else args[1]
            stages.append(stage)
            if stage not in ("--version", "restore", "test", "discovery"):
                raise AssertionError("Unexpected controlled command")
            output = "Controlled output; no native execution.\n"
            if stage == "--version":
                output = "10.0.403\n"
            elif stage == "test":
                (evidence / "candidate.trx").write_bytes(NATIVE_TRX_BYTES)
            elif stage == "discovery":
                output = NATIVE_DISCOVERY
            if log is not None:
                log.write_text(output, encoding="utf-8")
            return output if preserve_output else output.strip()

        environment = {**fixture_environment(), **overrides}
        try:
            with patch.dict(os.environ, environment, clear=True), \
                    patch.object(RUNNER, "ROOT", self.repository), \
                    patch.object(RUNNER, "PRODUCT_HEAD", self.product, create=True), \
                    patch.object(RUNNER, "PRODUCT_TREE", self.product_tree, create=True), \
                    patch.object(RUNNER, "command", controlled_command), \
                    patch.object(sys, "argv", ["compose_and_verify.py", "--product-head", self.product,
                                              "--worktree", str(worktree), "--evidence", str(evidence)]), \
                    redirect_stdout(io.StringIO()):
                RUNNER.main()
                self.assertEqual(environment, dict(os.environ))
            receipt = json.loads((evidence / "receipt.json").read_text(encoding="utf-8"))
            self.assertEqual(PASS_STATUS, receipt["status"])
            self.assertEqual(self.head, receipt["testHead"])
            self.assertEqual(self.product, receipt["productHead"])
            self.assertEqual(self.product_tree, receipt["productTree"])
            self.assertEqual(SOURCE_BLOB, receipt["sourceBlob"])
            self.assertEqual(hashlib.sha256(SOURCE_BYTES).hexdigest(), receipt["sourceSha256"])
            self.assertEqual(SOURCE_BYTES, (worktree / RUNNER.DESTINATION).read_bytes())
            self.assertEqual(self.product, fixture_git(worktree, "rev-parse", "HEAD"))
            self.assertEqual(receipt["composedTree"], fixture_git(worktree, "write-tree"))
            self.assertEqual(sorted(RUNNER.EXPECTED_IDENTITIES), receipt["nativeXunit"]["identities"])
            self.assertEqual(["--version", "restore", "test", "discovery"], stages)
            for flag in ("requiredCheckQualified", "independentReviewComplete", "nativeUiQualified",
                         "productionQualified", "trustQualified", "releaseQualified"):
                self.assertIs(False, receipt[flag])
            self.assertEqual(original_source, (self.repository / RUNNER.SOURCE).read_bytes())
            self.assertEqual(self.head, fixture_git(self.repository, "rev-parse", "HEAD"))
            self.assertEqual("", fixture_git(self.repository, "status", "--porcelain", "--untracked-files=all"))
        finally:
            if worktree.exists():
                fixture_git(self.repository, "worktree", "remove", "--force", str(worktree))
            self.assertEqual(original_foreign, file_snapshot(self.foreign))

    def test_source_and_original_native_roster_remain_frozen(self) -> None:
        blob = hashlib.sha1(b"blob " + str(len(SOURCE_BYTES)).encode() + b"\0" + SOURCE_BYTES).hexdigest()
        self.assertEqual(SOURCE_BLOB, blob)
        methods = re.findall(r"\[Fact\]\s+public (?:async )?(?:Task|void) (\w+)\(", SOURCE_BYTES.decode())
        self.assertEqual(EXPECTED_COUNT, len(methods))
        self.assertEqual(RUNNER.EXPECTED_IDENTITIES,
                         frozenset(RUNNER.TEST_CLASS_FQN + "." + method for method in methods))
        validate_retained_inputs()

    def test_git_child_environment_preserves_non_git_settings(self) -> None:
        supplied = {
            "GIT_DIR": "foreign", "git_work_tree": "foreign", "GiT_INDEX_FILE": "foreign",
            "GIT_OBJECT_DIRECTORY": "foreign", "GIT_ALTERNATE_OBJECT_DIRECTORIES": "foreign",
            "GIT_COMMON_DIR": "foreign", "GIT_CONFIG_COUNT": "1", "GIT_CONFIG_KEY_0": "core.bare",
            "GIT_CONFIG_VALUE_0": "true", "GIT_CONFIG_PARAMETERS": "'core.bare=true'",
            "PATH": "host-path", "DOTNET_CLI_UI_LANGUAGE": "en-US", "CONTROL_CANARY": "retained",
        }
        with patch.dict(os.environ, supplied, clear=True):
            before = dict(os.environ)
            self.assertEqual({key: supplied[key] for key in ("PATH", "DOTNET_CLI_UI_LANGUAGE", "CONTROL_CANARY")},
                             RUNNER.local_git_environment())
            self.assertEqual(before, dict(os.environ))

    def test_main_admits_original_inputs_without_git_overrides(self) -> None:
        self.run_main({})

    def test_repository_redirect_affects_only_unprotected_read(self) -> None:
        overrides = {"GIT_DIR": str(self.foreign / ".git"), "GIT_WORK_TREE": str(self.foreign)}
        before = file_snapshot(self.foreign)
        with patch.dict(os.environ, {**fixture_environment(), **overrides}, clear=True):
            unprotected = subprocess.run(["git", "rev-parse", "HEAD"], cwd=self.repository,
                                         capture_output=True, text=True, check=True).stdout.strip()
            self.assertEqual(self.foreign_head, unprotected)
            self.assertEqual(self.head, RUNNER.git(self.repository, "rev-parse", "HEAD"))
        self.assertEqual(before, file_snapshot(self.foreign))
        self.run_main(overrides)

    def test_foreign_index_is_not_read_or_modified(self) -> None:
        self.run_main({"GIT_INDEX_FILE": str(self.foreign / ".git" / "index")})

    def test_foreign_object_and_common_directories_are_ignored(self) -> None:
        self.run_main({"GIT_OBJECT_DIRECTORY": str(self.foreign / ".git" / "objects"),
                       "GIT_ALTERNATE_OBJECT_DIRECTORIES": str(self.foreign / ".git" / "objects"),
                       "GIT_COMMON_DIR": str(self.foreign / ".git")})

    def test_counted_git_configuration_cannot_replace_worktree(self) -> None:
        self.run_main({"GIT_CONFIG_COUNT": "2", "GIT_CONFIG_KEY_0": "core.worktree",
                       "GIT_CONFIG_VALUE_0": str(self.foreign),
                       "GIT_CONFIG_KEY_1": "core.bare", "GIT_CONFIG_VALUE_1": "true"})

    def test_parameter_git_configuration_cannot_disable_worktree(self) -> None:
        self.run_main({"GIT_CONFIG_PARAMETERS": "'core.bare=true'"})


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
        temporary = tempfile.TemporaryDirectory(prefix="scenario-core-native-controls-")
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
        for field, value in (("total", "21"), ("passed", "21"), ("notExecuted", "1"), ("inconclusive", "1")):
            with self.subTest(field=field):
                root = native_fixture()
                self.assertEqual(IDENTITIES, self.read(root))
                root.find(NAMESPACE + "ResultSummary/" + NAMESPACE + "Counters").set(field, value)
                self.reject("Native TRX must contain exactly twenty complete passing candidate facts", lambda: self.read(root))

    def test_nested_result_records_are_rejected_without_changing_outer_roster(self) -> None:
        for outcome in ("Passed", "NotExecuted", "Failed"):
            for location in ("Results", "TestDefinitions", "TestEntries", "ResultSummary", "TestRun"):
                for kind in ("UnitTestResult", "GenericTestResult"):
                    for namespace in (NAMESPACE, "{urn:foreign-trx}"):
                        for wrapped in (False, True):
                            with self.subTest(outcome=outcome, location=location, kind=kind,
                                              namespace=namespace, wrapped=wrapped):
                                root = native_fixture()
                                record = root if location == "TestRun" else root.find(NAMESPACE + location)
                                if location in ("Results", "TestDefinitions", "TestEntries"):
                                    record = record[0]
                                parent = ET.SubElement(record, namespace + "InnerResults") if wrapped else record
                                ET.SubElement(parent, namespace + kind, outcome=outcome,
                                              testName="Foreign.HiddenResult", testId="hidden", executionId="hidden")
                                self.reject("Native TRX contains nested result records", lambda: self.read(root))

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


    def test_run_level_failures_cannot_hide_behind_passing_counters(self) -> None:
        root = native_fixture()
        ET.SubElement(root.find(NAMESPACE + "ResultSummary"), NAMESPACE + "ErrorInfo")
        self.reject("Native TRX contains contradictory failure information", lambda: self.read(root))
        for outcome in ("Failed", "Error", "Aborted", "Timeout", "NotExecuted", "Warning", "Passed", "Completed", "unknown"):
            with self.subTest(outcome=outcome):
                root = native_fixture()
                infos = ET.SubElement(root.find(NAMESPACE + "ResultSummary"), NAMESPACE + "RunInfos")
                ET.SubElement(infos, NAMESPACE + "RunInfo", outcome=outcome)
                self.reject("Native TRX contains contradictory failure information", lambda: self.read(root))
        for outcome in ("Information",):
            root = native_fixture()
            infos = ET.SubElement(root.find(NAMESPACE + "ResultSummary"), NAMESPACE + "RunInfos")
            ET.SubElement(infos, NAMESPACE + "RunInfo", outcome=outcome)
            self.assertEqual(IDENTITIES, self.read(root))


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


class WorkingTreeControls(ExactRejection):
    RAW = b"// Disposable source-binding fixture; not executable native evidence.\n"

    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="scenario-core-git-controls-")
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
        (repository / " spaced\u2028product.txt").write_text("Unusual tracked path\n", encoding="utf-8")
        (repository / ".gitignore").write_text("bin/\nobj/\ndata/\n", encoding="utf-8")
        project = repository / "tests/Coglatas.Tests/Coglatas.Tests.csproj"
        project.parent.mkdir(parents=True)
        project.write_text('<Project Sdk="Microsoft.NET.Sdk" />\n', encoding="utf-8")
        fixture_git(repository, "add", "--", "product.txt", " spaced\u2028product.txt", ".gitignore", "tests/Coglatas.Tests/Coglatas.Tests.csproj")
        fixture_git(repository, "commit", "--quiet", "-m", "Disposable product fixture")
        head = fixture_git(repository, "rev-parse", "HEAD")
        destination = repository / RUNNER.DESTINATION
        destination.parent.mkdir(parents=True, exist_ok=True)
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

    def test_git_index_masks_cannot_hide_changed_product_bytes(self) -> None:
        tracked = self.repository / "product.txt"
        original = tracked.read_bytes()
        original_blob = fixture_git(self.repository, "rev-parse", ":product.txt")
        for flag in ("assume-unchanged", "skip-worktree"):
            with self.subTest(flag=flag):
                fixture_git(self.repository, "update-index", "--" + flag, "--", "product.txt")
                tracked.write_bytes(original + b"Unreviewed masked Product input\n")
                self.assertEqual(original_blob, fixture_git(self.repository, "rev-parse", ":product.txt"))
                self.assertEqual("", fixture_git(self.repository, "diff", "--name-only", "--"))
                self.reject("Git index flags hide tracked Product source", self.verify)
                fixture_git(self.repository, "update-index", "--no-" + flag, "--", "product.txt")
                tracked.write_bytes(original)
                self.admit()

    def test_unrelated_tracked_drift_is_rejected(self) -> None:
        tracked = self.repository / "product.txt"
        original = tracked.read_bytes()
        tracked.write_bytes(original + b"unexpected Product change\n")
        self.reject("Tracked Product source changed during independent composition", self.verify)
        tracked.write_bytes(original)
        self.admit()

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

    def test_ignored_compile_input_is_rejected_before_and_after_restore(self) -> None:
        relative = "tests/Coglatas.Tests/data/Injected.cs"
        injected = self.repository / relative
        injected.parent.mkdir(parents=True)
        injected.write_text("// Ignored by Git but included by the SDK compiler glob.\n", encoding="utf-8")
        self.assertEqual(relative, fixture_git(self.repository, "check-ignore", "--", relative))
        self.assertEqual("", fixture_git(self.repository, "ls-files", "--others", "--exclude-standard"))
        for allow_generated in (False, True):
            with self.subTest(allow_generated=allow_generated):
                self.reject("Unexpected untracked source appeared during independent composition",
                            lambda: RUNNER.verify_working_tree(self.repository, self.RAW, self.blob,
                                                              allow_generated=allow_generated))
        injected.unlink()
        self.admit()

    def test_nested_and_unregistered_output_names_cannot_hide_compile_input(self) -> None:
        for relative in ("tests/Coglatas.Tests/ProjectIde/bin/Injected.cs",
                         "tests/Coglatas.Tests/ProjectIde/obj/Injected.cs",
                         "tests/Unregistered/bin/Injected.cs"):
            with self.subTest(path=relative):
                injected = self.repository / relative
                injected.parent.mkdir(parents=True, exist_ok=True)
                injected.write_text("// An arbitrary directory name is not an admitted SDK output root.\n", encoding="utf-8")
                self.assertEqual(relative, fixture_git(self.repository, "check-ignore", "--", relative))
                self.reject("Unexpected untracked source appeared during independent composition",
                            lambda: RUNNER.verify_working_tree(self.repository, self.RAW, self.blob, allow_generated=True))
                injected.unlink()
                self.admit()

    def test_only_committed_project_output_roots_are_admitted_after_restore(self) -> None:
        for relative in ("tests/Coglatas.Tests/bin/Release/result.dll",
                         "tests/Coglatas.Tests/obj/Release/GeneratedAssemblyInfo.cs"):
            generated = self.repository / relative
            generated.parent.mkdir(parents=True, exist_ok=True)
            generated.write_text("// Disposable generated-output positive control.\n", encoding="utf-8")
        self.reject("Unexpected untracked source appeared during independent composition", self.verify)
        self.assertIsNone(RUNNER.verify_working_tree(self.repository, self.RAW, self.blob, allow_generated=True))

    def test_hostile_git_dir_redirects_only_unprotected_read(self) -> None:
        foreign, foreign_head, _ = self.make_repository("foreign")
        self.assertNotEqual(self.head, foreign_head)
        originals = file_snapshot(self.directory)
        hostile = {**fixture_environment(), "GIT_DIR": str(foreign / ".git")}
        with patch.dict(os.environ, hostile, clear=True):
            # Deliberately omit sanitization only for this read-only counterexample.
            unprotected = subprocess.run(["git", "rev-parse", "HEAD"], cwd=self.repository,
                                         capture_output=True, text=True, check=True).stdout.strip()
            self.assertEqual(foreign_head, unprotected)
            self.assertEqual(self.head, RUNNER.git(self.repository, "rev-parse", "HEAD"))
            self.admit()
        self.assertEqual(originals, file_snapshot(self.directory))


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



class WorkflowExecutionControls(unittest.TestCase):
    """Run each real workflow admission block against disposable script canaries."""

    SCRIPTS = ("tests/candidates/projectide-scenario-core/compose_and_verify.py",
               "tests/candidates/projectide-scenario-core/test_compose_and_verify.py")
    CANARY = (b"import json, os, sys\nfrom pathlib import Path\n"
              b"Path(os.environ['CONTROL_OUTPUT']).write_text(json.dumps({'isolated': sys.flags.isolated}))\n")

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
        for relative in self.SCRIPTS:
            target = self.repository / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(self.CANARY)
        self.git("add", "--", ".")
        self.git("commit", "--quiet", "-m", "Disposable Python execution canaries")
        self.head = self.git("rev-parse", "HEAD").strip()
        self.blob = self.git("rev-parse", "HEAD:" + self.SCRIPTS[0]).strip()
        workflow = RUNNER.ROOT / ".github/workflows/projectide-scenario-core-advisory.yml"
        lines = workflow.read_text(encoding="utf-8").splitlines()
        self.blocks = []
        for index, line in enumerate(lines):
            if not line.strip().startswith("python -I -B tests/candidates/projectide-scenario-core/"):
                continue
            first = index
            while first and lines[first - 1].strip().startswith(("$committed =", "$working =", "if ($LASTEXITCODE")):
                first -= 1
            while first < index and not lines[first].strip().startswith("$committed ="):
                first += 1
            self.blocks.append("\n".join(item[10:] for item in lines[first:index + 1]))
        self.assertEqual(4, len(self.blocks), "Both controls and runners on both hosts must be exercised")
        self.assertTrue(all("git hash-object --no-filters" in block for block in self.blocks))

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
        environment.update(TEST_HEAD=self.head, RUNNER_BLOB=self.blob, CONTROLS_BLOB=self.blob,
                           RUNNER_TEMP=str(self.directory), CONTROL_OUTPUT=str(marker),
                           PYTHONPATH=str(self.repository / Path(self.SCRIPTS[0]).parent))
        result = subprocess.run(["pwsh", "-NoProfile", "-NonInteractive", "-File", str(script)],
                                cwd=self.repository, env=environment, capture_output=True,
                                text=True, timeout=60)
        return result, marker

    def test_actual_blocks_use_isolated_python_despite_import_shadow(self) -> None:
        shadow = self.repository / Path(self.SCRIPTS[0]).parent / "json.py"
        shadow.write_text("raise RuntimeError('UNREVIEWED_IMPORT_SHADOW')\n", encoding="utf-8")
        for number, block in enumerate(self.blocks):
            with self.subTest(block=number):
                result, marker = self.execute(block)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertEqual({"isolated": 1}, json.loads(marker.read_text(encoding="utf-8")))

    def test_changed_execution_bytes_are_rejected_before_canary_runs(self) -> None:
        for number, block in enumerate(self.blocks):
            for relative in self.SCRIPTS:
                if relative not in block:
                    continue
                with self.subTest(block=number, script=relative):
                    target = self.repository / relative
                    target.write_bytes(self.CANARY + b"# Unreviewed execution bytes.\n")
                    result, marker = self.execute(block)
                    self.assertNotEqual(0, result.returncode)
                    self.assertIn("Working execution script identity mismatch", result.stdout + result.stderr)
                    self.assertFalse(marker.exists())
                    target.write_bytes(self.CANARY)
                    result, marker = self.execute(block)
                    self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                    self.assertTrue(marker.exists())

    def test_committed_and_normalized_working_bytes_are_refused(self) -> None:
        for number, block in enumerate(self.blocks):
            for relative in self.SCRIPTS:
                if relative not in block:
                    continue
                for kind in ("normalized", "committed"):
                    with self.subTest(block=number, script=relative, kind=kind):
                        target = self.repository / relative
                        if kind == "normalized":
                            self.git("config", "core.autocrlf", "true")
                            target.write_bytes(self.CANARY.replace(b"\n", b"\r\n"))
                            self.assertEqual(self.blob, self.git("-c", "core.autocrlf=true", "hash-object", "--", relative).strip())
                        else:
                            target.write_bytes(self.CANARY + b"# Changed committed instruction file.\n")
                            self.git("add", "--", relative)
                            self.git("commit", "--quiet", "-m", "Different committed canary")
                            self.head = self.git("rev-parse", "HEAD").strip()
                        result, marker = self.execute(block)
                        self.assertNotEqual(0, result.returncode)
                        expected = ("Working execution script identity mismatch" if kind == "normalized"
                                    else "Committed execution script identity mismatch")
                        self.assertIn(expected, result.stdout + result.stderr)
                        self.assertFalse(marker.exists())
                        self.git("config", "core.autocrlf", "false")
                        target.write_bytes(self.CANARY)
                        if kind == "committed":
                            self.git("add", "--", relative)
                            self.git("commit", "--quiet", "-m", "Restore original pinned canary")
                            self.head = self.git("rev-parse", "HEAD").strip()
                        result, marker = self.execute(block)
                        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                        self.assertTrue(marker.exists())


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--native-trx", type=Path, required=True)
    parser.add_argument("--compiled-discovery", type=Path, required=True)
    arguments, unittest_arguments = parser.parse_known_args()
    NATIVE_TRX_BYTES = arguments.native_trx.read_bytes()
    NATIVE_DISCOVERY = arguments.compiled_discovery.read_bytes().decode("utf-8")
    validate_retained_inputs()
    print("Admission controls reuse retained TRX SHA256 " + hashlib.sha256(NATIVE_TRX_BYTES).hexdigest()
          + "; copied reports add no native cases; the SDK probe runs a disposable project only.", flush=True)
    unittest.main(argv=[sys.argv[0], *unittest_arguments])
