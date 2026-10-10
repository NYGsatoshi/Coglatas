"""Adversarial controls for draft RLS operation reconciliation."""

import copy
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
from types import SimpleNamespace

import sec_arch_rls_matrix as matrix
import sec_arch_rls_sources as sources

SHA = "1" * 40
ASSEMBLY = "2" * 64
ENVIRONMENT = {"dotnetVersion": "10.0.12", "npgsqlVersion": "10.0.3.0", "postgresVersion": "18.6",
               "fixture": "isolated-migrated-postgresql"}
ENVIRONMENT_DIGEST = hashlib.sha256(json.dumps(ENVIRONMENT, separators=(",", ":")).encode()).hexdigest()


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
                                   "roleKind": "syntheticUnauthorized" if situation == "unauthorizedRole" else "syntheticApplication",
                                   "databaseRole": "synthetic_denied" if situation == "unauthorizedRole" else "synthetic_app",
                                   "expectedMechanism": mechanism,
                                   "observedMechanism": mechanism, "sqlState": sqlstate,
                                   "positiveControlAffectedRows": 1, "affectedRows": affected,
                                   "result": result, "reasonCode": "OwnershipReassignmentRequiresUpdate"
                                   if situation == "wrongOwnership" and operation != "UPDATE" else "SyntheticFixture"})
        self.receipt = {"schemaVersion": 1, "approval": "DRAFT", "candidateSha": SHA,
                        "environment": ENVIRONMENT, "environmentFingerprint": ENVIRONMENT_DIGEST,
                        "roles": [{"roleKind": kind, "databaseRole": role, "isSuperuser": False, "bypassRls": False,
                                   "canCreateDb": False, "canCreateRole": False, "inheritsRoles": False,
                                   "membershipCount": 0, "protectedTableOwnershipCount": 0}
                                  for kind, role in (("syntheticApplication", "synthetic_app"),
                                                     ("syntheticUnauthorized", "synthetic_denied"))],
                        "testAssemblyDigest": ASSEMBLY, "ownerApproval": None,
                        "productRlsAppliedCount": 0, "applicationRoleEquivalence": "UNVERIFIED",
                        "workerRoleEquivalence": "UNVERIFIED", "executionScope": "ISOLATED_SYNTHETIC_MODEL_ROWS",
                        "tables": [{"table": "synthetic_rows", "operations": operations,
                                    "fixtureStatus": "SEEDED", "tenantIdentityKind": "UUID", "policyDigest": "3" * 64,
                                    "ownershipProbeKind": "TENANT_REASSIGNMENT",
                                    "verificationControls": {"permissivePolicyExposureRows": 1, "restoredCrossTenantRows": 0,
                                                             "revokedSelectMechanism": "GRANT_DENIAL", "revokedSelectSqlState": "42501",
                                                             "restoredSameScopeRows": 1, "forbiddenTruncateGrantDetected": True}}]}

    def reconcile(self, receipt=None, inventory=None, candidate=SHA, assembly=ASSEMBLY):
        return matrix.reconcile(self.receipt if receipt is None else receipt,
                                self.inventory if inventory is None else inventory, candidate, assembly, ENVIRONMENT_DIGEST)

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
        for mutation in ("zero-positive", "wrong-positive", "failed-positive", "missing-positive", "zero-affected"):
            receipt = copy.deepcopy(self.receipt)
            positive = self.row(receipt, situation="sameScope")
            if mutation == "zero-positive":
                self.row(receipt)["positiveControlAffectedRows"] = 0
            elif mutation == "wrong-positive":
                self.row(receipt)["positiveControlAffectedRows"] = 2
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
            command = [sys.executable, "-B", str(Path(matrix.__file__)), "--matrix", str(receipt), "--inventory", str(inventory),
                       "--inventory-digest", hashlib.sha256(inventory.read_bytes()).hexdigest(), "--candidate-sha", SHA,
                       "--test-assembly-digest", ASSEMBLY, "--environment-fingerprint", ENVIRONMENT_DIGEST,
                       "--output", str(output)]
            self.assertEqual(0, subprocess.run(command, capture_output=True).returncode)
            saved = output.read_bytes()
            self.assertEqual(1, subprocess.run(command, capture_output=True).returncode)
            self.assertEqual(saved, output.read_bytes())
            self.assertEqual(original, receipt.read_bytes())
            output.unlink()
            inventory.write_text("{}", encoding="utf-8")
            self.assertEqual(1, subprocess.run(command, capture_output=True).returncode)
            self.assertFalse(output.exists())

    def test_observed_null_reason_for_normal_operation_is_supported(self):
        receipt = copy.deepcopy(self.receipt)
        for row in receipt["tables"][0]["operations"]:
            if row["result"] == "PASS": row["reasonCode"] = None
        self.assertEqual("PASS", self.reconcile(receipt)["observedMatrixOutcome"])

    def test_broad_role_missing_role_or_environment_drift_cannot_qualify(self):
        for mutation in ("super", "bypass", "owner", "membership", "duplicate", "missing", "environment", "fingerprint"):
            receipt = copy.deepcopy(self.receipt)
            if mutation in ("super", "bypass"):
                receipt["roles"][0]["isSuperuser" if mutation == "super" else "bypassRls"] = True
            elif mutation in ("owner", "membership"):
                receipt["roles"][0]["protectedTableOwnershipCount" if mutation == "owner" else "membershipCount"] = 1
            elif mutation == "duplicate": receipt["roles"][1] = receipt["roles"][0]
            elif mutation == "missing": receipt.pop("roles")
            elif mutation == "environment": receipt["environment"]["postgresVersion"] = "other"
            else: receipt["environmentFingerprint"] = "5" * 64
            with self.subTest(mutation=mutation), self.assertRaises(matrix.MatrixError):
                self.reconcile(receipt)

    def test_missing_mutation_controls_seed_or_parent_semantics_fail(self):
        for mutation in ("no-controls", "empty-exposure", "foreign-restored", "wrong-revoke", "no-positive", "broad-grant", "no-seed", "parent"):
            receipt = copy.deepcopy(self.receipt)
            table = receipt["tables"][0]
            if mutation == "no-controls": table.pop("verificationControls")
            elif mutation == "no-seed": table["fixtureStatus"] = "MISSING"
            elif mutation == "parent": table["tenantIdentityKind"] = "PARENT"
            else:
                field, value = {"empty-exposure": ("permissivePolicyExposureRows", 0),
                                "foreign-restored": ("restoredCrossTenantRows", 1),
                                "wrong-revoke": ("revokedSelectMechanism", "RLS_WITH_CHECK"),
                                "no-positive": ("restoredSameScopeRows", 0),
                                "broad-grant": ("forbiddenTruncateGrantDetected", False)}[mutation]
                table["verificationControls"][field] = value
            with self.subTest(mutation=mutation), self.assertRaises(matrix.MatrixError):
                self.reconcile(receipt)

    def test_each_operation_requires_its_observed_concrete_role(self):
        for situation in matrix.SITUATIONS:
            for mutation in ("missing", "foreign", "opposite", "root", "invalid", "null"):
                receipt = copy.deepcopy(self.receipt)
                row = self.row(receipt, situation=situation)
                if mutation == "missing": row.pop("databaseRole")
                else:
                    row["databaseRole"] = {"foreign": "other_synthetic_role", "opposite":
                        "synthetic_app" if situation == "unauthorizedRole" else "synthetic_denied",
                        "root": "postgres", "invalid": "role with whitespace", "null": None}[mutation]
                with self.subTest(situation=situation, mutation=mutation), self.assertRaises(matrix.MatrixError):
                    self.reconcile(receipt)


