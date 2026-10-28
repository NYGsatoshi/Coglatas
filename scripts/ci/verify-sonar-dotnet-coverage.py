#!/usr/bin/env python3
"""Reject missing, empty or unmappable C# coverage before Sonar publication."""

from __future__ import annotations

import hashlib
import json
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def verify_report(report: Path, root: Path) -> dict:
    """Check actual sequence points, not the report's claimed summary totals."""
    document = ET.parse(report).getroot()
    if document.tag != "CoverageSession":
        raise ValueError("Expected OpenCover CoverageSession")
    production_files: set[str] = set()
    points = covered = 0
    for module in document.findall("./Modules/Module"):
        files = {}
        for item in module.findall("./Files/File"):
            path = Path(item.attrib["fullPath"])
            path = (root / path).resolve()
            relative = path.relative_to(root.resolve())
            # Tests/tools can be measured but cannot satisfy production coverage.
            if (relative.parts[0] == "src" and path.suffix == ".cs"
                    and not {"bin", "obj"}.intersection(relative.parts)):
                if not path.is_file():
                    raise ValueError("Coverage references missing production source")
                files[item.attrib["uid"]] = relative.as_posix()
        for point in module.findall(".//SequencePoint"):
            visits = int(point.attrib["vc"])
            if visits < 0:
                raise ValueError("Negative visit count")
            if point.attrib["fileid"] in files:
                production_files.add(files[point.attrib["fileid"]])
                points += 1
                covered += visits > 0
    if not points or not covered:
        raise ValueError("No executed production sequence points")
    return {
        "reportSha256": hashlib.sha256(report.read_bytes()).hexdigest(),
        "productionFiles": len(production_files),
        "sequencePoints": points,
        "visitedSequencePoints": covered,
    }


def main() -> None:
    folder = ROOT / "artifacts/sonar-dotnet"
    reports = sorted(folder.glob("**/coverage.opencover.xml"))
    if len(reports) != 1:
        raise ValueError("Expected one fresh backend OpenCover report")
    result = verify_report(reports[0], ROOT)
    result.update({
        "sourceSha": subprocess.check_output(
            ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
        "treeSha": subprocess.check_output(
            ["git", "rev-parse", "HEAD^{tree}"], cwd=ROOT, text=True).strip(),
        "scope": "DOTNET_COVERAGE_IMPORT_ONLY_NOT_RELEASE_ACCEPTANCE",
    })
    (folder / "receipt.json").write_text(
        json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))


if __name__ == "__main__":
    main()
