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
                lastHttp=(None if mode == "seed" else {"operation":
                    "CONT_MESSAGE_DUPLICATE" if mode == "continuation" else
                    "RESTART_MESSAGE_FIRST" if mode == "restart" else
                    "RETAINED_SESSION_MESSAGES" if mode == "retained-session" else
                    "MISSING_KEYS_FRESH_BETA_ME" if mode == "missing-keys" else
                    "RESTORED_KEYS_MESSAGES" if mode == "restored-keys" else
                    "CSRF_MISSING_TOKEN" if mode == "restored-authority" else
                    "SOURCE_COOKIE_ME" if mode == "source-authority" else "AUTH_READER_POST",
                    "expectedStatus": 403 if mode in ("restored-authority", "rollback-authority") else 200,
                    "actualStatus": 403 if mode in ("restored-authority", "rollback-authority") else 200}),
                lastPredicate=(None if mode == "seed" else
                    {"operation": "MISSING_KEYS_FRESH_BETA_SUBJECT" if mode == "missing-keys" else
                        "RETAINED_SESSION_PROTECTED_READ" if mode in ("retained-session", "restored-keys") else
                        "CSRF_CREDENTIAL_UNCHANGED" if mode == "restored-authority" else
                        "SOURCE_COOKIE_SESSION_ACTIVE" if mode == "source-authority" else
                        "AUTH_READER_COUNT_UNCHANGED", "satisfied": True}), failureKind=None,
                transportObservationSeconds=15 if mode in ("continuation", "restart") else 0,
                webSocketDeliveryGuarantee="BOUNDED_CONTROL_OBSERVATION_ONLY",
                databaseAuthorityScope="SYNTHETIC_POSTGRES_BOOTSTRAP_ROLE_ONLY",
                formalRecoveryAcceptance="NOT_GRANTED")


def measurements():
    value = {key: "2026-10-27T04:00:00Z" for key in caller.UTC_FIELDS}
    value.update({key: 0.0 for key in caller.DURATION_FIELDS})
    return dict(value, scope=caller.MEASUREMENT_SCOPE, productionRpoRtoSloQualified=False,
                quiescentCheckpointExact=True)


