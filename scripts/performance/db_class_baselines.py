"""Normalize authenticated campaign evidence without rewriting historical decisions."""
from __future__ import annotations

import copy
import importlib.util
import json
import os
import re
from pathlib import Path

from common import load_json
from db_campaign import ancestor, campaign_environment_digest, digest, git, require, select_campaign, source_snapshot, utc
from environment_class import POLICY_VERSION, class_name, compatibility, environment_class, hardware_fingerprint

LEDGER = "performance/environment-class-baselines.json"
CATALOG = "performance/baselines/db/environment-class"
DOCUMENTS = ("manifest.json", "declaration.json", "raw-groups.json", "campaign-result.json")


def qualify(root, documents, target_fingerprint):
    """Eligibility changes; original manifest/result/raw bytes and statistical rules do not."""
    manifest, declaration = documents["manifest.json"], documents["declaration.json"]
    groups = documents["raw-groups.json"]["groups"]
    with source_snapshot(root, manifest["sourceSha"]) as source:
        original = select_campaign(manifest, declaration, groups, source)
        require(original == documents["campaign-result.json"], "original-campaign-replay-does-not-match")
        from db_campaign import source_comparator
        pinned = source_comparator(source)
        historical_key = getattr(pinned, "legacy_environment_compatibility_key", pinned.environment_compatibility_key)
        require(historical_key(target_fingerprint) == campaign_environment_digest(manifest), "target-fingerprint-not-declared")
        require(target_fingerprint["commitSha"] == manifest["sourceSha"], "target-source-mismatch")
        target_class = environment_class(target_fingerprint, source, authenticated_legacy_github=True)
        active_class = environment_class(target_fingerprint, root, authenticated_legacy_github=True)
        require(target_class == active_class, "measured-benchmark-contract-not-current-compatible")
        decisions = []
        for group, decision in zip(groups, original["groups"], strict=True):
            fp = group["fingerprints"][manifest["profile"]]
            value = environment_class(fp, source, authenticated_legacy_github=True)
            relation = compatibility(value, target_class, hardware_fingerprint(fp), hardware_fingerprint(target_fingerprint))
            # Admit only the explicitly retired hardware equality constraint.
            require(set(decision["reasons"]) <= {"wrong-environment", "unstable-duration"}, "non-hardware-original-failure")
            legacy_differences = {key for key in pinned._compatibility_payload(fp)
                                  if pinned._compatibility_payload(fp)[key] != pinned._compatibility_payload(target_fingerprint)[key]}
            require(legacy_differences <= {"cpuModel"}, "additional-legacy-hard-attribute-mismatch")
            eligible = relation != "INCOMPATIBLE" and decision["structuralDecision"] == "pass" and decision["structuralChecks"] == 28
            decisions.append({"ordinal": group["ordinal"], "eligible": eligible, "stable": decision["stable"],
                              "environmentCompatibility": relation, "environmentClass": value,
                              "hardwareFingerprint": hardware_fingerprint(fp), "canonicalStreams": decision["canonicalStreams"],
                              "structuralChecks": decision["structuralChecks"], "structuralDecision": decision["structuralDecision"],
                              "originalReasons": decision["reasons"]})
        selected = next((d["ordinal"] for d in decisions if d["eligible"] and d["stable"]), None)
    return {"schemaVersion": 1, "policyVersion": POLICY_VERSION, "campaignId": manifest["campaignId"],
            "manifestSha256": digest(manifest), "declaration": declaration, "sourceSha": manifest["sourceSha"],
            "originalDecision": original["decision"], "originalResultSha256": digest(original), "rawGroupsSha256": digest(groups),
            "decision": "BASELINE_CANDIDATE" if selected else "BASELINE_UNAVAILABLE", "approved": False,
            "selectedGroupOrdinal": selected, "groups": decisions, "targetEnvironmentClass": target_class,
            "priorRejectedGroups": [d for d in decisions if selected is None or d["ordinal"] < selected]}


