"""Advisory HTTP assertion accounting. Missing dimensions and approval remain UNVERIFIED."""

from __future__ import annotations

import argparse
from collections import Counter
from datetime import datetime, timezone
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET

from sec_arch_evidence import digest, instant, NS, observed_trx, reconcile_identity

AUTH = "Coglatas.Tests.SecurityArchitecture.SecurityArchitectureApiAuthorizationTests.EveryComposedProtectedHttpEndpointRejectsAnonymousRequestsAfterValidCsrf"
PUBLIC = "Coglatas.Tests.SecurityArchitecture.SecurityArchitectureApiCurrentAuthorityTests.AnonymousBypassHandlersAreClassifiedAndApplicationOwnedAuthenticationStillRejects"
CAPABILITY = "Coglatas.Tests.SecurityArchitecture.SecurityArchitectureApiCurrentAuthorityTests.PersistedProjectCreateCapabilityChangesDenyRealHttpWithoutCreationEffects"
KANBAN = "Coglatas.Tests.PostgreSql.TaskV1Pr05KanbanHostedHttpTests.Snapshot_HostedPostgreSqlPipelineEnforcesAuthVisibilityDoneWindowAndMembershipRevocation"
GANTT = "Coglatas.Tests.PostgreSql.TaskV1Pr06GanttHostedHttpTests.Snapshot_RealPipelineIsCanonicalDuplicateFreeAndSafelyRejectsRevokedArchivedDeletedAndCrossScopeAccess"
GANTT_COMMANDS = "Coglatas.Tests.PostgreSql.TaskV1Pr06GanttHostedHttpTests.Commands_RealPipelineEnforcesCookieCsrfPermissionsConcurrencyAtomicityAndCanonicalPersistence"
METHOD_SOURCES = {
    method: "tests/Coglatas.Tests/" + folder + "/" + method.rsplit(".", 2)[-2] + ".cs"
    for method, folder in ((AUTH, "SecurityArchitecture"), (PUBLIC, "SecurityArchitecture"),
                           (CAPABILITY, "SecurityArchitecture"), (KANBAN, "PostgreSql"), (GANTT, "PostgreSql"), (GANTT_COMMANDS, "PostgreSql"))
}
ASSEMBLIES = {"Coglatas.Tests", "Coglatas.Web", "Coglatas.Application", "Coglatas.Infrastructure", "Coglatas.Domain"}
PUBLIC_PATHS = {
    ("POST", "/api/auth/login"), ("POST", "/api/auth/register-by-invite"),
    ("POST", "/api/invites/accept"), ("GET", "/api/invites/validate"),
    ("GET", "/api/auth/status"), ("GET", "/api/security/csrf-token"),
    ("GET", "/api/ui/runtime-config.js"), ("GET", "/health/live"),
    ("GET", "/health/ready"), ("GET", "/health/realtime"), ("GET", "/health/task-deadline-digests"),
}
APP_PATHS = {
    ("GET", "/api/projects/{projectId}/gantt"), ("GET", "/api/tasks/{taskItemId}/dependencies"),
    ("POST", "/api/tasks/{taskItemId}/dependencies"),
    ("DELETE", "/api/tasks/{taskItemId}/dependencies/{dependencyId}"),
    ("PATCH", "/api/tasks/{taskItemId}/progress"), ("PATCH", "/api/tasks/{taskItemId}/schedule"),
}
POSITIVE = {"AUTHORIZED_SAME_SCOPE", "AUTHORIZED_RESTORED_SCOPE", "AUTHORIZED_SESSION_PIPELINE"}
CAPABILITY_CONTROLS = {"CURRENT_CAPABILITY_REVOKED", "CURRENT_CAPABILITY_EXPIRED", "CURRENT_CAPABILITY_NOT_YET_VALID",
                       "CURRENT_CAPABILITY_WRONG_SCOPE", "CURRENT_CAPABILITY_WRONG_SUBJECT", "CURRENT_CAPABILITY_UNKNOWN_KEY"}
RESOURCE_CONTROLS = {"SAME_TENANT_RESOURCE", "CROSS_TENANT", "CURRENT_WORKSPACE_MEMBERSHIP_REVOKED", "CURRENT_TENANT_MEMBERSHIP_REVOKED", "CURRENT_RESOURCE_ROLE_DENIED"}
PUBLIC_CONTROLS = {"PUBLIC_CREDENTIAL_REJECTED", "PUBLIC_HANDLER_RESPONSE"}
CONTROLS = POSITIVE | CAPABILITY_CONTROLS | RESOURCE_CONTROLS | PUBLIC_CONTROLS | {"ANONYMOUS", "ANONYMOUS_WITH_VALID_CSRF"}


