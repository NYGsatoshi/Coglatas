#!/usr/bin/env python3
"""Evaluate current ProjectIDE ReSharper evidence before a separate authorized squash."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
from hashlib import sha256
from importlib.util import module_from_spec, spec_from_file_location
from io import BytesIO
import json
from pathlib import Path
import re
import subprocess
from urllib.parse import quote
from zipfile import BadZipFile, ZipFile

REPOSITORY = "NYGsatoshi/Coglatas"
BASE = "projectide/integration-20261031"
SHA = re.compile(r"[0-9a-f]{40}")
WORKFLOW = ".github/workflows/ci.yml"
APP = 15368
TRACKED_CHECKS = {
    "build-test", "frontend-test", "security-scan", "publication-readiness",
    "functional-fast", "performance-fast",
}
INSPECTION = "ReSharper PR fast lane / ReSharper InspectCode / PR"
STEPS = (
    "Test exact composition and SARIF false-green controls",
    "Run ReSharper InspectCode against shared build outputs",
    "Verify independent inspection scope and complete SARIF",
    "Gate changed files on ReSharper findings",
    "Upload ReSharper report",
)
ARTIFACT = "resharper-pr-sarif"
MAX_ARCHIVE_BYTES = 32_000_000
THREAD_QUERY = """
query($owner: String!, $repo: String!, $pr: Int!) {
  repository(owner: $owner, name: $repo) {
    databaseId
    pullRequest(number: $pr) {
      number state isDraft headRefOid reviewDecision
      reviewThreads(first: 100) {
        totalCount pageInfo { hasNextPage } nodes { isResolved }
      }
    }
  }
}
"""


class Hold(Exception):
    """A fixed refusal reason suitable for a public integration receipt."""


def require(condition, reason):
    if not condition:
        raise Hold(reason)


def complete_success(item):
    return item["status"] == "completed" and item["conclusion"] == "success"


def complete_page(payload, key, reason):
    entries = payload[key]
    require(isinstance(entries, list) and payload["total_count"] == len(entries)
            and len(entries) <= 100, reason)
    return entries


def exactly_one(items, reason):
    require(len(items) == 1, reason)
    return items[0]


def timestamp(value):
    require(isinstance(value, str) and value.endswith("Z"), "INSPECTION_TIMESTAMPS")
    return datetime.fromisoformat(value.replace("Z", "+00:00"))


def verify_pr(pr, number, head, base, merge):
    require(pr["number"] == number and pr["state"] == "open" and pr["merged"] is False
            and pr["draft"] is False and pr["mergeable"] is True, "PR_NOT_OPEN_READY")
    require(pr["head"]["sha"] == head and pr["base"]["sha"] == base
            and pr["base"]["ref"] == BASE and pr["merge_commit_sha"] == merge
            and pr["head"]["repo"]["full_name"] == REPOSITORY
            and pr["base"]["repo"]["full_name"] == REPOSITORY
            and pr["head"]["repo"]["id"] == pr["base"]["repo"]["id"], "PR_SOURCE_DRIFT")


def verify_run(run, workflow, run_id, attempt, pr, head, base):
    repository = pr["base"]["repo"]["id"]
    require(workflow["name"] == "PR CI" and workflow["path"] == WORKFLOW
            and run["id"] == run_id and run["run_attempt"] == attempt
            and run["workflow_id"] == workflow["id"] and run["path"] == WORKFLOW
            and run["name"] == "PR CI" and run["event"] == "pull_request"
            and run["head_sha"] == head and run["head_branch"] == pr["head"]["ref"]
            and run["repository"]["id"] == repository
            and run["head_repository"]["id"] == repository, "CI_PROVENANCE")
    run_pr = exactly_one([item for item in run["pull_requests"]
                          if item["id"] == pr["id"] and item["number"] == pr["number"]],
                         "CI_PR_IDENTITY")
    require(run_pr["head"]["ref"] == pr["head"]["ref"] and run_pr["head"]["sha"] == head
            and run_pr["base"]["ref"] == BASE and run_pr["base"]["sha"] == base
            and run_pr["head"]["repo"]["id"] == repository
            and run_pr["base"]["repo"]["id"] == repository, "CI_PR_SOURCE_DRIFT")
    require(complete_success(run), "CI_NOT_SUCCESSFUL")


def verify_checks(checks, rules, job, run, head):
    required = {name: APP for name in TRACKED_CHECKS}
    for rule in rules:
        if rule["type"] == "required_status_checks":
            for context in rule["parameters"]["required_status_checks"]:
                name = context["context"]
                producer = context.get("integration_id")
                require(producer in (None, required.get(name, producer)), "CHECK_PRODUCER_CONFLICT")
                required.setdefault(name, producer)

    for name, producer in required.items():
        check = exactly_one([item for item in checks if item["name"] == name],
                            "REQUIRED_CHECK_AMBIGUOUS")
        require(check["head_sha"] == head and complete_success(check)
                and (producer is None or check["app"]["id"] == producer), "REQUIRED_CHECK_INCOMPLETE")

    check = exactly_one([item for item in checks if item["name"] == INSPECTION],
                        "INSPECTION_CHECK_AMBIGUOUS")
    require(check["id"] == job["id"] and check["app"]["id"] == APP
            and check["check_suite"]["id"] == run["check_suite_id"]
            and check["head_sha"] == head and check["url"] == job["check_run_url"]
            and complete_success(check), "INSPECTION_CHECK_MISMATCH")


def verify_jobs(payload, run_id, attempt, head):
    jobs = complete_page(payload, "jobs", "ORDINARY_JOBS_INCOMPLETE")
    require(bool(jobs) and all(job["run_id"] == run_id and job["run_attempt"] == attempt
                              and job["head_sha"] == head and job["status"] == "completed"
                              and job["conclusion"] in {"success", "skipped"} for job in jobs),
            "ORDINARY_JOBS_INCOMPLETE")
    job = exactly_one([item for item in jobs if item["name"] == INSPECTION],
                      "INSPECTION_JOB_AMBIGUOUS")
    require(complete_success(job), "INSPECTION_JOB_INCOMPLETE")
    previous_number = 0
    previous_end = timestamp(job["started_at"])
    for name in STEPS:
        step = exactly_one([item for item in job["steps"] if item["name"] == name],
                           "INSPECTION_STEP_AMBIGUOUS")
        start, end = timestamp(step["started_at"]), timestamp(step["completed_at"])
        require(complete_success(step) and step["number"] > previous_number
                and previous_end <= start <= end <= timestamp(job["completed_at"]),
                "INSPECTION_STEP_INCOMPLETE")
        previous_number, previous_end = step["number"], end
    return job, step


def verify_artifact(artifact, run, pr, upload, archive, merge):
    repository = pr["base"]["repo"]["id"]
    producer = artifact["workflow_run"]
    require(artifact["name"] == ARTIFACT and artifact["expired"] is False
            and producer["id"] == run["id"] and producer["head_sha"] == run["head_sha"]
            and producer["head_branch"] == run["head_branch"]
            and producer["repository_id"] == repository and producer["head_repository_id"] == repository
            and timestamp(upload["started_at"]) <= timestamp(artifact["created_at"])
            <= timestamp(upload["completed_at"]), "ARTIFACT_PROVENANCE")
    digest = sha256(archive).hexdigest()
    require(0 < len(archive) <= MAX_ARCHIVE_BYTES and len(archive) == artifact["size_in_bytes"]
            and artifact["digest"] == f"sha256:{digest}", "ARTIFACT_INTEGRITY")

    with ZipFile(BytesIO(archive)) as package:
        members = package.infolist()
        names = package.namelist()
        require(len(names) == len(set(names)) and sum(item.file_size for item in members) <= MAX_ARCHIVE_BYTES
                and {"inspectcode.sarif.json", "changed-files.nul", "projects.txt"} <= set(names)
                and package.testzip() is None, "ARTIFACT_INTEGRITY")
        sarif_bytes = package.read("inspectcode.sarif.json")
        validate_sarif(json.loads(sarif_bytes), merge)
        entry = load_quality_script("projectide_entry_resharper.py")
        changed = package.read("changed-files.nul").decode("utf-8", errors="surrogateescape").split("\0")
        if entry.entry_source_witnesses(changed):
            require("composition.json" in names, "ARTIFACT_COMPOSITION_MISSING")
        if "composition.json" in names:
            composition = json.loads(package.read("composition.json"))
            probe = package.read("scope-probe.sarif.json")
            require(composition["compositionCommit"] == merge and composition["integratedEntry"] is True
                    and composition["scopeProbe"] == "Passed"
                    and composition["sarifSha256"] == sha256(sarif_bytes).hexdigest()
                    and composition["probeSarifSha256"] == sha256(probe).hexdigest(),
                    "ARTIFACT_COMPOSITION")
            validate_sarif(json.loads(probe), merge)
    return digest


def load_quality_script(name):
    spec = spec_from_file_location(name, Path(__file__).resolve().parents[1] / "quality" / name)
    module = module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def validate_sarif(sarif, merge):
    load_quality_script("check-resharper-pr-sarif.py").validate_sarif(sarif)
    report = exactly_one(sarif["runs"], "ARTIFACT_SARIF_SCOPE")
    require([entry["revisionId"] for entry in report["versionControlProvenance"]] == [merge],
            "ARTIFACT_SARIF_SOURCE")


def verify_threads(result, pr, head):
    require(not result.get("errors"), "REVIEW_READBACK_UNAVAILABLE")
    repository = result["data"]["repository"]
    data = repository["pullRequest"]
    require(repository["databaseId"] == pr["base"]["repo"]["id"]
            and data["number"] == pr["number"] and data["state"] == "OPEN"
            and data["isDraft"] is False and data["headRefOid"] == head,
            "REVIEW_SOURCE_DRIFT")
    threads = data["reviewThreads"]
    require(threads["pageInfo"]["hasNextPage"] is False
            and threads["totalCount"] == len(threads["nodes"]), "REVIEW_THREADS_INCOMPLETE")
    require(all(thread["isResolved"] is True for thread in threads["nodes"]),
            "REVIEW_THREADS_UNRESOLVED")
    require(data["reviewDecision"] in {None, "APPROVED"}, "REVIEW_NOT_READY")
    return threads["totalCount"]


def gh(*args):
    result = subprocess.run(["gh", "api", *args], capture_output=True, check=False, timeout=60)
    require(result.returncode == 0 and len(result.stdout) <= MAX_ARCHIVE_BYTES, "GITHUB_EVIDENCE_UNAVAILABLE")
    return result.stdout


def qualify(args):
    require(args.repository == REPOSITORY and all(SHA.fullmatch(value) for value in (
        args.expected_head, args.expected_base, args.expected_tree))
        and min(args.pr, args.run_id, args.attempt) > 0, "INVALID_INPUT")
    root = f"repos/{args.repository}"
    head, base = args.expected_head, args.expected_base
    pr_path = f"pulls/{args.pr}"
    base_ref = f"git/ref/heads/{BASE}"
    test_ref = f"git/ref/pull/{args.pr}/merge"
    run_path = f"actions/runs/{args.run_id}"

    def read(path):
        return json.loads(gh("--method", "GET", f"{root}/{path}"))

    pr = read(pr_path)
    merge = read(test_ref)["object"]["sha"]
    verify_pr(pr, args.pr, head, base, merge)
    require(read(base_ref)["object"]["sha"] == base, "BASE_REF_DRIFT")
    composition = read(f"git/commits/{merge}")
    require(composition["sha"] == merge and composition["tree"]["sha"] == args.expected_tree
            and [parent["sha"] for parent in composition["parents"]] == [base, head], "TEST_MERGE_DRIFT")

    workflow = read("actions/workflows/ci.yml")
    run = read(run_path)
    verify_run(run, workflow, args.run_id, args.attempt, pr, head, base)
    job, upload = verify_jobs(read(f"{run_path}/attempts/{args.attempt}/jobs?per_page=100"),
                              args.run_id, args.attempt, head)
    checks = complete_page(read(f"commits/{head}/check-runs?per_page=100"), "check_runs",
                           "CHECKS_INCOMPLETE")
    rules = read(f"rules/branches/{quote(BASE, safe='')}")
    verify_checks(checks, rules, job, run, head)

    artifacts = complete_page(read(f"{run_path}/artifacts?per_page=100"), "artifacts",
                              "ARTIFACT_LIST_INCOMPLETE")
    artifact = exactly_one([item for item in artifacts if item["name"] == ARTIFACT],
                           "INSPECTION_ARTIFACT_AMBIGUOUS")
    if args.artifact_zip:
        with args.artifact_zip.open("rb") as stream:
            archive = stream.read(MAX_ARCHIVE_BYTES + 1)
    else:
        archive = gh("--method", "GET", f"{root}/actions/artifacts/{artifact['id']}/zip")
    digest = verify_artifact(artifact, run, pr, upload, archive, merge)

    owner, repo = args.repository.split("/")
    review = json.loads(gh("graphql", "-f", f"query={THREAD_QUERY}",
                           "-f", f"owner={owner}", "-f", f"repo={repo}", "-F", f"pr={args.pr}"))
    thread_count = verify_threads(review, pr, head)
    verify_pr(read(pr_path), args.pr, head, base, merge)
    require(read(base_ref)["object"]["sha"] == base and read(test_ref)["object"]["sha"] == merge,
            "FINAL_REF_DRIFT")
    latest = read(run_path)
    require(latest["run_attempt"] == args.attempt and latest["check_suite_id"] == run["check_suite_id"]
            and complete_success(latest), "FINAL_CI_ATTEMPT_DRIFT")

    return {
        "decision": "QUALIFIED", "repository": args.repository, "pr": args.pr,
        "head": head, "base": base, "test_merge": merge, "tree": args.expected_tree,
        "workflow_id": workflow["id"], "run_id": args.run_id, "attempt": args.attempt,
        "inspection_check_id": job["id"], "inspection_artifact_id": artifact["id"],
        "inspection_artifact_sha256": digest, "resolved_review_threads": thread_count,
        "observed_at_utc": datetime.now(timezone.utc).isoformat(),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--pr", required=True, type=int)
    parser.add_argument("--expected-head", required=True)
    parser.add_argument("--expected-base", required=True)
    parser.add_argument("--expected-tree", required=True)
    parser.add_argument("--run-id", required=True, type=int)
    parser.add_argument("--attempt", required=True, type=int)
    parser.add_argument("--artifact-zip", type=Path, help="Original provider ZIP when CLI download is unavailable")
    args = parser.parse_args()
    try:
        print(json.dumps(qualify(args), sort_keys=True))
    except Hold as error:
        print(f"HOLD:{error}")
        return 1
    except (OSError, ValueError, KeyError, TypeError, AttributeError, RuntimeError, BadZipFile,
            subprocess.TimeoutExpired):
        print("HOLD:EVIDENCE_UNAVAILABLE_OR_MALFORMED")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
