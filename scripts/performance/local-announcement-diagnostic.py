#!/usr/bin/env python3
"""Fresh local Announcement exploration; never an old diagnostic replacement."""
import importlib.util
import os
import re
import time
import http.cookiejar
import subprocess
import urllib.request
from pathlib import Path
from common import write_json_atomic
from local_support import ROOT, invoke, load_json, now, failure
from local_evidence import require

spec = importlib.util.spec_from_file_location("frozen_announcement_helpers", ROOT / "scripts/performance/announcement-diagnostic.py")
observer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(observer)

def main():
    require(os.environ.get("COGLATAS_LOCAL_EVIDENCE_MODE") == "LOCAL_DIAGNOSTIC" and
            not os.environ.get("GITHUB_ACTIONS"), "LOCAL_EXECUTION_REQUIRED", "announcement")
    out = Path(os.environ["COGLATAS_LOCAL_ANNOUNCEMENT_OUTPUT"])
    require(not (out / "prospective-manifest.json").exists(), stage="immutable-diagnostic")
    source = os.environ["COGLATAS_PERFORMANCE_TARGET_SHA"]
    fixture = load_json(Path(os.environ["COGLATAS_PERFORMANCE_FIXTURE_EVIDENCE"]))
    write_json_atomic(out / "prospective-manifest.json", {
        "schemaVersion": 1, "executionProvider": "local", "mode": "LOCAL_DIAGNOSTIC",
        "sourceSha": source, "createdAtUtc": now(), "groups": 5, "requestsPerGroup": 20,
        "retries": 0, "retainAllInOriginalOrder": True, "historicalReplacement": False,
        "acceptanceCredit": False, "baselineCredit": False, "fixtureHash": fixture["fixtureHash"],
        "observerLimitations": ["observer-overhead-present", "process-wide-GC-CPU-counters",
                               "no-query-text", "millisecond-clock-alignment"]})
    project = os.environ["COGLATAS_PERFORMANCE_COMPOSE_PROJECT"]
    ids = {s: invoke(["docker", "ps", "-q", "--filter", "label=com.docker.compose.project=" + project,
                     "--filter", "label=com.docker.compose.service=" + s]) for s in ("app", "postgres")}
    pids = {s: int(invoke(["docker", "inspect", "--format", "{{.State.Pid}}", cid])) for s, cid in ids.items()}
    opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
    base = os.environ["COGLATAS_PERFORMANCE_BASE_URL"]
    headers = observer.login(opener, base, fixture)
    route = "/api/announcements?pageSize=5&page=1"
    status, body = observer.request(opener, base, route, headers)
    require(status == 200 and len(body["items"]) == 5, "PREFLIGHT_FAILED", "announcement-warmup")
    samples = []
    for ordinal in range(1, 101):
        before = {s: observer.container_snapshot(pid) for s, pid in pids.items()}
        host_before, began, stamp = observer.host_snapshot(), time.perf_counter_ns(), now()
        status, count, rejected = None, None, None
        try:
            status, body = observer.request(opener, base, route, headers | {"X-Announcement-Ordinal": str(ordinal)})
            count = len(body["items"]) if isinstance(body, dict) and isinstance(body.get("items"), list) else None
        except Exception as error:
            rejected = failure(error, component="announcement", operation="read", sample_count=len(samples))
        ended = time.perf_counter_ns()
        samples.append({"ordinal": ordinal, "group": (ordinal - 1) // 20 + 1,
            "sampleInGroup": (ordinal - 1) % 20 + 1, "startUtc": stamp,
            "clientStartNs": began, "clientEndNs": ended, "wallTimeMs": (ended - began) / 1000000,
            "status": status, "returnedCount": count, "failure": rejected, "containersBefore": before,
            "containersAfter": {s: observer.container_snapshot(pid) for s, pid in pids.items()},
            "hostBefore": host_before, "hostAfter": observer.host_snapshot()})
        write_json_atomic(out / "client-samples.json", {"samples": samples})
    observer.request(opener, base, "/health/ready", headers | {"X-Announcement-Flush": "complete-100"})
    sidecar = Path(os.environ["COGLATAS_PERFORMANCE_API_DIAGNOSTICS_PATH"]) / "announcement-samples.json"
    deadline = time.monotonic() + 5
    while not sidecar.exists() and time.monotonic() < deadline:
        time.sleep(.05)
    server = observer.load_json(sidecar, list) if sidecar.exists() else []
    if sidecar.exists():
        (out / "server-samples.json").write_bytes(sidecar.read_bytes())
    # Preserve only the original collector's full-line numeric PostgreSQL allowlist.
    raw = subprocess.run(["docker", "logs", ids["postgres"]], capture_output=True, text=True, timeout=60, check=True)
    pattern = re.compile(r"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} UTC) \[(\d+)\] LOG:  duration: ([0-9.]+) ms\s*$")
    durations = []
    for line in (raw.stdout + raw.stderr).splitlines():
        if match := pattern.fullmatch(line):
            durations.append({"ordinal": len(durations) + 1, "endUtc": match[1],
                              "backendPid": int(match[2]), "durationMs": float(match[3])})
    write_json_atomic(out / "postgres-durations.json", {"samples": durations})
    write_json_atomic(out / "diagnostic-completion.json", {
        "attemptedRequests": len(samples), "serverSamples": len(server),
        "postgresDurationRecords": len(durations), "retries": 0,
        "acceptanceCredit": False, "baselineCredit": False, "historicalReplacement": False})
    require(len(server) == 100 and durations and all(s["status"] == 200 and s["returnedCount"] == 5 for s in samples),
            "COLLECTOR_FAILED", "announcement-completeness")
if __name__ == "__main__":
    main()
