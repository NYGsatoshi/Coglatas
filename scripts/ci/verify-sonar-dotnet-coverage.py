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


# Qualification-only partitions. The fourth lane is an explicit complement,
# not a list of today's namespaces; new tests cannot silently fall outside it.
SHARD_LANES = ("baseline", "files-db", "other-db", "security-aq", "remaining")
TRX_NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
TEST_ASSEMBLY = "tests/Coglatas.Tests/bin/Release/net11.0/Coglatas.Tests.dll"


def shard_filter(lane: str) -> str | None:
    if lane not in SHARD_LANES:
        raise ValueError("Unknown native coverage proof lane")
    if lane == "baseline":
        return None
    db = "Coglatas.Tests.PostgreSql."
    special = ("Coglatas.Tests.SecurityArchitecture.", "Coglatas.Tests.Aq.")
    files = tuple(db + name for name in ("File", "Project", "Task"))

    def positive(values):
        return "(" + "|".join("FullyQualifiedName~" + name for name in values) + ")"

    def negative(values):
        return "(" + "&".join("FullyQualifiedName!~" + name for name in values) + ")"

    # Earlier predicates are excluded from later lanes; substring matching
    # in VSTest is case-insensitive. Actual TRX union is the admission gate.
    if lane == "files-db":
        return positive((db,)) + "&" + positive(files)
    if lane == "other-db":
        return positive((db,)) + "&" + negative(files)
    if lane == "security-aq":
        return negative((db,)) + "&" + positive(special)
    return negative((db, *special))


def shard_command(lane: str) -> list[str]:
    command = ["dotnet", "test", "tests/Coglatas.Tests/Coglatas.Tests.csproj",
               "--configuration", "Release", "--no-build", "--no-restore",
               "--disable-build-servers", "-m:1", "--collect",
               "XPlat Code Coverage;Format=opencover", "--logger",
               "trx;LogFileName=backend-tests.trx", "--results-directory",
               "artifacts/sonar-dotnet"]
    selection = shard_filter(lane)
    if selection is not None:
        command += ["--filter", selection]
    return command


def scanner_compatible_fixture(root: Path, folder: Path, expected: dict,
                               before: dict, mapping: bytes) -> dict:
    """Bind the proven two-XML fixture to actual pre-rebuild and physical-build evidence.

    This is still a credential-free qualification; actual scanner comparison
    must be performed on each future authenticated ingestion run.
    """
    import importlib.util

    native = _native_verifier()
    spec = importlib.util.spec_from_file_location(
        "scanner_trial", Path(__file__).with_name("verify-sonar-scanner-identity-trial.py"))
    if spec is None or spec.loader is None:
        raise ValueError("Scanner compiler identity verifier is unavailable")
    trial = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(trial)
    relative = folder.relative_to(root).as_posix()
    archive = folder / "compiler-identity"
    receipt = native.owned_bytes(root, relative + "/compiler-identity/receipt.json")
    digest = native.digest(receipt)
    compiler = trial.producer_receipt(archive, expected, digest)
    if (compiler["assemblies"] != before
            or compiler["hashes"]["mapping.original"] != native.digest(mapping)
            or native.owned_bytes(root, relative + "/compiler-identity/mapping.original") != mapping):
        raise ValueError("Scanner-compatible compiler evidence differs from measured initial build")
    physical = native.owned_bytes(root, relative + "/physical-compiler-inputs.txt")
    source_mapping = native.owned_bytes(root, relative + "/physical-test-source-roots.original")
    reproduction = trial.reproduction()
    # The fixture target records basenames only. Require a fresh, exact two-XML
    # observation from the test-only rebuild rather than accepting the earlier
    # deterministic build's observation.
    if (reproduction.fixture_count(folder / "physical-compiler-inputs.txt") != 2
            or not source_mapping):
        raise ValueError("Physical test rebuild compiler fixture or mapping differs")
    return {
        "fixtureXmlCount": 2,
        "compilerReceiptSha256": digest,
        "physicalCompilerInputsSha256": native.digest(physical),
        "physicalSourceMappingSha256": native.digest(source_mapping),
        "generatedSourceDigests": {
            name: compiler["hashes"][name + "/" + reproduction.GENERATOR]
            for name in reproduction.PROJECTS},
    }


