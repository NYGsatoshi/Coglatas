"""Produce sanitized Advisory specification-contract linkage from existing verifier lanes."""

from __future__ import annotations

import argparse
from collections import Counter
from datetime import datetime, timedelta, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import xml.etree.ElementTree as ET

from sec_arch_evidence import observed_trx
from sec_arch_assembly_binding import capture_assemblies, validate_local_assemblies

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
FAMILIES = ("ARCH", "AUTH", "RT", "STATE", "API", "UI")
CLASSES = ("ArchUnit", "Roslyn", "StaticCustom", "UnitTest", "IntegrationTest", "E2ETest",
           "ContractTest", "GeneratedEvidence", "Manual")
MAXIMUM_BYTES = 32 * 1024 * 1024
TOOLING_GROUPS = {"SecurityArchitectureCliTests", "SecurityArchitectureSpecRegistryTests", "SecurityArchitectureOwnerReviewTests"}
INVENTORY_GROUPS = {"SecurityArchitectureApiInventoryTests", "SecurityArchitectureInventoryTests"}
RUNTIME_GROUPS = {"SecurityArchitectureRlsTests", "SecurityArchitectureRlsCatalogTests", "SecurityArchitectureParentRlsTests",
                  "SecurityArchitectureRlsOperationTests", "SecurityArchitectureRlsDispositionTests", "SecurityArchitectureRlsSourceReferenceTests",
                  "SecurityArchitectureRlsRuntimeTests", "SecurityArchitectureRlsRecoveryTests", "SecurityArchitectureRlsAdapterTests", "SecurityArchitectureRlsComposedHostTests",
                  "SecurityArchitectureApiAuthorizationTests", "SecurityArchitectureApiCurrentAuthorityTests",
                  "SecurityArchitectureServiceTests", "SecurityArchitectureSignalRTests", "SecurityArchitectureSignalREventTests",
                  "OutboxReplayPostgreSqlTests", "SecurityArchitectureOutboxReplayTransportTests"}


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def bounded_bytes(path: Path) -> bytes:
    if path.stat().st_size > MAXIMUM_BYTES:
        raise ValueError("Unbounded input.")
    with path.open("rb") as source:
        data = source.read(MAXIMUM_BYTES + 1)
    if len(data) > MAXIMUM_BYTES:
        raise ValueError("Unbounded input.")
    return data


