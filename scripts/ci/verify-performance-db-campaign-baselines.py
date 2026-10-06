#!/usr/bin/env python3
"""Review-only promotion of trusted, complete, earliest stable campaign groups."""
from __future__ import annotations

import argparse
import io
import json
import os
import sys
import urllib.request
from urllib.parse import urlsplit
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts/performance"))
from common import PerformanceContractError, load_json
from db_campaign import POLICY_VERSION, MANIFEST_DIRECTORY, WORKFLOW, ancestor, campaign_environment_digest, digest, git, github_api, is_sha, require, select_campaign, source_snapshot, utc

LEDGER = "performance/baseline-campaigns/approvals.json"


class SafeArtifactRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, response, code, message, headers, url):
        redirected = super().redirect_request(request, response, code, message, headers, url)
        require(urlsplit(url).scheme == "https", "artifact-redirect-must-use-https")
        if redirected and urlsplit(request.full_url).netloc != urlsplit(url).netloc:
            redirected.remove_header("Authorization")
        return redirected


def baseline_documents(manifest: dict, result: dict, groups: list[dict], artifact: dict, *, approved: bool = False) -> dict[str, dict]:
    require(result["decision"] == "BASELINE_CANDIDATE" and result["selectedGroupOrdinal"] is not None,
            "campaign-has-no-stable-complete-baseline")
    group = groups[result["selectedGroupOrdinal"] - 1]
    environment_field = "environmentCompatibilityDigest" if manifest["schemaVersion"] == 2 else "environmentCompatibilityKey"
    environment_digest = campaign_environment_digest(manifest)
    documents = {}
    for measurement in group["profiles"][manifest["profile"]]["measurements"]:
        if measurement["pageSize"] not in (0, 5):
            continue
        provenance = {"campaignId": manifest["campaignId"], "policyVersion": POLICY_VERSION,
                      "manifestSha256": digest(manifest), "declarationSha": result["declaration"]["declarationSha"],
                      "sourceSha": manifest["sourceSha"], "workflowPath": WORKFLOW,
                      "workflowRunId": result["declaration"]["workflowRunId"], "workflowRunAttempt": 1,
                      "artifactId": artifact["id"], "artifactName": artifact["name"], "artifactDigest": artifact["digest"],
                      "rawGroupsSha256": digest(groups), "selectedGroupOrdinal": group["ordinal"],
                      "priorRejectedGroups": result["priorRejectedGroups"], "stabilityDecision": "stable",
                      "comparatorSha256": manifest["stabilityRule"]["comparatorSha256"], "toolVersions": manifest["toolVersions"],
                      "fixtureDigest": manifest["fixtureIdentity"]["hash"], environment_field: environment_digest,
                      "samplesSha256": digest(measurement["samples"]), "sampleCount": manifest["sampleCount"],
                      "sampleOrder": list(range(1, manifest["sampleCount"] + 1)), "pageSize": measurement["pageSize"]}
        documents[measurement["scenario"]] = {"schemaVersion": 1, "resultSchemaVersion": 1, "scenario": measurement["scenario"],
                                               "metric": "db.total_time_ms", "unit": "ms", "baselineSha": manifest["sourceSha"],
                                               "sourceRef": "refs/heads/main", "approved": approved,
                                               environment_field: environment_digest,
                                               "fixtureHash": manifest["fixtureIdentity"]["hash"], "fixtureVersion": manifest["fixtureIdentity"]["version"],
                                               "samples": measurement["samples"], "provenance": provenance}
    return documents


