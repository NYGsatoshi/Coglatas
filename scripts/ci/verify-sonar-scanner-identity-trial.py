#!/usr/bin/env python3
"""Compare real scanner compilation against a same-SHA diagnostic build.

No coverage admission, scanner configuration export, or report rewriting.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import os
from pathlib import Path
import re
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
FOLDER = ROOT / "artifacts/sonar-openapi-reproduction"
KNOWN_XML = ("sonarlint.xml", "sonarprojectconfig.xml")
INVENTORY_NAME = "sonar-scanner-additional-files.txt"


def load(name: str):
    spec = importlib.util.spec_from_file_location(name, ROOT / "scripts/ci" / (name + ".py"))
    if spec is None or spec.loader is None:
        raise ValueError("Required native diagnostic module unavailable")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def reproduction():
    return load("verify-sonar-openapi-reproduction")


def native():
    return load("verify-sonar-windows-coverage")


def producer_receipt(folder: Path, identity: dict, expected_digest: str) -> dict:
    helper = reproduction()
    verifier = native()
    if folder.is_symlink() or not re.fullmatch(r"[a-f0-9]{64}", expected_digest):
        raise ValueError("Invalid diagnostic producer identity")
    receipt = folder / "receipt.json"
    if helper.digest(receipt) != expected_digest:
        raise ValueError("Diagnostic producer receipt digest differs")
    record = json.loads(receipt.read_bytes())
    actual = helper.manifest(folder)
    keys = {"assemblies/" + name + "." + ext
            for name in verifier.ASSEMBLIES for ext in ("dll", "pdb")}
    expected_files = keys | {"mapping.original"} | {
        name + "/" + file for name in helper.PROJECTS
        for file in (helper.GENERATOR, "inputs.txt")}
    if (any(record.get(key) != value for key, value in identity.items())
            or record.get("xmlCount") != 2
            or set(actual) != expected_files
            or record.get("hashes") != actual
            or record.get("assemblies") != {key: actual[key] for key in keys}
            or any(helper.fixture_count(folder / name / "inputs.txt") != 2
                   for name in helper.PROJECTS)):
        raise ValueError("Diagnostic producer evidence differs")
    return record


def write_observer(folder: Path) -> Path:
    """Observe the final AdditionalFiles list without modifying compiler inputs."""
    folder.mkdir(parents=True, exist_ok=False)
    project = ET.Element("Project")
    target = ET.SubElement(project, "Target", {
        "Name": "SonarScannerNativeInputObserver",
        "AfterTargets": "CoreCompile",
        "Condition": "'$(MSBuildProjectName)' == 'Coglatas.Web' Or '$(MSBuildProjectName)' == 'Coglatas.Tests'",
    })
    ET.SubElement(target, "WriteLinesToFile", {
        "File": "$(IntermediateOutputPath)" + INVENTORY_NAME,
        "Lines": "PROBE;SCANNER=$(SonarQubeTargetsImported);@(AdditionalFiles->'%(Filename)%(Extension)')",
        "Overwrite": "true",
    })
    path = folder / "observer.targets"
    ET.ElementTree(project).write(path, encoding="utf-8", xml_declaration=True)
    return path


def summarize_inputs(raw: str) -> dict:
    """Export only known compiler input categories and numeric counts."""
    lines = raw.splitlines()
    if len(lines) < 2 or lines[0] != "PROBE" or lines[1].lower() != "scanner=true":
        raise ValueError("Actual scanner target import was not observed")
    names = [name.lower() for name in lines[2:] if name]
    if any("/" in name or "\\" in name or "\0" in name for name in names):
        raise ValueError("Unexpected non-basename compiler input")
    known = {name: names.count(name) for name in KNOWN_XML}
    xml_count = sum(name.endswith(".xml") for name in names)
    return {"scannerTargetsImported": True, "additionalFileCount": len(names),
            "xmlCount": xml_count, "knownSonarXmlCounts": known,
            "otherXmlCount": xml_count - sum(known.values()),
            "nonXmlCount": len(names) - xml_count}


def compare_digests(expected: dict, actual: dict) -> list[str]:
    if not expected or set(expected) != set(actual):
        raise ValueError("Compiler assembly inventory differs")
    return sorted(key for key in expected if expected[key] != actual[key])


def collect() -> None:
    if os.environ.get("PROOF_XML_COUNT") != "2":
        raise ValueError("Expected qualified two-XML diagnostic fixture")
    reproduction().collect()


def bind() -> None:
    helper = reproduction()
    digest = helper.digest(FOLDER / "receipt.json")
    producer_receipt(FOLDER, helper.identity(), digest)
    with Path(os.environ["GITHUB_OUTPUT"]).open("a", encoding="utf-8") as stream:
        stream.write("digest=" + digest + "\n")


def prepare() -> None:
    helper = reproduction()
    producer_receipt(FOLDER, helper.identity(), os.environ["EXPECTED_RECEIPT_DIGEST"])
    write_observer(Path(os.environ["RUNNER_TEMP"]) / "sonar-scanner-input-observer")


def compare() -> None:
    helper = reproduction()
    info = helper.identity()
    producer = producer_receipt(FOLDER, info, os.environ["EXPECTED_RECEIPT_DIGEST"])
    # Capture actual bytes in a private temporary directory only. The existing
    # scanner-native coverage job retains its normal production evidence later.
    with tempfile.TemporaryDirectory(dir=os.environ["RUNNER_TEMP"]) as directory:
        actual = native().assemblies(ROOT, Path(directory), info["sourceSha"], True)
    changed = compare_digests(producer["assemblies"], actual)
    generated = {}
    for name, project in helper.PROJECTS.items():
        output = ROOT / project / "obj/Release/net11.0"
        source = helper.find_one(output, helper.GENERATOR)
        inventory = helper.find_one(output, INVENTORY_NAME)
        helper.digest(inventory)
        actual_digest = helper.digest(source)
        producer_digest = producer["hashes"][name + "/" + helper.GENERATOR]
        generated[name] = {"producerSha256": producer_digest, "scannerSha256": actual_digest,
                           "identical": producer_digest == actual_digest,
                           "scannerInputs": summarize_inputs(inventory.read_text(encoding="utf-8-sig"))}
    mapping = helper.digest(ROOT / "tests/Coglatas.Tests/bin/Release/net11.0/CoverletSourceRootsMapping_Coglatas.Tests")
    mapping_matches = mapping == producer["hashes"]["mapping.original"]
    identical = not changed and mapping_matches and all(row["identical"] for row in generated.values())
    result = dict(info, scope="DIAGNOSTIC_ONLY_SCANNER_NATIVE_TESTS_REMAIN_REQUIRED",
                  producerReceiptSha256=os.environ["EXPECTED_RECEIPT_DIGEST"],
                  byteIdentical=identical, changedAssemblyFiles=changed,
                  scannerAssemblyDigests=actual, producerAssemblyDigests=producer["assemblies"],
                  mappingIdentical=mapping_matches, generated=generated)
    output = ROOT / "artifacts/sonar-scanner-identity-result.json"
    with output.open("x", encoding="utf-8") as stream:
        json.dump(result, stream, sort_keys=True, indent=2)
        stream.write("\n")
    print(json.dumps(result, sort_keys=True))
    # A negative result is a real diagnostic outcome, not permission to import
    # independent coverage. The existing scanner-built full native suite and
    # all of its publication checks remain mandatory regardless of this value.


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=("collect", "bind", "prepare", "compare"))
    args = parser.parse_args()
    {"collect": collect, "bind": bind, "prepare": prepare, "compare": compare}[args.command]()


if __name__ == "__main__":
    main()
