from __future__ import annotations

import importlib.util
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
COMPARE_PATH = ROOT / "scripts" / "performance" / "compare.py"
BASELINE_VALIDATOR_PATH = ROOT / "scripts" / "ci" / "verify-performance-baseline-updates.py"

compare_spec = importlib.util.spec_from_file_location("performance_compare_test", COMPARE_PATH)
assert compare_spec is not None and compare_spec.loader is not None
compare = importlib.util.module_from_spec(compare_spec)
compare_spec.loader.exec_module(compare)

baseline_spec = importlib.util.spec_from_file_location("performance_baseline_updates_test", BASELINE_VALIDATOR_PATH)
assert baseline_spec is not None and baseline_spec.loader is not None
baseline_updates = importlib.util.module_from_spec(baseline_spec)
baseline_spec.loader.exec_module(baseline_updates)

HEAD = "1" * 40
BASE = "2" * 40
NEW_BASE = "3" * 40
FIXTURE_HASH = "a" * 64


def policy() -> dict:
    return {
        "schemaVersion": 1,
        "resultSchemaVersion": 1,
        "statistics": {"percentileMethod": "linear-interpolation"},
        "variability": {
            "defaultIndicator": "relative-mad",
            "defaultRelativeMadMax": 0.2,
            "ratioIndicator": "range",
            "ratioRangeMax": 0.05,
        },
        "rerun": {
            "maxReruns": 1,
            "preserveAttempts": True,
            "secondAttemptRequiresPreviousArtifact": True,
        },
        "baseline": {
            "approvedSourceRef": "refs/heads/main",
            "rejectHeadAsBaseline": True,
            "requireEnvironmentCompatibility": True,
            "requireFixtureCompatibility": True,
        },
    }


def fingerprint() -> dict:
    return {
        "schemaVersion": 1,
        "phase": "environment-fingerprint",
        "commitSha": HEAD,
        "runner": {
            "os": "Linux test",
            "runnerOs": "Linux",
            "runnerImage": "ubuntu-24.04",
            "cpuCount": 4,
            "cpuModel": "Synthetic CPU",
            "memoryBytes": 8_000_000_000,
            "provider": "github-hosted", "runnerClass": "standard", "architecture": "x86_64",
            "osFamily": "ubuntu", "osVersionClass": "24.04",
        },
        "dotnet": {"sdkInfo": ".NET SDK 10", "runtimeInfo": ".NET 10 runtime"},
        "node": {"version": "v24.0.0", "npmVersion": "11.0.0"},
        "postgresql": {"version": "PostgreSQL 18"},
        "browser": {"playwrightVersion": "1.55.0", "version": "Chromium 140"},
        "containerImages": {
            "app": "sha256:app",
            "postgres": "sha256:postgres",
            "performanceBrowser": "sha256:browser",
        },
        "fixture": {"profile": "medium", "seed": 22, "hash": FIXTURE_HASH, "version": 1},
    }


def scenarios(gate: str = "hard-ceiling", metric: str = "api.latency.p95_ms") -> dict:
    return {
        "schemaVersion": 1,
        "scenarios": [
            {
                "id": "workspace.list",
                "metrics": [{"name": metric, "gate": gate}],
            }
        ],
    }


def budgets(gate: str = "hard-ceiling", metric: str = "api.latency.p95_ms") -> dict:
    entry = {
        "id": "workspace.list.p95",
        "scenarioId": "workspace.list",
        "metric": metric,
        "gate": gate,
        "baseline": {
            "identity": f"main@{BASE}",
            "sha": BASE,
            "date": "2026-09-06",
            "sourceKind": "measurement",
            "evidence": ["artifacts/performance/baseline.json"],
        },
    }
    if gate == "hard-ceiling":
        entry["limit"] = {"value": 120.0, "unit": "ms"}
    elif gate == "relative-regression":
        entry["comparison"] = {"maxIncreasePercent": 10.0}
    else:
        return {"schemaVersion": 1, "budgets": []}
    return {"schemaVersion": 1, "budgets": [entry]}


def environment() -> dict:
    return {"schemaVersion": 1, "measurement": {"minimumSamples": 5, "warmupSamplesExcluded": True}}


def measurement(samples: list[float], *, attempt: int = 1, unit: str = "ms", metric: str = "api.latency.p95_ms") -> dict:
    value = {
        "schemaVersion": 1,
        "scenario": "workspace.list",
        "metric": metric,
        "unit": unit,
        "headSha": HEAD,
        "samples": samples,
        "attempt": attempt,
        "measurementEnvelope": {
            "warmupSamplesExcluded": True,
            "environmentStable": True,
            "benchmarkExitCode": 0,
            "timedOut": False,
        },
    }
    if attempt == 2:
        value["previousAttemptArtifact"] = "artifacts/performance/attempt-1.json"
    return value


