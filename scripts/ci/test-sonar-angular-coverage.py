#!/usr/bin/env python3
"""Negative controls for native Angular coverage proof."""

from collections import Counter
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock

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

    def test_owned_evidence_bytes_reject_missing_empty_and_traversal(self):
        self.assertEqual(MODULE.owned_bytes(self.root, self.source), b"fixture\n")
        (self.root / "empty").touch()
        for name in ("", ".", "../outside", "/absolute", "frontend//src/app/example.ts",
                     "frontend/./src/app/example.ts", "frontend\\src", "missing", "empty"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                MODULE.owned_bytes(self.root, name)

    def test_compare_admission_checks_exact_receipts_and_native_bytes(self):
        # Temporary fixtures exercise the real on-disk admission and comparison
        # logic. They are never treated as hosted Angular execution evidence.
        identity = {"sourceSha": "a" * 40, "treeSha": "b" * 40,
                    "runId": "123", "attempt": "1", "workspace": str(self.root),
                    "inputs": {"fixture": "c" * 64}}
        parent = self.root / "artifacts/sonar-angular-shard-proof"
        selections = {"baseline": [self.one, self.two],
                      "shard-1": [self.one], "shard-2": [self.two]}
        for lane, selected in selections.items():
            folder = parent / ("sonar-angular-shard-proof-" + lane + "-1")
            folder.mkdir(parents=True)
            suites = [json.loads(self.report(name))["testResults"][0] for name in selected]
            raw = json.dumps({"success": True, "numFailedTests": 0,
                              "numPassedTests": len(suites), "numTotalTests": len(suites),
                              "testResults": suites}).encode()
            (folder / "tests.json").write_bytes(raw)
            (folder / "lcov.info").write_bytes(self.native())
            receipt = {**identity, "lane": lane, "runnerShard": MODULE.runner_shard(lane),
                       "selection": sorted(selected), "testSeconds": 1,
                       "passedTests": len(suites), "coverageFiles": 1,
                       "hashes": {name: MODULE.sha256((folder / name).read_bytes())
                                  for name in MODULE.ARTIFACTS}}
            (folder / "receipt.json").write_text(json.dumps(receipt), encoding="utf-8")
        target = parent / "sonar-angular-shard-proof-shard-1-1"
        receipt_path = target / "receipt.json"
        original = receipt_path.read_bytes()
        output = self.root / "artifacts/sonar-angular-shard-equivalence.json"
        with (mock.patch.object(MODULE, "ROOT", self.root),
              mock.patch.object(MODULE, "checkout_identity", return_value=identity),
              mock.patch.object(MODULE, "tracked_frontend", return_value=self.tracked)):
            MODULE.compare()
            result = json.loads(output.read_bytes())
            self.assertEqual(result["passedTests"], 2)
            self.assertEqual(result["specFiles"], 2)
            self.assertEqual(result["linePoints"], 2)
            self.assertEqual(result["branchPoints"], 2)
            self.assertEqual(result["sourceSha"], identity["sourceSha"])
            self.assertEqual(result["scope"],
                             "ANGULAR_NATIVE_LINE_BRANCH_EQUIVALENCE_ONLY_NOT_SONARCLOUD")
            output.unlink()
            for key, value in (("runId", "124"), ("attempt", "2"), ("lane", "baseline"),
                               ("runnerShard", "2/2"), ("testSeconds", 0),
                               ("selection", [self.two]), ("passedTests", 2),
                               ("coverageFiles", 2), ("hashes", {})):
                with self.subTest(key=key):
                    changed = json.loads(original)
                    changed[key] = value
                    receipt_path.write_text(json.dumps(changed), encoding="utf-8")
                    with self.assertRaises(ValueError):
                        MODULE.compare()
                    self.assertFalse(output.exists())
                    receipt_path.write_bytes(original)
            report = target / "lcov.info"
            raw = report.read_bytes()
            report.write_bytes(raw + b"\n")
            with self.assertRaisesRegex(ValueError, "identity or digest mismatch"):
                MODULE.compare()
            report.write_bytes(raw)
            (parent / "unexpected").mkdir()
            with self.assertRaisesRegex(ValueError, "Missing, stale or unexpected"):
                MODULE.compare()
            self.assertFalse(output.exists())

            (parent / "unexpected").rmdir()
            with self.assertRaisesRegex(ValueError, "Missing, stale or unexpected"):
                MODULE.compare("baseline-only")
            for lane in ("shard-1", "shard-2"):
                (parent / ("sonar-angular-shard-proof-" + lane + "-1")).rename(self.root / lane)
            MODULE.compare("baseline-only")
            canonical = json.loads(output.read_bytes())
            self.assertEqual(canonical["mode"], "baseline-only")
            self.assertFalse(canonical["baselineCompared"])
            self.assertEqual(canonical["scope"], "ANGULAR_NATIVE_UNSHARDED_BASELINE_ONLY_NOT_SONARCLOUD")
            self.assertEqual(set(canonical["lanes"]), {"baseline"})
            for key in ("passedTests", "specFiles", "coverageFiles", "linePoints",
                        "lineVisited", "branchPoints", "branchVisited"):
                self.assertEqual(canonical[key], result[key])
            output.unlink()
            with self.assertRaisesRegex(ValueError, "Missing, stale or unexpected"):
                MODULE.compare()
            with self.assertRaisesRegex(ValueError, "Unknown Angular admission mode"):
                MODULE.compare("unknown")
            baseline = parent / "sonar-angular-shard-proof-baseline-1"
            receipt_file = baseline / "receipt.json"
            receipt = json.loads(receipt_file.read_bytes())
            (baseline / "tests.json").write_bytes(self.report(self.one))
            receipt["hashes"]["tests.json"] = MODULE.sha256((baseline / "tests.json").read_bytes())
            receipt["selection"] = [self.one]
            receipt["passedTests"] = 1
            receipt_file.write_text(json.dumps(receipt), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "test file set differs"):
                MODULE.compare("baseline-only")
            self.assertFalse(output.exists())

    def test_production_admission_preserves_credential_free_native_proof(self):
        root = Path(__file__).resolve().parents[2]
        proof = (root / ".github/workflows/sonar-angular-shard-proof.yml").read_text()
        production = (root / ".github/workflows/sonarcloud-ci.yml").read_text()
        self.assertIn("lane: [baseline, shard-1, shard-2]", proof)
        self.assertIn("needs: collect", proof)
        self.assertIn("github.event.pull_request.head.repo.full_name == github.repository", proof)
        self.assertIn("persist-credentials: false", proof)
        self.assertNotIn("secrets.", proof)
        self.assertNotIn("SONAR_TOKEN", proof)
        lanes = production.split("  angular-native-lanes:\n", 1)[1].split("\n  angular-coverage:\n", 1)[0]
        admission = production.split("  angular-coverage:\n", 1)[1].split("\n  sonarcloud:\n", 1)[0]
        scanner = production.split("  sonarcloud:\n", 1)[1]
        self.assertIn("needs: target", lanes)
        self.assertIn("lane: ${{ fromJSON(needs.target.outputs.analysis_kind == 'pull_request' && '[\"baseline\"]' || '[\"baseline\",\"shard-1\",\"shard-2\"]') }}", lanes)
        self.assertIn("needs: [target, angular-native-lanes]", admission)
        self.assertIn('verify-sonar-angular-coverage.py compare --mode "$PROOF_MODE"', admission)
        self.assertIn("analysis_kind == 'pull_request' && 'baseline-only' || 'full'", admission)
        self.assertIn("Verify Angular coverage handoff before scanner credentials", scanner)
        self.assertLess(scanner.index("Verify Angular coverage handoff before scanner credentials"),
                        scanner.index("Begin pinned SonarCloud analysis"))
        for job in (lanes, admission):
            self.assertIn("contents: read", job)
            self.assertNotIn("secrets.", job)
            self.assertNotIn("SONAR_TOKEN", job)
        self.assertIn("artifacts/sonar-angular-inputs/shard-1/lcov.info,"
                      "artifacts/sonar-angular-inputs/shard-2/lcov.info", scanner)
        self.assertIn("artifacts/sonar-angular-inputs/baseline/lcov.info", scanner)
        self.assertIn("sonar.javascript.lcov.reportPaths=$angular_reports,", scanner)
        self.assertNotIn("sonar.javascript.lcov.reportPaths=frontend/coverage/frontend/lcov.info", scanner)


if __name__ == "__main__":
    unittest.main()