def trusted_artifact(repository: str, approval: dict, declaration: dict, api=github_api) -> dict[str, dict]:
    """On introduction, verify GitHub metadata and the actual downloaded archive."""
    artifact = approval["artifact"]
    metadata = api(repository, f"/actions/artifacts/{artifact['id']}")
    require(metadata.get("id") == artifact["id"] and metadata.get("name") == artifact["name"]
            and metadata.get("digest") == artifact["digest"] and metadata.get("expired") is False
            and metadata.get("workflow_run", {}).get("id") == declaration["workflowRunId"]
            and metadata.get("workflow_run", {}).get("head_sha") == declaration["declarationSha"], "campaign-artifact-provenance-invalid")
    run = api(repository, f"/actions/runs/{declaration['workflowRunId']}")
    require(run.get("head_sha") == declaration["declarationSha"] and run.get("head_branch") == "main"
            and run.get("event") == "push" and run.get("path") == WORKFLOW and run.get("run_attempt") == 1
            and run.get("status") == "completed" and run.get("conclusion") == "success"
            and run.get("created_at") == declaration["runCreatedAtUtc"], "campaign-workflow-provenance-invalid")
    request = urllib.request.Request(f"https://api.github.com/repos/{repository}/actions/artifacts/{artifact['id']}/zip",
                                     headers={"Authorization": "Bearer " + os.environ["GH_TOKEN"], "Accept": "application/vnd.github+json"})
    opener = urllib.request.build_opener(SafeArtifactRedirect())
    with opener.open(request, timeout=60) as response:
        raw = response.read(32 * 1024 * 1024 + 1)
    import hashlib
    require(len(raw) <= 32 * 1024 * 1024 and "sha256:" + hashlib.sha256(raw).hexdigest() == artifact["digest"],
            "campaign-artifact-archive-digest-invalid")
    names = ("manifest.json", "declaration.json", "raw-groups.json", "campaign-result.json")
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        require(all(archive.namelist().count(n) == 1 for n in names), "campaign-artifact-required-files-missing-or-duplicate")
        require(sum(archive.getinfo(n).file_size for n in names) <= 32 * 1024 * 1024, "campaign-expanded-artifact-too-large")
        return {name: json.loads(archive.read(name)) for name in names}


def validate_approval(root: Path, approval: dict, head_sha: str, base_ref: str, *, historical: bool, fetch=trusted_artifact) -> None:
    require(set(approval) == {"campaignId", "approver", "reference", "approvedAtUtc", "artifact", "evidenceDirectory", "baselineDirectory"},
            "invalid-campaign-approval-schema")
    manifest_path = f"{MANIFEST_DIRECTORY}/{approval['campaignId']}.json"
    manifest = load_json(root / manifest_path)
    require(approval["approver"] == manifest["authorization"]["approver"] and approval["reference"] == manifest["authorization"]["reference"],
            "campaign-independent-approval-mismatch")
    if not historical:
        require(manifest["sourceSha"] != head_sha, "candidate-self-baseline-forbidden")
    evidence = f"performance/baseline-campaigns/evidence/{manifest['campaignId']}"
    baseline = f"performance/baselines/db/{manifest['profile']}/{campaign_environment_digest(manifest)}"
    require(approval["evidenceDirectory"] == evidence and approval["baselineDirectory"] == baseline, "campaign-approval-path-invalid")
    documents = {name: load_json(root / evidence / name) for name in ("manifest.json", "declaration.json", "raw-groups.json", "campaign-result.json")}
    require(documents["manifest.json"] == manifest, "campaign-evidence-manifest-mismatch")
    declaration, groups = documents["declaration.json"], documents["raw-groups.json"]["groups"]
    ancestor(root, declaration["declarationSha"], base_ref)
    ancestor(root, manifest["sourceSha"], declaration["declarationSha"] + "^")
    require(json.loads(git(root, "show", f"{declaration['declarationSha']}:{manifest_path}")) == manifest,
            "campaign-not-in-main-before-measurement")
    additions = git(root, "diff", "--name-only", "--diff-filter=A", declaration["declarationSha"] + "^", declaration["declarationSha"]).splitlines()
    require(manifest_path in additions, "historical-run-cannot-be-enrolled-in-campaign")
    with source_snapshot(root, manifest["sourceSha"]) as snapshot:
        recomputed = select_campaign(manifest, declaration, groups, snapshot, selected_ordinal=documents["campaign-result.json"]["selectedGroupOrdinal"])
    require(recomputed == documents["campaign-result.json"], "campaign-selection-or-rejected-evidence-changed")
    require(utc(approval["approvedAtUtc"]) >= max(utc(g["endedAtUtc"]) for g in groups), "campaign-approval-before-completion")
    artifact = approval["artifact"]
    require(set(artifact) == {"id", "name", "digest", "workflowRunId"} and type(artifact["id"]) is int and artifact["id"] > 0
            and artifact["name"] == "perf05-campaign-" + manifest["campaignId"]
            and isinstance(artifact["digest"], str) and artifact["digest"].startswith("sha256:") and is_sha(artifact["digest"][7:])
            and artifact["workflowRunId"] == declaration["workflowRunId"], "campaign-artifact-reference-invalid")
    if not historical:
        archived = fetch(os.environ.get("GITHUB_REPOSITORY", "NYGsatoshi/Coglatas"), approval, declaration)
        require(archived == documents, "campaign-retained-raw-evidence-does-not-match-trusted-artifact")
    expected = baseline_documents(manifest, recomputed, groups, artifact, approved=True)
    actual_paths = list((root / baseline).glob("*.json"))
    require({p.stem for p in actual_paths} == set(expected), "campaign-approved-group-incomplete-or-duplicate")
    for scenario, document in expected.items():
        require(load_json(root / baseline / (scenario + ".json")) == document, "campaign-baseline-samples-or-provenance-changed")


