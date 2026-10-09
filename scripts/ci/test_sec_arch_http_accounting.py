"""Deliberate-invalid controls for explicit HTTP execution accounting."""

import copy
from datetime import datetime, timezone
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

import sec_arch_http_accounting as http

NOW = datetime(2026, 10, 10, 1, tzinfo=timezone.utc)
Q = "{" + http.NS["t"] + "}"


def execution(outcome="Passed"):
    root = ET.Element(Q + "TestRun")
    ET.SubElement(root, Q + "Times", start="2026-10-10T00:00:00Z", finish="2026-10-10T00:01:00Z")
    definition = ET.SubElement(ET.SubElement(root, Q + "TestDefinitions"), Q + "UnitTest", id="1", name=http.GANTT)
    ET.SubElement(definition, Q + "Execution", id="1")
    group, member = http.GANTT.rsplit(".", 1)
    ET.SubElement(definition, Q + "TestMethod", className=group, name=member)
    ET.SubElement(ET.SubElement(root, Q + "Results"), Q + "UnitTestResult", testId="1", executionId="1", testName=http.GANTT,
                  outcome=outcome, startTime="2026-10-10T00:00:01Z", endTime="2026-10-10T00:00:59Z")
    ET.SubElement(ET.SubElement(root, Q + "ResultSummary"), Q + "Counters", total="1",
                  passed="1" if outcome == "Passed" else "0", failed="1" if outcome == "Failed" else "0",
                  executed="0" if outcome == "NotExecuted" else "1")
    return ET.tostring(root)


class HttpAccountingTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        source = http.METHOD_SOURCES[http.GANTT]
        (self.root / source).parent.mkdir(parents=True)
        (self.root / source).write_bytes(b"reviewed test source")
        assemblies = {}
        for name in http.ASSEMBLIES:
            folder = "tests" if name == "Coglatas.Tests" else "src"
            path = self.root / folder / name / "bin/Release/net10.0" / (name + ".dll")
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(name.encode())
            assemblies[name] = http.digest(path.read_bytes())
        self.inventory = {"schemaVersion": 1, "catalogScope": "ACTUAL_COMPOSED_TEST_HOST", "endpointCount": 1,
                          "webAssemblyDigest": assemblies["Coglatas.Web"],
                          "endpoints": [{"surfaceId": "synthetic", "method": "GET", "normalizedPath": "/api/projects/{projectId}/gantt",
                                         "authorizationRequired": False, "kind": "CONTROLLER"}]}
        def observation(control, status, code=None):
            return {"path": "/api/projects/{projectId}/gantt", "method": "GET", "control": control,
                    "observedStatus": status, "expectedStatus": status, "errorCode": code,
                    "observedAtUtc": "2026-10-10T00:00:30Z"}
        self.record = {"schemaVersion": 1, "verifierMethod": http.GANTT, "sourcePath": source,
                       "sourceDigest": http.digest((self.root / source).read_bytes()), "assemblyDigests": assemblies,
                       "ownerApproval": None, "observations": [observation("AUTHORIZED_SAME_SCOPE", 200),
                           observation("ANONYMOUS", 401, "GANTT_AUTHENTICATION_REQUIRED"),
                           observation("CROSS_TENANT", 404, "GANTT_PROJECT_NOT_FOUND")]}
        self.trx = execution()
        self.receipt = {"candidateSha": "a" * 40, "environmentFingerprint": "b" * 64, "runId": "17", "runAttempt": "2",
                        "buildStampMatchesCandidate": True, "executionDigest": http.digest(self.trx), "assemblyDigests": assemblies}
        self.identity = ("a" * 40, "b" * 64, "17", "2")

    def account(self, record=None, trx=None, receipt=None, identity=None):
        return http.account(self.root, self.inventory, [record or self.record], trx or self.trx, NOW, receipt, identity)

    def test_positive_observations_leave_full_contract_and_approval_pending(self):
        result = self.account()
        self.assertEqual(3, result["observedControlCount"])
        self.assertTrue(all(row["accountingOutcome"] == "PASS" for row in result["endpoints"][0]["controls"]))
        self.assertEqual("UNVERIFIED", result["candidateBinding"])
        self.assertEqual("UNVERIFIED", result["endpoints"][0]["resourceCoverageOutcome"])
        self.assertEqual([], result["endpoints"][0]["specIds"])
        self.assertIsNone(result["ownerApproval"])
        self.assertEqual("PRE-AVALONIA SEC-ARCH: BLOCKED", result["preAvaloniaVerdict"])

    def test_independent_exact_identity_reconciliation_does_not_create_approval(self):
        result = self.account(receipt=self.receipt, identity=self.identity)
        self.assertEqual("EXACT_RECEIPT_RECONCILED_TRUSTED_ATTESTATION_PENDING", result["candidateBinding"])
        self.assertEqual("UNVERIFIED", result["trustedAttestation"])

    def test_names_and_source_references_without_observations_receive_no_credit(self):
        record = copy.deepcopy(self.record)
        record["observations"] = []
        result = self.account(record)
        self.assertEqual(0, result["observedControlCount"])
        self.assertEqual(1, result["anonymousOutstandingEndpointCount"])

    def test_missing_positive_cannot_qualify_negative(self):
        record = copy.deepcopy(self.record)
        record["observations"].pop(0)
        result = self.account(record)
        self.assertTrue(all(row["accountingOutcome"] == "UNVERIFIED" for row in result["endpoints"][0]["controls"]))

    def test_failed_or_disabled_execution_cannot_qualify_observations(self):
        for outcome in ("Failed", "NotExecuted"):
            with self.subTest(outcome=outcome):
                result = self.account(trx=execution(outcome))
                self.assertTrue(all(row["accountingOutcome"] == "UNVERIFIED" for row in result["endpoints"][0]["controls"]))

    def test_missing_execution_cannot_qualify_observations(self):
        trx = ET.fromstring(self.trx)
        definition = trx.find("t:TestDefinitions/t:UnitTest/t:TestMethod", http.NS)
        definition.attrib["name"] = "DeletedVerifier"
        for row in (trx.find("t:TestDefinitions/t:UnitTest", http.NS), trx.find("t:Results/t:UnitTestResult", http.NS)):
            row.attrib["name" if row.tag.endswith("UnitTest") else "testName"] = definition.attrib["className"] + ".DeletedVerifier"
        with self.assertRaises(ValueError):
            self.account(trx=ET.tostring(trx))

    def test_wrong_candidate_attempt_build_or_artifact_rejected(self):
        mutations = {"candidateSha": "c" * 40, "runAttempt": "3", "runId": "18", "environmentFingerprint": "c" * 64,
                     "buildStampMatchesCandidate": False, "executionDigest": "c" * 64}
        for key, value in mutations.items():
            receipt = copy.deepcopy(self.receipt)
            receipt[key] = value
            with self.subTest(field=key), self.assertRaises(ValueError):
                self.account(receipt=receipt, identity=self.identity)
        with self.assertRaises(ValueError):
            self.account(receipt=self.receipt)

    def test_source_weakening_deleted_test_wrong_assembly_and_inventory_rejected(self):
        for field, value in (("sourceDigest", "c" * 64), ("sourcePath", "../outside"),
                             ("verifierMethod", http.GANTT + "Renamed")):
            record = copy.deepcopy(self.record)
            record[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                self.account(record)
        record = copy.deepcopy(self.record)
        record["assemblyDigests"]["Coglatas.Web"] = "c" * 64
        with self.assertRaises(ValueError):
            self.account(record)
        self.inventory["webAssemblyDigest"] = "c" * 64
        with self.assertRaises(ValueError):
            self.account()

    def test_unrelated_permission_or_model_error_is_not_authentication_denial(self):
        for status in (400, 403, 404, 415, 500):
            record = copy.deepcopy(self.record)
            record["observations"][1].update(observedStatus=status, expectedStatus=status)
            with self.subTest(status=status), self.assertRaises(ValueError):
                self.account(record)

    def test_untyped_resource_denial_is_rejected(self):
        record = copy.deepcopy(self.record)
        record["observations"][2]["errorCode"] = "ValidationFailed"
        with self.assertRaises(ValueError):
            self.account(record)

    def test_application_owned_authentication_requires_typed_error(self):
        record = copy.deepcopy(self.record)
        record["verifierMethod"] = http.GANTT_COMMANDS
        self.inventory["endpoints"][0].update(method="PATCH", normalizedPath="/api/tasks/{taskItemId}/schedule")
        for row in record["observations"]:
            row.update(method="PATCH", path="/api/tasks/{taskItemId}/schedule")
        record["observations"][1].update(control="ANONYMOUS_WITH_VALID_CSRF", errorCode="GANTT_CSRF_REQUIRED")
        trx = ET.fromstring(self.trx)
        definition = trx.find("t:TestDefinitions/t:UnitTest", http.NS)
        definition.attrib["name"] = http.GANTT_COMMANDS
        definition.find("t:TestMethod", http.NS).attrib["name"] = http.GANTT_COMMANDS.rsplit(".", 1)[1]
        trx.find("t:Results/t:UnitTestResult", http.NS).attrib["testName"] = http.GANTT_COMMANDS
        with self.assertRaises(ValueError):
            self.account(record, trx=ET.tostring(trx))

    def test_wrong_endpoint_method_control_and_timestamp_rejected(self):
        for field, value in (("path", "/api/unknown"), ("method", "POST"), ("control", "NOT_APPLICABLE"),
                             ("observedAtUtc", "2026-10-10T00:01:00Z"), ("observedStatus", 403)):
            record = copy.deepcopy(self.record)
            record["observations"][2][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                self.account(record)

    def test_duplicate_observations_or_receipts_cannot_multiply_coverage(self):
        record = copy.deepcopy(self.record)
        record["observations"].append(copy.deepcopy(record["observations"][2]))
        with self.assertRaises(ValueError):
            self.account(record)
        with self.assertRaises(ValueError):
            http.account(self.root, self.inventory, [self.record, self.record], self.trx, NOW)

    def test_unauthenticated_approval_fields_are_rejected(self):
        record = copy.deepcopy(self.record)
        record["ownerApproval"] = {"approved": True, "actor": "owner"}
        with self.assertRaises(ValueError):
            self.account(record)


if __name__ == "__main__":
    unittest.main()
