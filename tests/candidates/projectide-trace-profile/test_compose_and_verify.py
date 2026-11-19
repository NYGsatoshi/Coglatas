#!/usr/bin/env python3
"""Admission regressions; copied inputs and disposable SDK probes are not Product execution."""
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
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("trace_profile_candidate_runner", HERE / "compose_and_verify.py")
assert SPEC is not None and SPEC.loader is not None
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)
NAMESPACE = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
SOURCE_BYTES = (HERE / Path(RUNNER.SOURCE).name).read_bytes()
SOURCE_BLOB = "aa5420d0237cbba892983ad627997e1676c45286"
EXPECTED_COUNT = 32
PASS_STATUS = "SYNTHETIC_MODEL_TRACE_PROFILE_PASSED"
NATIVE_TRX_BYTES = b""
NATIVE_DISCOVERY = ""


def fixture_environment() -> dict[str, str]:
    # Independent of the function under test, including before fixture writes.
    return {key: value for key, value in os.environ.items() if not key.upper().startswith("GIT_")}


def fixture_git(repository: Path, *args: str) -> str:
    completed = subprocess.run(
        ["git", "-c", "core.autocrlf=false", "-c", "commit.gpgsign=false",
         "-c", "core.hooksPath=" + str(repository / "empty-hooks"),
         "-c", "user.name=Git isolation controls",
         "-c", "user.email=git-isolation-controls@example.invalid", *args],
        cwd=repository, env=fixture_environment(), capture_output=True, text=True, check=True)
    return completed.stdout.strip()


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
        temporary = tempfile.TemporaryDirectory(prefix="trace_profile-git-controls-")
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
            if stage != "--version":
                self.assertIn("-p:ImportDirectoryBuildProps=false", args)
                self.assertIn("-p:ImportDirectoryBuildTargets=false", args)
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


class NestedEvidenceControls(unittest.TestCase):
    """Malformed copies retain the original outer counters and identity roster."""

    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="nested-native-controls-")
        self.addCleanup(temporary.cleanup)
        self.path = Path(temporary.name) / "copied.trx"
        self.assertEqual(sorted(RUNNER.EXPECTED_IDENTITIES), self.read(self.original()))

    def original(self) -> ET.Element:
        return ET.fromstring(NATIVE_TRX_BYTES)

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
        self.git("add", "--", ".")
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
            self.git("add", "--", names[-1])
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

    SCRIPTS = ("tests/candidates/projectide-trace-profile/compose_and_verify.py",
               "tests/candidates/projectide-trace-profile/test_compose_and_verify.py")
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
        workflow = RUNNER.ROOT / ".github/workflows/projectide-trace-profile-advisory.yml"
        lines = workflow.read_text(encoding="utf-8").splitlines()
        self.blocks = []
        for index, line in enumerate(lines):
            if not line.strip().startswith("python -I -B tests/candidates/projectide-trace-profile/"):
                continue
            first = index
            while first and lines[first - 1].strip().startswith(("$committed =", "$working =", "if ($LASTEXITCODE")):
                first -= 1
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


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--native-trx", type=Path, required=True)
    parser.add_argument("--compiled-discovery", type=Path, required=True)
    arguments, unittest_arguments = parser.parse_known_args()
    NATIVE_TRX_BYTES = arguments.native_trx.read_bytes()
    NATIVE_DISCOVERY = arguments.compiled_discovery.read_bytes().decode("utf-8")
    validate_retained_inputs()
    print("Admission controls reuse retained TRX SHA256 " + hashlib.sha256(NATIVE_TRX_BYTES).hexdigest()
          + "; copied-input controls do not execute Product tests.", flush=True)
    unittest.main(argv=[sys.argv[0], *unittest_arguments])