def run_shard_proof(lane: str) -> None:
    import os
    import time

    sha = os.environ["PROOF_SOURCE_SHA"]
    run = os.environ["GITHUB_RUN_ID"]
    attempt = os.environ["GITHUB_RUN_ATTEMPT"]
    native = _native_verifier()
    tree, _ = native.git_reader(ROOT, Path("/usr/bin/git"), sha)
    if not all(re.fullmatch(r"[1-9][0-9]*", value) for value in (run, attempt)):
        raise ValueError("Invalid proof run identity")
    if os.environ["GITHUB_WORKSPACE"] != str(ROOT):
        raise ValueError("Unexpected native proof checkout root")
    folder = ROOT / "artifacts/sonar-dotnet"
    receipt = folder / "shard-lane.json"
    if receipt.exists() or (folder / "backend-tests.trx").exists():
        raise ValueError("Proof must start without prior execution evidence")
    identity = {"sourceSha": sha, "treeSha": tree, "runId": run,
                "attempt": attempt, "workspace": str(ROOT)}
    before = json.loads((folder / "pre-test-rebuild/outputs.json").read_bytes())
    mapping = native.owned_bytes(folder, "deterministic-source-roots.original")
    fixture = scanner_compatible_fixture(ROOT, folder, identity, before["artifacts"], mapping)
    start = time.monotonic()
    subprocess.run(shard_command(lane), cwd=ROOT, check=True)
    elapsed = round(time.monotonic() - start, 3)
    if scanner_compatible_fixture(ROOT, folder, identity, before["artifacts"], mapping) != fixture:
        raise ValueError("Compiler evidence changed during native coverage")
    value = {**identity, "lane": lane, "filter": shard_filter(lane),
             "testSeconds": elapsed, "compilerFixture": fixture}
    with receipt.open("x", encoding="utf-8") as stream:
        json.dump(value, stream, indent=2, sort_keys=True)
        stream.write("\n")


def trx_identities(raw: bytes, root: Path):
    from collections import Counter

    native = _native_verifier()
    total = native.verify_trx(raw, str(root / TEST_ASSEMBLY))
    document = native.parse_xml(raw)
    summary = document.find("./t:ResultSummary", TRX_NS)
    if summary is None or summary.get("outcome") not in {"Completed", "Passed"}:
        raise ValueError("Incomplete test result summary")
    counters = summary.find("t:Counters", TRX_NS)
    if counters is None or any(int(value) for key, value in counters.attrib.items()
                               if key not in {"total", "executed", "passed"}):
        raise ValueError("Non-passing TRX accounting")
    methods = {}
    for definition in document.findall("./t:TestDefinitions/t:UnitTest", TRX_NS):
        method = definition.find("t:TestMethod", TRX_NS)
        identity = definition.get("id")
        if (not identity or identity in methods or method is None
                or not method.get("className") or not method.get("name")):
            raise ValueError("Missing or duplicated TRX test definition")
        methods[identity] = (method.attrib["className"], method.attrib["name"])
    result = Counter()
    for case in document.findall("./t:Results/t:UnitTestResult", TRX_NS):
        if not case.get("testName") or case.get("testId") not in methods:
            raise ValueError("Missing executed test identity")
        result[(*methods[case.attrib["testId"]], case.attrib["testName"])] += 1
    if sum(result.values()) != total:
        raise ValueError("Test identity inventory is incomplete")
    return result


