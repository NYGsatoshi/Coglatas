#!/usr/bin/env python3
"""Resolve an approved immutable revision for native SonarQube Cloud PR decoration."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
from urllib.error import HTTPError
from urllib.parse import urlencode
from urllib.request import Request, urlopen

GITHUB_API = "https://api.github.com"
SHA = re.compile(r"[0-9a-f]{40}\Z")
REPOSITORY = re.compile(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\Z")
GITHUB_API_PATH = re.compile(
    r"/(?:environments/sonarcloud-analysis|pulls(?:/[1-9][0-9]*)?|git/ref/heads/main)\Z"
)


def request(base, endpoint, token, params=None):
    """Restrict requests to GitHub and the precise read-only API paths used here."""
    match = re.fullmatch(r"/repos/([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)(/.*)", endpoint)
    if (base != GITHUB_API or not match
            or not REPOSITORY.fullmatch(match.group(1))
            or not GITHUB_API_PATH.fullmatch(match.group(2))):
        raise ValueError("Unexpected GitHub API host or path")
    url = base + endpoint
    if params:
        url += "?" + urlencode(params)
    headers = {"Authorization": f"Bearer {token}", "Accept": "application/json",
               "X-GitHub-Api-Version": "2022-11-28"}
    try:
        with urlopen(Request(url, headers=headers), timeout=60) as response:
            return json.load(response)
    except HTTPError as error:
        # Never log credential-bearing URLs, server responses or auth headers.
        raise RuntimeError(f"GitHub API request failed with HTTP {error.code}") from None


def github(endpoint, **kwargs):
    repository = os.environ["GITHUB_REPOSITORY"]
    if not REPOSITORY.fullmatch(repository) or not GITHUB_API_PATH.fullmatch(endpoint):
        raise ValueError("Unexpected GitHub repository or API endpoint")
    return request(GITHUB_API, f"/repos/{repository}{endpoint}",
                   os.environ["GH_TOKEN"], **kwargs)


def outputs(values):
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as stream:
        for key, value in values.items():
            value = str(value)
            if "\n" in value or "\r" in value:
                raise ValueError("Unexpected newline in workflow output")
            stream.write(f"{key}={value}\n")


def require_reviewed_environment():
    environment = github("/environments/sonarcloud-analysis")
    if not any(rule.get("type") == "required_reviewers" and rule.get("reviewers")
               for rule in environment.get("protection_rules", [])):
        raise ValueError("sonarcloud-analysis requires reviewers")


def requested_revision(event):
    """Accept upstream PR CI only for a successful same-repository run."""
    run = event.get("workflow_run")
    if not run:
        return os.environ.get("REQUESTED_PR", ""), None
    if (run["event"] != "pull_request" or run["conclusion"] != "success"
            or run["head_repository"]["full_name"] != os.environ["GITHUB_REPOSITORY"]):
        raise ValueError("Ineligible upstream workflow")
    candidates = github("/pulls", params={"state": "open", "base": "main",
                        "head": f"{run['head_repository']['owner']['login']}:{run['head_branch']}",
                        "per_page": 100})
    if len(candidates) != 1:
        raise ValueError("Expected one open same-repository PR")
    return str(candidates[0]["number"]), run


def pr_revision(number, run, event):
    if not number.isascii() or not number.isdecimal() or int(number) == 0:
        raise ValueError("PR number must contain positive ASCII digits")
    pr_number = int(number)
    pr = github(f"/pulls/{pr_number}")
    if (pr["state"] != "open" or pr["base"]["ref"] != "main"
            or pr["head"]["repo"]["full_name"] != os.environ["GITHUB_REPOSITORY"]):
        raise ValueError("Only open same-repository PRs targeting main are eligible")
    if run and pr["head"]["sha"] != run["head_sha"]:
        raise ValueError("PR moved since upstream CI; wait for its new run")
    if event.get("after") and pr["head"]["sha"] != event["after"]:
        raise ValueError("PR moved since the bootstrap push")
    if pr.get("mergeable") is not True:
        raise ValueError("PR mergeability is not confirmed; retry after GitHub computes it")
    return {"sha": pr.get("merge_commit_sha", ""), "ref": f"refs/pull/{pr_number}/merge",
            "pr": pr_number, "branch": pr["head"]["ref"], "base": "main",
            "head": pr["head"]["sha"]}


def resolve():
    require_reviewed_environment()
    event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text(encoding="utf-8"))
    number, run = requested_revision(event)
    if number:
        values = pr_revision(number, run, event)
    else:
        sha = event.get("after") or github("/git/ref/heads/main")["object"]["sha"]
        values = {"sha": sha, "ref": "refs/heads/main", "pr": "", "branch": "main",
                  "base": "", "head": sha}
    sha = values["sha"]
    if not SHA.fullmatch(sha):
        raise ValueError("Expected an immutable Git SHA")
    outputs(values)
    summary = f"Selected {values['ref']} at {sha}. Protected approval must cover this revision."
    print(summary)
    with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as stream:
        stream.write(summary + "\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=["resolve"])
    args = parser.parse_args()
    if args.operation == "resolve":
        resolve()


if __name__ == "__main__":
    try:
        main()
    except (ValueError, RuntimeError, KeyError, OSError) as error:
        raise SystemExit(str(error)) from None