def baseline_documents(documents, qualification, artifact, *, approved=False):
    require(qualification["decision"] == "BASELINE_CANDIDATE", "no-class-qualified-baseline")
    manifest = documents["manifest.json"]
    group = documents["raw-groups.json"]["groups"][qualification["selectedGroupOrdinal"] - 1]
    decision = qualification["groups"][group["ordinal"] - 1]
    output = {}
    for measurement in group["profiles"][manifest["profile"]]["measurements"]:
        if measurement["pageSize"] not in (0, 5):
            continue
        stream = next(s for s in decision["canonicalStreams"] if s["scenario"] == measurement["scenario"])
        require(stream["sampleCount"] == 5 and stream["stable"] is True, "invalid-class-baseline-stream")
        from db_campaign import source_comparator, source_snapshot
        # Median/MAD use the immutable campaign comparator, never a favorable new estimator.
        source_root = Path(__file__).resolve().parents[2]
        with source_snapshot(source_root, manifest["sourceSha"]) as source:
            summary = source_comparator(source).summarize(measurement["samples"])
        provenance = {"policyVersion": POLICY_VERSION, "campaignPolicyVersion": manifest["policyVersion"],
                      "profile": manifest["profile"],
                      "campaignId": manifest["campaignId"], "manifestSha256": digest(manifest),
                      "declarationSha": qualification["declaration"]["declarationSha"], "sourceSha": manifest["sourceSha"],
                      "workflowPath": qualification["declaration"]["workflowPath"], "workflowRunId": artifact["workflowRunId"],
                      "workflowRunAttempt": 1, "artifactId": artifact["id"], "artifactName": artifact["name"],
                      "artifactDigest": artifact["digest"], "rawGroupsSha256": qualification["rawGroupsSha256"],
                      "selectedGroupOrdinal": group["ordinal"], "priorRejectedGroups": qualification["priorRejectedGroups"],
                      "originalDecision": qualification["originalDecision"], "originalResultSha256": qualification["originalResultSha256"],
                      "qualificationSha256": digest(qualification), "comparatorSha256": manifest["stabilityRule"]["comparatorSha256"],
                      "sampleCount": 5, "sampleOrder": [1,2,3,4,5], "samplesSha256": digest(measurement["samples"]),
                      "pageSize": measurement["pageSize"], "benchmarkSchema": decision["environmentClass"]["benchmarkSchema"]}
        output[measurement["scenario"]] = {"schemaVersion": 1, "resultSchemaVersion": 1, "scenario": measurement["scenario"],
              "metric": "db.total_time_ms", "unit": "ms", "baselineSha": manifest["sourceSha"], "sourceRef": "refs/heads/main",
              "approved": approved, "environmentCompatibilityDigest": digest(decision["environmentClass"]),
              "environmentClass": decision["environmentClass"], "environmentClassName": class_name(decision["environmentClass"]),
              "hardwareFingerprint": decision["hardwareFingerprint"], "environmentCompatibility": decision["environmentCompatibility"],
              "fixtureHash": manifest["fixtureIdentity"]["hash"], "fixtureVersion": manifest["fixtureIdentity"]["version"],
              "samples": measurement["samples"], "median": summary["median"], "MAD": summary["mad"], "sampleCount": 5,
              "captureGroup": group["ordinal"], "benchmarkSchema": decision["environmentClass"]["benchmarkSchema"], "provenance": provenance}
    require(len(output) == 9, "incomplete-class-baseline-inventory")
    return output


