"""Portable verifier controls; synthetic reports and disposable Git, never native C# evidence."""
from __future__ import annotations

import importlib.util
import shutil
import re
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
SPEC = importlib.util.spec_from_file_location("semantic_index_viewmodel_candidate_runner", RUNNER_PATH)
assert SPEC is not None and SPEC.loader is not None
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)
NAMESPACE = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
IDENTITIES = sorted(RUNNER.EXPECTED_IDENTITIES)


class WorkflowCasefoldCacheControls(unittest.TestCase):
    """Execute the actual isolated scan against case-variant Python caches."""

    def test_uppercase_cache_and_bytecode_names_fail_closed(self) -> None:
        workflow = (Path(__file__).resolve().parents[3]
                    / ".github/workflows/projectide-semantic-index-viewmodel-advisory.yml").read_text(encoding="utf-8")
        predicate = "p.name.casefold()=='__pycache__' or p.suffix.casefold() in ('.pyc','.pyo')"
        self.assertEqual(6, workflow.count(predicate))
        self.assertEqual(6, workflow.count("if ($LASTEXITCODE -ne 0 -or $committed -ne $script.Sha) { throw 'Pre-execution committed Python identity mismatch' }"))
        self.assertEqual(6, workflow.count("if ($LASTEXITCODE -ne 0 -or $working -ne $script.Sha) { throw 'Pre-execution working Python bytes mismatch' }"))
        lines = [line.strip() for line in workflow.splitlines()
                 if 'python -I -B -c "from pathlib import Path; import sys;' in line
                 and predicate in line]
        self.assertEqual(6, len(lines))
        programs = [line.split('python -I -B -c "', 1)[1].split('"', 1)[0] for line in lines]
        self.assertEqual(1, len(set(programs)), "Both hosts must run identical cache scanners")
        with tempfile.TemporaryDirectory(prefix="security-python-casefold-cache-") as temp:
            root = Path(temp)

            def scan() -> subprocess.CompletedProcess:
                return subprocess.run([sys.executable, "-I", "-B", "-c", programs[0], str(root)],
                                      capture_output=True, text=True, timeout=15)

            self.assertEqual(0, scan().returncode, "Cache-free paths must remain admitted")
            cache = root / "__PYCACHE__"
            cache.mkdir()
            self.assertNotEqual(0, scan().returncode, "Case-variant cache directory must be refused")
            cache.rmdir()
            for suffix in (".PYC", ".PYO"):
                with self.subTest(suffix=suffix):
                    artifact = root / ("injected" + suffix)
                    artifact.write_bytes(b"untrusted cache")
                    self.assertNotEqual(0, scan().returncode, "Case-variant bytecode file must be refused")
                    artifact.unlink()
                    self.assertEqual(0, scan().returncode, "Removal must restore cache-free admission")



