"""Positive and deliberate-invalid controls for explicit SignalR assertion accounting."""

import copy
from datetime import datetime, timezone
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

import sec_arch_signalr_accounting as signalr
from sec_arch_assembly_binding import assembly_path, loaded_assembly_path, LEGACY_ASSEMBLIES, SIX_ASSEMBLY_SCOPE
from test_sec_arch_http_accounting import execution

NOW = datetime(2026, 10, 10, 1, tzinfo=timezone.utc)


class SignalRAccountingTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.method = signalr.HIDDEN
        source = signalr.METHOD_SOURCES[self.method]
        (self.root / source).parent.mkdir(parents=True)
        (self.root / source).write_bytes(("public async Task " + self.method.rsplit(".", 1)[1] + "() { }").encode())
        assemblies = {}
        for name in signalr.ASSEMBLIES:
            path = assembly_path(self.root, name)
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(name.encode())
            copied = loaded_assembly_path(self.root, name)
            copied.parent.mkdir(parents=True, exist_ok=True)
            copied.write_bytes(name.encode())
            assemblies[name] = signalr.digest(path.read_bytes())
        self.inventory = {"schemaVersion": 1, "catalogScope": "ACTUAL_COMPOSED_TEST_HOST", "webAssemblyDigest": assemblies["Coglatas.Web"],
            "realtime": {"events": [{"eventType": name} for name in signalr.EVENT_TARGETS],
                "subscriptionTypes": ["User", "Tenant", "Workspace", "Project", "Conversation"],
                "methods": [{"name": name} for name in ("SubscribeUser", "SubscribeTenant", "SubscribeWorkspace", "SubscribeProject",
                    "SubscribeConversation", "UnsubscribeWorkspace", "UnsubscribeProject", "UnsubscribeConversation")]}}
        self.record = {"schemaVersion": 2, "assemblyBindingScope": SIX_ASSEMBLY_SCOPE, "verifierMethod": self.method, "sourcePath": source,
            "sourceDigest": signalr.digest((self.root / source).read_bytes()), "environment": signalr.ENVIRONMENT,
            "assemblyDigests": assemblies, "ownerApproval": None, "contractCompletion": "UNVERIFIED", "observations":
                [{"eventType": event, "subscriptionType": target, "control": control, "positiveDelivery": "OBSERVED",
                  "excludedDelivery": "NOT_OBSERVED", "observedAtUtc": "2026-10-10T00:00:30Z"}
                 for event, target, control in sorted(signalr.RULES[self.method])]}
        self.trx = execution(method=self.method)
        self.receipt = {"schemaVersion": 2, "verifierId": "SEC-ARCH-EXECUTION-COVERAGE", "verifierVersion": "2",
            "assemblyBindingScope": SIX_ASSEMBLY_SCOPE,
            "candidateSha": "a" * 40, "environmentFingerprint": "b" * 64, "runId": "17", "runAttempt": "2",
            "buildStampMatchesCandidate": True, "executionDigest": signalr.digest(self.trx), "assemblyDigests": assemblies}
        self.identity = ("a" * 40, "b" * 64, "17", "2")

    def account(self, record=None, trx=None, receipt=None, identity=None):
        return signalr.account(self.root, self.inventory, [record if record is not None else self.record],
                               trx if trx is not None else self.trx, NOW, receipt, identity)

    def test_positive_receipts_preserve_pending_full_coverage_and_approval(self):
        result = self.account()
        self.assertEqual(3, result["observedControlCount"])
        self.assertEqual(15, result["eventTypeCount"])
        self.assertEqual(17, result["reviewedRouteCount"])
        self.assertTrue(all(row["accountingOutcome"] == "PASS" for event in result["events"] for row in event["controls"]))
        self.assertEqual("UNVERIFIED", result["assertionCoverageOutcome"])
        self.assertEqual("UNVERIFIED", result["candidateBinding"])
        self.assertEqual("SIX_ASSEMBLY_LOCAL_BYTES_RECONCILED", result["fullDependencyQualification"])
        self.assertEqual("UNVERIFIED", result["tenantRoutingApplicability"])
        self.assertIsNone(result["ownerApproval"])
        self.assertTrue(all(row["specIds"] == [] and row["fullContractCoverage"] == "UNVERIFIED" for row in result["events"]))
        self.assertEqual("PRE-AVALONIA SEC-ARCH: BLOCKED", result["preAvaloniaVerdict"])

    def test_names_without_recorded_assertions_receive_no_credit(self):
        record = copy.deepcopy(self.record)
        record["observations"] = []
        result = self.account(record)
        self.assertEqual(0, result["observedControlCount"])
        self.assertEqual(sum(len(scope) for scope in signalr.RULES.values()), len(result["unobservedScopedControls"]))

    def test_complete_reviewed_assertion_scopes_do_not_create_acceptance(self):
        q = "{" + signalr.NS["t"] + "}"
        trx = ET.Element(q + "TestRun")
        ET.SubElement(trx, q + "Times", start="2026-10-10T00:00:00Z", finish="2026-10-10T00:01:00Z")
        definitions = ET.SubElement(trx, q + "TestDefinitions")
        results = ET.SubElement(trx, q + "Results")
        files = {}
        for method, source in signalr.METHOD_SOURCES.items():
            files.setdefault(source, []).append("public async Task " + method.rsplit(".", 1)[1] + "() { }")
        for source, declarations in files.items():
            (self.root / source).write_bytes("\n".join(declarations).encode())
        records = []
        for index, (method, source) in enumerate(signalr.METHOD_SOURCES.items()):
            record = copy.deepcopy(self.record)
            record.update(verifierMethod=method, sourcePath=source, sourceDigest=signalr.digest((self.root / source).read_bytes()),
                hubInvocations=[{"hubMethod": name, "allowed": allowed, "code": code, "observedAtUtc": "2026-10-10T00:00:30Z"}
                    for name, allowed, code in sorted(signalr.HUB_RULES[method])],
                observations=[{"eventType": event, "subscriptionType": target, "control": control, "positiveDelivery": "OBSERVED",
                    "excludedDelivery": None if control in signalr.POSITIVE_ONLY else "NOT_OBSERVED",
                    "observedAtUtc": "2026-10-10T00:00:30Z"} for event, target, control in sorted(signalr.RULES[method])])
            records.append(record)
            one = ET.fromstring(execution(method=method))
            definition = one.find("t:TestDefinitions/t:UnitTest", signalr.NS)
            definition.attrib["id"] = str(index)
            definition.find("t:Execution", signalr.NS).attrib["id"] = str(index)
            result = one.find("t:Results/t:UnitTestResult", signalr.NS)
            result.attrib.update(testId=str(index), executionId=str(index))
            definitions.append(definition)
            results.append(result)
        ET.SubElement(ET.SubElement(trx, q + "ResultSummary"), q + "Counters", total="8", passed="8", failed="0", executed="8")
        result = signalr.account(self.root, self.inventory, records, ET.tostring(trx), NOW)
        self.assertEqual(298, result["observedControlCount"])
        self.assertEqual("PASS", result["assertionCoverageOutcome"])
        self.assertEqual([], result["unobservedScopedControls"])
        self.assertEqual(0, result["hubInvocationReceiptOutstandingMethodCount"])
        self.assertEqual([], result["unobservedInvocationAssertions"])
        self.assertEqual("PRE-AVALONIA SEC-ARCH: BLOCKED", result["preAvaloniaVerdict"])

    def test_exact_candidate_binding_does_not_create_trusted_attestation(self):
        result = self.account(receipt=self.receipt, identity=self.identity)
        self.assertEqual("EXACT_RECEIPT_RECONCILED_TRUSTED_ATTESTATION_PENDING", result["candidateBinding"])
        self.assertEqual("UNVERIFIED", result["trustedAttestation"])

    def test_failed_or_skipped_execution_cannot_qualify(self):
        for outcome in ("Failed", "NotExecuted"):
            with self.subTest(outcome=outcome):
                result = self.account(trx=execution(outcome, method=self.method))
                self.assertTrue(all(row["accountingOutcome"] == "UNVERIFIED" for event in result["events"] for row in event["controls"]))

    def test_missing_positive_or_unexpected_delivery_cannot_qualify(self):
        for field, value in (("positiveDelivery", "UNVERIFIED"), ("excludedDelivery", "OBSERVED")):
            record = copy.deepcopy(self.record)
            record["observations"][0][field] = value
            with self.assertRaises(ValueError):
                self.account(record)

    def test_unreviewed_metadata_session_denial_is_rejected(self):
        record = copy.deepcopy(self.record)
        record["observations"][0].update(eventType=signalr.INVALIDATION, subscriptionType="User", control="CURRENT_SESSION_REVOCATION")
        with self.assertRaises(ValueError):
            self.account(record)

    def test_changed_inventory_or_tenant_route_is_rejected(self):
        self.inventory["realtime"]["events"].pop()
        with self.assertRaises(ValueError):
            self.account()
        self.inventory["realtime"]["events"] = [{"eventType": name} for name in signalr.EVENT_TARGETS]
        record = copy.deepcopy(self.record)
        record["observations"][0]["subscriptionType"] = "Tenant"
        with self.assertRaises(ValueError):
            self.account(record)

    def test_duplicate_control_or_verifier_is_rejected(self):
        record = copy.deepcopy(self.record)
        record["observations"].append(copy.deepcopy(record["observations"][0]))
        with self.assertRaises(ValueError):
            self.account(record)
        with self.assertRaises(ValueError):
            signalr.account(self.root, self.inventory, [self.record, self.record], self.trx, NOW)

    def test_changed_deleted_or_duplicate_declaration_is_rejected(self):
        source = self.root / self.record["sourcePath"]
        for content in (b"renamed", b"", source.read_bytes() * 2):
            source.write_bytes(content)
            record = copy.deepcopy(self.record)
            record["sourceDigest"] = signalr.digest(content)
            with self.assertRaises(ValueError):
                self.account(record)

    def test_source_or_assembly_digest_mismatch_is_rejected(self):
        for field in ("sourceDigest", "assemblyDigests"):
            record = copy.deepcopy(self.record)
            if field == "sourceDigest":
                record[field] = "0" * 64
            else:
                record[field]["Coglatas.Tests"] = "0" * 64
            with self.assertRaises(ValueError):
                self.account(record)

    def test_observation_outside_test_execution_is_rejected(self):
        record = copy.deepcopy(self.record)
        record["observations"][0]["observedAtUtc"] = "2026-10-10T00:02:00Z"
        with self.assertRaises(ValueError):
            self.account(record)

    def test_unsafe_payload_or_forged_approval_is_rejected(self):
        for where, field, value in (("record", "ownerApproval", "APPROVED"), ("record", "approval", "APPROVED"),
                                    ("record", "contractCompletion", "PASS"), ("row", "payload", "private")):
            record = copy.deepcopy(self.record)
            (record if where == "record" else record["observations"][0])[field] = value
            with self.assertRaises(ValueError):
                self.account(record)

    def test_wrong_candidate_attempt_and_execution_artifact_are_rejected(self):
        for field, value in (("candidateSha", "c" * 40), ("runAttempt", "3"), ("executionDigest", "d" * 64)):
            receipt = copy.deepcopy(self.receipt)
            receipt[field] = value
            with self.assertRaises(ValueError):
                self.account(receipt=receipt, identity=self.identity)

    def test_invocation_name_without_actual_result_and_execution_receives_no_credit(self):
        result = self.account()
        self.assertEqual(8, result["hubInvocationReceiptOutstandingMethodCount"])
        record = copy.deepcopy(self.record)
        record["hubInvocations"] = [{"hubMethod": "SubscribeWorkspace", "allowed": True, "code": "Subscribed",
                                     "observedAtUtc": "2026-10-10T00:00:30Z"}]
        result = self.account(record, trx=execution("Failed", method=self.method))
        self.assertEqual(8, result["hubInvocationReceiptOutstandingMethodCount"])

    def test_wrong_or_duplicate_invocation_result_is_rejected(self):
        row = {"hubMethod": "SubscribeWorkspace", "allowed": True, "code": "Subscribed", "observedAtUtc": "2026-10-10T00:00:30Z"}
        for rows in ([dict(row, allowed="true")], [dict(row, code="RateLimited")], [row, row], [dict(row, resourceId="private")]):
            record = copy.deepcopy(self.record)
            record["hubInvocations"] = rows
            with self.assertRaises(ValueError):
                self.account(record)

    def test_historical_five_assembly_receipt_retains_only_scoped_evidence(self):
        record = copy.deepcopy(self.record)
        record["schemaVersion"] = 1
        record.pop("assemblyBindingScope")
        record["assemblyDigests"] = {name: record["assemblyDigests"][name] for name in LEGACY_ASSEMBLIES}
        result = self.account(record)
        self.assertEqual(3, result["observedControlCount"])
        self.assertEqual([1], result["inputReceiptSchemaVersions"])
        self.assertEqual("UNVERIFIED", result["fullDependencyQualification"])
        self.assertEqual("PRE-AVALONIA SEC-ARCH: BLOCKED", result["preAvaloniaVerdict"])

    def test_changed_tool_or_loaded_copy_is_rejected(self):
        for name, loaded in (("Coglatas.SecurityArchitecture", False), ("Coglatas.SecurityArchitecture", True), ("Coglatas.Web", True)):
            path = loaded_assembly_path(self.root, name) if loaded else assembly_path(self.root, name)
            original = path.read_bytes()
            path.write_bytes(b"different bytes")
            with self.assertRaises(ValueError):
                self.account()
            path.write_bytes(original)

    def test_version_scope_parity_and_missing_tool_are_rejected(self):
        for changes in ({"schemaVersion": True}, {"schemaVersion": 1}, {"assemblyBindingScope": "HISTORICAL_FIVE_ASSEMBLIES"}):
            record = copy.deepcopy(self.record)
            record.update(changes)
            with self.assertRaises(ValueError):
                self.account(record)
        record = copy.deepcopy(self.record)
        record["assemblyDigests"].pop("Coglatas.SecurityArchitecture")
        with self.assertRaises(ValueError):
            self.account(record)

    def test_execution_receipt_cannot_upgrade_or_mismatch_dependency_scope(self):
        receipt = copy.deepcopy(self.receipt)
        receipt.update(schemaVersion=1, verifierVersion="1")
        receipt.pop("assemblyBindingScope")
        receipt["assemblyDigests"] = {name: receipt["assemblyDigests"][name] for name in LEGACY_ASSEMBLIES}
        with self.assertRaises(ValueError):
            self.account(receipt=receipt, identity=self.identity)

    def test_execution_tool_dependency_must_match_local_producer_and_loaded_bytes(self):
        receipt = copy.deepcopy(self.receipt)
        receipt["assemblyDigests"]["Coglatas.SecurityArchitecture"] = "0" * 64
        with self.assertRaises(ValueError):
            self.account(receipt=receipt, identity=self.identity)


if __name__ == "__main__":
    unittest.main()
