"""Actual clean-source binding and invalid controls for isolated manual replay accounting."""

import copy
from pathlib import Path
import shutil
import subprocess
import unittest

import sec_arch_signalr_accounting as signalr
from sec_arch_manual_replay import STAGES
from sec_arch_assembly_binding import SIX_ASSEMBLY_SCOPE
from test_sec_arch_http_accounting import execution
import test_sec_arch_signalr_accounting as signalr_fixture


@unittest.skipUnless(shutil.which("git"), "Actual clean-candidate manual replay controls require Git.")
class ManualReplayAccountingTests(unittest.TestCase):
    def setUp(self):
        self.fixture = signalr_fixture.SignalRAccountingTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.root, self.inventory = self.fixture.root, self.fixture.inventory
        self.method = signalr.MANUAL_REPLAY
        source = signalr.METHOD_SOURCES[self.method]
        (self.root / source).write_bytes(("public async Task " + self.method.rsplit(".", 1)[1] + "() { }").encode())
        (self.root / ".gitignore").write_text("**/bin/\n", encoding="utf-8")
        for args in (("init",), ("config", "user.name", "Synthetic manual receipt"),
                     ("config", "user.email", "manual@example.invalid"), ("add", "."),
                     ("commit", "-m", "Synthetic manual source binding")):
            subprocess.run(["git", "-C", str(self.root), *args], capture_output=True, text=True, check=True, timeout=20)
        sha = subprocess.check_output(["git", "-C", str(self.root), "rev-parse", "HEAD"], text=True).strip()
        self.record = copy.deepcopy(self.fixture.record)
        self.record.update(verifierMethod=self.method, sourcePath=source,
            sourceDigest=signalr.digest((self.root / source).read_bytes()), hubInvocations=[], originBoundaries=[],
            observations=[{"eventType": event, "subscriptionType": target, "control": control,
                "positiveDelivery": "OBSERVED", "excludedDelivery": None if control in signalr.POSITIVE_ONLY else "NOT_OBSERVED",
                "observedAtUtc": "2026-10-10T00:00:30Z"} for event, target, control in sorted(signalr.RULES[self.method])])
        self.trx = execution(method=self.method)
        self.receipt = copy.deepcopy(self.fixture.receipt)
        self.receipt.update(candidateSha=sha, executionDigest=signalr.digest(self.trx))
        self.identity = (sha, "b" * 64, "17", "2")
        self.native = {"schemaVersion": 2, "approvalStatus": "DRAFT", "ownerApproval": None,
            "candidateSha": sha, "assemblyBindingScope": SIX_ASSEMBLY_SCOPE,
            "assemblyDigests": copy.deepcopy(self.record["assemblyDigests"]), "verifierMethod": self.method,
            "sourcePath": source, "sourceDigest": self.record["sourceDigest"], "eventType": "Projects.ProjectChanged.v1",
            "payloadSchemaVersion": 1, "executionScope": "ACTUAL_APPLICATION_REPLAY_TO_PRODUCT_POSTGRES_OUTBOX_AND_REAL_WEBSOCKET",
            "replayActorAuthority": "SUPPLIED_TEST_ACTOR_WITH_CURRENT_PERSISTED_AUTHORITY_CHECKS",
            "recipientAuthentication": "ACTUAL_PRODUCT_PASSWORD_COOKIE_SESSION", "durableEnvelopeProducer": "SYNTHETIC_TEST_ENQUEUE",
            "businessProducerCoverage": "UNVERIFIED", "authenticatedHttpReplayAdapter": "UNVERIFIED", "operationalCliReplayAdapter": "UNVERIFIED",
            "operatorIssuanceAuthority": "UNVERIFIED", "productRlsAppliedCount": 0, "replayDatabaseRole": "synthetic_migration",
            "databaseVersion": "18.6", "replayDatabaseRoleIsSuperuser": True, "replayDatabaseRoleHasBypassRls": True,
            "operationalDatabaseIdentity": "UNVERIFIED", "originalPositiveDeliveryCount": 1, "firstManualReplayDeliveryCount": 2,
            "deniedManualReplayAdditionalDeliveryCount": 0, "restoredManualReplayDeliveryCount": 3, "verifiedPositiveRecipientCount": 2,
            "verifiedCrossTenantPeerCount": 1, "verifiedRevocationSentinelRecipientCount": 3, "finalReplayAuditCount": 2,
            "originalIdentityPayloadRoutingPreserved": True, "deniedEventStatePreserved": True, "deniedReplayAuditPreserved": True,
            "immutableDigest": "a" * 64, "deniedEventStateDigest": "b" * 64, "deniedReplayAuditDigest": "c" * 64,
            "snapshotDigests": {stage: {"immutableDigest": "a" * 64,
                "eventStateDigest": ("d" if stage == "baseline" else "e" if stage == "restoredReplay" else "b") * 64,
                "replayAuditDigest": ("f" if stage == "baseline" else "1" if stage == "restoredReplay" else "c") * 64,
                "replayAuditCount": count} for stage, count in zip(STAGES, (0, 1, 1, 1, 2))},
            "recordedAtUtc": "2026-10-10T00:00:45Z", "runtimeOutcome": "PASS", "normativeContractCompletion": "UNVERIFIED",
            "preAvaloniaVerdict": "BLOCKED"}

    def account(self, native=None, record=None, trx=None, receipt=None, identity=None):
        return signalr.account(self.root, self.inventory, [record if record is not None else self.record],
            trx if trx is not None else self.trx, signalr_fixture.NOW, receipt if receipt is not None else self.receipt,
            identity if identity is not None else self.identity, native if native is not None else self.native)

    def test_bound_positive_qualifies_only_application_seam_and_retains_every_operational_hold(self):
        result = self.account()
        self.assertEqual(0, result["manualReplayToTransportOutstandingAdapterCount"])
        self.assertEqual("PASS", result["manualReplayToTransport"]["applicationToTransportOutcome"])
        self.assertEqual(5, result["manualReplayToTransport"]["snapshotStageCount"])
        self.assertEqual(3, result["observedControlCount"])
        self.assertEqual(0, result["observedHubInvocationCount"])
        self.assertEqual(8, result["hubInvocationReceiptOutstandingMethodCount"])
        self.assertEqual([], result["observedBusinessProducerEventTypes"])
        for key in ("httpReplayAdapter", "operationalCliReplayAdapter", "operatorIssuanceAuthority",
                    "operationalDatabaseIdentity", "productRls", "normativeContractCompletion"):
            self.assertEqual("UNVERIFIED", result["manualReplayToTransport"][key])
        self.assertEqual("UNVERIFIED", result["trustedAttestation"])
        self.assertEqual("PRE-AVALONIA SEC-ARCH: BLOCKED", result["preAvaloniaVerdict"])

    def test_transport_names_without_separate_native_execution_receive_no_seam_credit(self):
        result = signalr.account(self.root, self.inventory, [self.record], self.trx, signalr_fixture.NOW)
        self.assertEqual(3, result["observedControlCount"])
        self.assertEqual(1, result["manualReplayToTransportOutstandingAdapterCount"])
        with self.assertRaises(ValueError):
            signalr.account(self.root, self.inventory, [self.record], self.trx, signalr_fixture.NOW, manual_replay_receipt=self.native)

    def test_failed_skipped_or_missing_delivery_control_receives_no_manual_seam_credit(self):
        for outcome in ("Failed", "NotExecuted"):
            trx = execution(outcome, method=self.method)
            receipt = dict(self.receipt, executionDigest=signalr.digest(trx))
            result = self.account(trx=trx, receipt=receipt)
            self.assertEqual(1, result["manualReplayToTransportOutstandingAdapterCount"])
        record = copy.deepcopy(self.record)
        record["observations"].pop()
        self.assertEqual(1, self.account(record=record)["manualReplayToTransportOutstandingAdapterCount"])

    def test_zero_or_boolean_positive_denial_sentinel_restore_or_audit_counts_are_rejected(self):
        for key in ("originalPositiveDeliveryCount", "firstManualReplayDeliveryCount", "deniedManualReplayAdditionalDeliveryCount",
                    "restoredManualReplayDeliveryCount", "verifiedPositiveRecipientCount", "verifiedCrossTenantPeerCount",
                    "verifiedRevocationSentinelRecipientCount", "finalReplayAuditCount", "productRlsAppliedCount"):
            for value in (True, -1, 99):
                native = copy.deepcopy(self.native)
                native[key] = value
                with self.subTest(key=key, value=value), self.assertRaises(ValueError): self.account(native=native)

    def test_preservation_booleans_cannot_substitute_for_changed_or_missing_stage_digests(self):
        mutations = [("afterDeniedReplay", "eventStateDigest", "2" * 64),
                     ("afterDeniedReplay", "replayAuditDigest", "2" * 64),
                     ("restoredReplay", "immutableDigest", "2" * 64),
                     ("restoredReplay", "replayAuditCount", True),
                     ("firstReplay", "eventStateDigest", "d" * 64)]
        for stage, field, value in mutations:
            native = copy.deepcopy(self.native)
            native["snapshotDigests"][stage][field] = value
            with self.subTest(stage=stage, field=field), self.assertRaises(ValueError): self.account(native=native)
        native = copy.deepcopy(self.native)
        native["snapshotDigests"].pop("baseline")
        with self.assertRaises(ValueError): self.account(native=native)

    def test_forged_approval_operator_identity_adapter_rls_or_producer_expansion_is_rejected(self):
        for key, value in (("ownerApproval", "APPROVED"), ("approvalStatus", "APPROVED"),
                ("replayActorAuthority", "ACTUAL_AUTHENTICATED_OPERATOR"), ("durableEnvelopeProducer", "ACTUAL_PRODUCT_PRODUCER"),
                ("normativeContractCompletion", "PASS"), ("authenticatedHttpReplayAdapter", "PASS"),
                ("operationalCliReplayAdapter", "PASS"), ("operatorIssuanceAuthority", "PASS"),
                ("operationalDatabaseIdentity", "PASS"), ("productRlsAppliedCount", 1),
                ("businessProducerCoverage", "PASS"), ("preAvaloniaVerdict", "PASS")):
            native = copy.deepcopy(self.native)
            native[key] = value
            with self.subTest(key=key), self.assertRaises(ValueError): self.account(native=native)

    def test_wrong_candidate_source_assembly_attempt_and_outside_timestamp_are_rejected(self):
        for key, value in (("candidateSha", None), ("candidateSha", "c" * 40), ("sourceDigest", "c" * 64),
                ("sourcePath", "other.cs"), ("recordedAtUtc", "2026-10-10T00:02:00Z"),
                ("schemaVersion", True), ("schemaVersion", 1), ("payloadSchemaVersion", True), ("replayDatabaseRoleIsSuperuser", "true"),
                ("replayDatabaseRole", "unbounded role;"), ("databaseVersion", "18.6 private")):
            native = copy.deepcopy(self.native)
            native[key] = value
            with self.subTest(key=key), self.assertRaises(ValueError): self.account(native=native)
        native = copy.deepcopy(self.native)
        native["assemblyDigests"]["Coglatas.SecurityArchitecture"] = "c" * 64
        with self.assertRaises(ValueError): self.account(native=native)
        with self.assertRaises(ValueError): self.account(receipt=dict(self.receipt, runAttempt="3"))

    def test_unsafe_fields_or_typed_hub_credit_without_recorded_result_are_rejected(self):
        native = copy.deepcopy(self.native)
        native["payload"] = "private"
        with self.assertRaises(ValueError): self.account(native=native)
        native = copy.deepcopy(self.native)
        native["snapshotDigests"]["baseline"]["eventId"] = "private"
        with self.assertRaises(ValueError): self.account(native=native)
        record = copy.deepcopy(self.record)
        record["hubInvocations"] = [{"hubMethod": "SubscribeProject", "allowed": True, "code": "Subscribed",
                                     "observedAtUtc": "2026-10-10T00:00:30Z"}]
        with self.assertRaises(ValueError): self.account(record=record)

    def test_dirty_source_with_consistent_self_declared_hashes_still_fails_exact_git_binding(self):
        source = self.root / self.record["sourcePath"]
        source.write_bytes(source.read_bytes() + b"\n// Synthetic source change.\n")
        record, native = copy.deepcopy(self.record), copy.deepcopy(self.native)
        record["sourceDigest"] = native["sourceDigest"] = signalr.digest(source.read_bytes())
        with self.assertRaises(ValueError): self.account(native=native, record=record)


if __name__ == "__main__":
    unittest.main()
