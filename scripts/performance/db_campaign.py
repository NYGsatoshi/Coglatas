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
from compare import _compatibility_payload, environment_compatibility_key, summarize
from db_gate import validate_capture, validate_contract

POLICY_VERSION = "perf05-db-campaign-v1"
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
    field = PUBLIC_DIGEST_FIELD if manifest.get("schemaVersion") == 2 else "environmentCompatibilityKey"
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
    require(set(fp) in (expected, expected | {"applicationRuntime"}), "unsafe-fingerprint-fields")
    for field, fields in {"runner": {"os", "runnerOs", "runnerImage", "cpuCount", "cpuModel", "memoryBytes"},
                          "dotnet": {"sdkInfo", "runtimeInfo"}, "node": {"version", "npmVersion"},
                          "postgresql": {"version"}, "browser": {"playwrightVersion", "version"},
                          "containerImages": {"app", "postgres", "performanceBrowser"},
                          "fixture": {"profile", "seed", "hash", "version"}}.items():
        require(isinstance(fp[field], dict) and set(fp[field]) == fields, "unsafe-fingerprint-section")
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
    fields = (MANIFEST_FIELDS - {"environmentCompatibilityKey"}) | {PUBLIC_DIGEST_FIELD} if version == 2 else MANIFEST_FIELDS
    require(set(manifest) == fields, "missing-or-invalid-campaign-manifest")
    require(type(version) is int and version in (1, 2) and manifest["policyVersion"] == POLICY_VERSION, "campaign-policy-mismatch")
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
    require(auth["cause"] in ("initial-governance-campaign", "accepted-product-change", "measurement-correction"),
            "campaign-result-only-retry-forbidden")
    require((auth["cause"] == "initial-governance-campaign" and auth["supersedesCampaignId"] is None)
            or (auth["cause"] != "initial-governance-campaign" and isinstance(auth["supersedesCampaignId"], str)
                and auth["supersedesCampaignId"] != manifest["campaignId"]), "campaign-predecessor-invalid")


def validate_registry(root: Path, manifests: list[dict], *, main_sha: str | None = None) -> None:
    ids = [m["campaignId"] for m in manifests]
    require(len(ids) == len(set(ids)), "duplicate-campaign-id")
    prior: dict[tuple[str, str], dict] = {}
    for manifest in sorted(manifests, key=lambda m: utc(m["createdAtUtc"])):
        scope = (manifest["profile"], campaign_environment_digest(manifest))
        previous = prior.get(scope)
        auth = manifest["authorization"]
        if previous:
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
        prior[scope] = manifest


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
    if environment_compatibility_key(fp) != campaign_environment_digest(manifest):
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
        summary = summarize(measurement["samples"])
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
    validate_registry(root, manifests, main_sha=sha)
    reference = manifest["authorization"]["reference"]
    match = re.fullmatch(r"https://github\.com/([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)/(issues|pull)/[1-9][0-9]*#issuecomment-([1-9][0-9]*)", reference)
    require(match is not None and match[1] == os.environ["GITHUB_REPOSITORY"], "campaign-requires-independent-premeasurement-review-comment")
    review = github_api(os.environ["GITHUB_REPOSITORY"], f"/issues/comments/{match[3]}")
    require(review.get("user", {}).get("login") == manifest["authorization"]["approver"]
            and manifest["campaignId"] in review.get("body", "") and digest(manifest) in review.get("body", "")
            and utc(review["created_at"]) < dt.datetime.now(dt.timezone.utc), "campaign-review-must-bind-fixed-manifest")
    run = github_api(os.environ["GITHUB_REPOSITORY"], f"/actions/runs/{os.environ['GITHUB_RUN_ID']}")
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
    validate_registry(root, manifests, main_sha=base_ref)
    if base_ref:
        prior_paths = git(root, "ls-tree", "-r", "--name-only", base_ref, "--", MANIFEST_DIRECTORY).splitlines()
        for path in prior_paths:
            require((root / path).exists() and git_document(root, base_ref, path) == load_json(root / path),
                    "registered-campaign-manifest-is-immutable")


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
            result = select_campaign(load_json(args.output / "manifest.json"), load_json(args.output / "declaration.json"),
                                     load_json(args.output / "raw-groups.json")["groups"], root)
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
