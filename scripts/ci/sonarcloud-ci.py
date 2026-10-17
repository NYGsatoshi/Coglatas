#!/usr/bin/env python3
"""Resolve reviewed revisions and bridge SonarCloud issues to GitHub SARIF."""

from __future__ import annotations

import argparse
import base64
import gzip
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import time
from urllib.error import HTTPError
from urllib.parse import quote, urlencode
from urllib.request import Request, urlopen

SONAR = "https://sonarcloud.io"
GITHUB_API = "https://api.github.com"
SHA = re.compile(r"[0-9a-f]{40}\Z")
REPOSITORY = re.compile(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\Z")
GITHUB_API_PATH = re.compile(
    r"/(?:environments/sonarcloud-analysis|pulls(?:/[1-9][0-9]*)?|"
    r"git/ref/heads/main|code-scanning/sarifs(?:/[A-Za-z0-9_-]+)?)\Z"
)
SONAR_API_PATHS = {
    "/api/ce/task", "/api/issues/search", "/api/qualitygates/project_status",
}
TASK_ID = re.compile(r"[A-Za-z0-9_-]{1,160}\Z")


def request(base, endpoint, token, params=None, payload=None):
    """Only reach the expected service and vetted API endpoints."""
    if base == GITHUB_API:
        # The repository component is validated by github(); the suffix is a
        # fixed endpoint or a validated numeric PR / API-generated SARIF ID.
        match = re.fullmatch(r"/repos/([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)(/.*)", endpoint)
        if not match or not GITHUB_API_PATH.fullmatch(match.group(2)):
            raise ValueError("Unexpected GitHub API path")
    elif base != SONAR or endpoint not in SONAR_API_PATHS:
        raise ValueError("Unexpected API host or path")
    url = base + endpoint
    if params:
        url += "?" + urlencode(params)
    headers = {"Authorization": f"Bearer {token}", "Accept": "application/json"}
    data = None if payload is None else json.dumps(payload).encode()
    if data is not None:
        headers["Content-Type"] = "application/json"
    if base == GITHUB_API:
        headers["X-GitHub-Api-Version"] = "2022-11-28"
    try:
        with urlopen(Request(url, data=data, headers=headers), timeout=60) as response:
            return json.load(response)
    except HTTPError as error:
        # Do not print server responses, headers or credential-bearing requests.
        raise RuntimeError(f"Service request failed with HTTP {error.code}") from None


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


def to_sarif(issues, components, project):
    paths = {item["key"]: item.get("path") for item in components}
    rules = sorted({issue["rule"] for issue in issues})
    indices = {rule: position for position, rule in enumerate(rules)}
    results = []
    for issue in issues:
        result = {"ruleId": issue["rule"], "ruleIndex": indices[issue["rule"]],
                  "message": {"text": issue["message"]},
                  "level": {"BLOCKER": "error", "CRITICAL": "error", "MAJOR": "warning",
                            "MINOR": "note", "INFO": "note"}.get(issue.get("severity"), "warning"),
                  "partialFingerprints": {"sonarIssueKey/v1": hashlib.sha256(issue["key"].encode()).hexdigest()},
                  "properties": {"sonarIssueType": issue.get("type", "UNKNOWN")}}
        path = paths.get(issue.get("component"))
        if path:
            if ("\\" in path or ":" in path or path.startswith("/")
                    or ".." in PurePosixPath(path).parts):
                raise ValueError("Sonar returned a path outside the repository")
            location = {"artifactLocation": {"uri": quote(path, safe="/")}}
            line = issue.get("line", issue.get("textRange", {}).get("startLine"))
            if isinstance(line, int) and line > 0:
                location["region"] = {"startLine": line}
            result["locations"] = [{"physicalLocation": location}]
        results.append(result)
    return {"version": "2.1.0", "$schema": "https://json.schemastore.org/sarif-2.1.0.json",
            "runs": [{"tool": {"driver": {"name": "SonarCloud", "informationUri": SONAR,
                       "rules": [{"id": rule, "helpUri": f"{SONAR}/coding_rules?open={quote(rule, safe='')}"}
                                 for rule in rules]}},
                      "automationDetails": {"id": "sonarcloud/"}, "results": results,
                      "properties": {"sonarProjectKey": project}}]}


def sonar_api(token):
    def api(endpoint, **params):
        return request(SONAR, "/api/" + endpoint, token, params=params)
    return api


def wait_for_analysis(api, task_id):
    for _ in range(120):
        task = api("ce/task", id=task_id)["task"]
        if task["status"] == "SUCCESS":
            return task["analysisId"]
        if task["status"] not in {"PENDING", "IN_PROGRESS"}:
            raise ValueError("Sonar server analysis did not succeed")
        time.sleep(5)
    raise TimeoutError("Sonar server analysis exceeded ten minutes")


def unresolved_issues(api, project, pr):
    params = {"componentKeys": project, "resolved": "false", "ps": 500}
    params["pullRequest" if pr else "branch"] = pr or "main"
    issues, components, total = [], [], None
    for page in range(1, 21):
        response = api("issues/search", **params, p=page)
        current_total = response["paging"]["total"]
        if current_total > 10000 or (total is not None and total != current_total):
            raise ValueError("Issue export exceeds API limit or changed during pagination")
        total = current_total
        issues.extend(response["issues"])
        components.extend(response.get("components", []))
        if len(issues) >= total:
            break
        if not response["issues"]:
            raise ValueError("Incomplete issue pagination")
    if len(issues) != total or len({issue["key"] for issue in issues}) != total:
        raise ValueError("Incomplete or duplicate issue export")
    return issues, components


def export(report, output):
    metadata = {key: value for key, value in (
        line.split("=", 1) for line in Path(report).read_text(encoding="utf-8").splitlines()
        if "=" in line)}
    project = os.environ["SONAR_PROJECT_KEY"]
    if metadata.get("serverUrl", "").rstrip("/") != SONAR or metadata.get("projectKey") != project:
        raise ValueError("Unexpected analysis server or project")
    task_id = metadata["ceTaskId"]
    if not TASK_ID.fullmatch(task_id):
        raise ValueError("Invalid Sonar task ID")
    api = sonar_api(os.environ["SONAR_TOKEN"])
    analysis_id = wait_for_analysis(api, task_id)
    issues, components = unresolved_issues(api, project, os.environ.get("PR_NUMBER"))
    gate = api("qualitygates/project_status", analysisId=analysis_id)["projectStatus"]["status"]
    if gate not in {"OK", "ERROR", "WARN", "NONE"}:
        raise ValueError("Unexpected Quality Gate status")
    Path(output).write_text(json.dumps(to_sarif(issues, components, project)), encoding="utf-8")
    outputs({"gate": gate})
    print(f"Exported {len(issues)} unresolved issues; Quality Gate: {gate}.")


def publish(report):
    sha, ref = os.environ["ANALYSIS_SHA"], os.environ["ANALYSIS_REF"]
    if not SHA.fullmatch(sha):
        raise ValueError("Invalid analysis SHA")
    pr = os.environ.get("PR_NUMBER")
    if pr:
        if not pr.isascii() or not pr.isdecimal() or int(pr) == 0:
            raise ValueError("Invalid PR number")
        current = github(f"/pulls/{int(pr)}")
        if (current["state"] != "open" or current["head"]["sha"] != os.environ["PR_HEAD"]
                or current["merge_commit_sha"] != sha or ref != f"refs/pull/{int(pr)}/merge"):
            raise ValueError("PR moved or closed; refusing stale Code Scanning publication")
    elif ref != "refs/heads/main":
        raise ValueError("Unexpected publication ref")
    raw = Path(report).read_bytes()
    sarif = json.loads(raw)
    if sarif.get("version") != "2.1.0" or len(sarif.get("runs", [])) != 1:
        raise ValueError("Unexpected SARIF structure")
    encoded = base64.b64encode(gzip.compress(raw)).decode("ascii")
    result = github("/code-scanning/sarifs", payload={"commit_sha": sha, "ref": ref, "sarif": encoded})
    upload_id = result["id"]
    if not re.fullmatch(r"[A-Za-z0-9_-]+", upload_id):
        raise ValueError("Unexpected SARIF upload ID")
    for _ in range(60):
        status = github(f"/code-scanning/sarifs/{upload_id}")
        if status["processing_status"] == "complete":
            print(f"Code Scanning accepted SonarCloud SARIF for {ref} at {sha}.")
            return
        if status["processing_status"] == "failed":
            raise ValueError("GitHub rejected SARIF processing")
        time.sleep(5)
    raise TimeoutError("GitHub SARIF processing exceeded five minutes")


def workspace_file(relative, *, for_write=False):
    """Restrict runtime file IO to fixed paths below the checked workspace."""
    root = Path(os.environ["GITHUB_WORKSPACE"]).resolve(strict=True)
    rel = PurePosixPath(relative)
    if rel.is_absolute() or ".." in rel.parts:
        raise ValueError("Invalid workspace path")
    target = root.joinpath(*rel.parts)
    parent = target.parent.resolve(strict=True)
    if parent != root and root not in parent.parents:
        raise ValueError("Workspace path escapes root")
    if target.is_symlink() or (target.exists() and root not in target.resolve().parents):
        raise ValueError("Symlink or path outside workspace")
    if not for_write and not target.is_file():
        raise ValueError("Expected a regular workspace file")
    return target


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=["resolve", "export", "publish"])
    args = parser.parse_args()
    if args.operation == "resolve":
        resolve()
    elif args.operation == "export":
        export(workspace_file("source/.sonarqube/out/.sonar/report-task.txt"),
               workspace_file("sonarcloud.sarif", for_write=True))
    else:
        publish(workspace_file("sarif/sonarcloud.sarif"))


if __name__ == "__main__":
    try:
        main()
    except (ValueError, RuntimeError, TimeoutError, KeyError, OSError) as error:
        raise SystemExit(str(error)) from None
