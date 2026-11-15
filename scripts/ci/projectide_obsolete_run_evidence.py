#!/usr/bin/env python3
"""Read-only obsolete PR CI run admission for Security P0 #1974.

Never cancels, reruns, approves, merges, or grants the caller any permission.
Only reports whether *observed* external evidence warrants separate Owner review.
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass
from datetime import datetime
import json
import re
import subprocess
from typing import Any

REPOSITORY = re.compile(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")
REVISION = re.compile(r"^[0-9a-f]{40}$")
EXPECTED_WORKFLOW_ID = 290753477
WORKFLOW_PATH = ".github/workflows/ci.yml"
MAX_RESULTS = 100


@dataclass(frozen=True)
class Decision:
    observed: bool
    code: str
    # A successful observation is intentionally NOT authorization to cancel.
    cancellation_authorized: bool = False
    required_check_qualified: bool = False


class Refused(Exception):
    """Refuse without publishing PR data, run logs, or credentials."""


def need(value: bool, code: str) -> None:
    if not value:
        raise Refused(code)


def record(value: Any) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise Refused("MALFORMED_EVIDENCE")
    return value


def sequence(value: Any) -> list[Any]:
    if not isinstance(value, list):
        raise Refused("MALFORMED_EVIDENCE")
    return value


def full_page(value: Any, key: str) -> list[Any]:
    result = record(value)
    items = sequence(result[key])
    total = result.get("total_count")
    need(type(total) is int and total == len(items) and len(items) < MAX_RESULTS,
         "INCOMPLETE_OR_PAGINATED_EVIDENCE")
    return items


def iso_time(value: Any) -> datetime:
    need(isinstance(value, str) and value.endswith("Z"), "INVALID_RUN_TIME")
    try:
        return datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as error:
        raise Refused("INVALID_RUN_TIME") from error


def run_binding(run: dict[str, Any], repository: str, expected_id: int, attempt: int,
                expected_head: str, expected_branch: str) -> None:
    need(type(run.get("id")) is int and run["id"] == expected_id
         and type(run.get("run_attempt")) is int and run["run_attempt"] == attempt,
         "RUN_ATTEMPT_OR_ID_MISMATCH")
    need(run.get("name") == "PR CI" and run.get("event") == "pull_request"
         and run.get("workflow_id") == EXPECTED_WORKFLOW_ID
         and run.get("path") == WORKFLOW_PATH,
         "WRONG_WORKFLOW_OR_EVENT")
    need(run.get("head_sha") == expected_head
         and run.get("head_branch") == expected_branch,
         "RUN_SOURCE_MISMATCH")
    need(record(run["repository"]).get("full_name") == repository
         and record(run["head_repository"]).get("full_name") == repository,
         "WRONG_REPOSITORY")


def observed_identity(pr: dict[str, Any], repository: str, pr_number: int,
                      current_head: str, base: str) -> str:
    head, prbase = record(pr["head"]), record(pr["base"])
    need(pr.get("number") == pr_number and pr.get("state") == "open"
         and pr.get("merged") is False, "PR_NOT_CURRENT_OPEN")
    need(head.get("sha") == current_head and record(head["repo"]).get("full_name") == repository,
         "CURRENT_HEAD_DRIFT")
    need(prbase.get("ref") == base and record(prbase["repo"]).get("full_name") == repository,
         "BASE_OR_REPOSITORY_DRIFT")
    branch = head.get("ref")
    need(isinstance(branch, str) and bool(branch) and len(branch) <= 256,
         "HEAD_BRANCH_UNAVAILABLE")
    return branch


def evaluate(snapshot: dict[str, Any], *, repository: str, pr_number: int,
             old_run_id: int, new_run_id: int, old_head: str, current_head: str,
             base: str, old_attempt: int, new_attempt: int) -> Decision:
    """Evaluate one read-only snapshot. Even a positive result cannot cancel."""
    try:
        need(REPOSITORY.fullmatch(repository) is not None
             and REVISION.fullmatch(old_head) is not None
             and REVISION.fullmatch(current_head) is not None
             and old_head != current_head
             and isinstance(base, str) and 0 < len(base) <= 256
             and type(pr_number) is int and pr_number > 0
             and type(old_run_id) is int and old_run_id > 0
             and type(new_run_id) is int and new_run_id > 0 and new_run_id != old_run_id
             and type(old_attempt) is int and old_attempt > 0
             and type(new_attempt) is int and new_attempt > 0, "INVALID_INPUT")

        evidence = record(snapshot)
        branch = observed_identity(record(evidence["pr_before"]), repository, pr_number,
                                   current_head, base)
        need(observed_identity(record(evidence["pr_after"]), repository, pr_number,
                               current_head, base) == branch, "IDENTITY_CHANGED_ON_RECHECK")

        old, new = record(evidence["obsolete"]), record(evidence["successor"])
        run_binding(old, repository, old_run_id, old_attempt, old_head, branch)
        run_binding(new, repository, new_run_id, new_attempt, current_head, branch)
        # Empty run.pull_requests is a real GitHub API response for some PR workflows.
        # Require independent immutable commit->PR association instead of guessing.
        for run in (old, new):
            refs = sequence(run["pull_requests"])
            need(all(record(item).get("number") == pr_number for item in refs),
                 "RUN_PR_LINK_MISMATCH")
        associated = sequence(evidence["old_commit_prs"])
        need(0 < len(associated) < MAX_RESULTS
             and len([item for item in associated
                      if record(item).get("number") == pr_number]) == 1,
             "OLD_COMMIT_NOT_ASSOCIATED_WITH_PR")

        need(old.get("status") == "in_progress" and old.get("conclusion") is None,
             "OBSOLETE_RUN_NOT_ACTIVE")
        need(new.get("status") in ("pending", "queued") and new.get("conclusion") is None,
             "SUCCESSOR_NOT_WAITING")
        need(iso_time(old.get("created_at")) < iso_time(new.get("created_at")),
             "OBSOLETE_RUN_NOT_EARLIER")

        old_jobs = full_page(evidence["obsolete_jobs"], "jobs")
        waiting_jobs = full_page(evidence["successor_jobs"], "jobs")
        need(not waiting_jobs, "SUCCESSOR_ALREADY_DISPATCHED")
        frontends = [record(job) for job in old_jobs
                     if record(job).get("name") == "frontend-test"]
        need(len(frontends) == 1
             and frontends[0].get("status") == "in_progress"
             and frontends[0].get("conclusion") is None,
             "OBSOLETE_FRONTEND_NOT_ACTIVE")

        # Repeat the read after fetching jobs; a changed/finished run is never
        # accepted as still blocking. Actual cancellation requires further
        # Owner-side reauthorization immediately before the irreversible action.
        checked_old, checked_new = record(evidence["obsolete_again"]), record(evidence["successor_again"])
        run_binding(checked_old, repository, old_run_id, old_attempt, old_head, branch)
        run_binding(checked_new, repository, new_run_id, new_attempt, current_head, branch)
        need(checked_old.get("status") == "in_progress" and checked_old.get("conclusion") is None,
             "OBSOLETE_RUN_CHANGED_ON_RECHECK")
        need(checked_new.get("status") in ("pending", "queued")
             and checked_new.get("conclusion") is None,
             "SUCCESSOR_CHANGED_ON_RECHECK")
        return Decision(True, "OBSOLETE_CI_QUEUE_BLOCK_OBSERVED")
    except Refused as error:
        return Decision(False, str(error))
    except (KeyError, TypeError, AttributeError, ValueError, OverflowError):
        return Decision(False, "MALFORMED_EVIDENCE")


def github_get(path: str) -> Any:
    """Read-only gh API GET. Never invokes GitHub mutations or shells."""
    completed = subprocess.run(["gh", "api", "--method", "GET", path],
                               capture_output=True, timeout=20, check=False)
    if completed.returncode or len(completed.stdout) > 1024 * 1024:
        raise Refused("GITHUB_READ_UNAVAILABLE")
    try:
        return json.loads(completed.stdout)
    except (ValueError, UnicodeError) as error:
        raise Refused("MALFORMED_GITHUB_RESPONSE") from error


def live_snapshot(repository: str, pr_number: int, old_run: int,
                  new_run: int, old_head: str) -> dict[str, Any]:
    """Bounded read snapshots; the returned record is never a cancel token."""
    prefix = f"/repos/{repository}"
    return {
        "pr_before": github_get(f"{prefix}/pulls/{pr_number}"),
        "obsolete": github_get(f"{prefix}/actions/runs/{old_run}"),
        "successor": github_get(f"{prefix}/actions/runs/{new_run}"),
        "old_commit_prs": github_get(f"{prefix}/commits/{old_head}/pulls?per_page=100"),
        "obsolete_jobs": github_get(f"{prefix}/actions/runs/{old_run}/jobs?per_page=100"),
        "successor_jobs": github_get(f"{prefix}/actions/runs/{new_run}/jobs?per_page=100"),
        "pr_after": github_get(f"{prefix}/pulls/{pr_number}"),
        "obsolete_again": github_get(f"{prefix}/actions/runs/{old_run}"),
        "successor_again": github_get(f"{prefix}/actions/runs/{new_run}"),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--pr", required=True, type=int)
    parser.add_argument("--obsolete-run", required=True, type=int)
    parser.add_argument("--successor-run", required=True, type=int)
    parser.add_argument("--obsolete-head", required=True)
    parser.add_argument("--current-head", required=True)
    parser.add_argument("--base", required=True)
    parser.add_argument("--obsolete-attempt", required=True, type=int)
    parser.add_argument("--successor-attempt", required=True, type=int)
    args = parser.parse_args()
    # Validate the caller's namespace before building any request path.
    if not REPOSITORY.fullmatch(args.repository) or not REVISION.fullmatch(args.obsolete_head):
        decision = Decision(False, "INVALID_INPUT")
    else:
        try:
            snapshot = live_snapshot(args.repository, args.pr, args.obsolete_run,
                                     args.successor_run, args.obsolete_head)
            decision = evaluate(snapshot, repository=args.repository, pr_number=args.pr,
                                old_run_id=args.obsolete_run, new_run_id=args.successor_run,
                                old_head=args.obsolete_head, current_head=args.current_head,
                                base=args.base, old_attempt=args.obsolete_attempt,
                                new_attempt=args.successor_attempt)
        except (Refused, subprocess.TimeoutExpired, OSError):
            decision = Decision(False, "GITHUB_READ_UNAVAILABLE")
    print(json.dumps({"code": decision.code, "observed": decision.observed,
                      "observationOnly": True, "cancellationAuthorized": False,
                      "requiredCheckQualified": False, "securityOwnerReviewComplete": False,
                      "productionQualified": False, "releaseQualified": False},
                     sort_keys=True))
    return 0 if decision.observed else 2


if __name__ == "__main__":
    raise SystemExit(main())
