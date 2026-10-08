#!/usr/bin/env python3
"""Resolve authoritative source identities using trusted Main; never execute PR code."""
import json
import os
import re
import sys
import urllib.request
from pathlib import Path
from local_support import LocalError, invoke
from local_evidence import require

def get(path):
    req = urllib.request.Request("https://api.github.com/repos/NYGsatoshi/Coglatas/" + path,
        headers={"Accept": "application/vnd.github+json", "Authorization": "Bearer " + os.environ["GH_TOKEN"],
                 "X-GitHub-Api-Version": "2022-11-28"})
    with urllib.request.urlopen(req, timeout=30) as response:
        return json.load(response)

def main():
    require(os.environ["GITHUB_REPOSITORY"] == "NYGsatoshi/Coglatas" and
            os.environ["GITHUB_REF"] == "refs/heads/main", stage="trusted-workflow")
    source, base, evidence_sha = (os.environ[key] for key in ("SOURCE_SHA", "BASE_SHA", "EVIDENCE_COMMIT_SHA"))
    require(all(re.fullmatch(r"[0-9a-f]{40}", s) for s in (source, base, evidence_sha)), stage="input-sha")
    pr = int(os.environ["PR_NUMBER"])
    current_main = get("branches/main")["commit"]["sha"]
    trusted_sha = invoke(["git", "rev-parse", "HEAD"])
    require(trusted_sha == current_main, stage="trusted-main")
    if pr:
        info = get("pulls/" + str(pr))
        require(info["state"] == "open" and info["head"]["repo"]["full_name"] == "NYGsatoshi/Coglatas" and
                info["head"]["sha"] == source and info["base"]["sha"] == base == current_main, stage="live-pr-head")
    else:
        require(source == current_main, stage="live-main")
    compared = get("compare/" + base + "..." + source)
    require(compared["status"] in ("ahead", "identical"), stage="base-ancestry")
    tree = get("commits/" + source)["commit"]["tree"]["sha"]
    get("commits/" + evidence_sha)
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as handle:
        handle.write("tree_sha=" + tree + "\ntrusted_sha=" + trusted_sha + "\n")
    print(json.dumps({"sourceSha": source, "baseSha": base, "treeSha": tree, "trustedVerifierSha": trusted_sha}))
if __name__ == "__main__":
    try:
        main()
    except Exception:
        print('{"decision":"blocking","reasonCode":"EVIDENCE_INVALID","stage":"trusted-github-source"}')
        sys.exit(2)