def native_source_points(raw: bytes, root: Path, tracked: set[str]) -> dict:
    """Compare original OpenCover point identities, never rewrite XML or counts."""
    native = _native_verifier()
    document = native.parse_xml(raw)
    if document.tag != "CoverageSession":
        raise ValueError("Expected native OpenCover report")
    points = {}
    for module in document.findall("./Modules/Module"):
        module_name = module.findtext("ModuleName")
        files = {}
        for item in module.findall("./Files/File"):
            original = item.attrib["fullPath"]
            path = (root / original[3:] if original.startswith("/_/")
                    else root / original).resolve()
            relative = path.relative_to(root.resolve()).as_posix()
            uid = item.attrib["uid"]
            if uid in files:
                raise ValueError("Duplicate OpenCover file identity")
            if relative in tracked:
                if not path.is_file():
                    raise ValueError("Missing tracked OpenCover source")
                files[uid] = relative
            else:
                # Original report may include compiler-generated obj sources;
                # retain the bytes, but never credit untracked generated code.
                parts = Path(relative).parts
                if parts[0] in {"src", "tools"} and not {"bin", "obj"}.intersection(parts):
                    raise ValueError("Untracked production coverage source")
                files[uid] = None
        for cls in module.findall("./Classes/Class"):
            class_name = cls.findtext("FullName")
            for method in cls.findall("./Methods/Method"):
                method_name = method.findtext("Name")
                file_ref = method.find("FileRef")
                for tag, attrs in (
                    # Coverlet 10.1.1 emits ordinal/uspid and source spans for
                    # sequence points, not IL offset. Branch points retain both
                    # IL offsets and their path/ordinal. Compare native identities.
                    ("SequencePoint", ("sl", "sc", "el", "ec", "ordinal", "uspid")),
                    ("BranchPoint", ("sl", "offset", "offsetend", "path", "ordinal")),
                ):
                    for point in method.findall("./" + tag + "s/" + tag):
                        uid = point.get("fileid") or (
                            file_ref.get("uid") if file_ref is not None else None)
                        if uid not in files:
                            raise ValueError("Unmapped native point file identity")
                        source = files[uid]
                        if source is None:
                            continue
                        visits = int(point.attrib["vc"])
                        try:
                            coordinates = tuple(int(point.attrib[key]) for key in attrs)
                        except (KeyError, ValueError) as error:
                            raise ValueError("Missing or invalid native " + tag
                                             + " identity attribute") from error
                        if (not module_name or not class_name or not method_name
                                or visits < 0 or coordinates[0] < 1
                                or any(value < 0 for value in coordinates)):
                            raise ValueError("Invalid native point identity")
                        key = (tag, module_name, class_name, method_name, source,
                               *coordinates)
                        if key in points:
                            raise ValueError("Duplicate native point identity: " + repr(key))
                        points[key] = visits > 0
    if not points or not any(points.values()):
        raise ValueError("No tracked native coverage points")
    return points


def require_shard_equivalence(baseline_tests, baseline_points, shards) -> dict:
    from collections import Counter

    if set(shards) != set(SHARD_LANES[1:]):
        raise ValueError("Exactly four proof shards are required")
    union_tests = Counter()
    union_points = {}
    for lane in SHARD_LANES[1:]:
        tests, points = shards[lane]
        if not tests or not points:
            raise ValueError("Empty shard: " + lane)
        if set(union_tests).intersection(tests):
            raise ValueError("Shard test identities overlap: " + lane)
        union_tests.update(tests)
        for key, hit in points.items():
            union_points[key] = union_points.get(key, False) or hit
    if union_tests != baseline_tests:
        raise ValueError("Shard test identities or multiplicities differ: "
                         + str(sum((baseline_tests - union_tests).values()))
                         + " missing, " + str(sum((union_tests - baseline_tests).values()))
                         + " unexpected")
    if union_points != baseline_points:
        missing = len(set(baseline_points) - set(union_points))
        extra = len(set(union_points) - set(baseline_points))
        changed = [key for key in set(baseline_points).intersection(union_points)
                   if baseline_points[key] != union_points[key]]
        # Bounded source-only diagnostics, not raw application/test data.
        examples = sorted(changed)[:10]
        print(json.dumps({"changedHitExamples": [
            {"point": list(key), "baseline": baseline_points[key], "shards": union_points[key]}
            for key in examples]}, sort_keys=True))
        raise ValueError(f"Native source coverage differs: {missing} missing, "
                         f"{extra} extra, {len(changed)} changed hit states")
    result = {"passedTests": sum(union_tests.values())}
    for tag, label in (("SequencePoint", "sequence"), ("BranchPoint", "branch")):
        subset = {key: value for key, value in union_points.items() if key[0] == tag}
        if not subset:
            raise ValueError("No native " + label + " points to compare")
        result[label + "Points"] = len(subset)
        result[label + "Visited"] = sum(subset.values())
    return result


