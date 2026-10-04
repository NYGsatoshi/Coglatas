#!/usr/bin/env python3
"""Require real same-execution Main prerequisites for additive status contexts."""
from __future__ import annotations

import json
import os
import re
import sys
from pathlib import Path


PREREQUISITES = {
    "build-test": {"main-dotnet-build", "main-build-artifacts", "main-validation"},
    "frontend-test": {"main-frontend-build", "main-build-artifacts", "main-validation"},
    "security-scan": {"main-dotnet-build", "main-build-artifacts", "main-validation"},
    "functional-fast": {"main-build-artifacts", "functional-fast-domains"},
}


def validate_main_results(context: str, needs: object, environment: dict[str, str]) -> dict:
    """GitHub supplies needs from this Main push; never accept a partial projection."""
    if context not in PREREQUISITES:
        raise ValueError("Unknown Main check context")
    if (environment.get("GITHUB_EVENT_NAME") != "push"
            or environment.get("GITHUB_REF") != "refs/heads/main"
            or environment.get("GITHUB_REPOSITORY") != "NYGsatoshi/Coglatas"
            or environment.get("GITHUB_WORKFLOW_REF") !=
            "NYGsatoshi/Coglatas/.github/workflows/main-build-artifacts.yml@refs/heads/main"):
        raise ValueError("Results must come from the authoritative Main push workflow")
    sha = environment.get("GITHUB_SHA", "")
    run_id = environment.get("GITHUB_RUN_ID", "")
    attempt = environment.get("GITHUB_RUN_ATTEMPT", "")
    if not re.fullmatch(r"[0-9a-f]{40}", sha) or not re.fullmatch(r"[1-9][0-9]*", run_id):
        raise ValueError("An exact source SHA and workflow execution are required")
    if not re.fullmatch(r"[1-9][0-9]*", attempt):
        raise ValueError("An exact positive workflow attempt is required")
    if not isinstance(needs, dict) or set(needs) != PREREQUISITES[context]:
        raise ValueError("The complete declared Main prerequisite set is required")
    results = {}
    for job in sorted(PREREQUISITES[context]):
        item = needs[job]
        if not isinstance(item, dict) or item.get("result") != "success":
            raise ValueError(f"Main prerequisite {job} did not succeed")
        results[job] = "success"
    return {"context": context, "sourceSha": sha, "workflowRunId": int(run_id),
            "workflowRunAttempt": int(attempt), "event": "push", "ref": "refs/heads/main",
            "prerequisiteResults": results, "decision": "PASS"}


def main() -> int:
    try:
        identity = validate_main_results(os.environ.get("CHECK_CONTEXT", ""),
                                         json.loads(os.environ.get("NEEDS_JSON", "null")),
                                         dict(os.environ))
    except (ValueError, TypeError) as error:
        print(f"NO-GO: {error}", file=sys.stderr)
        return 1
    print(json.dumps(identity, sort_keys=True))
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with Path(summary).open("a", encoding="utf-8") as stream:
            stream.write(f"### Main {identity['context']}: PASS\n\n")
            stream.write(f"Exact SHA `{identity['sourceSha']}`, run {identity['workflowRunId']}, "
                         f"attempt {identity['workflowRunAttempt']}.\n\n")
            for job in identity["prerequisiteResults"]:
                stream.write(f"- `{job}`: success\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
