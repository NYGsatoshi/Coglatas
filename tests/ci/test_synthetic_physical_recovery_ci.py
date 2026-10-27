import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("physical_ci", ROOT / "scripts/ci/run-synthetic-physical-recovery-ci.py")
ci = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ci)


def fixture():
    expected = {"sourceSha": "a" * 40, "sourceTree": "b" * 40, "sdkVersion": "10.0.403",
                "probeSha256": "c" * 64, "ddlSha256": "d" * 64, "postgresImageId": "sha256:" + "e" * 64,
                "assemblies": {name: str(index) * 64 for index, name in enumerate(ci.ASSEMBLIES)},
                "guardSourceSha256": "f" * 64, "fileTriggerSourceSha256": "1" * 64}
    prefix = "coglatas-synthetic-recovery-123456789abc"
    file = {"status": "PASS", "bytes": 36, "sha256": "2" * 64}
    guard = {"revisionHeadNonzeroInsert": "DENIED_23514_WITH_ZERO_POSITIVE",
             "guardSourceSha256": expected["guardSourceSha256"],
             "fileVersionArchiveCopyAfterCapture": "DENIED_23505_WITH_CAPTURE_POSITIVE",
             "fileTriggerSourceSha256": expected["fileTriggerSourceSha256"]}
    receipt = {"schema": "coglatas-synthetic-physical-recovery-v1", "sourceSha": expected["sourceSha"],
               "probeSha256": expected["probeSha256"], "ddlSha256": expected["ddlSha256"],
               "postgresImageId": expected["postgresImageId"],
               "resources": {"containers": [prefix + "-source", prefix + "-target"],
                             "volumes": [prefix + "-source", prefix + "-backup", prefix + "-target"]},
               "status": "SYNTHETIC_DATABASE_CANDIDATE_PASS", "formalStrategyApproved": False, "publicGo": False,
               "fullApplicationRecovery": "NOT_EXECUTED", "targetHttps": "NOT_EXECUTED",
               "rollbackRehearsal": "NOT_EXECUTED", "startedAtUtc": "2026-10-27T00:00:00Z",
               "finishedAtUtc": "2026-10-27T00:01:00Z",
               "applicationBuild": {"sourceSha": expected["sourceSha"], "sourceTree": expected["sourceTree"],
                   "buildExitCode": 0, "sdkVersion": expected["sdkVersion"], "buildLogSha256": "3" * 64,
                   "generatedDdlSha256": expected["ddlSha256"], "assemblies": expected["assemblies"].copy()},
               "postgresServerVersion": "18.6", "triggerControls": guard,
               "sourceLoadedAssemblies": expected["assemblies"].copy(),
               "sourceFile": file, "webDllSha256": expected["assemblies"]["Coglatas.Web.dll"],
               "basebackupSeconds": 1.5, "backupManifestVerification": "PASS",
               "backupManifestSha256Before": "4" * 64,
               "comparisons": {name: {"sourceSha256": "5" * 64, "restoredSha256": "5" * 64,
                                     "exactBytesEqual": True} for name in ci.COMPARISONS},
               "oracleNegativeControls": {"schemaAndRawCatalogChange": "DETECTED", "rowChange": "DETECTED",
                   "sequenceChange": "DETECTED", "exactOriginalStateRestored": "PASS"},
               "restoredTriggerControls": guard.copy(), "restoredLoadedAssemblies": expected["assemblies"].copy(),
               "restoredFile": file.copy(), "restartFile": file.copy(),
               "boundedApplicationControls": {"status": "PASS", "retainedKeysSession": "PASS",
                   "missingKeysSession": "DENIED_401", "missingCsrf": "DENIED_403",
                   "restartRetainedSession": "PASS", "freshLogin": "PASS"},
               "backupManifestSha256After": "4" * 64, "backupManifestVerificationAfterControls": "PASS",
               "stoppedContainers": [prefix + "-source", prefix + "-target"],
               "admittedContainerIds": ["6" * 64, "7" * 64], "cleanupState": "VERIFIED"}
    return expected, receipt


def encoded(receipt):
    raw = json.dumps(receipt).encode()
    summary = json.dumps({"status": "SYNTHETIC_DATABASE_CANDIDATE_PASS",
                          "receiptSha256": ci.sha(raw), "formalStrategyApproved": False,
                          "fullApplicationRecovery": "NOT_EXECUTED"}).encode()
    return summary, raw


