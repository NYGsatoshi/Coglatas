"""Closed tests for the single logical attempt and strict failure publication."""
import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

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
            "restoreList": {"entryCount": 9, "foreignKeyCount": 2,
                            "originalSha256": "a" * 64, "plannedSha256": "b" * 64},
            "comparisons": caller.strict_comparisons(
                {name: b"identical" for name in caller.COMPARISONS},
                {name: b"identical" for name in caller.COMPARISONS}),
            "portableCatalog": {"availability": "AVAILABLE", "sourceSha256": "a" * 64,
                                "restoredSha256": "a" * 64, "exactBytesEqual": True},
            "catalogAcceptancePolicy": "MAIN_PORTABLE_LOGICAL_V1",
            "catalogDiagnostic": {
                "availability": "AVAILABLE", "sourceCatalogSha256": caller.support.sha(b"identical"),
                "restoredCatalogSha256": caller.support.sha(b"identical"),
                "counts": {"SOURCE_RECORDS": 4, "RESTORED_RECORDS": 4}},
            "controls": caller.CONTROLS.copy(), "cleanupState": "VERIFIED",
            "admittedContainers": 2, "stoppedContainers": 2}


class LogicalRecoveryContracts(unittest.TestCase):
    def test_native_logical_workflow_filter_covers_imports_and_invoked_tests(self):
        workflow = (ROOT / ".github/workflows/synthetic-logical-recovery.yml").read_text()
        self.assertIn("    paths:\n", workflow)
        self.assertIn("\npermissions:", workflow)
        selectors = workflow.split("    paths:\n", 1)[1].split("\npermissions:", 1)[0]
        registered = {line.strip()[3:-1] for line in selectors.splitlines()
                      if line.strip().startswith("- '") and line.strip().endswith("'")}
        required = {
            "scripts/ci/run-synthetic-logical-recovery-ci.py",
            "scripts/ci/run-synthetic-logical-recovery.mjs",
            "scripts/ci/synthetic-portable-recovery-catalog.mjs",
            "scripts/ci/synthetic-trigger-catalog.mjs",
            "scripts/ci/synthetic-restore-list.mjs",
            "tests/ci/test_synthetic_logical_recovery_ci.py",
            "tests/ci/synthetic-portable-recovery-catalog.node-test.mjs",
            "tests/ci/synthetic-trigger-catalog.node-test.mjs",
            "tests/ci/synthetic-restore-list.node-test.mjs",
            "tests/ci/test_synthetic_physical_recovery_http_guard.py",
            "Coglatas.slnx",
        }
        self.assertFalse(required - registered, sorted(required - registered))
        for path in required:
            self.assertTrue((ROOT / path).is_file(), path)
        jobs = workflow.split("\njobs:", 1)[1]
        for path in ("tests/ci/synthetic-portable-recovery-catalog.node-test.mjs",
                     "tests/ci/synthetic-trigger-catalog.node-test.mjs",
                     "tests/ci/synthetic-restore-list.node-test.mjs"):
            self.assertIn("node --test", jobs)
            self.assertIn(path, jobs)
        self.assertIn("Verify Main-compatible portable catalog acceptance contracts", jobs)
        self.assertNotIn("shadow contracts", jobs)

    def test_main_push_identity_is_exact_and_cannot_claim_pr_metadata(self):
        base = {
            "COGLATAS_RECOVERY_RUNNER_ENVIRONMENT": "github-hosted",
            "GITHUB_EVENT_NAME": "push",
            "RUNNER_OS": "Linux",
            "GITHUB_REPOSITORY": "NYGsatoshi/Coglatas",
            "GITHUB_REF": "refs/heads/main",
            "GITHUB_SHA": "a" * 40,
            "COGLATAS_PR_HEAD_SHA": "",
            "COGLATAS_PR_BASE_SHA": "",
        }
        self.assertEqual(caller.admitted_run_revisions(base), ("a" * 40, None))
        pr = {**base, "GITHUB_EVENT_NAME": "pull_request",
              "COGLATAS_PR_HEAD_SHA": "b" * 40,
              "COGLATAS_PR_BASE_SHA": "c" * 40}
        self.assertEqual(caller.admitted_run_revisions(pr), ("b" * 40, "c" * 40))
        for update in (
            {"GITHUB_EVENT_NAME": "workflow_dispatch"},
            {"GITHUB_EVENT_NAME": "pull_request_target"},
            {"GITHUB_REF": "refs/heads/release"},
            {"GITHUB_REF": "refs/tags/main"},
            {"GITHUB_REPOSITORY": "someone-else/Coglatas"},
            {"GITHUB_SHA": "not-a-commit"},
            {"COGLATAS_PR_HEAD_SHA": "b" * 40},
            {"COGLATAS_PR_BASE_SHA": "c" * 40},
            {"RUNNER_OS": "Windows"},
            {"COGLATAS_RECOVERY_RUNNER_ENVIRONMENT": "self-hosted"},
        ):
            with self.subTest(update=update):
                with self.assertRaises(caller.support.Rejected):
                    caller.admitted_run_revisions({**base, **update})

    def test_main_push_routes_and_retains_pr_contracts(self):
        workflow = (ROOT / ".github/workflows/synthetic-logical-recovery.yml").read_text()
        self.assertIn("  pull_request:\n", workflow)
        marker = "  push:\n    branches:\n      - main\n    paths:\n"
        self.assertEqual(workflow.count(marker), 1)
        push_paths = workflow.split(marker, 1)[1].split("\npermissions:", 1)[0]
        pr_paths = workflow.split("  pull_request:\n    paths:\n", 1)[1].split(
            "\n  push:\n", 1)[0]
        self.assertEqual(push_paths, pr_paths)
        self.assertIn("  contents: read\n", workflow)
        self.assertIn("  cancel-in-progress: false\n", workflow)
        self.assertIn("github.event.pull_request.number || github.ref", workflow)
        self.assertIn("ref: " + chr(36) + "{{ github.sha }}", workflow)
        self.assertIn("COGLATAS_PR_HEAD_SHA: " + chr(36) + "{{ github.event.pull_request.head.sha }}", workflow)
        self.assertIn("COGLATAS_PR_BASE_SHA: " + chr(36) + "{{ github.event.pull_request.base.sha }}", workflow)

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

    def test_raw_catalog_failure_requires_exact_closed_digest_binding(self):
        result = complete()
        comparison = result["comparisons"]["catalog.txt"]
        comparison["restoredSha256"] = "b" * 64
        comparison["exactBytesEqual"] = False
        self.assertFalse(caller.candidate_complete(result))
        self.assertFalse(caller.comparisons_pass(result["comparisons"]))

    def test_portable_catalog_is_required_and_cannot_be_forged(self):
        for invalid in (
            {"availability": "UNAVAILABLE"},
            {"availability": "AVAILABLE", "sourceSha256": "a" * 64,
             "restoredSha256": "b" * 64, "exactBytesEqual": True},
            {"availability": "AVAILABLE", "sourceSha256": "a" * 64,
             "restoredSha256": "a" * 64, "exactBytesEqual": 1},
            {"availability": "AVAILABLE", "sourceSha256": "a" * 64,
             "restoredSha256": "a" * 64, "exactBytesEqual": False},
        ):
            result = complete()
            result["portableCatalog"] = invalid
            self.assertFalse(caller.candidate_complete(result))
        for value in (None, "INCOMPATIBLE_V0"):
            result = complete()
            result["catalogAcceptancePolicy"] = value
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

    def test_restore_list_witness_is_required_and_type_safe(self):
        for key, value in (("restoreList", None),
                           ("restoreList", {}),
                           ("restoreList", {"entryCount": 0, "foreignKeyCount": 0,
                                            "originalSha256": "a" * 64,
                                            "plannedSha256": "b" * 64}),
                           ("restoreList", {"entryCount": True, "foreignKeyCount": 1,
                                            "originalSha256": "a" * 64,
                                            "plannedSha256": "b" * 64})):
            result = complete()
            result[key] = value
            self.assertFalse(caller.candidate_complete(result))

    def test_restore_list_staging_is_private_and_install_failure_is_closed(self):
        self.assertEqual(caller.RESTORE_LIST_PATH, "/run/coglatas-recovery.list")
        listing = (b"; Synthetic archive\n"
                   b"1; 10 0 TABLE public sample postgres\n"
                   b"2; 11 0 FK CONSTRAINT public sample sample_fk postgres\n")
        for installed_ok in (False, True):
            with self.subTest(installed_ok=installed_ok):
                calls = []

                def runner(args, **kwargs):
                    calls.append(args)
                    if args[-2:] == ["pg_restore", "--list"]:
                        self.assertEqual(kwargs["input"], b"synthetic archive")
                        return subprocess.CompletedProcess(args, 0, listing, b"")
                    if args[0] == "node" and args[-1] == "plan":
                        self.assertEqual(kwargs["input"], listing)
                        return subprocess.CompletedProcess(args, 0, listing, b"")
                    self.assertEqual(args[-3:-1], ["/bin/sh", "-c"])
                    self.assertEqual(kwargs["input"], listing)
                    installer = args[-1]
                    self.assertIn("test -d /run; test ! -L /run", installer)
                    self.assertIn('test "$(stat -c %u:%a /run)" = 0:755', installer)
                    self.assertIn("(set -C; cat > /run/coglatas-recovery.list)", installer)
                    self.assertIn('test "$(stat -c %u:%a /run/coglatas-recovery.list)" = 0:600', installer)
                    self.assertIn("sha256sum /run/coglatas-recovery.list", installer)
                    self.assertNotIn("/tmp/", installer)
                    digest = caller.support.sha(listing)
                    return subprocess.CompletedProcess(
                        args, 0 if installed_ok else 13,
                        (digest + "  " + caller.RESTORE_LIST_PATH + "\n").encode()
                        if installed_ok else b"", b"")

                with tempfile.TemporaryDirectory() as directory:
                    (Path(directory) / "database.dump").write_bytes(b"synthetic archive")
                    result = {}
                    with patch.object(caller.subprocess, "run", side_effect=runner):
                        if installed_ok:
                            caller.prepare_fk_restore_list(
                                SOURCE, TARGET, Path(directory), result, {})
                        else:
                            with self.assertRaises(caller.support.Rejected):
                                caller.prepare_fk_restore_list(
                                    SOURCE, TARGET, Path(directory), result, {})
                    self.assertEqual(len(calls), 3)
                    if installed_ok:
                        self.assertEqual(result["restoreList"]["entryCount"], 2)
                        self.assertEqual(result["restoreList"]["foreignKeyCount"], 1)
                    else:
                        self.assertNotIn("restoreList", result)

    def test_restore_list_admission_failure_never_starts_pg_restore(self):
        calls = []
        def runner(args, **kwargs):
            calls.append(args)
            return subprocess.CompletedProcess(args, 0, b"synthetic-dump", b"")
        def fail_list(*args):
            raise caller.support.Rejected("RESTORE_LIST_REJECTED")
        with tempfile.TemporaryDirectory() as directory:
            result = {}
            with self.assertRaises(caller.support.Rejected):
                caller.transfer_once(SOURCE, TARGET, Path(directory), result, {},
                                     runner=runner, before_restore=fail_list)
        self.assertEqual(len(calls), 1)
        self.assertIsNone(result["commands"]["restore"]["exitCode"])
        self.assertNotIn("restoreList", result)

    def test_exact_documented_commands_and_fresh_distinct_targets(self):
        commands = caller.logical_commands(SOURCE, TARGET)
        self.assertEqual(commands, {
            "dump": ["docker", "--host=unix:///var/run/docker.sock", "exec", SOURCE,
                     "pg_dump", "-U", "postgres", "-d", "recovery", "--format=custom",
                     "--no-owner", "--no-privileges"],
            "restore": ["docker", "--host=unix:///var/run/docker.sock", "exec", "-i", TARGET,
                        "pg_restore", "-U", "postgres", "-d", "recovery", "--no-owner",
                        "--no-privileges", "--exit-on-error",
                        "--use-list=/run/coglatas-recovery.list"]})
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