def validate(root: Path, base_ref: str, head_sha: str) -> int:
    ledger = load_json(root / LEDGER)
    require(set(ledger) == {"schemaVersion", "policyVersion", "approvals"} and ledger["schemaVersion"] == 1
            and ledger["policyVersion"] == POLICY_VERSION and isinstance(ledger["approvals"], list), "invalid-campaign-approval-ledger")
    try:
        old = json.loads(git(root, "show", f"{base_ref}:{LEDGER}"))
    except PerformanceContractError:
        old = {"approvals": []}
    old_records = {a["campaignId"]: a for a in old["approvals"]}
    require(len({a["campaignId"] for a in ledger["approvals"]}) == len(ledger["approvals"]), "duplicate-campaign-approval")
    new_records = {a["campaignId"]: a for a in ledger["approvals"]}
    require(all(new_records.get(key) == value for key, value in old_records.items()), "approved-campaign-ledger-is-immutable")
    approved_paths = set()
    for approval in ledger["approvals"]:
        historical = approval["campaignId"] in old_records
        validate_approval(root, approval, head_sha, base_ref, historical=historical)
        baseline = approval["baselineDirectory"]
        require(baseline not in approved_paths, "moving-or-ambiguous-campaign-baseline-forbidden")
        approved_paths.add(baseline)
        if historical:
            for directory in (approval["evidenceDirectory"], baseline):
                for path in git(root, "ls-tree", "-r", "--name-only", base_ref, "--", directory).splitlines():
                    require((root / path).exists() and json.loads(git(root, "show", f"{base_ref}:{path}")) == load_json(root / path),
                            "approved-campaign-evidence-is-immutable")
    for path in (root / "performance/baselines/db").rglob("*.json"):
        document = load_json(path)
        if document.get("provenance", {}).get("policyVersion") == POLICY_VERSION:
            require(path.parent.relative_to(root).as_posix() in approved_paths, "campaign-baseline-missing-approval-ledger")
    return len(ledger["approvals"])


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-ref", required=True)
    parser.add_argument("--head-sha", required=True)
    args = parser.parse_args()
    try:
        count = validate(ROOT, args.base_ref, args.head_sha)
        print(f"PERF-05 approved campaign baselines valid: {count}")
        return 0
    except (PerformanceContractError, OSError, ValueError, KeyError, TypeError, zipfile.BadZipFile):
        print("PERF-05 campaign baseline approval rejected: invalid or untrusted provenance", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
