"""Portable verifier controls; synthetic reports and disposable Git, never native C# evidence."""
from __future__ import annotations

import importlib.util
import os
import re
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
from uuid import UUID
import xml.etree.ElementTree as ET


RUNNER_PATH = Path(__file__).with_name("compose_and_verify.py")
SPEC = importlib.util.spec_from_file_location("reference_cache_candidate_runner", RUNNER_PATH)
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


def historical_fixture() -> ET.Element:
    root = native_fixture()
    summary = root.find(NAMESPACE + "ResultSummary")
    summary.set("outcome", "Failed")
    counters = summary.find(NAMESPACE + "Counters")
    for key, value in {"total": "7", "executed": "7", "passed": "1", "failed": "6"}.items():
        counters.set(key, value)
    results = root.find(NAMESPACE + "Results")
    definitions = root.find(NAMESPACE + "TestDefinitions")
    entries = root.find(NAMESPACE + "TestEntries")
    retained_ids = set()
    for case in list(results):
        name = case.get("testName")
        if name not in RUNNER.HISTORICAL_IDENTITIES:
            results.remove(case)
            continue
        retained_ids.add(case.get("testId"))
        if name in RUNNER.HISTORICAL_FAILURES:
            case.set("outcome", "Failed")
            output = ET.SubElement(case, NAMESPACE + "Output")
            error = ET.SubElement(output, NAMESPACE + "ErrorInfo")
            ET.SubElement(error, NAMESPACE + "Message").text = RUNNER.HISTORICAL_INVARIANT
            ET.SubElement(error, NAMESPACE + "StackTrace").text = "at " + name + "() in independently-owned.cs"
    for definition in list(definitions):
        if definition.get("id") not in retained_ids:
            definitions.remove(definition)
    for entry in list(entries):
        if entry.get("testId") not in retained_ids:
            entries.remove(entry)
    return root