def named_index_bytes(rows):
    return ("\n".join(json.dumps(row) for row in rows) + "\n").encode()


class CatalogMetadataContracts(unittest.TestCase):
    def assert_closed(self, diagnostic):
        self.assertEqual(set(diagnostic), {"availability", "sourceCatalogSha256",
                         "restoredCatalogSha256", "schemaClassification", "interpretation",
                         "counts", "namedIndex"})
        self.assertEqual(diagnostic["namedIndex"], {"availability": "NOT_EXECUTED", "counts": {}})
        self.assertEqual(diagnostic["schemaClassification"], "UNCLASSIFIED")
        self.assertIn(diagnostic["interpretation"], {
            "UNAVAILABLE", "EXACT_CATALOG_BYTES", "RAW_TREE_FIELD_DIFFERENCES_PRESENT",
            "OTHER_OR_UNMATCHED_RECORDS", "SAME_RECORDS_DIFFERENT_BYTES"})
        self.assertEqual(diagnostic["interpretation"] == "UNAVAILABLE",
                         diagnostic["availability"] == "UNAVAILABLE")
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
        self.assertEqual(diagnostic["interpretation"], "EXACT_CATALOG_BYTES")
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
        self.assertEqual(diagnostic["interpretation"], "RAW_TREE_FIELD_DIFFERENCES_PRESENT")
        self.assertEqual(diagnostic["counts"]["EXACT_RECORDS"], 3)
        self.assertNotEqual(diagnostic["sourceCatalogSha256"], diagnostic["restoredCatalogSha256"])

    def test_either_index_raw_field_change_is_categorized(self):
        for field in (3, 4):
            left = catalog_rows()
            right = copy.deepcopy(left)
            right[1][field] = None
            diagnostic = self.diagnose(left, right)
            self.assertEqual(diagnostic["counts"]["INDEX_RAW_TREE_FIELDS_ONLY"], 1)
            self.assertEqual(diagnostic["interpretation"], "RAW_TREE_FIELD_DIFFERENCES_PRESENT")
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
            self.assertEqual(diagnostic["interpretation"], "OTHER_OR_UNMATCHED_RECORDS")
            self.assertEqual(diagnostic["counts"]["EXACT_RECORDS"], 3)

    def test_added_removed_and_duplicate_records_do_not_invent_matches(self):
        rows = catalog_rows()
        diagnostic = self.diagnose(rows, rows[:-1])
        self.assertEqual(diagnostic["counts"]["SOURCE_ONLY_RECORDS"], 1)
        self.assertEqual(diagnostic["interpretation"], "OTHER_OR_UNMATCHED_RECORDS")
        self.assertEqual(diagnostic["counts"]["RESTORED_ONLY_RECORDS"], 0)
        diagnostic = self.diagnose(rows[:-1], rows)
        self.assertEqual(diagnostic["counts"]["RESTORED_ONLY_RECORDS"], 1)
        self.assertEqual(diagnostic["interpretation"], "OTHER_OR_UNMATCHED_RECORDS")
        diagnostic = self.diagnose(rows + [rows[0]], rows)
        self.assertEqual(diagnostic["availability"], "UNAVAILABLE")
        self.assertEqual(diagnostic["counts"], {})

    def test_mixed_raw_tree_and_unmatched_records_are_not_misreported_as_raw_only(self):
        left = catalog_rows()
        right = copy.deepcopy(left)
        right[0][8] = "RAW_B"
        right[1][7] = "DIFFERENT_INDEX_DEFINITION"
        diagnostic = self.diagnose(left, right)
        self.assertEqual(diagnostic["counts"]["CONSTRAINT_RAW_TREE_FIELD_ONLY"], 1)
        self.assertEqual(diagnostic["counts"]["SOURCE_ONLY_RECORDS"], 1)
        self.assertEqual(diagnostic["counts"]["RESTORED_ONLY_RECORDS"], 1)
        self.assertEqual(diagnostic["interpretation"], "OTHER_OR_UNMATCHED_RECORDS")

    def test_generated_internal_trigger_name_drift_keeps_raw_failure_but_portable_acceptance(self):
        source = catalog_rows()
        source[2][3] = "RI_ConstraintTrigger_a_1024"
        source[2][5] = True
        restored = copy.deepcopy(source)
        restored[2][3] = "RI_ConstraintTrigger_a_2048"
        diagnostic = self.diagnose(source, restored)
        self.assertEqual(diagnostic["counts"]["SOURCE_ONLY_RECORDS"], 1)
        self.assertEqual(diagnostic["counts"]["RESTORED_ONLY_RECORDS"], 1)
        for side in ("SOURCE", "RESTORED"):
            self.assertEqual(diagnostic["counts"][f"{side}_ONLY_TRIGGER_RECORDS"], 1)
            self.assertEqual(diagnostic["counts"][f"{side}_ONLY_INTERNAL_TRIGGER_RECORDS"], 1)
            self.assertEqual(diagnostic["counts"][f"{side}_ONLY_RI_TRIGGER_RECORDS"], 1)
            self.assertEqual(diagnostic["counts"][f"{side}_ONLY_INDEX_RECORDS"], 0)
        self.assertEqual(diagnostic["interpretation"], "OTHER_OR_UNMATCHED_RECORDS")
        before = {name: b"identical" for name in caller.COMPARISONS}
        after = before.copy()
        before["catalog.txt"], after["catalog.txt"] = catalog_bytes(source), catalog_bytes(restored)
        result = complete()
        result["comparisons"] = caller.strict_comparisons(before, after)
        result["catalogDiagnostic"] = diagnostic
        self.assertFalse(caller.comparisons_pass(result["comparisons"]))
        # Main's source-owned projector independently authenticates RI identity,
        # trigger attributes and effective firing order before portable approval.
        self.assertTrue(caller.candidate_complete(result))

    def test_index_definition_and_trigger_flag_differences_keep_distinct_counts(self):
        source = catalog_rows()
        restored = copy.deepcopy(source)
        restored[1][7] = "DIFFERENT_INDEX_DEFINITION"
        restored[2][4] = "D"
        diagnostic = self.diagnose(source, restored)
        self.assertEqual(diagnostic["counts"]["SOURCE_ONLY_INDEX_RECORDS"], 1)
        self.assertEqual(diagnostic["counts"]["RESTORED_ONLY_INDEX_RECORDS"], 1)
        self.assertEqual(diagnostic["counts"]["OTHER_CHANGED_TRIGGER_RECORDS"], 1)
        self.assertEqual(diagnostic["counts"]["OTHER_CHANGED_RECORDS"], 1)
        self.assertEqual(diagnostic["counts"]["SOURCE_ONLY_RI_TRIGGER_RECORDS"], 0)
        self.assertEqual(diagnostic["counts"]["RESTORED_ONLY_RI_TRIGGER_RECORDS"], 0)

    def test_named_index_read_only_metadata_separates_definitions_and_raw_fields(self):
        # Real index names are private; only closed aggregate counts may escape.
        private = CANARY.decode()
        original = [
            [private, private, private + "1", "EXPR_A", None, True, True, "CREATE INDEX one"],
            [private, private, private + "2", None, "PRED_A", True, True, "CREATE INDEX two"],
            [private, private, private + "3", None, None, True, True, "CREATE INDEX three"],
            [private, private, private + "4", None, None, True, True, "CREATE INDEX four"],
        ]
        target = copy.deepcopy(original)
        target[0][3] = "EXPR_B"
        target[1][7] = "CREATE INDEX two modified"
        target[2][5] = False
        target.pop()
        target.append([private, private, private + "5", None, None, True, True,
                       "CREATE INDEX five"])
        diagnostic = caller.named_index_diagnostic(
            named_index_bytes(original), named_index_bytes(target))
        self.assertEqual(diagnostic["availability"], "AVAILABLE")
        self.assertEqual(diagnostic["counts"], {
            "SOURCE_INDEXES": 4, "RESTORED_INDEXES": 4,
            "SOURCE_ONLY_NAMES": 1, "RESTORED_ONLY_NAMES": 1,
            "SOURCE_ONLY_PG_TOAST_NAMES": 0, "RESTORED_ONLY_PG_TOAST_NAMES": 0,
            "EXACT_NAMED_INDEXES": 0, "RAW_TREE_ONLY_NAMED_INDEXES": 1,
            "DECOMPILED_SQL_CHANGED": 1, "FLAGS_CHANGED": 1, "OTHER_CHANGED": 0})
        self.assertNotIn(private, json.dumps(diagnostic))
        # Additional evidence cannot waive the original catalog mismatch.
        before = {name: b"identical" for name in caller.COMPARISONS}
        after = before.copy()
        after["catalog.txt"] = b"DIFFERENT"
        result = complete()
        result["comparisons"] = caller.strict_comparisons(before, after)
        result["catalogDiagnostic"] = caller.catalog_diagnostic(
            catalog_bytes(catalog_rows()), catalog_bytes(catalog_rows()))
        result["catalogDiagnostic"]["namedIndex"] = diagnostic
        self.assertFalse(caller.candidate_complete(result))

    def test_toast_index_identifier_changes_stay_closed_and_unmatched(self):
        source = [["pg_toast", "pg_toast_17101", "pg_toast_17101_index",
                   None, None, True, True, "CREATE INDEX source"]]
        target = [["pg_toast", "pg_toast_19300", "pg_toast_19300_index",
                   None, None, True, True, "CREATE INDEX target"]]
        diagnostic = caller.named_index_diagnostic(
            named_index_bytes(source), named_index_bytes(target))
        self.assertEqual(diagnostic["counts"]["SOURCE_ONLY_NAMES"], 1)
        self.assertEqual(diagnostic["counts"]["RESTORED_ONLY_NAMES"], 1)
        self.assertEqual(diagnostic["counts"]["SOURCE_ONLY_PG_TOAST_NAMES"], 1)
        self.assertEqual(diagnostic["counts"]["RESTORED_ONLY_PG_TOAST_NAMES"], 1)
        self.assertEqual(diagnostic["counts"]["DECOMPILED_SQL_CHANGED"], 0)
        self.assertNotIn("pg_toast_", json.dumps(diagnostic))

    def test_named_index_metadata_rejects_duplicate_and_malformed_records(self):
        private = CANARY.decode()
        record = [private, private, private, None, None, True, True, "CREATE INDEX"]
        good = named_index_bytes([record])
        bad = (b"", b"null", b"[1]", b"not-json", b"\\xff",
               named_index_bytes([record, record]),
               named_index_bytes([[private, private, private, None, None, 1, True, "CREATE INDEX"]]),
               named_index_bytes([[private, private, private, None, None, True, True]]))
        for invalid in bad:
            result = caller.named_index_diagnostic(good, invalid)
            self.assertEqual(result, {"availability": "UNAVAILABLE", "counts": {}})
            self.assertNotIn(private, json.dumps(result))

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
            self.assertEqual(diagnostic["interpretation"], "SAME_RECORDS_DIFFERENT_BYTES")
            before = {name: b"identical" for name in caller.COMPARISONS}
            after = before.copy()
            before["catalog.txt"], after["catalog.txt"] = left, right
            comparisons = caller.strict_comparisons(before, after)
            self.assertIs(comparisons["catalog.txt"]["exactBytesEqual"], False)
            result = complete()
            result["comparisons"], result["catalogDiagnostic"] = comparisons, diagnostic
            # The approved portable contract admits logically identical records
            # even when their former raw JSON serialization differs.
            self.assertTrue(caller.candidate_complete(result))
            self.assertFalse(caller.comparisons_pass(result["comparisons"]))

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
        self.assertEqual(result["catalogDiagnostic"]["interpretation"], "UNAVAILABLE")
        self.assertFalse(caller.candidate_complete(result))


if __name__ == "__main__":
    unittest.main()
