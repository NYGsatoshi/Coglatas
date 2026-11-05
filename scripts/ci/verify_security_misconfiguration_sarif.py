#!/usr/bin/env python3
"""Validate Trivy misconfiguration evidence without exposing finding payloads."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any

MAX_REPORT_BYTES = 16 * 1024 * 1024
MAX_FINDINGS = 100_000


class EvidenceError(ValueError):
    """A security scanner result is missing or does not meet the evidence contract."""


def _unique_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise EvidenceError("SARIF_DUPLICATE_KEY")
        result[key] = value
    return result


def count_findings(report_path: Path) -> int:
    try:
        if report_path.stat().st_size > MAX_REPORT_BYTES:
            raise EvidenceError("SARIF_TOO_LARGE")
        document = json.loads(
            report_path.read_text(encoding="utf-8"),
            object_pairs_hook=_unique_object,
            parse_constant=lambda _: (_ for _ in ()).throw(EvidenceError("SARIF_NONFINITE_NUMBER")),
        )
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise EvidenceError("SARIF_UNAVAILABLE_OR_INVALID") from exc

    if not isinstance(document, dict) or document.get("version") != "2.1.0":
        raise EvidenceError("SARIF_VERSION_INVALID")
    runs = document.get("runs")
    if not isinstance(runs, list) or not runs:
        raise EvidenceError("SARIF_RUNS_MISSING")

    total = 0
    for run in runs:
        if not isinstance(run, dict):
            raise EvidenceError("SARIF_RUN_INVALID")
        tool = run.get("tool")
        driver = tool.get("driver") if isinstance(tool, dict) else None
        if not isinstance(driver, dict) or driver.get("name") != "Trivy":
            raise EvidenceError("SARIF_TOOL_INVALID")
        results = run.get("results")
        if not isinstance(results, list):
            raise EvidenceError("SARIF_RESULTS_MISSING")
        for result in results:
            if not isinstance(result, dict) or not isinstance(result.get("ruleId"), str) or not result["ruleId"].strip():
                raise EvidenceError("SARIF_FINDING_INVALID")
        total += len(results)
        if total > MAX_FINDINGS:
            raise EvidenceError("SARIF_FINDINGS_LIMIT")
    return total


def write_summary(path: Path, findings: int, sha: str) -> None:
    if sha and not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise EvidenceError("SHA_INVALID")
    summary = (
        "## IaC / Docker misconfiguration scan (shadow)\n\n"
        "| Property | Result |\n| --- | --- |\n"
        "| Scanner | Trivy (misconfig) |\n"
        f"| Findings in HIGH/CRITICAL filter | {findings} |\n"
        "| Decision | ADVISORY — findings are not a release clearance or blocking gate |\n"
        f"| Analyzed SHA | `{sha or 'not supplied'}` |\n\n"
        "A successful scan does not establish full IaC coverage. Raw finding messages are not copied to the job summary.\n"
    )
    try:
        with path.open("a", encoding="utf-8") as stream:
            stream.write(summary)
    except OSError as exc:
        raise EvidenceError("SUMMARY_WRITE_FAILED") from exc


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report", type=Path)
    parser.add_argument("--summary", type=Path)
    parser.add_argument("--sha", default="")
    args = parser.parse_args()
    try:
        count = count_findings(args.report)
        if args.summary is not None:
            write_summary(args.summary, count, args.sha)
    except EvidenceError as exc:
        print(f"::error::{exc}", file=sys.stderr)
        return 1
    print(f"Trivy SARIF verified: {count} finding(s); advisory only.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
