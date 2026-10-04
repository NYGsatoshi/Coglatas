from __future__ import annotations

import copy
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from test_performance_comparator import fingerprint, measurement, compare
from test_performance_db import module as adapter
from test_performance_comparator_initial_baseline import introduction, validator, base

ROOT = Path(__file__).resolve().parents[2]
CONTRACT = json.loads((ROOT / "performance/db-scenarios.json").read_text())


def variant_group(profile="medium", *, cpu="Synthetic CPU"):
    """Synthetic temporary test documents; no approved repository evidence."""
    fp = fingerprint()
    fp["runner"]["cpuModel"] = cpu
    fp["fixture"]["profile"] = profile
    fp["fixture"]["version"] = 2
    key = compare.environment_compatibility_key(fp)
    pairs = []
    streams = []
    for index, scenario in enumerate(CONTRACT["scenarios"]):
        record, document = introduction(profile)
        record["scenarioId"] = document["scenario"] = scenario["id"]
        record["baselinePath"] = f"performance/baselines/db/{profile}/{key}/{scenario['id']}.json"
        record["provenance"].update(environmentCompatibilityKey=key, fixtureHash=fp["fixture"]["hash"],
                                    pageSize=5 if scenario["paged"] else 0,
                                    samplesEvidence=f"db.json#/measurements/{index}/samples")
        document.update(environmentCompatibilityKey=key, fixtureHash=fp["fixture"]["hash"],
                        provenance=copy.deepcopy(record["provenance"]))
        pairs.append((record, document))
        stream = measurement([12, 12, 12, 12, 12], metric="db.total_time_ms")
        stream.update(scenario=scenario["id"], pageSize=record["provenance"]["pageSize"])
        streams.append(stream)
    return fp, {"headSha": base.HEAD, "fixtureHash": fp["fixture"]["hash"], "profile": profile, "measurements": streams}, pairs


def write_pairs(root, pairs):
    for record, document in pairs:
        path = root / record["baselinePath"]
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(document), encoding="utf-8")


class ExactEnvironmentDbBaselineTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "performance").mkdir()
        shutil.copyfile(ROOT / "performance/db-scenarios.json", self.root / "performance/db-scenarios.json")
        (self.root / "performance/environment.json").write_text(json.dumps({
            "dbFixtureVersion": 2, "measurement": {"minimumSamples": 5},
        }))
        self.fp, self.profile, self.pairs = variant_group()

    def duration(self, fp=None, profile=None):
        return adapter.duration_results(profile or self.profile, fp or self.fp, ROOT, self.root / "performance/baselines/db")

    def governance(self, pairs=None, *, old=None, ancestor=True):
        current = base.ledger()
        current["baselineUpdates"] = [record for record, _ in (pairs or self.pairs)]

        def git(_root, *args):
            return base.NEW_BASE if args[0] == "rev-parse" else ""

        with patch.object(validator, "_git", side_effect=git), patch.object(validator, "_git_is_ancestor", return_value=ancestor):
            return validator.validate_initial_baselines(self.root, old or base.ledger(), current, "main-base", base.HEAD)

    def test_exact_full_key_selects_complete_group_without_favorable_value_selection(self):
        other_fp, _, other_pairs = variant_group(cpu="Different CPU")
        other_pairs[0][1]["samples"] = [1] * 5
        write_pairs(self.root, self.pairs + other_pairs)
        values = self.duration()
        self.assertEqual(9, len(values))
        self.assertTrue(all(value["decision"] == "pass" and value["baselineValue"] == 11 for value in values))
        self.assertNotEqual(compare.environment_compatibility_key(self.fp), compare.environment_compatibility_key(other_fp))
        unknown = copy.deepcopy(self.fp)
        unknown["runner"]["cpuCount"] += 1
        self.assertTrue(all(value["decision"] == "invalid" for value in self.duration(unknown)))

    def test_unstable_earlier_same_key_cannot_be_replaced_by_another_environment(self):
        self.pairs[0][1]["samples"] = [1, 50, 100, 150, 200]
        _, _, other_pairs = variant_group(cpu="Faster alternate CPU")
        write_pairs(self.root, self.pairs + other_pairs)
        values = self.duration()
        self.assertEqual("unstable", values[0]["decision"])

    def test_canonical_backward_compatibility_and_exact_key_ambiguity(self):
        write_pairs(self.root, self.pairs)
        record, document = copy.deepcopy(self.pairs[0])
        canonical = self.root / "performance/baselines/db/medium" / (document["scenario"] + ".json")
        canonical.write_text(json.dumps(document))
        with self.assertRaisesRegex(adapter.PerformanceContractError, "ambiguous"):
            self.duration()
        document["environmentCompatibilityKey"] = "f" * 64
        canonical.write_text(json.dumps(document))
        self.assertTrue(all(value["decision"] == "pass" for value in self.duration()))
        shutil.rmtree(canonical.parent / self.pairs[0][0]["provenance"]["environmentCompatibilityKey"])
        self.assertTrue(all(value["decision"] == "invalid" for value in self.duration()))

    def test_partial_extra_and_forged_path_variant_groups_fail(self):
        for mutation in ("partial", "extra", "key", "profile", "fixture", "artifact"):
            with self.subTest(mutation=mutation):
                pairs = copy.deepcopy(self.pairs)
                if mutation == "partial":
                    pairs.pop()
                elif mutation == "extra":
                    extra = copy.deepcopy(pairs[0])
                    extra[0]["baselinePath"] = extra[0]["baselinePath"].replace("workspace.list", "unknown.list")
                    pairs.append(extra)
                elif mutation in ("key", "profile"):
                    pairs[0][1]["provenance"]["environmentCompatibilityKey" if mutation == "key" else "profile"] = "f" * 64 if mutation == "key" else "small"
                elif mutation == "fixture":
                    pairs[0][1]["fixtureHash"] = "f" * 64
                else:
                    pairs[0][1]["provenance"]["artifactId"] += 1
                directory = self.root / "performance/baselines"
                if directory.exists():
                    shutil.rmtree(directory)
                write_pairs(self.root, pairs)
                with self.assertRaises(adapter.PerformanceContractError):
                    self.duration()

    def test_governance_accepts_complete_immutable_variants_and_rejects_missing_ledger(self):
        write_pairs(self.root, self.pairs)
        self.assertEqual(9, self.governance())
        previous = base.ledger()
        previous["baselineUpdates"] = [record for record, _ in self.pairs]
        self.assertEqual(0, self.governance(old=previous))
        with self.assertRaisesRegex(validator.BaselineUpdateError, "review records"):
            self.governance(self.pairs[:-1])

    def test_empty_ledger_cannot_ignore_untracked_nested_variant_documents(self):
        write_pairs(self.root, self.pairs)
        with self.assertRaisesRegex(validator.BaselineUpdateError, "review records"):
            validator.validate_initial_baselines(self.root, base.ledger(), base.ledger(), "main", base.HEAD)
        path = self.root / "performance/baselines/db/medium/not-a-full-key/task.list.json"
        path.parent.mkdir()
        path.write_text("{}")
        with self.assertRaisesRegex(validator.BaselineUpdateError, "exact-environment path"):
            self.governance()

    def test_governance_rejects_partial_mixed_and_duplicate_stream_groups(self):
        for field in ("missing", "headSha", "workflowRunId", "workflowRunAttempt", "artifactId", "fixtureHash", "samplesEvidence", "pageSize"):
            with self.subTest(field=field):
                pairs = copy.deepcopy(self.pairs)
                if field == "missing":
                    pairs.pop()
                else:
                    provenance = pairs[0][0]["provenance"]
                    provenance[field] = pairs[1][0]["provenance"][field] if field == "samplesEvidence" else (99 if isinstance(provenance[field], int) else "f" * (40 if field == "headSha" else 64))
                    pairs[0][1]["provenance"] = copy.deepcopy(provenance)
                    if field == "fixtureHash":
                        pairs[0][1]["fixtureHash"] = provenance[field]
                directory = self.root / "performance/baselines"
                if directory.exists():
                    shutil.rmtree(directory)
                write_pairs(self.root, pairs)
                with self.assertRaises(validator.BaselineUpdateError):
                    self.governance(pairs)

    def test_forged_directory_key_and_canonical_duplicate_rejected_in_ledger(self):
        pairs = copy.deepcopy(self.pairs)
        pairs[0][0]["baselinePath"] = pairs[0][0]["baselinePath"].replace(pairs[0][0]["provenance"]["environmentCompatibilityKey"], "f" * 64)
        write_pairs(self.root, pairs)
        with self.assertRaisesRegex(validator.BaselineUpdateError, "path environment key"):
            self.governance(pairs)
        shutil.rmtree(self.root / "performance/baselines")
        pairs = copy.deepcopy(self.pairs)
        canonical = copy.deepcopy(pairs[0])
        canonical[0]["baselinePath"] = f"performance/baselines/db/medium/{canonical[0]['scenarioId']}.json"
        pairs.append(canonical)
        write_pairs(self.root, pairs)
        with self.assertRaisesRegex(validator.BaselineUpdateError, "ambiguous"):
            self.governance(pairs)

    def test_extra_or_excluded_raw_samples_cannot_change_the_variant_stream(self):
        for excluded in (False, True):
            pairs = copy.deepcopy(self.pairs)
            for record, document in pairs:
                if excluded:
                    document["samples"].pop()
                else:
                    document["samples"].append(11)
                record["provenance"]["sampleCount"] = len(document["samples"])
                record["provenance"]["samplesSha256"] = validator.samples_sha256(document["samples"])
                document["provenance"] = copy.deepcopy(record["provenance"])
            write_pairs(self.root, pairs)
            with self.assertRaises(validator.BaselineUpdateError):
                self.governance(pairs)

    def test_historical_variant_sample_record_and_ancestry_tampering_fail(self):
        previous = base.ledger()
        previous["baselineUpdates"] = copy.deepcopy([record for record, _ in self.pairs])
        write_pairs(self.root, self.pairs)
        self.assertEqual(0, self.governance(old=previous))
        with self.assertRaisesRegex(validator.BaselineUpdateError, "distinct approved main history"):
            self.governance(old=previous, ancestor=False)
        self.pairs[0][1]["samples"][0] = 999
        write_pairs(self.root, self.pairs)
        with self.assertRaisesRegex(validator.BaselineUpdateError, "complete sample digest mismatch"):
            self.governance(old=previous)
        self.pairs[0][0]["reason"] = "Attempt to hide unauthorized baseline sample replacement."
        with self.assertRaisesRegex(validator.BaselineUpdateError, "altered or removed"):
            self.governance(old=previous)

    def test_no_arguments_cli_revalidates_variant_inventory_and_samples(self):
        script = self.root / "scripts/ci/verify-performance-baseline-updates.py"
        script.parent.mkdir(parents=True)
        shutil.copyfile(ROOT / "scripts/ci/verify-performance-baseline-updates.py", script)
        def git(*args):
            return subprocess.run(["git", "-c", "user.name=Synthetic baseline test", "-c", "user.email=baseline-test@example.invalid", *args], cwd=self.root, check=True, capture_output=True, text=True).stdout.strip()
        git("init", "-b", "main")
        git("add", ".")
        git("commit", "-m", "Synthetic approved main source")
        source = git("rev-parse", "HEAD")
        for record, document in self.pairs:
            record["newBaselineSha"] = document["baselineSha"] = source
            record["provenance"]["headSha"] = document["provenance"]["headSha"] = source
        write_pairs(self.root, self.pairs)
        ledger = base.ledger()
        ledger["baselineUpdates"] = [record for record, _ in self.pairs]
        (self.root / "performance/baseline-updates.json").write_text(json.dumps(ledger))
        git("add", ".")
        git("commit", "-m", "Synthetic reviewed complete environment baseline")
        def run():
            return subprocess.run([sys.executable, str(script)], cwd=self.root, check=False, capture_output=True, text=True)
        result = run()
        self.assertEqual(0, result.returncode, result.stderr)
        self.pairs[0][1]["samples"][0] = 999
        write_pairs(self.root, self.pairs)
        result = run()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("complete sample digest mismatch", result.stderr)


if __name__ == "__main__":
    unittest.main()
