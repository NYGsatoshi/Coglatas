#!/usr/bin/env python3
"""Negative controls for native Angular coverage proof."""

from collections import Counter
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location(
    "angular_proof", Path(__file__).with_name("verify-sonar-angular-coverage.py"))
MODULE = importlib.util.module_from_spec(spec)
spec.loader.exec_module(MODULE)


class AngularProofTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.one = "frontend/src/app/features/projects/example.spec.ts"
        self.two = "frontend/src/app/other.spec.ts"
        self.source = "frontend/src/app/example.ts"
        self.tracked = {self.one, self.two, self.source}
        for path in self.tracked:
            file = self.root / path
            file.parent.mkdir(parents=True, exist_ok=True)
            file.write_text("fixture\n")

    def test_runner_shards_keep_all_builder_entry_points(self):
        specs = MODULE.spec_inventory(self.tracked)
        baseline = MODULE.command(self.root, "baseline", specs)
        self.assertFalse(any(item.startswith(("--include", "--runner-args")) for item in baseline))
        self.assertEqual(MODULE.command(self.root, "shard-1", specs), baseline)
        self.assertEqual(MODULE.command(self.root, "shard-2", specs), baseline)
        key = MODULE.PROOF_SHARD_ENV
        inherited = {"CI": "true", key: "2/2", "PATH": "/usr/bin"}
        self.assertNotIn(key, MODULE.runner_environment("baseline", inherited))
        self.assertEqual(MODULE.runner_environment("shard-1", inherited)[key], "1/2")
        self.assertEqual(MODULE.runner_environment("shard-2", inherited)[key], "2/2")
        self.assertEqual(inherited[key], "2/2")
        with self.assertRaises(ValueError):
            MODULE.runner_environment("shard-1", {})
        with self.assertRaises(ValueError):
            MODULE.runner_environment("unknown", inherited)
        config = (MODULE.ROOT / "frontend/vitest.config.ts").read_text()
        self.assertIn(key, config)
        self.assertIn("{ shard: proofShard }", config)
        with self.assertRaises(ValueError):
            MODULE.command(self.root, "unknown", specs)
        with self.assertRaises(ValueError):
            MODULE.spec_inventory(self.tracked | {"frontend/out/untracked.spec.ts"})

    def report(self, name, status="passed"):
        return json.dumps({"success": True, "numFailedTests": 0,
                           "numPassedTests": 1, "numTotalTests": 1,
                           "testResults": [{"name": str(self.root / name),
                                            "status": "passed",
                                            "assertionResults": [{"status": status,
                                                                  "ancestorTitles": ["Suite"],
                                                                  "title": "Case"}]}]}).encode()

    def test_test_inventory_rejects_wrong_file_skip_and_counter(self):
        raw = self.report(self.one)
        self.assertEqual(MODULE.test_inventory(raw, self.root, {self.one}),
                         Counter({(self.one, ("Suite",), "Case"): 1}))
        self.assertEqual(MODULE.test_inventory(raw, self.root, {self.one, self.two}, exact=False),
                         Counter({(self.one, ("Suite",), "Case"): 1}))
        with self.assertRaises(ValueError):
            MODULE.test_inventory(raw, self.root, {self.one, self.two})
        for bad in (self.report(self.two), self.report(self.one, "pending"),
                    raw.replace(b'"numTotalTests": 1', b'"numTotalTests": 2')):
            with self.assertRaises(ValueError):
                MODULE.test_inventory(bad, self.root, {self.one})

    def native(self):
        return ("TN:\nSF:" + self.source + "\n"
                "FN:1,run\nFNDA:1,run\nFNF:1\nFNH:1\n"
                "DA:1,1\nDA:2,0\nLF:2\nLH:1\n"
                "BRDA:1,0,0,1\nBRDA:1,0,1,-\nBRF:2\nBRH:1\nend_of_record\n").encode()

    def test_native_lcov_validates_source_points_and_totals(self):
        raw = self.native()
        parsed = MODULE.native_lcov(raw, self.root, self.tracked)
        self.assertEqual(parsed[self.source], {("DA", 1): True, ("DA", 2): False,
                                               ("BRDA", 1, 0, 0): True,
                                               ("BRDA", 1, 0, 1): False})
        for bad in (raw.replace(b"LF:2", b"LF:3"),
                    raw.replace(b"BRH:1", b"BRH:2"),
                    raw.replace(b"DA:1,1", b"DA:1,1\nDA:1,0"),
                    raw.replace(self.source.encode(), b"frontend/src/missing.ts"),
                    raw.replace(b"end_of_record\n", b""), raw + raw):
            with self.assertRaises(ValueError):
                MODULE.native_lcov(bad, self.root, self.tracked)

    def test_compare_requires_identical_tests_and_native_point_union(self):
        one = Counter({(self.one, ("Suite",), "Case"): 1})
        two = Counter({(self.two, ("Suite",), "Case"): 1})
        a, b = ("DA", 1), ("BRDA", 1, 0, 0)
        baseline = (one + two, {self.source: {a: True, b: True}})
        shards = {"shard-1": (one, {self.source: {a: True, b: False}}),
                  "shard-2": (two, {self.source: {a: False, b: True}})}
        result = MODULE.compare_results(baseline, shards)
        self.assertEqual(result["passedTests"], 2)
        self.assertEqual(result["branchVisited"], 1)
        for changed in (
            {"shard-1": shards["shard-1"]},
            shards | {"shard-2": (one, shards["shard-2"][1])},
            shards | {"shard-2": (two + two, shards["shard-2"][1])},
            shards | {"shard-2": (two, {self.source: {a: False, b: False}})},
            shards | {"shard-2": (two, {self.source: {a: False, b: True, ("DA", 3): False}})},
        ):
            with self.assertRaises(ValueError):
                MODULE.compare_results(baseline, changed)

    def test_original_production_workflow_does_not_admit_angular_proof(self):
        root = Path(__file__).resolve().parents[2]
        proof = (root / ".github/workflows/sonar-angular-shard-proof.yml").read_text()
        production = (root / ".github/workflows/sonarcloud-ci.yml").read_text()
        self.assertIn("lane: [baseline, shard-1, shard-2]", proof)
        self.assertIn("needs: collect", proof)
        self.assertIn("github.event.pull_request.head.repo.full_name == github.repository", proof)
        self.assertIn("persist-credentials: false", proof)
        self.assertNotIn("secrets.", proof)
        self.assertNotIn("SONAR_TOKEN", proof)
        self.assertNotIn("verify-sonar-angular-coverage.py", production)


if __name__ == "__main__":
    unittest.main()
