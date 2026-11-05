#!/usr/bin/env python3
"""Local shadow JUnit counts only; NEVER a full roster/producer attestation."""
from __future__ import annotations
import argparse
import os
from pathlib import Path
import stat
import sys
import xml.etree.ElementTree as ET


def summarize(path: Path, shard: str) -> dict:
    if shard not in ("1/2", "2/2"):
        raise ValueError("INVALID_SHARD")
    meta = path.lstat()
    if not stat.S_ISREG(meta.st_mode) or meta.st_nlink != 1 or not (0 < meta.st_size <= 16 * 1024 * 1024):
        raise ValueError("INVALID_JUNIT_FILE")
    root = ET.parse(path).getroot()
    if root.tag not in ("testsuite", "testsuites"):
        raise ValueError("INVALID_JUNIT_ROOT")
    cases = root.findall(".//testcase")
    if not 0 < len(cases) <= 5000:
        raise ValueError("INVALID_JUNIT_CASES")
    if any(case.find("failure") is not None or case.find("error") is not None for case in cases):
        raise ValueError("FAILED_TEST")
    skipped = sum(case.find("skipped") is not None for case in cases)
    if len(cases) - skipped <= 0:
        raise ValueError("NO_PASSES")
    return {"shard": shard, "cases": len(cases), "passed": len(cases) - skipped,
            "skipped": skipped, "qualification": "LOCAL_SHARD_ONLY_NOT_FULL_ROSTER"}


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--shard", required=True)
    parser.add_argument("--file", type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        report = summarize(args.file, args.shard)
        safe = ("Playwright shadow shard: {shard} cases={cases} passed={passed} "
                "skipped={skipped} qualification={qualification}").format(**report)
        print(safe)
        summary = os.getenv("GITHUB_STEP_SUMMARY")
        if summary:
            with open(summary, "a", encoding="utf-8") as sink:
                sink.write(safe + "\n")
        return 0
    except (OSError, ValueError, TypeError, ET.ParseError):
        print("Playwright shadow shard refused: INCOMPLETE_OR_INVALID_LOCAL_JUNIT", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
