"""Resolve live Main run/artifact provenance before reconciling exact producer bytes."""

from __future__ import annotations

import argparse
import base64
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import urllib.request

import sec_arch_reconcile as binding

REPOSITORY = "NYGsatoshi/Coglatas"
WORKFLOW = ".github/workflows/main-build-artifacts.yml"
REQUIRED_JOBS = ("Main .NET producer", "Main Test / Frontend / Security / Main Test",
                 "Main Test / Frontend / Security / Main Security")
ARTIFACT_NAMES = ("main-dotnet-build", "main-sec-arch-kafka")
MAX_RESPONSE = 2 * 1024 * 1024


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, response, code, message, headers, new_url):
        raise binding.ReconciliationError("GitHub API redirects are unsupported.")


class LiveGitHub:
    """Only fixed HTTPS GitHub API requests; credentials never enter reports."""

    def __init__(self, token: str | None = None):
        self.token = token
        self.opener = urllib.request.build_opener(NoRedirect())

    def __call__(self, path: str) -> dict:
        binding.require(path.startswith("repos/" + REPOSITORY + "/") and
                        re.fullmatch(r"[A-Za-z0-9_./?=&-]+", path) is not None and ".." not in path,
                        "GitHub API path is unsupported.")
        headers = {"Accept": "application/vnd.github+json", "User-Agent": "Coglatas-SEC-ARCH-Provenance/1",
                   "X-GitHub-Api-Version": "2022-11-28"}
        if self.token:
            headers["Authorization"] = "Bearer " + self.token
        request = urllib.request.Request("https://api.github.com/" + path, headers=headers)
        with self.opener.open(request, timeout=30) as response:
            binding.require(response.status == 200, "Live GitHub metadata is unavailable.")
            data = response.read(MAX_RESPONSE + 1)
        binding.require(len(data) <= MAX_RESPONSE, "GitHub API metadata exceeds the bounded size.")
        result = json.loads(data, object_pairs_hook=binding.unique_object, parse_constant=binding.reject_constant)
        binding.require(isinstance(result, dict), "GitHub API metadata must be a document.")
        return result


def instant(value: str) -> datetime:
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    binding.require(parsed.tzinfo is not None, "GitHub metadata timestamp needs an offset.")
    return parsed.astimezone(timezone.utc)


def collect(api, path: str, key: str) -> list[dict]:
    rows = []
    for page in range(1, 11):
        document = api(path + "?per_page=100&page=" + str(page))
        total, current = document.get("total_count"), document.get(key)
        binding.require(type(total) is int and 0 <= total <= 1000 and isinstance(current, list) and
                        all(isinstance(row, dict) for row in current), "GitHub list scope is unsupported.")
        rows.extend(current)
        binding.require(len(rows) <= total, "GitHub list scope changed during resolution.")
        if len(rows) == total:
            return rows
        binding.require(len(current) == 100, "GitHub list scope is incomplete.")
    raise binding.ReconciliationError("GitHub list exceeds the bounded page count.")


def check_run(run: dict, candidate: str, run_id: int, attempt: int, now: datetime) -> tuple[int, int]:
    repository, head = run.get("repository", {}), run.get("head_repository", {})
    binding.require(run.get("id") == run_id and type(run.get("id")) is int and
                    run.get("run_attempt") == attempt and type(run.get("run_attempt")) is int and
                    run.get("head_sha") == candidate and run.get("head_branch") == "main" and
                    run.get("event") == "push" and run.get("path") == WORKFLOW and
                    run.get("status") == "completed" and run.get("conclusion") == "success" and
                    repository.get("full_name") == REPOSITORY and head.get("full_name") == REPOSITORY and
                    type(repository.get("id")) is int and repository["id"] > 0 and head.get("id") == repository["id"] and
                    type(run.get("workflow_id")) is int and run["workflow_id"] > 0,
                    "Live Main run candidate, workflow, attempt, origin or outcome differs.")
    binding.require(instant(run["created_at"]) <= instant(run["updated_at"]) <= now,
                    "Live Main run timestamps are inconsistent.")
    return repository["id"], run["workflow_id"]


