#!/usr/bin/env python3
"""Cross-language synthetic Playwright hashed-ID/roster contract tests.

The Node implementation emits only sanitized, offline preparation data.
This does not attest actual Playwright discovery, reporter events or artifacts.
"""
from __future__ import annotations

import hashlib
import json
import subprocess
import unittest

SOURCE = "a" * 40
IDENTITY_PREFIX = b"coglatas.playwright-test-id.v1\x00"
INPUTS = (
    ("private/desktop/first", "passed"),
    ("private/desktop/skipped", "skipped"),
    ("private/mobile/first", "passed"),
    ("private/mobile/second", "passed"),
)

NODE_FIXTURE = r"""
import { constructUnverifiedShardEvidence as build } from './tests/ui/playwright-shard-evidence.mjs';
const fullTests = [
  { id: 'private/desktop/first', expectedStatus: 'passed' },
  { id: 'private/desktop/skipped', expectedStatus: 'skipped' },
  { id: 'private/mobile/first', expectedStatus: 'passed' },
  { id: 'private/mobile/second', expectedStatus: 'passed' },
];
const metadata = {
  sourceSha: 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
  runId: 12345, runAttempt: 1, frontendArtifactId: 98765,
};
const make = (index, subset) => build({
  metadata: { ...metadata, index }, fullTests, shardTests: subset,
  results: subset.map(t => ({
    id: t.id, status: t.expectedStatus, retry: 0,
    outcome: t.expectedStatus === 'skipped' ? 'skipped' : 'expected',
  })),
});
process.stdout.write(JSON.stringify({
  first: make(1, fullTests.slice(0, 2)),
  second: make(2, fullTests.slice(2)),
}));
"""


def test_id(raw):
    return hashlib.sha256(
        IDENTITY_PREFIX + raw.encode("utf-8")
    ).hexdigest()


def canonical_digest(roster):
    payload = {
        "sourceSha": roster["sourceSha"],
        "suite": roster["suite"],
        "tests": roster["tests"],
    }
    return hashlib.sha256(json.dumps(
        payload, sort_keys=True, separators=(",", ":"), ensure_ascii=True
    ).encode("utf-8")).hexdigest()


class CrossLanguageShardEvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        proc = subprocess.run(
            ["node", "--input-type=module", "-e", NODE_FIXTURE],
            check=True, capture_output=True, text=True, timeout=30,
        )
        cls.raw_output = proc.stdout
        cls.pair = json.loads(proc.stdout)
        cls.roster = cls.pair["first"]["roster"]
        cls.left = cls.pair["first"]["shard"]
        cls.right = cls.pair["second"]["shard"]

    def test_exact_python_verifier_schema_field_sets(self):
        self.assertEqual({"first", "second"}, set(self.pair))
        self.assertEqual(
            {"schema", "suite", "sourceSha", "frontendProducerSha",
             "runId", "runAttempt", "frontendArtifactId", "tests"},
            set(self.roster),
        )
        required = {
            "schema", "suite", "sourceSha", "frontendProducerSha",
            "runId", "runAttempt", "frontendArtifactId", "rosterSha256",
            "index", "total", "status", "tests",
        }
        for receipt in (self.left, self.right):
            self.assertEqual(required, set(receipt))
            self.assertEqual("coglatas.playwright-shard.v1", receipt["schema"])
            self.assertEqual("angular-static", receipt["suite"])
            self.assertEqual(SOURCE, receipt["sourceSha"])
            self.assertEqual(SOURCE, receipt["frontendProducerSha"])
            self.assertEqual((12345, 1, 98765),
                             (receipt["runId"], receipt["runAttempt"],
                              receipt["frontendArtifactId"]))

    def test_node_hashed_ids_match_independent_python_sha256(self):
        expected = {test_id(name): status for name, status in INPUTS}
        self.assertEqual(expected, {
            row["id"]: row["status"] for row in self.roster["tests"]
        })
        self.assertEqual(
            sorted(expected),
            [row["id"] for row in self.roster["tests"]],
        )

    def test_node_roster_digest_matches_python_canonical_json(self):
        actual = canonical_digest(self.roster)
        self.assertEqual(actual, self.left["rosterSha256"])
        self.assertEqual(actual, self.right["rosterSha256"])
        self.assertEqual(self.roster, self.pair["second"]["roster"])
        self.assertEqual("coglatas.playwright-roster.v1",
                         self.roster["schema"])

    def test_disjoint_complete_synthetic_union_and_explicit_zero_retries(self):
        ids = set()
        for index, shard in enumerate((self.left, self.right), 1):
            self.assertEqual((index, 2, "passed"),
                             (shard["index"], shard["total"], shard["status"]))
            self.assertEqual(
                sorted(row["id"] for row in shard["tests"]),
                [row["id"] for row in shard["tests"]],
            )
            for item in shard["tests"]:
                self.assertEqual({"id", "status", "retries"}, set(item))
                self.assertEqual(0, item["retries"])
                self.assertNotIn(item["id"], ids)
                ids.add(item["id"])
        self.assertEqual(ids, {row["id"] for row in self.roster["tests"]})
        self.assertEqual(
            sum(row["status"] == "skipped" for row in self.left["tests"]
                + self.right["tests"]), 1,
        )

    def test_source_data_is_not_exposed_or_trusted(self):
        for name, _ in INPUTS:
            self.assertNotIn(name, self.raw_output)
        for sample in self.pair.values():
            self.assertIs(sample["requiredCheckQualified"], False)
            self.assertEqual("EXTERNAL_VERIFICATION_REQUIRED",
                             sample["artifactTrust"])
            self.assertNotIn("PASS", str(sample["artifactTrust"]))


if __name__ == "__main__":
    unittest.main()