def account(root: Path, inventory: dict, recordings: list[dict], trx: bytes, now: datetime,
            execution_receipt: dict | None = None, identity: tuple[str, str, str, str] | None = None) -> dict:
    if inventory.get("catalogScope") != "ACTUAL_COMPOSED_TEST_HOST" or inventory.get("schemaVersion") != 1:
        raise ValueError("Actual composed inventory required.")
    endpoints = {(row["method"], row["normalizedPath"]): row for row in inventory["endpoints"]}
    if len(endpoints) != inventory["endpointCount"] or len(endpoints) != len(inventory["endpoints"]):
        raise ValueError("Duplicate or incomplete endpoint inventory.")
    methods = [record["verifierMethod"] for record in recordings]
    if len(set(methods)) != len(methods) or not methods or set(methods) - METHOD_SOURCES.keys():
        raise ValueError("Duplicate or unsupported response verifier.")
    execution = observed_trx(trx, now, {method: 1 for method in methods})
    passed = {row["method"] for row in execution["cases"] if row["outcome"] == "PASS"}
    xml = ET.fromstring(trx)
    intervals = {}
    definitions = {row.attrib["id"]: row.find("t:TestMethod", NS) for row in xml.findall("t:TestDefinitions/t:UnitTest", NS)}
    for result in xml.findall("t:Results/t:UnitTestResult", NS):
        definition = definitions[result.attrib["testId"]]
        method = definition.attrib["className"] + "." + definition.attrib["name"]
        if method in methods:
            intervals[method] = (instant(result.attrib["startTime"]), instant(result.attrib["endTime"]))
    candidate = "UNVERIFIED"
    if execution_receipt is not None:
        if identity is None:
            raise ValueError("Independent candidate/run identity required.")
        reconcile_identity(execution_receipt, *identity)
        if execution_receipt["executionDigest"] != digest(trx):
            raise ValueError("Wrong execution artifact.")
        candidate = "EXACT_RECEIPT_RECONCILED_TRUSTED_ATTESTATION_PENDING"
    observations = []
    for record in recordings:
        method = record["verifierMethod"]
        if record.get("schemaVersion") != 1 or record.get("ownerApproval") is not None:
            raise ValueError("Unsupported receipt or self-declared approval.")
        source = METHOD_SOURCES[method]
        if record["sourcePath"] != source or record["sourceDigest"] != digest((root / source).read_bytes()):
            raise ValueError("Renamed, deleted or changed verifier source.")
        assemblies = record["assemblyDigests"]
        if set(assemblies) != ASSEMBLIES:
            raise ValueError("Incomplete assembly identity.")
        for name, value in assemblies.items():
            folder = "tests" if name == "Coglatas.Tests" else "src"
            path = root / folder / name / "bin/Release/net10.0" / (name + ".dll")
            if not re.fullmatch(r"[a-f0-9]{64}", value) or value != digest(path.read_bytes()):
                raise ValueError("Changed build identity.")
        if inventory["webAssemblyDigest"] != assemblies["Coglatas.Web"]:
            raise ValueError("Inventory from a different build.")
        if execution_receipt is not None and execution_receipt["assemblyDigests"] != assemblies:
            raise ValueError("Wrong candidate assemblies.")
        seen = Counter()
        for row in record["observations"]:
            key = (row["method"], row["path"])
            control = row["control"]
            if key not in endpoints or control not in CONTROLS or row["observedStatus"] != row["expectedStatus"]:
                raise ValueError("Unclassified endpoint/control or failed assertion.")
            timestamp = instant(row["observedAtUtc"])
            if method not in intervals or not intervals[method][0] <= timestamp <= intervals[method][1]:
                raise ValueError("Observation outside actual verifier execution.")
            if control in POSITIVE and not 200 <= row["observedStatus"] < 300:
                raise ValueError("Positive operation failed.")
            if control in {"ANONYMOUS", "ANONYMOUS_WITH_VALID_CSRF"} and row["observedStatus"] != 401:
                raise ValueError("Unrelated permission/input rejection is not authentication evidence.")
            if method in {PUBLIC, GANTT_COMMANDS} and key in APP_PATHS and control == "ANONYMOUS_WITH_VALID_CSRF":
                expected_code = "TASK_DEPENDENCY_AUTHENTICATION_REQUIRED" if "/dependencies" in key[1] else "GANTT_AUTHENTICATION_REQUIRED"
                if row.get("errorCode") != expected_code:
                    raise ValueError("Application-owned authentication lacks typed authority result.")
            if control in CAPABILITY_CONTROLS and (row["observedStatus"] != 403 or row.get("errorCode") != "CapabilityDenied"):
                raise ValueError("Capability denial lacks typed authority result.")
            if control in RESOURCE_CONTROLS:
                expected_status = 401 if control == "CURRENT_TENANT_MEMBERSHIP_REVOKED" else 403 if control == "CURRENT_RESOURCE_ROLE_DENIED" else 404
                expected_code = "KANBAN_NOT_FOUND" if method == KANBAN else "GANTT_PROJECT_NOT_FOUND"
                if method == GANTT_COMMANDS:
                    expected_code = "GANTT_FORBIDDEN" if control == "CURRENT_RESOURCE_ROLE_DENIED" else (
                        "TASK_DEPENDENCY_NOT_FOUND" if key[1].endswith("/dependencies") else "GANTT_WORK_ITEM_NOT_FOUND")
                if row["observedStatus"] != expected_status or (expected_status != 401 and row.get("errorCode") != expected_code):
                    raise ValueError("Resource rejection lacks asserted authority result.")
            if method == AUTH:
                allowed = control == "ANONYMOUS_WITH_VALID_CSRF" and endpoints[key]["authorizationRequired"] or (
                    control == "AUTHORIZED_SESSION_PIPELINE" and key == ("GET", "/api/auth/me"))
            elif method == PUBLIC:
                allowed = key in PUBLIC_PATHS and control in PUBLIC_CONTROLS or key in APP_PATHS and control == "ANONYMOUS_WITH_VALID_CSRF" or (
                    key == ("GET", "/api/auth/me") and control == "AUTHORIZED_SESSION_PIPELINE")
            elif method == CAPABILITY:
                allowed = key == ("POST", "/api/workspaces/{workspaceId}/projects") and control in CAPABILITY_CONTROLS | {"AUTHORIZED_SAME_SCOPE", "AUTHORIZED_RESTORED_SCOPE"}
            elif method == GANTT_COMMANDS:
                allowed = key in APP_PATHS - {("GET", "/api/projects/{projectId}/gantt"), ("GET", "/api/tasks/{taskItemId}/dependencies")} and control in (
                    RESOURCE_CONTROLS | {"ANONYMOUS_WITH_VALID_CSRF", "AUTHORIZED_SAME_SCOPE"})
            else:
                path = "/api/projects/{projectId}/kanban" if method == KANBAN else "/api/projects/{projectId}/gantt"
                allowed = key == ("GET", path) and control in RESOURCE_CONTROLS | {"ANONYMOUS", "AUTHORIZED_SAME_SCOPE", "AUTHORIZED_RESTORED_SCOPE"}
            if not allowed:
                raise ValueError("Assertion outside reviewed verifier scope.")
            seen[(key, control)] += 1
            maximum = 6 if method == CAPABILITY and control == "AUTHORIZED_RESTORED_SCOPE" else 1
            if seen[(key, control)] > maximum:
                raise ValueError("Duplicate observation cannot multiply coverage.")
            observations.append({**row, "verifierMethod": method, "executionOutcome": "PASS" if method in passed else "UNVERIFIED"})
    for row in observations:
        if row["control"] in RESOURCE_CONTROLS | CAPABILITY_CONTROLS:
            positive = any(peer["verifierMethod"] == row["verifierMethod"] and peer["method"] == row["method"] and
                           peer["path"] == row["path"] and peer["control"] == "AUTHORIZED_SAME_SCOPE" and
                           peer["executionOutcome"] == "PASS" for peer in observations)
        elif row["control"] in {"ANONYMOUS", "ANONYMOUS_WITH_VALID_CSRF"}:
            positive = any(peer["verifierMethod"] == row["verifierMethod"] and peer["control"] in POSITIVE and
                           peer["executionOutcome"] == "PASS" for peer in observations)
        else:
            positive = True
        row["accountingOutcome"] = "PASS" if positive and row["executionOutcome"] == "PASS" else "UNVERIFIED"
    rows = []
    for key, endpoint in sorted(endpoints.items()):
        controls = [row for row in observations if (row["method"], row["path"]) == key]
        rows.append({"surfaceId": endpoint["surfaceId"], "method": key[0], "path": key[1], "controls": controls,
                     "sourceObservation": endpoint.get("accessPathObservation"),
                     "specIds": [], "specMappingOutcome": "UNVERIFIED", "resourceCoverageOutcome": "UNVERIFIED",
                     "apiToRlsOutcome": "UNVERIFIED", "contractCompletion": "UNVERIFIED"})
    protected = {key for key, row in endpoints.items() if row["authorizationRequired"] and row["kind"] != "HUB"} | (APP_PATHS & endpoints.keys())
    anonymous = {(row["method"], row["path"]) for row in observations
                 if row["accountingOutcome"] == "PASS" and row["control"] in {"ANONYMOUS", "ANONYMOUS_WITH_VALID_CSRF"}}
    dimensions = {}
    for dimension, names in {
        "authorizedSameScope": {"AUTHORIZED_SAME_SCOPE", "AUTHORIZED_RESTORED_SCOPE"},
        "crossTenant": {"CROSS_TENANT"}, "sameTenantResource": {"SAME_TENANT_RESOURCE"},
        "currentWorkspaceMembershipRevocation": {"CURRENT_WORKSPACE_MEMBERSHIP_REVOKED"},
        "currentTenantMembershipRevocation": {"CURRENT_TENANT_MEMBERSHIP_REVOKED"},
        "currentResourceRoleDenial": {"CURRENT_RESOURCE_ROLE_DENIED"},
        "currentCapability": CAPABILITY_CONTROLS,
    }.items():
        observed = {(row["method"], row["path"]) for row in observations
                    if row["accountingOutcome"] == "PASS" and row["control"] in names} & protected
        dimensions[dimension] = {"observedEndpointCount": len(observed), "unobservedEndpointCount": len(protected - observed),
                                 "unobservedApplicability": "UNVERIFIED"}
    return {"schemaVersion": 1, "verifierId": "SEC-ARCH-HTTP-ASSERTION-ACCOUNTING", "mode": "ADVISORY",
            "inventoryDigest": digest(json.dumps(inventory, sort_keys=True).encode()), "executionDigest": digest(trx),
            "candidateBinding": candidate, "executionOutcome": execution["outcome"],
            "endpointCount": len(rows), "observedControlCount": len(observations),
            "protectedHttpEndpointCount": len(protected), "anonymousObservedEndpointCount": len(anonymous & protected),
            "anonymousOutstandingEndpointCount": len(protected - anonymous),
            "controlDimensions": dimensions,
            "resourceCoverageOutstandingEndpointCount": len(protected), "unmappedSurfaceCount": len(rows),
            "apiToRlsOutstandingAdapterCount": 1, "endpoints": rows,
            "trustedAttestation": "UNVERIFIED", "ownerApproval": None,
            "preAvaloniaVerdict": "PRE-AVALONIA SEC-ARCH: BLOCKED",
            "limits": ["Names, source references and metadata alone receive no execution credit.",
                       "Passed explicit observations cover only their named controls; full endpoint coverage remains UNVERIFIED.",
                       "Receipt fields are not authentic owner approval or trusted execution attestations.",
                       "Declared public paths remain observations pending canonical classification approval."]}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inventory", required=True, type=Path)
    parser.add_argument("--observations", required=True, nargs="+", type=Path)
    parser.add_argument("--trx", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--execution-receipt", type=Path)
    parser.add_argument("--candidate-sha")
    parser.add_argument("--environment-fingerprint")
    parser.add_argument("--run-id")
    parser.add_argument("--run-attempt")
    args = parser.parse_args()
    identity = (args.candidate_sha, args.environment_fingerprint, args.run_id, args.run_attempt)
    if args.execution_receipt is not None and not all(identity):
        raise ValueError("Independent candidate/run binding missing.")
    root = Path(__file__).resolve().parents[2]
    report = account(root, json.loads(args.inventory.read_bytes()), [json.loads(path.read_bytes()) for path in args.observations],
                     args.trx.read_bytes(), datetime.now(timezone.utc),
                     json.loads(args.execution_receipt.read_bytes()) if args.execution_receipt else None,
                     identity if args.execution_receipt else None)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("x", encoding="utf-8") as output:
        output.write(json.dumps(report, indent=2) + "\n")
    print("SEC-ARCH HTTP accounting: " + report["executionOutcome"] + "; pre-Avalonia BLOCKED")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (ValueError, KeyError, TypeError, OSError, ET.ParseError):
        print("SEC-ARCH HTTP accounting ERROR: invalid or unavailable evidence inputs.")
        raise SystemExit(1)
