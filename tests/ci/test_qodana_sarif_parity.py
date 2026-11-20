#!/usr/bin/env python3
"""No-network negative controls for advisory-only Qodana SARIF parity."""

from __future__ import annotations

import copy
import json
import os
import sys
import tempfile
import unittest
import uuid
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts" / "quality"))
import qodana_sarif_parity as parity  # noqa: E402

SHA = "c" * 40
RUN = "40649034671"
BRANCH = "main"


def document(guid: str) -> dict:
    return {
        "$schema": parity.SCHEMA,
        "version": "2.1.0",
        "runs": [{
            "tool": {"driver": {"name": "QDNETC", "version": "2026.2.672",
                                "rules": [{"id": "SEC-1"}, {"id": "SEC-2"}]}},
            "invocations": [{"exitCode": 0, "executionSuccessful": True}],
            "versionControlProvenance": [{
                "repositoryUri": parity.REPOSITORY, "revisionId": SHA, "branch": BRANCH,
            }],
            "automationDetails": {
                "id": "QDNETC/qodana/2026-10-29",
                "guid": guid,
                "properties": {"jobUrl": f"https://github.com/NYGsatoshi/Coglatas/actions/runs/{RUN}"},
            },
            "artifacts": [{"location": {"uri": "src/Project.cs"}}],
            "results": [{"ruleId": "SEC-1", "message": {"text": "Synthetic issue"}},
                        {"ruleId": "SEC-2", "message": {"text": "Another synthetic issue"}}],
        }],
    }


