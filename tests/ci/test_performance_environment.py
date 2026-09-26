from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
COMMON_PATH = ROOT / "scripts" / "performance" / "common.py"
spec = importlib.util.spec_from_file_location("performance_common_test", COMMON_PATH)
assert spec is not None and spec.loader is not None
common = importlib.util.module_from_spec(spec)
spec.loader.exec_module(common)


class PerformanceEnvironmentContractTests(unittest.TestCase):
    def test_load_json_accepts_utf8_bom_but_remains_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            temp = Path(directory)
            bom_json = temp / "fixture.json"
            bom_json.write_bytes(b"\xef\xbb\xbf" + json.dumps({"schemaVersion": 1}).encode("utf-8"))
            self.assertEqual({"schemaVersion": 1}, common.load_json(bom_json))

            malformed = temp / "malformed.json"
            malformed.write_bytes(b"\xef\xbb\xbf{not-json}")
            with self.assertRaises(common.PerformanceContractError):
                common.load_json(malformed)

            invalid_utf8 = temp / "invalid-utf8.json"
            invalid_utf8.write_bytes(b"{\xff}")
            with self.assertRaises(common.PerformanceContractError):
                common.load_json(invalid_utf8)

    def test_fixture_hash_is_stable_and_profile_specific(self) -> None:
        hashes = []
        for profile in ("small", "medium", "large"):
            first = common.fixture_hash(profile)
            second = common.fixture_hash(profile)
            self.assertEqual(first, second)
            self.assertRegex(first, r"^[0-9a-f]{64}$")
            hashes.append(first)
        self.assertEqual(3, len(set(hashes)))

    def test_fixture_evidence_requires_exact_manifest_cardinalities(self) -> None:
        _, profile = common.load_profile("small")
        evidence = {
            "schemaVersion": 1,
            "fixtureVersion": 1,
            "seedManifestVersion": 1,
            "profile": "small",
            "seed": profile["seed"],
            "fixtureHash": common.fixture_hash("small"),
            "migrationStatus": "current",
            "complete": True,
            "cardinalities": profile["counts"],
            "focus": profile["focus"],
            "identities": {
                "tenantSlug": "perf-small",
                "operatorEmail": "perf-small-operator@example.test",
                "workspaceId": "00000000-0000-0000-0000-000000000001",
                "taskListProjectId": "00000000-0000-0000-0000-000000000002",
                "ganttProjectId": "00000000-0000-0000-0000-000000000002",
                "kanbanProjectId": "00000000-0000-0000-0000-000000000002",
            },
        }
        common.validate_fixture_evidence(evidence, "small")
        drifted = json.loads(json.dumps(evidence))
        drifted["cardinalities"]["tasks"] += 1
        with self.assertRaises(common.PerformanceContractError):
            common.validate_fixture_evidence(drifted, "small")

    def test_public_and_production_like_targets_are_rejected(self) -> None:
        for allowed in (
            "http://127.0.0.1:18080",
            "http://localhost:18080",
            "http://coglatas-performance:8080",
            "http://performance-app:8080",
        ):
            self.assertEqual(allowed, common.validate_target(allowed))

        for rejected in (
            "https://127.0.0.1:18080",
            "http://example.com:8080",
            "http://school.example.jp:8080",
            "http://127.0.0.1",
            "http://127.0.0.1:18080/api",
            "http://user:secret@127.0.0.1:18080",
        ):
            with self.subTest(rejected=rejected):
                with self.assertRaises(common.PerformanceContractError):
                    common.validate_target(rejected)

    def test_measurement_envelope_fails_closed(self) -> None:
        verifier = ROOT / "scripts" / "performance" / "verify-samples.py"
        environment = {
            "measurement": {
                "minimumSamples": 5,
                "warmupSamplesExcluded": True,
            }
        }
        with tempfile.TemporaryDirectory() as directory:
            temp = Path(directory)
            environment_path = temp / "environment.json"
            results_path = temp / "results.json"
            environment_path.write_text(json.dumps(environment), encoding="utf-8")

            success = {
                "warmupSamplesExcluded": True,
                "measuredSamples": 5,
                "environmentStable": True,
                "benchmarkExitCode": 0,
                "timedOut": False,
            }
            results_path.write_text(json.dumps(success), encoding="utf-8")
            completed = subprocess.run(
                [
                    sys.executable,
                    str(verifier),
                    "--results",
                    str(results_path),
                    "--environment-contract",
                    str(environment_path),
                ],
                check=False,
                capture_output=True,
                text=True,
            )
            self.assertEqual(0, completed.returncode, completed.stderr)

            for mutation in (
                {"measuredSamples": 4},
                {"warmupSamplesExcluded": False},
                {"environmentStable": False},
                {"benchmarkExitCode": 1},
                {"timedOut": True},
            ):
                failing = success | mutation
                results_path.write_text(json.dumps(failing), encoding="utf-8")
                completed = subprocess.run(
                    [
                        sys.executable,
                        str(verifier),
                        "--results",
                        str(results_path),
                        "--environment-contract",
                        str(environment_path),
                    ],
                    check=False,
                    capture_output=True,
                    text=True,
                )
                self.assertNotEqual(0, completed.returncode, mutation)

    def test_environment_contract_keeps_warmup_out_of_measurement(self) -> None:
        environment = json.loads(
            (ROOT / "performance" / "environment.json").read_text(encoding="utf-8")
        )
        self.assertFalse(environment["warmup"]["measured"])
        self.assertTrue(environment["measurement"]["warmupSamplesExcluded"])
        self.assertGreater(environment["warmup"]["iterations"], 0)
        self.assertGreater(environment["measurement"]["minimumSamples"], 0)
        self.assertEqual({"cold", "warm"}, set(environment["warmup"]["browserAssetCachePolicy"]))

    def test_lifecycle_contract_requires_clean_teardown(self) -> None:
        harness = (ROOT / "scripts" / "performance" / "with-environment.sh").read_text(encoding="utf-8")
        self.assertIn("down --volumes --remove-orphans", harness)
        self.assertIn("trap 'status=$?;", harness)
        self.assertIn('timeout "$COMMAND_TIMEOUT"', harness)
        self.assertIn("preflight.py", harness)
        self.assertIn("warmup.py", harness)
        self.assertIn("collect-environment.py", harness)


if __name__ == "__main__":
    unittest.main()
