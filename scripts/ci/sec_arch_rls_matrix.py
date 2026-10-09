"""Reconcile draft per-operation RLS observations; never authenticate policy approval."""

from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import re
import subprocess

import sec_arch_rls_sources as sources

OPERATIONS = ("SELECT", "INSERT", "UPDATE", "DELETE")
SITUATIONS = ("sameScope", "crossTenant", "missingContext", "invalidContext", "unauthorizedRole", "wrongOwnership")
CLASSIFICATIONS = {"RLS_REQUIRED", "GLOBAL_SHARED", "SYSTEM_INTERNAL", "REQUIRES_OWNER_REVIEW"}
MECHANISMS = {"RLS_FILTER", "RLS_WITH_CHECK", "GRANT_DENIAL", "TRIGGER_REJECTION", "CONSTRAINT_REJECTION",
              "MISSING_FIXTURE", "UNSUPPORTED_OPERATION", "UNEXPECTED_ERROR", "ALLOWED"}
RESULTS = {"PASS", "FAIL", "UNVERIFIED", "ERROR"}
MAX_JSON = 16 * 1024 * 1024


class MatrixError(ValueError):
    """Disclosure-safe input diagnostic."""


def require(condition: bool, message: str) -> None:
    if not condition:
        raise MatrixError(message)


def unique_object(pairs: list[tuple]) -> dict:
    result = {}
    for key, value in pairs:
        require(key not in result, "Duplicate JSON property.")
        result[key] = value
    return result


def read_document(path: Path, expected_digest: str | None = None) -> dict:
    with path.open("rb") as source:
        data = source.read(MAX_JSON + 1)
    require(len(data) <= MAX_JSON, "Input exceeds the bounded size.")
    if expected_digest is not None:
        require(bool(re.fullmatch(r"[a-f0-9]{64}", expected_digest)) and
                hashlib.sha256(data).hexdigest() == expected_digest, "Independent inventory digest differs.")
    result = json.loads(data, object_pairs_hook=unique_object,
                        parse_constant=lambda _value: (_ for _ in ()).throw(MatrixError("Non-finite JSON value.")))
    require(isinstance(result, dict), "Input must be a document.")
    return result


def nonnegative_integer(value) -> bool:
    return type(value) is int and value >= 0


