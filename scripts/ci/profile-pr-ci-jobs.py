#!/usr/bin/env python3
"""Fail-closed, offline profiling of a complete native PR CI job inventory.

This is measurement, not scanner/test coverage attestation or a speedup claim.
GitHub queue/runner allocation time is excluded from every job duration.
"""
from __future__ import annotations

import argparse
from datetime import datetime
import json
import os
from pathlib import Path
import re
import stat
import sys

SHA = re.compile(r"[0-9a-f]{40}\Z")
JOBS = {
    "CI preflight + route": "preflight",
    ".NET build producer": "dotnetProducer",
    "Frontend build producer": "frontendProducer",
    "build-test": "backend",
    "frontend-test": "frontend",
    "security-scan": "security",
    "functional-fast": "functional",
}
OWNING_STEPS = {
    "security-scan": "Run isolated security runtime lanes in parallel",
    "frontend-test": "Run frontend checks in parallel",
    "build-test": "Test",
}
SECURITY_STEPS = {
    "Run isolated security runtime lanes in parallel": "runtime",
    "Generate and verify SEC-01 / AV-MIG contract": "contract",
    "Download redistributed .NET build for security contract": "dotnetDownload",
    "Build reusable security runtime image": "imageBuild",
}
MAX_JOB_COUNT = 100
MAX_JOB_DURATION_SECONDS = 4 * 60 * 60


class IncompleteProfile(ValueError):
    """A run cannot be described as source-bound, complete native timing."""


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise IncompleteProfile(reason)


def is_positive_int(value: object) -> bool:
    return type(value) is int and 0 < value < 10**18


def unique_pairs(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "DUPLICATE_JSON_FIELD")
        result[key] = value
    return result


def refuse_constant(value):
    raise IncompleteProfile("NONFINITE_JSON")


def read_json(path: Path, maximum_bytes: int):
    flags = os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_NONBLOCK", 0)
    fd = os.open(path, flags)
    try:
        metadata = os.fstat(fd)
        require(stat.S_ISREG(metadata.st_mode) and metadata.st_nlink == 1
                and 0 < metadata.st_size <= maximum_bytes, "INVALID_JSON_FILE")
        chunks = []
        size = 0
        while True:
            part = os.read(fd, min(65536, maximum_bytes + 1 - size))
            if not part:
                break
            size += len(part)
            require(size <= maximum_bytes, "JSON_TOO_LARGE")
            chunks.append(part)
        require(size == os.fstat(fd).st_size, "JSON_CHANGED_DURING_READ")
        return json.loads(b"".join(chunks).decode("utf-8"),
                          object_pairs_hook=unique_pairs,
                          parse_constant=refuse_constant)
    finally:
        os.close(fd)


def parse_utc(value):
    require(type(value) is str and value.endswith("Z"), "INVALID_UTC_TIMESTAMP")
    try:
        timestamp = datetime.fromisoformat(value[:-1] + "+00:00")
    except ValueError as error:
        raise IncompleteProfile("INVALID_UTC_TIMESTAMP") from error
    return timestamp


def seconds(start, end):
    value = (parse_utc(end) - parse_utc(start)).total_seconds()
    require(0 <= value <= MAX_JOB_DURATION_SECONDS and value.is_integer(),
            "INVALID_JOB_DURATION")
    return int(value)


def owned_step_seconds(job, target):
    steps = job.get("steps")
    require(type(steps) is list, "MISSING_NATIVE_STEP_INVENTORY")
    matches = [step for step in steps
               if type(step) is dict and step.get("name") == target]
    require(len(matches) == 1 and matches[0].get("conclusion") == "success"
            and matches[0].get("status") == "completed", "NATIVE_STEP_INCOMPLETE")
    return seconds(matches[0].get("started_at"), matches[0].get("completed_at"))


