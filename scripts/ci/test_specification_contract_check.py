"""Non-required consumer controls; producer metadata is not normative or trusted execution authority."""

from copy import deepcopy
from datetime import datetime, timezone
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import specification_contract as producer
import specification_contract_check as consumer
from test_specification_contract import tool_result

SHA = "a" * 40
NOW = datetime(2026, 10, 10, tzinfo=timezone.utc)


def receipt():
    with tempfile.TemporaryDirectory() as directory, patch.object(producer.subprocess, "check_output", side_effect=[SHA, "", SHA, ""]):
        return producer.capture(Path(directory), SHA, NOW, run_id="42", run_attempt="1")


class SpecificationContractCheckTests(unittest.TestCase):
    def observe(self, report):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "advisory.json"
            path.write_text(json.dumps(report))
            return consumer.consume(path, SHA, "42", "1", NOW)

    def test_same_run_unavailable_registry_is_unknown_without_acceptance(self):
        result = self.observe(receipt())
        self.assertEqual("UNAVAILABLE", result["canonicalStatus"])
        self.assertIsNone(result["declaredDraftCoverage"])

    def test_missing_disabled_or_forged_representative_section_cannot_pass_integrity(self):
        for mutation in ("missing", "version", "scope", "approval", "negative", "extra", "unavailable-count"):
            report = receipt()
            if mutation == "missing": report.pop("secArchRepresentativeEvidence")
            if mutation == "version": report["verifierVersion"] = "DISABLED"
            if mutation == "scope": report["secArchRepresentativeEvidence"]["status"] = "ERROR"
            if mutation == "approval": report["secArchRepresentativeEvidence"]["canonicalSpecMappings"] = "APPROVED"
            if mutation == "negative": report["secArchRepresentativeEvidence"]["observedCases"] = -1
            if mutation == "extra": report["secArchRepresentativeEvidence"]["protectedContents"] = "must not be accepted"
            if mutation == "unavailable-count": report["secArchRepresentativeEvidence"]["requiredRepresentativeCases"] = 0
            with self.subTest(mutation=mutation), self.assertRaises(ValueError): self.observe(report)

    def test_representative_scope_is_bound_to_current_catalogue_and_failure_remains_visible(self):
        report = receipt()
        required = sum(consumer.EXPECTED.values())
        section = {"status": "REPRESENTATIVE_PASS", "receiptDigest": "d" * 64, "observedCases": required,
                   "requiredRepresentativeCases": required,
                   "observedKinds": {"tooling": 148, "inventory": 3, "representativeRuntime": required - 151, "unclassified": 0},
                   "canonicalSpecMappings": "UNRESOLVED"}
        report["secArchRepresentativeEvidence"] = section
        self.assertEqual(section, self.observe(report)["representative"])
        for mutation in ("shrink", "missing-cases", "negative-kind", "bool-count", "digest", "missing-kind"):
            changed = deepcopy(report)
            row = changed["secArchRepresentativeEvidence"]
            if mutation == "shrink": row["requiredRepresentativeCases"] -= 1
            if mutation == "missing-cases": row["observedCases"] -= 1; row["observedKinds"]["representativeRuntime"] -= 1
            if mutation == "negative-kind": row["observedKinds"]["unclassified"] = -1
            if mutation == "bool-count": row["observedCases"] = True
            if mutation == "digest": row["receiptDigest"] = "unknown"
            if mutation == "missing-kind": row["observedKinds"].pop("unclassified")
            with self.subTest(mutation=mutation), self.assertRaises(ValueError): self.observe(changed)
        for status in ("REPRESENTATIVE_FAIL", "REPRESENTATIVE_ERROR", "REPRESENTATIVE_UNVERIFIED"):
            report["secArchRepresentativeEvidence"]["status"] = status
            self.assertEqual(status, self.observe(report)["representative"]["status"])

    def test_wrong_candidate_attempt_run_and_stale_summary_fail(self):
        for key, value in (("candidateSha", "b" * 40), ("runAttempt", "2"), ("runId", "41"), ("capturedAtUtc", "2026-10-01T00:00:00Z")):
            with self.subTest(key=key):
                report = receipt()
                report[key] = value
                with self.assertRaises(ValueError): self.observe(report)

    def test_integrity_error_is_visible_even_when_reporting_step_is_advisory(self):
        report = receipt()
        report["status"] = "ERROR"
        with self.assertRaises(ValueError): self.observe(report)
        report.pop("status")
        report["canonicalInputs"]["status"] = "INVALID_OR_INCOMPLETE"
        with self.assertRaises(ValueError): self.observe(report)

    def test_forged_approval_readiness_attestation_and_missing_lane_fail(self):
        for key, value in (("normativeReady", True), ("ownerApproval", "APPROVED"), ("trustedExecutionAttestation", "PASS"),
                           ("qualifiedNormativeRequirementCount", 1), ("existingVerifierLanes", {})):
            with self.subTest(key=key):
                report = receipt()
                report[key] = value
                with self.assertRaises(ValueError): self.observe(report)

    def test_unavailable_input_cannot_claim_counts_or_revision(self):
        for key, value in (("declaredDraftCoverage", {"activeRequirements": 1}), ("sourceSpecificationRevision", "c" * 40), ("diagnosticCount", 0)):
            report = receipt()
            report["canonicalInputs"][key] = value
            with self.assertRaises(ValueError): self.observe(report)

    def test_observed_lane_counters_are_sanitized_and_mutations_rejected(self):
        report = receipt()
        report["existingVerifierLanes"]["backend"].update(status="OBSERVED_PASS", executionDigest="d" * 64, total=2, passed=2, failed=0, unexecuted=0)
        self.assertEqual(2, self.observe(report)["lanes"]["backend"]["passed"])
        for key, value in (("passed", 999), ("status", "PASS"), ("executionDigest", "private-do-not-publish")):
            mutated = deepcopy(report)
            mutated["existingVerifierLanes"]["backend"][key] = value
            with self.assertRaises(ValueError): self.observe(mutated)

    def test_duplicate_json_and_missing_artifact_fail(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "advisory.json"
            with self.assertRaises(OSError): consumer.consume(path, SHA, "42", "1", NOW)
            path.write_bytes(b'{"schemaVersion":1,"schemaVersion":1}')
            with self.assertRaises(ValueError): consumer.consume(path, SHA, "42", "1", NOW)

    def test_declared_draft_categories_remain_unqualified_and_bad_categories_fail(self):
        report = receipt()
        projected = producer.project_tool_result(json.dumps(tool_result()).encode(), SHA)
        projected.update(sourceSpecificationRevision="c" * 40)
        report["canonicalInputs"] = projected
        self.assertEqual(1, self.observe(report)["declaredDraftCoverage"]["activeRequirements"])
        report["canonicalInputs"]["declaredDraftCoverage"]["verificationClasses"]["Unsupported"] = 1
        with self.assertRaises(ValueError): self.observe(report)

    def test_cli_valid_unknown_receipt_publishes_bounded_advisory_summary(self):
        with tempfile.TemporaryDirectory() as directory:
            path, summary = Path(directory) / "advisory.json", Path(directory) / "summary.md"
            report = receipt()
            report["capturedAtUtc"] = datetime.now(timezone.utc).isoformat()
            path.write_text(json.dumps(report))
            with patch("sys.argv", ["check", "--receipt", str(path), "--candidate-sha", SHA]), patch.dict(os.environ, {"GITHUB_RUN_ID": "42", "GITHUB_RUN_ATTEMPT": "1", "GITHUB_STEP_SUMMARY": str(summary)}), patch("sys.stdout", new_callable=io.StringIO):
                self.assertEqual(0, consumer.main())
            self.assertIn("UNKNOWN", summary.read_text())
            self.assertIn("non-required", summary.read_text())

    def test_cli_error_is_generic_and_does_not_publish_supplied_paths(self):
        with patch("sys.argv", ["check", "--receipt", "private-path-do-not-publish", "--candidate-sha", SHA]), patch.dict(os.environ, {"GITHUB_RUN_ID": "42", "GITHUB_RUN_ATTEMPT": "1", "GITHUB_STEP_SUMMARY": ""}), patch("sys.stdout", new_callable=io.StringIO) as printed:
            self.assertEqual(1, consumer.main())
            self.assertIn("ERROR", printed.getvalue())
            self.assertNotIn("private-path-do-not-publish", printed.getvalue())


if __name__ == "__main__":
    unittest.main()
