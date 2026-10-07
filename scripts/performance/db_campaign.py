#!/usr/bin/env python3
"""Predeclared bounded DB baseline campaigns; capture never approves a baseline."""
from __future__ import annotations

import argparse
import ast
from contextlib import contextmanager
import datetime as dt
import hashlib
import importlib.util
import json
import math
import os
import re
import subprocess
import sys
import tempfile
import urllib.request
from pathlib import Path
from typing import Any

from common import DB_FIXTURE_VERSION, PerformanceContractError, fixture_hash, load_json, repository_root, write_json_atomic
from compare import _compatibility_payload, legacy_environment_compatibility_key as environment_compatibility_key, summarize
from db_gate import validate_capture, validate_contract

POLICY_VERSION = "perf05-db-campaign-v1"
ASSIGNMENT_RULE_VERSION = "perf05-environment-assignment-v1"
ASSIGNMENT_CAUSE = "environment-assignment-transition"
RECOVERY_RULE_VERSION = "perf05-zero-capture-recovery-v1"
RECOVERY_CAUSE = "premeasurement-validator-recovery"
RECOVERY_FAILURE_STEP = "Freeze premeasurement campaign identity and reject retries"
RECOVERY_MEASUREMENT_STEP = "Execute only the predeclared serial capture groups"
RECOVERY_JOB_NAME = "Predeclared bounded DB baseline campaign"
POLICY_DIRECTORY = "performance/baseline-campaigns/policies"
ASSIGNMENT_POLICY_PATH = f"{POLICY_DIRECTORY}/{ASSIGNMENT_RULE_VERSION}.json"
ASSIGNMENT_FIELDS = {"ruleVersion", "ruleSourceSha", "ruleApprovalReference", "predecessorArtifact", "predecessorRawGroupsSha256"}
RECOVERY_FIELDS = {"ruleVersion", "ruleSourceSha", "failedRunId", "failedDeclarationSha", "failedManifestSha256",
                   "failedRunCreatedAtUtc", "failedRunCompletedAtUtc", "failureStep"}
MANIFEST_DIRECTORY = "performance/baseline-campaigns/manifests"
WORKFLOW = ".github/workflows/performance-db-baseline-capture.yml"
CONTRACT_FILES = ("performance/datasets.json", "performance/db-scenarios.json", "performance/environment.json",
                  "performance/scenarios.json", "performance/budgets.json", "performance/comparison-policy.json")
TOOL_FILES = ("scripts/performance/db-probe.py", "scripts/performance/db_gate.py", "scripts/performance/db-compare.py", "scripts/performance/compare.py",
              "scripts/performance/common.py", "scripts/performance/collect-environment.py",
              "scripts/performance/with-environment.sh", "src/Coglatas.Infrastructure/Persistence/PerformanceDbCapture.cs")
MANIFEST_FIELDS = {"schemaVersion", "policyVersion", "campaignId", "createdAtUtc", "expiresAtUtc", "profile",
                   "environmentCompatibilityKey", "sourceSha", "sourceRef", "fixtureIdentity", "toolVersions",
                   "toolSourceDigests", "contractDigests", "scenarioSet", "sampleCount", "maxCaptureGroups",
                   "stabilityRule", "selectionAlgorithm", "earlyStopPolicy", "eligibilityRule", "authorization"}
PUBLIC_DIGEST_FIELD = "environmentCompatibilityDigest"


def campaign_environment_digest(manifest: dict) -> str:
    """The public SHA-256 is not a credential; retain immutable v1 readers."""
    field = PUBLIC_DIGEST_FIELD if manifest.get("schemaVersion") in (2, 3, 4) else "environmentCompatibilityKey"
    return manifest[field]


def require(condition: bool, code: str) -> None:
    if not condition:
        raise PerformanceContractError(code)


def digest(value: Any) -> str:
    """Canonical digest preserves every array element and its original order."""
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False,
                                     allow_nan=False).encode()).hexdigest()


def file_digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def is_sha(value: Any, length: int = 64) -> bool:
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{" + str(length) + "}", value) is not None


def utc(value: Any) -> dt.datetime:
    require(isinstance(value, str), "invalid-utc-timestamp")
    parsed = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    require(parsed.utcoffset() == dt.timedelta(0), "timestamp-must-be-utc")
    return parsed


def now_utc() -> str:
    return dt.datetime.now(dt.timezone.utc).isoformat()


def git(root: Path, *arguments: str) -> str:
    result = subprocess.run(["git", *arguments], cwd=root, text=True, capture_output=True, check=False)
    require(result.returncode == 0, "campaign-git-evidence-unavailable")
    return result.stdout.strip()


def git_document(root: Path, sha: str, path: str) -> dict:
    return json.loads(git(root, "show", f"{sha}:{path}"))


def ancestor(root: Path, before: str, after: str) -> None:
    result = subprocess.run(["git", "merge-base", "--is-ancestor", before, after], cwd=root, capture_output=True)
    require(result.returncode == 0, "campaign-source-not-approved-main-history")


@contextmanager
def source_snapshot(root: Path, source_sha: str):
    """Read pinned validation inputs without executing or checking out product code."""
    with tempfile.TemporaryDirectory(prefix="perf05-campaign-contract-") as temporary:
        snapshot = Path(temporary)
        for path in set(TOOL_FILES) | set(CONTRACT_FILES):
            target = snapshot / path
            target.parent.mkdir(parents=True, exist_ok=True)
            result = subprocess.run(["git", "show", f"{source_sha}:{path}"], cwd=root, capture_output=True, check=False)
            require(result.returncode == 0, "campaign-source-contract-unavailable")
            target.write_bytes(result.stdout)
        yield snapshot


def tools_from_fingerprint(fp: dict) -> dict:
    expected = {"schemaVersion", "phase", "capturedAtUtc", "commitSha", "runner", "dotnet", "node", "postgresql", "browser", "containerImages", "fixture"}
    require(set(fp) in (expected, expected | {"applicationRuntime"}, expected | {"environmentClass"},
                        expected | {"applicationRuntime", "environmentClass"}), "unsafe-fingerprint-fields")
    for field, fields in {"runner": {"os", "runnerOs", "runnerImage", "cpuCount", "cpuModel", "memoryBytes"},
                          "dotnet": {"sdkInfo", "runtimeInfo"}, "node": {"version", "npmVersion"},
                          "postgresql": {"version"}, "browser": {"playwrightVersion", "version"},
                          "containerImages": {"app", "postgres", "performanceBrowser"},
                          "fixture": {"profile", "seed", "hash", "version"}}.items():
        optional_runner = {"provider", "runnerClass", "architecture", "osFamily", "osVersionClass", "microcode"} if field == "runner" else set()
        require(isinstance(fp[field], dict) and fields <= set(fp[field]) <= fields | optional_runner, "unsafe-fingerprint-section")
    _compatibility_payload(fp)
    return {"dotnetRuntime": fp["dotnet"]["runtimeInfo"], "node": fp["node"]["version"],
            "postgresql": fp["postgresql"]["version"], "playwright": fp["browser"]["playwrightVersion"],
            "browser": fp["browser"]["version"]}


def scenario_set(contract: dict) -> list[dict]:
    return [{"scenario": s["id"], "pageSize": 5 if s["paged"] else 0, "metric": "db.total_time_ms", "unit": "ms"}
            for s in contract["scenarios"]]


