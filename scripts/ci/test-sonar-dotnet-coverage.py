#!/usr/bin/env python3
"""Positive and negative controls for real OpenCover production evidence."""

from __future__ import annotations

import importlib.util
import io
import json
from contextlib import redirect_stdout
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

SCRIPT = Path(__file__).with_name("verify-sonar-dotnet-coverage.py")
SPEC = importlib.util.spec_from_file_location("sonar_coverage", SCRIPT)
assert SPEC and SPEC.loader
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class CoverageTests(unittest.TestCase):
    def setUp(self) -> None:
        self.work = tempfile.TemporaryDirectory()
        self.addCleanup(self.work.cleanup)
        self.root = Path(self.work.name)
        self.source = self.root / "src/App/Logic.cs"
        self.source.parent.mkdir(parents=True)
        self.source.write_text("public class Logic {}", encoding="utf-8")
        self.report = self.root / "coverage.opencover.xml"

    def write_report(self, *, path: Path | None = None, visits: str = "2",
                     fileid: str = "1") -> None:
        document = ET.Element("CoverageSession")
        module = ET.SubElement(ET.SubElement(document, "Modules"), "Module")
        files = ET.SubElement(module, "Files")
        ET.SubElement(files, "File", uid="1", fullPath=str(path or self.source))
        points = ET.SubElement(module, "SequencePoints")
        ET.SubElement(points, "SequencePoint", fileid=fileid, vc=visits)
        ET.SubElement(points, "SequencePoint", fileid="1", vc="0")
        ET.ElementTree(document).write(self.report, encoding="utf-8")

    def test_visited_and_unvisited_production_points_are_retained(self) -> None:
        self.write_report()
        result = MODULE.verify_report(self.report, self.root)
        self.assertEqual(result["productionFiles"], 1)
        self.assertEqual(result["sequencePoints"], 2)
        self.assertEqual(result["visitedSequencePoints"], 1)
        self.assertEqual(len(result["reportSha256"]), 64)

    def test_empty_or_summary_only_report_fails(self) -> None:
        self.report.write_text('<CoverageSession><Summary visitedSequencePoints="42"/></CoverageSession>')
        with self.assertRaises(ValueError):
            MODULE.verify_report(self.report, self.root)

    def test_deterministic_paths_bind_to_checkout_without_rewriting_native_bytes(self) -> None:
        self.write_report()
        document = ET.parse(self.report)
        document.find("./Modules/Module/Files/File").set("fullPath", "/_/src/App/Logic.cs")
        document.write(self.report, encoding="utf-8")
        original = self.report.read_bytes()
        result = MODULE.verify_report(self.report, self.root)
        self.assertEqual(result["productionFiles"], 1)
        self.assertEqual(result["sequencePoints"], 2)
        self.assertEqual(result["visitedSequencePoints"], 1)
        self.assertEqual(self.report.read_bytes(), original)

    def test_deterministic_paths_reject_aliases_missing_sources_and_test_credit(self) -> None:
        for path in ("/_/src/../App/Logic.cs", "/_/src//App/Logic.cs",
                     "/_/src/App/Missing.cs", "/_/tests/PassingTests.cs",
                     "/_/src/App/obj/Generated.cs", "/_/src/App/Logic.cs/../Logic.cs"):
            with self.subTest(path=path):
                self.write_report()
                document = ET.parse(self.report)
                document.find("./Modules/Module/Files/File").set("fullPath", path)
                document.write(self.report, encoding="utf-8")
                with self.assertRaises(ValueError):
                    MODULE.verify_report(self.report, self.root)

    def test_all_unvisited_points_fail(self) -> None:
        self.write_report(visits="0")
        with self.assertRaises(ValueError):
            MODULE.verify_report(self.report, self.root)

    def test_tests_only_coverage_cannot_satisfy_production_evidence(self) -> None:
        self.write_report(path=self.root / "tests/PassingTests.cs")
        with self.assertRaises(ValueError):
            MODULE.verify_report(self.report, self.root)

    def test_unknown_file_reference_cannot_supply_visits(self) -> None:
        self.write_report(fileid="99")
        with self.assertRaises(ValueError):
            MODULE.verify_report(self.report, self.root)

    def test_missing_production_source_fails(self) -> None:
        self.write_report(path=self.root / "src/App/Missing.cs")
        with self.assertRaises(ValueError):
            MODULE.verify_report(self.report, self.root)

    def test_another_checkout_cannot_supply_source_paths(self) -> None:
        self.write_report(path=self.root.parent / "other-checkout/src/Logic.cs")
        with self.assertRaises(ValueError):
            MODULE.verify_report(self.report, self.root)

    def test_malformed_or_wrong_format_report_fails(self) -> None:
        for content in ("broken", "<coverage/>"):
            with self.subTest(content=content):
                self.report.write_text(content)
                with self.assertRaises((ValueError, ET.ParseError)):
                    MODULE.verify_report(self.report, self.root)

    def test_generated_points_cannot_supply_production_visits(self) -> None:
        generated = self.root / "src/App/obj/Release/Generated.cs"
        generated.parent.mkdir(parents=True)
        generated.write_text("// generated", encoding="utf-8")
        self.write_report(path=generated)
        with self.assertRaises(ValueError):
            MODULE.verify_report(self.report, self.root)

    def test_missing_or_multiple_reports_fail_before_receipt(self) -> None:
        original_root = MODULE.ROOT
        self.addCleanup(setattr, MODULE, "ROOT", original_root)
        MODULE.ROOT = self.root
        with self.assertRaises(ValueError):
            MODULE.main()
        self.write_report()
        for folder in ("a", "b"):
            destination = self.root / "artifacts/sonar-dotnet" / folder
            destination.mkdir(parents=True)
            (destination / "coverage.opencover.xml").write_bytes(self.report.read_bytes())
        with self.assertRaises(ValueError):
            MODULE.main()
        self.assertFalse((self.root / "artifacts/sonar-dotnet/receipt.json").exists())

    def test_receipt_binds_real_report_and_git_checkout(self) -> None:
        original_root = MODULE.ROOT
        self.addCleanup(setattr, MODULE, "ROOT", original_root)
        MODULE.ROOT = self.root
        for args in (["init", "--quiet"], ["add", "src"],
                     ["-c", "user.name=Coverage control", "-c",
                      "user.email=coverage@example.test", "-c", "commit.gpgsign=false",
                      "commit", "--quiet", "-m", "Synthetic coverage fixture"]):
            subprocess.run(["git", *args], cwd=self.root, check=True, capture_output=True)
        self.write_report()
        destination = self.root / "artifacts/sonar-dotnet/993ff674-8ddf-4916-ac9a-b6f614e902a2"
        destination.mkdir(parents=True)
        (destination / "coverage.opencover.xml").write_bytes(self.report.read_bytes())
        output = io.StringIO()
        with redirect_stdout(output):
            MODULE.main()
        result = json.loads((destination.parent / "receipt.json").read_text())
        self.assertEqual(json.loads(output.getvalue()), result)
        expected_sha = subprocess.check_output(
            ["git", "rev-parse", "HEAD"], cwd=self.root, text=True).strip()
        self.assertEqual(result["sourceSha"], expected_sha)
        self.assertEqual(len(result["treeSha"]), 40)
        self.assertEqual(result["visitedSequencePoints"], 1)
        self.assertEqual(result["identicalVSTestCopies"], 0)
        self.assertEqual((destination.parent / "verified.opencover.xml").read_bytes(), self.report.read_bytes())
        self.assertEqual(result["reportSha256"], MODULE.verify_report(self.report, self.root)["reportSha256"])
        self.assertEqual(result["scope"], "DOTNET_COVERAGE_IMPORT_ONLY_NOT_RELEASE_ACCEPTANCE")

    def collector_fixture(self) -> tuple[Path, Path, Path]:
        self.write_report()
        folder = self.root / "artifacts/sonar-dotnet"
        primary = folder / "993ff674-8ddf-4916-ac9a-b6f614e902a2/coverage.opencover.xml"
        attachment = folder / "_runner_2026-10-28_22_40_45/In/runner/coverage.opencover.xml"
        for path in (primary, attachment):
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(self.report.read_bytes())
        return folder, primary, attachment

    def test_native_vstest_identical_attachment_is_not_a_second_collector(self) -> None:
        folder, primary, _ = self.collector_fixture()
        self.assertEqual(MODULE.select_collector_report(folder), (primary, 1))

    def test_changed_vstest_attachment_fails(self) -> None:
        folder, _, attachment = self.collector_fixture()
        attachment.write_bytes(attachment.read_bytes() + b" ")
        with self.assertRaisesRegex(ValueError, "differs"):
            MODULE.select_collector_report(folder)

    def test_two_collectors_fail_even_with_identical_bytes(self) -> None:
        folder, primary, _ = self.collector_fixture()
        second = folder / "22222222-2222-4222-8222-222222222222/coverage.opencover.xml"
        second.parent.mkdir()
        second.write_bytes(primary.read_bytes())
        with self.assertRaisesRegex(ValueError, "one fresh"):
            MODULE.select_collector_report(folder)

    def test_attachment_without_collector_fails(self) -> None:
        folder, primary, _ = self.collector_fixture()
        primary.unlink()
        with self.assertRaisesRegex(ValueError, "one fresh"):
            MODULE.select_collector_report(folder)

    def test_unexpected_nested_report_fails_even_with_identical_bytes(self) -> None:
        folder, primary, _ = self.collector_fixture()
        extra = folder / "unrecognized/coverage.opencover.xml"
        extra.parent.mkdir()
        extra.write_bytes(primary.read_bytes())
        with self.assertRaisesRegex(ValueError, "layout"):
            MODULE.select_collector_report(folder)

    def test_attachment_runner_identity_must_match_directory(self) -> None:
        folder, _, attachment = self.collector_fixture()
        wrong = folder / "_other_2026-10-28_22_40_45/In/runner/coverage.opencover.xml"
        wrong.parent.mkdir(parents=True)
        wrong.write_bytes(attachment.read_bytes())
        with self.assertRaisesRegex(ValueError, "layout"):
            MODULE.select_collector_report(folder)

    def test_invalid_visit_counts_fail(self) -> None:
        for visits in ("-1", "invalid"):
            with self.subTest(visits=visits):
                self.write_report(visits=visits)
                with self.assertRaises(ValueError):
                    MODULE.verify_report(self.report, self.root)


    def parallel_fixture(self):
        for args in (["init", "--quiet"], ["add", "src"],
                     ["-c", "user.name=Coverage control", "-c",
                      "user.email=coverage@example.test", "-c", "commit.gpgsign=false",
                      "commit", "--quiet", "-m", "Synthetic coverage fixture"]):
            subprocess.run(["git", *args], cwd=self.root, check=True, capture_output=True)
        sha = subprocess.check_output(["git", "rev-parse", "HEAD"],
                                      cwd=self.root, text=True).strip()
        tree = subprocess.check_output(["git", "rev-parse", "HEAD^{tree}"],
                                       cwd=self.root, text=True).strip()
        native = MODULE._native_verifier()
        folder = self.root / "artifacts/sonar-dotnet"
        folder.mkdir(parents=True)
        self.write_report()
        report = folder / "verified.opencover.xml"
        report.write_bytes(self.report.read_bytes())
        receipt = MODULE.verify_report(report, self.root)
        receipt.update(sourceSha=sha, treeSha=tree,
                       scope="DOTNET_COVERAGE_IMPORT_ONLY_NOT_RELEASE_ACCEPTANCE")
        (folder / "receipt.json").write_text(json.dumps(receipt))
        trx = b"synthetic TRX fixture for handoff binding"
        mapping = b"synthetic source mapping"
        (folder / "backend-tests.trx").write_bytes(trx)
        (folder / "deterministic-source-roots.original").write_bytes(mapping)
        digests = {}
        for name, project in native.ASSEMBLIES.items():
            for ext in ("dll", "pdb"):
                output = "bin/Release/net11.0/" + name + "." + ext
                data = (name + ext + sha).encode()
                for prefix in (project, "tests/Coglatas.Tests"):
                    path = self.root / prefix / output
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_bytes(data)
                digests["assemblies/" + name + "." + ext] = native.digest(data)
        mapping_path = self.root / "tests/Coglatas.Tests/bin/Release/net11.0/CoverletSourceRootsMapping_Coglatas.Tests"
        mapping_path.write_bytes(mapping)
        (folder / "pre-test-rebuild").mkdir()
        (folder / "pre-test-rebuild/outputs.json").write_text(
            json.dumps(dict(sourceSha=sha, artifacts=digests)))
        executed = dict(sourceSha=sha, runId="100", attempt="1",
                        productionBinariesUnchanged=True,
                        trxSha256=native.digest(trx), sourceMappingSha256=native.digest(mapping),
                        executedArtifacts=digests.copy())
        executed["executedArtifacts"]["assemblies/Coglatas.Tests.dll"] = "0" * 64
        (folder / "binary-provenance.json").write_text(json.dumps(executed))
        return folder, sha

    def test_parallel_handoff_requires_exact_execution_and_scanner_build(self):
        folder, sha = self.parallel_fixture()
        MODULE.verify_parallel_handoff(self.root, folder, sha, "100", "1")
        MODULE.verify_parallel_handoff(self.root, folder, sha, "100", "1", compare_build=True)
        for identity in (("0" * 40, "100", "1"), (sha, "101", "1"),
                         (sha, "100", "2"), (sha, "0", "1")):
            with self.subTest(identity=identity), self.assertRaises(ValueError):
                MODULE.verify_parallel_handoff(self.root, folder, *identity)

    def test_parallel_handoff_rejects_receipt_and_native_evidence_changes(self):
        folder, sha = self.parallel_fixture()
        for name in ("verified.opencover.xml", "backend-tests.trx",
                     "deterministic-source-roots.original", "receipt.json",
                     "pre-test-rebuild/outputs.json", "binary-provenance.json"):
            path = folder / name
            original = path.read_bytes()
            with self.subTest(name=name):
                path.write_bytes(b"")
                with self.assertRaises(ValueError):
                    MODULE.verify_parallel_handoff(self.root, folder, sha, "100", "1")
                path.write_bytes(original)
        for name in ("verified.opencover.xml", "backend-tests.trx",
                     "deterministic-source-roots.original"):
            path = folder / name
            original = path.read_bytes()
            with self.subTest(name=name):
                path.write_bytes(original + b" ")
                with self.assertRaises(ValueError):
                    MODULE.verify_parallel_handoff(self.root, folder, sha, "100", "1")
                path.write_bytes(original)
        path = folder / "binary-provenance.json"
        original = path.read_bytes()
        for key, replacement in (
            ("productionBinariesUnchanged", False),
            ("executedArtifacts", {}),
            ("sourceMappingSha256", "1" * 64),
            ("executedArtifacts", json.loads(original)["executedArtifacts"] |
             {"assemblies/Coglatas.Domain.dll": "1" * 64}),
        ):
            with self.subTest(key=key):
                value = json.loads(original)
                value[key] = replacement
                path.write_text(json.dumps(value))
                with self.assertRaises(ValueError):
                    MODULE.verify_parallel_handoff(self.root, folder, sha, "100", "1")
        path.write_bytes(original)
        MODULE.verify_parallel_handoff(self.root, folder, sha, "100", "1")

    def test_parallel_handoff_rejects_scanner_binary_copy_and_mapping_mismatches(self):
        folder, sha = self.parallel_fixture()
        paths = (
            "src/Coglatas.Domain/bin/Release/net11.0/Coglatas.Domain.dll",
            "tests/Coglatas.Tests/bin/Release/net11.0/Coglatas.Domain.pdb",
            "tests/Coglatas.Tests/bin/Release/net11.0/CoverletSourceRootsMapping_Coglatas.Tests",
        )
        for name in paths:
            path = self.root / name
            original = path.read_bytes()
            with self.subTest(name=name):
                path.write_bytes(original + b"changed")
                with self.assertRaises(ValueError):
                    MODULE.verify_parallel_handoff(self.root, folder, sha, "100", "1", compare_build=True)
                path.write_bytes(original)
        MODULE.verify_parallel_handoff(self.root, folder, sha, "100", "1", compare_build=True)


if __name__ == "__main__":
    unittest.main()
