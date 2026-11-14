"""Closed tests for the single logical attempt and strict failure publication."""
import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "logical_recovery_ci", ROOT / "scripts/ci/run-synthetic-logical-recovery-ci.py")
caller = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(caller)
SOURCE, TARGET = "a" * 64, "b" * 64
CANARY = b"PRIVATE_SYNTHETIC_ROW_COOKIE_KEY_MUST_NOT_APPEAR"


def complete():
    commands = caller.logical_commands(SOURCE, TARGET)
    return {"resourceIds": [SOURCE, TARGET],
            "commands": {key: {"argv": value, "exitCode": 0, "timedOut": False}
                         for key, value in commands.items()},
            "dumpSha256": caller.support.sha(b"custom archive"),
            "comparisons": caller.strict_comparisons(
                {name: b"identical" for name in caller.COMPARISONS},
                {name: b"identical" for name in caller.COMPARISONS}),
            "controls": caller.CONTROLS.copy(), "cleanupState": "VERIFIED",
            "admittedContainers": 2, "stoppedContainers": 2}


class LogicalRecoveryContracts(unittest.TestCase):
    def test_complete_bounded_database_control(self):
        self.assertTrue(caller.candidate_complete(complete()))

    def test_every_strict_family_is_required(self):
        for name in caller.COMPARISONS:
            with self.subTest(name=name):
                result = complete()
                del result["comparisons"][name]
                self.assertFalse(caller.candidate_complete(result))

    def test_raw_bytes_are_never_normalized(self):
        before = {name: b"CHECK ((id > 0))\n" for name in caller.COMPARISONS}
        for name in caller.COMPARISONS:
            after = before.copy()
            after[name] = b"CHECK (id > 0)\n"
            observed = caller.strict_comparisons(before, after)
            self.assertIs(observed[name]["exactBytesEqual"], False)
            result = complete()
            result["comparisons"] = observed
            self.assertFalse(caller.candidate_complete(result))

    def test_unknown_missing_and_nonbyte_inputs_rejected(self):
        original = {name: b"unchanged" for name in caller.COMPARISONS}
        for value in ({}, {**original, "unexpected": CANARY},
                      {**original, "schema.sql": "text instead of bytes"}):
            with self.assertRaises(caller.support.Rejected):
                caller.strict_comparisons(original, value)

    def test_false_boolean_inconsistent_digests_and_extra_fields_fail(self):
        for mutation in (
            lambda item: item.update(exactBytesEqual=1),
            lambda item: item.update(exactBytesEqual=False),
            lambda item: item.update(restoredSha256="c" * 64),
            lambda item: item.update(sourceSha256="not-a-digest"),
            lambda item: item.update(rawBody=CANARY.decode()),
        ):
            result = complete()
            mutation(result["comparisons"]["schema.sql"])
            self.assertFalse(caller.candidate_complete(result))

    def test_nonzero_timeout_missing_and_boolean_exit_never_pass(self):
        for name in ("dump", "restore"):
            for exit_code in (1, -9, None, False):
                result = complete()
                result["commands"][name]["exitCode"] = exit_code
                self.assertFalse(caller.candidate_complete(result))
            result = complete()
            result["commands"][name]["timedOut"] = True
            self.assertFalse(caller.candidate_complete(result))

    def test_each_positive_and_cleanup_control_is_required(self):
        for key in caller.CONTROLS:
            result = complete()
            del result["controls"][key]
            self.assertFalse(caller.candidate_complete(result))
        for key, value in (("cleanupState", "INCOMPLETE"), ("admittedContainers", 1),
                           ("stoppedContainers", 1), ("admittedContainers", True),
                           ("dumpSha256", None), ("resourceIds", []),
                           ("resourceIds", [SOURCE]), ("resourceIds", [SOURCE, TARGET, "c" * 64])):
            result = complete()
            result[key] = value
            self.assertFalse(caller.candidate_complete(result))

    def test_exact_documented_commands_and_fresh_distinct_targets(self):
        commands = caller.logical_commands(SOURCE, TARGET)
        self.assertEqual(commands, {
            "dump": ["docker", "--host=unix:///var/run/docker.sock", "exec", SOURCE,
                     "pg_dump", "-U", "postgres", "-d", "recovery", "--format=custom",
                     "--no-owner", "--no-privileges"],
            "restore": ["docker", "--host=unix:///var/run/docker.sock", "exec", "-i", TARGET,
                        "pg_restore", "-U", "postgres", "-d", "recovery", "--no-owner",
                        "--no-privileges", "--exit-on-error"]})
        for source, target in ((SOURCE, SOURCE), ("external-host", TARGET), (SOURCE, "../target")):
            with self.assertRaises(caller.support.Rejected):
                caller.logical_commands(source, target)
        for name in commands:
            result = complete()
            result["commands"][name]["argv"].append("--disable-triggers")
            self.assertFalse(caller.candidate_complete(result))

    def run_transfer(self, codes):
        calls = []
        def run(args, **kwargs):
            calls.append((args, kwargs))
            index = len(calls) - 1
            return subprocess.CompletedProcess(args, codes[index], CANARY, CANARY)
        with tempfile.TemporaryDirectory() as directory:
            private = Path(directory)
            result = {}
            caller.transfer_once(SOURCE, TARGET, private, result, {}, runner=run)
            self.assertNotIn(CANARY.decode(), json.dumps(result))
            return result, calls

    def test_single_dump_and_restore_bind_exact_archive_bytes(self):
        result, calls = self.run_transfer([0, 0])
        self.assertEqual(len(calls), 2)
        self.assertIsNone(calls[0][1]["input"])
        self.assertTrue(calls[1][1]["input"] == CANARY)
        self.assertEqual(result["dumpSha256"], caller.support.sha(CANARY))

    def test_dump_failure_preserves_actual_nonzero_and_skips_restore(self):
        result, calls = self.run_transfer([17])
        self.assertEqual(len(calls), 1)
        self.assertEqual(result["commands"]["dump"]["exitCode"], 17)
        self.assertIsNone(result["commands"]["restore"]["exitCode"])

    def test_restore_failure_preserved_without_retry(self):
        result, calls = self.run_transfer([0, 23])
        self.assertEqual(len(calls), 2)
        self.assertEqual(result["commands"]["restore"]["exitCode"], 23)
        self.assertFalse(caller.candidate_complete(result))

    def test_timeout_does_not_retry_or_invent_exit_code(self):
        calls = []
        def timeout(args, **kwargs):
            calls.append(args)
            raise subprocess.TimeoutExpired(args, 120, output=CANARY, stderr=CANARY)
        with tempfile.TemporaryDirectory() as directory:
            result = {}
            caller.transfer_once(SOURCE, TARGET, Path(directory), result, {}, runner=timeout)
        self.assertEqual(len(calls), 1)
        self.assertTrue(result["commands"]["dump"]["timedOut"])
        self.assertIsNone(result["commands"]["dump"]["exitCode"])
        self.assertNotIn(CANARY.decode(), json.dumps(result))

    def test_same_attempt_cannot_be_executed_twice(self):
        result, _ = self.run_transfer([0, 23])
        def unexpected(*args, **kwargs):
            self.fail("A second acceptance command was executed")
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(caller.support.Rejected):
                caller.transfer_once(SOURCE, TARGET, Path(directory), result, {}, runner=unexpected)

    def test_first_failed_publication_cannot_be_overwritten(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "result.json"
            first = {"status": "FAIL", "restoreExitCode": 23}
            caller.support.publish_once(path, first)
            retained = path.read_bytes()
            with self.assertRaises(FileExistsError):
                caller.support.publish_once(path, {"status": "PASS"})
            self.assertTrue(path.read_bytes() == retained)
            self.assertEqual(json.loads(retained), first)

    def test_public_comparisons_never_contain_raw_snapshot_values(self):
        values = {name: CANARY for name in caller.COMPARISONS}
        observed = caller.strict_comparisons(values, values)
        self.assertNotIn(CANARY.decode(), json.dumps(observed))
        self.assertTrue(caller.comparisons_pass(observed))


def catalog_rows():
    private = CANARY.decode()
    return [
        ["constraint", private, private, private, "c", True, False, False, "RAW_A", private],
        ["index", private, private, "EXPR_A", "PRED_A", True, True, private],
        ["trigger", private, private, private, "O", False, private],
        ["table-security", private, private, False, False, [private]],
    ]


def catalog_bytes(rows):
    return ("\n".join(json.dumps(row) for row in rows) + "\n").encode()


class CatalogMetadataContracts(unittest.TestCase):
    def assert_closed(self, diagnostic):
        self.assertEqual(set(diagnostic), {"availability", "sourceCatalogSha256",
                         "restoredCatalogSha256", "schemaClassification", "interpretation", "counts"})
        self.assertEqual(diagnostic["schemaClassification"], "UNCLASSIFIED")
        self.assertEqual(diagnostic["interpretation"], "STRUCTURAL_FIELD_DIFFERENCES_ONLY")
        self.assertNotIn(CANARY.decode(), json.dumps(diagnostic))
        for key in ("sourceCatalogSha256", "restoredCatalogSha256"):
            self.assertTrue(diagnostic[key] is None or caller.support.digest_value(diagnostic[key]))
        self.assertTrue(all(type(value) is int and value >= 0 for value in diagnostic["counts"].values()))

    def diagnose(self, left, right):
        diagnostic = caller.catalog_diagnostic(catalog_bytes(left), catalog_bytes(right))
        self.assert_closed(diagnostic)
        return diagnostic

    def test_exact_record_positive_and_original_digest_binding(self):
        raw = catalog_bytes(catalog_rows())
        diagnostic = caller.catalog_diagnostic(raw, raw)
        self.assert_closed(diagnostic)
        self.assertEqual(diagnostic["availability"], "AVAILABLE")
        self.assertEqual(diagnostic["sourceCatalogSha256"], caller.support.sha(raw))
        self.assertEqual(diagnostic["restoredCatalogSha256"], caller.support.sha(raw))
        self.assertEqual(diagnostic["counts"]["EXACT_RECORDS"], 4)
        self.assertEqual(diagnostic["counts"]["SOURCE_RECORDS"], 4)
        self.assertEqual(diagnostic["counts"]["RESTORED_RECORDS"], 4)

    def test_only_constraint_raw_field_change_is_categorized(self):
        left = catalog_rows()
        right = copy.deepcopy(left)
        right[0][8] = "RAW_B"
        diagnostic = self.diagnose(left, right)
        self.assertEqual(diagnostic["counts"]["CONSTRAINT_RAW_TREE_FIELD_ONLY"], 1)
        self.assertEqual(diagnostic["counts"]["EXACT_RECORDS"], 3)
        self.assertNotEqual(diagnostic["sourceCatalogSha256"], diagnostic["restoredCatalogSha256"])

    def test_either_index_raw_field_change_is_categorized(self):
        for field in (3, 4):
            left = catalog_rows()
            right = copy.deepcopy(left)
            right[1][field] = None
            diagnostic = self.diagnose(left, right)
            self.assertEqual(diagnostic["counts"]["INDEX_RAW_TREE_FIELDS_ONLY"], 1)
            self.assertEqual(diagnostic["counts"]["OTHER_CHANGED_RECORDS"], 0)

    def test_constraint_definition_flags_and_scope_mutations_are_not_raw_only(self):
        for field, value in ((9, "DIFFERENT_DEFINITION"), (5, False), (6, True),
                             (7, True), (4, "f"), (1, "OTHER_SCOPE")):
            left = catalog_rows()
            right = copy.deepcopy(left)
            right[0][8], right[0][field] = "RAW_B", value
            diagnostic = self.diagnose(left, right)
            self.assertEqual(diagnostic["counts"]["CONSTRAINT_RAW_TREE_FIELD_ONLY"], 0)
            self.assertEqual(diagnostic["counts"]["EXACT_RECORDS"], 3)

    def test_index_definition_and_validity_changes_are_conservatively_other_or_unmatched(self):
        for field, value in ((7, "DIFFERENT_INDEX_DEFINITION"), (5, False), (6, False)):
            left = catalog_rows()
            right = copy.deepcopy(left)
            right[1][3], right[1][field] = "RAW_B", value
            diagnostic = self.diagnose(left, right)
            self.assertEqual(diagnostic["counts"]["INDEX_RAW_TREE_FIELDS_ONLY"], 0)
            self.assertEqual(diagnostic["counts"]["EXACT_RECORDS"], 3)

    def test_trigger_security_and_acl_changes_are_other(self):
        for row, field, value in ((2, 4, "D"), (2, 5, True), (3, 3, True), (3, 5, None)):
            left = catalog_rows()
            right = copy.deepcopy(left)
            right[row][field] = value
            diagnostic = self.diagnose(left, right)
            self.assertEqual(diagnostic["counts"]["OTHER_CHANGED_RECORDS"], 1)
            self.assertEqual(diagnostic["counts"]["EXACT_RECORDS"], 3)

    def test_added_removed_and_duplicate_records_do_not_invent_matches(self):
        rows = catalog_rows()
        diagnostic = self.diagnose(rows, rows[:-1])
        self.assertEqual(diagnostic["counts"]["SOURCE_ONLY_RECORDS"], 1)
        self.assertEqual(diagnostic["counts"]["RESTORED_ONLY_RECORDS"], 0)
        diagnostic = self.diagnose(rows[:-1], rows)
        self.assertEqual(diagnostic["counts"]["RESTORED_ONLY_RECORDS"], 1)
        diagnostic = self.diagnose(rows + [rows[0]], rows)
        self.assertEqual(diagnostic["availability"], "UNAVAILABLE")
        self.assertEqual(diagnostic["counts"], {})

    def test_malformed_unknown_nonfinite_and_nonbyte_inputs_are_closed(self):
        valid = catalog_bytes(catalog_rows())
        bad_values = (b"", b"not-json", b"\xff", b"null\n", b'["unknown","PRIVATE"]\n',
                      b'["constraint"]\n', b'["index",NaN]\n', CANARY.decode(), None,
                      b"x" * 2097153, b"[" * 2000 + b"0" + b"]" * 2000)
        for bad in bad_values:
            diagnostic = caller.catalog_diagnostic(valid, bad)
            self.assert_closed(diagnostic)
            self.assertEqual(diagnostic["availability"], "UNAVAILABLE")
            self.assertEqual(diagnostic["counts"], {})

    def test_type_confusion_and_unexpected_acl_values_are_closed(self):
        for row, field, value in ((0, 5, 1), (1, 6, 0), (2, 5, "false"),
                                  (3, 5, [CANARY.decode(), {"unexpected": CANARY.decode()}])):
            left = catalog_rows()
            right = copy.deepcopy(left)
            right[row][field] = value
            diagnostic = self.diagnose(left, right)
            self.assertEqual(diagnostic["availability"], "UNAVAILABLE")
            self.assertEqual(diagnostic["counts"], {})

    def test_record_order_or_json_encoding_cannot_normalize_strict_oracle(self):
        left = catalog_bytes(catalog_rows())
        for right in (catalog_bytes(list(reversed(catalog_rows()))),
                      ("\n".join(json.dumps(row, separators=(",", ":")) for row in catalog_rows()) + "\n").encode()):
            diagnostic = caller.catalog_diagnostic(left, right)
            self.assert_closed(diagnostic)
            self.assertEqual(diagnostic["availability"], "AVAILABLE")
            before = {name: b"identical" for name in caller.COMPARISONS}
            after = before.copy()
            before["catalog.txt"], after["catalog.txt"] = left, right
            comparisons = caller.strict_comparisons(before, after)
            self.assertIs(comparisons["catalog.txt"]["exactBytesEqual"], False)
            result = complete()
            result["comparisons"], result["catalogDiagnostic"] = comparisons, diagnostic
            self.assertFalse(caller.candidate_complete(result))

    def test_raw_tree_diagnostics_never_override_catalog_or_schema_false(self):
        left, right = catalog_rows(), catalog_rows()
        right[0][8], right[1][3] = "RAW_B", "EXPR_B"
        diagnostic = self.diagnose(left, right)
        self.assertEqual(diagnostic["counts"]["CONSTRAINT_RAW_TREE_FIELD_ONLY"], 1)
        self.assertEqual(diagnostic["counts"]["INDEX_RAW_TREE_FIELDS_ONLY"], 1)
        for name in ("catalog.txt", "schema.sql"):
            before = {key: b"identical" for key in caller.COMPARISONS}
            after = before.copy()
            after[name] = b"DIFFERENT"
            result = complete()
            result["comparisons"] = caller.strict_comparisons(before, after)
            result["catalogDiagnostic"] = diagnostic
            self.assertFalse(caller.candidate_complete(result))

    def test_unavailable_diagnostic_is_metadata_only(self):
        result = complete()
        result["catalogDiagnostic"] = caller.catalog_diagnostic(b"invalid", b"invalid")
        self.assertTrue(caller.candidate_complete(result))


if __name__ == "__main__":
    unittest.main()