def complete():
    values = {name: b"same" for name in caller.logical.COMPARISONS}
    return dict(measurements=measurements(), sourceSessionCookieSha256="d" * 64,
                baseControls=caller.BASE_CONTROLS.copy(),
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



    def test_closed_source_authority_failure_never_becomes_control_credit(self):
        value = receipt("source-authority")
        value.update(status="FAIL", phase="AUTH_ALPHA_MESSAGE_BODY", controls={}, assemblies={},
                     failureKind="CONTROL_REJECTED",
                     lastHttp={"operation": "AUTH_ALPHA_MESSAGES", "expectedStatus": 200, "actualStatus": 200},
                     lastPredicate={"operation": "AUTH_ALPHA_MESSAGE_BODY", "satisfied": False})
        raw = json.dumps(value).encode()
        observed = caller.failure_projection(raw, "source-authority", 1, caller.support.sha(raw))
        self.assertEqual(set(observed), {"mode", "exitCode", "phase", "failureKind", "lastHttp", "lastPredicate", "receiptSha256"})
        self.assertIs(observed["lastPredicate"]["satisfied"], False)
        self.assertNotIn("controls", observed)
        with self.assertRaises(caller.support.Rejected):
            caller.validate_tool(raw, "source-authority", 1, hashes())

    def test_failure_unknown_fields_canaries_wrong_mode_or_digest_are_omitted(self):
        original = receipt("source-authority")
        original.update(status="FAIL", phase="AUTH_ALPHA_MESSAGES", controls={}, assemblies={},
                        failureKind="CONTROL_REJECTED",
                        lastHttp={"operation": "AUTH_ALPHA_MESSAGES", "expectedStatus": 200, "actualStatus": 400})
        mutations = [
            lambda value: value.update(rawException=CANARY),
            lambda value: value.update(phase=CANARY),
            lambda value: value.update(mode="seed"),
            lambda value: value.update(failureKind=CANARY),
            lambda value: value["lastHttp"].update(operation=CANARY),
            lambda value: value["lastHttp"].update(actualStatus=True),
            lambda value: value["lastHttp"].update(expectedStatus=201),
            lambda value: value["lastHttp"].update(actualStatus=0),
            lambda value: value.update(lastPredicate={"operation": "AUTH_ALPHA_MESSAGE_BODY", "satisfied": 0}),
        ]
        for mutate in mutations:
            value = copy.deepcopy(original)
            mutate(value)
            raw = json.dumps(value).encode()
            self.assertIsNone(caller.failure_projection(raw, "source-authority", 1, caller.support.sha(raw)))
        raw = json.dumps(original).encode()
        for digest in ("a" * 64, CANARY):
            self.assertIsNone(caller.failure_projection(raw, "source-authority", 1, digest))
        for exit_code in (0, False):
            self.assertIsNone(caller.failure_projection(raw, "source-authority", exit_code, caller.support.sha(raw)))

    def test_success_cannot_hide_a_failed_http_or_predicate_observation(self):
        for observation in (
            {"lastHttp": None}, {"lastPredicate": None},
            {"lastHttp": {"operation": "AUTH_ALPHA_MESSAGES", "expectedStatus": 200, "actualStatus": 400}},
            {"lastPredicate": {"operation": "AUTH_ALPHA_MESSAGE_BODY", "satisfied": False}},
            {"lastHttp": {"operation": CANARY, "expectedStatus": 200, "actualStatus": 200}},
        ):
            value = receipt("source-authority")
            value.update(observation)
            with self.assertRaises(caller.support.Rejected):
                caller.validate_tool(json.dumps(value).encode(), "source-authority", 0, hashes())


    def test_all_three_key_boundary_modes_and_cookie_hash_are_required(self):
        for mode in ("retained-session", "missing-keys", "restored-keys"):
            value = complete()
            del value["toolControls"][mode]
            self.assertFalse(caller.complete(value))
            value = receipt(mode)
            value["controls"] = {"oldCookieRejectedBecauseServerUnavailable": "PASS"}
            with self.assertRaises(caller.support.Rejected):
                caller.validate_tool(json.dumps(value).encode(), mode, 0, hashes())
        for digest in (None, "", CANARY, True):
            value = complete()
            value["sourceSessionCookieSha256"] = digest
            self.assertFalse(caller.complete(value))

    def test_csrf_requires_same_route_handler_positive_and_credential_unchanged(self):
        for key in ("sameRouteValidCsrfHandlerAndMissingTokenDenial", "csrfControlsDoNotChangeCredential"):
            value = receipt("restored-authority")
            del value["controls"][key]
            with self.assertRaises(caller.support.Rejected):
                caller.validate_tool(json.dumps(value).encode(), "restored-authority", 0, hashes())

    def test_measurements_are_observations_with_no_invented_production_slo(self):
        self.assertTrue(caller.measurements_complete(measurements()))
        for key, changed in (("productionRpoRtoSloQualified", True),
                             ("productionRpoRtoSloQualified", 0),
                             ("quiescentCheckpointExact", 1),
                             ("scope", "PRODUCTION_RPO_RTO_PASS")):
            value = measurements()
            value[key] = changed
            self.assertFalse(caller.measurements_complete(value))

    def test_measurement_clock_order_closed_timestamps_and_durations_fail_closed(self):
        for key in caller.UTC_FIELDS:
            for stamp in (None, True, CANARY, "2026-02-30T00:00:00Z", "2026-10-27T04:00:00+00:00"):
                value = measurements()
                value[key] = stamp
                self.assertFalse(caller.measurements_complete(value))
            value = measurements()
            del value[key]
            self.assertFalse(caller.measurements_complete(value))
        value = measurements()
        value["backupStartedAtUtc"] = "2026-10-27T03:59:59Z"
        self.assertFalse(caller.measurements_complete(value))
        for key in caller.DURATION_FIELDS:
            for duration in (None, True, -1, float("nan"), float("inf"), CANARY):
                value = measurements()
                value[key] = duration
                self.assertFalse(caller.measurements_complete(value))
        value = measurements()
        value["syntheticDatabaseReadySeconds"] = 3
        value["syntheticRestoredAuthorityReadySeconds"] = 2
        self.assertFalse(caller.measurements_complete(value))
        value = measurements()
        value["privateKey"] = CANARY
        self.assertFalse(caller.measurements_complete(value))

    def test_key_failure_metadata_is_closed_and_cannot_credit_missing_key_success(self):
        value = receipt("missing-keys")
        value.update(status="FAIL", phase="MISSING_KEYS_OLD_COOKIE_DENIED", controls={}, assemblies={},
                     failureKind="CONTROL_REJECTED",
                     lastHttp={"operation": "MISSING_KEYS_OLD_ME", "expectedStatus": 401, "actualStatus": 200},
                     lastPredicate={"operation": "MISSING_KEYS_OLD_COOKIE_DENIED", "satisfied": False})
        raw = json.dumps(value).encode()
        observed = caller.failure_projection(raw, "missing-keys", 1, caller.support.sha(raw))
        self.assertEqual(observed["lastHttp"]["actualStatus"], 200)
        self.assertNotIn("controls", observed)
        value["rawCookie"] = CANARY
        raw = json.dumps(value).encode()
        self.assertIsNone(caller.failure_projection(raw, "missing-keys", 1, caller.support.sha(raw)))


if __name__ == "__main__":
    unittest.main()
