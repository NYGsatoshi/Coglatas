#!/usr/bin/env python3
"""Read-only exact-source ProjectIDE ReSharper premerge evidence gate (P0 #1954).

This is a *technical* source/probe gate, never an Owner approval or a merge action.
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
from dataclasses import dataclass
from typing import Any

SHA = re.compile(r"^[0-9a-f]{40}$")
REPO = re.compile(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")
PRODUCT_BASE = "projectide/integration-20261031"
CHECK_APP = 15368
REQUIRED = (
    "build-test", "frontend-test", "security-scan", "publication-readiness",
    "functional-fast", "performance-fast",
)
RESHARPER_JOB = "ReSharper PR fast lane / ReSharper InspectCode / PR"
REQUIRED_STEPS = (
    "Test exact composition and SARIF false-green controls",
    "Run ReSharper InspectCode against shared build outputs",
    "Verify independent inspection scope and complete SARIF",
    "Gate changed files on ReSharper findings",
    "Upload ReSharper report",
)


@dataclass(frozen=True)
class Decision:
    qualified: bool
    code: str


class GateDenied(Exception):
    """A fixed, content-free refusal code for a missing source/probe requirement."""


def require(condition: bool, code: str) -> None:
    if not condition:
        raise GateDenied(code)


def _map(value: Any) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise GateDenied("MALFORMED")
    return value


def _seq(value: Any) -> list[Any]:
    if not isinstance(value, list):
        raise GateDenied("MALFORMED")
    return value


def evaluate(snapshot: dict[str, Any], repo: str, pr_number: int, head: str,
             base: str, run_id: int, attempt: int) -> Decision:
    """Evaluate immutable external evidence without mutating a pull request or ruleset."""
    try:
        require(REPO.fullmatch(repo) is not None and SHA.fullmatch(head) is not None
                and SHA.fullmatch(base) is not None and pr_number > 0
                and run_id > 0 and attempt > 0, "INVALID_INPUT")
        evidence = _map(snapshot)
        pr = _map(evidence["pr"])
        pr_head, pr_base = _map(pr["head"]), _map(pr["base"])
        require(pr.get("number") == pr_number and pr.get("state") == "open"
                and pr.get("draft") is False and pr.get("merged") is False,
                "PR_NOT_OPEN_READY")
        require(pr_head.get("sha") == head and _map(pr_head["repo"]).get("full_name") == repo,
                "HEAD_DRIFT")
        require(pr_base.get("sha") == base and pr_base.get("ref") == PRODUCT_BASE,
                "BASE_DRIFT")
        require(pr_head.get("ref") is not None, "HEAD_DRIFT")

        check_data = _map(evidence["checks"])
        checks = _seq(check_data["check_runs"])
        require(isinstance(check_data.get("total_count"), int)
                and check_data["total_count"] == len(checks) and len(checks) < 100,
                "INCOMPLETE_CHECK_PAGES")
        for name in REQUIRED:
            found = [item for item in checks
                     if isinstance(item, dict) and item.get("name") == name]
            require(len(found) == 1, "REQUIRED_CHECK_AMBIGUOUS")
            check = found[0]
            require(check.get("head_sha") == head and
                    _map(check.get("app")).get("id") == CHECK_APP and
                    check.get("status") == "completed" and
                    check.get("conclusion") == "success",
                    "REQUIRED_CHECK_INCOMPLETE")

        run = _map(evidence["run"])
        require(run.get("id") == run_id and run.get("run_attempt") == attempt
                and run.get("event") == "pull_request" and run.get("name") == "PR CI"
                and run.get("head_sha") == head
                and run.get("head_branch") == pr_head["ref"],
                "CI_PROVENANCE_MISMATCH")
        require(run.get("status") == "completed" and run.get("conclusion") == "success",
                "CI_RUN_INCOMPLETE")

        jobs_data = _map(evidence["jobs"])
        jobs = _seq(jobs_data["jobs"])
        require(isinstance(jobs_data.get("total_count"), int)
                and jobs_data["total_count"] == len(jobs) and len(jobs) < 100,
                "INCOMPLETE_JOB_PAGES")
        matching = [job for job in jobs if isinstance(job, dict)
                    and job.get("name") == RESHARPER_JOB]
        require(len(matching) == 1, "RESHARPER_JOB_MISSING")
        job = matching[0]
        require(job.get("run_id") == run_id and job.get("head_sha") == head
                and job.get("status") == "completed" and job.get("conclusion") == "success",
                "RESHARPER_JOB_INCOMPLETE")
        steps = _seq(job["steps"])
        seen: list[int] = []
        for step_name in REQUIRED_STEPS:
            found = [(i, item) for i, item in enumerate(steps)
                     if isinstance(item, dict) and item.get("name") == step_name]
            require(len(found) == 1, "RESHARPER_PROBE_MISSING")
            index, step = found[0]
            require(step.get("status") == "completed"
                    and step.get("conclusion") == "success"
                    and step.get("completed_at") is not None, "RESHARPER_PROBE_INCOMPLETE")
            seen.append(index)
        require(seen == sorted(seen) and len(set(seen)) == len(seen), "PROBE_ORDER_DRIFT")
        return Decision(True, "TECHNICAL_CI_EVIDENCE_QUALIFIED")
    except GateDenied as error:
        return Decision(False, str(error))
    except (TypeError, ValueError, KeyError, AttributeError):
        return Decision(False, "MALFORMED")


def _gh_json(endpoint: str) -> dict[str, Any]:
    """Read the actual GitHub REST endpoint; never log response bodies or credentials."""
    result = subprocess.run(
        ["gh", "api", "--method", "GET", endpoint],
        capture_output=True, check=False, text=True, encoding="utf-8", timeout=30,
    )
    if result.returncode != 0 or len(result.stdout) > 12_000_000:
        raise GateDenied("GITHUB_EVIDENCE_UNAVAILABLE")
    value = json.loads(result.stdout)
    return _map(value)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--pr", type=int, required=True)
    parser.add_argument("--expected-head", required=True)
    parser.add_argument("--expected-base", required=True)
    parser.add_argument("--run-id", type=int, required=True)
    parser.add_argument("--attempt", type=int, required=True)
    parser.add_argument("--fixture", help="Unit-test snapshot only; cannot issue a live qualification")
    args = parser.parse_args()
    try:
        if args.fixture:
            with open(args.fixture, encoding="utf-8") as stream:
                source = _map(json.load(stream))
        else:
            require(REPO.fullmatch(args.repository) is not None and SHA.fullmatch(args.expected_head)
                    is not None and SHA.fullmatch(args.expected_base) is not None,
                    "INVALID_INPUT")
            root = f"/repos/{args.repository}"
            source = {
                "pr": _gh_json(f"{root}/pulls/{args.pr}"),
                "checks": _gh_json(f"{root}/commits/{args.expected_head}/check-runs?per_page=100"),
                "run": _gh_json(f"{root}/actions/runs/{args.run_id}"),
                "jobs": _gh_json(f"{root}/actions/runs/{args.run_id}/jobs?per_page=100"),
            }
        decision = evaluate(source, args.repository, args.pr, args.expected_head,
                            args.expected_base, args.run_id, args.attempt)
        if not decision.qualified:
            print("HOLD:" + decision.code)
            return 1
        if args.fixture:
            print("SYNTHETIC_SOURCE_GATE_ONLY_NOT_MERGE_APPROVAL")
            return 0
        print("CI_PROVENANCE_QUALIFIED_NOT_OWNER_APPROVAL")
        return 0
    except (GateDenied, OSError, ValueError, KeyError, subprocess.TimeoutExpired,
            json.JSONDecodeError):
        print("HOLD:GITHUB_EVIDENCE_UNAVAILABLE")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
