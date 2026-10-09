"""Regression contracts for suspended numerical performance assurance."""
from __future__ import annotations
import json
import copy
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
            self.assertEqual("SUSPENDED", value["assuranceMode"])
            self.assertEqual("INDEFINITE", value["suspensionDuration"])
            self.assertFalse(value["automaticReactivation"])
            self.assertEqual(1128, value["resumeIssue"])
        with self.assertRaises(ValueError):
            assurance.receipt("api", "not-a-sha")

    def test_date_ga_runner_and_single_approval_cannot_reactivate(self):
        base = assurance.load_policy()
        changes = ({"resumeDate": "2027-01-01"}, {"resumeAtGa": True},
                   {"automaticReactivation": True}, {"automaticReactivation": 0},
                   {"suspensionDuration": "TEMPORARY"},
                   {"reactivationRequires": ["RUNNER_CHANGED"]},
                   {"reactivationRequires": base["reactivationRequires"][1:]})
        for change in changes:
            with self.subTest(change=change), tempfile.TemporaryDirectory() as td:
                path = Path(td) / "policy.json"
                path.write_text(json.dumps(base | change), encoding="utf-8")
                with self.assertRaises(ValueError):
                    assurance.load_policy(path)

    def test_duration_variance_below_emergency_ceiling_does_not_change_structural_decision(self):
        from test_performance_db_ci import PerformanceDbComparisonIdentityTests, comparison, HEAD
        from common import load_json
        contract = load_json(ROOT / "performance/db-scenarios.json")
        fixture = PerformanceDbComparisonIdentityTests()
        small, medium = (fixture.profile(name, contract) for name in ("small", "medium"))
        expected = comparison.evaluate(small, medium, contract, HEAD)
        self.assertEqual("pass", expected["decision"])
        self.assertEqual(28, len(expected["results"]))
        for profile in (small, medium):
            for scenario in profile["scenarios"]:
                for index, sample in enumerate(scenario["samples"]):
                    capture = sample["capture"]
                    for command in capture["commands"]:
                        command["durationMs"] = 9000 if index % 2 else 0.01
                    capture["totalDurationMs"] = sum(c["durationMs"] for c in capture["commands"])
        varied = comparison.evaluate(small, medium, contract, HEAD)
        self.assertEqual(expected, varied)
        self.assertEqual("NOT_EVALUATED", assurance.receipt("db", HEAD)["numericalDecision"])

    def test_structural_mutations_fail_aggregate_during_suspension(self):
        from test_performance_db_ci import PerformanceDbComparisonIdentityTests, comparison, HEAD, ci
        from test_performance_db import capture
        from common import load_json
        contract = load_json(ROOT / "performance/db-scenarios.json")
        fixture = PerformanceDbComparisonIdentityTests()
        profiles = [fixture.profile(name, contract) for name in ("small", "medium")]
        route = {"schemaVersion": 1, "headSha": HEAD, "required": True}
        for mutation in ("count", "n-plus-one", "paging", "index", "hard-ceiling"):
            small, medium = copy.deepcopy(profiles)
            if mutation == "index":
                medium["plans"][0]["requiredKeyLookupPresent"] = False
            elif mutation == "n-plus-one":
                for sample in medium["scenarios"][0]["samples"]:
                    sample["capture"] = capture(count=8)
            else:
                record = medium["scenarios"][1]
                for sample in record["samples"]:
                    evidence = capture(count=81 if mutation == "count" else 4,
                                       duration=10001 if mutation == "hard-ceiling" else 2,
                                       bounded=mutation != "paging")
                    for command in evidence["commands"]:
                        command["rootTable"] = record["samples"][0]["capture"]["commands"][0]["rootTable"]
                    sample["capture"] = evidence
            with self.subTest(mutation=mutation):
                measured = comparison.evaluate(small, medium, contract, HEAD)
                self.assertEqual("regression", measured["decision"])
                self.assertEqual("regression", ci.gate_decision(route, "success", measured, HEAD))

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
