"""Resolve one exact-source protected P0 run and retain its original report bytes."""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import sys
import urllib.error
import urllib.parse
import urllib.request
import zipfile

from sec_arch_github_provenance import LiveGitHub
import sec_arch_reconcile as binding

REPOSITORY = "NYGsatoshi/Coglatas"
REPOSITORY_ID = 1261244608
WORKFLOW = ".github/workflows/licensed-real-backend-acceptance.yml"
PREFIX = "repos/" + REPOSITORY + "/"
MAX_ZIP = 16 * 1024 * 1024
MAX_EXPANDED = 128 * 1024 * 1024
FIXED_FILES = {
    "sonar-native-host/source.json", "sonar-native-host/execution.json",
    "sonar-native-host/lcov.info", "sonar-real-backend-native/environment.json",
    "sonar-real-backend-native/browser.json", "sonar-real-backend-native/lcov.info",
}
RAW_FILE = re.compile(r"(?:sonar-native-host|sonar-real-backend-native)/raw/coverage-[0-9]+-[0-9]+-[0-9]+\.json")


class ArtifactError(ValueError):
    """Closed diagnostic: never include API responses, signed URLs or credentials."""


def require(condition: bool, stage: str) -> None:
    if not condition:
        raise ArtifactError(stage)


def document(data: bytes) -> dict:
    require(len(data) <= 32 * 1024, "JSON")
    result = json.loads(data.decode("utf8"), object_pairs_hook=binding.unique_object,
                        parse_constant=binding.reject_constant)
    require(isinstance(result, dict), "JSON")
    return result


def collect(api, path: str, key: str) -> list[dict]:
    rows = []
    separator = "&" if "?" in path else "?"
    for page in range(1, 11):
        response = api(path + separator + "per_page=100&page=" + str(page))
        total, current = response.get("total_count"), response.get(key)
        require(type(total) is int and 0 <= total <= 1000 and isinstance(current, list)
                and all(isinstance(row, dict) for row in current), "LIST")
        rows.extend(current)
        require(len(rows) <= total, "LIST")
        if len(rows) == total:
            identifiers = [row.get("id") for row in rows]
            require(all(type(value) is int and value > 0 for value in identifiers)
                    and len(set(identifiers)) == len(identifiers), "LIST")
            return rows
        require(len(current) == 100, "LIST")
    raise ArtifactError("LIST")


def resolve(api, sha: str, branch: str) -> tuple[dict, dict, dict] | None:
    require(re.fullmatch(r"[a-f0-9]{40}", sha) is not None and bool(branch), "SOURCE")
    workflow = api(PREFIX + "actions/workflows/licensed-real-backend-acceptance.yml")
    require(type(workflow.get("id")) is int and workflow["id"] > 0
            and workflow.get("path") == WORKFLOW and workflow.get("state") == "active", "WORKFLOW")
    runs = collect(api, PREFIX + "actions/workflows/" + str(workflow["id"])
                   + "/runs?event=workflow_dispatch&head_sha=" + sha, "workflow_runs")
    candidates = []
    for run in runs:
        require(run.get("head_sha") == sha and run.get("event") == "workflow_dispatch"
                and run.get("path") == WORKFLOW and run.get("workflow_id") == workflow["id"]
                and run.get("head_branch") == branch
                and run.get("repository", {}).get("id") == REPOSITORY_ID
                and run.get("repository", {}).get("full_name") == REPOSITORY
                and run.get("head_repository", {}).get("id") == REPOSITORY_ID
                and run.get("head_repository", {}).get("full_name") == REPOSITORY, "RUN")
        attempt = run.get("run_attempt")
        require(type(attempt) is int and attempt > 0, "RUN")
        jobs = collect(api, PREFIX + "actions/runs/" + str(run["id"])
                       + "/attempts/" + str(attempt) + "/jobs", "jobs")
        owners = [job for job in jobs if job.get("name") == "licensed-real-backend"]
        require(len(owners) <= 1, "JOB")
        incomplete = run.get("status") != "completed"
        if not owners:
            if incomplete:
                require(run.get("actor", {}).get("login") == REPOSITORY.partition("/")[0], "RUN")
                candidates.append((run, None, None))
            continue
        job = owners[0]
        steps = [] if incomplete and "steps" not in job else job.get("steps")
        require(isinstance(steps, list) and all(isinstance(step, dict) for step in steps), "JOB")
        selected = [step for step in steps if step.get("name") == "Validate closed native coverage selection"]
        require(len(selected) <= 1 and (incomplete or len(selected) == 1), "SELECTION")
        if selected and selected[0].get("conclusion") == "skipped":
            continue
        require(run.get("actor", {}).get("login") == REPOSITORY.partition("/")[0], "RUN")
        require(job.get("run_id") == run["id"] and type(job.get("run_attempt")) is int
                and job["run_attempt"] == attempt and job.get("head_sha") == sha, "EXECUTION")
        # Selection and suite steps can still be pending. Retain this unresolved
        # history so a later success cannot replace it, but import no coverage.
        if incomplete:
            candidates.append((run, job, None))
            continue
        require(selected[0].get("conclusion") == "success", "SELECTION")
        p0 = [step for step in steps if step.get("name") == "Run P0 acceptance"]
        mobile = [step for step in steps if step.get("name") == "Run independent real-backend MVP0 Mobile acceptance"]
        require(len(p0) == 1 and len(mobile) == 1, "SELECTION")
        if p0[0].get("conclusion") == "skipped":
            require(mobile[0].get("conclusion") in {"success", "failure"}, "SELECTION")
            continue
        candidates.append((run, job, p0[0]))
    require(len(candidates) <= 1, "AMBIGUOUS")
    if not candidates:
        return None
    run, job, step = candidates[0]
    if run.get("status") != "completed":
        return None
    require(job.get("status") == "completed", "EXECUTION")
    # Preserve all candidate history above: a failed run plus a later pass must
    # never become a selectively green first-attempt coverage source.
    if (run["run_attempt"] != 1 or run.get("conclusion") != "success"
            or job.get("conclusion") != "success" or step.get("conclusion") != "success"):
        return None
    artifacts = collect(api, PREFIX + "actions/runs/" + str(run["id"]) + "/artifacts", "artifacts")
    selected = [item for item in artifacts if item.get("name") == "sonar-real-backend-p0-native-" + sha]
    require(len(selected) == 1, "ARTIFACT")
    artifact = selected[0]
    require(type(artifact.get("expired")) is bool and type(artifact.get("size_in_bytes")) is int
            and 0 < artifact["size_in_bytes"] <= MAX_ZIP
            and isinstance(artifact.get("digest"), str)
            and re.fullmatch(r"sha256:[a-f0-9]{64}", artifact["digest"]) is not None
            and artifact.get("workflow_run", {}).get("id") == run["id"]
            and artifact.get("workflow_run", {}).get("head_sha") == sha, "ARTIFACT")
    if artifact["expired"]:
        return None
    return run, job, artifact


class StopRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, response, code, message, headers, new_url):
        return None


def download(artifact: dict, token: str) -> bytes:
    require(bool(token), "AUTH")
    opener = urllib.request.build_opener(StopRedirect())
    request = urllib.request.Request("https://api.github.com/" + PREFIX + "actions/artifacts/"
                                     + str(artifact["id"]) + "/zip", headers={
        "Authorization": "Bearer " + token, "Accept": "application/vnd.github+json",
        "User-Agent": "Coglatas-Native-Coverage/1", "X-GitHub-Api-Version": "2022-11-28"})
    try:
        response = opener.open(request, timeout=30)
    except urllib.error.HTTPError as error:
        try:
            require(error.code == 302, "DOWNLOAD")
            location = error.headers.get("Location", "")
            parsed = urllib.parse.urlsplit(location)
            require(parsed.scheme == "https" and parsed.username is None and parsed.password is None
                    and parsed.port in {None, 443} and not parsed.fragment
                    and re.fullmatch(r"[a-z0-9]+\.blob\.core\.windows\.net", parsed.hostname or "") is not None,
                    "REDIRECT")
        finally:
            error.close()
        # A separate request deliberately carries no GitHub authorization header.
        response = opener.open(urllib.request.Request(location, headers={"User-Agent": "Coglatas-Native-Coverage/1"}), timeout=30)
    with response:
        require(response.status == 200, "DOWNLOAD")
        data = response.read(MAX_ZIP + 1)
    require(len(data) == artifact["size_in_bytes"] <= MAX_ZIP
            and "sha256:" + hashlib.sha256(data).hexdigest() == artifact["digest"], "DIGEST")
    return data


