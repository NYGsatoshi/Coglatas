"""Regression contracts for suspended numerical performance assurance."""
from __future__ import annotations
import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts/performance"))
import assurance_policy as assurance

class SuspensionTests(unittest.TestCase):
    def test_policy_not_pass_and_keeps_structural_gate(self):
        value = assurance.load_policy()
        self.assertEqual("SUSPENDED", value["state"])
        self.assertEqual("NOT_EVALUATED", value["apiNumericAssurance"])
        self.assertEqual("NOT_EVALUATED", value["dbDurationAssurance"])
        self.assertEqual("ENFORCED", value["dbStructuralGate"])

    def test_configuration_only_reactivation_is_rejected(self):
        base = assurance.load_policy()
        for change in ({"state": "ACTIVE"}, {"apiNumericAssurance": "PASS"},
                       {"dbStructuralGate": "DISABLED"}, {"resumeIssue": 0}):
            with self.subTest(change=change), tempfile.TemporaryDirectory() as td:
                path = Path(td) / "policy.json"
                path.write_text(json.dumps(base | change), encoding="utf-8")
                with self.assertRaises(ValueError):
                    assurance.load_policy(path)

    def test_duplicate_and_unknown_keys_rejected(self):
        with tempfile.TemporaryDirectory() as td:
            path = Path(td) / "policy.json"
            path.write_text('{"state":"SUSPENDED","state":"ACTIVE"}', encoding="utf-8")
            with self.assertRaises(ValueError):
                assurance.load_policy(path)
            path.write_text(json.dumps(assurance.load_policy() | {"bypass": True}), encoding="utf-8")
            with self.assertRaises(ValueError):
                assurance.load_policy(path)

    def test_receipt_has_no_numeric_credit(self):
        for suite in ("api", "db"):
            value = assurance.receipt(suite, "a" * 40)
            self.assertEqual("NOT_EVALUATED", value["numericalDecision"])
            self.assertFalse(value["numericalAcceptanceCredit"])
            self.assertFalse(value["baselineQualificationCredit"])
            self.assertEqual(1128, value["resumeIssue"])
        with self.assertRaises(ValueError):
            assurance.receipt("api", "not-a-sha")

    def test_api_required_check_cannot_hide_suspension(self):
        text = (ROOT / ".github/workflows/performance-api.yml").read_text()
        for part in ("name: performance-fast", "if: always()", "required=false",
                     'test "$BENCHMARK_RESULT" == skipped',
                     'true) test "$BENCHMARK_RESULT" == success',
                     'test "$ASSURANCE_MODE" == "$actual_mode"',
                     "assurance_policy.py receipt --suite api"):
            self.assertIn(part, text)
        self.assertNotIn("continue-on-error", text)

    def test_db_duration_suspended_but_structural_remains_strict(self):
        text = (ROOT / ".github/workflows/performance-db.yml").read_text()
        for part in ("name: PostgreSQL query regression gate", "needs: [route, collect]",
                     "python3 scripts/performance/db-compare.py",
                     "perf05-small/db.json", "perf05-medium/db.json",
                     "assurance_policy.py receipt --suite db"):
            self.assertIn(part, text)
        self.assertNotIn("args+=(--duration", text)
        self.assertNotIn("continue-on-error", text)

if __name__ == "__main__":
    unittest.main()
