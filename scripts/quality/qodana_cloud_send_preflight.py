#!/usr/bin/env python3
"""Non-executing Qodana Cloud-send preflight for an existing report directory.

The official "qodana send --results-dir" path is intentionally NOT run.
Current evidence lacks trusted producer-job attestation and Cloud ingest proof.
Do not add an "execute" flag without a separately approved security contract.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import tempfile
from pathlib import Path
from typing import Any

from qodana_sarif_parity import MAX_BYTES, ParityError, read_report

HEX_SHA = re.compile(r"[0-9a-f]{40}\Z")
RUN = re.compile(r"[1-9][0-9]{0,18}\Z")
MAX_SUPPORT_BYTES = 20 * 1024 * 1024
REQUIRED_REPORT_FILES = (
    "qodana.sarif.json",
    "qodana-short.sarif.json",
    "report/results/result-allProblems.json",
    "report/results/metaInformation.json",
    "report/manifest.json",
    "report/index.html",
)


def reject(message: str) -> None:
    raise ParityError(message)


def _regular_in_root(root: Path, name: str) -> Path:
    path = root
    for component in name.split("/"):
        path = path / component
        if path.is_symlink():
            reject("Qodana report contains a symlink")
    if not path.is_file() or not 0 < path.stat().st_size <= MAX_SUPPORT_BYTES:
        reject("Missing or oversized Qodana report part")
    return path


def preflight(root: Path, sha: str, run_id: str, attempt: int) -> dict[str, Any]:
    if not HEX_SHA.fullmatch(sha) or not RUN.fullmatch(str(run_id)):
        reject("Invalid source SHA or Actions run")
    if type(attempt) is not int or not 1 <= attempt <= 100:
        reject("Invalid run attempt")
    if not root.is_dir() or root.is_symlink() or root.resolve() != root.absolute():
        reject("Qodana report directory is not an independent absolute directory")
    files = [_regular_in_root(root, name) for name in REQUIRED_REPORT_FILES]
    if files[0].stat().st_size > MAX_BYTES:
        reject("Oversized original SARIF")
    _, source_digest, _, counts = read_report(files[0], sha, str(run_id), "main")
    total = sum(file.stat().st_size for file in files)
    if total > 32 * 1024 * 1024:
        reject("Qodana report support files exceed review size limit")
    # Do not trust support file data as a signed result or parse its arbitrary
    # embedded content. Only the canonical SARIF is semantically verified.
    return {
        "schemaVersion": 1,
        "status": "CLOUD_SEND_LAYOUT_REVIEWED_NOT_EXECUTED",
        "sourceSha": sha,
        "runId": str(run_id),
        "runAttempt": attempt,
        "originalSarifSha256": source_digest,
        "resultCount": counts[0],
        "ruleCount": counts[1],
        "artifactCount": counts[2],
        "requiredReportEntriesPresent": True,
        "qodanaCommand": "send",
        "qodanaDocumentedFlags": ["--results-dir", "--report-dir"],
        "qodanaCliVersionAndDigestApproved": False,
        "producerJobCryptographicallyAttested": False,
        "cloudReplayVerified": False,
        "cloudSendExecuted": False,
        "qualifiedForGateReuse": False,
    }


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description="Qodana Cloud-send layout check, never uploads")
    p.add_argument("--results-dir", required=True, type=Path)
    p.add_argument("--source-sha", required=True)
    p.add_argument("--run-id", required=True)
    p.add_argument("--run-attempt", required=True, type=int)
    p.add_argument("--output", required=True, type=Path)
    args = p.parse_args(argv)
    try:
        if args.output.exists() or args.output.is_symlink():
            reject("Output must be a new file")
        report = preflight(args.results_dir, args.source_sha, args.run_id, args.run_attempt)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.NamedTemporaryFile(
            mode="w", encoding="utf-8", dir=args.output.parent,
            prefix=".qodana-cloud-replay-", delete=False,
        ) as f:
            temp = Path(f.name)
            try:
                json.dump(report, f, indent=2, sort_keys=True)
                f.write("\n")
            except BaseException:
                temp.unlink(missing_ok=True)
                raise
        temp.replace(args.output)
        print("Qodana Cloud send layout REVIEWED; no Cloud send executed.")
        return 0
    except (ParityError, OSError, ValueError, TypeError, RecursionError):
        print("Qodana Cloud send preflight REJECTED.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
