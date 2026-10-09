"""False-green controls for observed SEC-ARCH execution capture."""

import copy
from datetime import datetime, timezone
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

import sec_arch_evidence as evidence

NOW = datetime(2026, 10, 9, 13, tzinfo=timezone.utc)
Q = "{" + evidence.NS["t"] + "}"


def fixture() -> ET.Element:
    root = ET.Element(Q + "TestRun")
    ET.SubElement(root, Q + "Times", start="2026-10-09T12:00:00Z", finish="2026-10-09T12:01:00Z")
    results, definitions = ET.SubElement(root, Q + "Results"), ET.SubElement(root, Q + "TestDefinitions")
    for method, count in evidence.EXPECTED.items():
        for index in range(count):
            name, identity = method + "(case:" + str(index) + ")", str(len(results))
            definition = ET.SubElement(definitions, Q + "UnitTest", id=identity, name=name)
            ET.SubElement(definition, Q + "Execution", id=identity)
            group, member = method.rsplit(".", 1)
            ET.SubElement(definition, Q + "TestMethod", className=group, name=member)
            ET.SubElement(results, Q + "UnitTestResult", testId=identity, executionId=identity,
                          testName=name, outcome="Passed", startTime="2026-10-09T12:00:01Z", endTime="2026-10-09T12:00:02Z")
    ET.SubElement(ET.SubElement(root, Q + "ResultSummary"), Q + "Counters")
    recalculate(root)
    return root


def recalculate(root: ET.Element) -> None:
    results = root.find(Q + "Results")
    counts = {outcome: sum(row.attrib["outcome"] == outcome for row in results)
              for outcome in ("Passed", "Failed")}
    root.find(Q + "ResultSummary/" + Q + "Counters").attrib.update(
        total=str(len(results)), passed=str(counts["Passed"]), failed=str(counts["Failed"]),
        executed=str(counts["Passed"] + counts["Failed"]))


def observe(root: ET.Element) -> dict:
    return evidence.observed_trx(ET.tostring(root), NOW)


