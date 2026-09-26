#!/usr/bin/env python3
"""Require green MVP-A checks from trusted GitHub Actions for the exact candidate commit."""

from __future__ import annotations

import json
import os
import sys
import urllib.error
import urllib.request

GITHUB_ACTIONS_APP_ID = 15368
GITHUB_ACTIONS_APP_SLUG = "github-actions"
PAGE_SIZE = 100
MAX_PAGES = 20
REQUIRED_CHECKS = (
    "build-test",
    "frontend-test",
    "security-scan",
    "publication-readiness",
    "frontend-static-analysis",
    "licensed-real-backend",
    "sbom-source",
    "sbom-image-trusted",
)


def required_env(name: str) -> str:
    value = os.environ.get(name, "").strip()
    if not value:
        raise RuntimeError(f"{name} is required for MVP-A final check verification.")
    return value


def fetch_page(url: str, token: str, sha: str) -> dict[str, object]:
    request = urllib.request.Request(
        url,
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {token}",
            "User-Agent": "coglatas-mvp-a-final-gate",
            "X-GitHub-Api-Version": "2022-11-28",
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            payload = json.load(response)
    except urllib.error.HTTPError as error:
        raise RuntimeError(
            f"Unable to read check runs for {sha}: HTTP {error.code}."
        ) from error
    except urllib.error.URLError as error:
        raise RuntimeError(f"Unable to read check runs for {sha}: request failed.") from error

    if not isinstance(payload, dict):
        raise RuntimeError(f"Unable to read check runs for {sha}: GitHub returned a non-object payload.")
    return payload


def fetch_check_runs(repository: str, sha: str, token: str, api_url: str) -> list[dict[str, object]]:
    check_runs: list[dict[str, object]] = []

    for page in range(1, MAX_PAGES + 1):
        url = (
            f"{api_url}/repos/{repository}/commits/{sha}/check-runs"
            f"?per_page={PAGE_SIZE}&page={page}&filter=all"
        )
        payload = fetch_page(url, token, sha)
        raw_items = payload.get("check_runs", [])
        if not isinstance(raw_items, list):
            raise RuntimeError(f"Unable to read check runs for {sha}: 'check_runs' is not a list.")

        items = [item for item in raw_items if isinstance(item, dict)]
        check_runs.extend(items)

        total_count = payload.get("total_count")
        if isinstance(total_count, int) and len(check_runs) >= total_count:
            return check_runs
        if len(raw_items) < PAGE_SIZE:
            return check_runs

    raise RuntimeError(
        f"Unable to read all check runs for {sha}: pagination exceeded {MAX_PAGES} pages."
    )


def check_run_id(check: dict[str, object]) -> int | None:
    """Extract and validate the check run ID from a check run object.

    Args:
        check: A GitHub check run dict

    Returns:
        The check run ID as a positive integer, or None if invalid or missing
    """
    raw_id = check.get("id")
    if isinstance(raw_id, bool) or not isinstance(raw_id, int) or raw_id <= 0:
        return None
    return raw_id


def check_app_identity(check: dict[str, object]) -> tuple[int | None, str]:
    app = check.get("app")
    if not isinstance(app, dict):
        return None, ""

    raw_id = app.get("id")
    app_id = raw_id if isinstance(raw_id, int) else None
    raw_slug = app.get("slug")
    slug = raw_slug if isinstance(raw_slug, str) else ""
    return app_id, slug


def is_trusted_required_check(check: dict[str, object], sha: str) -> bool:
    app_id, slug = check_app_identity(check)
    return (
        check.get("head_sha") == sha
        and app_id == GITHUB_ACTIONS_APP_ID
        and slug == GITHUB_ACTIONS_APP_SLUG
    )


def is_green_required_check(check: dict[str, object]) -> bool:
    """Reduce live check-run state to a bounded boolean before any log output."""
    return check.get("status") == "completed" and check.get("conclusion") == "success"


def evaluate_required_checks(
    check_runs: list[dict[str, object]], sha: str
) -> tuple[list[str], list[tuple[str, bool]]]:
    """Evaluate required checks by selecting the latest trusted check run by ID.

    For each required check, finds all trusted check runs for the exact SHA,
    validates their IDs, and selects the one with the highest ID as latest.
    Uses check run ID ordering instead of timestamps to avoid masking attacks.

    Args:
        check_runs: List of GitHub check run dicts to evaluate
        sha: The exact commit SHA to match against

    Returns:
        Tuple of (failure messages, summary of check results as name-boolean pairs)
    """
    failures: list[str] = []
    summary: list[tuple[str, bool]] = []

    for name in REQUIRED_CHECKS:
        named_matches = [check for check in check_runs if check.get("name") == name]
        trusted_matches = [
            check for check in named_matches if is_trusted_required_check(check, sha)
        ]

        if not trusted_matches:
            failures.append(f"{name}: no trusted GitHub Actions check run exists for exact candidate")
            summary.append((name, False))
            continue

        invalid_ids = [check for check in trusted_matches if check_run_id(check) is None]
        if invalid_ids:
            failures.append(f"{name}: trusted check run has an invalid or missing id")
            summary.append((name, False))
            continue

        latest = max(trusted_matches, key=lambda check: check_run_id(check) or 0)
        green = is_green_required_check(latest)
        summary.append((name, green))
        if not green:
            failures.append(f"{name}: latest trusted check is not completed successfully")

    return failures, summary


def main() -> int:
    repository = required_env("GITHUB_REPOSITORY")
    sha = required_env("GITHUB_SHA")
    token = required_env("GITHUB_TOKEN")
    api_url = os.environ.get("GITHUB_API_URL", "https://api.github.com").strip()
    check_runs = fetch_check_runs(repository, sha, token, api_url)
    failures, summary = evaluate_required_checks(check_runs, sha)

    print("MVP-A final check evidence:")
    for name, green in summary:
        result = "PASS" if green else "FAIL"
        print(f"- {name}: {result}")

    if failures:
        print("MVP-A final gate failed:", file=sys.stderr)
        for failure in failures:
            print(f"- {failure}", file=sys.stderr)
        return 1

    print(
        f"MVP-A final gate passed: {len(REQUIRED_CHECKS)} required checks are green, "
        f"trusted, and bound to exact SHA {sha}."
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except RuntimeError as error:
        print(error, file=sys.stderr)
        raise SystemExit(1) from error
