#!/usr/bin/env python3
"""Reject missing, empty or unmappable C# coverage before Sonar publication."""

from __future__ import annotations

import hashlib
import json
import re
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def _production_file_ids(module: ET.Element, root: Path) -> dict[str, str]:
    """Map actual production source IDs without crediting tests or generated files."""
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
    return files


def verify_report(report: Path, root: Path) -> dict:
    """Check actual sequence points, not the report's claimed summary totals."""
    document = ET.parse(report).getroot()
    if document.tag != "CoverageSession":
        raise ValueError("Expected OpenCover CoverageSession")
    production_files: set[str] = set()
    points = covered = 0
    for module in document.findall("./Modules/Module"):
        files = _production_file_ids(module, root)
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


def select_collector_report(folder: Path) -> tuple[Path, int]:
    """Require one collector report and only identical native VSTest copies."""
    reports = sorted(folder.glob("**/coverage.opencover.xml"))
    primary = [p for p in reports if p.parent.parent == folder and re.fullmatch(
        r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", p.parent.name)]
    if len(primary) != 1:
        print(json.dumps({"reportCount": len(reports), "collectorCount": len(primary)}))
        raise ValueError("Expected one fresh backend collector report")
    selected = primary[0]
    digest = hashlib.sha256(selected.read_bytes()).hexdigest()
    for path in reports:
        path.resolve().relative_to(folder.resolve())
        if path == selected:
            continue
        parts = path.relative_to(folder).parts
        if (len(parts) != 4 or parts[1] != "In" or not re.fullmatch(
                r"_" + re.escape(parts[2]) + r"_\d{4}-\d{2}-\d{2}_\d{2}_\d{2}_\d{2}", parts[0])):
            raise ValueError("Unexpected additional coverage report layout")
        if hashlib.sha256(path.read_bytes()).hexdigest() != digest:
            raise ValueError("VSTest attachment differs from collector report")
    return selected, len(reports) - 1


def main() -> None:
    folder = ROOT / "artifacts/sonar-dotnet"
    report, copies = select_collector_report(folder)
    result = verify_report(report, ROOT)
    # Import one validated report rather than both collector and TRX attachment.
    publication = folder / "verified.opencover.xml"
    publication.write_bytes(report.read_bytes())
    if hashlib.sha256(publication.read_bytes()).hexdigest() != result["reportSha256"]:
        raise ValueError("Coverage changed while preparing publication")
    result.update({"collectorReport": report.relative_to(ROOT).as_posix(),
                   "identicalVSTestCopies": copies})
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
