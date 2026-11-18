#!/usr/bin/env python3
"""Verify one frozen Angular candidate against an immutable Product composition."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[3]
PRODUCT_HEAD = "09347c1113f5f9c007bbbc5052278d5b5151feb5"
PRODUCT_TREE = "5abb6b4f4389f200a289a84b99a0dd13e60072cc"
EXECUTION_SCOPE = "SYNTHETIC_RUN_COMPARISON_WEB_READER_STORE_ONLY"
SUCCESS_STATUS = "SYNTHETIC_RUN_COMPARISON_WEB_PASSED"
SOURCE_PATH = "tests/candidates/projectide-run-comparison-web/projectide-run-comparison-security.candidate.spec.ts"
ANGULAR_DESTINATION = "frontend/src/app/features/projectide/projectide-run-comparison-security.candidate.spec.ts"
SOURCE_BLOB = "8cd3cf547440a428cabc3733cd5db50da8775397"
SOURCES = {SOURCE_PATH: ANGULAR_DESTINATION}
SOURCE_BLOBS = {SOURCE_PATH: SOURCE_BLOB}
RETAINED_SOURCES = {SOURCE_PATH: "candidate-source.spec.ts"}
ANGULAR_SUITE = "Independent recorded comparison bytes and pair lifecycle"
ANGULAR_TITLES = ('admits all genuine complete and bounded Core pairs with separate input and recording identities', 'refuses a rehashed false complete label and elapsed delta for two real bounded prefixes', 'refuses independently rehashed counts clocks and exact-integer delta substitutions', 'refuses rehashed swapped summaries and substituted original recording digests', 'refuses borrowed genuine admitted recordings instead of accepting matching report structure', 'refuses current-scope replacement in either original case after both runs were admitted', 'refuses rehashed future formats authority grants and result-pass claims', 'refuses duplicate original JSON members and oversized UTF8 frames despite correct hashes', 'retains frozen detached summaries without freezing or granting ownership to caller cases', 'rejects changed second-run bytes during the actual comparison digest boundary', 'rejects comparison projection substitution during the actual digest boundary', 'requires current Source and run admission and compares without replacing the owned current run', 'withdraws pending comparisons when a result Source or recording admission is replaced', 'clears both summaries on access loss and cannot restore them from an older pending read', 'preserves the exact newer comparison when an older request finishes later', 'withdraws a pair when its second original case is removed or replaced by an equal clone', 'withdraws current summaries after either original recording changes while preserving detached evidence', 'cannot publish a delayed pair after its selected second case leaves the bundle')
ANGULAR_COUNT = len(ANGULAR_TITLES)
ANGULAR_IDENTITIES = frozenset(ANGULAR_SUITE + " " + title for title in ANGULAR_TITLES)
QUALIFICATION_FLAGS = (
    "requiredCheckQualified", "independentReviewComplete", "nativeUiQualified",
    "productionQualified", "trustQualified", "releaseQualified",
)


def unique_json_object(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Angular report contains duplicate JSON keys")
        result[key] = value
    return result


def reject_json_constant(value: str) -> None:
    raise ValueError("Angular report contains a non-finite JSON constant")


def has_error_information(record: dict[str, object]) -> bool:
    return any(record.get(key) not in (None, "", []) for key in
               ("error", "errors", "unhandledErrors", "testExecError", "failureMessage", "failureMessages", "failureDetails"))


def angular_inventory(path: Path, expected_source: str | Path) -> list[str]:
    parsed = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique_json_object,
                        parse_constant=reject_json_constant)
    # Vitest counts the file and describe group as two suites, but emits one file.
    counts = {"numTotalTestSuites": 2, "numPassedTestSuites": 2, "numFailedTestSuites": 0,
              "numPendingTestSuites": 0, "numTotalTests": ANGULAR_COUNT, "numPassedTests": ANGULAR_COUNT,
              "numFailedTests": 0, "numPendingTests": 0, "numTodoTests": 0}
    if (not isinstance(parsed, dict) or parsed.get("success") is not True
            or any(type(parsed.get(key)) is not int or parsed[key] != value for key, value in counts.items())
            or has_error_information(parsed)):
        raise ValueError("Angular report must contain the exact complete passing Angular candidate roster")
    snapshot = parsed.get("snapshot")
    zero_snapshot = {key: 0 for key in ("added", "filesAdded", "filesRemoved", "filesUnmatched", "filesUpdated",
                                       "matched", "total", "unchecked", "unmatched", "updated")}
    if (not isinstance(snapshot, dict)
            or any(type(snapshot.get(key)) is not int or snapshot[key] != value for key, value in zero_snapshot.items())
            or snapshot.get("failure") is not False or snapshot.get("didUpdate") is not False
            or snapshot.get("filesRemovedList") != [] or snapshot.get("uncheckedKeysByFile") != []):
        raise ValueError("Angular report contains unexpected snapshot results")
    suites = parsed.get("testResults")
    if not isinstance(suites, list) or len(suites) != 1 or not isinstance(suites[0], dict):
        raise ValueError("Angular report requires exactly one candidate source file")
    suite = suites[0]
    expected_path = str(expected_source).replace("\\", "/")
    actual_path = suite.get("name")
    if (not isinstance(actual_path, str) or actual_path.replace("\\", "/") != expected_path
            or not expected_path.endswith("/" + ANGULAR_DESTINATION)):
        raise ValueError("Angular report source filename differs from the composed candidate")
    if suite.get("status") != "passed" or suite.get("message") != "" or has_error_information(suite):
        raise ValueError("Angular source suite contains failure information")
    assertions = suite.get("assertionResults")
    if (not isinstance(assertions, list) or len(assertions) != ANGULAR_COUNT
            or any(not isinstance(case, dict) for case in assertions)):
        raise ValueError("Angular report does not match the exact Angular candidate assertion roster")
    names = [case.get("fullName") for case in assertions]
    if (any(not isinstance(name, str) for name in names)
            or set(names) != ANGULAR_IDENTITIES or len(set(names)) != ANGULAR_COUNT):
        raise ValueError("Angular report does not match the exact Angular candidate assertion roster")
    for case in assertions:
        if (case.get("status") != "passed" or case.get("failureMessages") != []
                or has_error_information(case) or case.get("ancestorTitles") != [ANGULAR_SUITE]
                or case.get("title") not in ANGULAR_TITLES
                or ANGULAR_SUITE + " " + case["title"] != case["fullName"]):
            raise ValueError("Angular assertion metadata or failure information is inconsistent")
    return sorted(names)


def local_git_environment() -> dict[str, str]:
    # Preserve SDK/host settings, never inherited repository/index/config overrides.
    return {key: value for key, value in os.environ.items() if not key.upper().startswith("GIT_")}


def run(command: list[str], cwd: Path, log: Path | None = None, *, preserve_output: bool = False) -> str:
    result = subprocess.run(command, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                            text=True, encoding="utf-8", errors="replace", check=False,
                            env=local_git_environment() if command[0] == "git" else None)
    if log is not None:
        log.write_text(result.stdout, encoding="utf-8")
    if result.returncode:
        raise RuntimeError(f"Command failed ({result.returncode}): {command[:3]}; inspect retained log")
    return result.stdout if preserve_output else result.stdout.strip()


def git(cwd: Path, *arguments: str) -> str:
    return run(["git", "-c", "core.autocrlf=false", *arguments], cwd)


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify_working_tree(worktree: Path, raw_sources: dict[str, bytes], source_blobs: dict[str, str], *, phase: str = "source") -> None:
    if set(raw_sources) != set(SOURCES.values()) or set(source_blobs) != set(raw_sources):
        raise ValueError("Composition source inventory differs from the single frozen candidate")
    for destination, raw in raw_sources.items():
        output = worktree / destination
        parents = output.relative_to(worktree).parents
        if (output.is_symlink() or not output.is_file() or output.read_bytes() != raw
                or any((worktree / parent).is_symlink() for parent in parents)):
            raise ValueError("Composed working-tree Test source changed from the committed blob")
        expected_stage = "100644 " + source_blobs[destination] + " 0\t" + destination
        if git(worktree, "ls-files", "--stage", "--", destination) != expected_stage:
            raise ValueError("Composed candidate index identity or mode changed")
    if git(worktree, "rev-parse", "HEAD") != PRODUCT_HEAD:
        raise ValueError("Composed Product HEAD changed")
    expected = sorted(raw_sources)
    if (git(worktree, "diff", "--cached", "--name-only", "HEAD", "--").splitlines() != expected
            or git(worktree, "diff", "--name-only", "--")):
        raise ValueError("Tracked Product source changed during independent composition")
    allowed_outputs = {
        "source": (),
        "dependencies": ("frontend/node_modules/",),
        "tests": ("frontend/node_modules/", "frontend/.angular/cache/"),
    }
    if phase not in allowed_outputs:
        raise ValueError("Unsupported composition output phase")
    untracked = run(["git", "-c", "core.autocrlf=false", "ls-files", "--others", "-z"],
                    worktree, preserve_output=True).split("\0")
    if any(path and not path.startswith(allowed_outputs[phase]) for path in untracked):
        raise ValueError("Unexpected untracked source appeared during independent composition")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--product-head", required=True)
    parser.add_argument("--worktree", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    args = parser.parse_args()
    if not re.fullmatch(r"[0-9a-f]{40}", args.product_head) or args.product_head != PRODUCT_HEAD:
        raise ValueError("Only the frozen immutable Product SHA is admitted")
    target, evidence = args.worktree.resolve(), args.evidence.resolve()
    if target.exists() or evidence.exists() or target == evidence or target in evidence.parents or evidence in target.parents:
        raise ValueError("Distinct, fresh, non-nested composition and evidence paths are mandatory")
    if git(ROOT, "status", "--porcelain", "--untracked-files=all"):
        raise ValueError("Commit the independent sources before qualification")
    test_head = git(ROOT, "rev-parse", "HEAD")
    if test_head == args.product_head:
        raise ValueError("Test and Product source owners must be separate commits")
    git(ROOT, "cat-file", "-e", args.product_head + "^{commit}")
    if git(ROOT, "rev-parse", args.product_head + "^{tree}") != PRODUCT_TREE:
        raise ValueError("Immutable Product tree mismatch")
    evidence.mkdir(parents=True)
    receipt: dict[str, object] = {
        "productHead": args.product_head, "testHead": test_head, "executionScope": EXECUTION_SCOPE,
        "sourcePath": SOURCE_PATH, "destination": ANGULAR_DESTINATION, "sourceBlob": SOURCE_BLOB,
        "expectedIdentities": sorted(ANGULAR_IDENTITIES),
        **dict.fromkeys(QUALIFICATION_FLAGS, False), "status": "NOT_EXECUTED",
    }
    try:
        run(["git", "-c", "core.autocrlf=false", "worktree", "add", "--detach", str(target), args.product_head],
            ROOT, evidence / "worktree.log")
        sources, raw_sources, source_blobs = {}, {}, {}
        for original, destination in SOURCES.items():
            output = target / destination
            if output.exists() or output.is_symlink():
                raise ValueError(f"Unexpected existing candidate destination: {destination}")
            source_blob = git(ROOT, "rev-parse", f"{test_head}:{original}")
            if source_blob != SOURCE_BLOBS[original]:
                raise ValueError("Independent Angular candidate source changed from its frozen blob")
            raw = subprocess.run(["git", "show", f"{test_head}:{original}"], cwd=ROOT,
                                 stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True,
                                 env=local_git_environment()).stdout
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_bytes(raw)
            (evidence / RETAINED_SOURCES[original]).write_bytes(raw)
            git(target, "add", "--", destination)
            raw_sources[destination], source_blobs[destination] = raw, source_blob
            sources[destination] = {"source": original, "gitBlob": source_blob, "sha256": digest(output),
                                    "retainedSource": RETAINED_SOURCES[original]}
        verify_working_tree(target, raw_sources, source_blobs)
        receipt.update(productTree=git(target, "rev-parse", "HEAD^{tree}"),
                       composedTree=git(target, "write-tree"), sources=sources,
                       sourceSha256=sources[ANGULAR_DESTINATION]["sha256"])
        (evidence / "source-receipt.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")

        node = run(["node", "--version"], target / "frontend", evidence / "node.log")
        if not re.fullmatch(r"v26\.\d+\.\d+", node):
            raise ValueError("Actual Node version does not match the supported Node 26 runtime")
        verify_working_tree(target, raw_sources, source_blobs)
        npm = "npm.cmd" if os.name == "nt" else "npm"
        npm_version = run([npm, "--version"], target / "frontend", evidence / "npm-version.log")
        if npm_version != "11.17.0":
            raise ValueError("Actual npm version does not match the Product packageManager pin")
        verify_working_tree(target, raw_sources, source_blobs)
        run([npm, "ci", "--ignore-scripts", "--no-audit", "--no-fund"], target / "frontend", evidence / "npm.log")
        verify_working_tree(target, raw_sources, source_blobs, phase="dependencies")
        report = evidence / "vitest.json"
        run([npm, "test", "--", "--include=" + ANGULAR_DESTINATION.removeprefix("frontend/"),
             "--reporters=json", "--output-file=" + str(report)], target / "frontend", evidence / "vitest.log")
        verify_working_tree(target, raw_sources, source_blobs, phase="tests")
        names = angular_inventory(report, target / ANGULAR_DESTINATION)
        receipt["vitest"] = {"total": ANGULAR_COUNT, "passed": ANGULAR_COUNT, "failed": 0, "skipped": 0,
                             "identities": names}
        receipt["status"] = SUCCESS_STATUS
    except Exception as error:
        receipt["status"] = "FAILED_OR_INCOMPLETE"
        receipt["error"] = type(error).__name__ + ": " + str(error)
        raise
    finally:
        receipt["retainedSha256"] = {
            path.name: digest(path) for path in sorted(evidence.iterdir())
            if path.is_file() and path.name != "receipt.json"
        }
        (evidence / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(receipt, indent=2))


if __name__ == "__main__":
    main()
