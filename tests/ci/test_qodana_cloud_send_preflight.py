#!/usr/bin/env python3
"""No-network contract tests for preflight-only Qodana Cloud report reuse."""

from __future__ import annotations

import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts" / "quality"))
import qodana_cloud_send_preflight as policy  # noqa: E402
from qodana_sarif_parity import REPOSITORY

SHA = "b" * 40
RUN = "40649034671"
GUID = "22222222-2222-4222-8222-222222222222"


def synthetic_report() -> dict:
    return {
        "$schema": "https://schemastore.azurewebsites.net/schemas/json/sarif-2.1.0-rtm.6.json",
        "version": "2.1.0",
        "runs": [{
            "versionControlProvenance": [{"repositoryUri": REPOSITORY, "revisionId": SHA, "branch": "main"}],
            "automationDetails": {
                "id": "QDNETC/qodana",
                "guid": GUID,
                "properties": {
                    "jobUrl": f"https://github.com/{REPOSITORY.removeprefix('https://github.com/').removesuffix('.git')}/actions/runs/{RUN}",
                },
            },
            "invocations": [{"executionSuccessful": True, "exitCode": 0}],
            "tool": {"driver": {"name": "QDNETC", "version": "2026.2",
                                "rules": [{"id": "SyntheticRule"}]}},
            "results": [{"ruleId": "SyntheticRule", "message": {"text": "Synthetic"}}],
            "artifacts": [{"location": {"uri": "src/Example.cs"}}],
        }],
    }


class QodanaCloudSendPreflightTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.tmp = Path(temp.name)
        self.root = self.tmp / "results"
        self.root.mkdir()
        for relative in policy.REQUIRED_REPORT_FILES:
            path = self.root / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(
                json.dumps(synthetic_report()) if relative == "qodana.sarif.json" else "safe synthetic data",
                encoding="utf-8",
            )
        self.output = self.tmp / "summary.json"

    def check(self):
        return policy.preflight(self.root, SHA, RUN, 1)

    def assert_reject(self):
        with self.assertRaises(policy.ParityError):
            self.check()

    def test_valid_layout_never_permits_upload(self):
        data = self.check()
        self.assertEqual(data["status"], "CLOUD_SEND_LAYOUT_REVIEWED_NOT_EXECUTED")
        for key in ("qodanaCliVersionAndDigestApproved", "producerJobCryptographicallyAttested",
                    "cloudReplayVerified", "cloudSendExecuted", "qualifiedForGateReuse"):
            self.assertIs(data[key], False)
        self.assertTrue(data["requiredReportEntriesPresent"])
        self.assertEqual(data["qodanaCommand"], "send")
        self.assertEqual(data["runAttempt"], 1)

    def test_cli_preflight_creates_redacted_receipt_without_cloud_io(self):
        with patch.dict(os.environ, {"QODANA_TOKEN": "secret-must-not-appear"}):
            with patch("subprocess.run", side_effect=AssertionError("Never execute qodana or gh")):
                result = policy.main([
                    "--results-dir", str(self.root), "--source-sha", SHA,
                    "--run-id", RUN, "--run-attempt", "1",
                    "--output", str(self.output),
                ])
        self.assertEqual(result, 0)
        content = self.output.read_text(encoding="utf-8")
        self.assertNotIn("secret-must-not-appear", content)
        self.assertNotIn(str(self.root), content)
        self.assertFalse(json.loads(content)["cloudSendExecuted"])

    def test_refuses_to_overwrite_existing_receipt(self):
        self.output.write_text("prior evidence", encoding="utf-8")
        self.assertEqual(policy.main([
            "--results-dir", str(self.root), "--source-sha", SHA,
            "--run-id", RUN, "--run-attempt", "1",
            "--output", str(self.output),
        ]), 1)
        self.assertEqual(self.output.read_text(encoding="utf-8"), "prior evidence")

    def test_required_report_parts_fail_closed(self):
        for file in policy.REQUIRED_REPORT_FILES:
            with self.subTest(file=file):
                path = self.root / file
                backup = path.read_bytes()
                path.unlink()
                self.assert_reject()
                path.write_bytes(backup)

    def test_empty_and_oversized_files_fail(self):
        path = self.root / "report/results/metaInformation.json"
        path.write_bytes(b"")
        self.assert_reject()
        with patch.object(policy, "MAX_SUPPORT_BYTES", 5):
            self.assert_reject()

    def test_symlinked_report_file_is_rejected(self):
        path = self.root / "report/results/metaInformation.json"
        content = self.tmp / "outside.txt"
        path.replace(content)
        path.symlink_to(content)
        self.assert_reject()

    def test_symlinked_directory_is_rejected(self):
        newroot = self.tmp / "alias"
        newroot.symlink_to(self.root, target_is_directory=True)
        with self.assertRaises(policy.ParityError):
            policy.preflight(newroot, SHA, RUN, 1)

    def test_wrong_source_and_run_metadata_fail(self):
        for sha, run, attempt in [
            ("c" * 40, RUN, 1), ("nothex", RUN, 1), (SHA, "0", 1),
            (SHA, RUN, 0), (SHA, RUN, 101), (SHA, RUN + "x", 1),
        ]:
            with self.subTest(sha=sha, run=run, attempt=attempt):
                with self.assertRaises(policy.ParityError):
                    policy.preflight(self.root, sha, run, attempt)

    def test_sarif_scan_failures_are_rejected(self):
        source = self.root / "qodana.sarif.json"
        data = synthetic_report()
        data["runs"][0]["invocations"][0]["executionSuccessful"] = False
        source.write_text(json.dumps(data), encoding="utf-8")
        self.assert_reject()

    def test_untrusted_support_file_not_executed_or_parsed(self):
        file = self.root / "report/results/metaInformation.json"
        file.write_text("$(curl example.invalid) untrusted content", encoding="utf-8")
        with patch("subprocess.run", side_effect=AssertionError("No subprocess allowed")):
            data = self.check()
        self.assertFalse(data["cloudSendExecuted"])

    def test_main_cli_rejects_invalid_receipt_input(self):
        self.root.joinpath("qodana.sarif.json").write_text("{", encoding="utf-8")
        self.assertEqual(policy.main([
            "--results-dir", str(self.root), "--source-sha", SHA,
            "--run-id", RUN, "--run-attempt", "1",
            "--output", str(self.output),
        ]), 1)
        self.assertFalse(self.output.exists())


if __name__ == "__main__":
    unittest.main()
