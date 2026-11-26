#!/usr/bin/env python3
"""Positive and mutation controls for proposed Playwright shard evidence."""
from __future__ import annotations

import copy
import importlib.util
import json
import os
import subprocess
import sys
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
    """Construct a synthetic full roster and two disjoint native-style receipts."""
    roster = {
        "schema": reconcile.ROSTER_SCHEMA, "suite": reconcile.SUITE,
        "sourceSha": SHA, "frontendProducerSha": SHA,
        "runId": 12345, "runAttempt": 1, "frontendArtifactId": 98765,
        "tests": copy.deepcopy(ROSTER_TESTS),
    }
    def shard(index, selected):
        """Build a shard receipt with the same expected roster digest."""
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
    """Apply the real reconciler with fixed synthetic run and source IDs."""
    return reconcile.reconcile(roster, shards, source_sha=SHA, run_id=12345, attempt=1)


class ShardEvidenceTests(unittest.TestCase):
    """Verify fail-closed roster, receipt, file and CLI boundaries."""
    def test_actual_cli_rejects_private_argument_values_without_echo_or_receipt(self):
        """Ensure invalid CLI input cannot leak private arguments or create output."""
        canary = "SYNTHETIC_PRIVATE_ARGUMENT_CANARY"
        with tempfile.TemporaryDirectory() as tmp:
            args = ["--roster", "roster.json", "--shard-one", "one.json",
                    "--shard-two", "two.json", "--source-sha", SHA,
                    "--run-id", "12345", "--attempt", "1",
                    "--output", "artifacts/ci/playwright-shard-aggregate.json"]
            variants = [args + ["--unknown", canary],
                        args[:9] + [canary] + args[10:],
                        ["--roster", canary], ["--help", "--unknown", canary],
                        ["--sou", canary], ["--attempt"]]
            for values in variants:
                with self.subTest(selection=variants.index(values)):
                    child = subprocess.run([sys.executable, "-B", str(SCRIPT), *values],
                                           cwd=tmp, capture_output=True, text=True, timeout=10)
                    self.assertEqual(1, child.returncode)
                    self.assertEqual("", child.stdout)
                    self.assertEqual("Playwright shard reconciliation refused: INVALID_ARGUMENTS\n",
                                     child.stderr)
                    self.assertNotIn(canary, child.stdout + child.stderr)
                    self.assertFalse((Path(tmp) / "artifacts").exists())
            help_result = subprocess.run([sys.executable, "-B", str(SCRIPT), "--help"],
                                         cwd=tmp, capture_output=True, text=True, timeout=10)
            self.assertEqual(0, help_result.returncode)
            self.assertEqual("", help_result.stderr)
            self.assertIn("playwright-shard-reconcile", help_result.stdout)
            self.assertNotIn(str(SCRIPT), help_result.stdout)

    def test_real_node_fixture_is_accepted_by_python_reconciler(self):
        """Run the existing Node fixture through the real Python union gate."""
        interop_path = ROOT / "scripts/ci/test-playwright-shard-interop.py"
        fixture_spec = importlib.util.spec_from_file_location(
            "playwright_shard_node_fixture", interop_path)
        self.assertIsNotNone(fixture_spec)
        self.assertIsNotNone(fixture_spec.loader)
        fixture_module = importlib.util.module_from_spec(fixture_spec)
        fixture_spec.loader.exec_module(fixture_module)
        # The fixture invokes the original nonpublishing Node evidence producer;
        # no mocked/Python-created shard receipts can satisfy this assertion.
        produced = subprocess.run(
            ["node", "--input-type=module", "-e", fixture_module.NODE_FIXTURE],
            cwd=ROOT, stdin=subprocess.DEVNULL,
            capture_output=True, text=True, timeout=30,
        )
        self.assertEqual(0, produced.returncode, "NODE_FIXTURE_FAILED")
        self.assertFalse(produced.stderr, "NODE_FIXTURE_DIAGNOSTICS")
        self.assertLess(len(produced.stdout.encode("utf-8")), 32 * 1024)
        pair = json.loads(
            produced.stdout, object_pairs_hook=reconcile.unique_pairs,
            parse_constant=reconcile._reject_nonfinite,
        )
        self.assertEqual({"first", "second"}, set(pair))
        first, second = pair["first"], pair["second"]
        self.assertEqual(first["roster"], second["roster"])
        self.assertIs(first["requiredCheckQualified"], False)
        self.assertIs(second["requiredCheckQualified"], False)
        self.assertEqual("EXTERNAL_VERIFICATION_REQUIRED", first["artifactTrust"])
        self.assertEqual("EXTERNAL_VERIFICATION_REQUIRED", second["artifactTrust"])
        actual = verify(first["roster"], [first["shard"], second["shard"]])
        self.assertEqual(
            (2, 4, 3, 1, "PASS", "EXTERNAL_VERIFICATION_REQUIRED", False),
            (actual["shards"], actual["tests"], actual["passed"],
             actual["skipped"], actual["outcome"], actual["artifactTrust"],
             actual["requiredCheckQualified"]),
        )
        # A changed Node receipt must still be refused by the actual
        # cross-language consumer, not only by independent schema helpers.
        foreign = copy.deepcopy(second["shard"])
        foreign["tests"][0]["status"] = (
            "skipped" if foreign["tests"][0]["status"] == "passed" else "passed"
        )
        with self.assertRaisesRegex(reconcile.InvalidEvidence,
                                    "^SHARD_STATUS_MISMATCH$"):
            verify(first["roster"], [first["shard"], foreign])

    def test_valid_disjoint_roster_preserves_skips_and_sanitizes_output(self):
        """Preserve legitimate executed/skipped counts and untrusted output flags."""
        roster, shards = fixtures()
        output = verify(roster, shards)
        self.assertEqual({"shards": 2, "tests": 4, "passed": 3, "skipped": 1},
                         {k: output[k] for k in ("shards", "tests", "passed", "skipped")})
        self.assertEqual("PASS", output["outcome"])
        self.assertEqual("EXTERNAL_VERIFICATION_REQUIRED", output["artifactTrust"])
        self.assertIs(output["requiredCheckQualified"], False)
        self.assertTrue(all(x not in json.dumps(output) for x in IDS))

    def test_all_skipped_roster_refuses_false_green(self):
        """Refuse a complete but entirely skipped two-shard union."""
        roster, shards = fixtures()
        for record in roster["tests"]:
            record["status"] = "skipped"
        for shard in shards:
            for record in shard["tests"]:
                record["status"] = "skipped"
            shard["rosterSha256"] = reconcile.roster_digest(roster)
        with self.assertRaisesRegex(reconcile.InvalidEvidence,
                                    "^NO_PASSED_TESTS$"):
            verify(roster, shards)

    def test_one_entirely_skipped_shard_cannot_pass_union(self):
        """Refuse a shard that executed no passing tests despite a green union."""
        for skipped_index in (0, 1):
            with self.subTest(skipped_shard=skipped_index + 1):
                roster, shards = fixtures()
                skipped_ids = {record["id"] for record in shards[skipped_index]["tests"]}
                for record in roster["tests"]:
                    if record["id"] in skipped_ids:
                        record["status"] = "skipped"
                for shard in shards:
                    for record in shard["tests"]:
                        if record["id"] in skipped_ids:
                            record["status"] = "skipped"
                    shard["rosterSha256"] = reconcile.roster_digest(roster)
                self.assertTrue(any(record["status"] == "passed"
                                    for record in shards[1 - skipped_index]["tests"]))
                with self.assertRaisesRegex(reconcile.InvalidEvidence,
                                            "^SHARD_NO_PASSED_TESTS$"):
                    verify(roster, shards)

    def test_missing_duplicate_and_unknown_cases_fail(self):
        """Reject any incomplete, repeated or foreign shard case identity."""
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
        """Require the exact first and second of two shard reports."""
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
        """Reject source, run, attempt and producer-artifact identity drift."""
        for key, value in (("runId", 5), ("runAttempt", 2),
                           ("frontendArtifactId", 98766), ("frontendProducerSha", "b" * 40),
                           ("sourceSha", "b" * 40), ("rosterSha256", "f" * 64)):
            with self.subTest(key=key):
                roster, shards = fixtures()
                shards[1][key] = value
                with self.assertRaises(reconcile.InvalidEvidence):
                    verify(roster, shards)

    def test_expected_roster_drift_wrong_status_retry_failure_and_skip_fail(self):
        """Reject altered roster statuses, retries and non-successful runs."""
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
        """Enforce bounded, ordered, exact-keys case schemas."""
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
        """Reject repeated JSON keys and malformed/unsafe evidence files."""
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

    def test_input_parent_symlink_hardlink_traversal_and_nonfinite_refuse(self):
        """Reject unsafe input paths, aliases, hardlinks and nonfinite JSON."""
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            trusted = root / "trusted"
            trusted.mkdir()
            receipt = trusted / "receipt.json"
            receipt.write_text('{"schema":"synthetic"}', encoding="utf-8")
            self.assertEqual({"schema": "synthetic"}, reconcile.read_json(receipt))
            alias = root / "symlinked-parent"
            alias.symlink_to(trusted, target_is_directory=True)
            with self.assertRaises((OSError, reconcile.InvalidEvidence)):
                reconcile.read_json(alias / "receipt.json")
            with self.assertRaises((OSError, reconcile.InvalidEvidence)):
                reconcile.read_json(trusted / ".." / "trusted" / "receipt.json")
            second = root / "hardlink.json"
            os.link(receipt, second)
            with self.assertRaises(reconcile.InvalidEvidence):
                reconcile.read_json(receipt)
            second.unlink()
            self.assertEqual({"schema": "synthetic"}, reconcile.read_json(receipt))
            receipt.write_text('{"schema":NaN}', encoding="utf-8")
            with self.assertRaisesRegex(reconcile.InvalidEvidence, "NONFINITE_JSON"):
                reconcile.read_json(receipt)

    def test_cli_does_not_overwrite_existing_aggregate_or_allow_arbitrary_path(self):
        """Preserve fixed artifact path and exclusive-write behavior."""
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

    def test_symlinked_output_ancestors_never_escape_fixed_artifact_directory(self):
        """Reject output-directory links that could redirect a receipt."""
        roster, shards = fixtures()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            for name, payload in (("roster.json", roster),
                                  ("one.json", shards[0]), ("two.json", shards[1])):
                (root / name).write_text(json.dumps(payload))
            outside_ci = root / "outside" / "ci"
            outside_ci.mkdir(parents=True)
            args = ["--roster", "roster.json", "--shard-one", "one.json",
                    "--shard-two", "two.json", "--source-sha", SHA,
                    "--run-id", "12345", "--attempt", "1",
                    "--output", "artifacts/ci/playwright-shard-aggregate.json"]
            original_cwd = os.getcwd()
            try:
                os.chdir(root)
                artifacts = Path("artifacts")
                artifacts.symlink_to(root / "outside", target_is_directory=True)
                self.assertEqual(1, reconcile.main(args))
                self.assertFalse((outside_ci / "playwright-shard-aggregate.json").exists())
                artifacts.unlink()
                artifacts.mkdir()
                (artifacts / "ci").symlink_to(outside_ci, target_is_directory=True)
                self.assertEqual(1, reconcile.main(args))
                self.assertFalse((outside_ci / "playwright-shard-aggregate.json").exists())
            finally:
                os.chdir(original_cwd)

    def test_boolean_and_noninteger_provenance_cannot_masquerade_as_one(self):
        """Reject boolean, float or string provenance IDs."""
        # Python True == 1; a plain equality silently accepts forged JSON booleans.
        # The source/run/attempt/artifact identity is security-relevant metadata.
        for owner in ("roster", "one", "two"):
            for key in ("runId", "runAttempt", "frontendArtifactId"):
                for invalid in (True, 1.0, "1"):
                    with self.subTest(owner=owner, key=key, invalid=invalid):
                        roster, shards = fixtures()
                        roster["runId"] = 1
                        roster["frontendArtifactId"] = 1
                        for receipt in shards:
                            receipt["runId"] = 1
                            receipt["frontendArtifactId"] = 1
                        selected = {"roster": roster, "one": shards[0], "two": shards[1]}[owner]
                        selected[key] = invalid
                        with self.assertRaises(reconcile.InvalidEvidence):
                            reconcile.reconcile(
                                roster, shards, source_sha=SHA, run_id=1, attempt=1
                            )

    def test_sha_is_exact_and_cli_is_not_trusted_artifact_attestation(self):
        """Keep source SHA strict and external artifact verification mandatory."""
        roster, shards = fixtures()
        for sha in ("", "abc", "A" * 40, "a" * 39, "a" * 41):
            with self.subTest(sha=sha):
                with self.assertRaises(reconcile.InvalidEvidence):
                    reconcile.reconcile(roster, shards, source_sha=sha, run_id=12345, attempt=1)
        output = verify(roster, shards)
        self.assertEqual("EXTERNAL_VERIFICATION_REQUIRED", output["artifactTrust"])


if __name__ == "__main__":
    unittest.main()