def reconcile(receipt: dict, inventory: dict, candidate: str, assembly_digest: str,
              environment_fingerprint: str, source_reference: dict | None = None, source_checkout: Path | None = None) -> dict:
    require(bool(re.fullmatch(r"[a-f0-9]{40}", candidate)) and
            bool(re.fullmatch(r"[a-f0-9]{64}", assembly_digest)), "Independent candidate and assembly identity are required.")
    require(type(receipt.get("schemaVersion")) is int and receipt["schemaVersion"] == 1 and
            receipt.get("candidateSha") == candidate and receipt.get("testAssemblyDigest") == assembly_digest,
            "Matrix candidate, schema or assembly binding differs.")
    require(receipt.get("approval") == "DRAFT" and receipt.get("ownerApproval") is None and
            type(receipt.get("productRlsAppliedCount")) is int and receipt["productRlsAppliedCount"] == 0 and
            receipt.get("applicationRoleEquivalence") == "UNVERIFIED" and
            receipt.get("workerRoleEquivalence") == "UNVERIFIED" and
            receipt.get("executionScope") == "ISOLATED_SYNTHETIC_MODEL_ROWS",
            "An isolated observation cannot assert approval, product activation or operational identity.")
    environment = receipt.get("environment")
    environment_keys = ("dotnetVersion", "npgsqlVersion", "postgresVersion", "fixture")
    require(isinstance(environment, dict) and set(environment) == set(environment_keys) and
            all(isinstance(environment[key], str) and 0 < len(environment[key]) <= 100 for key in environment_keys) and
            environment["fixture"] == "isolated-migrated-postgresql" and
            re.fullmatch(r"[a-f0-9]{64}", environment_fingerprint) is not None and
            receipt.get("environmentFingerprint") == environment_fingerprint and
            hashlib.sha256(json.dumps({key: environment[key] for key in environment_keys},
                                      separators=(",", ":"), ensure_ascii=False).encode()).hexdigest() == environment_fingerprint,
            "Independent environment fingerprint or observed environment differs.")
    roles = receipt.get("roles")
    require(isinstance(roles, list) and len(roles) == 2 and all(isinstance(role, dict) for role in roles) and
            {role.get("roleKind") for role in roles} == {"syntheticApplication", "syntheticUnauthorized"} and
            all(isinstance(role.get("databaseRole"), str) and
                re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]{0,62}", role["databaseRole"]) is not None and
                all(role.get(flag) is False for flag in ("isSuperuser", "bypassRls", "canCreateDb", "canCreateRole", "inheritsRoles")) and
                all(type(role.get(count)) is int and role[count] == 0 for count in ("membershipCount", "protectedTableOwnershipCount"))
                for role in roles) and len({role["databaseRole"] for role in roles}) == 2,
            "Actual distinct non-owner/non-superuser/non-bypass fixture role observations are required.")
    require(type(inventory.get("schemaVersion")) is int and inventory["schemaVersion"] == 1 and
            isinstance(inventory.get("tables"), list), "Independent table inventory is unsupported.")
    classifications = {}
    for table in inventory["tables"]:
        require(isinstance(table, dict) and isinstance(table.get("table"), str) and
                re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", table["table"]) is not None and
                table.get("classification") in CLASSIFICATIONS and table["table"] not in classifications,
                "Inventory has an invalid, duplicate or unclassified table.")
        classifications[table["table"]] = table["classification"]
    require(bool(classifications) and type(inventory.get("totalTableCount")) is int and
            inventory["totalTableCount"] == len(classifications),
            "Inventory table count differs.")
    required = {table for table, classification in classifications.items() if classification == "RLS_REQUIRED"}
    require(bool(required) and isinstance(receipt.get("tables"), list), "Required scope or matrix rows are missing.")
    tables, cells = {}, {}
    concrete_roles = {role["roleKind"]: role["databaseRole"] for role in roles}
    for table in receipt["tables"]:
        require(isinstance(table, dict) and isinstance(table.get("table"), str) and
                table["table"] in required and table["table"] not in tables and
                isinstance(table.get("operations"), list), "Matrix includes a duplicate or unexpected table.")
        require(table.get("fixtureStatus") == "SEEDED" and table.get("tenantIdentityKind") in {"UUID", "TEXT", "PARENT"} and
                isinstance(table.get("policyDigest"), str) and re.fullmatch(r"[a-f0-9]{64}", table["policyDigest"]) is not None and
                table.get("ownershipProbeKind") == ("PARENT_REASSIGNMENT" if table["tenantIdentityKind"] == "PARENT"
                                                    else "TENANT_REASSIGNMENT"),
                "Each table needs seeded rows, draft policy identity and explicit limited ownership-probe semantics.")
        controls = table.get("verificationControls")
        require(isinstance(controls, dict) and type(controls.get("permissivePolicyExposureRows")) is int and
                controls["permissivePolicyExposureRows"] > 0 and type(controls.get("restoredCrossTenantRows")) is int and
                controls["restoredCrossTenantRows"] == 0 and controls.get("revokedSelectMechanism") == "GRANT_DENIAL" and
                controls.get("revokedSelectSqlState") == "42501" and type(controls.get("restoredSameScopeRows")) is int and
                controls["restoredSameScopeRows"] > 0 and controls.get("forbiddenTruncateGrantDetected") is True,
                "Every table needs live policy exposure/restoration and privilege mutation controls.")
        tables[table["table"]] = table
        for row in table["operations"]:
            require(isinstance(row, dict) and row.get("operation") in OPERATIONS and row.get("situation") in SITUATIONS and
                    row.get("roleKind") == ("syntheticUnauthorized" if row.get("situation") == "unauthorizedRole"
                                             else "syntheticApplication") and
                    row.get("databaseRole") == concrete_roles.get(row.get("roleKind")) and row.get("result") in RESULTS and
                    row.get("observedMechanism") in MECHANISMS and row.get("expectedMechanism") in MECHANISMS and
                    nonnegative_integer(row.get("positiveControlAffectedRows")) and nonnegative_integer(row.get("affectedRows")) and
                    (row.get("reasonCode") is None or isinstance(row["reasonCode"], str) and
                     re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", row["reasonCode"]) is not None),
                    "Matrix has an unsupported operation, role, result or count.")
            key = (table["table"], row["operation"], row["situation"])
            require(key not in cells, "Duplicate matrix operation cell.")
            if row["situation"] == "wrongOwnership" and row["operation"] != "UPDATE":
                require(row["result"] == "UNVERIFIED" and row["observedMechanism"] == "UNSUPPORTED_OPERATION" and
                        row["reasonCode"] == "OwnershipReassignmentRequiresUpdate",
                        "Unavailable ownership reassignment needs its explicit operation reason.")
            cells[key] = row
    expected = {(table, operation, situation) for table in required for operation in OPERATIONS for situation in SITUATIONS}
    missing = sorted(expected - cells.keys())
    invalid_pass, unresolved = [], []
    for key, row in sorted(cells.items()):
        table, operation, situation = key
        if row["result"] != "PASS":
            unresolved.append({"table": table, "operation": operation, "situation": situation,
                               "result": row["result"], "mechanism": row["observedMechanism"]})
            continue
        valid = row["positiveControlAffectedRows"] > 0 and row["observedMechanism"] == row["expectedMechanism"]
        positive = cells.get((table, operation, "sameScope"))
        valid = valid and positive is not None and positive["result"] == "PASS" and positive["affectedRows"] > 0
        valid = valid and positive is not None and row["positiveControlAffectedRows"] == positive["affectedRows"]
        if situation == "sameScope":
            valid = valid and row["observedMechanism"] == "ALLOWED" and row.get("sqlState") is None
        elif situation == "unauthorizedRole":
            valid = valid and row["observedMechanism"] == "GRANT_DENIAL" and row.get("sqlState") == "42501" and row["affectedRows"] == 0
        elif situation == "wrongOwnership" and operation != "UPDATE":
            valid = False
        elif operation in ("SELECT", "UPDATE", "DELETE") and situation != "wrongOwnership":
            valid = valid and row["observedMechanism"] == "RLS_FILTER" and row.get("sqlState") is None and row["affectedRows"] == 0
        else:
            valid = valid and row["observedMechanism"] == "RLS_WITH_CHECK" and row.get("sqlState") == "42501" and row["affectedRows"] == 0
        if not valid:
            invalid_pass.append({"table": table, "operation": operation, "situation": situation})
    counts = Counter(row["result"] for row in cells.values())
    mechanism_counts = Counter(row["observedMechanism"] for row in cells.values())
    # Non-UPDATE reassignment cells are explicitly unsupported, not successful denials.
    qualifying = {(table, operation, situation) for table, operation, situation in expected
                  if situation != "wrongOwnership" or operation == "UPDATE"}
    gaps = len(qualifying - cells.keys()) + sum(key in qualifying and row["result"] != "PASS" for key, row in cells.items())
    observed = "FAIL" if invalid_pass or counts["FAIL"] else ("ERROR" if counts["ERROR"] else (
        "UNVERIFIED" if missing or gaps else "PASS"))
    try:
        source_binding = sources.bind(tables, cells, classifications, source_reference, candidate, assembly_digest, environment_fingerprint, source_checkout)
    except sources.SourceError as error:
        raise MatrixError(str(error)) from None
    if source_binding["sourceBindingOutcome"] == "FAIL": observed = "FAIL"
    return {"schemaVersion": 1, "candidateSha": candidate, "testAssemblyDigest": assembly_digest,
            "environmentFingerprint": environment_fingerprint, "observedFixtureRoleCount": len(roles),
            "qualification": "DRAFT_OPERATION_RECONCILIATION", "observedMatrixOutcome": observed,
            "inventoryTableCount": len(classifications), "proposedRequiredTableCount": len(required),
            "observedTableCount": len(tables), "missingTableCount": len(required - tables.keys()),
            "expectedCellCount": len(expected), "observedCellCount": len(cells), "outstandingApplicableCellCount": gaps,
            "classificationCounts": dict(sorted(Counter(classifications.values()).items())),
            "resultCounts": dict(sorted(counts.items())), "mechanismCounts": dict(sorted(mechanism_counts.items())),
            "missingCells": [{"table": table, "operation": operation, "situation": situation} for table, operation, situation in missing],
            "invalidPassingCells": invalid_pass, "unresolvedCells": unresolved,
            **source_binding,
            "approvedRlsScope": "UNVERIFIED", "ownerApproval": None, "trustedAttestation": "UNVERIFIED",
            "preAvaloniaVerdict": "PRE-AVALONIA SEC-ARCH: BLOCKED",
            "limits": ["The inventory digest binds independent input bytes, without authenticating owner approval.",
                       "Synthetic application operations do not qualify worker identities, current-state authorization or deployed roles.",
                       "This reconciles observed mechanisms; compiled runtime and database provenance still need trusted evidence.",
                       "Missing/unsupported fixtures, constraints and triggers never qualify as RLS denials."]}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--matrix", required=True, type=Path)
    parser.add_argument("--inventory", required=True, type=Path)
    parser.add_argument("--inventory-digest", required=True)
    parser.add_argument("--candidate-sha", required=True)
    parser.add_argument("--test-assembly-digest", required=True)
    parser.add_argument("--environment-fingerprint", required=True)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--source-reference", type=Path)
    parser.add_argument("--source-reference-digest")
    parser.add_argument("--source-checkout", type=Path)
    args = parser.parse_args()
    try:
        require(all(value is not None for value in (args.source_reference, args.source_reference_digest, args.source_checkout)) or
                all(value is None for value in (args.source_reference, args.source_reference_digest, args.source_checkout)),
                "Source binding requires reference, independent digest and clean candidate checkout together.")
        report = reconcile(read_document(args.matrix), read_document(args.inventory, args.inventory_digest),
                           args.candidate_sha, args.test_assembly_digest, args.environment_fingerprint,
                           read_document(args.source_reference, args.source_reference_digest) if args.source_reference is not None else None,
                           args.source_checkout)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        with args.output.open("x", encoding="utf-8", newline="\n") as destination:
            destination.write(json.dumps(report, indent=2) + "\n")
    except (MatrixError, OSError, ValueError, TypeError, KeyError, subprocess.SubprocessError):
        print("RLS operation reconciliation ERROR: invalid input or exclusive output.")
        return 1
    print("RLS draft operation reconciliation: " + report["observedMatrixOutcome"] + "; pre-Avalonia BLOCKED")
    return 0 if report["observedMatrixOutcome"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
