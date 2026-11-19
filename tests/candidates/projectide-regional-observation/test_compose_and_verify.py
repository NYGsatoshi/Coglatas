"""Portable verifier controls; synthetic reports and disposable Git, never native C# evidence."""
from __future__ import annotations

import importlib.util
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
SPEC = importlib.util.spec_from_file_location("regional_observation_candidate_runner", RUNNER_PATH)
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


class WorkflowPythonIsolationControls(unittest.TestCase):
    def test_both_host_workflows_verify_scripts_reject_caches_and_load_controls_without_discovery(self) -> None:
        document = (Path(__file__).resolve().parents[3]
                    / ".github/workflows/projectide-regional-observation-advisory.yml").read_text(encoding="utf-8")
        self.assertEqual(2, document.count(
            "python -I -B -c \"import runpy,sys; p=sys.argv[1]; sys.argv=[p]; runpy.run_path(p,run_name='__main__')\""))
        self.assertEqual(2, document.count(
            "python -I -B tests/candidates/projectide-regional-observation/compose_and_verify.py"))
        self.assertEqual(4, document.count("python -I -B -c \"from pathlib import Path; import sys; root=Path(sys.argv[1]);"))
        self.assertNotIn("unittest discover -s tests/candidates/projectide-regional-observation", document)
        self.assertNotIn("python -B ", document)
        lines = document.splitlines()
        self.assertEqual(4, sum("git hash-object --no-filters -- $script.Path" in line for line in lines))
        for index, line in enumerate(lines):
            if "runpy.run_path(p,run_name=" in line or "python -I -B tests/candidates/projectide-regional-observation/compose_and_verify.py" in line:
                prior = "\n".join(lines[max(0, index - 21):index])
                self.assertIn('git rev-parse "$($env:TEST_HEAD):$($script.Path)"', prior)
                self.assertIn("git hash-object --no-filters -- $script.Path", prior)
                self.assertIn("Candidate Python bytecode cache is not admitted", prior)

    def test_untrusted_pythonpath_canary_runs_plain_but_not_isolated(self) -> None:
        with tempfile.TemporaryDirectory(prefix="regional-observation-python-shadow-") as temporary:
            marker = Path(temporary) / "import-marker"
            (Path(temporary) / "sitecustomize.py").write_text(
                "from pathlib import Path\nPath(" + repr(str(marker)) + ").touch()\n",
                encoding="utf-8")
            environment = {**os.environ, "PYTHONPATH": temporary}
            plain = subprocess.run([sys.executable, "-B", "-c", "print('PLAIN')"],
                                   env=environment, capture_output=True, text=True)
            self.assertEqual(0, plain.returncode, plain.stderr)
            self.assertTrue(marker.exists(), "The negative control must execute sitecustomize")
            marker.unlink()
            isolated = subprocess.run(
                [sys.executable, "-I", "-B", "-c",
                 "import sys; assert sys.flags.isolated == 1; print('ISOLATED')"],
                env=environment, capture_output=True, text=True)
            self.assertEqual(0, isolated.returncode, isolated.stderr)
            self.assertEqual("ISOLATED", isolated.stdout.strip())
            self.assertFalse(marker.exists(), "Isolated Python may not execute PYTHONPATH modules")

    def test_candidate_xml_shadow_cannot_enter_explicit_runpy_execution(self) -> None:
        with tempfile.TemporaryDirectory(prefix="regional-observation-source-shadow-") as temporary:
            root = Path(temporary)
            marker = root / "shadow-imported"
            (root / "xml").mkdir()
            (root / "xml" / "__init__.py").write_text(
                "from pathlib import Path\nPath(" + repr(str(marker)) + ").touch()\n",
                encoding="utf-8")
            program = root / "test_compose_and_verify.py"
            program.write_text(
                "import unittest\nimport xml.etree.ElementTree as ET\n"
                "class VerifiedControl(unittest.TestCase):\n"
                "    def test_xml_is_available(self):\n"
                "        self.assertEqual('xml.etree.ElementTree', ET.__name__)\n"
                "if __name__ == '__main__':\n"
                "    unittest.main(verbosity=2)\n", encoding="utf-8")
            code = "import runpy,sys; p=sys.argv[1]; sys.argv=[p]; runpy.run_path(p,run_name='__main__')"
            result = subprocess.run([sys.executable, "-I", "-B", "-c", code, str(program)],
                                    cwd=root, capture_output=True, text=True)
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertIn("Ran 1 test", result.stderr)
            self.assertFalse(marker.exists(), "The candidate directory must not shadow stdlib xml")

    def test_unchanged_source_with_substituted_bytecode_is_refused_by_the_workflow_cache_gate(self) -> None:
        import py_compile
        document = (Path(__file__).resolve().parents[3]
                    / ".github/workflows/projectide-regional-observation-advisory.yml").read_text(encoding="utf-8")
        cache_command = next(line.strip() for line in document.splitlines()
                             if line.strip().startswith('python -I -B -c "from pathlib import Path; import sys;'))
        code = cache_command.split(' -c "', 1)[1].split('" ', 1)[0]
        with tempfile.TemporaryDirectory(prefix="regional-observation-bytecode-") as temporary:
            directory = Path(temporary)
            verified = directory / "compose_and_verify.py"
            verified.write_bytes(b"VERIFIED = True\n")
            original = verified.read_bytes()
            def scan():
                return subprocess.run([sys.executable, "-I", "-B", "-c", code, temporary],
                                      capture_output=True, text=True)
            self.assertEqual(0, scan().returncode)
            cache = directory / "__pycache__"
            cache.mkdir()
            injected = directory / "injected.py"
            injected.write_bytes(b"VERIFIED = False\n")
            py_compile.compile(str(injected), cfile=str(cache / "compose_and_verify.cpython-312.pyc"),
                               doraise=True, invalidation_mode=py_compile.PycInvalidationMode.UNCHECKED_HASH)
            self.assertEqual(original, verified.read_bytes(), "The original source must remain unchanged")
            self.assertNotEqual(0, scan().returncode, "Unchecked hash bytecode must not be admitted")