def read_json(data: bytes) -> dict:
    def unique_pairs(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError("Duplicate JSON property.")
            result[key] = value
        return result
    result = json.loads(data, object_pairs_hook=unique_pairs)
    if not isinstance(result, dict):
        raise ValueError("Document must be an object.")
    return result


def instant(value: str) -> datetime:
    result = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if result.tzinfo is None:
        raise ValueError("UTC execution context required.")
    return result.astimezone(timezone.utc)


def execution_lane(path: Path | None, now: datetime, binding: bool) -> dict:
    if path is None or not path.is_file():
        return {"status": "UNAVAILABLE", "candidateBinding": "UNVERIFIED", "executionDigest": None,
                "total": None, "passed": None, "failed": None, "unexecuted": None,
                "canonicalSpecMappings": "UNRESOLVED"}
    data = bounded_bytes(path)
    if re.search(br"<!\s*(?:DOCTYPE|ENTITY)\b", data, re.I):
        raise ValueError("Unsupported XML.")
    root = ET.fromstring(data)
    times = root.find("t:Times", NS)
    counters = root.find("t:ResultSummary/t:Counters", NS)
    results = root.findall("t:Results/t:UnitTestResult", NS)
    if root.tag != "{" + NS["t"] + "}TestRun" or times is None or counters is None or not results:
        raise ValueError("TRX evidence unavailable.")
    start, finish = instant(times.attrib["start"]), instant(times.attrib["finish"])
    if not now - timedelta(hours=24) <= start <= finish <= now + timedelta(minutes=5):
        raise ValueError("Stale execution.")
    definitions = {}
    for definition in root.findall("t:TestDefinitions/t:UnitTest", NS):
        method, execution = definition.find("t:TestMethod", NS), definition.find("t:Execution", NS)
        identity = definition.attrib["id"]
        if identity in definitions or method is None or execution is None:
            raise ValueError("Unresolvable verifier.")
        definitions[identity] = (definition.attrib["name"], execution.attrib["id"])
    seen = set()
    for result in results:
        identity = result.attrib["testId"]
        if identity in seen or definitions.get(identity) != (result.attrib["testName"], result.attrib["executionId"]):
            raise ValueError("Verifier definition/result mismatch.")
        seen.add(identity)
        if not start <= instant(result.attrib["startTime"]) <= instant(result.attrib["endTime"]) <= finish:
            raise ValueError("Execution timestamps disagree.")
    observed = Counter(result.attrib["outcome"] for result in results)
    passed, failed = observed["Passed"], observed["Failed"]
    if (int(counters.attrib["total"]) != len(results) or int(counters.attrib["passed"]) != passed or
            int(counters.attrib["failed"]) != failed or int(counters.attrib["executed"]) != passed + failed):
        raise ValueError("Execution counters disagree.")
    unexecuted = len(results) - passed - failed
    return {"status": "FAIL" if failed else "UNVERIFIED" if unexecuted else "OBSERVED_PASS",
            "candidateBinding": "BUILD_STAMP_BOUND" if binding else "UNVERIFIED",
            "executionDigest": digest(data), "total": len(results), "passed": passed,
            "failed": failed, "unexecuted": unexecuted, "canonicalSpecMappings": "UNRESOLVED"}


def counts(rows: list, allowed: tuple[str, ...]) -> dict:
    result = dict.fromkeys(allowed, 0)
    seen = set()
    for row in rows:
        if set(row) != {"category", "count"} or row["category"] not in result or type(row["count"]) is not int or row["count"] < 0:
            raise ValueError("Unsupported coverage category.")
        if row["category"] in seen:
            raise ValueError("Duplicate coverage category.")
        seen.add(row["category"])
        result[row["category"]] = row["count"]
    return result


def project_tool_result(data: bytes, sha: str) -> dict:
    result = read_json(data)
    if (set(result) != {"valid", "normativeReady", "approvalStatus", "executionAttestationStatus", "diagnostics", "coverage"} or
            type(result["valid"]) is not bool or result["normativeReady"] is not False or
            result["approvalStatus"] != "UNVERIFIED" or result["executionAttestationStatus"] != "UNVERIFIED"):
        raise ValueError("Unqualified authority claim.")
    findings = []
    for diagnostic in result["diagnostics"]:
        if set(diagnostic) != {"ruleId", "subject", "reason"} or not re.fullmatch(r"[A-Z0-9_]{1,64}", diagnostic["ruleId"]):
            raise ValueError("Invalid diagnostic.")
        # Do not publish private requirement identities, paths, statements, reasons or blind-spot prose.
        findings.append({"rule": diagnostic["ruleId"], "referenceDigest": digest(diagnostic["subject"].encode())})
    if result["valid"] != (len(findings) == 0):
        raise ValueError("Validity/diagnostics disagreement.")
    coverage = result["coverage"]
    projected = None
    if coverage is not None:
        integer_fields = ("schemaVersion", "registryVersion", "activeRequirements", "deprecatedRequirements", "retiredRequirements",
                          "mappings", "manualMappings", "executedPassingLinks", "unresolvedLinks", "requirementsWithKnownLimitations")
        if (set(coverage) != {"candidateSha", "activeFamilies", "severities", "verificationClasses", *integer_fields} or
                coverage["candidateSha"] != sha or coverage["schemaVersion"] != 1 or coverage["registryVersion"] < 1 or
                any(type(coverage[key]) is not int or coverage[key] < 0 for key in integer_fields)):
            raise ValueError("Invalid candidate coverage.")
        families = counts(coverage["activeFamilies"], FAMILIES)
        severities = counts(coverage["severities"], ("Blocking", "Advisory", "Manual"))
        classes = counts(coverage["verificationClasses"], CLASSES)
        if (sum(families.values()) != coverage["activeRequirements"] or
                sum(severities.values()) != coverage["activeRequirements"] + coverage["deprecatedRequirements"] or
                classes["Manual"] != coverage["manualMappings"] or
                sum(count for category, count in classes.items() if category != "Manual") == 0 and coverage["executedPassingLinks"] > 0):
            raise ValueError("Coverage totals disagree.")
        projected = {key: coverage[key] for key in integer_fields}
        projected.update(activeFamilies=families, severities=severities, verificationClasses=classes)
    return {"status": "STRUCTURALLY_VALID_DRAFT" if result["valid"] else "INVALID_OR_INCOMPLETE",
            "declaredDraftCoverage": projected, "diagnosticCount": len(findings),
            "unresolvedMappings": sorted(findings, key=lambda row: (row["rule"], row["referenceDigest"]))[:256],
            "unresolvedListTruncated": len(findings) > 256, "diagnosticRuleCounts": dict(sorted(Counter(row["rule"] for row in findings).items()))}


def run_tool(command: list[str]) -> tuple[int, bytes]:
    # Fixed reviewed executable, shell-free arguments, bounded output reads, deadline and no stderr disclosure.
    with tempfile.TemporaryFile() as output:
        result = subprocess.run(command, stdout=output, stderr=subprocess.DEVNULL, timeout=120, check=False)
        if output.tell() > MAXIMUM_BYTES:
            raise ValueError("Unbounded tool report.")
        output.seek(0)
        return result.returncode, output.read(MAXIMUM_BYTES + 1)


def canonical_inputs(inputs: dict | None, sha: str, now: datetime, invoke=run_tool,
                     expected_tool_digest: str | None = None) -> dict:
    if inputs is None:
        return {"status": "UNAVAILABLE", "sourceSpecificationRevision": None, "inputDigests": {},
                "declaredDraftCoverage": None, "diagnosticCount": None, "unresolvedMappings": [],
                "unresolvedListTruncated": False, "diagnosticRuleCounts": {},
                "requiredDependency": "AUTHORIZED_PRIVATE_REGISTRY_MANIFEST_CONTRACT_SOURCE_AND_REVIEW"}
    required = {"registry", "manifest", "contracts", "spec_root", "spec_source_sha", "tool_assembly", "implementation_root"}
    if not required <= set(inputs) or set(inputs) - required - {"execution_links"} or not re.fullmatch(r"[a-f0-9]{40}", inputs["spec_source_sha"]):
        raise ValueError("Incomplete canonical input set.")
    artifacts = {key: digest(bounded_bytes(Path(inputs[key]))) for key in ("registry", "manifest", "contracts", "tool_assembly")}
    if expected_tool_digest is not None and artifacts["tool_assembly"] != expected_tool_digest:
        raise ValueError("Canonical adapter verifier differs from the copied candidate dependency.")
    command = ["dotnet", str(inputs["tool_assembly"]), "traceability-check", str(inputs["registry"]), str(inputs["manifest"]),
               str(inputs["contracts"]), str(inputs["spec_root"]), inputs["implementation_root"],
               inputs["spec_source_sha"], sha, now.isoformat()]
    if "execution_links" in inputs:
        artifacts["execution_links"] = digest(bounded_bytes(Path(inputs["execution_links"])))
        command.append(str(inputs["execution_links"]))
    exit_code, output = invoke(command)
    report = project_tool_result(output, sha)
    if (exit_code == 0) != (report["status"] == "STRUCTURALLY_VALID_DRAFT"):
        raise ValueError("Tool exit/metadata disagreement.")
    report.update(sourceSpecificationRevision=inputs["spec_source_sha"], inputDigests=artifacts,
                  requiredDependency="PERSONAL_SCOPED_MAPPING_APPROVAL_AND_TRUSTED_EXECUTION_RECONCILIATION")
    return report


def capture(root: Path, sha: str, now: datetime, architecture_trx: Path | None = None,
            backend_trx: Path | None = None, sec_arch_execution: Path | None = None,
            inputs: dict | None = None, run_id: str = "LOCAL", run_attempt: str = "1", invoke=run_tool) -> dict:
    def git(*arguments):
        return subprocess.check_output(["git", "-C", str(root), *arguments], text=True, timeout=30).strip()
    if not re.fullmatch(r"[a-f0-9]{40}", sha) or git("rev-parse", "HEAD") != sha or git("status", "--porcelain"):
        raise ValueError("Exact clean candidate required.")
    stamp = root / "artifacts/ci/dotnet-build-sha"
    binding = stamp.is_file() and bounded_bytes(stamp).decode().strip() == sha
    if stamp.is_file() and not binding:
        raise ValueError("Wrong build identity.")
    lanes = {"architecture": execution_lane(architecture_trx, now, binding),
             "backend": execution_lane(backend_trx, now, binding)}
    sec_arch = {"status": "UNAVAILABLE", "receiptDigest": None, "observedCases": None,
                "requiredRepresentativeCases": None, "observedKinds": None, "canonicalSpecMappings": "UNRESOLVED"}
    if sec_arch_execution is not None and sec_arch_execution.is_file():
        data = bounded_bytes(sec_arch_execution)
        receipt = read_json(data)
        validate_local_assemblies(root, receipt, execution=True)
        if (receipt["candidateSha"] != sha or receipt["runId"] != run_id or receipt["runAttempt"] != run_attempt or
                receipt["buildStampMatchesCandidate"] is not True or not binding or
                receipt["executionDigest"] != lanes["backend"]["executionDigest"]):
            raise ValueError("Wrong SEC-ARCH lane binding.")
        observed = receipt["observedExecution"]
        if backend_trx is None or observed != observed_trx(bounded_bytes(backend_trx), now):
            raise ValueError("Representative metadata differs from actual TRX observation.")
        kinds = dict.fromkeys(("tooling", "inventory", "representativeRuntime", "unclassified"), 0)
        for case in observed["cases"]:
            group = case["method"].split(".")[-2]
            kind = ("tooling" if group in TOOLING_GROUPS else "inventory" if group in INVENTORY_GROUPS else
                    "representativeRuntime" if group in RUNTIME_GROUPS else "unclassified")
            kinds[kind] += 1
        sec_arch = {"status": "REPRESENTATIVE_" + observed["outcome"], "receiptDigest": digest(data),
                    "observedCases": observed["observedCaseCount"], "requiredRepresentativeCases": observed["requiredCaseCount"],
                    "observedKinds": kinds, "canonicalSpecMappings": "UNRESOLVED"}
    if inputs is not None:
        inputs = {**inputs, "implementation_root": str(root)}
    copied_tool = capture_assemblies(root)["Coglatas.SecurityArchitecture"] if inputs is not None else None
    registry = canonical_inputs(inputs, sha, now, invoke, expected_tool_digest=copied_tool)
    if git("rev-parse", "HEAD") != sha or git("status", "--porcelain"):
        raise ValueError("Candidate changed during capture.")
    return {"schemaVersion": 1, "verifierId": "specification-contract", "verifierVersion": "1", "rollout": "ADVISORY",
            "candidateSha": sha, "runId": run_id, "runAttempt": run_attempt, "capturedAtUtc": now.isoformat(),
            "sourceBinding": "BUILD_STAMP_BOUND_PENDING_TRUSTED_PRODUCER_RECONCILIATION" if binding else "UNVERIFIED",
            "canonicalInputs": registry, "existingVerifierLanes": lanes, "secArchRepresentativeEvidence": sec_arch,
            "qualifiedNormativeRequirementCount": None, "qualifiedNormativeRelationshipCount": None,
            "ownerApproval": "UNVERIFIED", "trustedExecutionAttestation": "UNVERIFIED", "normativeReady": False,
            "preAvaloniaVerdict": "PRE-AVALONIA SEC-ARCH: BLOCKED",
            "blindSpots": ["CANONICAL_INPUT_ABSENCE_IS_UNKNOWN_COVERAGE", "DRAFT_ALLOCATION_IS_NOT_APPROVED_NORMATIVE_AUTHORITY",
                           "LANE_COUNTS_ARE_NOT_SPEC_CONTRACT_COVERAGE", "SYNTHETIC_TOOLING_CONTROLS_ARE_NOT_PRODUCT_ACCEPTANCE",
                           "SELF_REPORTED_LINKS_AND_BUILD_STAMPS_REQUIRE_INDEPENDENT_PROVENANCE",
                           "HISTORICAL_V1_EXECUTION_FULL_DEPENDENCY_QUALIFICATION_IS_UNVERIFIED",
                           "PERSONAL_SCOPED_OWNER_APPROVAL_IS_SEPARATE_FROM_ORDINARY_CODE_REVIEW"]}


def markdown(report: dict) -> str:
    lines = ["### specification-contract: Advisory", "", "Candidate: `" + report["candidateSha"] + "`.",
             "Canonical input status: **" + report["canonicalInputs"]["status"] + "**.",
             "Specification source revision: `" + (report["canonicalInputs"]["sourceSpecificationRevision"] or "UNKNOWN") + "`.",
             "Pending dependency: `" + report["canonicalInputs"]["requiredDependency"] + "`.",
             "Qualified normative coverage: **UNVERIFIED**. PRE-AVALONIA SEC-ARCH: **BLOCKED**.", "",
             "| Existing lane | Observed status | Passed | Failed | Unexecuted | Canonical linkage |",
             "| --- | --- | ---: | ---: | ---: | --- |"]
    for name, lane in report["existingVerifierLanes"].items():
        lines.append("| " + name + " | " + lane["status"] + " | " + str(lane["passed"]) + " | " + str(lane["failed"]) + " | " + str(lane["unexecuted"]) + " | UNRESOLVED |")
    coverage = report["canonicalInputs"]["declaredDraftCoverage"]
    if coverage is not None:
        lines.extend(["", "Declared Draft registry schema/version: " + str(coverage["schemaVersion"]) + "/" + str(coverage["registryVersion"]) + ".",
                      "Active/deprecated/retired entries: " + str(coverage["activeRequirements"]) + "/" + str(coverage["deprecatedRequirements"]) + "/" + str(coverage["retiredRequirements"]) + ".",
                      "Unresolved execution links: " + str(coverage["unresolvedLinks"]) + "; entries with known limitations: " + str(coverage["requirementsWithKnownLimitations"]) + "."])
    lines.extend(["", "Lane results and synthetic tooling controls do not establish canonical SPEC coverage or personal approval."])
    return "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidate-sha", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--markdown", type=Path)
    for flag in ("architecture-trx", "backend-trx", "sec-arch-execution", "registry", "manifest", "contracts", "spec-root", "tool-assembly", "execution-links"):
        parser.add_argument("--" + flag, type=Path)
    parser.add_argument("--spec-source-sha")
    args = parser.parse_args()
    inputs = {key: getattr(args, key) for key in ("registry", "manifest", "contracts", "spec_root", "spec_source_sha", "tool_assembly", "execution_links") if getattr(args, key) is not None}
    if args.output.exists() or args.markdown is not None and args.markdown.exists():
        print("specification-contract ERROR: retained output cannot be replaced.")
        return 1
    try:
        report = capture(Path(__file__).resolve().parents[2], args.candidate_sha, datetime.now(timezone.utc),
                         args.architecture_trx, args.backend_trx, args.sec_arch_execution, inputs or None,
                         os.environ.get("GITHUB_RUN_ID", "LOCAL"), os.environ.get("GITHUB_RUN_ATTEMPT", "1"))
        text = markdown(report)
        code = 1 if report["canonicalInputs"]["status"] == "INVALID_OR_INCOMPLETE" else 0
    except (ValueError, KeyError, TypeError, OSError, ET.ParseError, subprocess.SubprocessError):
        report = {"schemaVersion": 1, "verifierId": "specification-contract", "rollout": "ADVISORY", "status": "ERROR",
                  "candidateSha": args.candidate_sha if re.fullmatch(r"[a-f0-9]{40}", args.candidate_sha) else None,
                  "normativeReady": False, "diagnostic": "INVALID_OR_UNAVAILABLE_CANDIDATE_SOURCE_VERIFIER_OR_EXECUTION_INPUT",
                  "preAvaloniaVerdict": "PRE-AVALONIA SEC-ARCH: BLOCKED"}
        text = "### specification-contract: Advisory\n\n**ERROR**: source/verifier/evidence integrity could not be resolved. Pre-Avalonia remains BLOCKED.\n"
        code = 1
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("x", encoding="utf-8") as destination:
        destination.write(json.dumps(report, indent=2) + "\n")
    if args.markdown is not None:
        args.markdown.parent.mkdir(parents=True, exist_ok=True)
        with args.markdown.open("x", encoding="utf-8") as destination:
            destination.write(text)
    print("specification-contract: Advisory; canonical authority UNVERIFIED; pre-Avalonia BLOCKED")
    return code


if __name__ == "__main__":
    raise SystemExit(main())
