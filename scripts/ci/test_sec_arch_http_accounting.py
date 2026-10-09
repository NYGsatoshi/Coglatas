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


def execution(outcome="Passed", method=http.GANTT):
    root = ET.Element(Q + "TestRun")
    ET.SubElement(root, Q + "Times", start="2026-10-10T00:00:00Z", finish="2026-10-10T00:01:00Z")
    definition = ET.SubElement(ET.SubElement(root, Q + "TestDefinitions"), Q + "UnitTest", id="1", name=method)
    ET.SubElement(definition, Q + "Execution", id="1")
    group, member = method.rsplit(".", 1)
    ET.SubElement(definition, Q + "TestMethod", className=group, name=member)
    ET.SubElement(ET.SubElement(root, Q + "Results"), Q + "UnitTestResult", testId="1", executionId="1", testName=method,
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
        (self.root / source).write_bytes(("public async Task " + http.GANTT.rsplit(".", 1)[1] + "() { }").encode())
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
                       "environment": http.POSTGRES_COMPOSITION,
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

    def extra_fixture(self, method):
        source = http.METHOD_SOURCES[method]
        (self.root / source).parent.mkdir(parents=True, exist_ok=True)
        (self.root / source).write_bytes(("public async Task " + method.rsplit(".", 1)[1] + "() { }").encode())
        record = copy.deepcopy(self.record)
        record.update(verifierMethod=method, sourcePath=source, sourceDigest=http.digest((self.root / source).read_bytes()),
                      environment=http.METHOD_ENVIRONMENTS[method], observations=[])
        self.inventory["endpoints"] = []
        for (verb, path), controls in http.EXTRA_RULES[method].items():
            self.inventory["endpoints"].append({"surfaceId": verb + path, "method": verb, "normalizedPath": path,
                                               "authorizationRequired": True, "kind": "CONTROLLER"})
            for control, (status, code, assertion) in controls.items():
                record["observations"].append({"method": verb, "path": path, "control": control,
                    "observedStatus": status, "expectedStatus": status, "errorCode": code, "responseAssertion": assertion,
                    "observedAtUtc": "2026-10-10T00:00:30Z"})
        self.inventory["endpointCount"] = len(self.inventory["endpoints"])
        return record, execution(method=method)

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
        source = http.METHOD_SOURCES[http.GANTT_COMMANDS]
        (self.root / source).write_bytes(("public async Task " + http.GANTT_COMMANDS.rsplit(".", 1)[1] + "() { }").encode())
        record.update(sourcePath=source, sourceDigest=http.digest((self.root / source).read_bytes()))
        self.inventory["endpoints"][0].update(method="PATCH", normalizedPath="/api/tasks/{taskItemId}/schedule")
        record["observations"].pop()
        for row in record["observations"]:
            row.update(method="PATCH", path="/api/tasks/{taskItemId}/schedule")
        record["observations"][1].update(control="ANONYMOUS_WITH_VALID_CSRF", errorCode="GANTT_CSRF_REQUIRED")
        trx = ET.fromstring(self.trx)
        definition = trx.find("t:TestDefinitions/t:UnitTest", http.NS)
        definition.attrib["name"] = http.GANTT_COMMANDS
        definition.find("t:TestMethod", http.NS).attrib["name"] = http.GANTT_COMMANDS.rsplit(".", 1)[1]
        trx.find("t:Results/t:UnitTestResult", http.NS).attrib["testName"] = http.GANTT_COMMANDS
        record["observations"][1]["errorCode"] = "GANTT_AUTHENTICATION_REQUIRED"
        self.assertTrue(all(row["accountingOutcome"] == "PASS" for row in self.account(record, trx=ET.tostring(trx))["endpoints"][0]["controls"]))
        record["observations"][1]["errorCode"] = "GANTT_CSRF_REQUIRED"
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

    def test_each_reviewed_fixture_has_explicit_scopes_and_separate_provider_accounting(self):
        for method in http.EXTRA_RULES:
            with self.subTest(method=method):
                record, trx = self.extra_fixture(method)
                result = self.account(record, trx)
                controls = [control for row in result["endpoints"] for control in row["controls"]]
                self.assertTrue(all(control["accountingOutcome"] == "PASS" for control in controls))
                self.assertTrue(all(control["environment"] == http.METHOD_ENVIRONMENTS[method] for control in controls))
                self.assertFalse(any(row["verifierMethod"] == method for row in result["unobservedScopedControls"]))
                if http.METHOD_ENVIRONMENTS[method] in {http.COOKIE_MEMORY, http.SYNTHETIC_MEMORY}:
                    self.assertTrue(all(dimension["observedEndpointCount"] == 0 for dimension in result["controlDimensions"].values()))
                    self.assertTrue(any(dimension["observedEndpointCount"] > 0
                                        for dimension in result["fixtureControlDimensions"][http.METHOD_ENVIRONMENTS[method]].values()))

    def test_fixture_provider_or_authentication_category_cannot_be_forged(self):
        record, trx = self.extra_fixture(http.NOTIFICATIONS)
        for environment in (http.POSTGRES_COMPOSITION, http.ENTRY_POINT, http.COOKIE_MEMORY, None):
            record["environment"] = environment
            with self.subTest(environment=environment), self.assertRaises(ValueError):
                self.account(record, trx)

    def test_empty_projection_requires_exact_body_assertion_and_same_operation_positive(self):
        record, trx = self.extra_fixture(http.MY_TASKS)
        projection = next(row for row in record["observations"] if row["control"] == "CURRENT_WORKSPACE_REVOKED_EMPTY_PAGE")
        projection["responseAssertion"] = None
        with self.assertRaises(ValueError):
            self.account(record, trx)
        projection["responseAssertion"] = "TOTAL_COUNT_ZERO_AND_ITEMS_EMPTY"
        record["observations"] = [row for row in record["observations"] if row["control"] != "AUTHORIZED_SAME_SCOPE"]
        result = self.account(record, trx)
        self.assertTrue(all(row["accountingOutcome"] == "UNVERIFIED" for endpoint in result["endpoints"] for row in endpoint["controls"]))

    def test_disabled_or_unrecorded_fixture_controls_stay_explicitly_unverified(self):
        record, trx = self.extra_fixture(http.KANBAN_CONFIG)
        record["observations"].pop()
        result = self.account(record, trx)
        self.assertTrue(any(row["verifierMethod"] == http.KANBAN_CONFIG and row["outcome"] == "UNVERIFIED"
                            for row in result["unobservedScopedControls"]))
        self.assertIn(http.KANBAN_MOVE, result["unrecordedVerifierMethods"])

    def test_current_fact_declaration_must_exist_exactly_once_even_with_updated_digest(self):
        source = self.root / self.record["sourcePath"]
        original = source.read_bytes()
        for body in (b"// source reference only", original + original):
            source.write_bytes(body)
            record = copy.deepcopy(self.record)
            record["sourceDigest"] = http.digest(body)
            with self.subTest(body=body), self.assertRaises(ValueError):
                self.account(record)

    def test_bounded_json_rejects_duplicate_nested_fields_nonfinite_and_nonobjects(self):
        path = self.root / "evidence.json"
        for data in (b'{"ownerApproval":null,"ownerApproval":true}', b'{"row":{"observedStatus":401,"observedStatus":200}}',
                     b'{"status":NaN}', b'[]', b'[' * 2000 + b']' * 2000):
            path.write_bytes(data)
            with self.subTest(data=data[:80]), self.assertRaises(ValueError):
                http.read_json(path)
        path.write_bytes(b'{"status":401}')
        self.assertEqual({"status": 401}, http.read_json(path))
        with self.assertRaises(ValueError):
            http.read_json(path, maximum=4)

    def test_unknown_observation_payload_or_assertion_is_rejected(self):
        for field, value in (("protectedRow", "must not be copied"), ("responseAssertion", "SELF_DECLARED_PASS"),
                             ("errorCode", "unexpected positive payload")):
            record = copy.deepcopy(self.record)
            record["observations"][0][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                self.account(record)

    def test_reviewed_cookie_and_resource_denials_reject_unrelated_error_causes(self):
        for method in (http.KANBAN_CONFIG, http.NOTIFICATIONS, http.EXECUTION_SCOPE, next(iter(http.COOKIE_METHODS))):
            record, trx = self.extra_fixture(method)
            negative = next(row for row in record["observations"] if row.get("errorCode") is not None)
            for field, value in (("errorCode", "ValidationFailed"), ("observedStatus", 400)):
                mutated = copy.deepcopy(record)
                index = record["observations"].index(negative)
                mutated["observations"][index][field] = value
                if field == "observedStatus":
                    mutated["observations"][index]["expectedStatus"] = value
                with self.subTest(method=method, field=field), self.assertRaises(ValueError):
                    self.account(mutated, trx)

    def test_anonymous_extra_scope_cannot_borrow_a_different_operation_positive(self):
        record, trx = self.extra_fixture(http.EXECUTION_SCOPE)
        record["observations"] = [row for row in record["observations"]
                                  if not (row["control"] == "AUTHORIZED_SAME_SCOPE" and row["method"] == "GET")]
        result = self.account(record, trx)
        anonymous = next(row for endpoint in result["endpoints"] for row in endpoint["controls"] if row["control"] == "ANONYMOUS")
        self.assertEqual("UNVERIFIED", anonymous["accountingOutcome"])

    def test_runtime_template_parameter_identity_cannot_be_silently_replaced(self):
        record, trx = self.extra_fixture(http.KANBAN_MOVE)
        for row in record["observations"]:
            row["path"] = row["path"].replace("{taskId}", "{taskItemId}")
        with self.assertRaises(ValueError):
            self.account(record, trx)


if __name__ == "__main__":
    unittest.main()
