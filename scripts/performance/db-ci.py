#!/usr/bin/env python3
"""Route PERF-05 conservatively and resolve exact-main build artifacts."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

from common import PerformanceContractError, write_json_atomic


def sha(value):
    if not isinstance(value, str) or not re.fullmatch(r"[0-9a-f]{40}", value):
        raise PerformanceContractError("invalid target SHA")
    return value


def relevant(path):
    # Unknown files run the gate. Only established unrelated surfaces can skip.
    if path.startswith(("src/", "tests/Coglatas.Tests/", "scripts/performance/", "performance/",
                        "infra/compose/performance/", ".github/", "scripts/ci/")):
        return True
    if path.startswith(("frontend/", "tests/ui/", "docs/")) or ("/" not in path and path.endswith(".md")):
        return False
    return True


def route(head, base, event, git=subprocess.run):
    head = sha(head)
    actual = git(["git", "rev-parse", "HEAD"], capture_output=True, text=True, check=True).stdout.strip()
    if actual != head:
        raise PerformanceContractError("checkout does not match target SHA")
    result = {"schemaVersion": 1, "headSha": head, "baseSha": None, "event": event,
              "required": True, "reasonCode": "conservative-no-diff", "changedFileCount": None,
              "changedPathsHash": None}
    if event not in ("pull_request", "push") or not base or base == "0" * 40:
        return result
    try:
        sha(base)
        diff = git(["git", "diff", "--name-only", "-z", base + "..." + head], capture_output=True, check=True)
        paths = [p.decode("utf-8", errors="strict") for p in diff.stdout.split(b"\0") if p]
    except (subprocess.CalledProcessError, UnicodeError, PerformanceContractError):
        result["reasonCode"] = "conservative-diff-failure"
        return result
    result.update(baseSha=base, changedFileCount=len(paths), changedPathsHash=hashlib.sha256(diff.stdout).hexdigest())
    result["required"] = not paths or any(relevant(p) for p in paths)
    result["reasonCode"] = "relevant-change" if result["required"] else "validated-not-applicable"
    return result


def choose_build(runs, head):
    sha(head)
    # Select identity before observing completion; never substitute an older run.
    return [r for r in runs if r.get("head_sha") == head and r.get("head_branch") == "main"
            and r.get("event") == "push"
            and r.get("path") == ".github/workflows/main-build-artifacts.yml"]


def resolve_build(repository, head, api=None, *, clock=None, sleep=None, evidence=None):
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository or ""):
        raise PerformanceContractError("invalid repository identity")
    head = sha(head)
    clock = clock or time.monotonic
    sleep = sleep or time.sleep
    # Includes metadata requests and polling; the workflow's five-minute job
    # timeout remains the outer bound (including checkout and route validation).
    deadline = clock() + 5 * 60
    receipt = evidence if evidence is not None else {}
    receipt.update(schemaVersion=1, headSha=head, decision="invalid", producerRunId=None,
                   producerRunAttempt=None, observations=[])

    def remaining():
        seconds = deadline - clock()
        if seconds <= 0:
            raise PerformanceContractError("exact-main producer budget expired")
        return seconds

    if api is None:
        token = os.environ.get("GH_TOKEN")
        if not token:
            raise PerformanceContractError("build metadata token missing")

        def api(path):
            request = urllib.request.Request("https://api.github.com/repos/" + repository + path,
                                            headers={"Authorization": "Bearer " + token,
                                                     "Accept": "application/vnd.github+json",
                                                     "X-GitHub-Api-Version": "2022-11-28"})
            with urllib.request.urlopen(request, timeout=min(30, remaining())) as response:
                return json.load(response)

    def fetch(path, key):
        remaining()
        payload = api(path)
        remaining()
        items = payload.get(key)
        # An incomplete API page must not hide a newer producer or duplicate.
        if (not isinstance(items, list) or any(not isinstance(item, dict) for item in items)
                or type(payload.get("total_count", len(items))) is not int
                or payload.get("total_count", len(items)) != len(items)):
            raise PerformanceContractError("incomplete producer metadata")
        return items

    query = urllib.parse.urlencode({"branch": "main", "head_sha": head, "event": "push", "per_page": 100})
    selected = None
    while True:
        runs = fetch("/actions/workflows/main-build-artifacts.yml/runs?" + query, "workflow_runs")
        matches = choose_build(runs, head)
        if not matches or any(type(r.get("id")) is not int or r["id"] <= 0 for r in matches):
            raise PerformanceContractError("exact-main producer missing or invalid")
        run = max(matches, key=lambda r: r["id"])
        identity = (run["id"], run.get("run_attempt"))
        if type(identity[1]) is not int or identity[1] <= 0 or selected not in (None, identity):
            raise PerformanceContractError("exact-main producer identity changed")
        selected = identity
        receipt.update(producerRunId=identity[0], producerRunAttempt=identity[1])
        status, conclusion = run.get("status"), run.get("conclusion")
        receipt["observations"].append({"status": status, "conclusion": conclusion})
        if not ((status in ("queued", "in_progress") and conclusion is None)
                or (status == "completed" and conclusion in ("success", "failure"))):
            raise PerformanceContractError("exact-main producer cancelled or invalid")
        jobs = fetch(f"/actions/runs/{identity[0]}/attempts/{identity[1]}/jobs?per_page=100", "jobs")
        assemblers = [j for j in jobs if j.get("name") == "Main runtime artifact assembler"]
        if len(assemblers) > 1:
            raise PerformanceContractError("ambiguous runtime assembler")
        assembler = assemblers[0] if assemblers else None
        if assembler and assembler.get("status") == "completed":
            if assembler.get("conclusion") != "success":
                raise PerformanceContractError("runtime assembler did not succeed")
            artifacts = fetch(f"/actions/runs/{identity[0]}/artifacts?name=main-build-artifacts&per_page=100", "artifacts")
            matches = [a for a in artifacts if a.get("name") == "main-build-artifacts"]
            if len(matches) != 1:
                raise PerformanceContractError("exact-main artifact missing or ambiguous")
            artifact = matches[0]
            producer = artifact.get("workflow_run", {})
            if (artifact.get("expired") is not False or producer.get("head_sha") != head
                    or producer.get("id") != identity[0] or type(artifact.get("id")) is not int
                    or artifact["id"] <= 0 or type(artifact.get("size_in_bytes")) is not int
                    or artifact["size_in_bytes"] <= 0
                    or not isinstance(artifact.get("digest"), str)
                    or not re.fullmatch(r"sha256:[0-9a-f]{64}", artifact["digest"])):
                raise PerformanceContractError("invalid exact-main artifact")
            # Recheck after artifact lookup so replacement/rerun cannot race admission.
            final_runs = choose_build(fetch("/actions/workflows/main-build-artifacts.yml/runs?" + query,
                                           "workflow_runs"), head)
            if not final_runs or any(type(r.get("id")) is not int or r["id"] <= 0 for r in final_runs):
                raise PerformanceContractError("exact-main producer disappeared")
            final = max(final_runs, key=lambda r: r["id"])
            if (type(final.get("run_attempt")) is not int
                    or (final["id"], final.get("run_attempt")) != identity
                    or not ((final.get("status") in ("queued", "in_progress") and final.get("conclusion") is None)
                            or (final.get("status") == "completed" and final.get("conclusion") in ("success", "failure")))):
                raise PerformanceContractError("exact-main producer changed during artifact validation")
            remaining()
            receipt.update(decision="resolved", artifactId=artifact["id"], artifactDigest=artifact["digest"])
            return str(identity[0])
        if status == "completed":
            raise PerformanceContractError("completed producer has no successful assembler")
        if assembler and not (assembler.get("status") in ("queued", "in_progress", "waiting", "pending")
                              and assembler.get("conclusion") is None):
            raise PerformanceContractError("invalid active assembler")
        # Only metadata is polled. No test, measurement or failed attempt is retried.
        sleep(min(1, remaining()))


def gate_decision(routing, collector_result, measurement, expected):
    expected = sha(expected)
    if routing.get("schemaVersion") != 1 or routing.get("headSha") != expected or type(routing.get("required")) is not bool:
        raise PerformanceContractError("invalid route evidence")
    if not routing["required"]:
        if routing.get("reasonCode") != "validated-not-applicable" or collector_result != "skipped":
            raise PerformanceContractError("invalid not-applicable lane")
        return "not-applicable"
    if collector_result != "success" or not isinstance(measurement, dict) or measurement.get("headSha") != expected:
        raise PerformanceContractError("missing or incompatible collector evidence")
    if measurement.get("schemaVersion") != 1 or measurement.get("decision") not in ("pass", "regression", "invalid", "unstable", "insufficient-data"):
        raise PerformanceContractError("required DB gate failed")
    return measurement["decision"]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("operation", choices=("route", "resolve-build", "gate"))
    parser.add_argument("--head", required=True)
    parser.add_argument("--base")
    parser.add_argument("--event", default="workflow_dispatch")
    parser.add_argument("--repository")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--route", type=Path)
    parser.add_argument("--measurement", type=Path)
    parser.add_argument("--collector-result")
    args = parser.parse_args()
    producer_evidence = {}
    try:
        if args.operation == "resolve-build":
            run_id = resolve_build(args.repository, args.head, evidence=producer_evidence)
            if args.output:
                write_json_atomic(args.output, producer_evidence)
            print(run_id)
        elif args.operation == "route":
            result = route(args.head, args.base, args.event)
            write_json_atomic(args.output, result)
            print(str(result["required"]).lower())
        else:
            routing = json.loads(args.route.read_text(encoding="utf-8"))
            measurement = None if not args.measurement or not args.measurement.exists() else json.loads(args.measurement.read_text(encoding="utf-8"))
            decision = gate_decision(routing, args.collector_result, measurement, args.head)
            output = {"schemaVersion": 1, "headSha": sha(args.head), "decision": decision,
                      "route": routing, "workflowRunId": os.environ.get("GITHUB_RUN_ID"),
                      "workflowRunAttempt": os.environ.get("GITHUB_RUN_ATTEMPT")}
            if measurement:
                for key in ("contractHash", "environmentFingerprintHashes", "fixtureHashes", "results", "durationDecision"):
                    if key in measurement:
                        output[key] = measurement[key]
            write_json_atomic(args.output, output)
            print(decision)
            return 0 if decision in ("pass", "not-applicable") else 1
        return 0
    except (PerformanceContractError, OSError, ValueError, KeyError, TypeError, subprocess.CalledProcessError,
            urllib.error.URLError):
        if args.operation == "resolve-build" and args.output:
            producer_evidence.update(schemaVersion=1, headSha=args.head, decision="invalid",
                                     reasonCode="producer-resolution-failed")
            write_json_atomic(args.output, producer_evidence)
        if args.operation == "gate" and args.output:
            write_json_atomic(args.output, {"schemaVersion": 1, "headSha": args.head,
                                          "decision": "invalid", "reasonCode": "required-evidence-failed"})
        # Token, HTTP responses, command output and protected URLs never enter logs.
        print("PERF-05 CI failed: invalid routing, target, producer, or gate evidence", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
