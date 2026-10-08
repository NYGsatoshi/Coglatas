#!/usr/bin/env python3
"""Non-measuring readiness, capture, identity and Docker-isolation checks."""
import http.cookiejar
import os
import time
import urllib.request
import uuid
from pathlib import Path
from common import validate_fixture_evidence, validate_target, write_json_atomic
from db_probe_import import login, request
from db_gate import validate_capture
from local_support import LocalError, invoke, load_json, now, sha256

def main():
    out = Path(os.environ["COGLATAS_PERFORMANCE_EVIDENCE_DIR"])
    fp = load_json(out / "environment.json")
    fixture = validate_fixture_evidence(load_json(out / "fixture.json"), os.environ["COGLATAS_PERFORMANCE_PROFILE"])
    project = os.environ["COGLATAS_PERFORMANCE_COMPOSE_PROJECT"]
    image_evidence = {}
    for service in ("app", "postgres"):
        cid = invoke(["docker", "ps", "-q", "--filter", "label=com.docker.compose.project=" + project,
                      "--filter", "label=com.docker.compose.service=" + service])
        info = __import__("json").loads(invoke(["docker", "inspect", cid]))[0]
        image = __import__("json").loads(invoke(["docker", "image", "inspect", info["Image"]]))[0]
        image_evidence[service] = {"imageId": image["Id"], "registryDigests": image.get("RepoDigests", [])}
        if info["State"]["Status"] != "running":
            raise LocalError("CONTAINER_NOT_RUNNING", "docker-readiness")
        networks = info["NetworkSettings"]["Networks"]
        if set(networks) != {project + "_default"}:
            raise LocalError("NETWORK_NOT_ISOLATED", "docker-isolation")
        for mount in info["Mounts"]:
            if mount["Type"] == "volume" and not mount["Name"].startswith(project + "_"):
                raise LocalError("VOLUME_NOT_ISOLATED", "docker-isolation")
    capture_checked = False
    if os.environ.get("COGLATAS_PERFORMANCE_DB_CAPTURE_ENABLED") == "true":
        base = validate_target(os.environ["COGLATAS_PERFORMANCE_BASE_URL"])
        opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
        headers = login(opener, base, fixture)
        capture_id = uuid.uuid4().hex
        status, _ = request(opener, base, "/api/announcements?page=1&pageSize=5",
                            headers | {"X-Performance-Capture": capture_id})
        path = Path(os.environ["COGLATAS_PERFORMANCE_DB_EVIDENCE_PATH"]) / (capture_id + ".json")
        deadline = time.monotonic() + 5
        while not path.exists() and time.monotonic() < deadline:
            time.sleep(.01)
        try:
            captured = validate_capture(load_json(path))
            if status != 200 or captured["status"] != 200:
                raise LocalError("QUERY_CAPTURE_INVALID", "query-capture-preflight")
        finally:
            path.unlink(missing_ok=True)
        capture_checked = True
    write_json_atomic(out / "runtime-preflight.json", {
        "schemaVersion": 1, "phase": "runtime-preflight", "capturedAtUtc": now(),
        "sourceSha": fp["commitSha"], "fixtureHash": fixture["fixtureHash"],
        "readiness": True, "authentication": True, "volumeIsolation": True, "networkIsolation": True,
        "queryCaptureVerified": capture_checked, "measuredSamples": 0, "imageDigests": image_evidence,
        "fingerprintSha256": sha256(out / "environment.json")})
if __name__ == "__main__":
    main()