def pinned_db_fixture_version(root: Path) -> int:
    assignments = [node for node in ast.parse((root / "scripts/performance/common.py").read_text()).body
                   if isinstance(node, ast.Assign) and any(isinstance(t, ast.Name) and t.id == "DB_FIXTURE_VERSION" for t in node.targets)]
    require(len(assignments) == 1, "pinned-db-fixture-version-unavailable")
    version = ast.literal_eval(assignments[0].value)
    require(type(version) is int and version > 0, "pinned-db-fixture-version-invalid")
    return version


def validate_manifest(manifest: dict | None, root: Path, *, current_time: str | None = None) -> None:
    require(isinstance(manifest, dict), "missing-or-invalid-campaign-manifest")
    version = manifest.get("schemaVersion")
    fields = (MANIFEST_FIELDS - {"environmentCompatibilityKey"}) | {PUBLIC_DIGEST_FIELD} if version in (2, 3, 4) else MANIFEST_FIELDS
    if version in (3, 4):
        fields |= {"environmentAssignmentTransition"}
    if version == 4:
        fields |= {"premeasurementRecovery"}
    require(set(manifest) == fields, "missing-or-invalid-campaign-manifest")
    require(type(version) is int and version in (1, 2, 3, 4) and manifest["policyVersion"] == POLICY_VERSION, "campaign-policy-mismatch")
    require(isinstance(manifest["campaignId"], str) and re.fullmatch(r"[a-z0-9][a-z0-9-]{7,79}", manifest["campaignId"]) is not None,
            "invalid-campaign-id")
    require(manifest["profile"] in ("small", "medium"), "invalid-campaign-profile")
    require(is_sha(campaign_environment_digest(manifest)) and is_sha(manifest["sourceSha"], 40)
            and manifest["sourceRef"] == "refs/heads/main", "invalid-campaign-source-or-environment")
    created, expires = utc(manifest["createdAtUtc"]), utc(manifest["expiresAtUtc"])
    require(created < expires and expires - created <= dt.timedelta(days=7), "invalid-campaign-expiry")
    if current_time:
        require(created <= utc(current_time) < expires, "campaign-not-current")
    contract = load_json(root / "performance/db-scenarios.json")
    validate_contract(contract, load_json(root / "performance/scenarios.json"))
    require(manifest["scenarioSet"] == scenario_set(contract), "campaign-scenario-inventory-or-order-changed")
    require(type(manifest["sampleCount"]) is int and manifest["sampleCount"] == contract["policy"]["samples"],
            "campaign-sample-count-changed")
    require(type(manifest["maxCaptureGroups"]) is int and 1 <= manifest["maxCaptureGroups"] <= 3,
            "campaign-capture-bound-invalid")
    policy = load_json(root / "performance/comparison-policy.json")
    require(manifest["stabilityRule"] == {"indicator": "relative-mad", "maximum": policy["variability"]["defaultRelativeMadMax"],
                                            "comparatorSha256": file_digest(root / "scripts/performance/compare.py")},
            "campaign-stability-rule-changed")
    require(manifest["selectionAlgorithm"] == "earliest-eligible-stable-complete"
            and manifest["earlyStopPolicy"] in ("never", "first-eligible-stable-complete")
            and manifest["eligibilityRule"] == "exact-source-environment-fixture-toolchain-complete-structural-pass",
            "campaign-selection-policy-invalid")
    fixture = manifest["fixtureIdentity"]
    fixture_version = pinned_db_fixture_version(root)
    require(fixture == {"profile": manifest["profile"], "hash": fixture_hash(manifest["profile"], root / "performance/datasets.json", fixture_version=fixture_version),
                        "version": fixture_version, "manifestSha256": file_digest(root / "performance/datasets.json")},
            "campaign-fixture-changed")
    tool_versions = manifest["toolVersions"]
    require(isinstance(tool_versions, dict) and set(tool_versions) == {"dotnetRuntime", "node", "postgresql", "playwright", "browser"}
            and all(isinstance(v, str) and v.strip() for v in tool_versions.values()), "campaign-tool-versions-missing")
    for field, paths in (("toolSourceDigests", TOOL_FILES), ("contractDigests", CONTRACT_FILES)):
        require(manifest[field] == {p: file_digest(root / p) for p in paths}, "campaign-tool-or-contract-drift")
    auth = manifest["authorization"]
    require(isinstance(auth, dict) and set(auth) == {"approver", "reference", "reason", "cause", "supersedesCampaignId"},
            "campaign-authorization-missing")
    require(isinstance(auth["approver"], str) and re.fullmatch(r"[A-Za-z0-9_.-]{1,80}", auth["approver"]) is not None
            and isinstance(auth["reference"], str) and re.fullmatch(r"https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/(issues|pull)/[1-9][0-9]*(#[A-Za-z0-9_-]+)?", auth["reference"]) is not None
            and isinstance(auth["reason"], str) and len(auth["reason"].strip()) >= 40, "campaign-review-evidence-missing")
    causes = ((ASSIGNMENT_CAUSE,) if version == 3 else (RECOVERY_CAUSE,) if version == 4
              else ("initial-governance-campaign", "accepted-product-change", "measurement-correction"))
    require(auth["cause"] in causes,
            "campaign-result-only-retry-forbidden")
    require((auth["cause"] == "initial-governance-campaign" and auth["supersedesCampaignId"] is None)
            or (auth["cause"] != "initial-governance-campaign" and isinstance(auth["supersedesCampaignId"], str)
                and auth["supersedesCampaignId"] != manifest["campaignId"]), "campaign-predecessor-invalid")
    if version in (3, 4):
        transition = manifest["environmentAssignmentTransition"]
        require(isinstance(transition, dict) and set(transition) == ASSIGNMENT_FIELDS
                and transition["ruleVersion"] == ASSIGNMENT_RULE_VERSION
                and isinstance(transition["ruleApprovalReference"], str)
                and re.fullmatch(r"https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/(issues|pull)/[1-9][0-9]*#issuecomment-[1-9][0-9]*", transition["ruleApprovalReference"]) is not None
                and is_sha(transition["ruleSourceSha"], 40) and is_sha(transition["predecessorRawGroupsSha256"]),
                "assignment-transition-identity-invalid")
        if version == 3:
            validate_artifact_identity(transition["predecessorArtifact"], auth["supersedesCampaignId"])
        else:
            validate_artifact_identity(transition["predecessorArtifact"], None)
            recovery = manifest["premeasurementRecovery"]
            require(isinstance(recovery, dict) and set(recovery) == RECOVERY_FIELDS
                    and recovery["ruleVersion"] == RECOVERY_RULE_VERSION
                    and is_sha(recovery["ruleSourceSha"], 40)
                    and type(recovery["failedRunId"]) is int and recovery["failedRunId"] > 0
                    and is_sha(recovery["failedDeclarationSha"], 40) and is_sha(recovery["failedManifestSha256"])
                    and recovery["failureStep"] == RECOVERY_FAILURE_STEP,
                    "premeasurement-recovery-identity-invalid")
            utc(recovery["failedRunCreatedAtUtc"])
            utc(recovery["failedRunCompletedAtUtc"])


def validate_artifact_identity(artifact: dict, campaign_id: str | None) -> None:
    require(isinstance(artifact, dict) and set(artifact) == {"id", "name", "digest", "workflowRunId"}
            and type(artifact["id"]) is int and artifact["id"] > 0
            and type(artifact["workflowRunId"]) is int and artifact["workflowRunId"] > 0
            and isinstance(artifact["name"], str) and artifact["name"].startswith("perf05-campaign-")
            and (campaign_id is None or artifact["name"] == "perf05-campaign-" + campaign_id)
            and isinstance(artifact["digest"], str) and artifact["digest"].startswith("sha256:") and is_sha(artifact["digest"][7:]),
            "assignment-predecessor-artifact-invalid")


