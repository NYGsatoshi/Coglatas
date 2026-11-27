#!/usr/bin/env python3
"""Isolate the effect of non-documentation XML AdditionalFiles on a clean build.

Diagnostic only: never supplies binaries or coverage to authenticated analysis.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
PROJECTS = {
    "Coglatas.Web": "src/Coglatas.Web",
    "Coglatas.Tests": "tests/Coglatas.Tests",
}
GENERATOR = "OpenApiXmlCommentSupport.generated.cs"
FIXTURE_PREFIX = "sonar-openapi-input-probe-"
COUNTS = (0, 1, 2)


def digest(path: Path) -> str:
    if path.is_symlink() or not path.is_file() or path.stat().st_size == 0:
        raise ValueError("Missing or linked native evidence: " + str(path))
    return hashlib.sha256(path.read_bytes()).hexdigest()


def identity() -> dict:
    sha = os.environ["PROOF_SOURCE_SHA"]
    run = os.environ["GITHUB_RUN_ID"]
    attempt = os.environ["GITHUB_RUN_ATTEMPT"]
    if (not re.fullmatch(r"[0-9a-f]{40}", sha)
            or not all(re.fullmatch(r"[1-9][0-9]*", value) for value in (run, attempt))
            or str(ROOT) != os.environ["GITHUB_WORKSPACE"]
            or subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT,
                                       text=True).strip() != sha):
        raise ValueError("Unexpected diagnostic revision or execution")
    subprocess.run(["git", "diff", "--quiet", "HEAD", "--"], cwd=ROOT, check=True)
    tree = subprocess.check_output(["git", "rev-parse", "HEAD^{tree}"],
                                   cwd=ROOT, text=True).strip()
    return {"sourceSha": sha, "treeSha": tree, "runId": run,
            "attempt": attempt, "workspace": str(ROOT)}


def write_fixture(folder: Path, count: int) -> Path:
    if type(count) is not int or count not in COUNTS:
        raise ValueError("Invalid XML input count")
    folder.mkdir(parents=True, exist_ok=False)
    project = ET.Element("Project")
    target = ET.SubElement(project, "Target", {
        "Name": "SonarOpenApiInputReproduction", "BeforeTargets": "CoreCompile",
        "Condition": "'$(MSBuildProjectName)' == 'Coglatas.Web' Or '$(MSBuildProjectName)' == 'Coglatas.Tests'",
    })
    items = ET.SubElement(target, "ItemGroup")
    for index in range(count):
        name = FIXTURE_PREFIX + str(index + 1) + ".xml"
        (folder / name).write_text("<AnalysisInput><Settings /></AnalysisInput>\n", encoding="utf-8")
        ET.SubElement(items, "AdditionalFiles", {
            "Include": "$(MSBuildThisFileDirectory)" + name,
        })
    # Record basenames only, not file contents, environment or MSBuild command lines.
    ET.SubElement(target, "WriteLinesToFile", {
        "File": "$(IntermediateOutputPath)sonar-openapi-additional-files.txt",
        "Lines": "PROBE;@(AdditionalFiles->'%(Filename)%(Extension)')",
        "Overwrite": "true",
    })
    path = folder / "probe.targets"
    ET.ElementTree(project).write(path, encoding="utf-8", xml_declaration=True)
    return path


def find_one(root: Path, name: str) -> Path:
    paths = list(root.rglob(name))
    if len(paths) != 1 or paths[0].is_symlink():
        raise ValueError("Unexpected generated file inventory: " + name)
    return paths[0]


def manifest(folder: Path) -> dict:
    result = {}
    for path in sorted(folder.rglob("*")):
        if path.is_symlink():
            raise ValueError("Linked evidence")
        if path.is_file() and path.name != "receipt.json":
            result[path.relative_to(folder).as_posix()] = digest(path)
    return result


def collect() -> None:
    info = identity()
    count = int(os.environ["PROOF_XML_COUNT"])
    fixture = write_fixture(Path(os.environ["RUNNER_TEMP"]) / "sonar-openapi-fixture", count)
    subprocess.run(["dotnet", "build", "Coglatas.slnx", "--configuration", "Release",
                    "--no-restore", "--disable-build-servers", "-m",
                    "-p:ContinuousIntegrationBuild=true", "-p:DeterministicSourcePaths=true",
                    "-p:EmitCompilerGeneratedFiles=true",
                    "-p:CustomAfterMicrosoftCommonTargets=" + str(fixture)],
                   cwd=ROOT, check=True)
    if identity() != info:
        raise ValueError("Source changed during diagnostic build")
    folder = ROOT / "artifacts/sonar-openapi-reproduction"
    folder.mkdir(parents=True, exist_ok=False)
    spec = importlib.util.spec_from_file_location(
        "native", ROOT / "scripts/ci/verify-sonar-windows-coverage.py")
    verifier = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(verifier)
    assemblies = verifier.assemblies(ROOT, folder, info["sourceSha"], True)
    for name, project in PROJECTS.items():
        output = ROOT / project / "obj/Release/net11.0"
        generated = find_one(output, GENERATOR)
        inventory = find_one(output, "sonar-openapi-additional-files.txt")
        dest = folder / name
        dest.mkdir()
        shutil.copyfile(generated, dest / GENERATOR)
        shutil.copyfile(inventory, dest / "inputs.txt")
        if fixture_count(dest / "inputs.txt") != count:
            raise ValueError("Injected XML count differs from MSBuild input inventory")
    mapping = ROOT / "tests/Coglatas.Tests/bin/Release/net11.0/CoverletSourceRootsMapping_Coglatas.Tests"
    digest(mapping)
    shutil.copyfile(mapping, folder / "mapping.original")
    info.update(xmlCount=count, assemblies=assemblies, hashes=manifest(folder))
    (folder / "receipt.json").write_text(json.dumps(info, indent=2, sort_keys=True) + "\n")


def fixture_count(path: Path) -> int:
    lines = path.read_text(encoding="utf-8-sig").splitlines()
    if not lines or lines[0] != "PROBE":
        raise ValueError("Missing MSBuild input receipt")
    names = [line for line in lines[1:] if line.startswith(FIXTURE_PREFIX)]
    if names != [FIXTURE_PREFIX + str(i + 1) + ".xml" for i in range(len(names))]:
        raise ValueError("Unexpected fixture XML identities")
    return len(names)


def blank_line_delta(before: bytes, after: bytes) -> bool:
    """Historical diagnostic: exactly one extra newline at the cache return.

    This predicate never normalizes, edits or admits a DLL/PDB for publication.
    """
    if len(after) != len(before) + 1:
        return False
    marker = b"            return cache;"
    if before.count(marker) != 1:
        raise ValueError("Unexpected generated cache layout")
    pos = before.index(marker)
    return after == before[:pos] + b"\n" + before[pos:]


def compare() -> None:
    expected = identity()
    root = ROOT / "artifacts/sonar-openapi-inputs"
    names = {count: "sonar-openapi-reproduction-" + str(count) + "-" + expected["attempt"]
             for count in COUNTS}
    if {path.name for path in root.iterdir()} != set(names.values()):
        raise ValueError("Unexpected diagnostic artifact inventory")
    folders, records = {}, {}
    for count in COUNTS:
        folder = root / names[count]
        if folder.is_symlink():
            raise ValueError("Linked diagnostic artifact")
        digest(folder / "receipt.json")
        record = json.loads((folder / "receipt.json").read_bytes())
        if (any(record.get(key) != value for key, value in expected.items())
                or record.get("xmlCount") != count or record.get("hashes") != manifest(folder)
                or any(fixture_count(folder / name / "inputs.txt") != count for name in PROJECTS)
                or not record.get("assemblies")
                or any(record["hashes"].get(path) != value for path, value in record["assemblies"].items())):
            raise ValueError("Invalid native build receipt")
        folders[count], records[count] = folder, record
    baseline = records[0]
    result = {}
    for count in (1, 2):
        current = records[count]
        if set(current["assemblies"]) != set(baseline["assemblies"]):
            raise ValueError("Assembly inventory changed")
        changed = sorted(name for name in baseline["assemblies"]
                         if baseline["assemblies"][name] != current["assemblies"][name])
        files = {}
        for project in PROJECTS:
            before = (folders[0] / project / GENERATOR).read_bytes()
            after = (folders[count] / project / GENERATOR).read_bytes()
            files[project] = {"beforeSha256": hashlib.sha256(before).hexdigest(),
                              "afterSha256": hashlib.sha256(after).hexdigest(),
                              "byteDelta": len(after) - len(before),
                              "oneBlankLineAtCacheReturn": blank_line_delta(before, after)}
        result[str(count)] = {"changedAssemblyFiles": changed, "generated": files,
                              "mappingUnchanged": baseline["hashes"]["mapping.original"]
                              == current["hashes"]["mapping.original"]}
    reproduced = [count for count, row in result.items()
                  if row["mappingUnchanged"] and all(v["oneBlankLineAtCacheReturn"]
                                                     for v in row["generated"].values())
                  and "assemblies/Coglatas.Web.dll" in row["changedAssemblyFiles"]
                  and "assemblies/Coglatas.Web.pdb" in row["changedAssemblyFiles"]]
    # A negative reproduction is also diagnostic evidence, not a qualification pass.
    receipt = dict(expected, scope="DIAGNOSTIC_ONLY_NO_COVERAGE_ADMISSION",
                   historicalPatternReproduced=bool(reproduced),
                   matchingFixtureCounts=reproduced, observations=result)
    output = ROOT / "artifacts/sonar-openapi-reproduction-result.json"
    output.write_text(json.dumps(receipt, sort_keys=True, indent=2) + "\n")
    print(json.dumps(receipt, sort_keys=True))


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=("collect", "compare"))
    args = parser.parse_args()
    if args.command == "collect":
        collect()
    else:
        compare()


if __name__ == "__main__":
    main()
