"""Adversarial controls for draft RLS operation reconciliation."""

import copy
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

import sec_arch_rls_matrix as matrix

SHA = "1" * 40
ASSEMBLY = "2" * 64


class RlsOperationReconciliationTests(unittest.TestCase):
    def setUp(self):
        self.inventory = {"schemaVersion": 1, "totalTableCount": 2,
                          "tables": [{"table": "synthetic_rows", "classification": "RLS_REQUIRED"},
                                     {"table": "synthetic_identity", "classification": "REQUIRES_OWNER_REVIEW"}]}
        operations = []
        for operation in matrix.OPERATIONS:
            for situation in matrix.SITUATIONS:
                mechanism, result, affected, sqlstate = "RLS_FILTER", "PASS", 0, None
                if situation == "sameScope":
                    mechanism, affected = "ALLOWED", 1
                elif situation == "unauthorizedRole":
                    mechanism, sqlstate = "GRANT_DENIAL", "42501"
                elif situation == "wrongOwnership" and operation != "UPDATE":
                    mechanism, result = "UNSUPPORTED_OPERATION", "UNVERIFIED"
                elif operation == "INSERT" or situation == "wrongOwnership":
                    mechanism, sqlstate = "RLS_WITH_CHECK", "42501"
                operations.append({"operation": operation, "situation": situation,
                                   "roleKind": "syntheticApplication", "expectedMechanism": mechanism,
                                   "observedMechanism": mechanism, "sqlState": sqlstate,
                                   "positiveControlAffectedRows": 1, "affectedRows": affected,
                                   "result": result, "reasonCode": "OwnershipReassignmentRequiresUpdate"
                                   if situation == "wrongOwnership" and operation != "UPDATE" else "SyntheticFixture"})
        self.receipt = {"schemaVersion": 1, "approval": "DRAFT", "candidateSha": SHA,
                        "testAssemblyDigest": ASSEMBLY, "ownerApproval": None,
                        "productRlsAppliedCount": 0, "applicationRoleEquivalence": "UNVERIFIED",
                        "workerRoleEquivalence": "UNVERIFIED", "executionScope": "ISOLATED_SYNTHETIC_MODEL_ROWS",
                        "tables": [{"table": "synthetic_rows", "operations": operations}]}

    def reconcile(self, receipt=None, inventory=None, candidate=SHA, assembly=ASSEMBLY):
        return matrix.reconcile(self.receipt if receipt is None else receipt,
                                self.inventory if inventory is None else inventory, candidate, assembly)

    def row(self, receipt, operation="INSERT", situation="crossTenant"):
        return next(row for row in receipt["tables"][0]["operations"]
                    if row["operation"] == operation and row["situation"] == situation)

    def test_positive_matrix_has_no_normative_or_owner_approval_credit(self):
        result = self.reconcile()
        self.assertEqual("PASS", result["observedMatrixOutcome"])
        self.assertEqual(21, result["resultCounts"]["PASS"])
        self.assertEqual(3, result["resultCounts"]["UNVERIFIED"])
        self.assertEqual("UNVERIFIED", result["approvedRlsScope"])
        self.assertIsNone(result["ownerApproval"])
        self.assertEqual("PRE-AVALONIA SEC-ARCH: BLOCKED", result["preAvaloniaVerdict"])

    def test_every_missing_table_and_operation_remains_unverified(self):
        for field in ("table", "operation"):
            receipt = copy.deepcopy(self.receipt)
            if field == "table":
                receipt["tables"].clear()
            else:
                receipt["tables"][0]["operations"].pop(1)
            with self.subTest(field=field):
                result = self.reconcile(receipt)
                self.assertEqual("UNVERIFIED", result["observedMatrixOutcome"])
                self.assertGreater(result["outstandingApplicableCellCount"], 0)

    def test_policy_denial_requires_positive_same_operation_and_nonempty_fixture(self):
        for mutation in ("zero-positive", "failed-positive", "missing-positive", "zero-affected"):
            receipt = copy.deepcopy(self.receipt)
            positive = self.row(receipt, situation="sameScope")
            if mutation == "zero-positive":
                self.row(receipt)["positiveControlAffectedRows"] = 0
            elif mutation == "failed-positive":
                positive["result"] = "UNVERIFIED"
            elif mutation == "missing-positive":
                receipt["tables"][0]["operations"].remove(positive)
            else:
                positive["affectedRows"] = 0
            with self.subTest(mutation=mutation):
                self.assertEqual("FAIL", self.reconcile(receipt)["observedMatrixOutcome"])

    def test_permission_trigger_constraint_and_error_cannot_impersonate_rls(self):
        for mechanism, sqlstate in (("GRANT_DENIAL", "42501"), ("TRIGGER_REJECTION", "P0001"),
                                    ("CONSTRAINT_REJECTION", "23514"), ("MISSING_FIXTURE", None),
                                    ("UNSUPPORTED_OPERATION", None), ("UNEXPECTED_ERROR", "XX000")):
            receipt = copy.deepcopy(self.receipt)
            row = self.row(receipt)
            row.update(observedMechanism=mechanism, expectedMechanism=mechanism, sqlState=sqlstate)
            with self.subTest(mechanism=mechanism):
                self.assertEqual("FAIL", self.reconcile(receipt)["observedMatrixOutcome"])

    def test_grant_denial_only_qualifies_explicit_unauthorized_role_cell(self):
        for operation in matrix.OPERATIONS:
            receipt = copy.deepcopy(self.receipt)
            row = self.row(receipt, operation, "unauthorizedRole")
            row.update(observedMechanism="RLS_WITH_CHECK", expectedMechanism="RLS_WITH_CHECK")
            with self.subTest(operation=operation):
                self.assertEqual("FAIL", self.reconcile(receipt)["observedMatrixOutcome"])

    def test_nonzero_foreign_rows_and_sqlstate_mismatch_cannot_pass(self):
        for field, value in (("affectedRows", 1), ("sqlState", "23514")):
            receipt = copy.deepcopy(self.receipt)
            self.row(receipt)[field] = value
            with self.subTest(field=field):
                self.assertEqual("FAIL", self.reconcile(receipt)["observedMatrixOutcome"])

    def test_original_failure_error_and_missing_fixture_stay_distinct(self):
        for outcome in ("FAIL", "ERROR", "UNVERIFIED"):
            receipt = copy.deepcopy(self.receipt)
            self.row(receipt)["result"] = outcome
            with self.subTest(outcome=outcome):
                self.assertEqual(outcome, self.reconcile(receipt)["observedMatrixOutcome"])

    def test_foreign_candidate_build_and_owner_claims_are_rejected(self):
        for field, value in (("candidateSha", "3" * 40), ("testAssemblyDigest", "4" * 64),
                             ("schemaVersion", True), ("approval", "APPROVED"),
                             ("ownerApproval", {"owner": "self", "approved": True}), ("productRlsAppliedCount", 1),
                             ("applicationRoleEquivalence", "PASS"), ("workerRoleEquivalence", "PASS")):
            receipt = copy.deepcopy(self.receipt)
            receipt[field] = value
            with self.subTest(field=field), self.assertRaises(matrix.MatrixError):
                self.reconcile(receipt)

    def test_duplicate_unclassified_and_unexpected_scope_is_rejected(self):
        for change in ("duplicate-inventory", "unclassified", "duplicate-table", "unknown-table", "duplicate-cell"):
            inventory, receipt = copy.deepcopy(self.inventory), copy.deepcopy(self.receipt)
            if change == "duplicate-inventory":
                inventory["tables"].append(inventory["tables"][0])
            elif change == "unclassified":
                inventory["tables"][0]["classification"] = "NOT_APPLICABLE"
            elif change == "duplicate-table":
                receipt["tables"].append(receipt["tables"][0])
            elif change == "unknown-table":
                receipt["tables"][0]["table"] = "synthetic_identity"
            else:
                receipt["tables"][0]["operations"].append(receipt["tables"][0]["operations"][0])
            with self.subTest(change=change), self.assertRaises(matrix.MatrixError):
                self.reconcile(receipt, inventory)

    def test_skipped_not_applicable_migration_roles_and_invalid_counts_are_rejected(self):
        for field, value in (("result", "SKIP"), ("result", "NOT_APPLICABLE"), ("roleKind", "migration"),
                             ("positiveControlAffectedRows", True), ("affectedRows", -1), ("situation", "excluded")):
            receipt = copy.deepcopy(self.receipt)
            self.row(receipt)[field] = value
            with self.subTest(field=field, value=value), self.assertRaises(matrix.MatrixError):
                self.reconcile(receipt)

    def test_real_cli_pins_inventory_and_preserves_receipt_and_existing_report(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            receipt, inventory, output = root / "matrix.json", root / "inventory.json", root / "report.json"
            receipt.write_text(json.dumps(self.receipt), encoding="utf-8")
            inventory.write_text(json.dumps(self.inventory), encoding="utf-8")
            original = receipt.read_bytes()
            command = [sys.executable, str(Path(matrix.__file__)), "--matrix", str(receipt), "--inventory", str(inventory),
                       "--inventory-digest", hashlib.sha256(inventory.read_bytes()).hexdigest(), "--candidate-sha", SHA,
                       "--test-assembly-digest", ASSEMBLY, "--output", str(output)]
            self.assertEqual(0, subprocess.run(command, capture_output=True).returncode)
            saved = output.read_bytes()
            self.assertEqual(1, subprocess.run(command, capture_output=True).returncode)
            self.assertEqual(saved, output.read_bytes())
            self.assertEqual(original, receipt.read_bytes())
            output.unlink()
            inventory.write_text("{}", encoding="utf-8")
            self.assertEqual(1, subprocess.run(command, capture_output=True).returncode)
            self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