def review_comment(repository: str, reference: str, api) -> dict:
    match = re.fullmatch(r"https://github\.com/([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)/(issues|pull)/[1-9][0-9]*#issuecomment-([1-9][0-9]*)", reference)
    require(match is not None and match[1] == repository, "assignment-owner-reference-invalid")
    return api(repository, f"/issues/comments/{match[3]}")


def has_negative_approval_directive(lines: list[str]) -> bool:
    return any(re.fullmatch(r"\s*(?:NOT[ _]APPROVED|REJECTED|REVOKED)(?:\s+.*)?\s*", line, re.IGNORECASE)
               for line in lines)


def validate_assignment_policy(policy: dict, *, repository: str, api) -> None:
    require(isinstance(policy, dict) and set(policy) == {"schemaVersion", "ruleVersion", "decisionProposalHeadSha", "authorization"}
            and type(policy["schemaVersion"]) is int and policy["schemaVersion"] == 1 and policy["ruleVersion"] == ASSIGNMENT_RULE_VERSION
            and is_sha(policy["decisionProposalHeadSha"], 40), "assignment-policy-identity-invalid")
    auth = policy["authorization"]
    require(isinstance(auth, dict) and set(auth) == {"approver", "reference", "approvedAtUtc"}, "assignment-policy-approval-invalid")
    owner = api(repository, "")["owner"]["login"]
    review = review_comment(repository, auth["reference"], api)
    lines = review.get("body", "").splitlines()
    require(auth["approver"] == owner == review.get("user", {}).get("login")
            and "OWNER_RULE_APPROVAL_RECORDED" in lines and "RULE_VERSION " + ASSIGNMENT_RULE_VERSION in lines
            and policy["decisionProposalHeadSha"] in review.get("body", "")
            and not has_negative_approval_directive(lines)
            and utc(review["created_at"]) <= utc(review["updated_at"]) <= utc(auth["approvedAtUtc"]),
            "assignment-rule-owner-approval-missing-or-late")


def failed_archive(root: Path, artifact: dict, declaration: dict) -> dict:
    path = root / "scripts/ci/verify-performance-db-campaign-baselines.py"
    spec = importlib.util.spec_from_file_location("assignment_archive_validator", path)
    require(spec is not None and spec.loader is not None, "assignment-archive-validator-unavailable")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.trusted_failed_artifact(os.environ.get("GITHUB_REPOSITORY", "NYGsatoshi/Coglatas"), artifact, declaration)


def git_file_digest(root: Path, sha: str, path: str) -> str:
    original = subprocess.run(["git", "cat-file", "blob", f"{sha}:{path}"], cwd=root, capture_output=True, check=False)
    require(original.returncode == 0, "assignment-capture-workflow-not-retained")
    return hashlib.sha256(original.stdout).hexdigest()


def validate_assignment_transition(root: Path, manifest: dict, predecessor: dict, *, base_ref: str, introduced: bool) -> None:
    transition = manifest["environmentAssignmentTransition"]
    require(predecessor["authorization"]["cause"] not in (ASSIGNMENT_CAUSE, RECOVERY_CAUSE), "assignment-transition-chain-forbidden")
    fixed = MANIFEST_FIELDS - {"schemaVersion", "campaignId", "createdAtUtc", "expiresAtUtc", "environmentCompatibilityKey", "authorization"}
    require(all(manifest[k] == predecessor[k] for k in fixed), "assignment-transition-measurement-contract-changed")
    require(manifest["authorization"]["reference"] not in (predecessor["authorization"]["reference"], transition["ruleApprovalReference"]),
            "assignment-approval-cannot-transfer")
    require(manifest["expiresAtUtc"] != predecessor["expiresAtUtc"], "assignment-requires-new-expiry")
    ancestor(root, transition["ruleSourceSha"], base_ref)
    require(transition["ruleSourceSha"] in git(root, "rev-list", "--first-parent", base_ref).splitlines(),
            "assignment-rule-must-be-main-rollout")
    policy = git_document(root, transition["ruleSourceSha"], ASSIGNMENT_POLICY_PATH)
    require(policy["ruleVersion"] == ASSIGNMENT_RULE_VERSION
            and transition["ruleApprovalReference"] == policy["authorization"]["reference"]
            and manifest["authorization"]["approver"] == policy["authorization"]["approver"], "assignment-rule-reference-mismatch")
    additions = git(root, "diff", "--name-only", "--diff-filter=A", transition["ruleSourceSha"] + "^", transition["ruleSourceSha"]).splitlines()
    require(ASSIGNMENT_POLICY_PATH in additions
            and f'ASSIGNMENT_RULE_VERSION = "{ASSIGNMENT_RULE_VERSION}"' in git(root, "show", f"{transition['ruleSourceSha']}:scripts/performance/db_campaign.py"),
            "assignment-rule-must-roll-out-before-declaration")
    rolled_out = dt.datetime.fromtimestamp(int(git(root, "show", "-s", "--format=%ct", transition["ruleSourceSha"])), dt.timezone.utc)
    require(utc(policy["authorization"]["approvedAtUtc"]) <= rolled_out <= utc(manifest["createdAtUtc"]),
            "assignment-declaration-before-rule-rollout")
    evidence = root / "performance/baseline-campaigns/evidence" / predecessor["campaignId"]
    names = ("manifest.json", "declaration.json", "raw-groups.json", "campaign-result.json")
    require(all((evidence / n).is_file() for n in names), "assignment-predecessor-evidence-missing")
    documents = {n: load_json(evidence / n) for n in names}
    require(documents["manifest.json"] == predecessor, "assignment-predecessor-manifest-changed")
    if introduced:
        validate_assignment_policy(policy, repository=os.environ.get("GITHUB_REPOSITORY", "NYGsatoshi/Coglatas"), api=github_api)
        for name in names:
            path = (evidence / name).relative_to(root).as_posix()
            require(git_document(root, base_ref, path) == documents[name], "assignment-predecessor-must-already-be-retained-main")
    declaration, groups = documents["declaration.json"], documents["raw-groups.json"]["groups"]
    artifact = transition["predecessorArtifact"]
    require(load_json(evidence / "archive-metadata.json") == artifact, "assignment-predecessor-archive-identity-changed")
    require(artifact["workflowRunId"] == declaration["workflowRunId"]
            and digest(groups) == transition["predecessorRawGroupsSha256"], "assignment-predecessor-provenance-changed")
    ancestor(root, declaration["declarationSha"], base_ref)
    ancestor(root, predecessor["sourceSha"], declaration["declarationSha"] + "^")
    workflow_digest = git_file_digest(root, transition["ruleSourceSha"], WORKFLOW)
    require(git_file_digest(root, declaration["declarationSha"], WORKFLOW) == workflow_digest
            and (not introduced or file_digest(root / WORKFLOW) == workflow_digest), "assignment-capture-workflow-changed")
    with source_snapshot(root, predecessor["sourceSha"]) as snapshot:
        recomputed = select_campaign(predecessor, declaration, groups, snapshot)
        require(recomputed == documents["campaign-result.json"] and recomputed["decision"] == "BASELINE_UNAVAILABLE"
                and recomputed["selectedGroupOrdinal"] is None and recomputed["approved"] is False,
                "assignment-predecessor-not-exhausted-rejection")
        observed = []
        comparison = source_comparator(snapshot)
        for group in groups:
            require(group["captureExitCodes"] == {"small": 0, "medium": 0}
                    and all(p["collectionComplete"] is True for p in group["profiles"].values()), "assignment-predecessor-incomplete")
            fp = group["fingerprints"][predecessor["profile"]]
            require(tools_from_fingerprint(fp) == predecessor["toolVersions"]
                    and {"profile": fp["fixture"]["profile"], "hash": fp["fixture"]["hash"], "version": fp["fixture"]["version"],
                         "manifestSha256": file_digest(snapshot / "performance/datasets.json")} == predecessor["fixtureIdentity"],
                    "assignment-tool-or-fixture-drift")
            historical_key = getattr(comparison, "legacy_environment_compatibility_key", comparison.environment_compatibility_key)
            observed.append(historical_key(fp))
    require(len(groups) == predecessor["maxCaptureGroups"] and len(set(observed)) == 1
            and observed[0] != campaign_environment_digest(predecessor)
            and observed[0] == campaign_environment_digest(manifest), "assignment-must-use-entire-consistent-observed-scope")
    require(utc(manifest["createdAtUtc"]) > max(utc(g["endedAtUtc"]) for g in groups), "assignment-declared-before-predecessor-completion")
    if introduced:
        require(failed_archive(root, artifact, declaration) == documents, "assignment-retained-evidence-not-authenticated-archive")