class HistoricalAdmissionControls(unittest.TestCase):
    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="reference-cache-historical-controls-")
        self.addCleanup(temporary.cleanup)
        self.report = Path(temporary.name) / "synthetic-historical.trx"
        self.assertEqual(sorted(RUNNER.HISTORICAL_IDENTITIES), self.read(historical_fixture()))

    def read(self, root: ET.Element) -> list[str]:
        ET.ElementTree(root).write(self.report, encoding="utf-8", xml_declaration=True)
        return RUNNER.native_inventory(self.report, historical=True)

    def test_exact_six_failure_one_positive_shape_and_discovery(self) -> None:
        self.assertEqual(sorted(RUNNER.HISTORICAL_IDENTITIES), self.read(historical_fixture()))
        self.assertEqual(sorted(RUNNER.HISTORICAL_IDENTITIES), RUNNER.compiled_inventory(
            discovery_fixture(sorted(RUNNER.HISTORICAL_IDENTITIES)), historical=True))

    def test_historical_wrong_counter_or_overall_outcome_cannot_be_expected_red(self) -> None:
        for field, value in (("passed", "2"), ("failed", "5"), ("notExecuted", "1"), ("aborted", "1")):
            with self.subTest(field=field):
                root = historical_fixture()
                root.find(NAMESPACE + "ResultSummary/" + NAMESPACE + "Counters").set(field, value)
                with self.assertRaisesRegex(ValueError, "^Historical TRX must contain exactly six failed and one passing executed controls$"):
                    self.read(root)
        root = historical_fixture()
        root.find(NAMESPACE + "ResultSummary").set("outcome", "Completed")
        with self.assertRaisesRegex(ValueError, "^Historical TRX must contain exactly six failed and one passing executed controls$"):
            self.read(root)

    def test_an_unrelated_failure_message_or_stack_cannot_qualify_red(self) -> None:
        for field in ("Message", "StackTrace"):
            with self.subTest(field=field):
                root = historical_fixture()
                failed = next(case for case in root.find(NAMESPACE + "Results") if case.get("outcome") == "Failed")
                failed.find(NAMESPACE + "Output/" + NAMESPACE + "ErrorInfo/" + NAMESPACE + field).text = "unrelated failure"
                with self.assertRaisesRegex(ValueError, "^Historical native failure does not identify the retained-lifetime assertion$"):
                    self.read(root)

    def test_swapping_expected_failure_and_positive_with_unchanged_counts_is_rejected(self) -> None:
        root = historical_fixture()
        cases = list(root.find(NAMESPACE + "Results"))
        positive = next(case for case in cases if case.get("outcome") == "Passed")
        negative = next(case for case in cases if case.get("testName") in RUNNER.HISTORICAL_FAILURES)
        positive.set("outcome", "Failed")
        negative.set("outcome", "Passed")
        output = negative.find(NAMESPACE + "Output")
        negative.remove(output)
        positive.append(output)
        with self.assertRaisesRegex(ValueError, "^Native TRX does not match the exact reference-cache fact roster$"):
            self.read(root)

    def test_historical_mode_cannot_select_corrected_or_unapproved_product(self) -> None:
        self.assertEqual(RUNNER.PRODUCT_TREE, RUNNER.selected_product(RUNNER.PRODUCT_HEAD, False))
        self.assertEqual(RUNNER.HISTORICAL_TREE, RUNNER.selected_product(RUNNER.HISTORICAL_HEAD, True))
        for head, historical in ((RUNNER.PRODUCT_HEAD, True), (RUNNER.HISTORICAL_HEAD, False), ("a" * 40, True)):
            with self.subTest(head=head, historical=historical):
                with self.assertRaisesRegex(ValueError, "^Only the independently reviewed immutable Product commit is admitted$"):
                    RUNNER.selected_product(head, historical)

    def test_run_level_errors_cannot_hide_behind_expected_assertion_failures(self) -> None:
        for outcome in ("Error", "Failed", "Aborted", "Timeout", "Inconclusive", "Unknown"):
            with self.subTest(outcome=outcome):
                root = historical_fixture()
                infos = ET.SubElement(root.find(NAMESPACE + "ResultSummary"), NAMESPACE + "RunInfos")
                ET.SubElement(infos, NAMESPACE + "RunInfo", outcome=outcome)
                with self.assertRaisesRegex(ValueError, "^Native TRX contains failed or unsupported run-level result information$"):
                    self.read(root)
        root = historical_fixture()
        output = ET.SubElement(root.find(NAMESPACE + "ResultSummary"), NAMESPACE + "Output")
        ET.SubElement(output, NAMESPACE + "ErrorInfo")
        with self.assertRaisesRegex(ValueError, "^Native TRX contains a run-level error$"):
            self.read(root)
        self.assertEqual(sorted(RUNNER.HISTORICAL_IDENTITIES), self.read(historical_fixture()))

    def test_historical_positive_cannot_carry_contradictory_error_information(self) -> None:
        root = historical_fixture()
        positive = next(case for case in root.find(NAMESPACE + "Results") if case.get("outcome") == "Passed")
        output = ET.SubElement(positive, NAMESPACE + "Output")
        ET.SubElement(output, NAMESPACE + "ErrorInfo")
        with self.assertRaisesRegex(ValueError, "^Native TRX passing result contains error information$"):
            self.read(root)

    def test_full_source_roster_and_separate_positive_are_frozen(self) -> None:
        source = RUNNER_PATH.with_name("SecurityReferenceCacheCandidateTests.cs").read_text(encoding="utf-8")
        names = re.findall(r"\[Fact\]\s+public (?:async Task|void) (\w+)\(", source)
        self.assertEqual(24, len(names))
        self.assertEqual(set(RUNNER.CACHE_METHODS), set(names))
        self.assertEqual(6, len(RUNNER.HISTORICAL_FAILURES))
        self.assertEqual(7, len(RUNNER.HISTORICAL_IDENTITIES))
        self.assertNotIn(RUNNER.TEST_CLASS_FQN + "." + RUNNER.HISTORICAL_POSITIVE_METHOD, RUNNER.HISTORICAL_FAILURES)


class ExactRejection(unittest.TestCase):
    def reject(self, reason: str, action) -> None:
        with self.assertRaises(ValueError) as caught:
            action()
        self.assertEqual(reason, str(caught.exception))


class NativeInventoryControls(ExactRejection):
    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="reference-cache-native-controls-")
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
         "-c", "user.name=Reference cache verifier controls", "-c", "user.email=reference-cache-controls@example.invalid", *args],
        cwd=repository, env=setup_environment(), input=data, capture_output=True, check=True)
    return completed.stdout.decode("utf-8").strip()


def file_snapshot(directory: Path) -> dict[str, bytes]:
    return {str(path.relative_to(directory)): path.read_bytes()
            for path in directory.rglob("*") if path.is_file()}


