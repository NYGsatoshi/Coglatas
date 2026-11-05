#!/usr/bin/env python3
"""Advisory-only full SARIF parity for two independent Qodana scans.

Embedded source SHA / Actions run URL are checked, but run-attempt and
GitHub artifact-publisher provenance are NOT attested by SARIF alone.
"""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import os
import re
import sys
import tempfile
import uuid
from pathlib import Path
from typing import Any

MAX_BYTES = 20 * 1024 * 1024
REPOSITORY = "https://github.com/NYGsatoshi/Coglatas.git"
SCHEMA = "https://schemastore.azurewebsites.net/schemas/json/sarif-2.1.0-rtm.6.json"


class ParityError(ValueError):
    """Evidence is invalid; no successful gate qualification is authorized."""


def unique_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    out: dict[str, Any] = {}
    for key, value in pairs:
        if key in out:
            raise ParityError("Duplicate JSON field")
        out[key] = value
    return out


def reject_constant(_value: str) -> None:
    raise ParityError("Nonfinite JSON")


def read_report(path: Path, sha: str, run_id: str, branch: str) -> tuple[dict[str, Any], str, str, tuple[int, int, int]]:
    if path.is_symlink() or not path.is_file():
        raise ParityError("Missing or symlinked report")
    with path.open("rb") as f:
        raw = f.read(MAX_BYTES + 1)
    if not raw or len(raw) > MAX_BYTES:
        raise ParityError("Empty or oversized report")
    try:
        doc = json.loads(
            raw.decode("utf-8"),
            object_pairs_hook=unique_object,
            parse_constant=reject_constant,
        )
    except (json.JSONDecodeError, UnicodeError, RecursionError) as exc:
        raise ParityError("Invalid JSON document") from exc
    if not isinstance(doc, dict) or doc.get("$schema") != SCHEMA or doc.get("version") != "2.1.0":
        raise ParityError("Unexpected SARIF schema")
    runs = doc.get("runs")
    if not isinstance(runs, list) or len(runs) != 1 or not isinstance(runs[0], dict):
        raise ParityError("Exactly one SARIF run required")
    run = runs[0]
    vcs = run.get("versionControlProvenance")
    if not isinstance(vcs, list) or len(vcs) != 1 or not isinstance(vcs[0], dict):
        raise ParityError("Missing source provenance")
    if (vcs[0].get("repositoryUri"), vcs[0].get("revisionId"), vcs[0].get("branch")) != (REPOSITORY, sha, branch):
        raise ParityError("Source revision or repository mismatch")
    auto = run.get("automationDetails")
    if not isinstance(auto, dict) or not isinstance(auto.get("id"), str) or not auto["id"]:
        raise ParityError("Missing automation identity")
    expected_url = f"https://github.com/NYGsatoshi/Coglatas/actions/runs/{run_id}"
    if not isinstance(auto.get("properties"), dict) or auto["properties"].get("jobUrl") != expected_url:
        raise ParityError("Wrong GitHub Actions run")
    try:
        guid = str(uuid.UUID(auto.get("guid", "")))
    except (ValueError, AttributeError, TypeError) as exc:
        raise ParityError("Missing or invalid GUID") from exc
    if auto["guid"] != guid:
        raise ParityError("Noncanonical GUID")
    inv = run.get("invocations")
    if (
        not isinstance(inv, list) or len(inv) != 1 or not isinstance(inv[0], dict)
        or inv[0].get("executionSuccessful") is not True
        or type(inv[0].get("exitCode")) is not int or inv[0]["exitCode"] != 0
    ):
        raise ParityError("Execution not proven successful")
    tool = run.get("tool")
    driver = tool.get("driver") if isinstance(tool, dict) else None
    if not isinstance(driver, dict) or driver.get("name") != "QDNETC" or not isinstance(driver.get("version"), str) or not driver["version"]:
        raise ParityError("Unexpected analyzer")
    rules, results, artifacts = driver.get("rules"), run.get("results"), run.get("artifacts")
    if any(not isinstance(items, list) or not items or not all(isinstance(v, dict) for v in items)
           for items in (rules, results, artifacts)):
        raise ParityError("Incomplete inventory")
    ids = [rule.get("id") for rule in rules]
    if any(not isinstance(i, str) or not i for i in ids) or len(ids) != len(set(ids)):
        raise ParityError("Duplicate/invalid rules")
    return doc, hashlib.sha256(raw).hexdigest(), guid, (len(results), len(rules), len(artifacts))


def compare(community: Path, cloud: Path, sha: str, run_id: str, branch: str) -> dict[str, Any]:
    if not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise ParityError("Invalid expected commit")
    if not re.fullmatch(r"[1-9][0-9]{0,19}", str(run_id)):
        raise ParityError("Invalid expected run")
    if branch not in ("main", "SecurityTestImplimation"):
        raise ParityError("Invalid expected branch")
    if community.resolve() == cloud.resolve() or (
        community.exists() and cloud.exists() and os.path.samefile(community, cloud)
    ):
        raise ParityError("Same report passed twice")
    a, a_digest, a_guid, counts = read_report(community, sha, str(run_id), branch)
    b, b_digest, b_guid, other_counts = read_report(cloud, sha, str(run_id), branch)
    if a_guid == b_guid or counts != other_counts:
        raise ParityError("Independent run identity or inventory counts mismatch")
    left, right = copy.deepcopy(a), copy.deepcopy(b)
    # Never ignore rule/result/metadata drift beyond the one volatile GUID.
    del left["runs"][0]["automationDetails"]["guid"]
    del right["runs"][0]["automationDetails"]["guid"]
    if left != right:
        raise ParityError("SARIF differs outside run GUID")
    canonical = json.dumps(left, sort_keys=True, separators=(",", ":"), ensure_ascii=False, allow_nan=False)
    return {
        "schemaVersion": 1,
        "status": "PARITY_OBSERVED_NOT_ATTESTED",
        "qualifiedForGateReuse": False,
        "runAttemptVerified": False,
        "sourceSha": sha,
        "runId": str(run_id),
        "branch": branch,
        "resultCount": counts[0],
        "ruleCount": counts[1],
        "artifactCount": counts[2],
        "communitySarifSha256": a_digest,
        "cloudSarifSha256": b_digest,
        "normalizedSarifSha256": hashlib.sha256(canonical.encode("utf-8")).hexdigest(),
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Qodana independent-scan parity (advisory)")
    parser.add_argument("--community", required=True, type=Path)
    parser.add_argument("--cloud", required=True, type=Path)
    parser.add_argument("--source-sha", required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--branch", required=True, choices=["main", "SecurityTestImplimation"])
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args(argv)
    try:
        output = args.output
        if output.exists() or output.is_symlink() or output.resolve() in (args.community.resolve(), args.cloud.resolve()):
            raise ParityError("Output must be a new independent path")
        result = compare(args.community, args.cloud, args.source_sha, args.run_id, args.branch)
        output.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.NamedTemporaryFile("w", encoding="utf-8", dir=output.parent, prefix=".qodana-parity-", delete=False) as f:
            temporary = Path(f.name)
            try:
                json.dump(result, f, indent=2, sort_keys=True)
                f.write("\n")
            except BaseException:
                temporary.unlink(missing_ok=True)
                raise
        temporary.replace(output)
        print("Qodana SARIF parity observed; run-attempt/publisher NOT attested.")
        return 0
    except (ParityError, OSError, TypeError, ValueError, RecursionError, OverflowError):
        # Never echo potentially private source paths or findings.
        print("Qodana parity REJECTED; no qualification issued.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