def validate_zero_capture_recovery_run(recovery: dict, run: dict, jobs: list[dict], artifacts: list[dict]) -> None:
    require(run.get("id") == recovery["failedRunId"]
            and run.get("head_sha") == recovery["failedDeclarationSha"]
            and run.get("head_branch") == "main" and run.get("event") == "push"
            and run.get("path") == WORKFLOW and run.get("run_attempt") == 1
            and run.get("status") == "completed" and run.get("conclusion") == "failure"
            and run.get("created_at") == recovery["failedRunCreatedAtUtc"]
            and run.get("updated_at") == recovery["failedRunCompletedAtUtc"],
            "premeasurement-recovery-run-provenance-invalid")
    matching = [job for job in jobs if job.get("name") == RECOVERY_JOB_NAME]
    require(len(matching) == 1 and matching[0].get("status") == "completed" and matching[0].get("conclusion") == "failure",
            "premeasurement-recovery-job-provenance-invalid")
    steps = {step.get("name"): step for step in matching[0].get("steps", [])}
    require(steps.get("Set up job", {}).get("conclusion") == "success"
            and steps.get("Checkout independently rolled-out main policy and declaration", {}).get("conclusion") == "success"
            and steps.get("Test fail-closed campaign and DB validators", {}).get("conclusion") == "success"
            and steps.get(RECOVERY_FAILURE_STEP, {}).get("conclusion") == "failure",
            "premeasurement-recovery-not-validator-only-failure")
    require(steps.get(RECOVERY_MEASUREMENT_STEP, {}).get("conclusion") == "skipped",
            "premeasurement-recovery-measurement-already-started")
    require(not artifacts, "premeasurement-recovery-artifacts-exist")


def validate_premeasurement_recovery(root: Path, manifest: dict, predecessor: dict, *, base_ref: str, introduced: bool) -> None:
    recovery = manifest["premeasurementRecovery"]
    require(predecessor.get("schemaVersion") == 3 and predecessor["authorization"]["cause"] == ASSIGNMENT_CAUSE,
            "premeasurement-recovery-predecessor-invalid")
    fixed = MANIFEST_FIELDS - {"schemaVersion", "campaignId", "createdAtUtc", "expiresAtUtc", "environmentCompatibilityKey", "authorization"}
    require(all(manifest[k] == predecessor[k] for k in fixed)
            and campaign_environment_digest(manifest) == campaign_environment_digest(predecessor)
            and manifest["environmentAssignmentTransition"] == predecessor["environmentAssignmentTransition"],
            "premeasurement-recovery-contract-changed")
    require(manifest["authorization"]["supersedesCampaignId"] == predecessor["campaignId"]
            and manifest["authorization"]["reference"] not in
            (predecessor["authorization"]["reference"], manifest["environmentAssignmentTransition"]["ruleApprovalReference"]),
            "premeasurement-recovery-approval-cannot-transfer")
    require(manifest["expiresAtUtc"] != predecessor["expiresAtUtc"]
            and recovery["failedManifestSha256"] == digest(predecessor),
            "premeasurement-recovery-identity-mismatch")
    path = f"{MANIFEST_DIRECTORY}/{predecessor['campaignId']}.json"
    introductions = git(root, "log", "--first-parent", "--diff-filter=A", "--format=%H", base_ref, "--", path).splitlines()
    require(introductions == [recovery["failedDeclarationSha"]], "premeasurement-recovery-declaration-provenance-invalid")
    ancestor(root, recovery["failedDeclarationSha"], recovery["ruleSourceSha"])
    ancestor(root, recovery["ruleSourceSha"], base_ref)
    require(recovery["ruleSourceSha"] in git(root, "rev-list", "--first-parent", base_ref).splitlines()
            and f'RECOVERY_RULE_VERSION = "{RECOVERY_RULE_VERSION}"' in
            git(root, "show", f"{recovery['ruleSourceSha']}:scripts/performance/db_campaign.py"),
            "premeasurement-recovery-rule-must-be-main-rollout")
    rollout_time = dt.datetime.fromtimestamp(int(git(root, "show", "-s", "--format=%ct", recovery["ruleSourceSha"])), dt.timezone.utc)
    require(utc(recovery["failedRunCompletedAtUtc"]) <= rollout_time <= utc(manifest["createdAtUtc"]),
            "premeasurement-recovery-declared-before-rule-rollout")
    workflow_digest = git_file_digest(root, recovery["failedDeclarationSha"], WORKFLOW)
    require(git_file_digest(root, recovery["ruleSourceSha"], WORKFLOW) == workflow_digest
            and (not introduced or file_digest(root / WORKFLOW) == workflow_digest),
            "premeasurement-recovery-capture-workflow-changed")
    require(utc(manifest["createdAtUtc"]) > utc(recovery["failedRunCompletedAtUtc"]),
            "premeasurement-recovery-declared-before-failed-run-completed")
    if introduced:
        repository = os.environ.get("GITHUB_REPOSITORY", "NYGsatoshi/Coglatas")
        run = github_api(repository, f"/actions/runs/{recovery['failedRunId']}")
        jobs = github_api(repository, f"/actions/runs/{recovery['failedRunId']}/jobs?per_page=100").get("jobs", [])
        artifacts = github_api(repository, f"/actions/runs/{recovery['failedRunId']}/artifacts?per_page=100").get("artifacts", [])
        validate_zero_capture_recovery_run(recovery, run, jobs, artifacts)


def validate_transition_review(manifest: dict, review: dict, run: dict, owner: str) -> None:
    lines = review.get("body", "").splitlines()
    require(review.get("user", {}).get("login") == manifest["authorization"]["approver"] == owner
            and "APPROVED_PREMEASUREMENT" in lines
            and "CAMPAIGN_ID " + manifest["campaignId"] in lines
            and "MANIFEST_SHA256 " + digest(manifest) in lines
            and not has_negative_approval_directive(lines)
            and utc(review["created_at"]) <= utc(review["updated_at"]) <= utc(run["created_at"]),
            "assignment-campaign-explicit-owner-premeasurement-approval-missing")


