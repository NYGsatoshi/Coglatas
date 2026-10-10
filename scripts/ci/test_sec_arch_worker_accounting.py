"""Positive and deliberate-invalid registered-worker accounting controls."""

import copy
from datetime import datetime, timezone
import unittest

import sec_arch_signalr_accounting as signalr
import sec_arch_worker_accounting as worker
from test_sec_arch_http_accounting import execution
import test_sec_arch_signalr_accounting as signalr_fixtures

NOW = datetime(2026, 10, 10, 1, tzinfo=timezone.utc)


class WorkerAccountingTests(unittest.TestCase):
    def setUp(self):
        self.fixture = signalr_fixtures.SignalRAccountingTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.root, self.inventory = self.fixture.root, self.fixture.inventory
        self.inventory["serviceTopology"] = {"hostedServices": [{"type": worker.ANNOUNCEMENT}, {"type": worker.DIGEST}]}

    def records(self, method=signalr.ANNOUNCEMENT_WORKER):
        peer = copy.deepcopy(self.fixture.record)
        peer["limits"] = []
        source = signalr.METHOD_SOURCES[method]
        content = ("public async Task " + method.rsplit(".", 1)[1] + "() { }").encode()
        (self.root / source).write_bytes(content)
        peer.update(verifierMethod=method, sourcePath=source, sourceDigest=signalr.digest(content),
            observations=[{"eventType": event, "subscriptionType": target, "control": control,
                "positiveDelivery": "OBSERVED", "excludedDelivery": None if control in signalr.POSITIVE_ONLY else "NOT_OBSERVED",
                "observedAtUtc": "2026-10-10T00:00:30Z"} for event, target, control in sorted(signalr.RULES[method])],
            hubInvocations=[{"hubMethod": "SubscribeUser", "allowed": True, "code": "Subscribed", "observedAtUtc": "2026-10-10T00:00:02Z"}],
            originBoundaries=[])
        record = {key: copy.deepcopy(peer[key]) for key in ("schemaVersion", "assemblyBindingScope", "verifierMethod", "sourcePath", "sourceDigest", "assemblyDigests", "ownerApproval", "contractCompletion", "limits")}
        record.update(environment=worker.ENVIRONMENT, workerPollSeconds=1,
            observations=[{"workerType": rule[0], "control": control, "assertion": rule[1], "outcome": "OBSERVED",
                "observedAtUtc": "2026-10-10T00:00:31Z"} for control, rule in sorted(worker.METHOD_RULES[method].items())])
        return record, peer, execution(method=method)

    def account(self, record, peer, trx):
        return worker.account(self.root, self.inventory, [record], [peer], trx, NOW)

    def test_both_reviewed_worker_paths_require_actual_typed_frames_and_remain_scoped(self):
        for method, count in ((signalr.ANNOUNCEMENT_WORKER, 4), (signalr.DIGEST_WORKER, 3)):
            with self.subTest(method=method):
                result = self.account(*self.records(method))
                self.assertEqual(count, result["observedControlCount"])
                self.assertTrue(all(row["accountingOutcome"] == "PASS" for row in result["controls"]))
                self.assertEqual("UNVERIFIED", result["assertionCoverageOutcome"])
                self.assertEqual("UNVERIFIED", result["candidateBinding"])
                self.assertEqual("UNVERIFIED", result["workerRoleOutcome"])
                self.assertEqual("NOT_EVALUATED", result["performanceQualification"])
                self.assertEqual("PRE-AVALONIA SEC-ARCH: BLOCKED", result["preAvaloniaVerdict"])

    def test_false_approval_cadence_environment_or_completion_is_rejected(self):
        for field, value in (("ownerApproval", "approved"), ("workerPollSeconds", True), ("workerPollSeconds", 30),
                             ("environment", "PRODUCTION"), ("contractCompletion", "PASS")):
            record, peer, trx = self.records()
            record[field] = value
            with self.subTest(field=field, value=value), self.assertRaises(ValueError):
                self.account(record, peer, trx)

    def test_wrong_worker_or_status_only_assertion_cannot_qualify(self):
        for field, value in (("workerType", "SyntheticWorker"), ("assertion", "STATUS_ONLY"), ("outcome", "PASS")):
            record, peer, trx = self.records()
            record["observations"][0][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                self.account(record, peer, trx)

    def test_receipt_needs_the_same_actual_source_and_build_as_transport(self):
        for field, value in (("sourcePath", "other.cs"), ("sourceDigest", "a" * 64), ("assemblyBindingScope", "FIVE_ASSEMBLIES")):
            record, peer, trx = self.records()
            record[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                self.account(record, peer, trx)

    def test_missing_changed_or_duplicate_registered_worker_is_rejected(self):
        for values in ([], [{"type": "SyntheticWorker"}], [{"type": worker.ANNOUNCEMENT}] * 2):
            record, peer, trx = self.records()
            self.inventory["serviceTopology"]["hostedServices"] = values
            with self.assertRaises(ValueError):
                self.account(record, peer, trx)

    def test_missing_positive_or_no_typed_transport_peer_is_rejected(self):
        for scenario in ("positive", "typed", "ordered"):
            record, peer, trx = self.records()
            if scenario == "positive":
                record["observations"].pop(0)
            elif scenario == "typed":
                peer["observations"] = [row for row in peer["observations"] if row["control"] != "WORKER_ANNOUNCEMENT_ORIGINAL_PRODUCER"]
            else:
                record["observations"][0], record["observations"][1] = record["observations"][1], record["observations"][0]
            with self.subTest(scenario=scenario), self.assertRaises(ValueError):
                self.account(record, peer, trx)

    def test_outside_trx_and_future_positive_peer_is_rejected(self):
        for scenario in ("interval", "future"):
            record, peer, trx = self.records()
            if scenario == "interval":
                record["observations"][0]["observedAtUtc"] = "2026-10-10T00:02:00Z"
            else:
                for row in peer["observations"]:
                    row["observedAtUtc"] = "2026-10-10T00:00:32Z"
            with self.subTest(scenario=scenario), self.assertRaises(ValueError):
                self.account(record, peer, trx)

    def test_protected_identity_and_duplicate_fields_are_rejected(self):
        for scenario in ("identity", "duplicate"):
            record, peer, trx = self.records()
            if scenario == "identity":
                record["observations"][0]["jobId"] = "protected"
            else:
                record["observations"].append(copy.deepcopy(record["observations"][0]))
            with self.assertRaises(ValueError):
                self.account(record, peer, trx)

    def test_failed_or_skipped_method_retains_unverified_controls(self):
        for outcome in ("Failed", "NotExecuted"):
            record, peer, _ = self.records()
            result = self.account(record, peer, execution(outcome, method=record["verifierMethod"]))
            self.assertTrue(all(row["accountingOutcome"] == "UNVERIFIED" for row in result["controls"]))

    def test_missing_control_remains_unverified_and_cannot_shrink_scope(self):
        record, peer, trx = self.records()
        record["observations"].pop()
        result = self.account(record, peer, trx)
        self.assertEqual("UNVERIFIED", result["assertionCoverageOutcome"])
        self.assertTrue(any(row["control"] == "RESTORED_AUDIENCE_PUBLICATION" for row in result["unobservedControls"]))

    def test_duplicate_verifier_or_wrong_transport_companion_is_rejected(self):
        record, peer, trx = self.records()
        with self.assertRaises(ValueError):
            worker.account(self.root, self.inventory, [record, record], [peer], trx, NOW)
        with self.assertRaises(ValueError):
            worker.account(self.root, self.inventory, [record], [], trx, NOW)


if __name__ == "__main__":
    unittest.main()