def check_jobs(jobs: list[dict], candidate: str, run_id: int, attempt: int) -> list[dict]:
    binding.require(bool(jobs) and len({job.get("id") for job in jobs}) == len(jobs),
                    "Live job identities are missing or duplicated.")
    observed = []
    for name in REQUIRED_JOBS:
        matches = [job for job in jobs if job.get("name") == name]
        binding.require(len(matches) == 1, "A required verifier job is missing or duplicated.")
        job = matches[0]
        binding.require(type(job.get("id")) is int and job["id"] > 0 and job.get("run_id") == run_id and
                        job.get("run_attempt") == attempt and job.get("head_sha") == candidate and
                        job.get("status") == "completed" and job.get("conclusion") == "success",
                        "A required verifier job belongs to another candidate/attempt or did not succeed.")
        observed.append({"id": job["id"], "name": name, "conclusion": "success"})
    return observed


def check_artifacts(artifacts: list[dict], candidate: str, run_id: int, repository_id: int,
                    ids: tuple[int, int], now: datetime) -> list[dict]:
    binding.require(len(set(ids)) == 2 and all(type(identity) is int and identity > 0 for identity in ids) and
                    len({row.get("id") for row in artifacts}) == len(artifacts),
                    "Live artifact identities are missing or duplicated.")
    observed = []
    for identity, name in zip(ids, ARTIFACT_NAMES):
        matches = [row for row in artifacts if row.get("id") == identity and row.get("name") == name]
        binding.require(len(matches) == 1 and sum(row.get("name") == name for row in artifacts) == 1,
                        "The exact required artifact is missing, duplicated or replaced.")
        row, origin = matches[0], matches[0].get("workflow_run", {})
        binding.require(row.get("expired") is False and type(row.get("size_in_bytes")) is int and
                        0 < row["size_in_bytes"] <= binding.MAX_ARCHIVE and
                        isinstance(row.get("digest"), str) and re.fullmatch(r"sha256:[a-f0-9]{64}", row["digest"]) is not None and
                        origin.get("id") == run_id and origin.get("repository_id") == repository_id and
                        origin.get("head_repository_id") == repository_id and origin.get("head_sha") == candidate and
                        origin.get("head_branch") == "main" and instant(row["created_at"]) <= now < instant(row["expires_at"]),
                        "Live artifact digest, origin, size or expiry is invalid.")
        observed.append({"id": identity, "name": name, "digest": row["digest"][7:], "size": row["size_in_bytes"]})
    return observed