class WorkflowPythonIsolationControls(unittest.TestCase):
    """Workflow source/byte admission and a real isolated-import negative."""

    def test_both_host_verifiers_and_runner_require_pinned_isolated_python(self) -> None:
        document = (Path(__file__).resolve().parents[3]
                    / ".github/workflows/projectide-semantic-index-viewmodel-advisory.yml").read_text(encoding="utf-8")
        self.assertEqual(2, document.count("python -I -B tests/candidates/projectide-semantic-index-viewmodel/compose_and_verify.py"))
        self.assertEqual(2, document.count("python -I -B -c \"import runpy,sys; p=sys.argv[1]; sys.argv=[p]; runpy.run_path(p,run_name='__main__')\" 'tests/candidates/projectide-semantic-index-viewmodel/test_compose_and_verify.py'"))
        self.assertEqual(2, document.count("runpy.run_path('tests/candidates/workflow-provenance/test_workflow_provenance.py'"))
        self.assertEqual(6, document.count('git rev-parse "$($env:TEST_HEAD):$($script.Path)"'))
        self.assertEqual(6, document.count("git hash-object --no-filters -- $script.Path"))
        self.assertEqual(6, document.count("Python bytecode cache is not admitted"))
        self.assertNotIn("python -B ", document)
        for command in (
            "python -I -B -c \"import runpy,sys; p=sys.argv[1]; sys.argv=[p]; runpy.run_path(p,run_name='__main__')\" 'tests/candidates/projectide-semantic-index-viewmodel/test_compose_and_verify.py'",
            "runpy.run_path('tests/candidates/workflow-provenance/test_workflow_provenance.py'",
            "python -I -B tests/candidates/projectide-semantic-index-viewmodel/compose_and_verify.py",
        ):
            for index, line in enumerate(document.splitlines()):
                if command in line:
                    prior = "\n".join(document.splitlines()[max(0, index - 23):index])
                    self.assertIn('git rev-parse "$($env:TEST_HEAD):$($script.Path)"', prior)
                    self.assertIn("git hash-object --no-filters -- $script.Path", prior)
                    self.assertIn("Python bytecode cache is not admitted", prior)

    def test_pythonpath_shadow_loaded_plain_but_refused_by_isolation(self) -> None:
        with tempfile.TemporaryDirectory(prefix="semantic-index-viewmodel-python-shadow-") as temporary:
            marker = Path(temporary) / "shadow-import-executed"
            (Path(temporary) / "sitecustomize.py").write_text(
                "from pathlib import Path\nPath(" + repr(str(marker)) + ").touch()\n", encoding="utf-8")
            environment = {**os.environ, "PYTHONPATH": temporary}
            plain = subprocess.run([sys.executable, "-B", "-c", "print('PLAIN')"],
                                   env=environment, capture_output=True, text=True)
            self.assertEqual(0, plain.returncode, plain.stderr)
            self.assertTrue(marker.exists(), "Negative control must execute the import shadow")
            marker.unlink()
            isolated = subprocess.run([sys.executable, "-I", "-B", "-c",
                                       "import sys; assert sys.flags.isolated == 1"],
                                      env=environment, capture_output=True, text=True)
            self.assertEqual(0, isolated.returncode, isolated.stderr)
            self.assertFalse(marker.exists(), "Isolated Python must ignore PYTHONPATH")



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
        temporary = tempfile.TemporaryDirectory(prefix="semantic-index-viewmodel-native-controls-")
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
        for field, value in (("total", "21"), ("passed", "21"), ("notExecuted", "1"), ("inconclusive", "1")):
            with self.subTest(field=field):
                root = native_fixture()
                self.assertEqual(IDENTITIES, self.read(root))
                root.find(NAMESPACE + "ResultSummary/" + NAMESPACE + "Counters").set(field, value)
                self.reject("Native TRX must contain exactly twenty-four complete passing candidate facts", lambda: self.read(root))

    def test_nested_result_records_are_rejected_without_changing_outer_roster(self) -> None:
        for outcome in ("Passed", "NotExecuted", "Failed"):
            for wrapped in (False, True):
                with self.subTest(outcome=outcome, wrapped=wrapped):
                    root = native_fixture()
                    case = root.find(NAMESPACE + "Results")[0]
                    parent = ET.SubElement(case, NAMESPACE + "InnerResults") if wrapped else case
                    ET.SubElement(parent, NAMESPACE + "UnitTestResult", outcome=outcome,
                                  testName="Foreign.HiddenResult", testId=str(UUID(int=9000)),
                                  executionId=str(UUID(int=9001)))
                    self.reject("Native TRX contains nested result records", lambda: self.read(root))

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

    def test_empty_duplicate_or_unsupported_run_infos_containers_refuse(self) -> None:
        for location in ("root", "result", "definition", "foreign-summary", "duplicate", "unknown-child"):
            with self.subTest(location=location):
                root = native_fixture()
                summary = root.find(NAMESPACE + "ResultSummary")
                if location == "duplicate":
                    for _ in range(2):
                        infos = ET.SubElement(summary, NAMESPACE + "RunInfos")
                        ET.SubElement(infos, NAMESPACE + "RunInfo", outcome="Information")
                elif location == "unknown-child":
                    infos = ET.SubElement(summary, NAMESPACE + "RunInfos")
                    ET.SubElement(infos, NAMESPACE + "RunInfo", outcome="Information")
                    ET.SubElement(infos, "{urn:foreign-trx}OpaqueDiagnostic")
                else:
                    parent = {"root": root, "result": root.find(NAMESPACE + "Results")[0],
                              "definition": root.find(NAMESPACE + "TestDefinitions")[0],
                              "foreign-summary": summary}[location]
                    namespace = "{urn:foreign-trx}" if location == "foreign-summary" else NAMESPACE
                    ET.SubElement(parent, namespace + "RunInfos")
                self.reject("Native TRX contains contradictory failure information", lambda: self.read(root))

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
         "-c", "user.name=Semantic index ViewModel verifier controls", "-c", "user.email=semantic-index-viewmodel-controls@example.invalid", *args],
        cwd=repository, env=setup_environment(), input=data, capture_output=True, check=True)
    return completed.stdout.decode("utf-8").strip()


def file_snapshot(directory: Path) -> dict[str, bytes]:
    return {str(path.relative_to(directory)): path.read_bytes()
            for path in directory.rglob("*") if path.is_file()}


class WorkingTreeControls(ExactRejection):
    RAW = b"// Disposable source-binding fixture; not executable native evidence.\n"

    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="semantic-index-viewmodel-git-controls-")
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
        (repository / ".gitignore").write_text("bin/\nobj/\ndata/\n", encoding="utf-8")
        project = repository / "tests/Coglatas.Tests/Coglatas.Tests.csproj"
        project.parent.mkdir(parents=True)
        project.write_text('<Project Sdk="Microsoft.NET.Sdk" />\n', encoding="utf-8")
        fixture_git(repository, "add", "--", "product.txt", ".gitignore", "tests/Coglatas.Tests/Coglatas.Tests.csproj")
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
        hostile = {**setup_environment(), "GIT_DIR": str(foreign / ".git")}
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