def validate_registry(root: Path, manifests: list[dict], *, main_sha: str | None = None, base_ref: str | None = None, introduced_ids: set[str] | None = None) -> None:
    ids = [m["campaignId"] for m in manifests]
    require(len(ids) == len(set(ids)), "duplicate-campaign-id")
    prior: dict[tuple[str, str], dict] = {}
    by_id: dict[str, dict] = {}
    latest_profile: dict[str, dict] = {}
    transitioned_epochs: set[str] = set()
    introduced_ids = set(ids) if introduced_ids is None else introduced_ids
    historical_profiles = {m["profile"] for m in manifests if m["campaignId"] not in introduced_ids}
    for manifest in sorted(manifests, key=lambda m: utc(m["createdAtUtc"])):
        scope = (manifest["profile"], campaign_environment_digest(manifest))
        previous = prior.get(scope)
        auth = manifest["authorization"]
        if auth["cause"] == RECOVERY_CAUSE:
            predecessor = by_id.get(auth["supersedesCampaignId"])
            require(predecessor is not None and latest_profile.get(manifest["profile"]) == predecessor
                    and previous == predecessor, "premeasurement-recovery-must-bind-latest-consumed-campaign")
            require(predecessor["authorization"]["cause"] == ASSIGNMENT_CAUSE,
                    "premeasurement-recovery-chain-forbidden")
            require(auth["reference"] not in {p["authorization"]["reference"] for p in by_id.values()},
                    "premeasurement-recovery-approval-cannot-transfer")
            require(base_ref is not None, "premeasurement-recovery-requires-approved-main-context")
            validate_premeasurement_recovery(root, manifest, predecessor, base_ref=base_ref,
                                             introduced=manifest["campaignId"] in introduced_ids)
        elif auth["cause"] == ASSIGNMENT_CAUSE:
            predecessor = by_id.get(auth["supersedesCampaignId"])
            require(predecessor is not None and latest_profile.get(manifest["profile"]) == predecessor,
                    "assignment-must-bind-latest-profile-predecessor")
            require(previous is None and predecessor["campaignId"] not in transitioned_epochs, "assignment-cycle-or-epoch-reset-forbidden")
            require(auth["reference"] not in {p["authorization"]["reference"] for p in by_id.values()}, "assignment-approval-cannot-transfer")
            require(base_ref is not None, "assignment-requires-approved-main-context")
            validate_assignment_transition(root, manifest, predecessor, base_ref=base_ref, introduced=manifest["campaignId"] in introduced_ids)
            transitioned_epochs.add(predecessor["campaignId"])
        elif previous:
            require(auth["supersedesCampaignId"] == previous["campaignId"] and auth["cause"] != "initial-governance-campaign",
                    "new-campaign-cannot-reset-exhausted-scope")
            require(manifest["sourceSha"] != previous["sourceSha"], "same-source-campaign-retry-forbidden")
            if main_sha:
                ancestor(root, previous["sourceSha"], manifest["sourceSha"])
                paths = git(root, "diff", "--name-only", previous["sourceSha"], manifest["sourceSha"]).splitlines()
                allowed = set(TOOL_FILES) | set(CONTRACT_FILES) if auth["cause"] == "measurement-correction" else None
                require(any(p in allowed if allowed else p.startswith("src/") for p in paths),
                        "substantive-campaign-correction-required")
                evidence = root / "performance/baseline-campaigns/evidence" / previous["campaignId"] / "campaign-result.json"
                require(evidence.exists(), "prior-campaign-evidence-must-be-retained")
        else:
            require(auth["supersedesCampaignId"] is None, "unknown-campaign-predecessor")
            require(manifest["campaignId"] not in introduced_ids
                    or (manifest["profile"] not in latest_profile and manifest["profile"] not in historical_profiles),
                    "new-scope-cannot-reset-profile-epoch")
        prior[scope] = manifest
        by_id[manifest["campaignId"]] = manifest
        latest_profile[manifest["profile"]] = manifest


def validate_declaration(root: Path, manifest_path: Path, workflow_sha: str, run: dict) -> dict:
    """A new main push is the only campaign origin. No retrospective enrollment."""
    path = manifest_path.relative_to(root).as_posix()
    manifest = load_json(manifest_path)
    require(path == f"{MANIFEST_DIRECTORY}/{manifest['campaignId']}.json", "campaign-path-identity-mismatch")
    require(run.get("head_sha") == workflow_sha and run.get("head_branch") == "main"
            and run.get("event") == "push" and run.get("path") == WORKFLOW and run.get("run_attempt") == 1,
            "campaign-requires-first-main-push-attempt")
    additions = git(root, "diff", "--name-only", "--diff-filter=A", workflow_sha + "^", workflow_sha).splitlines()
    require(path in additions, "campaign-not-predeclared-by-this-main-push")
    declaration = git_document(root, workflow_sha, path)
    require(declaration == manifest, "campaign-manifest-changed-after-declaration")
    introductions = git(root, "log", "--first-parent", "--diff-filter=A", "--format=%H", workflow_sha, "--", path).splitlines()
    require(introductions == [workflow_sha], "campaign-id-already-consumed")
    ancestor(root, manifest["sourceSha"], workflow_sha + "^")
    # The earlier main source must already contain this general policy. A campaign
    # introduced with the policy, or a legacy capture source, cannot self-qualify.
    source_policy = git(root, "show", f"{manifest['sourceSha']}:scripts/performance/db_campaign.py")
    require(f'POLICY_VERSION = "{POLICY_VERSION}"' in source_policy, "campaign-policy-must-roll-out-before-source")
    created = utc(run["created_at"])
    require(utc(manifest["createdAtUtc"]) <= created < utc(manifest["expiresAtUtc"]), "campaign-not-declared-before-run")
    return {"schemaVersion": 1, "campaignId": manifest["campaignId"], "manifestSha256": digest(manifest),
            "declarationSha": workflow_sha, "workflowPath": WORKFLOW, "workflowRunId": run["id"],
            "workflowRunAttempt": 1, "sourceSha": manifest["sourceSha"], "runCreatedAtUtc": run["created_at"]}


def validate_profile_samples(profile: dict, contract: dict) -> None:
    """Bind duration values to every ordered structural capture, including page 10."""
    require(set(profile) == {"schemaVersion", "headSha", "profile", "fixtureHash", "fixtureVersion", "warmupSamplesExcluded",
                             "collectionComplete", "scenarios", "plans", "measurements"}, "unsafe-profile-fields")
    require([s["id"] for s in profile["scenarios"]] == [s["id"] for s in contract["scenarios"]], "raw-scenario-order-changed")
    expected_measurements = []
    for scenario, raw in zip(contract["scenarios"], profile["scenarios"], strict=True):
        require(set(raw) == {"id", "cardinality", "samples", "failures"}, "unsafe-scenario-fields")
        sizes = contract["policy"]["pageSizes"] if scenario["paged"] else [0]
        expected_ordinals = [(size, page, iteration) for size in sizes for iteration in range(1, contract["policy"]["samples"] + 1)
                             for page in ((1, 2) if scenario["paged"] else (1,))]
        require([(s["pageSize"], s["page"], s["iteration"]) for s in raw["samples"]] == expected_ordinals,
                "sample-removal-duplication-or-reordering")
        for sample in raw["samples"]:
            require(set(sample) == {"pageSize", "page", "iteration", "returnedCount", "requestDurationMs", "dbTimeFraction", "capture", "fingerprintCounts"},
                    "unsafe-sample-fields")
            validate_capture(sample["capture"])
        for size in sizes:
            samples = [s["capture"]["totalDurationMs"] for s in raw["samples"] if s["pageSize"] == size and s["page"] == 1]
            expected_measurements.append({"schemaVersion": 1, "scenario": scenario["id"], "metric": "db.total_time_ms", "unit": "ms",
                                          "headSha": profile["headSha"], "samples": samples, "attempt": 1, "pageSize": size,
                                          "measurementEnvelope": {"warmupSamplesExcluded": True, "environmentStable": True,
                                                                  "benchmarkExitCode": 0, "timedOut": False}})
    require(profile["measurements"] == expected_measurements, "duration-samples-do-not-match-ordered-raw-captures")


