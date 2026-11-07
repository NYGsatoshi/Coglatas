#!/usr/bin/env python3
"""Bind native Windows Cobertura bytes to the exact source and executed binaries."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parents[2]
ASSEMBLIES = {
    "Coglatas.Domain": "src/Coglatas.Domain",
    "Coglatas.Application": "src/Coglatas.Application",
    "Coglatas.Infrastructure": "src/Coglatas.Infrastructure",
    "Coglatas.UI.Core": "src/Coglatas.UI.Core",
    "Coglatas.Web": "src/Coglatas.Web",
    "Coglatas.SecurityArchitecture": "tools/Coglatas.SecurityArchitecture",
    "Coglatas.Tests": "tests/Coglatas.Tests",
}
GUID = r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}"


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def owned_bytes(root: Path, relative: str) -> bytes:
    path = PurePosixPath(relative)
    if (not relative or path.is_absolute() or path.as_posix() != relative
            or any(part in (".", "..") for part in path.parts) or "\\" in relative
            or ":" in relative or "\0" in relative):
        raise ValueError("Unsafe evidence path")
    current = root
    for part in path.parts:
        current /= part
        if current.is_symlink():
            raise ValueError("Symbolic evidence path")
    current.resolve().relative_to(root.resolve())
    return current.read_bytes()


def parse_xml(raw: bytes) -> ET.Element:
    if b"<!DOCTYPE" in raw.upper() or b"<!ENTITY" in raw.upper():
        raise ValueError("XML declarations are forbidden")
    return ET.fromstring(raw)


def native_report(folder: Path) -> tuple[str, list[str]]:
    reports = sorted(folder.glob("**/coverage.cobertura.xml"))
    primary = [p for p in reports if p.parent.parent == folder
               and re.fullmatch(GUID, p.parent.name)]
    if len(primary) != 1:
        raise ValueError("Expected one fresh native collector report")
    selected = primary[0].relative_to(folder).as_posix()
    original = owned_bytes(folder, selected)
    copies = []
    for path in reports:
        relative = path.relative_to(folder).as_posix()
        if relative == selected:
            continue
        parts = PurePosixPath(relative).parts
        if (len(parts) != 4 or parts[1] != "In"
                or not re.fullmatch(r"[A-Za-z0-9_.-]+", parts[2]) or not re.fullmatch(
                    r"[A-Za-z0-9_.-]*_" + re.escape(parts[2])
                    + r"_\d{4}-\d{2}-\d{2}_\d{2}_\d{2}_\d{2}", parts[0])):
            raise ValueError("Unexpected additional report layout")
        if owned_bytes(folder, relative) != original:
            raise ValueError("Native VSTest copy differs")
        copies.append(relative)
    return selected, copies


def verify_trx(raw: bytes) -> int:
    document = parse_xml(raw)
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    counters = document.find("./t:ResultSummary/t:Counters", ns)
    cases = document.findall("./t:Results/t:UnitTestResult", ns)
    if counters is None or not cases or any(c.get("outcome") != "Passed" for c in cases):
        raise ValueError("Actual nonempty all-passed Windows tests required")
    count = len(cases)
    if any(int(counters.get(key, "-1")) != count for key in ("total", "executed", "passed")):
        raise ValueError("TRX execution accounting differs")
    if any(int(counters.get(key, "0")) for key in ("failed", "notExecuted", "aborted", "error", "timeout")):
        raise ValueError("Incomplete Windows tests")
    return count


def verify_report(raw: bytes, source) -> dict:
    document = parse_xml(raw)
    if document.tag != "coverage" or document.findall("./sources/source"):
        raise ValueError("Expected native deterministic Cobertura paths")
    sources, lines, conditions, generated = {}, {}, {}, {}
    for item in document.findall("./packages/package/classes/class"):
        filename = item.attrib["filename"]
        if not filename.startswith("/_/"):
            # These compiler-generated virtual documents also occur in the
            # unchanged Linux report. Retain their original bytes and ranges,
            # but never credit them as mapped Git production source.
            normalized = filename.replace("\\", "/")
            generators = (
                r"System.Text.RegularExpressions.Generator/System.Text.RegularExpressions.Generator.RegexGenerator/RegexGenerator.g.cs",
                r"Microsoft.AspNetCore.OpenApi.SourceGenerators/Microsoft.AspNetCore.OpenApi.SourceGenerators.XmlCommentGenerator/OpenApiXmlCommentSupport.generated.cs",
            )
            pattern = (r"[A-Za-z]:/[^\x00]+/(?:(?:src|tools)/Coglatas\.[A-Za-z.]+/obj/Release/net10\.0"
                       r"|obj/Coglatas\.[A-Za-z.]+/release)/(?:"
                       + "|".join(re.escape(x) for x in generators) + r")")
            if not re.fullmatch(pattern, normalized) or "/../" in normalized or "/./" in normalized:
                raise ValueError("Nondeterministic coverage source")
            points = []
            for line in item.findall("./lines/line"):
                number, hits = int(line.attrib["number"]), int(line.attrib["hits"])
                if number < 1 or hits < 0:
                    raise ValueError("Invalid generated source range or hit count")
                points.append(dict(line.attrib))
            generated.setdefault(filename, []).extend(points)
            continue
        relative = filename[3:]
        path = PurePosixPath(relative)
        if (path.as_posix() != relative or ".." in path.parts or "\\" in relative
                or ":" in relative or "\0" in relative or path.is_absolute()
                or not path.parts or path.parts[0] not in ("src", "tools")
                or path.suffix != ".cs" or {"bin", "obj"}.intersection(path.parts)):
            raise ValueError("Non-production or unsafe coverage source")
        data = source(relative)
        sources[relative] = digest(data)
        bound = len(data.decode("utf-8-sig").splitlines())
        for line in item.findall("./lines/line"):
            number, hits = int(line.attrib["number"]), int(line.attrib["hits"])
            if number < 1 or number > bound or hits < 0:
                raise ValueError("Invalid native source range or hit count")
            key = (relative, number)
            lines[key] = max(lines.get(key, 0), hits)
            branch = line.get("branch", "false").lower()
            if branch not in ("true", "false"):
                raise ValueError("Invalid branch flag")
            if branch == "true":
                match = re.fullmatch(r"[0-9.]+% \((\d+)/(\d+)\)", line.get("condition-coverage", ""))
                if not match:
                    raise ValueError("Missing original condition accounting")
                covered, total = map(int, match.groups())
                if total < 1 or covered > total:
                    raise ValueError("Invalid condition accounting")
                if key in conditions and conditions[key] != (covered, total):
                    raise ValueError("Conflicting native condition accounting")
                conditions[key] = (covered, total)
    if not sources or not lines or not any(lines.values()):
        raise ValueError("No native executed production coverage")
    return {"sources": sources, "unmappedGeneratedSources": generated,
            "lineEntries": len(lines),
            "zeroHitLines": sum(value == 0 for value in lines.values()),
            "coveredLines": sum(value > 0 for value in lines.values()),
            "conditions": sum(v[1] for v in conditions.values()),
            "coveredConditions": sum(v[0] for v in conditions.values())}


def git_reader(root: Path, executable: Path, sha: str):
    if not executable.is_absolute() or not executable.is_file():
        raise ValueError("Explicit absolute host Git executable required")
    try:
        executable.resolve().relative_to(root.resolve())
    except ValueError:
        pass
    else:
        raise ValueError("Repository-local Git is not trusted")

    def git(*args):
        return subprocess.check_output([str(executable), *args], cwd=root, timeout=30)

    if git("rev-parse", "HEAD").decode().strip() != sha:
        raise ValueError("Checkout differs from expected source SHA")
    tree = git("rev-parse", "HEAD^{tree}").decode().strip()

    def source(relative):
        original = git("show", sha + ":" + relative)
        if owned_bytes(root, relative) != original:
            raise ValueError("Coverage source bytes differ from Git")
        return original

    return tree, source


def assemblies(root: Path, folder: Path, sha: str, capture: bool) -> dict:
    result = {}
    for name, project in ASSEMBLIES.items():
        for extension in ("dll", "pdb"):
            relative = "assemblies/" + name + "." + extension
            if capture:
                output = "bin/Release/net10.0/" + name + "." + extension
                data = owned_bytes(root, project + "/" + output)
                executed = owned_bytes(root, "tests/Coglatas.Tests/" + output)
                if data != executed:
                    raise ValueError("Executed assembly differs from project output")
                destination = folder / relative
                destination.parent.mkdir(exist_ok=True)
                with destination.open("xb") as stream:
                    stream.write(data)
            data = owned_bytes(folder, relative)
            if extension == "dll" and sha.encode() not in data:
                raise ValueError("Executed assembly lacks full source revision")
            result[relative] = digest(data)
    return result


def qualify(root: Path, folder: Path, executable: Path, sha: str, run: str,
            attempt: str, capture: bool) -> dict:
    if not re.fullmatch(r"[a-f0-9]{40}", sha) or not all(
            re.fullmatch(r"[1-9][0-9]*", x) for x in (run, attempt)):
        raise ValueError("Expected provider SHA/run/attempt required")
    tree, source = git_reader(root, executable, sha)
    report, copies = native_report(folder)
    raw = owned_bytes(folder, report)
    measured = verify_report(raw, source)
    trx = owned_bytes(folder, "windows-tests.trx")
    count = verify_trx(trx)
    project = source("tests/Coglatas.Tests/Coglatas.Tests.csproj")
    if b'Include="coverlet.collector" Version="10.1.0"' not in project:
        raise ValueError("Unexpected native collector version")
    receipt = {"schemaVersion": 1, "sourceSha": sha, "treeSha": tree,
               "runId": run, "attempt": attempt, "platform": "Windows",
               "collectorVersion": "10.1.0", "filter": "Portability=CrossPlatform",
               "collectorReport": report, "identicalVSTestCopies": copies,
               "reportSha256": digest(raw), "trxSha256": digest(trx),
               "passedTests": count, "coverage": measured,
               "executedArtifacts": assemblies(root, folder, sha, capture),
               "scope": "SUPPLEMENTAL_NATIVE_WINDOWS_COVERAGE_NOT_RELEASE_ACCEPTANCE"}
    publication = folder / "verified.cobertura.xml"
    if capture:
        with publication.open("xb") as stream:
            stream.write(raw)
        with (folder / "receipt.json").open("x", encoding="utf-8") as stream:
            json.dump(receipt, stream, indent=2, sort_keys=True)
            stream.write("\n")
    else:
        saved = json.loads(owned_bytes(folder, "receipt.json"))
        if saved != receipt or owned_bytes(folder, publication.name) != raw:
            raise ValueError("Producer receipt or publication bytes differ")
    return receipt


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("capture", "consume"))
    parser.add_argument("--folder", type=Path, required=True)
    parser.add_argument("--git", type=Path, required=True)
    parser.add_argument("--sha", required=True)
    parser.add_argument("--run", required=True)
    parser.add_argument("--attempt", required=True)
    args = parser.parse_args()
    result = qualify(ROOT, args.folder.resolve(), args.git, args.sha,
                     args.run, args.attempt, args.mode == "capture")
    print(json.dumps({key: result[key] for key in (
        "sourceSha", "treeSha", "runId", "attempt", "reportSha256", "passedTests")}))


if __name__ == "__main__":
    main()
