#!/usr/bin/env python3
"""Positive and mutation controls for proposed Playwright shard evidence."""
from __future__ import annotations

import copy
import importlib.util
import json
import os
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/ci/playwright_shard_reconcile.py"
spec = importlib.util.spec_from_file_location("pw_shard_reconcile", SCRIPT)
assert spec is not None and spec.loader is not None
reconcile = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reconcile)
SHA = "a" * 40
IDS = ["1" * 64, "2" * 64, "3" * 64, "4" * 64]
ROSTER_TESTS = [
    {"id": IDS[0], "status": "passed"},
    {"id": IDS[1], "status": "skipped"},
    {"id": IDS[2], "status": "passed"},
    {"id": IDS[3], "status": "passed"},
]


def fixtures():
    roster = {
        "schema": reconcile.ROSTER_SCHEMA, "suite": reconcile.SUITE,
        "sourceSha": SHA, "frontendProducerSha": SHA,
        "runId": 12345, "runAttempt": 1, "frontendArtifactId": 98765,
        "tests": copy.deepcopy(ROSTER_TESTS),
    }
    def shard(index, selected):
        return {
            "schema": reconcile.SHARD_SCHEMA, "suite": reconcile.SUITE,
            "sourceSha": SHA, "frontendProducerSha": SHA,
            "runId": 12345, "runAttempt": 1, "frontendArtifactId": 98765,
            "rosterSha256": reconcile.roster_digest(roster),
            "index": index, "total": 2, "status": "passed",
            "tests": [{"id": x["id"], "status": x["status"], "retries": 0}
                      for x in selected],
        }
    return roster, [shard(1, roster["tests"][:2]), shard(2, roster["tests"][2:])]


def verify(roster, shards):
    return reconcile.reconcile(roster, shards, source_sha=SHA, run_id=12345, attempt=1)