def validate_partial_profile(profile: dict, contract: dict) -> None:
    """Retain safe prefixes from failed collectors without making them eligible."""
    require(set(profile) == {"schemaVersion", "headSha", "profile", "fixtureHash", "fixtureVersion", "warmupSamplesExcluded",
                             "collectionComplete", "scenarios", "plans", "measurements"}, "unsafe-profile-fields")
    failure_codes = {"response-page-cardinality", "overlapping-pages", "unstable-page-order", "scenario-or-command-failed",
                     "query-count-hard-ceiling", "extreme-slow-command", "missing-ordered-db-page", "over-materialized-page",
                     "unbounded-collection-materialization"}
    for plan in profile["plans"]:
        require(set(plan) == {"id", "tableRows", "requiredKeyLookupPresent", "observedNodeTypes", "observedIndexKinds", "decision"}
                and plan["id"] in {p["id"] for p in contract["planChecks"]}
                and set(plan["observedNodeTypes"]) <= {"Index Scan", "Index Only Scan", "Bitmap Index Scan", "Bitmap Heap Scan", "Seq Scan", "Result", "Gather"}
                and set(plan["observedIndexKinds"]) <= {"primary-key", "task-project-key", "other"}
                and plan["decision"] in ("pass", "regression"), "unsafe-plan-fields")
    expected_ids = [s["id"] for s in contract["scenarios"]]
    require([s["id"] for s in profile["scenarios"]] == expected_ids[:len(profile["scenarios"])], "partial-scenario-order-changed")
    for raw, scenario in zip(profile["scenarios"], contract["scenarios"]):
        require(set(raw) == {"id", "cardinality", "samples", "failures"}, "unsafe-scenario-fields")
        require(set(raw["failures"]) <= failure_codes, "unsafe-scenario-failure")
        sizes = contract["policy"]["pageSizes"] if scenario["paged"] else [0]
        keys = [(size, page, iteration) for size in sizes for iteration in range(1, contract["policy"]["samples"] + 1)
                for page in ((1, 2) if scenario["paged"] else (1,))]
        require([(s["pageSize"], s["page"], s["iteration"]) for s in raw["samples"]] == keys[:len(raw["samples"])],
                "partial-sample-removal-duplication-or-reordering")
        for sample in raw["samples"]:
            require(set(sample) == {"pageSize", "page", "iteration", "returnedCount", "requestDurationMs", "dbTimeFraction", "capture", "fingerprintCounts"},
                    "unsafe-sample-fields")
            validate_capture(sample["capture"])
            require(all(type(sample[field]) in (int, float) and math.isfinite(sample[field]) and sample[field] >= 0
                        for field in ("returnedCount", "requestDurationMs", "dbTimeFraction"))
                    and isinstance(sample["fingerprintCounts"], dict)
                    and all(is_sha(k) and type(v) is int and v > 0 for k, v in sample["fingerprintCounts"].items()), "unsafe-sample-values")
    for measurement in profile["measurements"]:
        require(set(measurement) == {"schemaVersion", "scenario", "metric", "unit", "headSha", "samples", "attempt", "pageSize", "measurementEnvelope"}
                and measurement["scenario"] in expected_ids and measurement["metric"] == "db.total_time_ms"
                and measurement["unit"] == "ms" and len(measurement["samples"]) == contract["policy"]["samples"],
                "unsafe-partial-measurement")
        raw = next(s for s in profile["scenarios"] if s["id"] == measurement["scenario"])
        require(measurement["samples"] == [s["capture"]["totalDurationMs"] for s in raw["samples"] if s["page"] == 1
                                           and s["pageSize"] == measurement["pageSize"]], "partial-duration-sample-mismatch")


