#!/usr/bin/env python3
"""Evaluate authoritative PR review state and trusted GOV-06 exact-head state.

`evaluate()` remains network-free and deterministic for repository self-tests. The
CLI runs only inside the trusted default-branch evaluator. After review succeeds,
it fetches the GOV-06 evaluator/registry from the repository default branch and
requires the other exact-head required checks to pass before returning success.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys
import tempfile
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any

REVIEW_CONTROL_ID = "GOV-REVIEW-001"
PARENT_GATE_ID = "GOV-GATE-EXT-APPROVAL-001"


def _control(policy: dict[str, Any]) -> dict[str, Any]:
    matches = [
        c for c in policy.get("controls", [])
        if isinstance(c, dict) and c.get("id") == REVIEW_CONTROL_ID
    ]
    if len(matches) != 1:
        raise ValueError(f"policy must define exactly one {REVIEW_CONTROL_ID}")
    return matches[0]


def _timestamp(review: dict[str, Any]) -> str:
    value = review.get("submitted_at")
    return value if isinstance(value, str) else ""


def evaluate(policy: dict[str, Any], state: dict[str, Any]) -> dict[str, str]:
    expected = _control(policy).get("expected")
    if not isinstance(expected, dict):
        raise ValueError(f"{REVIEW_CONTROL_ID}.expected must be an object")

    if state.get("state") != "open":
        return {"state": "failure", "reason": "PR is not open."}
    if expected.get("draft_pr_blocks") is True and state.get("draft") is True:
        return {"state": "failure", "reason": "Draft PR cannot satisfy approval policy."}

    head_sha = state.get("head_sha")
    author = state.get("author")
    if not isinstance(head_sha, str) or not head_sha:
        raise ValueError("head_sha is required")
    if not isinstance(author, str) or not author:
        raise ValueError("author is required")

    raw_reviews = state.get("reviews")
    if not isinstance(raw_reviews, list) or not all(isinstance(item, dict) for item in raw_reviews):
        raise ValueError("reviews must be an array of objects")
    reviews: list[dict[str, Any]] = raw_reviews

    reviewer = expected.get("external_pr_approval_reviewer")
    if not isinstance(reviewer, str) or not reviewer.startswith("@"):
        raise ValueError("external_pr_approval_reviewer is invalid")
    reviewer_login = reviewer[1:]

    if author == reviewer_login and expected.get("owner_authored_pr_external_approval_required") is False:
        return {"state": "success", "reason": "Owner-authored PR; external-author approval is not required."}

    if expected.get("external_pr_current_head_approval_required") is not True:
        raise ValueError("external PR current-head approval must be required")

    current_head_reviews = [
        review for review in reviews
        if isinstance(review.get("user"), dict)
        and review["user"].get("login") == reviewer_login
        and review.get("commit_id") == head_sha
        and review.get("state") in {"APPROVED", "CHANGES_REQUESTED", "DISMISSED"}
    ]
    latest = max(current_head_reviews, key=_timestamp, default=None)
    latest_state = latest.get("state") if isinstance(latest, dict) else "NONE"
    if latest_state != "APPROVED":
        return {
            "state": "failure",
            "reason": f"Current head needs approval by {reviewer_login} (state: {latest_state}).",
        }
    return {"state": "success", "reason": f"Current head approved by {reviewer_login}."}


def _api_get(repository: str, path: str, token: str, *, raw: bool = False) -> Any:
    url = path if path.startswith("https://") else f"https://api.github.com/{path.lstrip('/')}"
    headers = {
        "Accept": "application/vnd.github.raw+json" if raw else "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "Coglatas-governance-review-evaluator",
        "Authorization": f"Bearer {token}",
    }
    try:
        with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=30) as response:
            body = response.read()
    except (urllib.error.URLError, TimeoutError) as exc:
        raise RuntimeError(f"GitHub API request failed for {url}: {exc}") from exc
    if raw:
        try:
            return body.decode("utf-8")
        except UnicodeDecodeError as exc:
            raise RuntimeError(f"GitHub raw response was not UTF-8 for {url}") from exc
    try:
        return json.loads(body.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise RuntimeError(f"GitHub API returned malformed JSON for {url}") from exc


def _resolve_pr_number(repository: str, head_sha: str, token: str) -> int:
    pulls = _api_get(repository, f"repos/{repository}/commits/{head_sha}/pulls?per_page=100", token)
    if not isinstance(pulls, list):
        raise RuntimeError("commit-to-PR lookup did not return an array")
    matches = [
        pr for pr in pulls
        if isinstance(pr, dict)
        and pr.get("state") == "open"
        and isinstance(pr.get("head"), dict)
        and pr["head"].get("sha") == head_sha
        and isinstance(pr.get("number"), int)
    ]
    if len(matches) != 1:
        raise RuntimeError(
            f"authoritative head must map to exactly one open PR; found {len(matches)}"
        )
    return int(matches[0]["number"])


def evaluate_required_checks(
    policy_path: Path,
    state: dict[str, Any],
    repository: str,
    token: str,
) -> dict[str, Any]:
    head_sha = state.get("head_sha")
    if not isinstance(head_sha, str) or not re.fullmatch(r"[0-9a-fA-F]{40}", head_sha):
        raise RuntimeError("GOV-06 integration requires a 40-character authoritative head SHA")
    repo = _api_get(repository, f"repos/{repository}", token)
    default_branch = repo.get("default_branch") if isinstance(repo, dict) else None
    if not isinstance(default_branch, str) or not default_branch:
        raise RuntimeError("repository default branch is unavailable")
    pr_number = _resolve_pr_number(repository, head_sha, token)

    with tempfile.TemporaryDirectory(prefix="gov06-review-") as td:
        root = Path(td)
        contract_path = root / "required_check_contract.py"
        parent_path = root / "required_check_parent.py"
        registry_path = root / "required-checks.json"
        for remote, local in (
            ("scripts/ci/required_check_contract.py", contract_path),
            ("scripts/ci/required_check_parent.py", parent_path),
            ("governance/required-checks.json", registry_path),
        ):
            quoted = urllib.parse.quote(remote, safe="/")
            content = _api_get(
                repository,
                f"repos/{repository}/contents/{quoted}?ref={urllib.parse.quote(default_branch, safe='')}",
                token,
                raw=True,
            )
            local.write_text(content, encoding="utf-8")

        sys.path.insert(0, str(root))
        try:
            import required_check_contract as guard  # type: ignore
            import required_check_parent as parent  # type: ignore

            registry = guard.load_required_check_registry(registry_path, policy_path)
            return parent.evaluate_from_trusted_parent(
                guard.GitHubApi(token),
                repository,
                pr_number,
                registry,
                PARENT_GATE_ID,
            )
        finally:
            sys.path.remove(str(root))
            sys.modules.pop("required_check_contract", None)
            sys.modules.pop("required_check_parent", None)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--policy", required=True, type=Path)
    parser.add_argument("--state", required=True, type=Path)
    args = parser.parse_args()
    policy = json.loads(args.policy.read_text(encoding="utf-8"))
    state = json.loads(args.state.read_text(encoding="utf-8"))
    if not isinstance(policy, dict) or not isinstance(state, dict):
        raise SystemExit("policy and state must be JSON objects")

    decision = evaluate(policy, state)
    if decision["state"] != "success":
        print(json.dumps(decision, separators=(",", ":")))
        return 0

    repository = os.environ.get("REPOSITORY")
    token = os.environ.get("GH_TOKEN") or os.environ.get("GITHUB_TOKEN")
    if not repository or not token:
        raise SystemExit("trusted GOV-06 integration requires REPOSITORY and GH_TOKEN/GITHUB_TOKEN")

    required = evaluate_required_checks(args.policy, state, repository, token)
    required_decision = required.get("decision")
    if required_decision == "pass":
        decision = {
            "state": "success",
            "reason": "Review, GOV-02, and GOV-06 exact-head requirements satisfied.",
        }
    elif required_decision == "pending":
        decision = {
            "state": "failure",
            "reason": "GOV-06 exact-head required checks are pending; merge remains blocked.",
        }
    else:
        decision = {
            "state": "failure",
            "reason": "GOV-06 exact-head required-check reconciliation failed; merge blocked.",
        }
    print(json.dumps(decision, separators=(",", ":")))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