class RlsSourceBindingTests(unittest.TestCase):
    """Real file-byte controls; git process responses are isolated unit boundaries."""

    def setUp(self):
        fixture = RlsOperationReconciliationTests()
        fixture.setUp()
        self.receipt, self.inventory = fixture.receipt, fixture.inventory
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.checkout = Path(self.temporary.name).resolve()
        self.relative = "src/Coglatas.Infrastructure/Persistence/Migrations/20261010000000_SyntheticGuard.cs"
        self.path = self.checkout / self.relative
        self.path.parent.mkdir(parents=True)
        self.path.write_bytes(b"// Synthetic source byte identity\n")
        self.guard = {"triggerName": "synthetic_guard", "enabled": "O", "commandMask": 27,
                      "triggerDefinitionDigest": "4" * 64, "functionSchema": "public",
                      "functionName": "synthetic_immutable_guard", "identityArguments": "",
                      "functionDefinitionDigest": "5" * 64}
        self.constraint = {"constraintName": "synthetic_inbound_fk", "kind": "f",
                           "constraintTable": "synthetic_child", "referencedTable": "synthetic_rows",
                           "relationship": "REFERENCING", "validated": True, "deferrable": False,
                           "deferred": False, "definitionDigest": "6" * 64}
        self.native = {"table": "synthetic_rows", "guards": [self.guard], "constraints": [self.constraint]}
        self.redigest(self.native)
        self.declaration = {"operation": "UPDATE", "reasonCode": "SyntheticImmutableSource",
                            "classification": "SOURCE_BLOCKED_DIRECT_MUTATION", "alternateLifecycle": None,
                            "sources": [{"path": self.relative, "digest": hashlib.sha256(self.path.read_bytes()).hexdigest()}],
                            "guard": self.guard, "constraint": None}
        table = self.receipt["tables"][0]
        table["sourceSchemaIdentity"] = copy.deepcopy(self.native)
        table["sourceUnavailableOperations"] = [copy.deepcopy(self.declaration)]
        for row in table["operations"]:
            if row["operation"] == "UPDATE":
                row.update(result="UNVERIFIED", affectedRows=0, positiveControlAffectedRows=0,
                           reasonCode="SyntheticImmutableSource")
                if row["situation"] == "sameScope":
                    row.update(expectedMechanism="TRIGGER_REJECTION", observedMechanism="TRIGGER_REJECTION", sqlState="42501",
                               sourceRejectionIdentity={"nativeConstraintName": None, "guardFunctionSchema": None,
                                                        "guardFunctionName": self.guard["functionName"]})
        identity_native = {"table": "synthetic_identity", "guards": [], "constraints": []}
        self.redigest(identity_native)
        self.reference = {"schemaVersion": 1, "candidateSha": SHA, "testAssemblyDigest": ASSEMBLY,
                          "approval": "DRAFT", "ownerApproval": None, "environment": ENVIRONMENT,
                          "environmentFingerprint": ENVIRONMENT_DIGEST, "executionScope": "ISOLATED_MIGRATED_SCHEMA_REFERENCE",
                          "productRlsAppliedCount": 0, "tables": [
                              {"table": "synthetic_rows", "sourceSchemaIdentity": copy.deepcopy(self.native),
                               "sourceUnavailableOperations": [copy.deepcopy(self.declaration)]},
                              {"table": "synthetic_identity", "sourceSchemaIdentity": identity_native,
                               "sourceUnavailableOperations": []}]}

    @staticmethod
    def redigest(native):
        canonical = {"guards": [{key: value[key] for key in sources.GUARD_KEYS} for value in native["guards"]],
                     "constraints": [{key: value[key] for key in sources.CONSTRAINT_KEYS} for value in native["constraints"]]}
        native["schemaDigest"] = hashlib.sha256(json.dumps(canonical, separators=(",", ":"), ensure_ascii=False).encode()).hexdigest()

    def reconcile(self, receipt=None, reference=None, head=SHA, clean=""):
        with patch.object(sources.subprocess, "run", side_effect=[SimpleNamespace(stdout=head + "\n"),
                                                                  SimpleNamespace(stdout=clean)]) as process:
            result = matrix.reconcile(self.receipt if receipt is None else receipt, self.inventory, SHA, ASSEMBLY,
                                      ENVIRONMENT_DIGEST, self.reference if reference is None else reference, self.checkout)
            self.assertEqual([["git", "-C", str(self.checkout), "rev-parse", "HEAD"],
                              ["git", "-C", str(self.checkout), "status", "--porcelain", "--untracked-files=all"]],
                             [call.args[0] for call in process.call_args_list])
            return result

    def test_source_positive_binds_real_bytes_without_qualifying_blocked_operations(self):
        result = self.reconcile()
        self.assertEqual("PASS", result["sourceBindingOutcome"])
        self.assertEqual(1, result["sourceBoundBlockedDirectOperationCount"])
        self.assertEqual(0, result["unboundSourceRejectionCount"])
        self.assertEqual(6, result["outstandingApplicableCellCount"])
        self.assertEqual("UNVERIFIED", result["observedMatrixOutcome"])
        self.assertEqual("PRE-AVALONIA SEC-ARCH: BLOCKED", result["preAvaloniaVerdict"])
        self.assertIsNone(result["ownerApproval"])

    def applicable_fixture(self, guarded=False):
        receipt, reference = self.guarded_fixture() if guarded else (copy.deepcopy(self.receipt), copy.deepcopy(self.reference))
        table = receipt["tables"][0]
        for row in table["operations"]:
            if row["operation"] == "UPDATE" and row["situation"] == "wrongOwnership":
                row.update(observedMechanism="TRIGGER_REJECTION", sqlState="P0001",
                           sourceRejectionIdentity={"nativeConstraintName": None, "guardFunctionSchema": "public",
                                                    "guardFunctionName": self.guard["functionName"]})
        for source_table in reference["tables"]:
            native = source_table["sourceSchemaIdentity"]
            source_table.setdefault("sourceGuardedProbes", [])
            blocked = sources.dispositions(source_table["sourceUnavailableOperations"], native, self.checkout)
            probes = sources.guarded_probes(source_table["sourceGuardedProbes"], native, self.checkout)
            source_table["sourceOperationDispositions"] = list(sources.applicable_dispositions(native, blocked, probes).values())
        table["sourceOperationDispositions"] = copy.deepcopy(reference["tables"][0]["sourceOperationDispositions"])
        return receipt, reference

    def test_all_applicable_source_dispositions_bind_dependencies_and_real_bytes_without_qualification(self):
        receipt, reference = self.applicable_fixture(guarded=True)
        result = self.reconcile(receipt, reference)
        self.assertEqual("PASS", result["applicableDispositionOutcome"])
        self.assertEqual(7, result["sourceBoundApplicableDispositionCount"])
        self.assertEqual(0, result["missingApplicableSourceDispositionCount"])
        self.assertEqual(7, result["outstandingApplicableCellCount"])
        self.assertEqual("UNVERIFIED", result["observedMatrixOutcome"])
        self.assertEqual("PRE-AVALONIA SEC-ARCH: BLOCKED", result["preAvaloniaVerdict"])

    def test_missing_applicable_dispositions_retain_exact_historical_or_partial_gap_counts(self):
        for mode, missing in (("historical", 7), ("reference_missing", 7), ("receipt_missing", 7), ("partial", 1)):
            receipt, reference = self.applicable_fixture(guarded=True)
            if mode in {"historical", "reference_missing"}:
                for table in reference["tables"]: table.pop("sourceOperationDispositions")
            if mode in {"historical", "receipt_missing"}: receipt["tables"][0].pop("sourceOperationDispositions")
            if mode == "partial":
                for table in (reference["tables"][0], receipt["tables"][0]): table["sourceOperationDispositions"].pop()
            with self.subTest(mode=mode):
                result = self.reconcile(receipt, reference)
                self.assertEqual("UNVERIFIED", result["applicableDispositionOutcome"])
                self.assertEqual(missing, result["missingApplicableSourceDispositionCount"])
                self.assertEqual(7 - missing, result["sourceBoundApplicableDispositionCount"])
                self.assertEqual(7, result["outstandingApplicableCellCount"])

    def test_applicable_dispositions_reject_forged_semantics_or_owner_authority_even_in_both_inputs(self):
        for field, forged in (("approvedApplicability", "NOT_APPLICABLE"), ("rlsQualification", "PASS"),
                              ("authorityDisposition", "OWNER_APPROVED"), ("nativeOperatorAvailability", "AVAILABLE"),
                              ("nativeSourceDigest", "0" * 64), ("dependencyKind", "NATIVE_BEFORE_RLS_PRECHECK"),
                              ("sourceOperation", "DELETE"), ("sourceSituation", "sameScope"),
                              ("expectedObservedMechanism", "RLS_WITH_CHECK"), ("roleKind", "operationalApplication"),
                              ("reasonCode", "NoSecurityRequirement")):
            receipt, reference = self.applicable_fixture()
            for table in (receipt["tables"][0], reference["tables"][0]): table["sourceOperationDispositions"][0][field] = forged
            with self.subTest(field=field), self.assertRaises(matrix.MatrixError): self.reconcile(receipt, reference)

    def test_applicable_dispositions_reject_unknown_or_duplicate_cells_and_unsupported_scope(self):
        for mode in ("duplicate", "unknown", "unsupported", "extra_field"):
            receipt, reference = self.applicable_fixture()
            for table in (receipt["tables"][0], reference["tables"][0]):
                rows = table["sourceOperationDispositions"]
                if mode == "duplicate": rows.append(copy.deepcopy(rows[0]))
                elif mode == "unknown": rows[0]["situation"] = "unreviewed_operation"
                elif mode == "unsupported": rows[0]["operation"] = "SELECT"
                else: rows[0]["approval"] = "APPROVED"
            with self.subTest(mode=mode), self.assertRaises(matrix.MatrixError): self.reconcile(receipt, reference)

    def test_applicable_source_cells_cannot_hide_zero_positive_native_prechecks_or_relabel_denials(self):
        for situation, field, value in (("crossTenant", "positiveControlAffectedRows", 0),
                                        ("crossTenant", "observedMechanism", "RLS_WITH_CHECK"),
                                        ("crossTenant", "result", "PASS"),
                                        ("sameScope", "positiveControlAffectedRows", 1)):
            receipt, reference = self.applicable_fixture(guarded=True)
            operation = "INSERT" if situation == "crossTenant" else "UPDATE"
            row = next(row for row in receipt["tables"][0]["operations"] if row["operation"] == operation and row["situation"] == situation)
            row[field] = value
            with self.subTest(situation=situation, field=field):
                result = self.reconcile(receipt, reference)
                self.assertEqual("FAIL", result["applicableDispositionOutcome"])
                self.assertGreater(result["missingApplicableSourceDispositionCount"], 0)

    def test_applicable_source_bindings_still_reject_current_source_bytes_and_same_name_native_weakening(self):
        receipt, reference = self.applicable_fixture()
        self.path.write_bytes(b"// Same source name with weakened behavior\n")
        with self.assertRaises(matrix.MatrixError): self.reconcile(receipt, reference)
        self.path.write_bytes(b"// Synthetic source byte identity\n")
        native = receipt["tables"][0]["sourceSchemaIdentity"]
        native["guards"][0]["functionDefinitionDigest"] = "7" * 64
        self.redigest(native)
        receipt["tables"][0]["sourceUnavailableOperations"][0]["guard"] = copy.deepcopy(native["guards"][0])
        result = self.reconcile(receipt, reference)
        self.assertEqual("FAIL", result["applicableDispositionOutcome"])
        self.assertEqual(0, result["sourceBoundApplicableDispositionCount"])

    def test_applicable_cells_require_the_exact_native_rejection_and_actual_sqlstate_mechanism(self):
        for situation, field, value in (("wrongOwnership", "sourceRejectionIdentity", {
                "nativeConstraintName": None, "guardFunctionSchema": "public", "guardFunctionName": "unrelated_guard"}),
                ("wrongOwnership", "sqlState", None), ("crossTenant", "sqlState", "P0001"),
                ("unauthorizedRole", "sqlState", "23514"), ("crossTenant", "sourceRejectionIdentity", {
                    "nativeConstraintName": None, "guardFunctionSchema": "public", "guardFunctionName": self.guard["functionName"]})):
            receipt, reference = self.applicable_fixture()
            row = next(row for row in receipt["tables"][0]["operations"] if row["operation"] == "UPDATE" and row["situation"] == situation)
            row[field] = value
            with self.subTest(situation=situation, field=field):
                result = self.reconcile(receipt, reference)
                self.assertEqual("FAIL", result["applicableDispositionOutcome"])
                self.assertEqual(5, result["sourceBoundApplicableDispositionCount"])
                self.assertEqual(1, result["missingApplicableSourceDispositionCount"])

    def test_native_restrict_constraint_state_is_unavailable_and_never_rls_denial_credit(self):
        receipt, reference = self.applicable_fixture()
        declaration = copy.deepcopy(self.declaration)
        declaration.update(operation="DELETE", reasonCode="SyntheticRetainedChild", guard=None, constraint=copy.deepcopy(self.constraint))
        for table in (receipt["tables"][0], reference["tables"][0]):
            table["sourceUnavailableOperations"].append(copy.deepcopy(declaration))
            native = table["sourceSchemaIdentity"]
            table["sourceOperationDispositions"] = list(sources.applicable_dispositions(native,
                sources.dispositions(table["sourceUnavailableOperations"], native), {}).values())
        for row in receipt["tables"][0]["operations"]:
            if row["operation"] != "DELETE" or row["situation"] == "wrongOwnership": continue
            row.update(result="UNVERIFIED", positiveControlAffectedRows=0, affectedRows=0, reasonCode="SyntheticRetainedChild")
            if row["situation"] == "sameScope":
                row.update(observedMechanism="CONSTRAINT_REJECTION", sqlState="23001", sourceRejectionIdentity={
                    "nativeConstraintName": self.constraint["constraintName"], "guardFunctionSchema": None, "guardFunctionName": None})
        result = self.reconcile(receipt, reference)
        self.assertEqual("PASS", result["applicableDispositionOutcome"])
        self.assertEqual(11, result["sourceBoundApplicableDispositionCount"])
        self.assertEqual(11, result["outstandingApplicableCellCount"])
        self.assertEqual("UNVERIFIED", result["observedMatrixOutcome"])
        positive = next(row for row in receipt["tables"][0]["operations"] if row["operation"] == "DELETE" and row["situation"] == "sameScope")
        for sqlstate in ("23514", "42501", None):
            positive["sqlState"] = sqlstate
            with self.subTest(sqlstate=sqlstate): self.assertEqual("FAIL", self.reconcile(receipt, reference)["applicableDispositionOutcome"])

    def guarded_fixture(self):
        receipt, reference = copy.deepcopy(self.receipt), copy.deepcopy(self.reference)
        guard = copy.deepcopy(self.guard)
        guard["commandMask"] = 31
        for table in (receipt["tables"][0], reference["tables"][0]):
            table["sourceSchemaIdentity"]["guards"] = [copy.deepcopy(guard)]
            self.redigest(table["sourceSchemaIdentity"])
            table["sourceUnavailableOperations"][0]["guard"] = copy.deepcopy(guard)
        probe = {"operation": "INSERT", "situation": "crossTenant", "semanticReason": "ParentVisibilityOrCurrentStatePrecheck",
                 "classification": "SOURCE_PRECHECK_BEFORE_RLS_WITH_CHECK", "rlsQualification": "UNVERIFIED",
                 "authorityDisposition": "ConcreteParentLookupAuthorityRequiresOwnerReview", "parentTables": ["synthetic_identity"],
                 "source": copy.deepcopy(self.declaration["sources"][0]), "guard": guard}
        # This actual enabled native identity is already independently supplied by the source fixture.
        for table in (receipt["tables"][0], reference["tables"][0]): table["sourceGuardedProbes"] = [copy.deepcopy(probe)]
        row = next(row for row in receipt["tables"][0]["operations"] if row["operation"] == "INSERT" and row["situation"] == "crossTenant")
        row.update(result="UNVERIFIED", observedMechanism="TRIGGER_REJECTION", reasonCode="SourceMutationGuard", sqlState="P0001",
                   sourceRejectionIdentity={"nativeConstraintName": None, "guardFunctionSchema": "public", "guardFunctionName": self.guard["functionName"]})
        return receipt, reference

    def test_guarded_probes_bind_independent_native_and_real_source_bytes_without_rls_credit(self):
        receipt, reference = self.guarded_fixture()
        result = self.reconcile(receipt, reference)
        self.assertEqual("PASS", result["sourceBindingOutcome"])
        self.assertEqual(1, result["sourceBoundGuardedProbeCount"])
        self.assertEqual(0, result["missingGuardedProbeDispositionCount"])
        self.assertEqual(7, result["outstandingApplicableCellCount"])
        self.assertEqual("UNVERIFIED", result["observedMatrixOutcome"])
        self.assertEqual("PRE-AVALONIA SEC-ARCH: BLOCKED", result["preAvaloniaVerdict"])
        reference["tables"][0].pop("sourceGuardedProbes")
        receipt["tables"][0].pop("sourceGuardedProbes")
        historical = self.reconcile(receipt, reference)
        self.assertEqual("UNVERIFIED", historical["sourceBindingOutcome"])
        self.assertEqual(1, historical["missingGuardedProbeDispositionCount"])

    def test_guarded_probes_reject_forged_authority_semantics_dependencies_or_observations(self):
        for field, value in (("rlsQualification", "PASS"), ("authorityDisposition", "APPROVED"),
                             ("semanticReason", "ImmutableBoundIdentity"), ("classification", "RLS_WITH_CHECK"),
                             ("parentTables", ["unregistered_parent"]), ("operation", "DELETE"),
                             ("situation", "sameScope"), ("source", {"path": self.relative, "digest": "0" * 64})):
            receipt, reference = self.guarded_fixture()
            for table in (receipt["tables"][0], reference["tables"][0]): table["sourceGuardedProbes"][0][field] = value
            with self.subTest(field=field), self.assertRaises(matrix.MatrixError): self.reconcile(receipt, reference)
        for field, value in (("result", "PASS"), ("reasonCode", "ApprovedNotApplicable"), ("positiveControlAffectedRows", 0),
                             ("affectedRows", 1), ("sourceRejectionIdentity", {"nativeConstraintName": None,
                                "guardFunctionSchema": "public", "guardFunctionName": "different_function"})):
            receipt, reference = self.guarded_fixture()
            row = next(row for row in receipt["tables"][0]["operations"] if row["operation"] == "INSERT" and row["situation"] == "crossTenant")
            row[field] = value
            with self.subTest(field=field):
                result = self.reconcile(receipt, reference)
                self.assertEqual("FAIL", result["sourceBindingOutcome"])
                self.assertEqual(0, result["sourceBoundGuardedProbeCount"])

    def test_guarded_probes_cannot_replace_independent_native_definitions_or_current_source(self):
        receipt, reference = self.guarded_fixture()
        receipt["tables"][0]["sourceGuardedProbes"][0]["semanticReason"] = "different_semantics"
        with self.assertRaises(matrix.MatrixError): self.reconcile(receipt, reference)
        receipt, reference = self.guarded_fixture()
        reference["tables"][0]["sourceGuardedProbes"][0]["guard"]["functionDefinitionDigest"] = "7" * 64
        with self.assertRaises(matrix.MatrixError): self.reconcile(receipt, reference)
        self.path.write_bytes(b"// Semantic source change retaining the same function name\n")
        with self.assertRaises(matrix.MatrixError): self.reconcile(*self.guarded_fixture())

    def test_source_and_candidate_reference_are_independent_and_complete(self):
        for field, value in (("candidateSha", "7" * 40), ("environmentFingerprint", "8" * 64),
                             ("approval", "APPROVED"), ("ownerApproval", {"approved": True}),
                             ("productRlsAppliedCount", True), ("executionScope", "PRODUCT"),
                             ("testAssemblyDigest", "wrong"), ("environment", {"fixture": "forged"}),
                             ("unknownApproval", True)):
            reference = copy.deepcopy(self.reference)
            reference[field] = value
            with self.subTest(field=field), self.assertRaises(matrix.MatrixError):
                self.reconcile(reference=reference)
        for mutation in ("missing", "duplicate", "extra"):
            reference = copy.deepcopy(self.reference)
            if mutation == "missing": reference["tables"].pop()
            elif mutation == "duplicate": reference["tables"].append(reference["tables"][0])
            else: reference["tables"][0]["table"] = "unclassified_rows"
            with self.subTest(mutation=mutation), self.assertRaises(matrix.MatrixError):
                self.reconcile(reference=reference)

    def test_wrong_git_candidate_or_dirty_checkout_cannot_bind_source(self):
        for head, clean in (("9" * 40, ""), (SHA, " M source.cs\n"), (SHA, "?? injected.cs\n")):
            with self.subTest(head=head, clean=clean), self.assertRaises(matrix.MatrixError):
                self.reconcile(head=head, clean=clean)

    def test_actual_changed_source_file_or_path_escape_is_rejected(self):
        original = self.path.read_bytes()
        self.path.write_bytes(original + b"// weakened source\n")
        with self.assertRaises(matrix.MatrixError): self.reconcile()
        self.path.write_bytes(original)
        for path in ("../outside.cs", "src/../source.cs", "/tmp/source.cs", self.relative.replace("/", "\\")):
            reference = copy.deepcopy(self.reference)
            reference["tables"][0]["sourceUnavailableOperations"][0]["sources"][0]["path"] = path
            with self.subTest(path=path), self.assertRaises(matrix.MatrixError): self.reconcile(reference=reference)
        self.path.unlink()
        with self.assertRaises(matrix.MatrixError): self.reconcile()

    def test_same_names_do_not_bind_changed_native_guard_or_constraint(self):
        for field in ("functionDefinitionDigest", "triggerDefinitionDigest", "enabled", "commandMask", "definitionDigest"):
            receipt = copy.deepcopy(self.receipt)
            table = receipt["tables"][0]
            native = table["sourceSchemaIdentity"]
            if field == "definitionDigest": native["constraints"][0][field] = "a" * 64
            else:
                native["guards"][0][field] = "a" * 64 if field.endswith("Digest") else "A" if field == "enabled" else 31
                table["sourceUnavailableOperations"][0]["guard"] = copy.deepcopy(native["guards"][0])
            self.redigest(native)
            with self.subTest(field=field):
                result = self.reconcile(receipt)
                self.assertEqual("FAIL", result["sourceBindingOutcome"])
                self.assertEqual("FAIL", result["observedMatrixOutcome"])
                self.assertEqual(0, result["sourceBoundBlockedDirectOperationCount"])

    def test_native_digest_duplicate_guard_disabled_guard_and_false_applicability_are_rejected(self):
        for mutation in ("digest", "duplicate", "disabled", "wrong-command", "false-retirement", "bool-mask", "constraint-relation"):
            reference = copy.deepcopy(self.reference)
            native, declaration = reference["tables"][0]["sourceSchemaIdentity"], reference["tables"][0]["sourceUnavailableOperations"][0]
            if mutation == "digest": native["schemaDigest"] = "b" * 64
            elif mutation == "duplicate": native["guards"].append(native["guards"][0]); self.redigest(native)
            elif mutation == "disabled": native["guards"][0]["enabled"] = "D"; declaration["guard"]["enabled"] = "D"; self.redigest(native)
            elif mutation == "wrong-command": native["guards"][0]["commandMask"] = 9; declaration["guard"]["commandMask"] = 9; self.redigest(native)
            elif mutation == "false-retirement": declaration["classification"] = "NOT_APPLICABLE"
            elif mutation == "bool-mask": native["guards"][0]["commandMask"] = True; self.redigest(native)
            else: native["constraints"][0]["referencedTable"] = "synthetic_identity"; self.redigest(native)
            with self.subTest(mutation=mutation), self.assertRaises(matrix.MatrixError): self.reconcile(reference=reference)

    def test_observed_native_rejection_must_bind_enabled_function_and_exact_declared_guard(self):
        for identity in (None, {"nativeConstraintName": "wrong", "guardFunctionSchema": None, "guardFunctionName": "synthetic_immutable_guard"},
                         {"nativeConstraintName": None, "guardFunctionSchema": "foreign", "guardFunctionName": "synthetic_immutable_guard"},
                         {"nativeConstraintName": None, "guardFunctionSchema": None, "guardFunctionName": "other_guard"}):
            receipt = copy.deepcopy(self.receipt)
            next(row for row in receipt["tables"][0]["operations"] if row["operation"] == "UPDATE" and row["situation"] == "sameScope")["sourceRejectionIdentity"] = identity
            with self.subTest(identity=identity):
                result = self.reconcile(receipt)
                self.assertEqual("FAIL", result["sourceBindingOutcome"])
                self.assertEqual(1, result["unboundSourceRejectionCount"])
                self.assertEqual(0, result["sourceBoundBlockedDirectOperationCount"])

    def test_missing_schema_retains_unbound_rejection_and_all_applicable_gaps(self):
        receipt = copy.deepcopy(self.receipt)
        del receipt["tables"][0]["sourceSchemaIdentity"]
        result = self.reconcile(receipt)
        self.assertEqual("UNVERIFIED", result["sourceBindingOutcome"])
        self.assertEqual(1, result["missingSourceIdentityCount"])
        self.assertEqual(1, result["unboundSourceRejectionCount"])
        self.assertEqual(6, result["outstandingApplicableCellCount"])

    def test_constraint_direct_rejection_binds_real_source_and_referencing_constraint(self):
        receipt, reference = copy.deepcopy(self.receipt), copy.deepcopy(self.reference)
        declaration = {**copy.deepcopy(self.declaration), "operation": "DELETE", "reasonCode": "SyntheticPersistentParent",
                       "guard": None, "constraint": copy.deepcopy(self.constraint), "alternateLifecycle": "ReviewedLifecyclePending"}
        receipt["tables"][0]["sourceUnavailableOperations"].append(declaration)
        reference["tables"][0]["sourceUnavailableOperations"].append(copy.deepcopy(declaration))
        for row in receipt["tables"][0]["operations"]:
            if row["operation"] != "DELETE": continue
            row.update(affectedRows=0, positiveControlAffectedRows=0, result="UNVERIFIED")
            if row["situation"] == "wrongOwnership": continue
            row["reasonCode"] = declaration["reasonCode"]
            if row["situation"] == "sameScope":
                row.update(observedMechanism="CONSTRAINT_REJECTION", sqlState="23503",
                           sourceRejectionIdentity={"nativeConstraintName": self.constraint["constraintName"],
                                                    "guardFunctionSchema": None, "guardFunctionName": None})
        result = self.reconcile(receipt, reference)
        self.assertEqual("PASS", result["sourceBindingOutcome"])
        self.assertEqual(2, result["sourceBoundBlockedDirectOperationCount"])
        self.assertEqual(11, result["outstandingApplicableCellCount"])
        positive = next(row for row in receipt["tables"][0]["operations"] if row["operation"] == "DELETE" and row["situation"] == "sameScope")
        positive["sourceRejectionIdentity"]["nativeConstraintName"] = "foreign_constraint"
        result = self.reconcile(receipt, reference)
        self.assertEqual("FAIL", result["sourceBindingOutcome"])
        self.assertEqual(1, result["unboundSourceRejectionCount"])

    def test_another_real_guard_cannot_replace_the_declared_unavailable_guard(self):
        receipt, reference = copy.deepcopy(self.receipt), copy.deepcopy(self.reference)
        guard = {**copy.deepcopy(self.guard), "triggerName": "other_trigger", "functionName": "other_function"}
        for table in (receipt["tables"][0], reference["tables"][0]):
            table["sourceSchemaIdentity"]["guards"].append(copy.deepcopy(guard))
            self.redigest(table["sourceSchemaIdentity"])
        row = next(row for row in receipt["tables"][0]["operations"] if row["operation"] == "UPDATE" and row["situation"] == "sameScope")
        row["sourceRejectionIdentity"]["guardFunctionName"] = "other_function"
        result = self.reconcile(receipt, reference)
        self.assertEqual("FAIL", result["sourceBindingOutcome"])
        self.assertEqual(0, result["sourceBoundBlockedDirectOperationCount"])
        self.assertEqual(0, result["unboundSourceRejectionCount"])

    def test_false_reason_or_positive_status_does_not_retire_source_blocked_cells(self):
        for situation in matrix.SITUATIONS:
            receipt = copy.deepcopy(self.receipt)
            row = next(row for row in receipt["tables"][0]["operations"] if row["operation"] == "UPDATE" and row["situation"] == situation)
            row["reasonCode"] = "OtherSourceReason"
            with self.subTest(situation=situation): self.assertEqual("FAIL", self.reconcile(receipt)["sourceBindingOutcome"])

    def test_source_cli_requires_all_independent_inputs_and_preserves_output(self):
        root = Path(self.temporary.name)
        receipt, inventory, reference, output = (root / name for name in ("matrix.json", "inventory.json", "source.json", "report.json"))
        for path, value in ((receipt, self.receipt), (inventory, self.inventory), (reference, self.reference)):
            path.write_text(json.dumps(value), encoding="utf-8")
        command = [sys.executable, "-B", str(Path(matrix.__file__)), "--matrix", str(receipt), "--inventory", str(inventory),
                   "--inventory-digest", hashlib.sha256(inventory.read_bytes()).hexdigest(), "--candidate-sha", SHA,
                   "--test-assembly-digest", ASSEMBLY, "--environment-fingerprint", ENVIRONMENT_DIGEST, "--output", str(output)]
        for flags in (("--source-reference", str(reference)), ("--source-checkout", str(self.checkout)),
                      ("--source-reference-digest", hashlib.sha256(reference.read_bytes()).hexdigest())):
            process = subprocess.run(command + list(flags), capture_output=True, text=True)
            self.assertNotEqual(0, process.returncode)
            self.assertFalse(output.exists())
        output.write_bytes(b"historical report\n")
        process = subprocess.run(command + ["--source-reference", str(reference), "--source-reference-digest", "f" * 64,
                                             "--source-checkout", str(self.checkout)], capture_output=True, text=True)
        self.assertNotEqual(0, process.returncode)
        self.assertEqual(b"historical report\n", output.read_bytes())


if __name__ == "__main__":
    unittest.main()