class ExecutionEvidenceTests(unittest.TestCase):
    def test_capture_command_preserves_existing_receipt_bytes(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "receipt.json"
            original = b'{"outcome":"FAIL","historical":true}\n'
            output.write_bytes(original)
            arguments = ["capture", "--candidate-sha", "a" * 40, "--trx", "synthetic.trx", "--output", str(output)]
            with patch.object(sys, "argv", arguments), patch.object(evidence, "capture", return_value={"outcome": "PASS"}), \
                    patch.object(evidence.subprocess, "check_output", return_value="synthetic-sdk"):
                with self.assertRaises(FileExistsError):
                    evidence.main()
            self.assertEqual(original, output.read_bytes())

    def test_positive_complete_observation_is_sanitized_and_exact(self):
        result = observe(fixture())
        self.assertEqual("PASS", result["outcome"])
        self.assertEqual(197, result["observedCaseCount"])
        self.assertEqual([], result["missingMethods"])
        self.assertTrue(all(set(row) == {"method", "caseDigest", "outcome"} for row in result["cases"]))

    def test_disabled_missing_verifier_is_unverified(self):
        root = fixture()
        root.find(Q + "Results").remove(root.find(Q + "Results")[-1])
        recalculate(root)
        result = observe(root)
        self.assertEqual("UNVERIFIED", result["outcome"])
        self.assertEqual(1, len(result["missingMethods"]))

    def test_missing_parent_policy_execution_cannot_use_other_passes_as_coverage(self):
        root = fixture()
        results = root.find(Q + "Results")
        for row in list(results):
            if ".SecurityArchitectureParentRlsTests." in row.attrib["testName"]:
                results.remove(row)
        recalculate(root)
        result = observe(root)
        self.assertEqual("UNVERIFIED", result["outcome"])
        self.assertEqual(2, len(result["missingMethods"]))

    def test_missing_prepared_policy_catalogue_is_unverified(self):
        root = fixture()
        results = root.find(Q + "Results")
        for row in list(results):
            if ".SecurityArchitectureRlsCatalogTests." in row.attrib["testName"]:
                results.remove(row)
        recalculate(root)
        result = observe(root)
        self.assertEqual("UNVERIFIED", result["outcome"])
        self.assertEqual(1, len(result["missingMethods"]))

    def test_missing_live_origin_controls_are_unverified(self):
        root = fixture()
        results = root.find(Q + "Results")
        for row in list(results):
            if any(method in row.attrib["testName"] for method in (
                "ProductTransportRejectsUnapprovedOriginsWithAuthenticatedLiveControls",
                "ProductTransportApprovedOriginRetainsSessionAndResourceAuthorization",
            )):
                results.remove(row)
        recalculate(root)
        result = observe(root)
        self.assertEqual("UNVERIFIED", result["outcome"])
        self.assertEqual(2, len(result["missingMethods"]))

    def test_missing_phase2_http_and_event_controls_are_unverified(self):
        root = fixture()
        results = root.find(Q + "Results")
        new_methods = (
            "EveryComposedProtectedHttpEndpointRejectsAnonymousRequestsAfterValidCsrf",
            "ProductTransportReconnectUsesCurrentHttpCatchUpAuthority",
            "ProductTransportTenantCookieSwitchCannotRetargetExistingOrNewSubscriptions",
            "EveryDeclaredEventHasLiveTenantAndCurrentMembershipControls",
            "ProjectAndWorkspaceUnsubscriptionOnlyRemovesCallingConnection",
        )
        for row in list(results):
            if any(method in row.attrib["testName"] for method in new_methods):
                results.remove(row)
        recalculate(root)
        result = observe(root)
        self.assertEqual("UNVERIFIED", result["outcome"])
        self.assertEqual(5, len(result["missingMethods"]))

    def test_failed_and_skipped_executions_never_pass(self):
        for actual, expected in (("Failed", "FAIL"), ("NotExecuted", "UNVERIFIED"), ("Aborted", "ERROR")):
            with self.subTest(actual=actual):
                root = fixture()
                root.find(Q + "Results")[0].attrib["outcome"] = actual
                recalculate(root)
                self.assertEqual(expected, observe(root)["outcome"])

    def test_omitted_manual_replay_provider_controls_are_unverified(self):
        root = fixture()
        results = root.find(Q + "Results")
        for row in list(results):
            if row.attrib["testName"].startswith(evidence.REPLAY_PREFIX):
                results.remove(row)
        recalculate(root)
        result = observe(root)
        self.assertEqual("UNVERIFIED", result["outcome"])
        self.assertEqual(6, len(result["missingMethods"]))

    def test_unclassified_manual_replay_method_cannot_replace_required_execution(self):
        root = fixture()
        definition = next(item for item in root.find(Q + "TestDefinitions")
                          if item.attrib["name"].startswith(evidence.REPLAY_PREFIX))
        definition.find(Q + "TestMethod").attrib["name"] = "UnclassifiedReplayControl"
        with self.assertRaises(ValueError):
            observe(root)

    def test_injected_duplicate_result_is_rejected(self):
        root = fixture()
        root.find(Q + "Results").append(copy.deepcopy(root.find(Q + "Results")[0]))
        recalculate(root)
        with self.assertRaises(ValueError):
            observe(root)

    def test_falsified_pass_counter_is_rejected(self):
        root = fixture()
        root.find(Q + "Results")[0].attrib["outcome"] = "Failed"
        with self.assertRaises(ValueError):
            observe(root)

    def test_identity_alias_or_unclassified_method_is_rejected(self):
        for field, value in (("executionId", "another-run"), ("testName", "secret-input")):
            root = fixture()
            root.find(Q + "Results")[0].attrib[field] = value
            with self.assertRaises(ValueError):
                observe(root)
        root = fixture()
        root.find(Q + "TestDefinitions")[0].find(Q + "TestMethod").attrib["name"] = "DisabledVerifier"
        with self.assertRaises(ValueError):
            observe(root)

    def test_stale_future_and_naive_timestamp_are_rejected(self):
        for start, finish in (("2026-10-01T12:00:00Z", "2026-10-01T12:01:00Z"),
                              ("2026-10-10T12:00:00Z", "2026-10-10T12:01:00Z"),
                              ("2026-10-09T12:00:00", "2026-10-09T12:01:00Z")):
            root = fixture()
            root.find(Q + "Times").attrib.update(start=start, finish=finish)
            with self.assertRaises(ValueError):
                observe(root)

    def test_case_outside_recorded_run_is_rejected(self):
        root = fixture()
        root.find(Q + "Results")[0].attrib["endTime"] = "2026-10-09T12:02:00Z"
        with self.assertRaises(ValueError):
            observe(root)

    def test_xml_entities_manual_json_and_unknown_namespace_are_rejected(self):
        for data in (b'<!DOCTYPE TestRun [<!ENTITY x "private">]><TestRun/>',
                     b'{"outcome":"PASS"}', b'<TestRun/>'):
            with self.assertRaises((ValueError, ET.ParseError)):
                evidence.observed_trx(data, NOW)

    def test_wrong_sha_environment_run_attempt_or_missing_build_binding_is_rejected(self):
        receipt = {"candidateSha": "a" * 40, "environmentFingerprint": "b" * 64,
                   "runId": "123", "runAttempt": "1", "buildStampMatchesCandidate": True}
        evidence.reconcile_identity(receipt, "a" * 40, "b" * 64, "123", "1")
        for field, value in (("candidateSha", "c" * 40), ("environmentFingerprint", "d" * 64),
                             ("runId", "124"), ("runAttempt", "2"), ("buildStampMatchesCandidate", False),
                             ("buildStampMatchesCandidate", "true")):
            invalid = dict(receipt, **{field: value})
            with self.assertRaises(ValueError):
                evidence.reconcile_identity(invalid, "a" * 40, "b" * 64, "123", "1")


if __name__ == "__main__":
    unittest.main()