class ClosedRecoveryPublicationTests(unittest.TestCase):
    def reject(self, mutation, exit_code=0):
        expected, receipt = fixture()
        mutation(receipt)
        summary, raw = encoded(receipt)
        with self.assertRaises((ci.Rejected, TypeError, ValueError)):
            ci.validate(summary, raw, exit_code, expected)

    def test_complete_bounded_receipt_requires_every_control(self):
        expected, receipt = fixture()
        controls = ci.validate(*encoded(receipt), 0, expected)
        self.assertEqual(8, len(controls))
        self.assertEqual({"PASS"}, set(controls.values()))
        self.assertFalse(receipt["formalStrategyApproved"])
        self.assertFalse(receipt["publicGo"])
        self.assertEqual("NOT_EXECUTED", receipt["fullApplicationRecovery"])

    def test_nonzero_exit_cannot_be_repaired_by_pass_receipt(self):
        for code in (1, 2, -15):
            with self.subTest(code=code):
                self.reject(lambda receipt: None, code)

    def test_each_source_and_build_binding_rejects_stale_values(self):
        for key in ("sourceSha", "probeSha256", "ddlSha256", "postgresImageId"):
            with self.subTest(key=key):
                self.reject(lambda receipt, key=key: receipt.__setitem__(key, "stale"))
        for key in ("sourceSha", "sourceTree", "sdkVersion", "generatedDdlSha256", "buildExitCode"):
            with self.subTest(build=key):
                self.reject(lambda receipt, key=key: receipt["applicationBuild"].__setitem__(key, "stale"))

    def test_all_seven_comparisons_are_mandatory_and_exact(self):
        for name in ci.COMPARISONS:
            with self.subTest(name=name):
                self.reject(lambda receipt, name=name: receipt["comparisons"].pop(name))
                self.reject(lambda receipt, name=name: receipt["comparisons"][name].__setitem__("exactBytesEqual", False))
                self.reject(lambda receipt, name=name: receipt["comparisons"][name].__setitem__("restoredSha256", "8" * 64))

    def test_negative_oracles_and_source_trigger_controls_cannot_be_omitted(self):
        for key in ci.ORACLE:
            self.reject(lambda receipt, key=key: receipt["oracleNegativeControls"].pop(key))
        for side in ("triggerControls", "restoredTriggerControls"):
            for key in ("revisionHeadNonzeroInsert", "fileVersionArchiveCopyAfterCapture",
                        "guardSourceSha256", "fileTriggerSourceSha256"):
                self.reject(lambda receipt, side=side, key=key: receipt[side].__setitem__(key, "WRONG"))

    def test_loaded_assemblies_and_preserved_backup_must_match(self):
        for side in ("sourceLoadedAssemblies", "restoredLoadedAssemblies"):
            self.reject(lambda receipt, side=side: receipt[side].__setitem__("Coglatas.Web.dll", "8" * 64))
        self.reject(lambda receipt: receipt.__setitem__("backupManifestSha256After", "9" * 64))
        self.reject(lambda receipt: receipt.__setitem__("backupManifestVerificationAfterControls", "NOT_EXECUTED"))

    def test_application_negatives_and_three_file_receipts_are_required(self):
        for key in ci.APP:
            self.reject(lambda receipt, key=key: receipt["boundedApplicationControls"].pop(key))
        for side in ("sourceFile", "restoredFile", "restartFile"):
            self.reject(lambda receipt, side=side: receipt[side].__setitem__("sha256", "9" * 64))
        self.reject(lambda receipt: receipt["sourceFile"].__setitem__("bytes", 0))

    def test_cleanup_empty_duplicate_unknown_and_partial_ownership_fail(self):
        self.reject(lambda receipt: receipt.__setitem__("admittedContainerIds", []))
        self.reject(lambda receipt: receipt.__setitem__("admittedContainerIds", ["6" * 64, "6" * 64]))
        self.reject(lambda receipt: receipt.__setitem__("stoppedContainers", []))
        self.reject(lambda receipt: receipt.__setitem__("cleanupState", "INCOMPLETE"))
        self.reject(lambda receipt: receipt["resources"]["volumes"].__setitem__(1, "existing-production"))

    def test_unknown_duplicate_nonfinite_and_malformed_inputs_fail_without_secret_echo(self):
        canary = "SYNTHETIC_SECRET_CANARY_DO_NOT_PUBLISH"
        self.reject(lambda receipt: receipt.__setitem__("unknown", canary))
        expected, receipt = fixture()
        summary, raw = encoded(receipt)
        for invalid in (b'{"status":"FAIL","status":"PASS"}', b'{"unexpected":NaN}', b"{"):
            with self.subTest(invalid=invalid):
                with self.assertRaises((ci.Rejected, ValueError)) as caught:
                    ci.validate(summary, invalid, 0, expected)
                self.assertNotIn(canary, str(caught.exception))
        self.reject(lambda receipt: receipt.__setitem__("basebackupSeconds", float("inf")))
        self.reject(lambda receipt: receipt.__setitem__("formalStrategyApproved", True))
        self.reject(lambda receipt: receipt.__setitem__("targetHttps", "PASS"))

    def test_stdout_summary_is_closed_and_digest_bound(self):
        expected, receipt = fixture()
        summary, raw = encoded(receipt)
        value = json.loads(summary)
        for mutation in (lambda x: x.__setitem__("receiptSha256", "0" * 64),
                         lambda x: x.__setitem__("status", "FAIL"),
                         lambda x: x.__setitem__("private", "SECRET")):
            changed = value.copy()
            mutation(changed)
            with self.assertRaises(ci.Rejected):
                ci.validate(json.dumps(changed).encode(), raw, 0, expected)


    def test_numeric_zero_is_not_a_boolean_approval_field(self):
        expected, receipt = fixture()
        summary, raw = encoded(receipt)
        value = json.loads(summary)
        value["formalStrategyApproved"] = 0
        with self.assertRaises(ci.Rejected):
            ci.validate(json.dumps(value).encode(), raw, 0, expected)

    def test_failure_comparison_projection_is_closed_bound_and_never_a_pass(self):
        expected, receipt = fixture()
        receipt["status"] = "FAIL"
        receipt["failureCategory"] = "STRICT_RESTORED_STATE_MISMATCH"
        receipt["comparisons"]["schema.sql"]["restoredSha256"] = "9" * 64
        receipt["comparisons"]["schema.sql"]["exactBytesEqual"] = False
        value = ci.comparison_evidence(json.dumps(receipt).encode(), expected)
        self.assertEqual(set(ci.COMPARISONS), set(value))
        self.assertFalse(value["schema.sql"]["exactBytesEqual"])
        self.assertNotIn("status", value)
        self.assertEqual("FAIL", receipt["status"])
        canary = "SYNTHETIC_SECRET_CANARY_DO_NOT_PUBLISH"
        for mutation in (
            lambda r: r.__setitem__("private", canary),
            lambda r: r.__setitem__("sourceSha", "stale"),
            lambda r: r["comparisons"].__setitem__("private", canary),
            lambda r: r["comparisons"]["schema.sql"].__setitem__("exactBytesEqual", True),
            lambda r: r["comparisons"]["schema.sql"].__setitem__("sourceSha256", canary),
        ):
            changed = json.loads(json.dumps(receipt))
            mutation(changed)
            projected = ci.comparison_evidence(json.dumps(changed).encode(), expected)
            self.assertIsNone(projected)
            self.assertNotIn(canary, json.dumps(projected))

    def test_first_result_is_atomic_and_never_overwritten(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "public/result.json"
            original = {"status": "FAIL", "runAttempt": 1}
            ci.publish_once(path, original)
            with self.assertRaises(FileExistsError):
                ci.publish_once(path, {"status": "PASS", "runAttempt": 2})
            self.assertEqual(original, json.loads(path.read_text()))

    def test_private_receipt_reader_rejects_links_missing_and_oversize(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            value = root / "receipt.json"
            value.write_bytes(b"{}")
            self.assertEqual(b"{}", ci.read_private(value))
            link = root / "link"
            link.symlink_to(value)
            with self.assertRaises(ci.Rejected):
                ci.read_private(link)
            value.write_bytes(b"x" * 131073)
            with self.assertRaises(ci.Rejected):
                ci.read_private(value)
            with self.assertRaises(FileNotFoundError):
                ci.read_private(root / "missing")


if __name__ == "__main__":
    unittest.main()
