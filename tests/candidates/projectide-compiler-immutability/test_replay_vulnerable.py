#!/usr/bin/env python3
"""Historical admission regressions use copied originals, never substitute native execution."""
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
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

WORKFLOW_PATH = ".github/workflows/projectide-compiler-immutability-historical-red.yml"
HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("historical_compiler_runner", HERE / "replay_vulnerable.py")
assert SPEC is not None and SPEC.loader is not None
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)
NAMESPACE = RUNNER.TRX_NAMESPACE
SOURCE_BYTES = (HERE / "SecurityCompilerImmutableBoundaryCandidateTests.cs").read_bytes()
NATIVE_TRX_BYTES = b""
NATIVE_DISCOVERY = ""


def native_fixture() -> ET.Element:
    return ET.fromstring(NATIVE_TRX_BYTES)


class HistoricalNativeInventoryTests(unittest.TestCase):
    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="historical-native-controls-")
        self.addCleanup(temporary.cleanup)
        self.path = Path(temporary.name) / "historical-red.trx"

    def admit(self, root: ET.Element | None = None) -> list[str]:
        self.path.write_bytes(NATIVE_TRX_BYTES if root is None else ET.tostring(root))
        return RUNNER.native_inventory(self.path)

    def test_original_native_failures_discovery_and_source_are_preserved(self) -> None:
        self.assertEqual(sorted(RUNNER.EXPECTED_IDENTITIES), self.admit())
        self.assertEqual(self.admit(), RUNNER.compiled_inventory(NATIVE_DISCOVERY))
        blob = hashlib.sha1(b"blob " + str(len(SOURCE_BYTES)).encode() + b"\0" + SOURCE_BYTES).hexdigest()
        self.assertEqual("067043361a2e7a83613886234e1afc0b499d70e9", blob)

    def test_nested_results_are_rejected_even_when_outer_failure_roster_is_intact(self) -> None:
        for outcome in ("Passed", "Failed", "NotExecuted"):
            with self.subTest(outcome=outcome):
                root = native_fixture()
                inner = ET.SubElement(root.find(NAMESPACE + "Results")[0], NAMESPACE + "InnerResults")
                ET.SubElement(inner, NAMESPACE + "UnitTestResult", outcome=outcome)
                with self.assertRaisesRegex(ValueError, "nested result records"):
                    self.admit(root)

    def test_duplicate_inventory_and_counters_are_rejected(self) -> None:
        for name in ("Results", "TestDefinitions", "TestEntries", "ResultSummary", "Counters"):
            with self.subTest(name=name):
                root = native_fixture()
                parent = root.find(NAMESPACE + "ResultSummary") if name == "Counters" else root
                parent.append(copy.deepcopy(parent.find(NAMESPACE + name)))
                with self.assertRaises(ValueError):
                    self.admit(root)

    def test_native_results_require_exact_fqns_methods_and_id_bijection(self) -> None:
        for kind in ("namespace", "method", "entry", "definition-name", "result-id", "case-outcome"):
            with self.subTest(kind=kind):
                root = native_fixture()
                case = root.find(NAMESPACE + "Results")[0]
                method = root.find(NAMESPACE + "TestDefinitions")[0].find(NAMESPACE + "TestMethod")
                if kind == "namespace":
                    root.tag = "TestRun"
                elif kind == "method":
                    method.set("className", "Coglatas.Tests.ProjectIde")
                    method.set("name", "SecurityCompilerImmutableBoundaryCandidateTests." + method.get("name"))
                elif kind == "entry":
                    root.find(NAMESPACE + "TestEntries")[0].set("executionId", "OTHER_EXECUTION")
                elif kind == "definition-name":
                    root.find(NAMESPACE + "TestDefinitions")[0].set("name", "Foreign.Alias")
                elif kind == "result-id":
                    case.set("testId", root.find(NAMESPACE + "Results")[1].get("testId"))
                else:
                    case.set("outcome", "Passed")
                with self.assertRaises(ValueError):
                    self.admit(root)

    def test_incomplete_counters_and_unexpected_run_failures_are_rejected(self) -> None:
        for kind in ("counter", "extra-run-error", "foreign-run-error", "duplicate-notification"):
            with self.subTest(kind=kind):
                root = native_fixture()
                summary = root.find(NAMESPACE + "ResultSummary")
                infos = summary.find(NAMESPACE + "RunInfos")
                if kind == "counter":
                    summary.find(NAMESPACE + "Counters").set("aborted", "1")
                elif kind == "extra-run-error":
                    ET.SubElement(infos, NAMESPACE + "RunInfo", outcome="Error")
                elif kind == "foreign-run-error":
                    infos[0].find(NAMESPACE + "Text").text = "Unrelated test-host crash"
                else:
                    infos[0].find(NAMESPACE + "Text").text = infos[1].find(NAMESPACE + "Text").text
                with self.assertRaises(ValueError):
                    self.admit(root)

    def test_misplaced_or_nested_run_errors_cannot_hide_outside_the_admitted_notifications(self) -> None:
        for kind in ("RunInfo", "ErrorInfo"):
            for location in ("root", "summary", "notification", "result", "assertion"):
                with self.subTest(kind=kind, location=location):
                    root = native_fixture()
                    summary = root.find(NAMESPACE + "ResultSummary")
                    result = root.find(NAMESPACE + "Results")[0]
                    parent = (root if location == "root" else summary if location == "summary" else
                              summary.find(NAMESPACE + "RunInfos")[0] if location == "notification" else
                              result.find(NAMESPACE + "Output").find(NAMESPACE + "ErrorInfo")
                              if location == "assertion" else result)
                    info = ET.SubElement(parent, NAMESPACE + kind, outcome="Error")
                    ET.SubElement(info, NAMESPACE + "Text").text = "Unrelated infrastructure failure"
                    expected = ("exactly the original xUnit failure notifications" if kind == "RunInfo" else
                                "unrelated run-level error" if location in ("summary", "notification") else
                                "error records outside the original assertion failures")
                    with self.assertRaisesRegex(ValueError, expected):
                        self.admit(root)

    def test_invariant_and_exact_native_frames_cannot_be_aliased(self) -> None:
        for kind in ("message", "helper-suffix", "class-suffix", "caller-suffix", "caller-alias", "missing-frame"):
            with self.subTest(kind=kind):
                root = native_fixture()
                case = root.find(NAMESPACE + "Results")[0]
                error = case.find(NAMESPACE + "Output").find(NAMESPACE + "ErrorInfo")
                stack = error.find(NAMESPACE + "StackTrace")
                if kind == "message":
                    error.find(NAMESPACE + "Message").text = "Different assertion: " + RUNNER.REFUSAL
                elif kind == "helper-suffix":
                    stack.text = stack.text.replace(".AssertSealed[", ".AssertSealedAlias[")
                elif kind == "class-suffix":
                    stack.text = stack.text.replace(RUNNER.TEST_CLASS_FQN, RUNNER.TEST_CLASS_FQN + "Alias")
                elif kind == "caller-suffix":
                    stack.text = stack.text.replace(case.get("testName") + "()", case.get("testName") + "Alias()")
                elif kind == "caller-alias":
                    stack.text = stack.text.replace(case.get("testName") + "()", "Foreign." + case.get("testName") + "()")
                else:
                    stack.text = "   at Foreign.Helper()"
                with self.assertRaisesRegex(ValueError, "exact original SyncRoot invariant and native frames"):
                    self.admit(root)

    def test_discovery_requires_only_the_three_full_original_names(self) -> None:
        for discovery in (NATIVE_DISCOVERY + "    Foreign.Namespace.Test\n",
                          NATIVE_DISCOVERY.replace(RUNNER.TEST_CLASS_FQN + ".", ""),
                          NATIVE_DISCOVERY.replace(RUNNER.NAMES[0], RUNNER.NAMES[1])):
            with self.assertRaises(ValueError):
                RUNNER.compiled_inventory(discovery)