def compare_shard_proof() -> None:
    import os

    sha = os.environ["PROOF_SOURCE_SHA"]
    run = os.environ["GITHUB_RUN_ID"]
    attempt = os.environ["GITHUB_RUN_ATTEMPT"]
    native = _native_verifier()
    tree, _ = native.git_reader(ROOT, Path("/usr/bin/git"), sha)
    if os.environ["GITHUB_WORKSPACE"] != str(ROOT):
        raise ValueError("Unexpected scanner checkout root")
    subprocess.run(["/usr/bin/git", "diff", "--quiet", "HEAD", "--", "src", "tools"],
                   cwd=ROOT, check=True)
    tracked = {value.decode("utf-8") for value in subprocess.check_output(
        ["/usr/bin/git", "ls-files", "-z", "--", "src", "tools"], cwd=ROOT).split(b"\0")
        if value.endswith(b".cs")}
    directory = ROOT / "artifacts/sonar-dotnet-shard-proof"
    names = {lane: "sonar-dotnet-shard-proof-" + lane + "-" + attempt for lane in SHARD_LANES}
    if {path.name for path in directory.iterdir()} != set(names.values()):
        raise ValueError("Missing, unexpected or stale proof artifacts")
    inventories, lane_receipts = {}, {}
    baseline_binaries = None
    for lane in SHARD_LANES:
        folder = directory / names[lane]
        rel = folder.relative_to(ROOT).as_posix()
        def read(name):
            return native.owned_bytes(ROOT, rel + "/" + name)
        verify_parallel_handoff(ROOT, folder, sha, run, attempt)
        receipt = json.loads(read("shard-lane.json"))
        expected = {"lane": lane, "filter": shard_filter(lane), "sourceSha": sha,
                    "treeSha": tree, "runId": run, "attempt": attempt, "workspace": str(ROOT)}
        if any(receipt.get(key) != value for key, value in expected.items()):
            raise ValueError("Proof lane execution identity mismatch")
        elapsed = receipt.get("testSeconds")
        if (type(elapsed) not in (float, int) or not 0 < elapsed < 4500):
            raise ValueError("Invalid proof duration")
        report, copies = select_collector_report(folder)
        raw = read(report.relative_to(folder).as_posix())
        if raw != read("verified.opencover.xml"):
            raise ValueError("Native collector bytes differ from published proof bytes")
        measured = json.loads(read("receipt.json"))
        if (measured.get("identicalVSTestCopies") != copies or
                measured.get("collectorReport") != "artifacts/sonar-dotnet/" +
                report.relative_to(folder).as_posix()):
            raise ValueError("Native collector identity differs")
        before = json.loads(read("pre-test-rebuild/outputs.json"))
        executed = json.loads(read("binary-provenance.json"))
        actual_before = native.assemblies(ROOT, folder / "pre-test-rebuild", sha, False)
        actual_after = native.assemblies(ROOT, folder / "executed-binaries", sha, False)
        if actual_before != before["artifacts"] or actual_after != executed["executedArtifacts"]:
            raise ValueError("Retained native binaries differ from receipts")
        mapping = read("deterministic-source-roots.original")
        fixture = scanner_compatible_fixture(
            ROOT, folder,
            {key: expected[key] for key in ("sourceSha", "treeSha", "runId", "attempt", "workspace")},
            actual_before, mapping)
        if receipt.get("compilerFixture") != fixture:
            raise ValueError("Compiler fixture differs from native execution receipt")
        identity = (actual_before, actual_after, mapping,
                    fixture["physicalSourceMappingSha256"],
                    fixture["physicalCompilerInputsSha256"],
                    fixture["generatedSourceDigests"])
        if baseline_binaries is None:
            baseline_binaries = identity
        elif identity != baseline_binaries:
            raise ValueError("Shard and baseline binaries or mapping differ")
        tests = trx_identities(read("backend-tests.trx"), ROOT)
        points = native_source_points(raw, ROOT, tracked)
        inventories[lane] = (tests, points)
        lane_receipts[lane] = {"tests": sum(tests.values()), "testSeconds": elapsed,
                               "reportSha256": native.digest(raw),
                               "trxSha256": native.digest(read("backend-tests.trx")),
                               "compilerFixture": fixture}
    baseline_tests, baseline_points = inventories.pop("baseline")
    result = require_shard_equivalence(baseline_tests, baseline_points, inventories)
    result.update(sourceSha=sha, treeSha=tree, runId=run, attempt=attempt,
                  scope="NATIVE_SHARD_EQUIVALENCE_ONLY_NOT_SONARCLOUD_OR_RELEASE",
                  scannerCompatibleFixtureXmlCount=2,
                  preRebuildAssemblyDigests=baseline_binaries[0],
                  executedAssemblyDigests=baseline_binaries[1],
                  deterministicSourceMappingSha256=native.digest(baseline_binaries[2]),
                  physicalSourceMappingSha256=baseline_binaries[3],
                  physicalCompilerInputsSha256=baseline_binaries[4],
                  generatedSourceDigests=baseline_binaries[5],
                  lanes=lane_receipts)
    output = ROOT / "artifacts/sonar-dotnet-shard-equivalence.json"
    with output.open("x", encoding="utf-8") as stream:
        json.dump(result, stream, indent=2, sort_keys=True)
        stream.write("\n")
    print(json.dumps(result, sort_keys=True))