def db_comparator(root: Path):
    spec = importlib.util.spec_from_file_location("db_campaign_structural", root / "scripts/performance/db-compare.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    # Its original default points to its own checkout. Explicitly bind that
    # same fixture manifest when replaying an immutable historical source.
    module.fixture_hash = lambda profile, *, fixture_version: fixture_hash(profile, root / "performance/datasets.json", fixture_version=fixture_version)
    module.DB_FIXTURE_VERSION = pinned_db_fixture_version(root)
    return module


def source_comparator(root: Path):
    """Execute the comparator bytes identified by the declaration's source."""
    spec = importlib.util.spec_from_file_location("db_campaign_source_compare", root / "scripts/performance/compare.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def evaluate_group(manifest: dict, group: dict, identity: dict, root: Path) -> dict:
    require(set(group) == {"ordinal", "startedAtUtc", "endedAtUtc", "manifestSha256", "sourceSha", "workflowRunId",
                           "workflowRunAttempt", "captureExitCodes", "profiles", "fingerprints", "rawDigests"}, "unsafe-or-incomplete-group")
    require(group["manifestSha256"] == digest(manifest) and group["sourceSha"] == manifest["sourceSha"]
            and group["workflowRunId"] == identity["workflowRunId"] and group["workflowRunAttempt"] == 1,
            "group-campaign-provenance-mismatch")
    started, ended = utc(group["startedAtUtc"]), utc(group["endedAtUtc"])
    require(utc(manifest["createdAtUtc"]) <= utc(identity["runCreatedAtUtc"]) <= started <= ended < utc(manifest["expiresAtUtc"]),
            "campaign-created-after-measurement-or-expired")
    reasons = []
    contract = load_json(root / "performance/db-scenarios.json")
    for name, raw in group["profiles"].items():
        require(name in ("small", "medium"), "unsafe-profile-identity")
        validate_partial_profile(raw, contract)
        fp = group["fingerprints"].get(name)
        if fp is not None:
            tools_from_fingerprint(fp)
        require(group["rawDigests"][name] == {"profileSha256": digest(raw), "fingerprintSha256": digest(fp) if fp is not None else None},
                "group-raw-digest-mismatch")
    if group["captureExitCodes"] != {"small": 0, "medium": 0}:
        reasons.append("incomplete-collection")
    if set(group["profiles"]) != {"small", "medium"} or set(group["fingerprints"]) != {"small", "medium"}:
        reasons.append("incomplete-group")
    if any(p.get("collectionComplete") is not True for p in group["profiles"].values()):
        reasons.append("incomplete-profile")
    if reasons:
        return {"ordinal": group["ordinal"], "eligible": False, "stable": False, "reasons": reasons}
    for name in ("small", "medium"):
        raw, fp = group["profiles"][name], group["fingerprints"][name]
        validate_profile_samples(raw, contract)
        require(group["rawDigests"][name] == {"profileSha256": digest(raw), "fingerprintSha256": digest(fp)}, "group-raw-digest-mismatch")
        require(raw["profile"] == name and raw["headSha"] == manifest["sourceSha"] and fp["commitSha"] == manifest["sourceSha"],
                "group-source-mismatch")
        require(fp["fixture"]["hash"] == raw["fixtureHash"] and fp["fixture"]["version"] == raw["fixtureVersion"], "group-fixture-mismatch")
    profile, fp = group["profiles"][manifest["profile"]], group["fingerprints"][manifest["profile"]]
    comparison = source_comparator(root)
    historical_key = getattr(comparison, "legacy_environment_compatibility_key", comparison.environment_compatibility_key)
    if historical_key(fp) != campaign_environment_digest(manifest):
        reasons.append("wrong-environment")
    if {"profile": fp["fixture"]["profile"], "hash": fp["fixture"]["hash"], "version": fp["fixture"]["version"],
        "manifestSha256": file_digest(root / "performance/datasets.json")} != manifest["fixtureIdentity"]:
        reasons.append("changed-fixture")
    if tools_from_fingerprint(fp) != manifest["toolVersions"]:
        reasons.append("changed-toolchain")
    structural = db_comparator(root).evaluate(group["profiles"]["small"], group["profiles"]["medium"], contract, manifest["sourceSha"])
    if structural["decision"] != "pass":
        reasons.append("structural-regression")
    canonical = [m for m in profile["measurements"] if m["pageSize"] in (0, 5)]
    values = []
    for measurement in canonical:
        summary = comparison.summarize(measurement["samples"])
        relative_mad = summary["relativeMad"]
        relative_mad = 0.0 if relative_mad is None and summary["mad"] == 0 else relative_mad
        stable = relative_mad is not None and relative_mad <= manifest["stabilityRule"]["maximum"]
        values.append({"scenario": measurement["scenario"], "samplesSha256": digest(measurement["samples"]),
                       "sampleCount": len(measurement["samples"]), "relativeMad": relative_mad, "stable": stable})
    stable = len(values) == len(manifest["scenarioSet"]) and all(v["stable"] for v in values)
    if not stable:
        reasons.append("unstable-duration")
    return {"ordinal": group["ordinal"], "eligible": not any(r != "unstable-duration" for r in reasons), "stable": stable,
            "reasons": reasons, "structuralDecision": structural["decision"], "structuralChecks": len(structural["results"]),
            "canonicalStreams": values}


def select_campaign(manifest: dict | None, declaration: dict, groups: list[dict], root: Path, *, selected_ordinal: int | None = None) -> dict:
    validate_manifest(manifest, root)
    require(declaration.get("manifestSha256") == digest(manifest) and declaration.get("campaignId") == manifest["campaignId"]
            and declaration.get("sourceSha") == manifest["sourceSha"] and declaration.get("workflowRunAttempt") == 1,
            "declaration-does-not-bind-manifest")
    require(0 < len(groups) <= manifest["maxCaptureGroups"], "campaign-max-groups-exceeded-or-empty")
    require([g["ordinal"] for g in groups] == list(range(1, len(groups) + 1)), "campaign-group-order-or-duplicate")
    decisions = [evaluate_group(manifest, g, declaration, root) for g in groups]
    selected = next((d["ordinal"] for d in decisions if d["eligible"] and d["stable"]), None)
    if manifest["earlyStopPolicy"] == "never":
        require(len(groups) == manifest["maxCaptureGroups"], "campaign-incomplete-declared-capture-count")
    else:
        require(len(groups) == (selected or manifest["maxCaptureGroups"]), "campaign-undeclared-early-stop")
    for a, b in zip(groups, groups[1:]):
        require(utc(a["endedAtUtc"]) <= utc(b["startedAtUtc"]), "campaign-groups-not-serial")
    if selected_ordinal is not None:
        require(selected_ordinal == selected, "selected-group-is-not-earliest-eligible-stable")
    return {"schemaVersion": 1, "policyVersion": POLICY_VERSION, "campaignId": manifest["campaignId"],
            "manifestSha256": digest(manifest), "declaration": declaration,
            "decision": "BASELINE_CANDIDATE" if selected else "BASELINE_UNAVAILABLE", "approved": False,
            "selectedGroupOrdinal": selected, "groups": decisions,
            "priorRejectedGroups": [d for d in decisions if selected is None or d["ordinal"] < selected],
            "rawGroupsSha256": digest(groups)}


def github_api(repository: str, path: str) -> dict:
    require(re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository or "") is not None, "invalid-repository")
    token = os.environ.get("GH_TOKEN")
    require(bool(token), "campaign-metadata-token-missing")
    request = urllib.request.Request("https://api.github.com/repos/" + repository + path,
                                     headers={"Authorization": "Bearer " + token, "Accept": "application/vnd.github+json",
                                              "X-GitHub-Api-Version": "2022-11-28"})
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)


def prepare(root: Path, output: Path) -> dict:
    sha = os.environ["GITHUB_SHA"]
    require(os.environ.get("GITHUB_REF") == "refs/heads/main" and os.environ.get("GITHUB_EVENT_NAME") == "push"
            and os.environ.get("GITHUB_RUN_ATTEMPT") == "1", "campaign-main-push-only-no-retry")
    paths = git(root, "diff", "--name-only", "--diff-filter=A", sha + "^", sha, "--", MANIFEST_DIRECTORY).splitlines()
    require(len(paths) == 1 and paths[0].endswith(".json"), "campaign-push-must-introduce-exactly-one-manifest")
    manifest_path = root / paths[0]
    manifest = load_json(manifest_path)
    # An additive schema reader must not rebind immutable manifests to the
    # latest tooling/contract bytes. Their earlier Main source owns those bytes.
    with source_snapshot(root, manifest["sourceSha"]) as snapshot:
        validate_manifest(manifest, snapshot, current_time=now_utc())
    manifests = [load_json(p) for p in sorted((root / MANIFEST_DIRECTORY).glob("*.json"))]
    validate_registry(root, manifests, main_sha=sha, base_ref=sha + "^", introduced_ids={manifest["campaignId"]})
    reference = manifest["authorization"]["reference"]
    match = re.fullmatch(r"https://github\.com/([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)/(issues|pull)/[1-9][0-9]*#issuecomment-([1-9][0-9]*)", reference)
    require(match is not None and match[1] == os.environ["GITHUB_REPOSITORY"], "campaign-requires-independent-premeasurement-review-comment")
    review = github_api(os.environ["GITHUB_REPOSITORY"], f"/issues/comments/{match[3]}")
    require(review.get("user", {}).get("login") == manifest["authorization"]["approver"]
            and manifest["campaignId"] in review.get("body", "") and digest(manifest) in review.get("body", "")
            and utc(review["created_at"]) < dt.datetime.now(dt.timezone.utc), "campaign-review-must-bind-fixed-manifest")
    run = github_api(os.environ["GITHUB_REPOSITORY"], f"/actions/runs/{os.environ['GITHUB_RUN_ID']}")
    if manifest["schemaVersion"] in (3, 4):
        validate_transition_review(manifest, review, run, github_api(os.environ["GITHUB_REPOSITORY"], "")["owner"]["login"])
    require(utc(review["created_at"]) <= utc(run["created_at"]) and utc(review["updated_at"]) <= utc(run["created_at"]),
            "campaign-review-created-or-changed-after-capture-run")
    declaration = validate_declaration(root, manifest_path, sha, run)
    output.mkdir(parents=True, exist_ok=False)
    write_json_atomic(output / "manifest.json", manifest)
    write_json_atomic(output / "declaration.json", declaration)
    return {"source_sha": manifest["sourceSha"], "campaign_id": manifest["campaignId"]}