class NativeInventoryControls(ExactRejection):
    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="regional-observation-native-controls-")
        self.addCleanup(temporary.cleanup)
        self.report = Path(temporary.name) / "synthetic.trx"
        self.assertEqual(24, len(IDENTITIES))
        self.assertEqual(IDENTITIES, self.read(native_fixture()))
        self.assertEqual(IDENTITIES, RUNNER.compiled_inventory(discovery_fixture(IDENTITIES)))

    def read(self, root: ET.Element) -> list[str]:
        ET.ElementTree(root).write(self.report, encoding="utf-8", xml_declaration=True)
        return RUNNER.native_inventory(self.report)

    def test_nested_results_are_rejected_without_changing_flat_metadata(self) -> None:
        for outcome in ("Passed", "NotExecuted"):
            for namespace in (NAMESPACE, "{urn:foreign}"):
                with self.subTest(outcome=outcome, namespace=namespace):
                    root = native_fixture()
                    case = root.find(NAMESPACE + "Results/" + NAMESPACE + "UnitTestResult")
                    nested = ET.SubElement(case, namespace + "InnerResults")
                    ET.SubElement(nested, namespace + "UnitTestResult", outcome=outcome)
                    self.reject("Native TRX contains nested result records", lambda: self.read(root))
        root = native_fixture()
        case = root.find(NAMESPACE + "Results/" + NAMESPACE + "UnitTestResult")
        ET.SubElement(case, NAMESPACE + "UnitTestResult", outcome="Passed")
        self.reject("Native TRX contains nested result records", lambda: self.read(root))

    def test_result_records_are_only_allowed_in_the_flat_roster(self) -> None:
        for name in ("UnitTestResult", "GenericTestResult", "TestResult",
                     "TestResultAggregation", "InnerResults"):
            for namespace in (NAMESPACE, "{urn:foreign}"):
                for location in ("ResultSummary", "TestDefinitions", "TestEntries"):
                    with self.subTest(name=name, namespace=namespace, location=location):
                        root = native_fixture()
                        parent = root.find(NAMESPACE + location)
                        if location != "ResultSummary":
                            parent = parent[0]
                        ET.SubElement(parent, namespace + name, outcome="Passed")
                        self.reject("Native TRX contains nested result records", lambda: self.read(root))

    def test_error_info_in_any_namespace_is_rejected(self) -> None:
        for namespace in (NAMESPACE, "{urn:foreign}"):
            for location in ("ResultSummary", "Results/" + NAMESPACE + "UnitTestResult"):
                with self.subTest(namespace=namespace, location=location):
                    root = native_fixture()
                    output = ET.SubElement(root.find(NAMESPACE + location), NAMESPACE + "Output")
                    ET.SubElement(output, namespace + "ErrorInfo")
                    self.reject("Native TRX contains error information", lambda: self.read(root))

    def test_only_native_informational_run_messages_are_admitted(self) -> None:
        root = native_fixture()
        summary = root.find(NAMESPACE + "ResultSummary")
        run_infos = ET.SubElement(summary, NAMESPACE + "RunInfos")
        info = ET.SubElement(run_infos, NAMESPACE + "RunInfo", outcome="Information")
        ET.SubElement(info, NAMESPACE + "Text").text = "Adapter message"
        self.assertEqual(IDENTITIES, self.read(root))
        for outcome in ("Warning", "Error", "Passed", "NotExecuted", "", None):
            with self.subTest(outcome=outcome):
                if outcome is None:
                    del info.attrib["outcome"]
                else:
                    info.set("outcome", outcome)
                self.reject("Native TRX contains unsupported run information", lambda: self.read(root))
        info.set("outcome", "Information")
        self.assertEqual(IDENTITIES, self.read(root))
        info.tag = "{urn:foreign}RunInfo"
        self.reject("Native TRX contains unsupported run information", lambda: self.read(root))
        info.tag = NAMESPACE + "RunInfo"
        summary.remove(run_infos)
        root.find(NAMESPACE + "TestDefinitions")[0].append(run_infos)
        self.reject("Native TRX contains unsupported run information", lambda: self.read(root))

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
    def test_native_sdk_actions_exclude_ancestor_build_imports(self) -> None:
        for subcommand, arguments in (
            ("restore", ("frozen.csproj", "--nologo")),
            ("test", ("frozen.csproj", "--no-restore", "--filter", "Frozen.Fact")),
            ("test", ("frozen.csproj", "--no-build", "--list-tests")),
        ):
            with self.subTest(subcommand=subcommand, arguments=arguments):
                self.assertEqual(
                    ["dotnet", subcommand, "-p:ImportDirectoryBuildProps=false",
                     "-p:ImportDirectoryBuildTargets=false", *arguments],
                    RUNNER.source_only_dotnet(subcommand, *arguments))
        self.reject("Unsupported native SDK action", lambda: RUNNER.source_only_dotnet("publish"))

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
         "-c", "user.name=Regional observation verifier controls", "-c", "user.email=regional-observation-controls@example.invalid", *args],
        cwd=repository, env=setup_environment(), input=data, capture_output=True, check=True)
    return completed.stdout.decode("utf-8").strip()


def file_snapshot(directory: Path) -> dict[str, bytes]:
    return {str(path.relative_to(directory)): path.read_bytes()
            for path in directory.rglob("*") if path.is_file()}


class WorkingTreeControls(ExactRejection):
    RAW = b"// Disposable source-binding fixture; not executable native evidence.\n"

    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="regional-observation-git-controls-")
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

    def test_masked_tracked_product_edits_are_rejected(self) -> None:
        tracked = self.repository / "product.txt"
        original = tracked.read_bytes()
        original_blob = fixture_git(self.repository, "rev-parse", ":product.txt")
        for flag, reverse in (("--assume-unchanged", "--no-assume-unchanged"),
                              ("--skip-worktree", "--no-skip-worktree")):
            with self.subTest(flag=flag):
                fixture_git(self.repository, "update-index", flag, "--", "product.txt")
                tracked.write_bytes(original + b"Hidden Product compiler input edit\n")
                try:
                    self.assertEqual(original_blob, fixture_git(self.repository, "rev-parse", ":product.txt"))
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


if __name__ == "__main__":
    unittest.main(verbosity=2)
