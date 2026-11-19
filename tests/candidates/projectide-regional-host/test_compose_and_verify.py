"""Portable verifier controls; synthetic reports and disposable Git, never native C# evidence."""
from __future__ import annotations

import importlib.util
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
from uuid import UUID
import xml.etree.ElementTree as ET


RUNNER_PATH = Path(__file__).with_name("compose_and_verify.py")
SPEC = importlib.util.spec_from_file_location("regional_host_candidate_runner", RUNNER_PATH)
assert SPEC is not None and SPEC.loader is not None
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)
NAMESPACE = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
IDENTITIES = sorted(RUNNER.EXPECTED_IDENTITIES)


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
        temporary = tempfile.TemporaryDirectory(prefix="regional-host-native-controls-")
        self.addCleanup(temporary.cleanup)
        self.report = Path(temporary.name) / "synthetic.trx"
        self.assertEqual(24, len(IDENTITIES))
        self.assertEqual(IDENTITIES, self.read(native_fixture()))
        self.assertEqual(IDENTITIES, RUNNER.compiled_inventory(discovery_fixture(IDENTITIES)))

    def read(self, root: ET.Element) -> list[str]:
        ET.ElementTree(root).write(self.report, encoding="utf-8", xml_declaration=True)
        return RUNNER.native_inventory(self.report)

    def test_flat_native_counters_cannot_hide_nested_results(self) -> None:
        for outcome in ("NotExecuted", "Passed"):
            with self.subTest(outcome=outcome):
                root = native_fixture()
                case = root.find(NAMESPACE + "Results/" + NAMESPACE + "UnitTestResult")
                inner = ET.SubElement(case, NAMESPACE + "InnerResults")
                ET.SubElement(inner, NAMESPACE + "UnitTestResult", outcome=outcome)
                self.reject("Native TRX contains nested result records", lambda: self.read(root))

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

    def test_result_records_belong_only_to_the_flat_results_inventory(self) -> None:
        for location in ("TestRun", "UnitTestResult", "UnitTest", "TestEntry", "ResultSummary"):
            for kind in ("InnerResults", "TestResultAggregation", "GenericTestResult", "UnitTestResult"):
                for namespace in (NAMESPACE, "{urn:foreign-trx}"):
                    with self.subTest(location=location, kind=kind, namespace=namespace):
                        root = native_fixture()
                        parent = next(root.iter(NAMESPACE + location))
                        ET.SubElement(parent, namespace + kind)
                        self.reject("Native TRX contains nested result records", lambda: self.read(root))

    def test_error_information_invalidates_an_otherwise_passing_report(self) -> None:
        for location in ("UnitTestResult", "ResultSummary"):
            for namespace in (NAMESPACE, "{urn:foreign-trx}"):
                with self.subTest(location=location, namespace=namespace):
                    root = native_fixture()
                    output = ET.SubElement(next(root.iter(NAMESPACE + location)), NAMESPACE + "Output")
                    ET.SubElement(output, namespace + "ErrorInfo")
                    self.reject("Native TRX contains contradictory failure information", lambda: self.read(root))

    def test_only_native_informational_run_messages_are_admitted(self) -> None:
        root = native_fixture()
        summary = root.find(NAMESPACE + "ResultSummary")
        output = ET.SubElement(summary, NAMESPACE + "Output")
        ET.SubElement(output, NAMESPACE + "StdOut").text = "Native test adapter output"
        infos = ET.SubElement(summary, NAMESPACE + "RunInfos")
        info = ET.SubElement(infos, NAMESPACE + "RunInfo", outcome="Information")
        ET.SubElement(info, NAMESPACE + "Text").text = "Adapter information"
        self.assertEqual(IDENTITIES, self.read(root))
        for outcome in ("Failed", "Warning", "Passed", "Aborted", "NotExecuted", "", None):
            with self.subTest(outcome=outcome):
                info.attrib = {} if outcome is None else {"outcome": outcome}
                self.reject("Native TRX contains contradictory failure information", lambda: self.read(root))

        infos.remove(info)
        info.set("outcome", "Information")
        for parent, tag in ((output, NAMESPACE + "RunInfo"), (infos, "{urn:foreign-trx}RunInfo")):
            with self.subTest(parent=parent.tag, tag=tag):
                info.tag = tag
                parent.append(info)
                self.reject("Native TRX contains contradictory failure information", lambda: self.read(root))
                parent.remove(info)

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
                self.reject("Compiled discovery does not match the exact twenty-four-fact candidate roster",
                            lambda: RUNNER.compiled_inventory(discovery_fixture(identities)))


class EnvironmentControls(ExactRejection):
    def test_native_sdk_commands_disable_external_build_imports(self) -> None:
        for command in ("restore", "test"):
            args = RUNNER.source_only_dotnet(command, "tests/Coglatas.Tests/Coglatas.Tests.csproj")
            self.assertEqual(["dotnet", command], args[:2])
            self.assertEqual(list(RUNNER.SOURCE_ONLY_MSBUILD_PROPERTIES), args[2:4])
        with self.assertRaises(ValueError):
            RUNNER.source_only_dotnet("publish")

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
         "-c", "user.name=Regional host verifier controls", "-c", "user.email=regional-host-controls@example.invalid", *args],
        cwd=repository, env=setup_environment(), input=data, capture_output=True, check=True)
    return completed.stdout.decode("utf-8").strip()


def file_snapshot(directory: Path) -> dict[str, bytes]:
    return {str(path.relative_to(directory)): path.read_bytes()
            for path in directory.rglob("*") if path.is_file()}


class WorkingTreeControls(ExactRejection):
    RAW = b"// Disposable source-binding fixture; not executable native evidence.\n"

    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="regional-host-git-controls-")
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
        (repository / ".gitignore").write_text("**/data/\n**/bin/\n**/obj/\n**/node_modules/\nartifacts/\n", encoding="utf-8")
        project = repository / "tests/Coglatas.Tests/Coglatas.Tests.csproj"
        project.parent.mkdir(parents=True)
        project.write_text('<Project Sdk="Microsoft.NET.Sdk" />\n', encoding="utf-8")
        fixture_git(repository, "add", "--", "product.txt", ".gitignore", str(project.relative_to(repository)))
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

    def test_masked_index_flags_cannot_bypass_product_byte_controls(self) -> None:
        tracked = self.repository / "product.txt"
        original = tracked.read_bytes()
        for flag, reverse in (("--assume-unchanged", "--no-assume-unchanged"),
                              ("--skip-worktree", "--no-skip-worktree")):
            with self.subTest(flag=flag):
                fixture_git(self.repository, "update-index", flag, "--", "product.txt")
                tracked.write_bytes(original + b"masked Product edit\n")
                try:
                    self.assertEqual("", fixture_git(self.repository, "diff", "--name-only", "--"))
                    self.reject("Git index flags hide tracked Product source", self.verify)
                finally:
                    fixture_git(self.repository, "update-index", reverse, "--", "product.txt")
                    tracked.write_bytes(original)
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


if __name__ == "__main__":
    unittest.main(verbosity=2)