class HistoricalMainTests(unittest.TestCase):
    """Run the real historical main with real Git and controlled dotnet outputs."""

    def run_main(self, scenario: str) -> tuple[Exception | None, dict, list[str]]:
        with tempfile.TemporaryDirectory(prefix="historical-main-control-") as directory:
            root = Path(directory)
            repository, target, evidence = root / "security", root / "product", root / "evidence"
            repository.mkdir()
            def git(*arguments):
                return subprocess.run(["git", "-c", "core.autocrlf=false", *arguments], cwd=repository,
                                      capture_output=True, text=True, check=True,
                                      env=RUNNER.local_git_environment()).stdout.strip()
            git("init", "--quiet", "--template=")
            git("config", "user.name", "Historical fixture")
            git("config", "user.email", "historical-fixture@example.invalid")
            git("config", "commit.gpgsign", "false")
            (repository / "product.txt").write_text("Original Product\n", encoding="utf-8")
            (repository / ".gitignore").write_text("ignored/\n", encoding="utf-8")
            git("add", "--all")
            git("commit", "--quiet", "-m", "Historical Product fixture")
            product = git("rev-parse", "HEAD")
            source = repository / RUNNER.SOURCE
            source.parent.mkdir(parents=True)
            source.write_bytes(SOURCE_BYTES)
            git("add", "--", RUNNER.SOURCE)
            git("commit", "--quiet", "-m", "Independent historical Test fixture")
            original_command = RUNNER.command
            calls = []
            def controlled_command(arguments, cwd, log=None, success=True):
                if arguments[0] != "dotnet":
                    return original_command(arguments, cwd, log, success)
                stage = "discovery" if "--list-tests" in arguments else arguments[1]
                calls.append(stage)
                if stage in ("restore", "test", "discovery"):
                    self.assertIn("-p:ImportDirectoryBuildProps=false", arguments)
                    self.assertIn("-p:ImportDirectoryBuildTargets=false", arguments)
                output = "10.0.403\n" if stage == "--version" else ""
                code = 1 if stage == "test" else 0
                if stage == "restore":
                    if scenario == "source-after-restore":
                        (target / RUNNER.DESTINATION).write_bytes(SOURCE_BYTES + b"\n// Hidden source drift")
                    elif scenario == "masked-after-restore":
                        original_command(["git", "update-index", "--assume-unchanged", "--", "product.txt"], target)
                        (target / "product.txt").write_text("Changed Product\n", encoding="utf-8")
                    elif scenario == "ignored-after-restore":
                        (target / "ignored").mkdir()
                        (target / "ignored/Injected.cs").write_text("// Hidden input\n", encoding="utf-8")
                if stage == "test":
                    (evidence / "historical-red.trx").write_bytes(NATIVE_TRX_BYTES)
                    if scenario == "source-after-test":
                        (target / RUNNER.DESTINATION).write_bytes(SOURCE_BYTES + b"\n// Test drift")
                    if scenario in ("exit-zero", "exit-two"):
                        code = 0 if scenario == "exit-zero" else 2
                if stage == "discovery":
                    output = NATIVE_DISCOVERY
                    if scenario == "source-after-discovery":
                        (target / RUNNER.DESTINATION).write_bytes(SOURCE_BYTES + b"\n// Discovery drift")
                if log is not None:
                    log.write_text(output, encoding="utf-8")
                return subprocess.CompletedProcess(arguments, code, stdout=output, stderr="")
            caught = None
            try:
                with patch.object(RUNNER, "ROOT", repository), patch.object(RUNNER, "VULNERABLE", product), \
                        patch.object(RUNNER, "command", controlled_command), \
                        patch.object(sys, "argv", ["replay_vulnerable.py", "--vulnerable-head", product,
                                                  "--worktree", str(target), "--evidence", str(evidence)]), \
                        redirect_stdout(io.StringIO()):
                    try:
                        RUNNER.main()
                    except Exception as error:
                        caught = error
                receipt = json.loads((evidence / "receipt.json").read_text(encoding="utf-8"))
                for flag in ("requiredCheckQualified", "independentReviewComplete", "nativeUiQualified",
                             "productionQualified", "trustQualified", "releaseQualified"):
                    self.assertIs(False, receipt[flag])
                return caught, receipt, calls
            finally:
                if target.exists():
                    git("worktree", "remove", "--force", str(target))

    def test_expected_native_failure_and_exact_copied_original_remain_admitted(self) -> None:
        error, receipt, calls = self.run_main("valid")
        self.assertIsNone(error)
        self.assertEqual("EXPECTED_HISTORICAL_RED_VERIFIED", receipt["status"])
        self.assertEqual(["--version", "restore", "test", "discovery"], calls)
        self.assertEqual(3, receipt["historicalXunit"]["failed"])

    def test_real_composition_drift_is_refused_at_every_sdk_boundary(self) -> None:
        for scenario in ("source-after-restore", "masked-after-restore", "ignored-after-restore",
                         "source-after-test", "source-after-discovery"):
            with self.subTest(scenario=scenario):
                error, receipt, calls = self.run_main(scenario)
                self.assertIsInstance(error, ValueError)
                self.assertEqual("FAILED_OR_INCOMPLETE", receipt["status"])
                self.assertNotIn("historicalXunit", receipt)
                if scenario.endswith("restore"):
                    self.assertEqual(["--version", "restore"], calls)

    def test_native_pass_or_abort_never_receives_historical_failure_credit(self) -> None:
        for scenario in ("exit-zero", "exit-two"):
            with self.subTest(scenario=scenario):
                error, receipt, calls = self.run_main(scenario)
                self.assertIsInstance(error, ValueError)
                self.assertEqual(["--version", "restore", "test"], calls)
                self.assertNotIn("historicalXunit", receipt)


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
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--native-trx", type=Path, required=True)
    parser.add_argument("--compiled-discovery", type=Path, required=True)
    arguments, remaining = parser.parse_known_args()
    NATIVE_TRX_BYTES = arguments.native_trx.read_bytes()
    NATIVE_DISCOVERY = arguments.compiled_discovery.read_text(encoding="utf-8")
    if RUNNER.native_inventory(arguments.native_trx) != RUNNER.compiled_inventory(NATIVE_DISCOVERY):
        raise ValueError("Retained original historical native and compiled identities differ")
    print("Historical admission controls use original TRX SHA256 " + hashlib.sha256(NATIVE_TRX_BYTES).hexdigest()
          + "; copied-report and disposable SDK controls receive no Product xUnit execution credit.")
    unittest.main(argv=[sys.argv[0], *remaining], verbosity=2)
