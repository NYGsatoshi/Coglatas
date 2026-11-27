#!/usr/bin/env python3
"""Collect and compare native Python Sonar coverage from the canonical control loop."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shlex
import shutil
import subprocess
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ".github/workflows/sonarcloud-ci.yml"
CONTROL_LOOP = "scripts/ci/sonar-python-controls.sh"
CONFIG = "scripts/ci/sonar-python.coveragerc"
REQUIREMENTS = "scripts/ci/sonar-coverage-requirements.txt"
BOOTSTRAP = "scripts/ci/sonar-python-coverage-bootstrap/sitecustomize.py"
LANES = ("baseline", "shard-1", "shard-2")
MUTATION_CONTROL = "scripts/ci/test_av_mig_production_cli.py"
MUTATION_REPORT = "artifacts/av-mig/production-cli-mutations.json"
NATIVE_REPORTS = ("coverage.native.data", "coverage.native.json", "coverage.xml")

# Greedy two-way assignment from actual control timings in run 45913402906.
# The complement always includes newly introduced canonical controls.
SECOND_GROUP = {
    "deploy/sakura/test_validate_pilot_intake_policy.py",
    "scripts/ci/qodana-packets/test_r2_verify.py",
    "scripts/ci/test-functional-diagnostics.py",
    "scripts/ci/test-functional-setup-failure-probe.py",
    "scripts/ci/test-mbj02-trx-receipt.py",
    "scripts/ci/test-mvp-a-final-checks.py",
    "scripts/ci/test-nuget-lock-policy.py",
    "scripts/ci/test-postgres-log-privacy.py",
    "scripts/ci/test-required-pr-checks.py",
    "scripts/ci/test-sonar-dotnet-coverage.py",
    "scripts/ci/test-sonar-windows-coverage.py",
    "scripts/ci/test-workspace-icon-disclosure-policy.py",
    "scripts/ci/test_av_mig_contract_boundary.py",
    "scripts/ci/test_github_action_pins.py",
    "scripts/ci/test_sec17_deep_execution.py",
    "scripts/ci/test_sec17_file_policy_execution.py",
    "scripts/ci/test_sec_arch_evidence.py",
    "scripts/ci/test_sec_arch_github_provenance.py",
    "scripts/ci/test_sec_arch_worker_accounting.py",
    "scripts/ci/test_verify_sbom_vulnerability_gate.py",
    "tests/ci/test_performance_api_diagnostics.py",
    "tests/ci/test_performance_db.py",
    "tests/ci/test_performance_db_assignment_transition.py",
    "tests/ci/test_performance_db_campaign.py",
    "tests/ci/test_performance_local.py",
    "tests/ci/test_performance_public_digest.py",
    "tests/ci/test_performance_runtime_compatibility.py",
    "tests/ci/test_release_assurance_advisory.py",
    "tests/ci/test_release_browser_evidence.py",
    "tests/ci/test_release_image_graph.py",
    "tests/ci/test_release_main_security_evidence.py",
    "tests/ci/test_release_supply_chain.py",
    "tests/ci/test_security_finding_policy.py",
    "tests/ci/test_security_zap_attribution.py",
    "tests/ci/test_synthetic_logical_recovery_ci.py",
}


def sha256(path: Path) -> str:
    if path.is_symlink() or not path.is_file() or not path.stat().st_size:
        raise ValueError("Missing, empty or linked evidence: " + str(path))
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def git(root: Path, *args: str) -> bytes:
    return subprocess.check_output(["/usr/bin/git", *args], cwd=root)


def tracked_paths(root: Path) -> set[str]:
    return {name.decode("utf-8") for name in git(root, "ls-files", "-z").split(b"\0") if name}


def identity(root: Path) -> dict:
    sha, run, attempt = (os.environ[key] for key in
                         ("PROOF_SOURCE_SHA", "GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT"))
    if (not re.fullmatch(r"[0-9a-f]{40}", sha)
            or not all(re.fullmatch(r"[1-9][0-9]*", value) for value in (run, attempt))
            or os.environ["GITHUB_WORKSPACE"] != str(root)
            or git(root, "rev-parse", "HEAD").decode().strip() != sha):
        raise ValueError("Unexpected Python qualification source identity")
    subprocess.run(["/usr/bin/git", "diff", "--quiet", "HEAD", "--"], cwd=root, check=True)
    return {"sourceSha": sha, "treeSha": git(root, "rev-parse", "HEAD^{tree}").decode().strip(),
            "runId": run, "attempt": attempt, "workspace": str(root),
            "inputs": {name: sha256(root / name) for name in (WORKFLOW, CONTROL_LOOP, CONFIG, REQUIREMENTS, BOOTSTRAP)}}


def parse_loop(workflow: str) -> tuple[list[str], str]:
    """Use the reviewed original dispatch body, not a second command inventory."""
    anchor = "          for control in \\\n"
    if workflow.count(anchor) != 1:
        raise ValueError("Expected one canonical Python control loop")
    loop = workflow.split(anchor, 1)[1]
    paths, sep, tail = loop.partition("; do\n")
    body, end, _ = tail.partition("\n          done\n")
    if not sep or not end or not body or any(not line.startswith("          ")
                                             for line in body.splitlines() if line):
        raise ValueError("Unsupported canonical dispatch layout")
    controls = [line.strip().removesuffix("\\").strip()
                for line in paths.splitlines() if line.strip()]
    if (not controls or len(controls) != len(set(controls)) or any(
            not re.fullmatch(r"(?:scripts/ci|scripts/quality|tests/ci|deploy/sakura)/"
                             r"[A-Za-z0-9_/-]+\.py", item)
            or any(part in ("", ".", "..") for part in item.split("/")) for item in controls)):
        raise ValueError("Invalid or duplicate canonical control")
    if ('python3 -B -m coverage run "$control"' not in body
            or 'COVERAGE_PROCESS_START="$COVERAGE_RCFILE"' not in body
            or 'unittest discover -s tests/ci -p "${control##*/}"' not in body):
        raise ValueError("Canonical coverage dispatch changed")
    return controls, "\n".join(line[10:] for line in body.splitlines()) + "\n"


def selection(lane: str, controls: list[str]) -> list[str]:
    if lane not in LANES or not SECOND_GROUP.issubset(controls):
        raise ValueError("Unknown lane or stale reviewed partition")
    first = [name for name in controls if name not in SECOND_GROUP]
    second = [name for name in controls if name in SECOND_GROUP]
    if not first or not second:
        raise ValueError("Empty Python lane")
    return {"baseline": list(controls), "shard-1": first, "shard-2": second}[lane]


def command(control: str, dispatch: str) -> list[str]:
    return ["bash", "-c", "set -euo pipefail\nfor control in "
            + shlex.quote(control) + "; do\n" + dispatch + "done\n"]


def execution_result(control: str, output: str, root: Path) -> dict:
    if control == MUTATION_CONTROL:
        report = root / MUTATION_REPORT
        sha256(report)
        rows = json.loads(report.read_bytes())
        if not isinstance(rows, list) or not rows:
            raise ValueError("Missing AV-MIG CLI mutation results")
        seen = set()
        for row in rows:
            if (not isinstance(row, dict) or not isinstance(row.get("mutation"), str)
                    or not row["mutation"] or row["mutation"] in seen
                    or type(row.get("expectedExit")) is not int
                    or row.get("actualExit") != row["expectedExit"]
                    or row.get("result") != "PASS"):
                raise ValueError("Invalid AV-MIG mutation execution receipt")
            seen.add(row["mutation"])
        return {"kind": "mutation", "cases": sorted(rows, key=lambda row: row["mutation"])}
    summaries = re.findall(r"(?m)^Ran ([1-9][0-9]*) tests? in [0-9.]+s\s*$", output)
    if len(summaries) != 1 or len(re.findall(r"(?m)^OK\s*$", output)) != 1:
        raise ValueError("Expected one all-passed unittest summary: " + control)
    return {"kind": "unittest", "tests": int(summaries[0])}


def coverage_environment(root: Path, data_file: Path) -> dict[str, str]:
    env = dict(os.environ)
    env.pop("COVERAGE_PROCESS_START", None)
    env["SONAR_PYTHON_SOURCE_ROOT"] = str(root)
    env["COVERAGE_RCFILE"] = str(root / CONFIG)
    env["COVERAGE_FILE"] = str(data_file)
    env["COVERAGE_CORE"] = "pytrace"
    tool = Path(os.environ["RUNNER_TEMP"]) / "sonar-coverage-tools"
    env["PYTHONPATH"] = str(tool)
    return env


def native_coverage(*args: str, env: dict[str, str]) -> None:
    subprocess.run(["python3", "-B", "-m", "coverage", *args], cwd=ROOT,
                   env=env, check=True)


def reports(folder: Path, env: dict[str, str]) -> None:
    native_coverage("xml", "-o", str(folder / "coverage.xml"), env=env)
    native_coverage("json", "-o", str(folder / "coverage.native.json"), env=env)


def file_manifest(folder: Path) -> dict[str, str]:
    result = {}
    for path in sorted(folder.rglob("*")):
        if path.is_symlink():
            raise ValueError("Linked native evidence")
        if path.is_file() and path.name != "receipt.json":
            result[path.relative_to(folder).as_posix()] = sha256(path)
    if not all(name in result for name in NATIVE_REPORTS):
        raise ValueError("Incomplete native Python reports")
    return result


def native_points(path: Path, root: Path, tracked: set[str]) -> dict:
    import coverage

    if coverage.__version__ != "7.17.0":
        raise ValueError("Unexpected native coverage.py version")
    data = coverage.CoverageData(basename=str(path))
    data.read()
    if not data.has_arcs() or not data.measured_files():
        raise ValueError("Missing native branch measurement")
    result = {}
    for name in data.measured_files():
        source = Path(name)
        if not source.is_absolute():
            source = root / source
        relative = source.relative_to(root).as_posix()
        if (relative not in tracked or source.resolve() != source
                or not source.is_file() or relative in result):
            raise ValueError("Native Python data references unowned source: " + name)
        result[relative] = (frozenset(data.lines(name) or []),
                            frozenset(tuple(arc) for arc in data.arcs(name) or []))
    return result


def native_json(path: Path) -> dict:
    value = json.loads(path.read_bytes())
    if (value["meta"].get("branch_coverage") is not True
            or value["meta"].get("version") != "7.17.0" or not value["files"]):
        raise ValueError("Unexpected native Python JSON schema")
    return value["files"]


def native_xml(path: Path) -> bytes:
    root = ET.parse(path).getroot()
    if root.tag != "coverage" or not root.findall(".//class"):
        raise ValueError("Invalid native Python XML")
    # Only the coverage.py report generation timestamp is volatile.
    root.attrib.pop("timestamp", None)
    return ET.tostring(root, encoding="utf-8")


def require_execution_equivalence(controls: list[str], baseline: dict, shards: dict) -> dict:
    if set(shards) != set(LANES[1:]) or set(baseline) != set(controls):
        raise ValueError("Incomplete canonical control inventory")
    union = {}
    for lane in LANES[1:]:
        current = shards[lane]
        if set(current) != set(selection(lane, controls)) or set(union).intersection(current):
            raise ValueError("Missing or duplicated Python control execution")
        union.update(current)
    if union != baseline:
        raise ValueError("Python execution summaries or mutation identities differ")
    tests = sum(row["tests"] for row in union.values() if row["kind"] == "unittest")
    mutations = sum(len(row["cases"]) for row in union.values() if row["kind"] == "mutation")
    if tests <= 0 or mutations <= 0:
        raise ValueError("No actual unittest or CLI mutation results")
    return {"controls": len(union), "unittestCases": tests, "mutationCases": mutations}


def collect(lane: str) -> None:
    receipt = identity(ROOT)
    controls, dispatch = parse_loop((ROOT / CONTROL_LOOP).read_text())
    tracked = tracked_paths(ROOT)
    if any(name not in tracked or (ROOT / name).is_symlink() for name in controls):
        raise ValueError("Control is not an owned tracked file")
    selected = selection(lane, controls)
    folder = ROOT / "artifacts/sonar-python-shard-lane"
    folder.mkdir(parents=True, exist_ok=False)
    data = Path(os.environ["RUNNER_TEMP"]) / "sonar-python-proof.coverage"
    if data.exists() or list(data.parent.glob(data.name + ".*")):
        raise ValueError("Pre-existing native coverage data")
    env = coverage_environment(ROOT, data)
    native_coverage("erase", env=env)
    timings, execution = {}, {}
    started = time.monotonic()
    for control in selected:
        if control == MUTATION_CONTROL and (ROOT / MUTATION_REPORT).exists():
            raise ValueError("Pre-existing mutation result")
        print("Measuring " + control, flush=True)
        start = time.monotonic()
        result = subprocess.run(command(control, dispatch), cwd=ROOT, env=env,
                                text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        print(result.stdout, end="", flush=True)
        result.check_returncode()
        execution[control] = execution_result(control, result.stdout, ROOT)
        timings[control] = round(time.monotonic() - start, 3)
    run_seconds = round(time.monotonic() - started, 3)
    start = time.monotonic()
    raw = folder / "native-shards"
    raw.mkdir()
    originals = sorted(data.parent.glob(data.name + ".*"))
    if not originals:
        raise ValueError("No original process coverage files")
    for path in originals:
        sha256(path)
        shutil.copyfile(path, raw / path.name)
    native_coverage("combine", env=env)
    shutil.copyfile(data, folder / NATIVE_REPORTS[0])
    reports(folder, env)
    if not native_points(folder / NATIVE_REPORTS[0], ROOT, tracked):
        raise ValueError("No native Python coverage")
    if identity(ROOT) != receipt:
        raise ValueError("Candidate changed during Python execution")
    if MUTATION_CONTROL in selected:
        shutil.copyfile(ROOT / MUTATION_REPORT, folder / "production-cli-mutations.json")
    receipt.update(lane=lane, selection=selected, execution=execution, timings=timings,
                   controlSeconds=run_seconds, combineReportSeconds=round(time.monotonic() - start, 3),
                   hashes=file_manifest(folder))
    (folder / "receipt.json").write_text(json.dumps(receipt, sort_keys=True, indent=2) + "\n")
    print(json.dumps({"lane": lane, "controlSeconds": run_seconds,
                      "combineReportSeconds": receipt["combineReportSeconds"],
                      "controls": len(execution), "timings": timings}, sort_keys=True))


def compare() -> None:
    expected = identity(ROOT)
    controls, _ = parse_loop((ROOT / CONTROL_LOOP).read_text())
    tracked = tracked_paths(ROOT)
    directory = ROOT / "artifacts/sonar-python-shard-proof"
    names = {lane: "sonar-python-shard-proof-" + lane + "-" + expected["attempt"] for lane in LANES}
    if {path.name for path in directory.iterdir()} != set(names.values()):
        raise ValueError("Missing, stale or unexpected Python lane artifact")
    folders, receipts, points = {}, {}, {}
    for lane in LANES:
        folder = directory / names[lane]
        if folder.is_symlink():
            raise ValueError("Linked Python artifact")
        sha256(folder / "receipt.json")
        receipt = json.loads((folder / "receipt.json").read_bytes())
        selected = selection(lane, controls)
        if (any(receipt.get(key) != value for key, value in expected.items())
                or receipt.get("lane") != lane or receipt.get("selection") != selected
                or set(receipt.get("execution", {})) != set(selected)
                or set(receipt.get("timings", {})) != set(selected)
                or receipt.get("hashes") != file_manifest(folder)
                or not any(name.startswith("native-shards/") for name in receipt["hashes"])):
            raise ValueError("Native Python artifact receipt or digest differs")
        for elapsed in [receipt["controlSeconds"], receipt["combineReportSeconds"], *receipt["timings"].values()]:
            if type(elapsed) not in (int, float) or not 0 < elapsed < 2700:
                raise ValueError("Invalid native control timing")
        if MUTATION_CONTROL in selected:
            mutation_rows = json.loads((folder / "production-cli-mutations.json").read_bytes())
            if receipt["execution"][MUTATION_CONTROL]["cases"] != sorted(mutation_rows, key=lambda row: row["mutation"]):
                raise ValueError("Native mutation receipt differs from original ledger")
        points[lane] = native_points(folder / NATIVE_REPORTS[0], ROOT, tracked)
        native_json(folder / NATIVE_REPORTS[1])
        native_xml(folder / NATIVE_REPORTS[2])
        receipts[lane], folders[lane] = receipt, folder

    result = require_execution_equivalence(controls, receipts["baseline"]["execution"],
                                          {lane: receipts[lane]["execution"] for lane in LANES[1:]})
    output = ROOT / "artifacts/sonar-python-shard-equivalence"
    output.mkdir(parents=True, exist_ok=False)
    data = output / NATIVE_REPORTS[0]
    env = coverage_environment(ROOT, data)
    for lane in LANES[1:]:
        shutil.copyfile(folders[lane] / NATIVE_REPORTS[0], output / (data.name + "." + lane))
    # coverage.py performs the actual native combination; no XML/JSON merging.
    native_coverage("combine", env=env)
    reports(output, env)
    merged = native_points(data, ROOT, tracked)
    if merged != points["baseline"]:
        raise ValueError("Combined Python native line/arc inventory differs from baseline")
    if native_json(output / NATIVE_REPORTS[1]) != native_json(folders["baseline"] / NATIVE_REPORTS[1]):
        raise ValueError("Combined native Python JSON point/branch evidence differs")
    if native_xml(output / NATIVE_REPORTS[2]) != native_xml(folders["baseline"] / NATIVE_REPORTS[2]):
        raise ValueError("Combined native Python XML differs outside timestamp")
    result.update(expected, scope="PYTHON_NATIVE_SHARD_EQUIVALENCE_ONLY_NOT_SONARCLOUD",
                  measuredFiles=len(merged), executedLines=sum(len(row[0]) for row in merged.values()),
                  executedArcs=sum(len(row[1]) for row in merged.values()),
                  lanes={lane: {key: receipts[lane][key] for key in ("controlSeconds", "combineReportSeconds", "timings")}
                         for lane in LANES}, hashes={name: sha256(output / name) for name in NATIVE_REPORTS})
    (output / "receipt.json").write_text(json.dumps(result, sort_keys=True, indent=2) + "\n")
    print(json.dumps(result, sort_keys=True))


def main() -> None:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)
    collect_parser = sub.add_parser("collect")
    collect_parser.add_argument("--lane", choices=LANES, required=True)
    sub.add_parser("compare")
    args = parser.parse_args()
    if args.command == "collect":
        collect(args.lane)
    else:
        compare()


if __name__ == "__main__":
    main()