def resolve(api, candidate: str, run_id: int, attempt: int, artifact_ids: tuple[int, int], now: datetime,
            reconcile_bytes=None) -> dict:
    binding.require(bool(re.fullmatch(r"[a-f0-9]{40}", candidate)) and
                    type(run_id) is int and run_id > 0 and type(attempt) is int and attempt > 0 and
                    now.tzinfo is not None, "An independent exact candidate/run/attempt is required.")
    prefix = "repos/" + REPOSITORY
    run_path = prefix + "/actions/runs/" + str(run_id)
    run = api(run_path)
    repository_id, workflow_id = check_run(run, candidate, run_id, attempt, now)
    workflow = api(prefix + "/actions/workflows/" + str(workflow_id))
    binding.require(workflow.get("id") == workflow_id and workflow.get("path") == WORKFLOW,
                    "Live workflow identity differs.")
    source = api(prefix + "/contents/" + WORKFLOW + "?ref=" + candidate)
    binding.require(source.get("type") == "file" and source.get("encoding") == "base64" and
                    type(source.get("size")) is int and 0 < source["size"] <= MAX_RESPONSE,
                    "The immutable workflow source is unavailable.")
    binding.require(isinstance(source.get("content"), str), "Immutable workflow bytes are unavailable.")
    workflow_bytes = base64.b64decode("".join(source["content"].split()), validate=True)
    binding.require(len(workflow_bytes) == source["size"], "Immutable workflow source size differs.")
    jobs_path = run_path + "/attempts/" + str(attempt) + "/jobs"
    jobs = check_jobs(collect(api, jobs_path, "jobs"), candidate, run_id, attempt)
    artifact_path = run_path + "/artifacts"
    artifacts = check_artifacts(collect(api, artifact_path, "artifacts"), candidate, run_id, repository_id, artifact_ids, now)
    byte_report = reconcile_bytes(artifacts) if reconcile_bytes else None
    binding.require(check_run(api(run_path), candidate, run_id, attempt, now) == (repository_id, workflow_id) and
                    check_jobs(collect(api, jobs_path, "jobs"), candidate, run_id, attempt) == jobs and
                    check_artifacts(collect(api, artifact_path, "artifacts"), candidate, run_id, repository_id, artifact_ids, now) == artifacts,
                    "Run/job/artifact authority changed during resolution.")
    return {"schemaVersion": 1, "qualification": "LIVE_GITHUB_MAIN_PROVENANCE_OBSERVED",
            "repository": REPOSITORY, "candidateSha": candidate, "runId": str(run_id), "runAttempt": str(attempt),
            "workflowPath": WORKFLOW, "workflowId": workflow_id,
            "workflowSourceDigest": hashlib.sha256(workflow_bytes).hexdigest(), "jobs": jobs, "artifacts": artifacts,
            "producerByteBinding": byte_report, "ownerApproval": None, "trustedAttestation": "UNVERIFIED",
            "preAvaloniaVerdict": "PRE-AVALONIA SEC-ARCH: BLOCKED",
            "limits": ["Live GitHub HTTPS metadata authenticates server observations, not personal owner approval.",
                       "This is a refreshed snapshot, not an atomic or cryptographically signed execution attestation.",
                       "Job success cannot establish complete normative contract coverage or independently validate raw test semantics.",
                       "Artifact APIs do not expose upload attempt; exact receipt run/attempt binding remains mandatory.",
                       "Metadata-only resolution does not prove local producer/execution bytes."]}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidate-sha", required=True)
    parser.add_argument("--run-id", required=True, type=int)
    parser.add_argument("--run-attempt", required=True, type=int)
    parser.add_argument("--producer-id", required=True, type=int)
    parser.add_argument("--execution-id", required=True, type=int)
    parser.add_argument("--producer", type=Path)
    parser.add_argument("--execution", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    try:
        binding.require((args.producer is None) == (args.execution is None), "Both exact artifact paths are required together.")

        def bytes_report(artifacts):
            for path, artifact in zip((args.producer, args.execution), artifacts):
                binding.require(path.is_file() and path.stat().st_size == artifact["size"], "Artifact size differs from live metadata.")
            return binding.reconcile(args.producer, args.execution, args.candidate_sha, str(args.run_id),
                                     str(args.run_attempt), artifacts[0]["digest"], artifacts[1]["digest"])

        report = resolve(LiveGitHub(os.environ.get("GH_TOKEN") or os.environ.get("GITHUB_TOKEN")),
                         args.candidate_sha, args.run_id, args.run_attempt, (args.producer_id, args.execution_id),
                         datetime.now(timezone.utc), bytes_report if args.producer else None)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        with args.output.open("x", encoding="utf-8", newline="\n") as destination:
            destination.write(json.dumps(report, indent=2) + "\n")
    except (OSError, ValueError, TypeError, KeyError, RuntimeError):
        print("SEC-ARCH live provenance ERROR: authority, bounded input or exclusive output is invalid.")
        return 1
    print("SEC-ARCH live Main provenance observed; owner approval and final attestation remain UNVERIFIED.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
