from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts/performance"))
from common import PerformanceContractError
spec = importlib.util.spec_from_file_location("approval_history", ROOT / "scripts/ci/verify-performance-db-campaign-baselines.py")
approval = importlib.util.module_from_spec(spec)
spec.loader.exec_module(approval)


class ApprovedEvidenceHistoryTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.evidence = self.root / "performance/baseline-campaigns/evidence/history-test"
        self.evidence.mkdir(parents=True)
        (self.evidence / "README.md").write_text("# Retained review\n\nActual owner decision.\n", encoding="utf-8", newline="\n")
        self.raw = self.evidence / "raw-groups.json"
        self.raw.write_text('{"samples":[1,2,3,4,5]}\n', encoding="utf-8", newline="\n")
        self.record = {"campaignId": "history-test", "evidenceDirectory": self.evidence.relative_to(self.root).as_posix(),
                       "baselineDirectory": "performance/baselines/db/small/test"}
        ledger = self.root / approval.LEDGER
        ledger.parent.mkdir(parents=True, exist_ok=True)
        ledger.write_text(json.dumps({"schemaVersion": 1, "policyVersion": approval.POLICY_VERSION,
                                     "approvals": [self.record]}), encoding="utf-8")
        for arguments in (["init", "-q"], ["config", "core.autocrlf", "false"], ["add", "."],
                          ["-c", "user.name=Test", "-c", "user.email=test@example.invalid", "commit", "-qm", "Approved evidence"]):
            subprocess.run(["git", *arguments], cwd=self.root, check=True, capture_output=True)
        self.base = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=self.root, text=True).strip()

    def validate_history(self):
        # Full artifact/replay validation is tested separately; exercise the real
        # historical preservation path against actual committed Git bytes here.
        with patch.object(approval, "validate_approval"):
            return approval.validate(self.root, self.base, "a" * 40)

    def test_unchanged_markdown_and_json_history_remain_verifiable(self):
        self.assertEqual(1, self.validate_history())

    def test_json_reserialization_cannot_rewrite_approved_archive_bytes(self):
        self.raw.write_text(json.dumps({"samples": [1, 2, 3, 4, 5]}, indent=2), encoding="utf-8")
        with self.assertRaisesRegex(PerformanceContractError, "approved-campaign-evidence-is-immutable"):
            self.validate_history()

    def test_sample_reordering_remains_rejected(self):
        self.raw.write_text('{"samples":[5,4,3,2,1]}\n', encoding="utf-8")
        with self.assertRaisesRegex(PerformanceContractError, "approved-campaign-evidence-is-immutable"):
            self.validate_history()

    def test_deleted_review_document_remains_rejected(self):
        (self.evidence / "README.md").unlink()
        with self.assertRaisesRegex(PerformanceContractError, "approved-campaign-evidence-is-immutable"):
            self.validate_history()