DOTNET_SHARD_INPUTS = "artifacts/sonar-dotnet-shard-inputs"
DOTNET_SHARD_SCOPE = "NATIVE_SHARD_EQUIVALENCE_ONLY_NOT_SONARCLOUD_OR_RELEASE"
DOTNET_METRICS = ("passedTests", "sequencePoints", "sequenceVisited",
                  "branchPoints", "branchVisited")


def shard_trial_identity(root: Path) -> dict:
    import os

    sha, run, attempt = (os.environ[key] for key in
                         ("PROOF_SOURCE_SHA", "GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT"))
    if (not re.fullmatch(r"[a-f0-9]{40}", sha)
            or not all(re.fullmatch(r"[1-9][0-9]*", value) for value in (run, attempt))
            or os.environ["GITHUB_WORKSPACE"] != str(root)):
        raise ValueError("Invalid native shard handoff execution")
    tree, _ = _native_verifier().git_reader(root, Path("/usr/bin/git"), sha)
    subprocess.run(["/usr/bin/git", "diff", "--quiet", "HEAD", "--"], cwd=root, check=True)
    return dict(sourceSha=sha, treeSha=tree, runId=run, attempt=attempt, workspace=str(root))


def validate_shard_trial_receipt(record: dict, identity: dict) -> None:
    native = _native_verifier()
    if (any(record.get(key) != identity[key] for key in
            ("sourceSha", "treeSha", "runId", "attempt"))
            or record.get("scope") != DOTNET_SHARD_SCOPE
            or record.get("scannerCompatibleFixtureXmlCount") != 2):
        raise ValueError("Native shard comparison identity differs")
    expected_keys = {"assemblies/" + name + "." + ext
                     for name in native.ASSEMBLIES for ext in ("dll", "pdb")}

    def digest(value):
        return isinstance(value, str) and re.fullmatch(r"[a-f0-9]{64}", value) is not None

    for key in ("preRebuildAssemblyDigests", "executedAssemblyDigests"):
        values = record.get(key)
        if not isinstance(values, dict) or set(values) != expected_keys or not all(
                digest(value) for value in values.values()):
            raise ValueError("Invalid native shard binary inventory")
    for key in expected_keys:
        if key not in {"assemblies/Coglatas.Tests.dll", "assemblies/Coglatas.Tests.pdb"} and (
                record["preRebuildAssemblyDigests"][key] != record["executedAssemblyDigests"][key]):
            raise ValueError("Production shard binaries changed")
    generated = record.get("generatedSourceDigests")
    if (not isinstance(generated, dict) or set(generated) != {"Coglatas.Web", "Coglatas.Tests"}
            or not all(digest(value) for value in generated.values())
            or not all(digest(record.get(key)) for key in (
                "deterministicSourceMappingSha256", "physicalSourceMappingSha256",
                "physicalCompilerInputsSha256"))):
        raise ValueError("Invalid native shard compiler evidence")
    lanes = record.get("lanes")
    if (not isinstance(lanes, dict) or set(lanes) != set(SHARD_LANES)
            or any(not isinstance(row, dict) or type(row.get("tests")) is not int
                   or row["tests"] <= 0 or not digest(row.get("reportSha256"))
                   or not digest(row.get("trxSha256")) for row in lanes.values())):
        raise ValueError("Invalid native shard execution inventory")
    if (any(type(record.get(key)) is not int or record[key] <= 0 for key in DOTNET_METRICS)
            or record["sequenceVisited"] > record["sequencePoints"]
            or record["branchVisited"] > record["branchPoints"]
            or record["passedTests"] != lanes["baseline"]["tests"]
            or record["passedTests"] != sum(lanes[lane]["tests"] for lane in SHARD_LANES[1:])):
        raise ValueError("Invalid native shard comparison counters")


