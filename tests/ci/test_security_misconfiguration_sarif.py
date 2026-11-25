#!/usr/bin/env python3
"""Regression tests for fail-closed, content-safe Trivy SARIF handling."""

from __future__ import annotations

import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

MODULE = Path(__file__).resolve().parents[2] / "scripts/ci/verify_security_misconfiguration_sarif.py"
SPEC = importlib.util.spec_from_file_location("misconfiguration_sarif", MODULE)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError("Cannot import SARIF validator")
validator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(validator)


def report(results=None, *, tool="Trivy"):
    return {"version": "2.1.0", "runs": [{"tool": {"driver": {"name": tool}}, "results": results if results is not None else []}]}


class EvidenceTests(unittest.TestCase):
    def check(self, document, expected=None):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "report.sarif"
            path.write_text(json.dumps(document), encoding="utf-8")
            if expected is None:
                with self.assertRaises(validator.EvidenceError):
                    validator.count_findings(path)
            else:
                self.assertEqual(expected, validator.count_findings(path))

    def test_empty_results_are_valid_not_complete_coverage(self):
        self.check(report(), 0)

    def test_finding_count(self):
        self.check(report([{"ruleId": "AVD-TEST-0001", "message": {"text": "PRIVATE_PAYLOAD"}}]), 1)

    def test_invalid_version_rejected(self):
        self.check({"version": "2.0.0", "runs": report()["runs"]})

    def test_missing_runs_rejected(self):
        self.check({"version": "2.1.0"})
        self.check({"version": "2.1.0", "runs": []})

    def test_wrong_scanner_rejected(self):
        self.check(report(tool="OtherTool"))

    def test_missing_results_rejected(self):
        self.check({"version": "2.1.0", "runs": [{"tool": {"driver": {"name": "Trivy"}}}]})

    def test_malformed_result_rejected(self):
        self.check(report([{"message": {"text": "missing rule"}}]))
        self.check(report([None]))

    def test_unavailable_report_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(validator.EvidenceError):
                validator.count_findings(Path(directory) / "missing.sarif")

    def test_duplicate_keys_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "duplicate.sarif"
            path.write_text('{"version":"2.1.0","version":"2.1.0","runs":[]}', encoding="utf-8")
            with self.assertRaisesRegex(validator.EvidenceError, "SARIF_DUPLICATE_KEY"):
                validator.count_findings(path)

    def test_summary_is_payload_free(self):
        with tempfile.TemporaryDirectory() as directory:
            summary = Path(directory) / "summary.md"
            validator.write_summary(summary, 1, "a" * 40)
            text = summary.read_text(encoding="utf-8")
            self.assertIn("ADVISORY", text)
            self.assertIn("1", text)
            self.assertNotIn("PRIVATE_PAYLOAD", text)

    def test_invalid_commit_identity_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(validator.EvidenceError, "SHA_INVALID"):
                validator.write_summary(Path(directory) / "summary.md", 0, "untrusted\\n## fake")


if __name__ == "__main__":
    unittest.main()
