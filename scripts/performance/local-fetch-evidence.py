#!/usr/bin/env python3
"""Fetch an immutable Git tree's bundle as data outside the trusted checkout."""
import base64
import json
import os
import re
import sys
import urllib.request
from pathlib import Path
from local_evidence import require, safe_path

MAX_BYTES = 134217728

def get(path):
    request = urllib.request.Request("https://api.github.com/repos/NYGsatoshi/Coglatas/" + path,
        headers={"Accept": "application/vnd.github+json", "Authorization": "Bearer " + os.environ["GH_TOKEN"],
                 "X-GitHub-Api-Version": "2022-11-28"})
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)

def main():
    require(os.environ["GITHUB_REPOSITORY"] == "NYGsatoshi/Coglatas" and
            os.environ["GITHUB_REF"] == "refs/heads/main", stage="trusted-data-fetch")
    revision = os.environ["EVIDENCE_COMMIT_SHA"]
    require(re.fullmatch(r"[0-9a-f]{40}", revision) is not None, stage="evidence-revision")
    commit = get("git/commits/" + revision)
    require(commit["sha"] == revision and re.fullmatch(r"[0-9a-f]{40}", commit["tree"]["sha"]),
            stage="evidence-commit")
    listing = get("git/trees/" + commit["tree"]["sha"] + "?recursive=1")
    require(listing["truncated"] is False, stage="evidence-tree")
    records = [r for r in listing["tree"] if r["path"].startswith("bundle/") and r["type"] != "tree"]
    require(all(type(r.get("size")) is int and r["size"] >= 0 for r in records), stage="evidence-file-size")
    require(len({r["path"].casefold() for r in records}) == len(records), stage="evidence-file-uniqueness")
    require(0 < len(records) <= 2000 and sum(r.get("size", MAX_BYTES + 1) for r in records) <= MAX_BYTES,
            "EVIDENCE_MISSING", "evidence-inventory")
    require(any(r["path"] == "bundle/manifest.json" for r in records), "EVIDENCE_MISSING", "manifest")
    destination = Path(os.environ["RUNNER_TEMP"]) / "local-performance-evidence"
    destination.mkdir(exist_ok=False)
    for record in records:
        name = record["path"][len("bundle/"):]
        require(record["type"] == "blob" and record["mode"] == "100644" and
                re.fullmatch(r"[0-9a-f]{40}", record["sha"]) is not None and
                Path(name).suffix in (".json", ".sig") and not any(p.startswith(".") for p in name.split("/")),
                stage="data-file-type")
        target = safe_path(destination, name)
        payload = get("git/blobs/" + record["sha"])
        require(payload["encoding"] == "base64" and payload["sha"] == record["sha"], stage="git-blob")
        data = base64.b64decode(payload["content"].replace("\n", ""), validate=True)
        require(len(data) == record["size"], stage="git-blob-size")
        # Authentication uses the signed manifest and every file SHA-256 after fetch.
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
    print('{"stage":"immutable-data-fetched","codeExecuted":false}')
if __name__ == "__main__":
    try:
        main()
    except Exception:
        print('{"decision":"blocking","reasonCode":"EVIDENCE_INVALID","stage":"immutable-data-fetch"}')
        sys.exit(2)
