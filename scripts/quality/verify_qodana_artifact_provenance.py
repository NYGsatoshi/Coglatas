#!/usr/bin/env python3
"""Verify Qodana GitHub run/attempt and artifact integrity (advisory only).

Queries GitHub's fixed repository API at runtime. User-controlled JSON sidecars
are never accepted as authentication evidence. Invariant test fixtures may
inject a mock API client; live use always fetches GitHub metadata anew.
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import io
import json
import os
import re
import sys
import tempfile
import urllib.error
import urllib.request
import zipfile
from pathlib import Path
from typing import Any, Callable

from qodana_sarif_parity import ParityError, REPOSITORY, compare

REPO_ID, WORKFLOW_ID = 1261244608, 373879708
# Derive the approved API address from the already-reviewed public GitHub URL.
BASE_URL = "https://api.github.com/repos/" + REPOSITORY.removeprefix("https://github.com/").removesuffix(".git")
MAX_ZIP_BYTES = 15 * 1024 * 1024
MAX_SARIF_BYTES = 20 * 1024 * 1024
MAX_ENTRIES = 100
EXPECTED_NAMES = {
    "community": ("qodana-full-inventory", "Qodana Community from main artifacts / Qodana Community / .NET"),
    "cloud": ("qodana-cloud-full-inventory", "Qodana Cloud from main artifacts"),
}


def fail(reason: str) -> None:
    raise ParityError(reason)


def _number(value: Any, name: str) -> int:
    if type(value) is not int or value <= 0 or value > 2**63 - 1:
        fail(f"Invalid {name}")
    return value


def _timestamp(value: Any) -> dt.datetime:
    if not isinstance(value, str):
        fail("Missing timestamp")
    try:
        parsed = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as exc:
        raise ParityError("Malformed timestamp") from exc
    if parsed.tzinfo is None:
        fail("Missing timezone")
    return parsed.astimezone(dt.timezone.utc)


def _parse_json(payload: bytes) -> Any:
    if len(payload) > 2 * 1024 * 1024:
        fail("Oversized GitHub API result")
    def no_duplicate(fields: list[tuple[str, Any]]) -> dict[str, Any]:
        out = {}
        for key, val in fields:
            if key in out:
                fail("Duplicate API field")
            out[key] = val
        return out
    def no_constant(_value: str) -> None:
        fail("Nonfinite API value")
    try:
        return json.loads(payload.decode("utf-8"), object_pairs_hook=no_duplicate, parse_constant=no_constant)
    except (ValueError, UnicodeError, RecursionError) as exc:
        raise ParityError("Malformed GitHub API result") from exc


def live_get(path: str) -> Any:
    # Only caller-built REST paths in the fixed repository are allowed.
    if not re.fullmatch(r"/actions/runs/[1-9][0-9]{0,18}(?:/(?:jobs|artifacts)\?per_page=100&page=[1-5])?", path):
        fail("Invalid GitHub API path")
    token = os.environ.get("GITHUB_TOKEN", "")
    if not token:
        fail("Missing read-only GitHub Actions token")
    request = urllib.request.Request(
        BASE_URL + path,
        headers={"Authorization": "Bearer " + token, "Accept": "application/vnd.github+json",
                 "X-GitHub-Api-Version": "2022-11-28", "User-Agent": "Coglatas-Qodana-Provenance/1"},
    )
    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            if response.status != 200:
                fail("GitHub metadata request failed")
            payload = response.read(2 * 1024 * 1024 + 1)
    except (OSError, urllib.error.URLError, ValueError) as exc:
        raise ParityError("GitHub metadata unavailable") from exc
    return _parse_json(payload)


def collect(run_id: int, resource: str, get: Callable[[str], Any]) -> list[dict[str, Any]]:
    if resource not in ("jobs", "artifacts"):
        fail("Unsupported resource")
    found: list[dict[str, Any]] = []
    seen = set()
    for page in range(1, 6):
        obj = get(f"/actions/runs/{run_id}/{resource}?per_page=100&page={page}")
        if not isinstance(obj, dict) or type(obj.get("total_count")) is not int:
            fail("Malformed paginated response")
        total = obj["total_count"]
        if total < 0 or total > 500:
            fail("Too many workflow entries")
        items = obj.get(resource)
        if not isinstance(items, list) or any(not isinstance(i, dict) for i in items):
            fail("Malformed workflow entries")
        for item in items:
            iid = _number(item.get("id"), "entry id")
            if iid in seen:
                fail("Duplicate workflow entry")
            seen.add(iid)
            found.append(item)
        if len(found) == total:
            return found
        if len(found) > total or not items:
            fail("Incomplete or inconsistent workflow pagination")
    fail("Workflow metadata pagination limit exceeded")


def _read_zip(path: Path, artifact: dict[str, Any]) -> bytes:
    if not isinstance(path, Path) or path.is_symlink() or not path.is_file():
        fail("Missing or symlinked artifact ZIP")
    with path.open("rb") as handle:
        archive = handle.read(MAX_ZIP_BYTES + 1)
    if not archive or len(archive) > MAX_ZIP_BYTES:
        fail("Empty or oversized artifact ZIP")
    if artifact.get("size_in_bytes") != len(archive):
        fail("Artifact size mismatch")
    expected_digest = artifact.get("digest")
    if not isinstance(expected_digest, str) or not re.fullmatch(r"sha256:[a-f0-9]{64}", expected_digest):
        fail("Invalid artifact digest")
    if hashlib.sha256(archive).hexdigest() != expected_digest.removeprefix("sha256:"):
        fail("Archive content digest mismatch")
    try:
        with zipfile.ZipFile(io.BytesIO(archive)) as z:
            info = z.infolist()
            if not 1 <= len(info) <= MAX_ENTRIES:
                fail("Unsafe ZIP entry count")
            names = [i.filename for i in info]
            if len(names) != len(set(names)):
                fail("Duplicate archive entry")
            for member in info:
                name = member.filename
                if (name.startswith("/") or "\\" in name or not name or
                    any(part in ("", ".", "..") for part in name.rstrip("/").split("/")) or
                    (member.external_attr >> 16) & 0o170000 == 0o120000 or
                    member.file_size > MAX_SARIF_BYTES):
                    fail("Unsafe archive entry")
            if names.count("qodana.sarif.json") != 1:
                fail("Missing canonical Qodana SARIF")
            sarif = z.read("qodana.sarif.json")
            if not 0 < len(sarif) <= MAX_SARIF_BYTES:
                fail("Empty or oversized embedded SARIF")
            return sarif
    except (zipfile.BadZipFile, RuntimeError, OSError, ValueError, EOFError) as exc:
        raise ParityError("Invalid artifact ZIP") from exc


def _find_one(items: list[dict[str, Any]], field: str, value: Any, label: str) -> dict[str, Any]:
    matched = [item for item in items if item.get(field) == value]
    if len(matched) != 1:
        fail(f"Expected unique {label}")
    return matched[0]


def verify(
    run_id: int, attempt: int, sha: str, branch: str, artifact_ids: dict[str, int],
    archive_paths: dict[str, Path], get: Callable[[str], Any] = live_get,
) -> dict[str, Any]:
    run_id = _number(run_id, "run id")
    attempt = _number(attempt, "run attempt")
    if not re.fullmatch(r"[a-f0-9]{40}", sha) or branch != "main":
        fail("Invalid qualified Main revision")
    if set(artifact_ids) != set(EXPECTED_NAMES) or set(archive_paths) != set(EXPECTED_NAMES):
        fail("Missing expected Qodana artifact pair")
    if artifact_ids["community"] == artifact_ids["cloud"]:
        fail("One artifact cannot represent both scans")
    if archive_paths["community"].resolve() == archive_paths["cloud"].resolve():
        fail("Same archive input")

    run = get(f"/actions/runs/{run_id}")
    if not isinstance(run, dict) or (
        run.get("id") != run_id or run.get("run_attempt") != attempt or
        run.get("workflow_id") != WORKFLOW_ID or run.get("head_sha") != sha or
        run.get("head_branch") != branch or run.get("event") != "push" or
        run.get("status") != "completed" or run.get("repository", {}).get("id") != REPO_ID
    ):
        fail("Wrong GitHub Actions workflow run or attempt")
    # The whole workflow may fail because of unrelated integration lanes;
    # both individual analyzer jobs must nevertheless succeed.
    jobs = collect(run_id, "jobs", get)
    artifacts = collect(run_id, "artifacts", get)
    sarif: dict[str, bytes] = {}
    job_ids: dict[str, int] = {}
    archive_digests: dict[str, str] = {}
    for role, (artifact_name, job_name) in EXPECTED_NAMES.items():
        job = _find_one(jobs, "name", job_name, role + " producer job")
        if (job.get("run_id") != run_id or job.get("run_attempt") != attempt or
            job.get("head_sha") != sha or job.get("workflow_name") != "Main CI" or
            job.get("status") != "completed" or job.get("conclusion") != "success"):
            fail("Qodana job did not complete on the expected attempt")
        steps = job.get("steps")
        if not isinstance(steps, list) or not any(s.get("name") == "Upload Qodana inspection inventory" and
            s.get("conclusion") == "success" for s in steps if isinstance(s, dict)):
            fail("Qodana upload step not successful")
        artifact = _find_one(artifacts, "id", _number(artifact_ids[role], "artifact id"), role + " artifact")
        metadata = artifact.get("workflow_run")
        if (artifact.get("name") != artifact_name or artifact.get("expired") is not False or
            not isinstance(metadata, dict) or metadata.get("id") != run_id or
            metadata.get("repository_id") != REPO_ID or
            metadata.get("head_repository_id") != REPO_ID or
            metadata.get("head_branch") != branch or metadata.get("head_sha") != sha):
            fail("Artifact not bound to the qualified workflow run")
        start = _timestamp(job.get("started_at"))
        end = _timestamp(job.get("completed_at"))
        created = _timestamp(artifact.get("created_at"))
        if start > end or not start <= created <= end + dt.timedelta(seconds=60):
            fail("Artifact creation outside matching attempt's job window")
        archive_digests[role] = artifact["digest"]
        sarif[role] = _read_zip(archive_paths[role], artifact)
        job_ids[role] = job["id"]

    with tempfile.TemporaryDirectory(prefix="qodana-parity-") as temp:
        paths = {role: Path(temp) / f"{role}.sarif.json" for role in EXPECTED_NAMES}
        for role in paths:
            paths[role].write_bytes(sarif[role])
        receipt = compare(paths["community"], paths["cloud"], sha, str(run_id), branch)
    receipt.update({
        "status": "ATTEMPT_CORRELATED_NOT_PUBLISHER_ATTESTED",
        "runAttempt": attempt,
        "runAttemptVerified": True,
        "artifactMetadataSource": "live-github-api",
        "artifactDigestVerified": True,
        "artifactCreationWindowsMatched": True,
        "producerJobIds": job_ids,
        "artifactIds": artifact_ids,
        "archiveDigests": archive_digests,
        "producerCryptographicallyAttested": False,
        "cloudReplayVerified": False,
        "qualifiedForGateReuse": False,
    })
    return receipt


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description="Read-only Main Qodana attempt/artifact correlation")
    p.add_argument("--run-id", required=True, type=int)
    p.add_argument("--run-attempt", required=True, type=int)
    p.add_argument("--source-sha", required=True)
    p.add_argument("--community-artifact-id", required=True, type=int)
    p.add_argument("--cloud-artifact-id", required=True, type=int)
    p.add_argument("--community-zip", required=True, type=Path)
    p.add_argument("--cloud-zip", required=True, type=Path)
    p.add_argument("--output", required=True, type=Path)
    args = p.parse_args(argv)
    try:
        if args.output.exists() or args.output.is_symlink():
            fail("Refusing overwrite of prior evidence")
        receipt = verify(args.run_id, args.run_attempt, args.source_sha, "main", {
            "community": args.community_artifact_id, "cloud": args.cloud_artifact_id,
        }, {"community": args.community_zip, "cloud": args.cloud_zip})
        args.output.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.NamedTemporaryFile("w", encoding="utf-8", dir=args.output.parent, prefix=".qodana-receipt-", delete=False) as f:
            intermediate = Path(f.name)
            try:
                json.dump(receipt, f, sort_keys=True, indent=2)
                f.write("\n")
            except BaseException:
                intermediate.unlink(missing_ok=True)
                raise
        intermediate.replace(args.output)
        print("Qodana artifact digests and attempt window CORRELATED; Cloud publisher NOT attested.")
        return 0
    except (ParityError, OSError, ValueError, TypeError, OverflowError, RecursionError):
        print("Qodana artifact provenance REJECTED (fail closed).", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
