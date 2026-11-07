#!/usr/bin/env python3
"""Mutation controls for supplemental native Windows coverage provenance."""

import importlib.util
import io
import json
import shutil
import subprocess
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location(
    "windows_coverage", Path(__file__).with_name("verify-sonar-windows-coverage.py"))
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)
SHA = "a" * 40
GUID = "11111111-1111-1111-1111-111111111111"
REPORT = b'''<coverage><sources/><packages><package><classes>
<class filename="/_/tools/Example.cs"><lines>
<line number="1" hits="2" branch="True" condition-coverage="50% (1/2)"/>
<line number="2" hits="0"/>
</lines></class></classes></package></packages></coverage>'''
TRX = b'''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
<Results><UnitTestResult outcome="Passed" testId="synthetic-unit"/></Results>
<TestDefinitions><UnitTest id="synthetic-unit"><TestMethod codeBase="@ASSEMBLY@"/></UnitTest></TestDefinitions>
<ResultSummary><Counters total="1" executed="1" passed="1" failed="0" notExecuted="0"/></ResultSummary>
</TestRun>'''
PROJECT = b'<PackageReference Include="coverlet.collector" Version="10.1.0" />'


class WindowsCoverageTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.folder = self.root / "evidence"
        self.folder.mkdir()
        self.primary = self.folder / GUID / "coverage.cobertura.xml"
        self.primary.parent.mkdir()
        self.primary.write_bytes(REPORT)
        self.assembly_path = str(self.root / "tests/Coglatas.Tests/bin/Release/net10.0/Coglatas.Tests.dll")
        (self.folder / "windows-tests.trx").write_bytes(TRX.replace(b"@ASSEMBLY@", self.assembly_path.encode()))
        self.sources = {"tools/Example.cs": b"one\ntwo\n",
                        "tests/Coglatas.Tests/Coglatas.Tests.csproj": PROJECT}
        for name, project in MODULE.ASSEMBLIES.items():
            for extension in ("dll", "pdb"):
                data = (name + SHA + extension).encode()
                output = "bin/Release/net10.0/" + name + "." + extension
                for path in {self.root / project / output,
                             self.root / "tests/Coglatas.Tests" / output}:
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_bytes(data)

    def qualify(self, capture):
        # Synthetic unit fixtures test tamper rejection; they are not native evidence.
        with patch.object(MODULE, "git_reader", return_value=("b" * 40, self.sources.__getitem__)):
            return MODULE.qualify(self.root, self.folder, Path("unused"), SHA, "123", "1", capture)

    def test_original_zero_hits_conditions_and_source_hashes_survive(self):
        receipt = self.qualify(True)
        self.assertEqual(receipt["coverage"]["zeroHitLines"], 1)
        self.assertEqual(receipt["coverage"]["coveredLines"], 1)
        self.assertEqual(receipt["coverage"]["conditions"], 2)
        self.assertEqual(receipt["coverage"]["coveredConditions"], 1)
        self.assertEqual(len(receipt["executedArtifacts"]), 14)
        self.assertEqual(receipt, self.qualify(False))
        self.assertEqual((self.folder / "verified.cobertura.xml").read_bytes(), REPORT)

    def test_native_vstest_attachment_is_verified_but_not_imported_twice(self):
        attachment = self.folder / "Example_User_runner_2026-11-07_00_00_00/In/runner/coverage.cobertura.xml"
        attachment.parent.mkdir(parents=True)
        attachment.write_bytes(REPORT)
        receipt = self.qualify(True)
        self.assertEqual(receipt["identicalVSTestCopies"], [attachment.relative_to(self.folder).as_posix()])
        attachment.write_bytes(REPORT.replace(b'hits="0"', b'hits="1"'))
        with self.assertRaisesRegex(ValueError, "copy differs"):
            self.qualify(False)

    def test_extra_collector_or_unexpected_report_layout_is_rejected(self):
        extra = self.folder / "extra/coverage.cobertura.xml"
        extra.parent.mkdir()
        extra.write_bytes(REPORT)
        with self.assertRaisesRegex(ValueError, "layout"):
            self.qualify(True)
        extra.unlink()
        extra.parent.rename(self.folder / GUID.replace("1", "2"))
        (self.folder / GUID.replace("1", "2") / extra.name).write_bytes(REPORT)
        with self.assertRaisesRegex(ValueError, "one fresh"):
            self.qualify(True)

    def test_source_paths_ranges_counts_and_xml_fail_closed(self):
        mutations = [
            (b"/_/tools/Example.cs", b"C:/agent/tools/Example.cs"),
            (b"/_/tools/Example.cs", b"/_/tools/../Example.cs"),
            (b"/_/tools/Example.cs", b"/_/tools//Example.cs"),
            (b"/_/tools/Example.cs", b"/_/tests/Example.cs"),
            (b"/_/tools/Example.cs", b"/_/tools/obj/Example.cs"),
            (b'number="2"', b'number="3"'),
            (b'hits="0"', b'hits="-1"'),
            (b'hits="2"', b'hits="0"'),
            (b"50% (1/2)", b"150% (3/2)"),
            (b"50% (1/2)", b"unknown"),
            (b'branch="True"', b'branch="unknown"'),
            (b"<sources/>", b"<sources><source>C:/agent</source></sources>"),
            (b"<coverage>", b'<!DOCTYPE coverage [<!ENTITY x "x">]><coverage>'),
        ]
        for old, new in mutations:
            with self.subTest(new=new), self.assertRaises(ValueError):
                MODULE.verify_report(REPORT.replace(old, new), self.sources.__getitem__)

    def test_empty_skipped_failed_and_falsely_counted_trx_are_rejected(self):
        for raw in (TRX.replace(b'outcome="Passed"', b'outcome="NotExecuted"'),
                    TRX.replace(b'outcome="Passed"', b'outcome="Failed"'),
                    TRX.replace(b'total="1"', b'total="2"'),
                    TRX.replace(b'notExecuted="0"', b'notExecuted="1"'),
                    b'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"/>'):
            with self.subTest(raw=raw), self.assertRaises(ValueError):
                MODULE.verify_trx(raw)

    def test_generated_virtual_documents_are_retained_without_git_source_credit(self):
        filename = (b'C:/runner/src/Coglatas.Application/obj/Release/net10.0/'
                    b'System.Text.RegularExpressions.Generator/'
                    b'System.Text.RegularExpressions.Generator.RegexGenerator/RegexGenerator.g.cs')
        generated = b'<class filename="' + filename + b'"><lines><line number="3" hits="0"/></lines></class>'
        result = MODULE.verify_report(REPORT.replace(b'</classes>', generated + b'</classes>'), self.sources.__getitem__)
        self.assertEqual(len(result["sources"]), 1)
        self.assertEqual(result["lineEntries"], 2)
        self.assertEqual(result["unmappedGeneratedSources"][filename.decode()], [{"number": "3", "hits": "0"}])
        deterministic = generated.replace(b'C:/runner/', b'/_/')
        mapped = MODULE.verify_report(REPORT.replace(b'</classes>', deterministic + b'</classes>'), self.sources.__getitem__)
        self.assertEqual(mapped["sources"], result["sources"])
        self.assertEqual(mapped["lineEntries"], result["lineEntries"])
        self.assertEqual(len(mapped["unmappedGeneratedSources"]), 1)
        with self.assertRaisesRegex(ValueError, "Nondeterministic"):
            MODULE.verify_report(REPORT.replace(b'</classes>', generated.replace(b'RegexGenerator.g.cs', b'Handwritten.cs') + b'</classes>'), self.sources.__getitem__)

    def test_trx_must_bind_the_executed_binary_and_case_definitions(self):
        raw = (self.folder / "windows-tests.trx").read_bytes()
        self.assertEqual(MODULE.verify_trx(raw, self.assembly_path), 1)
        for mutated in (raw.replace(b'codeBase="', b'codeBase="other-'),
                        raw.replace(b'id="synthetic-unit"', b'id="wrong-unit"'),
                        raw.replace(b'<TestMethod ', b'<MissingMethod ')):
            with self.subTest(mutated=mutated), self.assertRaises(ValueError):
                MODULE.verify_trx(mutated, self.assembly_path)

    def test_manifest_publication_raw_source_and_assembly_tampering_are_rejected(self):
        self.qualify(True)
        paths = [self.folder / "verified.cobertura.xml",
                 self.primary, self.folder / "assemblies/Coglatas.Web.dll",
                 self.folder / "windows-tests.trx"]
        for path in paths:
            original = path.read_bytes()
            path.write_bytes(original + b" ")
            with self.subTest(path=path.name), self.assertRaises((ValueError, KeyError)):
                self.qualify(False)
            path.write_bytes(original)
        self.sources["tools/Example.cs"] += b"changed\n"
        with self.assertRaisesRegex(ValueError, "receipt"):
            self.qualify(False)

    def test_wrong_run_attempt_revision_and_collector_are_rejected(self):
        self.qualify(True)
        path = self.folder / "receipt.json"
        original = json.loads(path.read_bytes())
        for key, value in (("runId", "124"), ("attempt", "2"), ("sourceSha", "c" * 40),
                           ("treeSha", "d" * 40), ("collectorVersion", "0")):
            path.write_text(json.dumps({**original, key: value}), encoding="utf-8")
            with self.subTest(key=key), self.assertRaisesRegex(ValueError, "receipt"):
                self.qualify(False)
        path.write_text(json.dumps(original), encoding="utf-8")
        self.sources["tests/Coglatas.Tests/Coglatas.Tests.csproj"] = PROJECT.replace(b"10.1.0", b"10.0.0")
        with self.assertRaisesRegex(ValueError, "collector version"):
            self.qualify(False)

    def test_capture_rejects_different_executed_assembly(self):
        (self.root / "src/Coglatas.Web/bin/Release/net10.0/Coglatas.Web.dll").write_bytes(b"different")
        with self.assertRaisesRegex(ValueError, "Executed assembly differs"):
            self.qualify(True)

    def test_capture_rejects_missing_full_revision(self):
        for path in (self.root / "src/Coglatas.Domain/bin/Release/net10.0/Coglatas.Domain.dll",
                     self.root / "tests/Coglatas.Tests/bin/Release/net10.0/Coglatas.Domain.dll"):
            path.write_bytes(b"old revision")
        with self.assertRaisesRegex(ValueError, "full source revision"):
            self.qualify(True)

    def test_unsafe_evidence_and_repository_local_git_are_rejected(self):
        for relative in ("../outside", "/outside", "a//b", "a/./b", "a\\b", "a:b", "a\0b"):
            with self.subTest(relative=relative), self.assertRaises(ValueError):
                MODULE.owned_bytes(self.folder, relative)
        executable = self.root / "git.exe"
        executable.write_bytes(b"untrusted")
        with self.assertRaisesRegex(ValueError, "Repository-local Git"):
            MODULE.git_reader(self.root, executable, SHA)
        with self.assertRaisesRegex(ValueError, "absolute host Git"):
            MODULE.git_reader(self.root, Path("git"), SHA)

    def test_cli_receipts_bind_actual_git_tree_bytes_and_expected_provider_identity(self):
        executable = Path(shutil.which("git")).resolve()
        def git(*args):
            return subprocess.check_output([str(executable), *args], cwd=self.root,
                                           stderr=subprocess.PIPE).decode().strip()
        git("init", "--quiet")
        git("config", "core.autocrlf", "false")
        git("config", "user.name", "Synthetic coverage control")
        git("config", "user.email", "coverage-control@example.invalid")
        for path, data in self.sources.items():
            destination = self.root / path
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(data)
        git("add", "tools/Example.cs", "tests/Coglatas.Tests/Coglatas.Tests.csproj")
        git("commit", "--quiet", "-m", "Create synthetic source identity fixture")
        actual_sha = git("rev-parse", "HEAD")
        for path in self.root.glob("**/bin/Release/net10.0/*"):
            path.write_bytes(path.read_bytes().replace(SHA.encode(), actual_sha.encode()))
        def cli(mode, sha=actual_sha, run="123", attempt="1"):
            arguments = ["verify-sonar-windows-coverage.py", mode, "--folder", str(self.folder),
                         "--git", str(executable), "--sha", sha, "--run", run, "--attempt", attempt]
            with patch.object(MODULE, "ROOT", self.root), patch("sys.argv", arguments), redirect_stdout(io.StringIO()) as stream:
                MODULE.main()
                return json.loads(stream.getvalue())
        result = cli("capture")
        self.assertEqual(result["sourceSha"], actual_sha)
        self.assertEqual(result["treeSha"], git("rev-parse", "HEAD^{tree}"))
        self.assertEqual(result, cli("consume"))
        with self.assertRaisesRegex(ValueError, "Checkout differs"):
            cli("consume", sha=SHA)
        for run, attempt in (("124", "1"), ("123", "2")):
            with self.subTest(run=run, attempt=attempt), self.assertRaisesRegex(ValueError, "receipt"):
                cli("consume", run=run, attempt=attempt)
        for run, attempt in (("0", "1"), ("123", "0"), ("123x", "1")):
            with self.subTest(run=run, attempt=attempt), self.assertRaisesRegex(ValueError, "provider"):
                cli("consume", run=run, attempt=attempt)
        (self.root / "tools/Example.cs").write_bytes(b"changed checkout source\n")
        with self.assertRaisesRegex(ValueError, "source bytes differ"):
            cli("consume")


if __name__ == "__main__":
    unittest.main()