def validate_zip(data: bytes, sha: str, tree: str, run_id: int, required: int) -> dict[str, bytes]:
    require(len(data) <= MAX_ZIP and re.fullmatch(r"[a-f0-9]{40}", sha) is not None
            and re.fullmatch(r"[a-f0-9]{40}", tree) is not None
            and type(run_id) is int and run_id > 0 and type(required) is int and 0 < required <= 100, "ARCHIVE")
    files = {}
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        entries = archive.infolist()
        require(0 < len(entries) <= 64 and sum(entry.file_size for entry in entries) <= MAX_EXPANDED, "ARCHIVE")
        for entry in entries:
            name = entry.filename
            path = PurePosixPath(name)
            require(not entry.is_dir() and name == path.as_posix() and not path.is_absolute()
                    and ".." not in path.parts and "\\" not in name and name not in files
                    and stat.S_IFMT(entry.external_attr >> 16) in {0, stat.S_IFREG}
                    and entry.compress_type in {zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED}
                    and not entry.flag_bits & 1
                    and 0 < entry.file_size <= 32 * 1024 * 1024
                    and (name in FIXED_FILES or RAW_FILE.fullmatch(name) is not None), "ARCHIVE")
            files[name] = archive.read(entry)
    require(FIXED_FILES <= files.keys() and all(any(name.startswith(prefix + "/raw/") for name in files)
            for prefix in ("sonar-native-host", "sonar-real-backend-native")), "ARCHIVE")
    source = document(files["sonar-native-host/source.json"])
    require(type(source.get("schemaVersion")) is int and type(source.get("attempt")) is int
            and source.get("nativeCoverage") is True
            and source == {"schemaVersion": 1, "sourceSha": sha, "treeSha": tree, "suite": "p0",
                       "nativeCoverage": True, "runId": str(run_id), "attempt": 1}, "SOURCE")
    execution = document(files["sonar-native-host/execution.json"])
    require(all(type(execution.get(key)) is int for key in ("attempt", "retries", "requiredCases", "junitCases"))
            and execution == {"sourceSha": sha, "treeSha": tree, "runId": str(run_id), "attempt": 1,
                          "retries": 0, "requiredCases": required, "junitCases": required, "status": "PASS"}, "EXECUTION")
    environment = document(files["sonar-real-backend-native/environment.json"])
    require(set(environment) == {"sourceSha", "hostNodeVersion", "postgresVersionNum", "backendImage", "qualification"}
            and environment.get("sourceSha") == sha and type(environment.get("postgresVersionNum")) is int
            and environment["postgresVersionNum"] // 10000 == 18
            and re.fullmatch(r"v24\.\d+\.\d+", environment.get("hostNodeVersion", "")) is not None
            and re.fullmatch(r"sha256:[a-f0-9]{64}", environment.get("backendImage", "")) is not None
            and environment.get("qualification") == "Synthetic Compose database version only; no application-role RLS claim.", "ENVIRONMENT")
    browser = document(files["sonar-real-backend-native/browser.json"])
    require(set(browser) == {"sourceSha", "nodeVersion", "suite"} and browser.get("sourceSha") == sha
            and browser.get("suite") == "p0" and re.fullmatch(r"v24\.\d+\.\d+", browser.get("nodeVersion", "")) is not None,
            "ENVIRONMENT")
    return files


def retain(root: Path, files: dict[str, bytes], receipt: dict) -> None:
    require(all(name in FIXED_FILES or RAW_FILE.fullmatch(name) is not None for name in files), "OUTPUT")
    artifacts = root / "artifacts"
    require(artifacts.is_dir() and not artifacts.is_symlink() and artifacts.resolve() == artifacts, "OUTPUT")
    target = artifacts / "sonar-native-import"
    target.mkdir(mode=0o700)
    for name, data in files.items():
        path = target.joinpath(*PurePosixPath(name).parts)
        path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
        with path.open("xb") as output:
            output.write(data)
        path.chmod(0o600)
    with (target / "provenance.json").open("x", encoding="utf8") as output:
        json.dump(receipt, output, indent=2)
        output.write("\n")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--sha", required=True)
    parser.add_argument("--tree", required=True)
    parser.add_argument("--branch", required=True)
    args = parser.parse_args()
    try:
        token = os.environ.get("GH_TOKEN") or os.environ.get("GITHUB_TOKEN")
        selected = resolve(LiveGitHub(token), args.sha, args.branch)
        if selected is None:
            print("available=false")
            return 0
        run, job, artifact = selected
        root = Path.cwd().resolve()
        manifest = (root / "scripts/ci/real-backend-pr-p0-required-tests.txt").read_text(encoding="utf8")
        required = len([line for line in manifest.splitlines() if line.strip() and not line.lstrip().startswith("#")])
        files = validate_zip(download(artifact, token), args.sha, args.tree, run["id"], required)
        current = resolve(LiveGitHub(token), args.sha, args.branch)
        require(current is not None and current[0]["id"] == run["id"] and current[1]["id"] == job["id"]
                and current[2]["id"] == artifact["id"] and current[2]["digest"] == artifact["digest"], "RECHECK")
        retain(root, files, {"sourceSha": args.sha, "treeSha": args.tree, "runId": run["id"], "jobId": job["id"],
                             "attempt": 1, "artifactId": artifact["id"], "artifactDigest": artifact["digest"]})
        print("available=true")
        return 0
    except (ArtifactError, binding.ReconciliationError, ValueError, OSError, zipfile.BadZipFile, KeyError, TypeError, AttributeError):
        print("Native coverage artifact did not qualify.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
