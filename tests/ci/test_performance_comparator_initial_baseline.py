from __future__ import annotations

import importlib.util
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("initial_baseline_base_tests", ROOT / "tests/ci/test_performance_comparator.py")
assert spec is not None and spec.loader is not None
base = importlib.util.module_from_spec(spec)
spec.loader.exec_module(base)
validator = base.baseline_updates


def introduction(profile: str = "small") -> tuple[dict, dict]:
    # Synthetic test evidence only; no repository baseline is approved here.
    samples = [10, 11, 12, 11, 10]
    provenance = {
        "sourceRef": "refs/heads/main", "headSha": base.BASE,
        "workflowPath": ".github/workflows/performance-db-baseline-capture.yml",
        "workflowRunId": 42, "workflowRunAttempt": 1,
        "artifactId": 43, "artifactName": f"perf05-{profile}",
        "artifactDigest": "sha256:" + "c" * 64,
        "artifactUrl": "https://github.com/example/test/actions/runs/42/artifacts/43",
        "profile": profile, "pageSize": 5, "sampleCount": len(samples),
        "samplesSha256": validator.samples_sha256(samples),
        "samplesEvidence": "db.json#/measurements/0/samples",
        "environmentCompatibilityKey": "a" * 64, "fixtureHash": "b" * 64, "fixtureVersion": 2,
    }
    record = {
        "changeType": "initial-baseline", "oldBaselineSha": None, "newBaselineSha": base.BASE,
        "baselinePath": f"performance/baselines/db/{profile}/task.list.json",
        "scenarioId": "task.list", "metric": "db.total_time_ms", "budgetChanged": False,
        "cause": "accepted-product-change",
        "reason": "First baseline after reviewed structural query remediation on main.",
        "beforeEvidence": ["base main tree contains no approved DB baseline"],
        "afterEvidence": [provenance["artifactUrl"]], "provenance": provenance,
    }
    document = {
        "schemaVersion": 1, "resultSchemaVersion": 1, "scenario": "task.list",
        "metric": "db.total_time_ms", "unit": "ms", "baselineSha": base.BASE,
        "sourceRef": "refs/heads/main", "approved": True, "samples": samples,
        "environmentCompatibilityKey": provenance["environmentCompatibilityKey"],
        "fixtureHash": provenance["fixtureHash"], "fixtureVersion": 2,
        "provenance": json.loads(json.dumps(provenance)),
    }
    return record, document


class InitialBaselineGovernanceTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "performance").mkdir()
        (self.root / "performance/environment.json").write_text(json.dumps({
            "dbFixtureVersion": 2, "measurement": {"minimumSamples": 5},
        }), encoding="utf-8")

    def validate(self, record: dict, document: dict, *, previous: dict | None = None,
                 existing: bool = False, ancestor: bool = True, head: str = base.HEAD) -> int:
        path = self.root / record["baselinePath"]
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(document), encoding="utf-8")
        ledger = base.ledger()
        ledger["baselineUpdates"] = [record]

        def git(_root: Path, *args: str) -> str:
            if args[0] == "rev-parse":
                return base.NEW_BASE
            self.assertEqual(("ls-tree", "--name-only", base.NEW_BASE, "--", record["baselinePath"]), args)
            return record["baselinePath"] if existing else ""

        with patch.object(validator, "_git", side_effect=git), patch.object(validator, "_git_is_ancestor", return_value=ancestor):
            return validator.validate_initial_baselines(self.root, previous or base.ledger(), ledger, "base-main", head)

    def test_first_introduction_preserves_explicit_absence_and_complete_samples(self) -> None:
        record, document = introduction()
        self.assertEqual(1, self.validate(record, document))
        self.assertIsNone(record["oldBaselineSha"])
        self.assertEqual(5, document["provenance"]["sampleCount"])

    def test_profiles_have_distinct_introduction_records(self) -> None:
        ledger = base.ledger()
        ledger["baselineUpdates"] = [introduction("small")[0], introduction("medium")[0]]
        self.assertEqual(2, len(validator.validate_ledger(ledger)[0]))

    def test_approved_document_without_any_ledger_record_is_rejected(self) -> None:
        record, document = introduction()
        path = self.root / record["baselinePath"]
        path.parent.mkdir(parents=True)
        path.write_text(json.dumps(document), encoding="utf-8")
        with self.assertRaisesRegex(validator.BaselineUpdateError, "require introduction review records"):
            validator.validate_initial_baselines(self.root, base.ledger(), base.ledger(), "base-main", base.HEAD)

    def test_legacy_repository_without_db_baselines_remains_valid(self) -> None:
        with patch.object(validator, "_git", return_value=base.NEW_BASE):
            self.assertEqual(0, validator.validate_initial_baselines(
                self.root, base.ledger(), base.ledger(), "base-main", base.HEAD,
            ))

    def test_initial_record_cannot_replace_existing_base_document(self) -> None:
        record, document = introduction()
        with self.assertRaisesRegex(validator.BaselineUpdateError, "cannot replace an existing"):
            self.validate(record, document, existing=True)

    def test_initial_record_cannot_authorize_an_existing_budget_baseline_change(self) -> None:
        old = base.budgets()
        new = json.loads(json.dumps(old))
        new["budgets"][0]["baseline"]["sha"] = base.NEW_BASE
        ledger = base.ledger()
        ledger["baselineUpdates"] = [introduction()[0]]
        with self.assertRaisesRegex(validator.BaselineUpdateError, "requires review metadata"):
            validator.validate_transition(old, new, ledger, head_sha=base.HEAD)

    def test_null_without_explicit_introduction_and_missing_null_are_rejected(self) -> None:
        for change in (lambda r: r.pop("changeType"), lambda r: r.pop("oldBaselineSha")):
            record, document = introduction()
            change(record)
            with self.assertRaises(validator.BaselineUpdateError):
                self.validate(record, document)

    def test_non_main_self_and_nonancestor_sources_are_rejected(self) -> None:
        record, document = introduction()
        record["provenance"]["sourceRef"] = "refs/heads/feature"
        with self.assertRaisesRegex(validator.BaselineUpdateError, "approved main SHA"):
            self.validate(record, document)
        record, document = introduction()
        for args in ({"head": base.BASE}, {"ancestor": False}):
            with self.assertRaisesRegex(validator.BaselineUpdateError, "distinct approved main history"):
                self.validate(record, document, **args)

    def test_forged_samples_count_digest_and_artifact_provenance_are_rejected(self) -> None:
        mutations = (
            lambda d: d["samples"].__setitem__(0, 999),
            lambda d: d["samples"].pop(),
            lambda d: d["provenance"].__setitem__("artifactId", 999),
            lambda d: d["provenance"].__setitem__("workflowRunAttempt", 2),
            lambda d: d.__setitem__("environmentCompatibilityKey", "f" * 64),
            lambda d: d.__setitem__("approved", False),
        )
        for mutate in mutations:
            record, document = introduction()
            mutate(document)
            with self.assertRaises(validator.BaselineUpdateError):
                self.validate(record, document)

    def test_invalid_artifact_identity_path_and_budget_change_are_rejected(self) -> None:
        mutations = (
            lambda r: r["provenance"].__setitem__("workflowRunAttempt", True),
            lambda r: r["provenance"].__setitem__("artifactDigest", "unverified"),
            lambda r: r["provenance"].__setitem__("artifactUrl", "https://github.com/example/test/actions/runs/99/artifacts/43"),
            lambda r: r.__setitem__("baselinePath", "performance/baselines/db/small/../task.list.json"),
            lambda r: r.__setitem__("budgetChanged", True),
        )
        for mutate in mutations:
            record, document = introduction()
            mutate(record)
            ledger = base.ledger()
            ledger["baselineUpdates"] = [record]
            with self.assertRaises(validator.BaselineUpdateError):
                validator.validate_ledger(ledger)

    def test_historical_introduction_does_not_require_absence_again(self) -> None:
        record, document = introduction()
        previous = base.ledger()
        previous["baselineUpdates"] = [record]
        self.assertEqual(0, self.validate(record, document, previous=previous, existing=True))

    def test_historical_document_samples_and_provenance_remain_protected(self) -> None:
        for mutate in (lambda d: d["samples"].__setitem__(0, 999),
                       lambda d: d["provenance"].__setitem__("artifactId", 999)):
            record, document = introduction()
            previous = base.ledger()
            previous["baselineUpdates"] = [record]
            mutate(document)
            with self.assertRaises(validator.BaselineUpdateError):
                self.validate(record, document, previous=previous, existing=True)

    def test_historical_source_still_requires_main_ancestry(self) -> None:
        record, document = introduction()
        previous = base.ledger()
        previous["baselineUpdates"] = [record]
        with self.assertRaisesRegex(validator.BaselineUpdateError, "distinct approved main history"):
            self.validate(record, document, previous=previous, existing=True, ancestor=False)

    def test_historical_record_cannot_be_removed_or_rewritten_to_hide_tampering(self) -> None:
        record, _ = introduction()
        previous = base.ledger()
        previous["baselineUpdates"] = [record]
        for replacement in ([], [dict(record, reason="Alter approval metadata to hide an unauthorized sample replacement.")]):
            current = base.ledger()
            current["baselineUpdates"] = replacement
            with self.assertRaisesRegex(validator.BaselineUpdateError, "cannot be altered or removed"):
                validator.validate_initial_baselines(self.root, previous, current, "base-main", base.HEAD)

    def test_missing_document_is_rejected(self) -> None:
        record, _ = introduction()
        ledger = base.ledger()
        ledger["baselineUpdates"] = [record]
        with patch.object(validator, "_git", side_effect=[base.NEW_BASE, ""]), patch.object(validator, "_git_is_ancestor", return_value=True):
            with self.assertRaisesRegex(validator.BaselineUpdateError, "cannot read"):
                validator.validate_initial_baselines(self.root, base.ledger(), ledger, "base-main", base.HEAD)

    def test_no_argument_cli_revalidates_historical_document_samples(self) -> None:
        script = self.root / "scripts/ci/verify-performance-baseline-updates.py"
        script.parent.mkdir(parents=True)
        shutil.copyfile(ROOT / "scripts/ci/verify-performance-baseline-updates.py", script)
        ledger_path = self.root / "performance/baseline-updates.json"
        ledger_path.write_text(json.dumps(base.ledger()), encoding="utf-8")

        def git(*args: str) -> str:
            return subprocess.run(
                ["git", "-c", "user.name=Synthetic baseline test", "-c", "user.email=baseline-test@example.invalid", *args],
                cwd=self.root, check=True, capture_output=True, text=True,
            ).stdout.strip()

        git("init", "-b", "main")
        git("add", ".")
        git("commit", "-m", "Synthetic main source before first baseline")
        source = git("rev-parse", "HEAD")
        def run() -> subprocess.CompletedProcess[str]:
            return subprocess.run([sys.executable, str(script)], cwd=self.root, check=False, capture_output=True, text=True)
        self.assertEqual(0, run().returncode, "An empty baseline repository remains valid without CLI arguments")

        record, document = introduction()
        record["newBaselineSha"] = source
        record["provenance"]["headSha"] = source
        document["baselineSha"] = source
        document["provenance"]["headSha"] = source
        current = base.ledger()
        current["baselineUpdates"] = [record]
        ledger_path.write_text(json.dumps(current), encoding="utf-8")
        path = self.root / record["baselinePath"]
        path.parent.mkdir(parents=True)
        path.write_text(json.dumps(document), encoding="utf-8")
        git("add", ".")
        git("commit", "-m", "Synthetic reviewed first baseline approval")
        unchanged = run()
        self.assertEqual(0, unchanged.returncode, unchanged.stderr)
        document["samples"][0] = 999
        path.write_text(json.dumps(document), encoding="utf-8")
        tampered = run()
        self.assertNotEqual(0, tampered.returncode)
        self.assertIn("complete sample digest mismatch", tampered.stderr)


if __name__ == "__main__":
    unittest.main()