def profile(run, inventory, *, expected_sha):
    """Measure only one complete, successful, source-matched PR CI attempt."""
    require(type(expected_sha) is str and SHA.fullmatch(expected_sha) is not None,
            "EXPECTED_SHA")
    require(type(run) is dict and type(inventory) is dict, "INPUT_SCHEMA")
    run_id, attempt = run.get("id"), run.get("run_attempt")
    require(is_positive_int(run_id) and is_positive_int(attempt), "RUN_IDENTITY")
    require(run.get("name") == "PR CI"
            and run.get("path") == ".github/workflows/ci.yml"
            and run.get("event") == "pull_request"
            and run.get("status") == "completed"
            and run.get("conclusion") == "success", "RUN_NOT_COMPARABLE")
    require(run.get("head_sha") == expected_sha, "RUN_SOURCE_MISMATCH")
    if run.get("head_commit") is not None:
        require(type(run["head_commit"]) is dict
                and run["head_commit"].get("id", run["head_commit"].get("sha"))
                    == expected_sha, "RUN_COMMIT_MISMATCH")
    rows = inventory.get("jobs")
    require(type(rows) is list and 0 < len(rows) <= MAX_JOB_COUNT
            and type(inventory.get("total_count")) is int
            and inventory["total_count"] == len(rows),
            "MISSING_OR_PAGINATED_JOBS")
    ids, names, elapsed, selected = set(), set(), [], {}
    for job in rows:
        require(type(job) is dict, "JOB_SCHEMA")
        job_id, name = job.get("id"), job.get("name")
        require(is_positive_int(job_id) and job_id not in ids
                and type(name) is str and 0 < len(name) <= 200
                and name not in names, "DUPLICATE_OR_INVALID_JOB")
        ids.add(job_id)
        names.add(name)
        require(job.get("run_id") == run_id
                and type(job.get("run_id")) is int
                and job.get("run_attempt") == attempt
                and type(job.get("run_attempt")) is int
                and job.get("head_sha") == expected_sha,
                "FOREIGN_JOB_OR_ATTEMPT")
        require(job.get("status") == "completed"
                and job.get("conclusion") in ("success", "skipped"),
                "INCOMPLETE_OR_FAILED_JOB")
        if job["conclusion"] == "skipped":
            continue
        duration = seconds(job.get("started_at"), job.get("completed_at"))
        elapsed.append(duration)
        if name in JOBS:
            selected[name] = (job, duration)
    require(set(selected) == set(JOBS), "MISSING_REQUIRED_NATIVE_JOB")
    for job, _ in selected.values():
        require(job.get("conclusion") == "success", "NATIVE_JOB_NOT_SUCCESS")
    detailed = {}
    for job_name, stage in OWNING_STEPS.items():
        detailed[stage] = owned_step_seconds(selected[job_name][0], stage)
    security_steps = {}
    security_job = selected["security-scan"][0]
    security_stage_names = {
        step.get("name") for step in security_job.get("steps", [])
        if type(step) is dict
    }
    for name, key in SECURITY_STEPS.items():
        if name in security_stage_names:
            security_steps[key] = owned_step_seconds(security_job, name)
    require("runtime" in security_steps, "SECURITY_RUNTIME_NOT_EXECUTED")
    duration_by_key = {label: selected[name][1] for name, label in JOBS.items()}
    security_path = (duration_by_key["preflight"] +
                     duration_by_key["dotnetProducer"] +
                     duration_by_key["security"])
    frontend_path = (duration_by_key["preflight"] +
                     duration_by_key["frontendProducer"] +
                     duration_by_key["frontend"])
    return {
        "schema": "coglatas.pr-ci-queue-excluded-profile.v1",
        "sourceSha": expected_sha, "runId": run_id, "attempt": attempt,
        "jobCount": len(rows), "totalRunnerSeconds": sum(elapsed),
        "ownedJobsSeconds": duration_by_key,
        "securityStepsSeconds": security_steps,
        "pathDurationSumsSeconds": {
            "security": security_path, "frontend": frontend_path,
        },
        "status": "JOB_TIMING_ONLY",
        "requiredSixCheckAttestation": "NOT_PERFORMED",
        "nativeTestAndScannerParity": "NOT_PERFORMED",
        "speedupAttribution": "NOT_ESTABLISHED",
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run-json", type=Path, required=True)
    parser.add_argument("--jobs-json", type=Path, required=True)
    parser.add_argument("--sha", required=True)
    args = parser.parse_args(argv)
    try:
        report = profile(read_json(args.run_json, 512 * 1024),
                         read_json(args.jobs_json, 8 * 1024 * 1024),
                         expected_sha=args.sha)
        print(json.dumps(report, sort_keys=True, separators=(",", ":")))
        return 0
    except (OSError, ValueError, TypeError, OverflowError, RecursionError,
            UnicodeError) as error:
        code = str(error) if isinstance(error, IncompleteProfile) else "INVALID_INPUT"
        print("Unqualified PR CI profile: " + code, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