def shard_handoff_digest(root: Path, folder: Path, identity: dict) -> tuple[dict, str]:
    native = _native_verifier()
    relative = folder.relative_to(root).as_posix()
    if folder.is_symlink():
        raise ValueError("Linked native shard handoff")
    expected_files = {"receipt.json"} | {
        lane + "/" + name for lane in SHARD_LANES
        for name in ("verified.opencover.xml", "backend-tests.trx")}
    actual_files = set()
    actual_dirs = set()
    for path in folder.rglob("*"):
        if path.is_symlink():
            raise ValueError("Linked native shard input")
        if path.is_file():
            actual_files.add(path.relative_to(folder).as_posix())
        elif path.is_dir():
            actual_dirs.add(path.relative_to(folder).as_posix())
        else:
            raise ValueError("Unexpected native shard input type")
    if actual_files != expected_files or actual_dirs != set(SHARD_LANES):
        raise ValueError("Unexpected native shard handoff inventory")
    receipt = json.loads(native.owned_bytes(root, relative + "/receipt.json"))
    validate_shard_trial_receipt(receipt, identity)
    hashes = {}
    for name in sorted(expected_files):
        raw = native.owned_bytes(root, relative + "/" + name)
        if not raw:
            raise ValueError("Empty native shard input")
        hashes[name] = native.digest(raw)
        if name != "receipt.json":
            lane, kind = name.split("/")
            key = "reportSha256" if kind == "verified.opencover.xml" else "trxSha256"
            if hashes[name] != receipt["lanes"][lane][key]:
                raise ValueError("Native shard input differs from comparison receipt")
    # This is a digest of exact original bytes and execution identity, not a
    # synthetic coverage report. A job output independently binds it.
    binding = json.dumps({"identity": identity, "hashes": hashes},
                         sort_keys=True, separators=(",", ":")).encode("utf-8")
    return receipt, native.digest(binding)


def stage_shard_handoff(root: Path, identity: dict) -> str:
    native = _native_verifier()
    raw = native.owned_bytes(root, "artifacts/sonar-dotnet-shard-equivalence.json")
    receipt = json.loads(raw)
    validate_shard_trial_receipt(receipt, identity)
    folder = root / DOTNET_SHARD_INPUTS
    folder.mkdir(parents=True, exist_ok=False)
    (folder / "receipt.json").write_bytes(raw)
    # Include the original independent baseline for semantic verification only.
    # Scanner reportPaths still explicitly import the four shard files, not this baseline.
    for lane in SHARD_LANES:
        prefix = ("artifacts/sonar-dotnet-shard-proof/sonar-dotnet-shard-proof-"
                  + lane + "-" + identity["attempt"])
        destination = folder / lane
        destination.mkdir()
        for name in ("verified.opencover.xml", "backend-tests.trx"):
            (destination / name).write_bytes(native.owned_bytes(root, prefix + "/" + name))
    _, digest = shard_handoff_digest(root, folder, identity)
    return digest


def verify_shard_handoff(root: Path, identity: dict, expected_digest: str) -> dict:
    if not re.fullmatch(r"[a-f0-9]{64}", expected_digest):
        raise ValueError("Missing independent native shard handoff digest")
    receipt, digest = shard_handoff_digest(root, root / DOTNET_SHARD_INPUTS, identity)
    if digest != expected_digest:
        raise ValueError("Native shard handoff digest differs")
    return receipt


def require_actual_scanner_identity(proof: dict, scanner: dict, identity: dict) -> None:
    if (any(scanner.get(key) != value for key, value in identity.items())
            or scanner.get("scope") != "DIAGNOSTIC_ONLY_SCANNER_NATIVE_TESTS_REMAIN_REQUIRED"
            or scanner.get("byteIdentical") is not True
            or scanner.get("mappingIdentical") is not True
            or scanner.get("changedAssemblyFiles") != []
            or scanner.get("scannerAssemblyDigests") != proof["preRebuildAssemblyDigests"]
            or scanner.get("producerAssemblyDigests") != proof["preRebuildAssemblyDigests"]):
        raise ValueError("Actual scanner initial build differs from qualified shards")
    generated = scanner.get("generated")
    if not isinstance(generated, dict) or set(generated) != set(proof["generatedSourceDigests"]):
        raise ValueError("Actual scanner generated source inventory differs")
    expected_inputs = {"scannerTargetsImported": True, "additionalFileCount": 2,
                       "xmlCount": 2, "knownSonarXmlCounts": {
                           "sonarlint.xml": 1, "sonarprojectconfig.xml": 1},
                       "otherXmlCount": 0, "nonXmlCount": 0}
    for name, digest in proof["generatedSourceDigests"].items():
        row = generated[name]
        if (row.get("identical") is not True or row.get("scannerSha256") != digest
                or row.get("producerSha256") != digest or row.get("scannerInputs") != expected_inputs):
            raise ValueError("Actual scanner compiler input or generated bytes differ")


def read_admission_inventories(root: Path, proof: dict, tracked: set[str], lanes) -> dict:
    native = _native_verifier()
    inventories = {}
    for lane in lanes:
        prefix = DOTNET_SHARD_INPUTS + "/" + lane + "/"
        trx = native.owned_bytes(root, prefix + "backend-tests.trx")
        report = native.owned_bytes(root, prefix + "verified.opencover.xml")
        tests = trx_identities(trx, root)
        if sum(tests.values()) != proof["lanes"][lane]["tests"]:
            raise ValueError("Native shard TRX counter differs")
        inventories[lane] = (tests, native_source_points(report, root, tracked))
    return inventories


