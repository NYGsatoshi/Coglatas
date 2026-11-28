#!/usr/bin/env python3
"""Credential-free qualification of Angular's original LCOV and test results."""

from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import time

ROOT = Path(__file__).resolve().parents[2]
LANES = ("baseline", "shard-1", "shard-2")
SHARDS = {"baseline": None, "shard-1": "1/2", "shard-2": "2/2"}
PROOF_SHARD_ENV = "COGLATAS_SONAR_ANGULAR_PROOF_SHARD"
LCOV = "frontend/coverage/frontend/lcov.info"
INPUTS = ("frontend/package.json", "frontend/package-lock.json",
          "frontend/angular.json", "frontend/vitest.config.ts")
ARTIFACTS = ("lcov.info", "tests.json")


def sha256(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def owned_bytes(root: Path, name: str) -> bytes:
    parts = PurePosixPath(name).parts
    if (not parts or name.startswith("/") or "\\" in name
            or any(part in ("", ".", "..") for part in name.split("/"))):
        raise ValueError("Invalid evidence path")
    path = root
    for part in parts:
        path = path / part
        if path.is_symlink():
            raise ValueError("Symlink evidence: " + name)
    if not path.is_file() or not path.stat().st_size:
        raise ValueError("Missing or empty evidence: " + name)
    return path.read_bytes()


def git(root: Path, *args: str) -> bytes:
    return subprocess.check_output(["/usr/bin/git", *args], cwd=root)


def checkout_identity(root: Path) -> dict:
    sha = os.environ["PROOF_SOURCE_SHA"]
    run, attempt = os.environ["GITHUB_RUN_ID"], os.environ["GITHUB_RUN_ATTEMPT"]
    if (not re.fullmatch(r"[a-f0-9]{40}", sha)
            or not all(re.fullmatch(r"[1-9][0-9]*", value) for value in (run, attempt))
            or os.environ["GITHUB_WORKSPACE"] != str(root)
            or git(root, "rev-parse", "HEAD").decode().strip() != sha):
        raise ValueError("Unexpected Angular qualification execution identity")
    subprocess.run(["/usr/bin/git", "diff", "--quiet", "HEAD", "--", "frontend"],
                   cwd=root, check=True)
    tree = git(root, "rev-parse", "HEAD^{tree}").decode().strip()
    return {"sourceSha": sha, "treeSha": tree, "runId": run, "attempt": attempt,
            "workspace": str(root), "inputs": {
                name: sha256(owned_bytes(root, name)) for name in INPUTS}}


def tracked_frontend(root: Path) -> set[str]:
    tracked = {item.decode("utf-8") for item in
               git(root, "ls-files", "-z", "--", "frontend").split(b"\0") if item}
    if not tracked:
        raise ValueError("Empty tracked frontend tree")
    return tracked


def spec_inventory(tracked: set[str]) -> set[str]:
    specs = {name for name in tracked if name.endswith((".spec.ts", ".test.ts"))}
    if (not specs or any(not name.startswith("frontend/src/")
                         or not re.fullmatch(r"[A-Za-z0-9_./-]+", name) for name in specs)):
        raise ValueError("Unexpected tracked Angular test path")
    return specs


def runner_shard(lane: str) -> str | None:
    if lane not in SHARDS:
        raise ValueError("Unknown Angular qualification lane")
    return SHARDS[lane]


def command(root: Path, lane: str, specs: set[str]) -> list[str]:
    if not specs:
        raise ValueError("Empty Angular test inventory")
    args = ["npm", "--prefix", "frontend", "run", "test:coverage", "--",
            "--reporters=json",
            "--output-file=" + str(root / "artifacts/sonar-angular-proof/tests.json")]
    # Every lane executes the same builder command. Only the runner's
    # reviewed config receives an explicit shard selection.
    runner_shard(lane)
    return args


def runner_environment(lane: str, inherited: dict[str, str]) -> dict[str, str]:
    environment = dict(inherited)
    environment.pop(PROOF_SHARD_ENV, None)
    shard = runner_shard(lane)
    if shard is not None:
        if environment.get("CI") != "true" and environment.get("GITHUB_ACTIONS") != "true":
            raise ValueError("Native shard qualification requires CI")
        environment[PROOF_SHARD_ENV] = shard
    return environment


def source_identity(name: str, root: Path, tracked: set[str]) -> str:
    if not isinstance(name, str) or not name or "\\" in name:
        raise ValueError("Invalid Angular source name")
    path = Path(name)
    if path.is_absolute():
        try:
            relative = path.relative_to(root).as_posix()
        except ValueError as error:
            raise ValueError("Angular source is outside the candidate checkout") from error
    elif name.startswith("frontend/"):
        relative = name
    elif name.startswith("src/"):
        relative = "frontend/" + name
    else:
        raise ValueError("Unknown native Angular source path: " + name)
    if (relative not in tracked or not (root / relative).is_file()
            or (root / relative).resolve() != root / relative):
        raise ValueError("Native Angular source not owned by the tracked tree: " + relative)
    return relative


def test_inventory(raw: bytes, root: Path, expected: set[str], *, exact: bool = True) -> Counter:
    report = json.loads(raw)
    if report.get("success") is not True or report.get("numFailedTests") != 0:
        raise ValueError("Native Angular execution did not pass")
    files, tests = set(), Counter()
    for suite in report["testResults"]:
        source = source_identity(suite["name"], root, expected)
        if source in files or suite.get("status") != "passed":
            raise ValueError("Duplicate or non-passing test file: " + source)
        files.add(source)
        assertions = suite["assertionResults"]
        if not assertions:
            raise ValueError("Empty native test file: " + source)
        for case in assertions:
            ancestors = case["ancestorTitles"]
            title = case["title"]
            if (case["status"] != "passed" or not isinstance(title, str) or not title
                    or not isinstance(ancestors, list)
                    or any(not isinstance(value, str) for value in ancestors)):
                raise ValueError("Skipped, failed or unidentified native test: " + source)
            tests[(source, tuple(ancestors), title)] += 1
    if (not files or not files.issubset(expected)
            or (exact and files != expected)):
        raise ValueError("Angular test file set differs from selected tracked inventory")
    if (not tests or report.get("numTotalTests") != sum(tests.values())
            or report.get("numPassedTests") != sum(tests.values())
            or report.get("numPendingTests", 0) != 0
            or report.get("numTodoTests", 0) != 0):
        raise ValueError("Native Angular test counters differ")
    return tests


def natural(value: str) -> int:
    if not re.fullmatch(r"[0-9]+", value):
        raise ValueError("Malformed native LCOV counter: " + value)
    return int(value)


def native_lcov(raw: bytes, root: Path, tracked: set[str]) -> dict:
    """Validate original LCOV line and branch identities; do not merge report bytes."""
    files = {}
    source = None
    points, totals = {}, {}

    def add(key, value):
        if key in points:
            raise ValueError("Duplicate LCOV point identity")
        points[key] = value

    for line in raw.decode("utf-8").splitlines():
        if not line or line.startswith("TN:"):
            continue
        if line.startswith("SF:"):
            if source is not None:
                raise ValueError("Unterminated LCOV source record")
            source = source_identity(line[3:], root, tracked)
            if source in files:
                raise ValueError("Duplicate LCOV source record")
            points, totals = {}, {}
            continue
        if source is None:
            raise ValueError("LCOV data without a source record")
        if line == "end_of_record":
            for kind, found, hit in (("DA", "LF", "LH"), ("BRDA", "BRF", "BRH")):
                subset = [value for key, value in points.items() if key[0] == kind]
                if (totals.get(found, 0) != len(subset)
                        or totals.get(hit, 0) != sum(subset)
                        or (subset and (found not in totals or hit not in totals))):
                    raise ValueError("Native LCOV totals disagree: " + source)
            files[source] = points
            source = None
            continue
        tag, sep, payload = line.partition(":")
        if not sep:
            raise ValueError("Malformed LCOV record")
        if tag == "DA":
            values = payload.split(",")
            if len(values) not in (2, 3) or natural(values[0]) < 1:
                raise ValueError("Malformed LCOV line")
            add(("DA", natural(values[0])), natural(values[1]) > 0)
        elif tag == "BRDA":
            values = payload.split(",")
            if len(values) != 4 or natural(values[0]) < 1:
                raise ValueError("Malformed LCOV branch")
            key = ("BRDA", natural(values[0]), natural(values[1]), natural(values[2]))
            add(key, values[3] != "-" and natural(values[3]) > 0)
        elif tag in {"LF", "LH", "BRF", "BRH"}:
            if tag in totals:
                raise ValueError("Duplicate LCOV total")
            totals[tag] = natural(payload)
        elif tag in {"FN", "FNDA", "FNF", "FNH"}:
            # Sonar imports lines and branches; function counters are retained
            # unchanged in the original artifacts, not claimed equivalent.
            continue
        else:
            raise ValueError("Unsupported native LCOV record: " + tag)
    if source is not None or not files or not any(
            key[0] == "DA" and value for points in files.values() for key, value in points.items()):
        raise ValueError("Incomplete or empty native Angular coverage")
    return files


def compare_results(baseline, shards) -> dict:
    if set(shards) != set(LANES[1:]):
        raise ValueError("Missing or unexpected Angular shard")
    baseline_tests, baseline_files = baseline
    tests, union = Counter(), {}
    seen_files = set()
    for lane in LANES[1:]:
        lane_tests, lane_files = shards[lane]
        current_files = {key[0] for key in lane_tests}
        if not lane_tests or not lane_files or seen_files.intersection(current_files):
            raise ValueError("Empty or overlapping Angular shard")
        seen_files.update(current_files)
        tests.update(lane_tests)
        for source, points in lane_files.items():
            target = union.setdefault(source, {})
            for key, value in points.items():
                target[key] = target.get(key, False) or value
    if tests != baseline_tests:
        raise ValueError("Angular test identity or multiplicity changed")
    if set(union) != set(baseline_files):
        raise ValueError("Angular LCOV source inventory differs: "
                         + str(len(set(baseline_files) - set(union))) + " missing, "
                         + str(len(set(union) - set(baseline_files))) + " extra")
    missing = extra = different = 0
    examples = []
    for source in sorted(baseline_files):
        old, new = baseline_files[source], union[source]
        missing += len(set(old) - set(new))
        extra += len(set(new) - set(old))
        for key in sorted(set(old).intersection(new)):
            if old[key] != new[key]:
                different += 1
                if len(examples) < 10:
                    examples.append({"source": source, "point": list(key),
                                     "baseline": old[key], "shards": new[key]})
    if missing or extra or different:
        print(json.dumps({"changedHitExamples": examples}, sort_keys=True))
        raise ValueError(f"Angular LCOV differs: {missing} missing, {extra} extra, "
                         f"{different} changed hit states")
    return summarize_results(tests, union)


def summarize_results(tests: Counter, union: dict) -> dict:
    result = {"passedTests": sum(tests.values()), "specFiles": len({key[0] for key in tests}),
              "coverageFiles": len(union)}
    for tag, label in (("DA", "line"), ("BRDA", "branch")):
        hits = [value for points in union.values() for key, value in points.items() if key[0] == tag]
        if not hits:
            raise ValueError("No Angular " + label + " points")
        result[label + "Points"] = len(hits)
        result[label + "Visited"] = sum(hits)
    return result


def collect(lane: str) -> None:
    identity = checkout_identity(ROOT)
    tracked = tracked_frontend(ROOT)
    specs = spec_inventory(tracked)
    shard = runner_shard(lane)
    folder = ROOT / "artifacts/sonar-angular-proof"
    folder.mkdir(parents=True, exist_ok=False)
    if (ROOT / LCOV).exists():
        raise ValueError("Pre-existing Angular coverage report")
    start = time.monotonic()
    subprocess.run(command(ROOT, lane, specs), cwd=ROOT, check=True,
                   env=runner_environment(lane, os.environ))
    elapsed = round(time.monotonic() - start, 3)
    if checkout_identity(ROOT) != identity:
        raise ValueError("Angular checkout changed during execution")
    report = owned_bytes(ROOT, LCOV)
    # Retain exact bytes, even if native validation fails.
    (folder / "lcov.info").write_bytes(report)
    tests = test_inventory(owned_bytes(folder, "tests.json"), ROOT, specs,
                           exact=lane == "baseline")
    points = native_lcov(report, ROOT, tracked)
    receipt = {**identity, "lane": lane, "runnerShard": shard,
               "selection": sorted({key[0] for key in tests}),
               "testSeconds": elapsed, "passedTests": sum(tests.values()),
               "coverageFiles": len(points), "hashes": {
                   name: sha256(owned_bytes(folder, name)) for name in ARTIFACTS}}
    (folder / "receipt.json").write_text(json.dumps(receipt, indent=2, sort_keys=True) + "\n")
    print(json.dumps({key: receipt[key] for key in ("lane", "testSeconds", "passedTests", "coverageFiles")}))


def compare(mode: str = "full") -> None:
    if mode not in ("full", "baseline-only"):
        raise ValueError("Unknown Angular admission mode")
    selected_lanes = LANES if mode == "full" else ("baseline",)
    identity = checkout_identity(ROOT)
    tracked = tracked_frontend(ROOT)
    specs = spec_inventory(tracked)
    directory = ROOT / "artifacts/sonar-angular-shard-proof"
    names = {lane: "sonar-angular-shard-proof-" + lane + "-" + identity["attempt"]
             for lane in selected_lanes}
    if directory.is_symlink() or {path.name for path in directory.iterdir()} != set(names.values()):
        raise ValueError("Missing, stale or unexpected Angular artifact")
    inventories, timings = {}, {}
    for lane in selected_lanes:
        folder = directory / names[lane]
        if folder.is_symlink() or {path.name for path in folder.iterdir()} != {"receipt.json", *ARTIFACTS}:
            raise ValueError("Invalid Angular artifact contents")
        receipt = json.loads(owned_bytes(folder, "receipt.json"))
        if (any(receipt.get(key) != value for key, value in identity.items())
                or receipt.get("lane") != lane
                or receipt.get("runnerShard") != runner_shard(lane)
                or receipt.get("hashes") != {name: sha256(owned_bytes(folder, name))
                                              for name in ARTIFACTS}):
            raise ValueError("Angular artifact identity or digest mismatch")
        seconds = receipt.get("testSeconds")
        if type(seconds) not in (int, float) or not 0 < seconds < 2700:
            raise ValueError("Invalid Angular lane duration")
        tests = test_inventory(owned_bytes(folder, "tests.json"), ROOT, specs,
                               exact=lane == "baseline")
        if receipt.get("selection") != sorted({key[0] for key in tests}):
            raise ValueError("Angular lane test selection receipt differs")
        points = native_lcov(owned_bytes(folder, "lcov.info"), ROOT, tracked)
        if receipt.get("passedTests") != sum(tests.values()) or receipt.get("coverageFiles") != len(points):
            raise ValueError("Angular lane receipt counters differ")
        inventories[lane] = (tests, points)
        timings[lane] = {"testSeconds": seconds, "passedTests": sum(tests.values()),
                         "hashes": receipt["hashes"]}
    if mode == "full":
        result = compare_results(inventories.pop("baseline"), inventories)
        scope = "ANGULAR_NATIVE_LINE_BRANCH_EQUIVALENCE_ONLY_NOT_SONARCLOUD"
    else:
        # Every tracked spec was executed by the original unsharded command.
        # This receipt is admission evidence, not an independent comparison.
        result = summarize_results(*inventories["baseline"])
        scope = "ANGULAR_NATIVE_UNSHARDED_BASELINE_ONLY_NOT_SONARCLOUD"
    result.update(identity, scope=scope, mode=mode, baselineCompared=mode == "full",
                  lanes=timings)
    output = ROOT / "artifacts/sonar-angular-shard-equivalence.json"
    with output.open("x", encoding="utf-8") as stream:
        json.dump(result, stream, indent=2, sort_keys=True)
        stream.write("\n")
    print(json.dumps(result, sort_keys=True))


def main() -> None:
    parser = argparse.ArgumentParser()
    commands = parser.add_subparsers(dest="command", required=True)
    collect_args = commands.add_parser("collect")
    collect_args.add_argument("--lane", choices=LANES, required=True)
    compare_args = commands.add_parser("compare")
    compare_args.add_argument("--mode", choices=("full", "baseline-only"), default="full")
    args = parser.parse_args()
    if args.command == "collect":
        collect(args.lane)
    else:
        compare(args.mode)


if __name__ == "__main__":
    main()
