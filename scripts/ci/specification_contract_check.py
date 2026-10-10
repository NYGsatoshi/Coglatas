"""Consume a same-run sanitized summary for the non-required specification-contract Advisory check."""

from __future__ import annotations

import argparse
from datetime import datetime, timedelta, timezone
import os
from pathlib import Path
import re

from specification_contract import bounded_bytes, read_json, instant, counts, FAMILIES, CLASSES


def consume(path: Path, sha: str, run_id: str, attempt: str, now: datetime) -> dict:
    report = read_json(bounded_bytes(path))
    if (not re.fullmatch(r"[a-f0-9]{40}", sha) or report["schemaVersion"] != 1 or
            report["verifierId"] != "specification-contract" or report["rollout"] != "ADVISORY" or
            report["candidateSha"] != sha or report.get("status") == "ERROR"):
        raise ValueError("Invalid Advisory identity or integrity error.")
    if (report["runId"] != run_id or report["runAttempt"] != attempt or
            not now - timedelta(hours=24) <= instant(report["capturedAtUtc"]) <= now + timedelta(minutes=5) or
            report["normativeReady"] is not False or report["ownerApproval"] != "UNVERIFIED" or
            report["trustedExecutionAttestation"] != "UNVERIFIED" or
            report["qualifiedNormativeRequirementCount"] is not None or report["qualifiedNormativeRelationshipCount"] is not None or
            report["preAvaloniaVerdict"] != "PRE-AVALONIA SEC-ARCH: BLOCKED"):
        raise ValueError("Unqualified provenance or authority.")
    canonical = report["canonicalInputs"]
    if canonical["status"] not in ("UNAVAILABLE", "STRUCTURALLY_VALID_DRAFT"):
        raise ValueError("Canonical integrity error.")
    coverage = canonical["declaredDraftCoverage"]
    if canonical["status"] == "UNAVAILABLE":
        if coverage is not None or canonical["sourceSpecificationRevision"] is not None or canonical["diagnosticCount"] is not None:
            raise ValueError("Unavailable input cannot imply canonical counts.")
    else:
        if (not re.fullmatch(r"[a-f0-9]{40}", canonical["sourceSpecificationRevision"]) or
                canonical["diagnosticCount"] != 0 or coverage is None):
            raise ValueError("Incomplete canonical linkage.")
        if coverage is not None:
            numeric = ("schemaVersion", "registryVersion", "activeRequirements", "deprecatedRequirements", "retiredRequirements",
                       "mappings", "manualMappings", "executedPassingLinks", "unresolvedLinks", "requirementsWithKnownLimitations")
            if any(type(coverage[key]) is not int or coverage[key] < 0 for key in numeric) or coverage["schemaVersion"] != 1 or coverage["registryVersion"] < 1:
                raise ValueError("Unsupported Draft counters.")
            for name, categories in (("activeFamilies", FAMILIES), ("severities", ("Blocking", "Advisory", "Manual")), ("verificationClasses", CLASSES)):
                rows = [{"category": category, "count": count} for category, count in coverage[name].items()]
                if set(coverage[name]) != set(categories):
                    raise ValueError("Unsupported coverage categories.")
                counts(rows, categories)
            if (sum(coverage["activeFamilies"].values()) != coverage["activeRequirements"] or
                    sum(coverage["severities"].values()) != coverage["activeRequirements"] + coverage["deprecatedRequirements"] or
                    coverage["verificationClasses"]["Manual"] != coverage["manualMappings"] or
                    sum(count for category, count in coverage["verificationClasses"].items() if category != "Manual") == 0 and coverage["executedPassingLinks"] > 0):
                raise ValueError("Draft coverage totals disagree.")
    lanes = report["existingVerifierLanes"]
    if set(lanes) != {"architecture", "backend"}:
        raise ValueError("Missing existing lane.")
    observed = {}
    for name, lane in lanes.items():
        if lane["canonicalSpecMappings"] != "UNRESOLVED" or lane["status"] not in ("UNAVAILABLE", "UNVERIFIED", "FAIL", "OBSERVED_PASS"):
            raise ValueError("Unsupported lane authority.")
        values = [lane[key] for key in ("total", "passed", "failed", "unexecuted")]
        if lane["status"] == "UNAVAILABLE":
            if any(value is not None for value in values) or lane["executionDigest"] is not None:
                raise ValueError("Missing lane cannot imply execution.")
        else:
            if (any(type(value) is not int or value < 0 for value in values) or values[0] == 0 or
                    values[0] != sum(values[1:]) or not re.fullmatch(r"[a-f0-9]{64}", lane["executionDigest"]) or
                    lane["status"] != ("FAIL" if values[2] else "UNVERIFIED" if values[3] else "OBSERVED_PASS")):
                raise ValueError("Inconsistent lane observations.")
        observed[name] = {key: lane[key] for key in ("status", "total", "passed", "failed", "unexecuted")}
    return {"canonicalStatus": canonical["status"], "sourceRevision": canonical["sourceSpecificationRevision"],
            "declaredDraftCoverage": coverage, "lanes": observed}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--receipt", required=True, type=Path)
    parser.add_argument("--candidate-sha", required=True)
    args = parser.parse_args()
    try:
        result = consume(args.receipt, args.candidate_sha, os.environ["GITHUB_RUN_ID"], os.environ["GITHUB_RUN_ATTEMPT"], datetime.now(timezone.utc))
        lines = ["### specification-contract: Advisory check", "", "Sanitized same-run receipt: **OBSERVED**.",
                 "Candidate: `" + args.candidate_sha + "`. Specification source revision: `" + (result["sourceRevision"] or "UNKNOWN") + "`.",
                 "Canonical input: **" + result["canonicalStatus"] + "**. Qualified normative coverage: **UNKNOWN**.",
                 "Owner scope and trusted execution provenance: **UNVERIFIED**. PRE-AVALONIA SEC-ARCH: **BLOCKED**.", "",
                 "This non-required check consumes existing lane observations; it grants no acceptance or enforcement authority."]
        lines.extend(["", "| Existing lane | Observed status | Passed | Failed | Unexecuted |", "| --- | --- | ---: | ---: | ---: |"])
        for name, lane in result["lanes"].items():
            lines.append("| " + name + " | " + lane["status"] + " | " + str(lane["passed"]) + " | " + str(lane["failed"]) + " | " + str(lane["unexecuted"]) + " |")
        print("specification-contract: ADVISORY / UNVERIFIED; canonical coverage UNKNOWN")
        code = 0
    except (ValueError, KeyError, TypeError, AttributeError, OSError):
        lines = ["### specification-contract: Advisory check", "", "**ERROR**: the same-run sanitized receipt is missing or has an integrity failure.",
                 "Canonical qualification remains **UNKNOWN**. PRE-AVALONIA SEC-ARCH: **BLOCKED**."]
        print("specification-contract: ERROR; sanitized same-run receipt unavailable or inconsistent")
        code = 1
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with Path(summary).open("a", encoding="utf-8") as destination:
            destination.write("\n".join(lines) + "\n")
    return code


if __name__ == "__main__":
    raise SystemExit(main())
