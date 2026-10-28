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
        destination = self.root / "artifacts/sonar-dotnet/collector"
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
        self.assertEqual(result["reportSha256"], MODULE.verify_report(self.report, self.root)["reportSha256"])
        self.assertEqual(result["scope"], "DOTNET_COVERAGE_IMPORT_ONLY_NOT_RELEASE_ACCEPTANCE")

    def test_invalid_visit_counts_fail(self) -> None:
        for visits in ("-1", "invalid"):
            with self.subTest(visits=visits):
                self.write_report(visits=visits)
                with self.assertRaises(ValueError):
                    MODULE.verify_report(self.report, self.root)


if __name__ == "__main__":
    unittest.main()