def approved_baseline(samples: list[float], *, metric: str = "api.latency.p95_ms", unit: str = "ms") -> dict:
    fp = fingerprint()
    return {
        "schemaVersion": 1,
        "resultSchemaVersion": 1,
        "scenario": "workspace.list",
        "metric": metric,
        "unit": unit,
        "baselineSha": BASE,
        "sourceRef": "refs/heads/main",
        "approved": True,
        "samples": samples,
        "environmentCompatibilityKey": compare.environment_compatibility_key(fp),
        "fixtureHash": FIXTURE_HASH,
        "fixtureVersion": 1,
    }


def ledger() -> dict:
    return {
        "schemaVersion": 1,
        "policy": {
            "approvedSourceRef": "refs/heads/main",
            "requiredBaselineFields": sorted(baseline_updates.REQUIRED_BASELINE_FIELDS),
            "requiredBudgetRelaxationFields": sorted(baseline_updates.REQUIRED_RELAXATION_FIELDS),
            "allowedCauses": sorted(baseline_updates.ALLOWED_CAUSES),
            "regressionOnlyCauseForbidden": True,
        },
        "baselineUpdates": [],
        "budgetRelaxations": [],
    }


class ComparatorTests(unittest.TestCase):
    def compare(self, current: dict, base: dict, *, gate: str = "hard-ceiling", metric: str = "api.latency.p95_ms") -> dict:
        return compare.compare_documents(
            current,
            base,
            fingerprint(),
            scenarios(gate, metric),
            budgets(gate, metric),
            environment(),
            policy(),
        )

    def test_clear_hard_ceiling_regression_is_blocking(self) -> None:
        result = self.compare(measurement([130, 131, 129, 130, 130]), approved_baseline([100, 101, 99, 100, 100]))
        self.assertEqual("regression", result["decision"])
        self.assertEqual("hard-ceiling-exceeded", result["reasonCode"])
        self.assertGreater(result["currentValue"], result["budget"]["limit"])
        self.assertEqual(BASE, result["baselineSha"])

    def test_under_hard_ceiling_passes(self) -> None:
        result = self.compare(measurement([105, 106, 104, 105, 105]), approved_baseline([100, 101, 99, 100, 100]))
        self.assertEqual("pass", result["decision"])
        self.assertEqual("hard-ceiling-satisfied", result["reasonCode"])

    def test_relative_regression_and_under_budget_are_distinguished(self) -> None:
        regressed = self.compare(
            measurement([115, 116, 114, 115, 115]),
            approved_baseline([100, 101, 99, 100, 100]),
            gate="relative-regression",
        )
        self.assertEqual("regression", regressed["decision"])
        self.assertEqual("relative-budget-exceeded", regressed["reasonCode"])
        passing = self.compare(
            measurement([105, 106, 104, 105, 105]),
            approved_baseline([100, 101, 99, 100, 100]),
            gate="relative-regression",
        )
        self.assertEqual("pass", passing["decision"])

    def test_insufficient_samples_never_pass(self) -> None:
        result = self.compare(measurement([100, 101, 99, 100]), approved_baseline([100, 101, 99, 100, 100]))
        self.assertEqual("insufficient-data", result["decision"])
        self.assertEqual("insufficient-samples", result["reasonCode"])

    def test_high_variance_is_unstable_and_rerun_is_bounded(self) -> None:
        first = self.compare(measurement([10, 100, 200, 300, 400]), approved_baseline([100, 101, 99, 100, 100]))
        self.assertEqual("unstable", first["decision"])
        self.assertTrue(first["rerun"]["allowed"])
        self.assertEqual(1, first["rerun"]["remaining"])

        second = self.compare(
            measurement([10, 100, 200, 300, 400], attempt=2),
            approved_baseline([100, 101, 99, 100, 100]),
        )
        self.assertEqual("unstable", second["decision"])
        self.assertFalse(second["rerun"]["allowed"])
        self.assertEqual(0, second["rerun"]["remaining"])

        no_artifact = measurement([10, 100, 200, 300, 400], attempt=2)
        no_artifact.pop("previousAttemptArtifact")
        invalid = self.compare(no_artifact, approved_baseline([100, 101, 99, 100, 100]))
        self.assertEqual("invalid", invalid["decision"])
        self.assertEqual("missing-field", invalid["reasonCode"])

    def test_missing_or_unapproved_baseline_fails_closed(self) -> None:
        base = approved_baseline([100, 101, 99, 100, 100])
        base["approved"] = False
        result = self.compare(measurement([100, 101, 99, 100, 100]), base)
        self.assertEqual("invalid", result["decision"])
        self.assertEqual("unapproved-baseline", result["reasonCode"])


    def test_perf02_environment_instability_is_explicitly_unstable(self) -> None:
        current = measurement([100, 101, 99, 100, 100])
        current["measurementEnvelope"]["environmentStable"] = False
        result = self.compare(current, approved_baseline([100, 101, 99, 100, 100]))
        self.assertEqual("unstable", result["decision"])
        self.assertEqual("environment-unstable", result["reasonCode"])
        self.assertTrue(result["rerun"]["allowed"])

    def test_fingerprint_must_belong_to_current_head(self) -> None:
        fp = fingerprint()
        fp["commitSha"] = "4" * 40
        result = compare.compare_documents(
            measurement([100, 101, 99, 100, 100]),
            approved_baseline([100, 101, 99, 100, 100]),
            fp,
            scenarios(),
            budgets(),
            environment(),
            policy(),
        )
        self.assertEqual("invalid", result["decision"])
        self.assertEqual("environment-head-mismatch", result["reasonCode"])

    def test_invalid_input_still_emits_schema_safe_identity_fields(self) -> None:
        current = measurement([100, 101, 99, 100, 100])
        current["scenario"] = 123
        current["headSha"] = "not-a-sha"
        current["unit"] = []
        current["attempt"] = 7
        result = self.compare(current, approved_baseline([100, 101, 99, 100, 100]))
        self.assertEqual("invalid", result["decision"])
        self.assertIsNone(result["scenario"])
        self.assertIsNone(result["headSha"])
        self.assertIsNone(result["unit"])
        self.assertIsNone(result["attempt"])

    def test_measurement_schema_mismatch_fails_closed(self) -> None:
        current = measurement([100, 101, 99, 100, 100])
        current["schemaVersion"] = 2
        result = self.compare(current, approved_baseline([100, 101, 99, 100, 100]))
        self.assertEqual("invalid", result["decision"])
        self.assertEqual("measurement-schema-mismatch", result["reasonCode"])

    def test_negative_samples_are_rejected(self) -> None:
        result = self.compare(
            measurement([100, 101, -1, 100, 100]),
            approved_baseline([100, 101, 99, 100, 100]),
        )
        self.assertEqual("invalid", result["decision"])
        self.assertEqual("invalid-samples", result["reasonCode"])

    def test_fixture_mismatch_fails_closed(self) -> None:
        base = approved_baseline([100, 101, 99, 100, 100])
        base["fixtureHash"] = "b" * 64
        result = self.compare(measurement([100, 101, 99, 100, 100]), base)
        self.assertEqual("invalid", result["decision"])
        self.assertEqual("fixture-mismatch", result["reasonCode"])

    def test_environment_mismatch_fails_closed(self) -> None:
        base = approved_baseline([100, 101, 99, 100, 100])
        base["environmentCompatibilityKey"] = "f" * 64
        result = self.compare(measurement([100, 101, 99, 100, 100]), base)
        self.assertEqual("invalid", result["decision"])
        self.assertEqual("environment-fingerprint-mismatch", result["reasonCode"])

    def test_pr_head_baseline_injection_is_rejected(self) -> None:
        base = approved_baseline([100, 101, 99, 100, 100])
        base["baselineSha"] = HEAD
        contract = budgets()
        contract["budgets"][0]["baseline"]["sha"] = HEAD
        result = compare.compare_documents(
            measurement([100, 101, 99, 100, 100]), base, fingerprint(), scenarios(), contract, environment(), policy()
        )
        self.assertEqual("invalid", result["decision"])
        self.assertEqual("pr-head-baseline-rejected", result["reasonCode"])

    def test_trend_only_uses_same_schema_without_implicit_budget(self) -> None:
        metric = "browser.load_ms"
        result = self.compare(
            measurement([500, 501, 499, 500, 500], metric=metric),
            approved_baseline([490, 491, 489, 490, 490], metric=metric),
            gate="trend-only",
            metric=metric,
        )
        self.assertEqual("pass", result["decision"])
        self.assertEqual("trend-recorded", result["reasonCode"])
        self.assertIsNone(result["budget"])

    def test_deterministic_input_has_deterministic_output(self) -> None:
        current = measurement([100, 102, 98, 101, 99])
        base = approved_baseline([95, 96, 94, 95, 95])
        first = self.compare(current, base)
        second = self.compare(json.loads(json.dumps(current)), json.loads(json.dumps(base)))
        self.assertEqual(first, second)
        self.assertEqual(100.0, first["summary"]["median"])
        self.assertIn("p95", first["summary"])
        self.assertIn("mad", first["summary"])
        self.assertIn("cv", first["summary"])

    def test_repository_comparison_policy_is_versioned_and_valid(self) -> None:
        document = json.loads((ROOT / "performance" / "comparison-policy.json").read_text(encoding="utf-8"))
        self.assertIs(document, compare._policy(document))
        self.assertEqual(1, document["rerun"]["maxReruns"])
        self.assertTrue(document["rerun"]["secondAttemptRequiresPreviousArtifact"])
        self.assertEqual("refs/heads/main", document["baseline"]["approvedSourceRef"])
        self.assertTrue(document["baseline"]["requireEnvironmentCompatibility"])
        self.assertTrue(document["baseline"]["requireFixtureCompatibility"])

    def test_versioned_result_schema_declares_all_decisions(self) -> None:
        schema = json.loads((ROOT / "performance" / "performance-result.schema.json").read_text(encoding="utf-8"))
        self.assertEqual(1, schema["properties"]["schemaVersion"]["const"])
        self.assertEqual(
            {"pass", "regression", "unstable", "insufficient-data", "invalid"},
            set(schema["properties"]["decision"]["enum"]),
        )