class ShardEvidenceTests(unittest.TestCase):
    def test_valid_disjoint_roster_preserves_skips_and_sanitizes_output(self):
        roster, shards = fixtures()
        output = verify(roster, shards)
        self.assertEqual({"shards": 2, "tests": 4, "passed": 3, "skipped": 1},
                         {k: output[k] for k in ("shards", "tests", "passed", "skipped")})
        self.assertEqual("PASS", output["outcome"])
        self.assertEqual("EXTERNAL_VERIFICATION_REQUIRED", output["artifactTrust"])
        self.assertTrue(all(x not in json.dumps(output) for x in IDS))

    def test_missing_duplicate_and_unknown_cases_fail(self):
        for kind in ("missing", "duplicate", "unknown"):
            with self.subTest(kind=kind):
                roster, shards = fixtures()
                if kind == "missing":
                    shards[1]["tests"].pop()
                elif kind == "duplicate":
                    shards[1]["tests"][0]["id"] = IDS[1]
                else:
                    shards[1]["tests"][-1]["id"] = "f" * 64
                with self.assertRaises(reconcile.InvalidEvidence):
                    verify(roster, shards)

    def test_wrong_shard_index_count_and_order_fail(self):
        for key, value in (("index", 1), ("total", 3), ("index", "2")):
            with self.subTest(key=key, value=value):
                roster, shards = fixtures()
                shards[1][key] = value
                with self.assertRaises(reconcile.InvalidEvidence):
                    verify(roster, shards)
        roster, shards = fixtures()
        shards.reverse()
        with self.assertRaises(reconcile.InvalidEvidence):
            verify(roster, shards)

    def test_producer_run_attempt_source_and_digest_mismatch_fail(self):
        for key, value in (("runId", 5), ("runAttempt", 2),
                           ("frontendArtifactId", 98766), ("frontendProducerSha", "b" * 40),
                           ("sourceSha", "b" * 40), ("rosterSha256", "f" * 64)):
            with self.subTest(key=key):
                roster, shards = fixtures()
                shards[1][key] = value
                with self.assertRaises(reconcile.InvalidEvidence):
                    verify(roster, shards)

    def test_expected_roster_drift_wrong_status_retry_failure_and_skip_fail(self):
        cases = ("roster", "status", "retry", "failure", "interrupted", "flaky")
        for kind in cases:
            with self.subTest(kind=kind):
                roster, shards = fixtures()
                if kind == "roster":
                    roster["tests"][0]["status"] = "skipped"
                elif kind == "status":
                    shards[0]["tests"][1]["status"] = "passed"
                elif kind == "retry":
                    shards[0]["tests"][0]["retries"] = 1
                elif kind in ("failure", "interrupted"):
                    shards[0]["status"] = kind
                elif kind == "flaky":
                    shards[1]["tests"][0]["status"] = "flaky"
                with self.assertRaises(reconcile.InvalidEvidence):
                    verify(roster, shards)

    def test_sorted_unique_and_exact_schema_are_mandatory(self):
        for kind in ("unsorted", "duplicate", "unexpected", "empty", "bool"):
            with self.subTest(kind=kind):
                roster, shards = fixtures()
                if kind == "unsorted":
                    roster["tests"].reverse()
                elif kind == "duplicate":
                    roster["tests"].append(copy.deepcopy(roster["tests"][-1]))
                elif kind == "unexpected":
                    roster["privateRawCaseName"] = "never-allowed"
                elif kind == "empty":
                    shards[1]["tests"] = []
                elif kind == "bool":
                    roster["runId"] = True
                with self.assertRaises(reconcile.InvalidEvidence):
                    verify(roster, shards)

    def test_duplicate_json_fields_and_invalid_files_refuse(self):
        with self.assertRaisesRegex(reconcile.InvalidEvidence, "DUPLICATE_JSON_KEY"):
            json.loads('{"schema":1,"schema":2}', object_pairs_hook=reconcile.unique_pairs)
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            path = root / "roster.json"
            path.write_text("{}" + "x" * (reconcile.MAX_BYTES + 1))
            with self.assertRaises(reconcile.InvalidEvidence):
                reconcile.read_json(path)
            path.unlink()
            path.symlink_to(root / "not-in-roster.json")
            with self.assertRaises((OSError, reconcile.InvalidEvidence)):
                reconcile.read_json(path)

    def test_cli_does_not_overwrite_existing_aggregate_or_allow_arbitrary_path(self):
        roster, shards = fixtures()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            for name, payload in (("roster.json", roster),
                                  ("one.json", shards[0]), ("two.json", shards[1])):
                (root / name).write_text(json.dumps(payload))
            old_cwd = os.getcwd()
            try:
                os.chdir(root)
                (root / "artifacts/ci").mkdir(parents=True)
                args = [
                    "--roster", "roster.json", "--shard-one", "one.json",
                    "--shard-two", "two.json", "--source-sha", SHA,
                    "--run-id", "12345", "--attempt", "1",
                    "--output", "artifacts/ci/playwright-shard-aggregate.json",
                ]
                self.assertEqual(0, reconcile.main(args))
                self.assertEqual(1, reconcile.main(args))
                self.assertEqual(1, reconcile.main(args[:-1] + ["other.json"]))
                report = json.loads((root / "artifacts/ci/playwright-shard-aggregate.json").read_text())
                self.assertEqual(4, report["tests"])
            finally:
                os.chdir(old_cwd)

    def test_sha_is_exact_and_cli_is_not_trusted_artifact_attestation(self):
        roster, shards = fixtures()
        for sha in ("", "abc", "A" * 40, "a" * 39, "a" * 41):
            with self.subTest(sha=sha):
                with self.assertRaises(reconcile.InvalidEvidence):
                    reconcile.reconcile(roster, shards, source_sha=sha, run_id=12345, attempt=1)
        output = verify(roster, shards)
        self.assertEqual("EXTERNAL_VERIFICATION_REQUIRED", output["artifactTrust"])


if __name__ == "__main__":
    unittest.main()
