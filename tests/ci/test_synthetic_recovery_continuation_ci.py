"""Closed synthetic continuation receipts cannot turn partial/failed controls green."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("continuation_ci", ROOT / "scripts/ci/run-synthetic-recovery-continuation-ci.py")
caller = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(caller)
CANARY = "PRIVATE_PASSWORD_COOKIE_ROW_KEY_MUST_NOT_APPEAR"


def hashes():
    return {name: "a" * 64 for name in caller.TOOL_ASSEMBLIES}


def receipt(mode):
    return dict(schemaVersion=1, mode=mode, status="PASS", phase="COMPLETE",
                controls=copy.deepcopy(caller.TOOL_CONTROLS[mode]), assemblies=hashes(),
                transportObservationSeconds=15 if mode in ("continuation", "restart") else 0,
                webSocketDeliveryGuarantee="BOUNDED_CONTROL_OBSERVATION_ONLY",
                databaseAuthorityScope="SYNTHETIC_POSTGRES_BOOTSTRAP_ROLE_ONLY",
                formalRecoveryAcceptance="NOT_GRANTED")


def complete():
    values = {name: b"same" for name in caller.logical.COMPARISONS}
    return dict(baseControls=caller.BASE_CONTROLS.copy(),
                toolControls=copy.deepcopy(caller.TOOL_CONTROLS),
                toolReceiptSha256={mode: "b" * 64 for mode in caller.TOOL_CONTROLS},
                toolExitCodes={mode: 0 for mode in caller.TOOL_CONTROLS},
                comparisons=caller.logical.strict_comparisons(values, values),
                rollbackComparisons=caller.logical.strict_comparisons(values, values),
                backupManifestSha256="c" * 64, admittedContainers=2, stoppedContainers=2,
                cleanupState="VERIFIED")


class ContinuationContracts(unittest.TestCase):
    def test_every_exact_mode_receipt_is_required(self):
        for mode in caller.TOOL_CONTROLS:
            value = caller.validate_tool(json.dumps(receipt(mode)).encode(), mode, 0, hashes())
            self.assertEqual(value, caller.TOOL_CONTROLS[mode])
        self.assertTrue(caller.complete(complete()))

    def test_missing_or_unknown_mode_control_never_passes(self):
        for mode in caller.TOOL_CONTROLS:
            for key in caller.TOOL_CONTROLS[mode]:
                value = receipt(mode)
                del value["controls"][key]
                with self.assertRaises(caller.support.Rejected):
                    caller.validate_tool(json.dumps(value).encode(), mode, 0, hashes())
            value = receipt(mode)
            value["controls"]["unexpectedPrivateBody"] = CANARY
            with self.assertRaises(caller.support.Rejected) as refused:
                caller.validate_tool(json.dumps(value).encode(), mode, 0, hashes())
            self.assertNotIn(CANARY, str(refused.exception))

    def test_failure_phase_nonzero_missing_or_boolean_exit_cannot_pass(self):
        for code in (1, -9, None, False):
            with self.assertRaises(caller.support.Rejected):
                caller.validate_tool(json.dumps(receipt("seed")).encode(), "seed", code, hashes())
        for key, value in (("status", "FAIL"), ("phase", "AUDIT_FAILURE_INJECTION"), ("schemaVersion", True)):
            changed = receipt("continuation")
            changed[key] = value
            with self.assertRaises(caller.support.Rejected):
                caller.validate_tool(json.dumps(changed).encode(), "continuation", 0, hashes())

    def test_assembly_binding_requires_each_actual_binary(self):
        for name in caller.TOOL_ASSEMBLIES:
            value = receipt("restored-authority")
            value["assemblies"][name] = "d" * 64
            with self.assertRaises(caller.support.Rejected):
                caller.validate_tool(json.dumps(value).encode(), "restored-authority", 0, hashes())
        value = receipt("seed")
        value["assemblies"]["unapproved.dll"] = "e" * 64
        with self.assertRaises(caller.support.Rejected):
            caller.validate_tool(json.dumps(value).encode(), "seed", 0, hashes())

    def test_authority_scope_and_finite_observation_are_exact(self):
        for key, value in (("databaseAuthorityScope", "PRODUCTION_OWNER"),
                           ("formalRecoveryAcceptance", "GRANTED"),
                           ("transportObservationSeconds", True),
                           ("transportObservationSeconds", 0),
                           ("webSocketDeliveryGuarantee", "GLOBAL_EXACTLY_ONCE")):
            changed = receipt("restart")
            changed[key] = value
            with self.assertRaises(caller.support.Rejected):
                caller.validate_tool(json.dumps(changed).encode(), "restart", 0, hashes())

    def test_unknown_duplicate_nonfinite_or_canary_receipts_rejected_privately(self):
        value = receipt("continuation")
        value["rawCookie"] = CANARY
        duplicate = json.dumps(receipt("seed"))[:-1] + ', "status":"PASS"}'
        for raw in (json.dumps(value).encode(), duplicate.encode(), b'{"private":NaN}', b'{"private":Infinity}'):
            with self.assertRaises(caller.support.Rejected) as refused:
                caller.validate_tool(raw, "seed", 0, hashes())
            self.assertNotIn(CANARY, str(refused.exception))

    def test_every_base_control_is_mandatory(self):
        for key in caller.BASE_CONTROLS:
            value = complete()
            del value["baseControls"][key]
            self.assertFalse(caller.complete(value))

    def test_each_source_restored_and_rollback_family_is_exact(self):
        for family in caller.logical.COMPARISONS:
            for key in ("comparisons", "rollbackComparisons"):
                value = complete()
                value[key][family]["restoredSha256"] = "f" * 64
                value[key][family]["exactBytesEqual"] = False
                self.assertFalse(caller.complete(value))

    def test_no_missing_mode_receipt_hash_exit_or_control(self):
        for mode in caller.TOOL_CONTROLS:
            for key in ("toolControls", "toolReceiptSha256", "toolExitCodes"):
                value = complete()
                del value[key][mode]
                self.assertFalse(caller.complete(value))

    def test_boolean_nonzero_exit_or_invalid_digest_never_green(self):
        for mode in caller.TOOL_CONTROLS:
            for code in (1, -9, None, False):
                value = complete()
                value["toolExitCodes"][mode] = code
                self.assertFalse(caller.complete(value))
            value = complete()
            value["toolReceiptSha256"][mode] = CANARY
            self.assertFalse(caller.complete(value))

    def test_cleanup_manifest_and_immutable_admission_are_required(self):
        for key, value in (("admittedContainers", 0), ("admittedContainers", True),
                           ("stoppedContainers", 1), ("cleanupState", "INCOMPLETE"),
                           ("backupManifestSha256", None)):
            result = complete()
            result[key] = value
            self.assertFalse(caller.complete(result))

    def test_first_published_failure_cannot_be_replaced(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "result.json"
            first = {"status": "FAIL", "phase": "STRICT_RESTORED_BASELINE"}
            caller.support.publish_once(path, first)
            original = path.read_bytes()
            with self.assertRaises(FileExistsError):
                caller.support.publish_once(path, {"status": "PASS"})
            self.assertEqual(path.read_bytes(), original)

    def test_validated_projection_contains_only_fixed_controls(self):
        value = caller.validate_tool(json.dumps(receipt("continuation")).encode(), "continuation", 0, hashes())
        self.assertEqual(value, caller.TOOL_CONTROLS["continuation"])
        self.assertNotIn(CANARY, json.dumps(value))


if __name__ == "__main__":
    unittest.main()