def require_actual_scanner_rebuild(root: Path, identity: dict, proof: dict) -> dict:
    """Verify native independent execution against the actual scanner's rebuilt binaries.

    The independently executed, unfiltered baseline and all four native shards
    still have to agree in this exact run and attempt. This path does not claim
    that the scanner itself executed a second baseline.
    """
    native = _native_verifier()
    folder = root / "artifacts/sonar-dotnet"
    validate_shard_trial_receipt(proof, identity)
    scanner = json.loads(native.owned_bytes(root, "artifacts/sonar-scanner-identity-result.json"))
    require_actual_scanner_identity(proof, scanner, identity)

    def read(name):
        return native.owned_bytes(root, "artifacts/sonar-dotnet/" + name)

    saved = json.loads(read("pre-test-rebuild/outputs.json"))
    before = native.assemblies(root, folder / "pre-test-rebuild", identity["sourceSha"], False)
    mapping = read("deterministic-source-roots.original")
    physical = read("physical-test-source-roots.original")
    live_mapping = native.owned_bytes(root,
        "tests/Coglatas.Tests/bin/Release/net11.0/CoverletSourceRootsMapping_Coglatas.Tests")
    if (saved.get("sourceSha") != identity["sourceSha"] or saved.get("artifacts") != before
            or before != proof["preRebuildAssemblyDigests"]
            or not mapping or not physical or mapping != live_mapping
            or native.digest(mapping) != proof["deterministicSourceMappingSha256"]
            or native.digest(physical) != proof["physicalSourceMappingSha256"]):
        raise ValueError("Actual scanner original binaries or source mappings differ")

    rebuilt = {}
    for name, project in native.ASSEMBLIES.items():
        for ext in ("dll", "pdb"):
            relative = "bin/Release/net11.0/" + name + "." + ext
            data = native.owned_bytes(root, project + "/" + relative)
            copied = native.owned_bytes(root, "tests/Coglatas.Tests/" + relative)
            if not data or data != copied:
                raise ValueError("Actual scanner rebuilt project and test copies differ")
            rebuilt["assemblies/" + name + "." + ext] = native.digest(data)
    if rebuilt != proof["executedAssemblyDigests"]:
        raise ValueError("Actual scanner rebuilt binaries differ from independently executed binaries")

    tracked = {value.decode("utf-8") for value in subprocess.check_output(
        ["/usr/bin/git", "ls-files", "-z", "--", "src", "tools"], cwd=root).split(b"\0")
        if value.endswith(b".cs")}
    inventories = read_admission_inventories(root, proof, tracked, SHARD_LANES)
    baseline_tests, baseline_points = inventories.pop("baseline")
    result = require_shard_equivalence(baseline_tests, baseline_points, inventories)
    if any(result[key] != proof[key] for key in DOTNET_METRICS):
        raise ValueError("Independent baseline comparison counters differ")
    return dict(identity, **result, scope="NATIVE_MULTI_OPENCOVER_REBUILT_IDENTITY_NOT_SCANNER_TEST_EXECUTION",
                byteIdentical=True, mappingIdentical=True, scannerBaselineExecuted=False,
                independentBaselineCompared=True, scannerRebuiltAssemblyDigests=rebuilt,
                independentBaselineReportSha256=proof["lanes"]["baseline"]["reportSha256"],
                independentBaselineTrxSha256=proof["lanes"]["baseline"]["trxSha256"])