class BaselineGovernanceTests(unittest.TestCase):
    def test_baseline_change_without_metadata_is_rejected(self) -> None:
        old = budgets()
        new = json.loads(json.dumps(old))
        new["budgets"][0]["baseline"]["sha"] = NEW_BASE
        new["budgets"][0]["baseline"]["identity"] = f"main@{NEW_BASE}"
        with self.assertRaisesRegex(baseline_updates.BaselineUpdateError, "requires review metadata"):
            baseline_updates.validate_transition(old, new, ledger(), head_sha=HEAD)

    def test_reviewable_baseline_change_is_accepted(self) -> None:
        old = budgets()
        new = json.loads(json.dumps(old))
        new["budgets"][0]["baseline"]["sha"] = NEW_BASE
        new["budgets"][0]["baseline"]["identity"] = f"main@{NEW_BASE}"
        records = ledger()
        records["baselineUpdates"].append(
            {
                "oldBaselineSha": BASE,
                "newBaselineSha": NEW_BASE,
                "scenarioId": "workspace.list",
                "metric": "api.latency.p95_ms",
                "reason": "Accepted main baseline after a reviewed measurement correction.",
                "cause": "measurement-correction",
                "beforeEvidence": ["artifacts/performance/before.json"],
                "afterEvidence": ["artifacts/performance/after.json"],
                "budgetChanged": False,
            }
        )
        summary = baseline_updates.validate_transition(old, new, records, head_sha=HEAD)
        self.assertEqual(1, summary["baselineChanges"])
        self.assertEqual(0, summary["budgetRelaxations"])

    def test_budget_relaxation_without_metadata_is_rejected(self) -> None:
        old = budgets()
        new = json.loads(json.dumps(old))
        new["budgets"][0]["limit"]["value"] = 150.0
        with self.assertRaisesRegex(baseline_updates.BaselineUpdateError, "budget relaxation"):
            baseline_updates.validate_transition(old, new, ledger(), head_sha=HEAD)

    def test_reviewable_budget_relaxation_is_accepted(self) -> None:
        old = budgets()
        new = json.loads(json.dumps(old))
        new["budgets"][0]["limit"]["value"] = 150.0
        records = ledger()
        records["budgetRelaxations"].append(
            {
                "budgetId": "workspace.list.p95",
                "scenarioId": "workspace.list",
                "metric": "api.latency.p95_ms",
                "reason": "Accepted product contract changed after reviewed performance evidence.",
                "cause": "accepted-product-change",
                "beforeEvidence": ["artifacts/performance/before.json"],
                "afterEvidence": ["artifacts/performance/after.json"],
                "oldValue": 120.0,
                "newValue": 150.0,
            }
        )
        summary = baseline_updates.validate_transition(old, new, records, head_sha=HEAD)
        self.assertEqual(1, summary["budgetRelaxations"])

    def test_new_budget_cannot_use_pr_head_as_baseline(self) -> None:
        old = {"schemaVersion": 1, "budgets": []}
        new = budgets()
        new["budgets"][0]["baseline"]["sha"] = HEAD
        new["budgets"][0]["baseline"]["identity"] = f"main@{HEAD}"
        with self.assertRaisesRegex(baseline_updates.BaselineUpdateError, "PR head SHA"):
            baseline_updates.validate_transition(old, new, ledger(), head_sha=HEAD)

    def test_regression_only_reason_is_not_an_allowed_cause(self) -> None:
        records = ledger()
        records["baselineUpdates"].append(
            {
                "oldBaselineSha": BASE,
                "newBaselineSha": NEW_BASE,
                "scenarioId": "workspace.list",
                "metric": "api.latency.p95_ms",
                "reason": "Performance regressed, so move the baseline to make the gate green.",
                "cause": "regression",
                "beforeEvidence": ["before.json"],
                "afterEvidence": ["after.json"],
                "budgetChanged": False,
            }
        )
        with self.assertRaisesRegex(baseline_updates.BaselineUpdateError, "cause is not allowed"):
            baseline_updates.validate_ledger(records)


if __name__ == "__main__":
    unittest.main()