def capture(root: Path, source: Path, output: Path) -> None:
    manifest, declaration = load_json(output / "manifest.json"), load_json(output / "declaration.json")
    validate_manifest(manifest, source, current_time=now_utc())
    require(git(source, "rev-parse", "HEAD") == manifest["sourceSha"], "campaign-runtime-checkout-mismatch")
    groups = []
    for ordinal in range(1, manifest["maxCaptureGroups"] + 1):
        started = now_utc()
        require(utc(started) < utc(manifest["expiresAtUtc"]), "campaign-expired-before-next-group")
        group = {"ordinal": ordinal, "startedAtUtc": started, "endedAtUtc": started,
                 "manifestSha256": digest(manifest), "sourceSha": manifest["sourceSha"],
                 "workflowRunId": declaration["workflowRunId"], "workflowRunAttempt": 1,
                 "captureExitCodes": {}, "profiles": {}, "fingerprints": {}, "rawDigests": {}}
        group_path = output / "groups" / str(ordinal)
        group_path.mkdir(parents=True, exist_ok=False)
        write_json_atomic(group_path / "started.json", {k: group[k] for k in ("ordinal", "startedAtUtc", "manifestSha256", "workflowRunId")})
        for profile in ("small", "medium"):
            raw_path = source / "artifacts" / "baseline-campaign-raw" / manifest["campaignId"] / str(ordinal) / profile
            env = os.environ | {"COGLATAS_PERFORMANCE_TARGET_SHA": manifest["sourceSha"],
                                "COGLATAS_PERFORMANCE_PROFILE": profile, "COGLATAS_PERFORMANCE_RUNTIME_MODE": "production",
                                "COGLATAS_PERFORMANCE_DB_CAPTURE_ENABLED": "true", "COGLATAS_PERFORMANCE_PORT": "18080",
                                "COGLATAS_PERFORMANCE_EVIDENCE_DIR": str(raw_path),
                                "COGLATAS_PERFORMANCE_COMPOSE_PROJECT": f"coglatas-performance-campaign-{declaration['workflowRunId']}-{ordinal}-{profile}"}
            completed = subprocess.run(["bash", "scripts/performance/with-environment.sh", "python3", "scripts/performance/db-probe.py",
                                        "--profile", profile, "--output", str(raw_path / "db.json")], cwd=source, env=env, check=False)
            group["captureExitCodes"][profile] = completed.returncode
            if (raw_path / "db.json").exists():
                raw = load_json(raw_path / "db.json")
                fp = load_json(raw_path / "environment.json") if (raw_path / "environment.json").exists() else None
                # Unknown/private data never reaches the campaign artifact. Every
                # safe sample in a partial collector is retained and stays ineligible.
                try:
                    validate_partial_profile(raw, load_json(source / "performance/db-scenarios.json"))
                    if fp is not None:
                        tools_from_fingerprint(fp)
                except (PerformanceContractError, ValueError, KeyError, TypeError):
                    group["endedAtUtc"] = now_utc()
                    group["captureExitCodes"][profile] = 97
                    write_json_atomic(group_path / "group.json", group)
                    write_json_atomic(output / "raw-groups.json", {"groups": groups + [group]})
                    write_json_atomic(group_path / "decision.json", {"ordinal": ordinal, "eligible": False, "stable": False,
                                                                     "reasons": ["unsafe-or-corrupt-evidence"]})
                    raise PerformanceContractError("unsafe-or-corrupt-campaign-evidence") from None
                group["profiles"][profile] = raw
                if fp is not None:
                    group["fingerprints"][profile] = fp
                group["rawDigests"][profile] = {"profileSha256": digest(raw), "fingerprintSha256": digest(fp) if fp is not None else None}
                write_json_atomic(group_path / "group.json", group)
        group["endedAtUtc"] = now_utc()
        write_json_atomic(group_path / "group.json", group)
        groups.append(group)
        write_json_atomic(output / "raw-groups.json", {"groups": groups})
        decision = evaluate_group(manifest, group, declaration, source)
        write_json_atomic(group_path / "decision.json", decision)
        print(json.dumps({"campaignId": manifest["campaignId"], **{k: decision[k] for k in ("ordinal", "eligible", "stable", "reasons")}}), flush=True)
        if manifest["earlyStopPolicy"] == "first-eligible-stable-complete" and decision["eligible"] and decision["stable"]:
            break
    result = select_campaign(manifest, declaration, groups, source)
    write_json_atomic(output / "campaign-result.json", result)


def validate_repository(root: Path, base_ref: str | None = None) -> None:
    paths = sorted((root / MANIFEST_DIRECTORY).glob("*.json"))
    manifests = [load_json(p) for p in paths]
    for manifest in manifests:
        require(manifest.get("policyVersion") == POLICY_VERSION and is_sha(manifest.get("sourceSha"), 40), "invalid-registered-campaign")
        source_sha = manifest["sourceSha"]
        if base_ref:
            ancestor(root, source_sha, base_ref)
        require(f'POLICY_VERSION = "{POLICY_VERSION}"' in git(root, "show", f"{source_sha}:scripts/performance/db_campaign.py"),
                "campaign-policy-must-already-be-main")
        # Extract only allowlisted validation inputs from the earlier immutable
        # source, so a future main contract cannot rewrite historical campaigns.
        with source_snapshot(root, source_sha) as snapshot:
            validate_manifest(manifest, snapshot)
    prior_paths = git(root, "ls-tree", "-r", "--name-only", base_ref, "--", MANIFEST_DIRECTORY).splitlines() if base_ref else []
    prior_ids = {Path(p).stem for p in prior_paths}
    validate_registry(root, manifests, main_sha=base_ref, base_ref=base_ref,
                      introduced_ids={m["campaignId"] for m in manifests} - prior_ids)
    if base_ref:
        for path in prior_paths:
            require((root / path).exists() and git_document(root, base_ref, path) == load_json(root / path),
                    "registered-campaign-manifest-is-immutable")
        immutable_paths = git(root, "ls-tree", "-r", "--name-only", base_ref, "--", POLICY_DIRECTORY, "performance/baseline-campaigns/evidence").splitlines()
        for path in immutable_paths:
            original = subprocess.run(["git", "cat-file", "blob", f"{base_ref}:{path}"], cwd=root, capture_output=True, check=False)
            require(original.returncode == 0 and (root / path).is_file() and original.stdout == (root / path).read_bytes(),
                    "retained-campaign-evidence-or-rule-is-immutable")
        prior_policies = set(git(root, "ls-tree", "-r", "--name-only", base_ref, "--", POLICY_DIRECTORY).splitlines())
        for path in (root / POLICY_DIRECTORY).glob("*.json"):
            require(path.relative_to(root).as_posix() == ASSIGNMENT_POLICY_PATH, "unknown-assignment-policy")
            if path.relative_to(root).as_posix() not in prior_policies:
                validate_assignment_policy(load_json(path), repository=os.environ.get("GITHUB_REPOSITORY", "NYGsatoshi/Coglatas"), api=github_api)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("operation", choices=("prepare", "capture", "select", "validate-repository"))
    parser.add_argument("--output", type=Path)
    parser.add_argument("--source", type=Path)
    parser.add_argument("--base-ref")
    args = parser.parse_args()
    root = repository_root()
    try:
        if args.operation == "prepare":
            result = prepare(root, args.output)
            for key, value in result.items():
                print(f"{key}={value}")
                if os.environ.get("GITHUB_OUTPUT"):
                    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as stream:
                        stream.write(f"{key}={value}\n")
        elif args.operation == "capture":
            capture(root, args.source.resolve(), args.output.resolve())
        elif args.operation == "select":
            manifest = load_json(args.output / "manifest.json")
            with source_snapshot(root, manifest["sourceSha"]) as snapshot:
                result = select_campaign(manifest, load_json(args.output / "declaration.json"),
                                         load_json(args.output / "raw-groups.json")["groups"], snapshot)
            write_json_atomic(args.output / "campaign-result.json", result)
            return 0 if result["decision"] == "BASELINE_CANDIDATE" else 1
        else:
            validate_repository(root, args.base_ref)
        return 0
    except (PerformanceContractError, OSError, ValueError, KeyError, TypeError):
        # Commands and HTTP exceptions may carry credentials or private details.
        print("PERF-05 campaign failed: invalid, incomplete, expired, or incompatible evidence", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
