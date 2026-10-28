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


if __name__ == "__main__":
    unittest.main()