def validate(root, base_ref, head_sha, *, authenticate=True):
    ledger = load_json(root / LEDGER)
    require(set(ledger) == {"schemaVersion", "policyVersion", "enrollments"} and ledger["schemaVersion"] == 1
            and ledger["policyVersion"] == POLICY_VERSION, "invalid-class-enrollment-ledger")
    old = json.loads(git(root, "show", f"{base_ref}:{LEDGER}")) if git(root, "ls-tree", "--name-only", base_ref, "--", LEDGER) else {"enrollments": []}
    old_records = {record["campaignId"]: record for record in old["enrollments"]}
    records = {record["campaignId"]: record for record in ledger["enrollments"]}
    require(len(records) == len(ledger["enrollments"]) and all(records.get(k) == v for k,v in old_records.items()), "class-enrollment-is-immutable")
    spec = importlib.util.spec_from_file_location("class_archive_auth", root / "scripts/ci/verify-performance-db-campaign-baselines.py")
    auth = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(auth)
    paths = set()
    for record in ledger["enrollments"]:
        required = {"campaignId", "artifact", "workflowConclusion", "evidenceDirectory", "baselineDirectory", "targetFingerprintPath",
                    "qualificationSha256", "approval", "legacyApprovalCampaignId"}
        require(set(record) == required and record["workflowConclusion"] in ("success", "failure"), "invalid-class-enrollment-record")
        historical = record["campaignId"] in old_records
        evidence = root / record["evidenceDirectory"]
        documents = {name: load_json(evidence / name) for name in DOCUMENTS}
        manifest, declaration = documents["manifest.json"], documents["declaration.json"]
        require(manifest["campaignId"] == record["campaignId"] and manifest["sourceSha"] != head_sha, "class-candidate-self-baseline-forbidden")
        require(record["evidenceDirectory"] == f"performance/baseline-campaigns/evidence/{manifest['campaignId']}"
                and record["targetFingerprintPath"] == record["evidenceDirectory"] + "/target-fingerprint.json", "class-evidence-path-invalid")
        ancestor(root, declaration["declarationSha"], base_ref)
        ancestor(root, manifest["sourceSha"], declaration["declarationSha"] + "^")
        require(json.loads(git(root, "show", f"{declaration['declarationSha']}:performance/baseline-campaigns/manifests/{record['campaignId']}.json")) == manifest,
                "class-campaign-declaration-not-immutable")
        if not historical and authenticate:
            archived = auth.trusted_campaign_archive(os.environ.get("GITHUB_REPOSITORY", "NYGsatoshi/Coglatas"), record["artifact"], declaration,
                                                    conclusion=record["workflowConclusion"], api=auth.github_api)
            require(archived == documents, "class-raw-evidence-not-trusted-artifact")
            if record["workflowConclusion"] == "failure":
                jobs = auth.github_api(os.environ.get("GITHUB_REPOSITORY", "NYGsatoshi/Coglatas"), f"/actions/runs/{declaration['workflowRunId']}/jobs?per_page=100")["jobs"]
                failed_steps = [s["name"] for job in jobs for s in job["steps"] if s["conclusion"] == "failure"]
                require(failed_steps == ["Replay all groups and fail when the bounded campaign has no baseline"], "unclassified-capture-workflow-failure")
        qualification = qualify(root, documents, load_json(root / record["targetFingerprintPath"]))
        require(qualification == load_json(evidence / "environment-class-qualification.json")
                and digest(qualification) == record["qualificationSha256"], "class-qualification-changed")
        approval = record["approval"]
        require(set(approval) == {"approver", "reference", "approvedAtUtc", "authorizationScope"}
                and approval["approver"] == manifest["authorization"]["approver"]
                and approval["authorizationScope"] in ("preserve-existing-small-owner-approval", "owner-conditional-class-promotion"), "class-promotion-not-authorized")
        require(utc(approval["approvedAtUtc"]) >= max(utc(g["endedAtUtc"]) for g in documents["raw-groups.json"]["groups"]), "class-approval-before-capture")
        if record["legacyApprovalCampaignId"] is not None:
            original_ledger = load_json(root / "performance/baseline-campaigns/approvals.json")["approvals"]
            original = next((a for a in original_ledger if a["campaignId"] == record["legacyApprovalCampaignId"]), None)
            require(original is not None and original["campaignId"] == record["campaignId"] and original["artifact"] == record["artifact"], "small-owner-approval-not-preserved")
            require(approval["authorizationScope"] == "preserve-existing-small-owner-approval"
                    and approval["approvedAtUtc"] == original["approvedAtUtc"], "small-approval-scope-or-time-changed")
        else:
            require(approval["authorizationScope"] == "owner-conditional-class-promotion", "medium-conditional-authorization-missing")
        reference = re.fullmatch(r"https://github\.com/NYGsatoshi/Coglatas/(?:issues|pull)/[1-9][0-9]*#issuecomment-([1-9][0-9]*)", approval["reference"])
        require(reference is not None, "class-approval-reference-invalid")
        if not historical and authenticate:
            review = auth.github_api(os.environ.get("GITHUB_REPOSITORY", "NYGsatoshi/Coglatas"), f"/issues/comments/{reference[1]}")
            require(review["user"]["login"] == approval["approver"] and utc(review["updated_at"]) <= utc(approval["approvedAtUtc"]), "class-approval-comment-author-or-time-invalid")
            if record["legacyApprovalCampaignId"] is None:
                expected = {"OWNER_CONDITIONAL_PROMOTION_RECORD", "CAMPAIGN_ID " + manifest["campaignId"],
                            "ARTIFACT_SHA256 " + record["artifact"]["digest"][7:], "QUALIFICATION_SHA256 " + record["qualificationSha256"],
                            "AUTHORIZATION_ORIGIN trusted-owner-Codex-request", "SELECTED_GROUP " + str(qualification["selectedGroupOrdinal"])}
                require(expected <= set(review["body"].splitlines()), "class-conditional-promotion-record-does-not-bind-evidence")
        expected = baseline_documents(documents, qualification, record["artifact"], approved=True)
        directory = f"{CATALOG}/{manifest['profile']}/{next(iter(expected.values()))['environmentCompatibilityDigest']}"
        require(record["baselineDirectory"] == directory and directory not in paths, "ambiguous-class-catalog")
        paths.add(directory)
        require({p.stem for p in (root / directory).glob("*.json")} == set(expected), "class-baseline-inventory-invalid")
        for scenario, document in expected.items():
            require(load_json(root / directory / (scenario + ".json")) == document, "class-baseline-samples-provenance-changed")
            if record["legacyApprovalCampaignId"] is not None:
                approved_document = load_json(root / original["baselineDirectory"] / (scenario + ".json"))
                require(document["samples"] == approved_document["samples"] and document["baselineSha"] == approved_document["baselineSha"]
                        and document["captureGroup"] == approved_document["provenance"]["selectedGroupOrdinal"], "small-approved-sample-selection-changed")
        if historical:
            auth.validate_retained_files(root, base_ref, record["evidenceDirectory"])
            auth.validate_retained_files(root, base_ref, directory)
    actual = {p.parent.relative_to(root).as_posix() for p in (root / CATALOG).rglob("*.json")}
    require(actual == paths, "class-catalog-without-enrollment")
    return len(records)