def require_actual_scanner_baseline(root: Path, identity: dict, proof: dict) -> dict:
    native = _native_verifier()
    folder = root / "artifacts/sonar-dotnet"
    sha, run, attempt = (identity[key] for key in ("sourceSha", "runId", "attempt"))

    def read(name):
        return native.owned_bytes(root, "artifacts/sonar-dotnet/" + name)

    scanner = json.loads(native.owned_bytes(root, "artifacts/sonar-scanner-identity-result.json"))
    require_actual_scanner_identity(proof, scanner, identity)
    verify_parallel_handoff(root, folder, sha, run, attempt, compare_build=True)
    before = native.assemblies(root, folder / "pre-test-rebuild", sha, False)
    after = native.assemblies(root, folder / "executed-binaries", sha, False)
    if (before != json.loads(read("pre-test-rebuild/outputs.json"))["artifacts"]
            or after != json.loads(read("binary-provenance.json"))["executedArtifacts"]
            or before != proof["preRebuildAssemblyDigests"]
            or after != proof["executedAssemblyDigests"]
            or native.digest(read("deterministic-source-roots.original"))
            != proof["deterministicSourceMappingSha256"]
            or native.digest(read("physical-test-source-roots.original"))
            != proof["physicalSourceMappingSha256"]):
        raise ValueError("Actual scanner pre/post-rebuild binaries or mappings differ")
    # Also bind live project and executed copies, including the test assembly.
    for name, project in native.ASSEMBLIES.items():
        for ext in ("dll", "pdb"):
            relative = "bin/Release/net11.0/" + name + "." + ext
            data = native.owned_bytes(root, project + "/" + relative)
            copied = native.owned_bytes(root, "tests/Coglatas.Tests/" + relative)
            if data != copied or native.digest(data) != after["assemblies/" + name + "." + ext]:
                raise ValueError("Actual scanner executed binary changed")
    original, copies = select_collector_report(folder)
    raw = read(original.relative_to(folder).as_posix())
    receipt = json.loads(read("receipt.json"))
    if (raw != read("verified.opencover.xml")
            or receipt.get("identicalVSTestCopies") != copies
            or receipt.get("collectorReport") != "artifacts/sonar-dotnet/" +
            original.relative_to(folder).as_posix()):
        raise ValueError("Actual scanner native baseline report differs")
    tracked = {value.decode("utf-8") for value in subprocess.check_output(
        ["/usr/bin/git", "ls-files", "-z", "--", "src", "tools"], cwd=root).split(b"\0")
        if value.endswith(b".cs")}
    baseline_tests = trx_identities(read("backend-tests.trx"), root)
    baseline_points = native_source_points(raw, root, tracked)
    shards = read_admission_inventories(root, proof, tracked, SHARD_LANES[1:])
    result = require_shard_equivalence(baseline_tests, baseline_points, shards)
    if any(result[key] != proof[key] for key in DOTNET_METRICS):
        raise ValueError("Actual scanner baseline comparison counters differ")
    return dict(identity, **result, byteIdentical=True, mappingIdentical=True,
                scannerBaselineReportSha256=native.digest(raw),
                scope="NATIVE_MULTI_OPENCOVER_ADMISSION_NOT_SONAR_MEASURE_EQUIVALENCE")


def shard_trial(command: str) -> None:
    import os

    identity = shard_trial_identity(ROOT)
    if command == "shard-stage":
        digest = stage_shard_handoff(ROOT, identity)
        with Path(os.environ["GITHUB_OUTPUT"]).open("a", encoding="utf-8") as stream:
            stream.write("digest=" + digest + "\n")
        return
    expected = os.environ["EXPECTED_DOTNET_SHARD_DIGEST"]
    proof = verify_shard_handoff(ROOT, identity, expected)
    if command in {"shard-admit", "shard-admit-rebuilt"}:
        result = (require_actual_scanner_baseline(ROOT, identity, proof) if command == "shard-admit"
                  else require_actual_scanner_rebuild(ROOT, identity, proof))
        # Recheck the independently bound original inputs immediately before
        # granting publication; never rewrite a report or admit a fallback.
        verify_shard_handoff(ROOT, identity, expected)
        result["handoffDigest"] = expected
        path = ROOT / "artifacts/sonar-dotnet-scanner-admission.json"
        with path.open("x", encoding="utf-8") as stream:
            json.dump(result, stream, sort_keys=True, indent=2)
            stream.write("\n")
        print(json.dumps(result, sort_keys=True))
    else:
        print("Exact native Ubuntu shard handoff verified before scanner credentials")


def cli() -> None:
    import argparse
    import sys

    if len(sys.argv) == 1:
        main()
        return
    parser = argparse.ArgumentParser()
    commands = parser.add_subparsers(dest="command", required=True)
    run = commands.add_parser("shard-run")
    run.add_argument("--lane", required=True, choices=SHARD_LANES)
    commands.add_parser("shard-compare")
    for name in ("shard-stage", "shard-verify", "shard-admit", "shard-admit-rebuilt"):
        commands.add_parser(name)
    args = parser.parse_args()
    if args.command == "shard-run":
        run_shard_proof(args.lane)
    elif args.command == "shard-compare":
        compare_shard_proof()
    else:
        shard_trial(args.command)


if __name__ == "__main__":
    cli()
