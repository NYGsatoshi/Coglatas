"""Bind private draft RLS source observations to independent native/source inputs."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path
import re
import subprocess

GUARD_KEYS = ("triggerName", "enabled", "commandMask", "triggerDefinitionDigest", "functionSchema",
              "functionName", "identityArguments", "functionDefinitionDigest")
CONSTRAINT_KEYS = ("constraintName", "kind", "constraintTable", "referencedTable", "relationship",
                   "validated", "deferrable", "deferred", "definitionDigest")
DISPOSITION_KEYS = {"operation", "reasonCode", "classification", "alternateLifecycle", "sources", "guard", "constraint"}
REFERENCE_KEYS = {"schemaVersion", "candidateSha", "testAssemblyDigest", "approval", "ownerApproval", "environment",
                  "environmentFingerprint", "executionScope", "productRlsAppliedCount", "tables"}
ENVIRONMENT_KEYS = ("dotnetVersion", "npgsqlVersion", "postgresVersion", "fixture")
PROBE_KEYS = {"operation", "situation", "semanticReason", "classification", "rlsQualification", "authorityDisposition",
              "parentTables", "source", "guard"}
IDENTIFIER = r"[A-Za-z_][A-Za-z0-9_]{0,62}"
CONSTRAINT_IDENTIFIER = r"[A-Za-z_][A-Za-z0-9_~]{0,62}"
DIGEST = r"[a-f0-9]{64}"
SOURCE_PATH = r"src/Coglatas.Infrastructure/Persistence/Migrations/[0-9]{14}_[A-Za-z0-9_]+\.cs"


class SourceError(ValueError):
    """Disclosure-safe source validation failure."""


def require(condition: bool, message: str) -> None:
    if not condition: raise SourceError(message)


def matches(value, pattern: str) -> bool:
    return isinstance(value, str) and re.fullmatch(pattern, value) is not None


def rejection_identity(value) -> bool:
    return isinstance(value, dict) and set(value) == {"nativeConstraintName", "guardFunctionSchema", "guardFunctionName"} and all(
        value[key] is None or matches(value[key], CONSTRAINT_IDENTIFIER if key == "nativeConstraintName" else IDENTIFIER) for key in value)


def schema(value: dict, table: str) -> dict:
    require(isinstance(value, dict) and set(value) == {"table", "schemaDigest", "guards", "constraints"} and
            value["table"] == table and matches(value["schemaDigest"], DIGEST) and
            isinstance(value["guards"], list) and len(value["guards"]) <= 100 and
            isinstance(value["constraints"], list) and len(value["constraints"]) <= 1000,
            "Native source schema shape differs.")
    guards, constraints, identities = [], [], set()
    for guard in value["guards"]:
        require(isinstance(guard, dict) and set(guard) == set(GUARD_KEYS) and
                all(matches(guard[key], IDENTIFIER) for key in ("triggerName", "functionSchema", "functionName")) and
                guard["enabled"] in {"O", "D", "R", "A"} and type(guard["commandMask"]) is int and
                0 < guard["commandMask"] <= 127 and guard["identityArguments"] == "" and
                all(matches(guard[key], DIGEST) for key in ("triggerDefinitionDigest", "functionDefinitionDigest")),
                "Native trigger/function identity is invalid.")
        require(guard["triggerName"] not in identities, "Duplicate native trigger identity.")
        identities.add(guard["triggerName"])
        guards.append({key: guard[key] for key in GUARD_KEYS})
    identities.clear()
    for constraint in value["constraints"]:
        require(isinstance(constraint, dict) and set(constraint) == set(CONSTRAINT_KEYS) and
                matches(constraint["constraintName"], CONSTRAINT_IDENTIFIER) and matches(constraint["constraintTable"], IDENTIFIER) and
                (constraint["referencedTable"] is None or matches(constraint["referencedTable"], IDENTIFIER)) and
                constraint["kind"] in {"c", "f", "p", "u", "x", "t", "n"} and
                constraint["relationship"] in {"OWNED", "REFERENCING"} and
                all(type(constraint[key]) is bool for key in ("validated", "deferrable", "deferred")) and
                matches(constraint["definitionDigest"], DIGEST), "Native constraint identity is invalid.")
        require((constraint["relationship"] == "OWNED" and constraint["constraintTable"] == table) or
                (constraint["relationship"] == "REFERENCING" and constraint["kind"] == "f" and
                 constraint["referencedTable"] == table), "Native constraint relationship differs.")
        identity = (constraint["constraintTable"], constraint["constraintName"])
        require(identity not in identities, "Duplicate native constraint identity.")
        identities.add(identity)
        constraints.append({key: constraint[key] for key in CONSTRAINT_KEYS})
    canonical = json.dumps({"guards": guards, "constraints": constraints}, separators=(",", ":"), ensure_ascii=False).encode()
    require(hashlib.sha256(canonical).hexdigest() == value["schemaDigest"], "Native schema digest does not bind its objects.")
    return value


def dispositions(values: list, native: dict, checkout: Path | None = None) -> dict:
    require(isinstance(values, list) and len(values) <= 4, "Unavailable source dispositions are invalid.")
    result = {}
    for value in values:
        require(isinstance(value, dict) and set(value) == DISPOSITION_KEYS and value["operation"] in {"UPDATE", "DELETE"} and
                value["operation"] not in result and matches(value["reasonCode"], IDENTIFIER) and
                value["classification"] == "SOURCE_BLOCKED_DIRECT_MUTATION" and
                (value["alternateLifecycle"] is None or matches(value["alternateLifecycle"], IDENTIFIER)) and
                isinstance(value["sources"], list) and 0 < len(value["sources"]) <= 10,
                "Unavailable source disposition is invalid or duplicated.")
        paths = set()
        for source in value["sources"]:
            require(isinstance(source, dict) and set(source) == {"path", "digest"} and
                    matches(source["path"], SOURCE_PATH) and matches(source["digest"], DIGEST) and
                    source["path"] not in paths, "Source path/digest is invalid or duplicated.")
            paths.add(source["path"])
            if checkout is not None:
                path = checkout.joinpath(*source["path"].split("/"))
                require(path.resolve() == path and path.is_file() and path.stat().st_size <= 2 * 1024 * 1024,
                        "Source file resolves outside its canonical bounded checkout path.")
                with path.open("rb") as source_file:
                    source_bytes = source_file.read(2 * 1024 * 1024 + 1)
                require(len(source_bytes) <= 2 * 1024 * 1024, "Current source file exceeds its supported bound.")
                require(hashlib.sha256(source_bytes).hexdigest() == source["digest"], "Current source file digest differs.")
        guard, constraint = value["guard"], value["constraint"]
        require((guard is None) != (constraint is None), "A source disposition needs exactly one native object.")
        if guard is not None:
            require(guard in native["guards"] and guard["enabled"] in {"O", "A"} and
                    guard["commandMask"] & (16 if value["operation"] == "UPDATE" else 8) != 0,
                    "Unavailable operation lacks its enabled native command guard.")
        else:
            require(constraint in native["constraints"] and constraint["validated"] and
                    constraint["relationship"] == "REFERENCING", "Unavailable operation lacks its current referencing constraint.")
        result[value["operation"]] = value
    return result


def guarded_probes(values: list, native: dict, checkout: Path | None = None) -> dict:
    require(isinstance(values, list) and len(values) <= 4, "Guarded probe dispositions are invalid.")
    result = {}
    for value in values:
        require(isinstance(value, dict) and set(value) == PROBE_KEYS and
                isinstance(value["operation"], str) and isinstance(value["situation"], str) and
                all(isinstance(value[key], str) for key in ("semanticReason", "classification", "rlsQualification", "authorityDisposition")) and
                (value["operation"], value["situation"]) not in result and
                ((value["operation"] == "INSERT" and value["situation"] in {"crossTenant", "missingContext", "invalidContext"} and
                  value["semanticReason"] == "ParentVisibilityOrCurrentStatePrecheck" and
                  value["authorityDisposition"] == "ConcreteParentLookupAuthorityRequiresOwnerReview") or
                 (value["operation"] == "UPDATE" and value["situation"] == "wrongOwnership" and
                  value["semanticReason"] in {"ParentScopeMismatch", "ImmutableBoundIdentity"} and
                  value["authorityDisposition"] == "ConcreteMutationAuthorityRequiresOwnerReview")) and
                value["classification"] == "SOURCE_PRECHECK_BEFORE_RLS_WITH_CHECK" and value["rlsQualification"] == "UNVERIFIED" and
                isinstance(value["parentTables"], list) and len(value["parentTables"]) <= 10 and
                all(matches(parent, IDENTIFIER) for parent in value["parentTables"]) and
                len(set(value["parentTables"])) == len(value["parentTables"]), "Guarded probe semantics or authority differ.")
        require((value["semanticReason"] == "ImmutableBoundIdentity") == (not value["parentTables"]),
                "Guarded probe parent dependencies differ.")
        guard = value["guard"]
        require(guard in native["guards"] and guard["enabled"] in {"O", "A"} and
                guard["commandMask"] & (4 if value["operation"] == "INSERT" else 16) != 0,
                "Guarded probe lacks its enabled native command guard.")
        source = value["source"]
        require(isinstance(source, dict) and set(source) == {"path", "digest"} and
                matches(source["path"], SOURCE_PATH) and matches(source["digest"], DIGEST), "Guarded probe source identity differs.")
        if checkout is not None:
            path = checkout.joinpath(*source["path"].split("/"))
            require(path.resolve() == path and path.is_file() and path.stat().st_size <= 2 * 1024 * 1024,
                    "Guarded probe source path differs.")
            with path.open("rb") as source_file: source_bytes = source_file.read(2 * 1024 * 1024 + 1)
            require(len(source_bytes) <= 2 * 1024 * 1024 and hashlib.sha256(source_bytes).hexdigest() == source["digest"],
                    "Guarded probe current source bytes differ.")
        result[(value["operation"], value["situation"])] = value
    return result


def bind(tables: dict, cells: dict, classifications: dict, reference: dict | None, candidate: str,
         assembly_digest: str, environment_fingerprint: str, checkout: Path | None) -> dict:
    rejected = {key: row for key, row in cells.items() if row["observedMechanism"] in {"TRIGGER_REJECTION", "CONSTRAINT_REJECTION"}}
    if reference is None:
        require(checkout is None, "Source checkout requires an independent source reference.")
        return {"sourceBindingOutcome": "UNVERIFIED", "sourceBoundBlockedDirectOperationCount": 0,
                "unboundSourceRejectionCount": len(rejected), "missingSourceIdentityCount": len(tables), "sourceViolations": [],
                "sourceBoundGuardedProbeCount": 0, "missingGuardedProbeDispositionCount": sum(
                    row.get("reasonCode") == "SourceMutationGuard" for row in cells.values())}
    require(isinstance(reference, dict) and set(reference) == REFERENCE_KEYS and checkout is not None and
            reference.get("testAssemblyDigest") == assembly_digest and
            isinstance(reference.get("environment"), dict) and
            set(reference["environment"]) == set(ENVIRONMENT_KEYS) and
            all(isinstance(reference["environment"][key], str) and 0 < len(reference["environment"][key]) <= 100 for key in ENVIRONMENT_KEYS) and
            reference["environment"]["fixture"] == "isolated-migrated-postgresql" and
            hashlib.sha256(json.dumps({key: reference["environment"][key] for key in ENVIRONMENT_KEYS},
                                      separators=(",", ":"), ensure_ascii=False).encode()).hexdigest() == environment_fingerprint and
            type(reference.get("schemaVersion")) is int and reference["schemaVersion"] == 1 and
            reference.get("candidateSha") == candidate and reference.get("approval") == "DRAFT" and
            reference.get("ownerApproval") is None and reference.get("environmentFingerprint") == environment_fingerprint and
            reference.get("executionScope") == "ISOLATED_MIGRATED_SCHEMA_REFERENCE" and
            type(reference.get("productRlsAppliedCount")) is int and reference["productRlsAppliedCount"] == 0 and
            isinstance(reference.get("tables"), list), "Independent source reference binding or authority differs.")
    checkout = checkout.resolve(strict=True)
    head = subprocess.run(["git", "-C", str(checkout), "rev-parse", "HEAD"], capture_output=True, text=True, check=True).stdout.strip()
    clean = subprocess.run(["git", "-C", str(checkout), "status", "--porcelain", "--untracked-files=all"], capture_output=True, text=True, check=True).stdout
    require(head == candidate and not clean, "Independent source checkout must be the clean exact candidate.")
    expected = {}
    for table in reference["tables"]:
        require(isinstance(table, dict) and set(table) in (
                    {"table", "sourceSchemaIdentity", "sourceUnavailableOperations"},
                    {"table", "sourceSchemaIdentity", "sourceUnavailableOperations", "sourceGuardedProbes"}) and
                table["table"] in classifications and table["table"] not in expected, "Independent source table is invalid or duplicated.")
        native = schema(table["sourceSchemaIdentity"], table["table"])
        blocked = dispositions(table["sourceUnavailableOperations"], native, checkout)
        probes = guarded_probes(table.get("sourceGuardedProbes", []), native, checkout)
        require(all(parent in classifications for probe in probes.values() for parent in probe["parentTables"]),
                "Guarded probe references an uninventoried dependency.")
        expected[table["table"]] = (native, blocked, probes)
    require(set(expected) == set(classifications), "Independent native source inventory is incomplete.")
    violations, missing, bound_blocked, unbound, bound_probes = [], 0, 0, 0, 0
    declared_probe_keys = set()
    for name, table in sorted(tables.items()):
        if table.get("sourceSchemaIdentity") is None:
            missing += 1
            unbound += sum(key[0] == name for key in rejected)
            continue
        observed = schema(table["sourceSchemaIdentity"], name)
        blocked = dispositions(table.get("sourceUnavailableOperations"), observed)
        native, expected_blocked, expected_probes = expected[name]
        probes = guarded_probes(table.get("sourceGuardedProbes", []), observed)
        if probes != expected_probes: violations.append({"table": name, "reasonCode": "GuardedProbeDispositionDiffers"})
        for (operation, situation), probe in expected_probes.items():
            key = (name, operation, situation)
            declared_probe_keys.add(key)
            row = cells.get(key)
            identity = row.get("sourceRejectionIdentity") if row is not None else None
            if (row is None or row["result"] != "UNVERIFIED" or row["observedMechanism"] != "TRIGGER_REJECTION" or
                    row["reasonCode"] != "SourceMutationGuard" or row["positiveControlAffectedRows"] <= 0 or row["affectedRows"] != 0 or
                    not rejection_identity(identity) or identity["nativeConstraintName"] is not None or
                    identity["guardFunctionName"] != probe["guard"]["functionName"] or
                    identity["guardFunctionSchema"] not in {None, probe["guard"]["functionSchema"]}):
                violations.append({"table": name, "operation": operation, "situation": situation, "reasonCode": "GuardedProbeObservationDiffers"})
            elif observed == native and probes == expected_probes:
                bound_probes += 1
        if observed != native: violations.append({"table": name, "reasonCode": "NativeSourceIdentityDiffers"})
        if blocked != expected_blocked: violations.append({"table": name, "reasonCode": "UnavailableSourceDispositionDiffers"})
        for operation, declaration in expected_blocked.items():
            positive = cells.get((name, operation, "sameScope"))
            if (positive is None or positive["result"] != "UNVERIFIED" or positive["affectedRows"] != 0 or
                    positive["positiveControlAffectedRows"] != 0 or positive["reasonCode"] != declaration["reasonCode"] or
                    positive["observedMechanism"] != ("TRIGGER_REJECTION" if declaration["guard"] is not None else "CONSTRAINT_REJECTION")):
                violations.append({"table": name, "operation": operation, "reasonCode": "UnavailableDirectPositiveDiffers"})
            else:
                direct_identity = positive.get("sourceRejectionIdentity")
                expected_identity = declaration["guard"] if declaration["guard"] is not None else declaration["constraint"]
                identity_matches = rejection_identity(direct_identity) and (
                    declaration["guard"] is not None and direct_identity.get("nativeConstraintName") is None and
                    direct_identity.get("guardFunctionName") == expected_identity["functionName"] and
                    direct_identity.get("guardFunctionSchema") in {None, expected_identity["functionSchema"]} or
                    declaration["constraint"] is not None and direct_identity.get("nativeConstraintName") == expected_identity["constraintName"] and
                    direct_identity.get("guardFunctionName") is None and direct_identity.get("guardFunctionSchema") is None)
                if not identity_matches:
                    violations.append({"table": name, "operation": operation, "reasonCode": "UnavailableDirectGuardDiffers"})
                elif observed == native and blocked == expected_blocked:
                    bound_blocked += 1
            for key, dependent in cells.items():
                if key[0:2] == (name, operation) and dependent["observedMechanism"] != "UNSUPPORTED_OPERATION" and (
                        dependent["result"] != "UNVERIFIED" or dependent["positiveControlAffectedRows"] != 0 or
                        dependent["reasonCode"] != declaration["reasonCode"]):
                    violations.append({"table": name, "operation": operation, "situation": key[2],
                                       "reasonCode": "UnavailableDependentCellDiffers"})
        for key, row in rejected.items():
            if key[0] != name: continue
            identity = row.get("sourceRejectionIdentity")
            matched = False
            if rejection_identity(identity):
                if row["observedMechanism"] == "TRIGGER_REJECTION":
                    matched = identity["nativeConstraintName"] is None and any(
                        guard["enabled"] in {"O", "A"} and guard["functionName"] == identity["guardFunctionName"] and
                        (identity["guardFunctionSchema"] is None or identity["guardFunctionSchema"] == guard["functionSchema"]) and
                        guard["commandMask"] & (16 if key[1] == "UPDATE" else 8 if key[1] == "DELETE" else 4) != 0
                        for guard in native["guards"])
                else:
                    matched = identity["guardFunctionSchema"] is None and identity["guardFunctionName"] is None and any(
                        constraint["validated"] and constraint["constraintName"] == identity["nativeConstraintName"]
                        for constraint in native["constraints"])
            if not matched or observed != native:
                unbound += 1
                violations.append({"table": name, "operation": key[1], "situation": key[2], "reasonCode": "SourceRejectionIdentityUnbound"})
    missing_probes = sum(row.get("reasonCode") == "SourceMutationGuard" and key not in declared_probe_keys for key, row in cells.items())
    return {"sourceBindingOutcome": "FAIL" if violations else "UNVERIFIED" if missing or missing_probes else "PASS",
            "sourceBoundBlockedDirectOperationCount": bound_blocked, "unboundSourceRejectionCount": unbound,
            "missingSourceIdentityCount": missing, "sourceViolations": violations,
            "sourceBoundGuardedProbeCount": bound_probes, "missingGuardedProbeDispositionCount": missing_probes}