class QodanaParityTests(unittest.TestCase):
    def setUp(self) -> None:
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.community = self.root / "community.sarif.json"
        self.cloud = self.root / "cloud.sarif.json"
        self.output = self.root / "evidence.json"
        self.left = document("11111111-1111-4111-8111-111111111111")
        self.right = document("22222222-2222-4222-8222-222222222222")
        self.persist()

    def persist(self) -> None:
        self.community.write_text(json.dumps(self.left, ensure_ascii=False), encoding="utf-8")
        self.cloud.write_text(json.dumps(self.right, ensure_ascii=False), encoding="utf-8")

    def compare(self) -> dict:
        self.persist()
        return parity.compare(self.community, self.cloud, SHA, RUN, BRANCH)

    def assert_reject(self) -> None:
        with self.assertRaises(parity.ParityError):
            self.compare()

    def test_only_execution_guid_diff_passes_but_never_qualifies(self) -> None:
        receipt = self.compare()
        self.assertEqual(receipt["status"], "PARITY_OBSERVED_NOT_ATTESTED")
        self.assertIs(receipt["qualifiedForGateReuse"], False)
        self.assertIs(receipt["runAttemptVerified"], False)
        self.assertEqual((receipt["resultCount"], receipt["ruleCount"], receipt["artifactCount"]), (2, 2, 1))
        self.assertEqual(receipt["sourceSha"], SHA)
        self.assertRegex(receipt["normalizedSarifSha256"], r"^[0-9a-f]{64}$")

    def test_cli_writes_only_sanitized_complete_receipt(self) -> None:
        args = [
            "--community", str(self.community), "--cloud", str(self.cloud),
            "--source-sha", SHA, "--run-id", RUN, "--branch", BRANCH,
            "--output", str(self.output),
        ]
        self.assertEqual(parity.main(args), 0)
        value = json.loads(self.output.read_text(encoding="utf-8"))
        self.assertFalse(value["qualifiedForGateReuse"])
        self.assertNotIn("Synthetic issue", self.output.read_text(encoding="utf-8"))
        # Existing output is never silently overwritten by an apparently green rerun.
        self.assertEqual(parity.main(args), 1)

    def test_cli_failed_parity_does_not_publish_receipt(self) -> None:
        self.right["runs"][0]["results"][0]["ruleId"] = "SEC-2"
        self.persist()
        self.assertEqual(parity.main([
            "--community", str(self.community), "--cloud", str(self.cloud),
            "--source-sha", SHA, "--run-id", RUN, "--branch", BRANCH,
            "--output", str(self.output),
        ]), 1)
        self.assertFalse(self.output.exists())

    def test_any_non_guid_difference_fails(self) -> None:
        mutations = {
            "result": lambda d: d["runs"][0]["results"][0]["message"].update(text="Changed"),
            "rule": lambda d: d["runs"][0]["tool"]["driver"]["rules"][0].update(name="Changed"),
            "artifact": lambda d: d["runs"][0]["artifacts"][0]["location"].update(uri="src/Other.cs"),
            "automation_id": lambda d: d["runs"][0]["automationDetails"].update(id="Other"),
            "invocation": lambda d: d["runs"][0]["invocations"][0].update(extra="Changed"),
            "extra_metadata": lambda d: d["runs"][0].update(hidden="Changed"),
            "ordering": lambda d: d["runs"][0]["results"].reverse(),
        }
        for key, mutate in mutations.items():
            with self.subTest(change=key):
                self.right = copy.deepcopy(self.left)
                self.right["runs"][0]["automationDetails"]["guid"] = "22222222-2222-4222-8222-222222222222"
                mutate(self.right)
                self.assert_reject()

    def test_same_guid_is_not_two_independent_runs(self) -> None:
        self.right["runs"][0]["automationDetails"]["guid"] = self.left["runs"][0]["automationDetails"]["guid"]
        self.assert_reject()

    def test_invalid_guid_is_rejected(self) -> None:
        for guid in ("not-a-guid", "11111111-1111-4111-8111-11111111111X", ""):
            with self.subTest(guid=guid):
                self.right["runs"][0]["automationDetails"]["guid"] = guid
                self.assert_reject()

    def test_wrong_provenance_fails(self) -> None:
        for key, value in (
            ("repositoryUri", "https://example.invalid/repo.git"),
            ("revisionId", "d" * 40),
            ("branch", "untrusted"),
        ):
            with self.subTest(key=key):
                self.right = document("22222222-2222-4222-8222-222222222222")
                self.right["runs"][0]["versionControlProvenance"][0][key] = value
                self.assert_reject()
        self.right = document("22222222-2222-4222-8222-222222222222")
        self.right["runs"][0]["automationDetails"]["properties"]["jobUrl"] += "/different"
        self.assert_reject()

    def test_failed_scan_and_empty_inventory_do_not_pass(self) -> None:
        for changes in (
            {"executionSuccessful": False},
            {"exitCode": 1},
            {"exitCode": True},
        ):
            with self.subTest(change=changes):
                self.right = document("22222222-2222-4222-8222-222222222222")
                self.right["runs"][0]["invocations"][0].update(changes)
                self.assert_reject()
        for key in ("results", "artifacts"):
            self.right = document("22222222-2222-4222-8222-222222222222")
            self.right["runs"][0][key] = []
            self.assert_reject()
        self.right = document("22222222-2222-4222-8222-222222222222")
        self.right["runs"][0]["tool"]["driver"]["rules"] = []
        self.assert_reject()

    def test_duplicate_rule_identity_fails(self) -> None:
        self.right["runs"][0]["tool"]["driver"]["rules"][1]["id"] = "SEC-1"
        self.assert_reject()

    def test_malformed_duplicate_or_nonfinite_json_fails(self) -> None:
        for raw in (
            b'{"version":"2.1.0","version":"2.1.0"}',
            b'{"unexpected":NaN}',
            b'{"unexpected":Infinity}',
            b'{"unexpected":',
            b"\xff\xfe",
        ):
            with self.subTest(raw=raw[:30]):
                self.cloud.write_bytes(raw)
                with self.assertRaises(parity.ParityError):
                    parity.compare(self.community, self.cloud, SHA, RUN, BRANCH)

    def test_same_path_and_hard_link_fail(self) -> None:
        with self.assertRaises(parity.ParityError):
            parity.compare(self.community, self.community, SHA, RUN, BRANCH)
        self.cloud.unlink()
        os.link(self.community, self.cloud)
        with self.assertRaises(parity.ParityError):
            parity.compare(self.community, self.cloud, SHA, RUN, BRANCH)

    def test_missing_or_symlinked_report_fails(self) -> None:
        self.cloud.unlink()
        with self.assertRaises(parity.ParityError):
            parity.compare(self.community, self.cloud, SHA, RUN, BRANCH)
        self.cloud.symlink_to(self.community)
        with self.assertRaises(parity.ParityError):
            parity.compare(self.community, self.cloud, SHA, RUN, BRANCH)

    def test_size_bound_is_enforced(self) -> None:
        with patch.object(parity, "MAX_BYTES", 32):
            with self.assertRaises(parity.ParityError):
                self.compare()

    def test_bad_expected_identifiers_fail(self) -> None:
        for sha, run_id, branch in (
            ("z" * 40, RUN, BRANCH),
            ("d" * 39, RUN, BRANCH),
            (SHA, "0", BRANCH),
            (SHA, "1/2", BRANCH),
            (SHA, RUN, "feature/untrusted"),
            (SHA, "9" * 30, BRANCH),
        ):
            with self.subTest(sha=sha, run_id=run_id, branch=branch):
                with self.assertRaises(parity.ParityError):
                    parity.compare(self.community, self.cloud, sha, run_id, branch)

    def test_noncanonical_schema_fails(self) -> None:
        self.right["$schema"] = "https://another.invalid/sarif.json"
        self.assert_reject()

    def test_wrong_analyzer_fails(self) -> None:
        self.right["runs"][0]["tool"]["driver"]["name"] = "Unknown Analyzer"
        self.assert_reject()


if __name__ == "__main__":
    unittest.main()
