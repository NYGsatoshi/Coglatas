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
        original = item.attrib["fullPath"]
        if original.startswith("/_/"):
            # The deterministic PDB prefix is a source identity, not a host
            # directory. Bind it to this checkout without changing report bytes.
            relative = original[3:]
            if (not relative or "\\" in relative or ":" in relative
                    or any(part in ("", ".", "..") for part in relative.split("/"))):
                raise ValueError("Invalid deterministic source path")
            path = root / relative
        else:
            path = Path(original)
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


def _native_verifier():
    import importlib.util

    spec = importlib.util.spec_from_file_location(
        "native_binaries", Path(__file__).with_name("verify-sonar-windows-coverage.py"))
    if spec is None or spec.loader is None:
        raise ValueError("Native binary verifier is unavailable")
    verifier = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(verifier)
    return verifier


def verify_parallel_handoff(root: Path, folder: Path, sha: str, run: str,
                            attempt: str, *, compare_build: bool = False) -> None:
    """Check producer evidence and, after the scanner build, production identity."""
    if not re.fullmatch(r"[a-f0-9]{40}", sha) or not all(
            re.fullmatch(r"[1-9][0-9]*", x) for x in (run, attempt)):
        raise ValueError("Expected source SHA, run and attempt")
    native = _native_verifier()
    tree, _ = native.git_reader(root, Path("/usr/bin/git"), sha)
    relative = folder.relative_to(root).as_posix()

    def read(name: str) -> bytes:
        data = native.owned_bytes(root, relative + "/" + name)
        if not data:
            raise ValueError("Empty native evidence: " + name)
        return data

    report = read("verified.opencover.xml")
    receipt = json.loads(read("receipt.json"))
    measured = verify_report(folder / "verified.opencover.xml", root)
    if (receipt.get("sourceSha") != sha or receipt.get("treeSha") != tree
            or receipt.get("scope") != "DOTNET_COVERAGE_IMPORT_ONLY_NOT_RELEASE_ACCEPTANCE"
            or any(receipt.get(key) != value for key, value in measured.items())
            or measured["reportSha256"] != native.digest(report)):
        raise ValueError("Native coverage receipt differs from checkout or report")
    trx = read("backend-tests.trx")
    mapping = read("deterministic-source-roots.original")
    before = json.loads(read("pre-test-rebuild/outputs.json"))
    executed = json.loads(read("binary-provenance.json"))
    if (before.get("sourceSha") != sha or executed.get("sourceSha") != sha
            or executed.get("runId") != run or executed.get("attempt") != attempt
            or executed.get("productionBinariesUnchanged") is not True
            or executed.get("trxSha256") != native.digest(trx)
            or executed.get("sourceMappingSha256") != native.digest(mapping)):
        raise ValueError("Native execution receipt differs from run or evidence")

    expected = {"assemblies/" + name + "." + ext
                for name in native.ASSEMBLIES for ext in ("dll", "pdb")}
    initial = before.get("artifacts")
    actual = executed.get("executedArtifacts")
    for record in (initial, actual):
        if not isinstance(record, dict) or set(record) != expected or any(
                not isinstance(value, str) or not re.fullmatch(r"[a-f0-9]{64}", value)
                for value in record.values()):
            raise ValueError("Unexpected native assembly digest inventory")

    # The native test-only rebuild intentionally changes the test DLL/PDB.
    for name in native.ASSEMBLIES:
        if name == "Coglatas.Tests":
            continue
        for ext in ("dll", "pdb"):
            key = "assemblies/" + name + "." + ext
            if initial[key] != actual[key]:
                raise ValueError("Production binary changed during native coverage: " + key)
            if compare_build:
                output = "bin/Release/net11.0/" + name + "." + ext
                project = native.owned_bytes(root, native.ASSEMBLIES[name] + "/" + output)
                copied = native.owned_bytes(root, "tests/Coglatas.Tests/" + output)
                if native.digest(project) != actual[key] or copied != project:
                    raise ValueError("Scanner build differs from measured production binary: " + key)

    if compare_build:
        current = native.owned_bytes(root,
            "tests/Coglatas.Tests/bin/Release/net11.0/CoverletSourceRootsMapping_Coglatas.Tests")
        if current != mapping:
            raise ValueError("Scanner build source mapping differs from measured production mapping")
    print("Native Ubuntu coverage receipt and production identity verified"
          + (" against scanner build" if compare_build else " before scanner begin"))


if __name__ == "__main__":
    main()