class WorkingTreeControls(ExactRejection):
    RAW = b"// Disposable source-binding fixture; not executable native evidence.\n"

    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="reference-cache-git-controls-")
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
        fixture_git(repository, "add", "--", "product.txt")
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

    def test_candidate_mode_only_index_substitution_is_rejected(self) -> None:
        fixture_git(self.repository, "config", "core.filemode", "false")
        fixture_git(self.repository, "update-index", "--chmod=+x", "--", RUNNER.DESTINATION)
        self.assertEqual(self.blob, fixture_git(self.repository, "rev-parse", ":" + RUNNER.DESTINATION))
        self.assertEqual("", fixture_git(self.repository, "diff", "--name-only", "--"))
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

    def test_staged_product_change_hidden_by_restored_working_bytes_is_rejected(self) -> None:
        tracked = self.repository / "product.txt"
        original = tracked.read_bytes()
        tracked.write_bytes(original + b"unexpected staged Product change\n")
        fixture_git(self.repository, "add", "--", "product.txt")
        tracked.write_bytes(original)
        # A net HEAD-to-working-tree diff hides the unrelated index change.
        self.assertEqual([RUNNER.DESTINATION],
                         fixture_git(self.repository, "diff", "--name-only", "HEAD", "--").splitlines())
        self.assertIn("product.txt",
                      fixture_git(self.repository, "diff", "--cached", "--name-only", "HEAD", "--").splitlines())
        self.assertEqual(["product.txt"],
                         fixture_git(self.repository, "diff", "--name-only", "--").splitlines())
        self.reject("Tracked Product source changed during independent composition", self.verify)
        fixture_git(self.repository, "restore", "--staged", "--source=HEAD", "--", "product.txt")
        self.assertEqual(original, tracked.read_bytes())
        self.admit()

    def test_untracked_compiler_source_is_rejected(self) -> None:
        relative = "tests/Coglatas.Tests/ProjectIde/UntrackedCompilerInput.cs"
        untracked = self.repository / relative
        untracked.write_text("// Unexpected compiler input; never native evidence.\n", encoding="utf-8")
        self.assertEqual([RUNNER.DESTINATION],
                         fixture_git(self.repository, "diff", "--name-only", "HEAD", "--").splitlines())
        self.assertEqual([relative],
                         fixture_git(self.repository, "ls-files", "--others", "--exclude-standard").splitlines())
        self.reject("Untracked Product source appeared during independent composition", self.verify)
        untracked.unlink()
        self.admit()

    def test_ignored_nongenerated_compiler_inputs_are_rejected(self) -> None:
        exclusions = self.repository / ".git/info/exclude"
        exclusions.parent.mkdir(exist_ok=True)
        exclusions.write_text("data/\nartifacts/\nnode_modules/\nbin/\nobj/\n", encoding="utf-8")
        for relative in ("tests/Coglatas.Tests/data/Injected.cs", "artifacts/Injected.cs",
                         "node_modules/Injected.cs", "tests/Coglatas.Tests/ProjectIde/bin/Injected.cs",
                         "src/Coglatas.Domain/ProjectIde/obj/Injected.cs",
                         " tests/Coglatas.Tests/obj/Injected.cs"):
            with self.subTest(path=relative):
                path = self.repository / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("// Ignored non-generated compiler input.\n", encoding="utf-8")
                self.assertEqual("", fixture_git(self.repository, "ls-files", "--others", "--exclude-standard"))
                raw_paths = RUNNER.command(["git", "ls-files", "--others", "-z"],
                                           self.repository, preserve_output=True)
                self.assertIn(relative, raw_paths.split("\0"))
                if relative.startswith(" "):
                    self.assertTrue(raw_paths.strip().startswith(RUNNER.GENERATED_OUTPUT_PREFIXES))
                self.reject("Untracked Product source appeared during independent composition", lambda:
                    RUNNER.verify_working_tree(self.repository, self.RAW, self.blob, allow_generated=True))
                path.unlink()
                self.admit()

    def test_only_fixed_project_root_sdk_outputs_are_allowed_after_restore(self) -> None:
        generated = ["tests/Coglatas.Tests/obj/project.assets.json",
                     "tests/Coglatas.Tests/obj/Release/net10.0/Coglatas.Tests.AssemblyInfo.cs",
                     "tests/Coglatas.Tests/bin/Release/net10.0/Coglatas.Tests.dll",
                     "src/Coglatas.Domain/obj/Release/net10.0/Coglatas.Domain.GlobalUsings.g.cs"]
        for relative in generated:
            path = self.repository / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"Disposable SDK-output-shaped control; not a native artifact.\n")
        self.reject("Untracked Product source appeared during independent composition", self.verify)
        self.assertIsNone(RUNNER.verify_working_tree(self.repository, self.RAW, self.blob, allow_generated=True))
        for relative in generated:
            (self.repository / relative).unlink()
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


if __name__ == "__main__":
    unittest.main(verbosity=2)