class WorkflowBootstrapControls(unittest.TestCase):
    """Execute the actual advisory bootstrap over disposable helper bytes, not Product code."""

    HELPER = "tests/candidates/workflow-provenance/verify_workflow_provenance.py"
    HELPER_BYTES = (b"import json, os, sys\nfrom pathlib import Path\n"
                    b"Path(os.environ['BOOTSTRAP_RESULT']).write_text(json.dumps({'isolated': sys.flags.isolated}))\n")

    def setUp(self) -> None:
        self.pwsh = shutil.which("pwsh")
        if self.pwsh is None:
            if os.environ.get("GITHUB_ACTIONS") == "true":
                self.fail("Hosted workflow bootstrap controls require PowerShell")
            self.skipTest("PowerShell is unavailable; hosted bootstrap execution remains required")
        temporary = tempfile.TemporaryDirectory(prefix="workflow-bootstrap-controls-")
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        self.repository = self.directory / "source"
        self.repository.mkdir()
        fixture_git(self.repository, "init", "--quiet", "--template=")
        self.helper = self.repository / self.HELPER
        self.helper.parent.mkdir(parents=True)
        self.helper.write_bytes(self.HELPER_BYTES)
        fixture_git(self.repository, "add", "--", self.HELPER)
        fixture_git(self.repository, "commit", "--quiet", "-m", "Disposable pinned helper")
        self.pinned = fixture_git(self.repository, "rev-parse", "HEAD:" + self.HELPER)
        self.result = self.directory / "executed.json"
        self.shadow = self.directory / "shadow-loaded.txt"
        workflow = Path(__file__).resolve().parents[3] / ".github/workflows/projectide-semantic-index-viewmodel-advisory.yml"
        self.scripts = re.findall(
            r"      - name: Bind the immutable executing workflow to the reviewed Test source\n"
            r"        shell: pwsh\n        run: \|\n((?:          [^\n]*\n)+)",
            workflow.read_text(encoding="utf-8"))
        self.assertEqual(2, len(self.scripts), "Both hosted bootstrap blocks must be exercised")

    def execute(self, script: str) -> subprocess.CompletedProcess:
        path = self.directory / "bootstrap.ps1"
        path.write_text("\n".join(line[10:] for line in script.splitlines()) + "\n", encoding="utf-8")
        environment = {key: value for key, value in os.environ.items()
                       if not key.upper().startswith("GIT_")}
        environment.update(PROVENANCE_BLOB=self.pinned, RUNNER_TEMP=str(self.directory),
                           BOOTSTRAP_RESULT=str(self.result), BOOTSTRAP_SHADOW=str(self.shadow))
        return subprocess.run([self.pwsh, "-NoLogo", "-NoProfile", "-NonInteractive", "-File", str(path)],
                              cwd=self.repository, env=environment, capture_output=True, text=True, timeout=30)

    def test_changed_helper_is_refused_before_any_helper_instruction(self) -> None:
        changed = self.HELPER_BYTES + b"# Different executable bytes.\n"
        for kind in ("working", "normalized-working", "committed"):
            with self.subTest(kind=kind):
                fixture_git(self.repository, "config", "core.autocrlf", "false")
                self.helper.write_bytes(changed)
                if kind == "normalized-working":
                    fixture_git(self.repository, "config", "core.autocrlf", "true")
                    self.helper.write_bytes(self.HELPER_BYTES.replace(b"\n", b"\r\n"))
                    self.assertEqual(self.pinned, fixture_git(
                        self.repository, "-c", "core.autocrlf=true", "hash-object", "--", self.HELPER))
                if kind == "committed":
                    fixture_git(self.repository, "add", "--", self.HELPER)
                    fixture_git(self.repository, "commit", "--quiet", "-m", "Different committed helper")
                for script in self.scripts:
                    result = self.execute(script)
                    self.assertNotEqual(0, result.returncode)
                    reason = "Provenance helper blob mismatch" if kind == "committed" else "Provenance helper working bytes mismatch"
                    self.assertIn(reason, result.stdout + result.stderr)
                    self.assertFalse(self.result.exists(), "Changed executable must not reach its first instruction")

    def test_original_helper_load_ignores_neighboring_import_shadow(self) -> None:
        shadow = self.helper.with_name("json.py")
        shadow.write_text("import os\nfrom pathlib import Path\n"
                          "Path(os.environ['BOOTSTRAP_SHADOW']).write_text('shadow executed')\n"
                          "raise RuntimeError('Neighboring import shadow executed')\n", encoding="utf-8")
        for script in self.scripts:
            result = self.execute(script)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertEqual({"isolated": 1}, json.loads(self.result.read_text(encoding="utf-8")))
            self.assertFalse(self.shadow.exists())
            self.result.unlink()


if __name__ == "__main__":
    unittest.main(verbosity=2)
