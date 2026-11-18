#!/usr/bin/env python3
"""Admission controls over original retained Angular reports; never fresh Angular execution."""
from __future__ import annotations

import argparse
from contextlib import redirect_stdout
import copy
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("angular_candidate_runner", HERE / "compose_and_verify.py")
assert SPEC is not None and SPEC.loader is not None
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)
ANGULAR_REPORT = b""
ANGULAR_SOURCE = ""
INPUT_RECEIPT = {}
INPUT_DIRECTORY = None
SOURCE_BYTES = {source: (HERE / Path(source).name).read_bytes() for source in RUNNER.SOURCES}


def fixture_environment() -> dict[str, str]:
    # Independent of the subject under test, before any disposable Git write.
    return {key: value for key, value in os.environ.items() if not key.upper().startswith("GIT_")}


def fixture_git(repository: Path, *args: str, data: bytes | None = None) -> str:
    result = subprocess.run(
        ["git", "-c", "core.autocrlf=false", "-c", "commit.gpgsign=false",
         "-c", "core.hooksPath=" + str(repository / "empty-hooks"),
         "-c", "user.name=Angular admission controls",
         "-c", "user.email=angular-controls@example.invalid", *args],
        cwd=repository, env=fixture_environment(), input=data, capture_output=True, check=True)
    return result.stdout.decode("utf-8").strip()


def file_snapshot(directory: Path) -> dict[str, bytes]:
    return {str(path.relative_to(directory)): path.read_bytes()
            for path in directory.rglob("*") if path.is_file()}


def validate_original_inputs() -> None:
    if not ANGULAR_REPORT or not INPUT_RECEIPT or INPUT_DIRECTORY is None:
        raise ValueError("Original Angular JSON, receipt and directory are required")
    if (INPUT_RECEIPT.get("productHead") != RUNNER.PRODUCT_HEAD
            or INPUT_RECEIPT.get("productTree") != RUNNER.PRODUCT_TREE
            or INPUT_RECEIPT.get("status") != RUNNER.SUCCESS_STATUS
            or INPUT_RECEIPT.get("executionScope") != RUNNER.EXECUTION_SCOPE):
        raise ValueError("Retained inputs do not identify the frozen successful Angular composition")
    for flag in RUNNER.QUALIFICATION_FLAGS:
        if INPUT_RECEIPT.get(flag) is not False:
            raise ValueError("Retained report unexpectedly claims qualification")
    if set(INPUT_RECEIPT["sources"]) != set(RUNNER.SOURCES.values()):
        raise ValueError("Retained source inventory does not contain exactly the candidate")
    for destination, source in INPUT_RECEIPT["sources"].items():
        raw = SOURCE_BYTES[source["source"]]
        if (RUNNER.SOURCES[source["source"]] != destination
                or source["gitBlob"] != RUNNER.SOURCE_BLOBS[source["source"]]
                or source["sha256"] != hashlib.sha256(raw).hexdigest()
                or (INPUT_DIRECTORY / source["retainedSource"]).read_bytes() != raw):
            raise ValueError("Retained candidate source differs from the frozen original")
    for name, expected in INPUT_RECEIPT["retainedSha256"].items():
        if Path(name).name != name or hashlib.sha256((INPUT_DIRECTORY / name).read_bytes()).hexdigest() != expected:
            raise ValueError("Retained original evidence digest mismatch")
    with tempfile.TemporaryDirectory(prefix="angular-original-inputs-") as temporary:
        angular = Path(temporary) / "original.json"
        angular.write_bytes(ANGULAR_REPORT)
        names = RUNNER.angular_inventory(angular, ANGULAR_SOURCE)
        if names != INPUT_RECEIPT["vitest"]["identities"] or names != INPUT_RECEIPT["expectedIdentities"]:
            raise ValueError("Original Angular receipt identity mismatch")


class ExactRejection(unittest.TestCase):
    def reject(self, reason: str, action) -> None:
        with self.assertRaises(ValueError) as caught:
            action()
        self.assertEqual(reason, str(caught.exception))


class RetainedReportControls(ExactRejection):
    @classmethod
    def setUpClass(cls) -> None:
        validate_original_inputs()

    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="angular-report-controls-")
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        self.assertEqual(RUNNER.ANGULAR_COUNT, len(self.angular(json.loads(ANGULAR_REPORT))))


    def angular(self, report: dict) -> list[str]:
        path = self.directory / "derived.json"
        path.write_text(json.dumps(report), encoding="utf-8")
        return RUNNER.angular_inventory(path, ANGULAR_SOURCE)

    def test_original_angular_report_and_frozen_source_blob_match(self) -> None:
        validate_original_inputs()
        self.assertEqual(RUNNER.ANGULAR_COUNT, len(RUNNER.ANGULAR_IDENTITIES))
        for source, raw in SOURCE_BYTES.items():
            blob = hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest()
            self.assertEqual(RUNNER.SOURCE_BLOBS[source], blob)


    def test_angular_counters_errors_and_success_are_consistent(self) -> None:
        original = json.loads(ANGULAR_REPORT)
        for key in ("numTotalTestSuites", "numPassedTestSuites", "numFailedTestSuites", "numPendingTestSuites",
                    "numTotalTests", "numPassedTests", "numFailedTests", "numPendingTests", "numTodoTests"):
            with self.subTest(counter=key):
                report = copy.deepcopy(original)
                report[key] = report[key] + 1
                self.reject("Angular report must contain the exact complete passing Angular candidate roster",
                            lambda: self.angular(report))
        for key, value in (("success", False), ("errors", ["unhandled failure"]), ("numTodoTests", False)):
            report = copy.deepcopy(original)
            report[key] = value
            self.reject("Angular report must contain the exact complete passing Angular candidate roster",
                        lambda: self.angular(report))

    def test_angular_requires_one_exact_source_filename(self) -> None:
        report = json.loads(ANGULAR_REPORT)
        report["testResults"].append(copy.deepcopy(report["testResults"][0]))
        self.reject("Angular report requires exactly one candidate source file", lambda: self.angular(report))
        for filename in ("Foreign.spec.ts", ANGULAR_SOURCE + ".other", "/foreign/" + RUNNER.ANGULAR_DESTINATION):
            report = json.loads(ANGULAR_REPORT)
            report["testResults"][0]["name"] = filename
            self.reject("Angular report source filename differs from the composed candidate", lambda: self.angular(report))

    def test_angular_foreign_or_duplicate_assertion_names_are_rejected(self) -> None:
        for duplicate in (False, True):
            report = json.loads(ANGULAR_REPORT)
            cases = report["testResults"][0]["assertionResults"]
            cases[0]["fullName"] = cases[1]["fullName"] if duplicate else "Foreign unrelated passing assertion"
            self.reject("Angular report does not match the exact Angular candidate assertion roster", lambda: self.angular(report))

    def test_angular_failed_suite_or_error_message_is_rejected(self) -> None:
        for key, value in (("status", "failed"), ("message", "reported failure"), ("errors", ["failed"])):
            report = json.loads(ANGULAR_REPORT)
            report["testResults"][0][key] = value
            self.reject("Angular source suite contains failure information", lambda: self.angular(report))

    def test_angular_assertion_metadata_and_failure_information_are_rejected(self) -> None:
        for key, value in (("status", "failed"), ("failureMessages", ["reported error"]),
                           ("failureMessages", None), ("ancestorTitles", ["Foreign group"]),
                           ("title", "Foreign title"), ("testExecError", {"message": "error"})):
            report = json.loads(ANGULAR_REPORT)
            report["testResults"][0]["assertionResults"][0][key] = value
            self.reject("Angular assertion metadata or failure information is inconsistent", lambda: self.angular(report))

    def test_angular_snapshot_failure_or_update_is_rejected(self) -> None:
        for key, value in (("failure", True), ("didUpdate", True), ("added", 1), ("unchecked", 1)):
            report = json.loads(ANGULAR_REPORT)
            report["snapshot"][key] = value
            self.reject("Angular report contains unexpected snapshot results", lambda: self.angular(report))

    def test_angular_duplicate_json_keys_and_nonfinite_values_are_rejected(self) -> None:
        path = self.directory / "malformed.json"
        text = ANGULAR_REPORT.decode("utf-8")
        path.write_text('{"success":false,' + text[1:], encoding="utf-8")
        self.reject("Angular report contains duplicate JSON keys", lambda: RUNNER.angular_inventory(path, ANGULAR_SOURCE))
        path.write_text('{"unexpected":NaN,' + text[1:], encoding="utf-8")
        self.reject("Angular report contains a non-finite JSON constant",
                    lambda: RUNNER.angular_inventory(path, ANGULAR_SOURCE))


class MainAdmissionControls(ExactRejection):
    """Actual main and disposable Git; only Angular process responses are controlled."""

    @classmethod
    def setUpClass(cls) -> None:
        validate_original_inputs()

    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="angular-main-controls-")
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        self.repository, self.product, self.product_tree, self.test_head = self.make_repository("intended")
        self.foreign, _, _, self.foreign_head = self.make_repository("foreign")
        self.sequence = 0

    def make_repository(self, name: str) -> tuple[Path, str, str, str]:
        repository = self.directory / name
        repository.mkdir()
        fixture_git(repository, "init", "--quiet", "--template=")
        (repository / "product.txt").write_text(name + " immutable Product fixture\n", encoding="utf-8")
        fixture_git(repository, "add", "--", "product.txt")
        fixture_git(repository, "commit", "--quiet", "-m", "Disposable Product fixture")
        product, product_tree = fixture_git(repository, "rev-parse", "HEAD"), fixture_git(repository, "rev-parse", "HEAD^{tree}")
        for source, raw in SOURCE_BYTES.items():
            output = repository / source
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_bytes(raw)
            fixture_git(repository, "add", "--", source)
        fixture_git(repository, "commit", "--quiet", "-m", "Disposable independent Test fixture")
        return repository, product, product_tree, fixture_git(repository, "rev-parse", "HEAD")

    def run_main(self, *, stage=None, mutate=None, reason=None, overrides=None, command_failure=False) -> None:
        self.sequence += 1
        # Match main's resolved paths before relocating the retained report.
        target = (self.directory / f"composed-{self.sequence}").resolve()
        evidence = (self.directory / f"evidence-{self.sequence}").resolve()
        original_foreign = file_snapshot(self.foreign)
        original_source = {path: (self.repository / path).read_bytes() for path in SOURCE_BYTES}
        real_run = RUNNER.run
        stages = []
        original_angular = json.loads(ANGULAR_REPORT)
        # Only relocate the retained file's physical path for this disposable main harness.
        # Case identities/counters stay native-derived; this is never Angular execution.
        original_angular["testResults"][0]["name"] = str(target / RUNNER.ANGULAR_DESTINATION)

        def controlled_run(args, cwd, log=None, *, preserve_output=False):
            if args[0] == "git":
                return real_run(args, cwd, log, preserve_output=preserve_output)
            if args[0] == "node":
                command_stage = "node"
            elif args[0] in ("npm", "npm.cmd"):
                command_stage = "npm-version" if args[1] == "--version" else "npm-ci" if args[1] == "ci" else "vitest"
            else:
                raise AssertionError("Unexpected controlled external command")
            stages.append(command_stage)
            output = "Controlled retained-output response; no Angular execution.\n"
            if command_stage == "node":
                output = "v26.1.0\n"
            elif command_stage == "npm-version":
                output = "11.17.0\n"
            elif command_stage == "vitest":
                (evidence / "vitest.json").write_text(json.dumps(original_angular), encoding="utf-8")
            if log is not None:
                log.write_text(output, encoding="utf-8")
            if command_stage == stage and mutate is not None:
                replacement = mutate(target, evidence)
                if isinstance(replacement, str):
                    output = replacement
                    if log is not None:
                        log.write_text(output, encoding="utf-8")
            if command_stage == stage and command_failure:
                raise RuntimeError("Original controlled command failed")
            return output if preserve_output else output.strip()

        supplied = {**fixture_environment(), **(overrides or {})}
        try:
            with patch.dict(os.environ, supplied, clear=True), patch.object(RUNNER, "ROOT", self.repository), \
                    patch.object(RUNNER, "PRODUCT_HEAD", self.product), patch.object(RUNNER, "PRODUCT_TREE", self.product_tree), \
                    patch.object(RUNNER, "run", controlled_run), \
                    patch.object(sys, "argv", ["compose_and_verify.py", "--product-head", self.product,
                                              "--worktree", str(target), "--evidence", str(evidence)]), \
                    redirect_stdout(io.StringIO()):
                if command_failure:
                    with self.assertRaisesRegex(RuntimeError, "^Original controlled command failed$"):
                        RUNNER.main()
                elif reason is not None:
                    self.reject(reason, RUNNER.main)
                else:
                    RUNNER.main()
                self.assertEqual(supplied, dict(os.environ))
            receipt = json.loads((evidence / "receipt.json").read_text(encoding="utf-8"))
            self.assertEqual("FAILED_OR_INCOMPLETE" if reason or command_failure else RUNNER.SUCCESS_STATUS,
                             receipt["status"])
            self.assertEqual(self.product, receipt["productHead"])
            self.assertEqual(self.test_head, receipt["testHead"])
            self.assertEqual(self.product_tree, receipt["productTree"])
            for flag in RUNNER.QUALIFICATION_FLAGS:
                self.assertIs(False, receipt[flag])
            if reason or command_failure:
                self.assertEqual(stage, stages[-1])
            else:
                self.assertEqual(["node", "npm-version", "npm-ci", "vitest"], stages)
                self.assertEqual(receipt["composedTree"], fixture_git(target, "write-tree"))
                self.assertEqual(sorted(RUNNER.ANGULAR_IDENTITIES), receipt["vitest"]["identities"])
                self.assertEqual(sorted(RUNNER.ANGULAR_IDENTITIES), receipt["expectedIdentities"])
                self.assertNotIn("xunit", receipt)
                self.assertEqual(RUNNER.SOURCE_PATH, receipt["sourcePath"])
                self.assertEqual(RUNNER.ANGULAR_DESTINATION, receipt["destination"])
                self.assertEqual(RUNNER.SOURCE_BLOB, receipt["sourceBlob"])
            for source, raw in original_source.items():
                self.assertEqual(raw, (self.repository / source).read_bytes())
                if reason is None and not command_failure:
                    self.assertEqual(raw, (target / RUNNER.SOURCES[source]).read_bytes())
                    self.assertEqual(RUNNER.SOURCE_BLOBS[source],
                                     receipt["sources"][RUNNER.SOURCES[source]]["gitBlob"])
            self.assertEqual("", fixture_git(self.repository, "status", "--porcelain", "--untracked-files=all"))
        finally:
            if target.exists():
                fixture_git(self.repository, "worktree", "remove", "--force", str(target))
            self.assertEqual(original_foreign, file_snapshot(self.foreign))

    def test_main_accepts_original_identities_and_preserves_all_false_flags(self) -> None:
        self.run_main()

    def test_main_resolves_disposable_path_alias_before_relocating_native_report(self) -> None:
        alias = self.directory / "alias"
        alias.mkdir()
        self.directory = alias / ".."
        self.assertNotEqual(self.directory, self.directory.resolve())
        self.run_main()

    def test_main_rechecks_source_after_every_external_command(self) -> None:
        for stage in ("node", "npm-version", "npm-ci", "vitest"):
            for destination in RUNNER.SOURCES.values():
                with self.subTest(stage=stage, destination=destination):
                    def change(target, evidence):
                        path = target / destination
                        path.write_bytes(path.read_bytes() + b"\n// Changed actual compiler input.\n")
                    self.run_main(stage=stage, mutate=change,
                                  reason="Composed working-tree Test source changed from the committed blob")

    def test_main_rejects_changed_product_head(self) -> None:
        def change(target, evidence):
            head = fixture_git(target, "commit-tree", self.product_tree, "-p", self.product,
                               "-m", "Different identity for the same Product tree")
            fixture_git(target, "update-ref", "--no-deref", "HEAD", head)
        self.run_main(stage="npm-ci", mutate=change, reason="Composed Product HEAD changed")

    def test_main_rejects_candidate_index_identity_and_mode_changes(self) -> None:
        for mode in ("100644", "100755"):
            def change(target, evidence):
                source, destination = next(iter(RUNNER.SOURCES.items()))
                blob = (fixture_git(target, "hash-object", "-w", "--stdin", data=b"Different staged source\n")
                        if mode == "100644" else RUNNER.SOURCE_BLOBS[source])
                fixture_git(target, "update-index", "--cacheinfo", mode, blob, destination)
            self.run_main(stage="vitest", mutate=change, reason="Composed candidate index identity or mode changed")

    def test_main_rejects_tracked_and_index_only_product_drift(self) -> None:
        for index_only in (False, True):
            def change(target, evidence):
                product = target / "product.txt"
                original = product.read_bytes()
                product.write_bytes(original + b"Unapproved Product change\n")
                if index_only:
                    fixture_git(target, "add", "--", "product.txt")
                    product.write_bytes(original)
            self.run_main(stage="npm-ci", mutate=change,
                          reason="Tracked Product source changed during independent composition")

    def test_main_rejects_untracked_compile_input(self) -> None:
        def change(target, evidence):
            output = target / "frontend/src/app/features/projectide/Injected.spec.ts"
            output.write_text("// Untracked compiler input.\n", encoding="utf-8")
        self.run_main(stage="npm-ci", mutate=change,
                      reason="Unexpected untracked source appeared during independent composition")

    def test_main_rejects_malformed_angular_output(self) -> None:
        def malformed_angular(target, evidence):
            path = evidence / "vitest.json"
            report = json.loads(path.read_text())
            report["numFailedTests"] = 1
            path.write_text(json.dumps(report), encoding="utf-8")
        self.run_main(stage="vitest", mutate=malformed_angular,
                      reason="Angular report must contain the exact complete passing Angular candidate roster")

    def test_main_preserves_command_failure(self) -> None:
        self.run_main(stage="vitest", command_failure=True)

    def test_git_redirect_is_read_only_and_all_main_git_operations_are_isolated(self) -> None:
        overrides = {"GIT_DIR": str(self.foreign / ".git"), "GIT_WORK_TREE": str(self.foreign)}
        before = file_snapshot(self.foreign)
        with patch.dict(os.environ, {**fixture_environment(), **overrides}, clear=True):
            unprotected = subprocess.run(["git", "rev-parse", "HEAD"], cwd=self.repository,
                                         capture_output=True, text=True, check=True).stdout.strip()
            self.assertEqual(self.foreign_head, unprotected)
            self.assertEqual(self.test_head, RUNNER.git(self.repository, "rev-parse", "HEAD"))
        self.assertEqual(before, file_snapshot(self.foreign))
        for supplied in (
            overrides, {"GIT_INDEX_FILE": str(self.foreign / ".git/index")},
            {"GIT_OBJECT_DIRECTORY": str(self.foreign / ".git/objects"), "GIT_COMMON_DIR": str(self.foreign / ".git")},
            {"GIT_CONFIG_COUNT": "1", "GIT_CONFIG_KEY_0": "core.worktree", "GIT_CONFIG_VALUE_0": str(self.foreign)},
            {"GIT_CONFIG_PARAMETERS": "'core.bare=true'"},
        ):
            with self.subTest(overrides=sorted(supplied)):
                self.run_main(overrides=supplied)

    def test_git_environment_preserves_non_git_host_settings(self) -> None:
        supplied = {"GIT_DIR": "foreign", "git_work_tree": "foreign", "GiT_INDEX_FILE": "foreign",
                    "PATH": "host-path", "DOTNET_CLI_UI_LANGUAGE": "en-US", "GITHUB_ACTIONS": "true"}
        with patch.dict(os.environ, supplied, clear=True):
            before = dict(os.environ)
            self.assertEqual({key: supplied[key] for key in ("PATH", "DOTNET_CLI_UI_LANGUAGE", "GITHUB_ACTIONS")},
                             RUNNER.local_git_environment())
            self.assertEqual(before, dict(os.environ))


class UntrackedInputControls(ExactRejection):
    """Actual disposable Git admission; no retained report or Angular execution."""

    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="angular-untracked-input-controls-")
        self.addCleanup(temporary.cleanup)
        self.repository = Path(temporary.name)
        fixture_git(self.repository, "init", "--quiet", "--template=")
        (self.repository / "product.txt").write_text("Original disposable Product.\n", encoding="utf-8")
        (self.repository / ".gitignore").write_text("data/\nnode_modules/\nfrontend/.angular/\nbin/\nobj/\n", encoding="utf-8")
        fixture_git(self.repository, "add", "--", "product.txt", ".gitignore")
        fixture_git(self.repository, "commit", "--quiet", "-m", "Disposable Angular Product")
        product = patch.object(RUNNER, "PRODUCT_HEAD", fixture_git(self.repository, "rev-parse", "HEAD"))
        product.start()
        self.addCleanup(product.stop)
        self.raw_sources = {}
        self.source_blobs = {}
        for source, destination in RUNNER.SOURCES.items():
            output = self.repository / destination
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_bytes(SOURCE_BYTES[source])
            fixture_git(self.repository, "add", "--", destination)
            self.raw_sources[destination] = SOURCE_BYTES[source]
            self.source_blobs[destination] = RUNNER.SOURCE_BLOBS[source]
        self.verify()

    def verify(self, phase: str = "source") -> None:
        RUNNER.verify_working_tree(self.repository, self.raw_sources, self.source_blobs, phase=phase)

    def test_ignored_compile_inputs_are_rejected_in_every_phase(self) -> None:
        for name in ("Injected.d.ts", "Injected.ts", "tsconfig.json"):
            relative = "frontend/src/app/data/" + name
            with self.subTest(path=relative):
                output = self.repository / relative
                output.parent.mkdir(parents=True, exist_ok=True)
                output.write_text("// Git ignored compiler input or configuration.\n", encoding="utf-8")
                self.assertEqual(relative, fixture_git(self.repository, "check-ignore", "--", relative))
                self.assertEqual("", fixture_git(self.repository, "ls-files", "--others", "--exclude-standard"))
                for phase in ("source", "dependencies", "tests"):
                    self.reject("Unexpected untracked source appeared during independent composition",
                                lambda: self.verify(phase))
                output.unlink()
                self.verify()

    def test_nested_dependency_and_cache_names_do_not_grant_source_exemptions(self) -> None:
        for relative in ("frontend/src/node_modules/Injected.d.ts",
                         "frontend/src/data/.angular/cache/Injected.d.ts",
                         "frontend/.angular/unapproved-output.d.ts",
                         "foreign/node_modules/Injected.js"):
            with self.subTest(path=relative):
                output = self.repository / relative
                output.parent.mkdir(parents=True, exist_ok=True)
                output.write_text("// Nested output names cannot authorize compiler source.\n", encoding="utf-8")
                self.assertEqual(relative, fixture_git(self.repository, "check-ignore", "--", relative))
                self.reject("Unexpected untracked source appeared during independent composition",
                            lambda: self.verify("tests"))
                output.unlink()
                self.verify()

    def test_only_exact_outputs_are_admitted_at_their_expected_phases(self) -> None:
        dependency = self.repository / "frontend/node_modules/example/index.js"
        dependency.parent.mkdir(parents=True)
        dependency.write_text("// Disposable installed dependency output.\n", encoding="utf-8")
        self.reject("Unexpected untracked source appeared during independent composition", self.verify)
        self.verify("dependencies")
        cache = self.repository / "frontend/.angular/cache/active/compiler.db"
        cache.parent.mkdir(parents=True)
        cache.write_bytes(b"Disposable Angular compiler cache.\n")
        self.reject("Unexpected untracked source appeared during independent composition",
                    lambda: self.verify("dependencies"))
        self.verify("tests")
        for destination, raw in self.raw_sources.items():
            self.assertEqual(raw, (self.repository / destination).read_bytes())


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence", required=True, type=Path)
    parser.add_argument("--report", required=True, type=Path)
    arguments, unittest_arguments = parser.parse_known_args()
    INPUT_DIRECTORY = arguments.evidence.resolve()
    report_path = arguments.report.resolve()
    if report_path == INPUT_DIRECTORY or INPUT_DIRECTORY in report_path.parents or report_path.exists():
        raise ValueError("Controls report must be a fresh file outside the unchanged Angular evidence directory")
    before = file_snapshot(INPUT_DIRECTORY)
    ANGULAR_REPORT = (INPUT_DIRECTORY / "vitest.json").read_bytes()
    INPUT_RECEIPT = json.loads((INPUT_DIRECTORY / "receipt.json").read_text(encoding="utf-8"))
    ANGULAR_SOURCE = json.loads(ANGULAR_REPORT)["testResults"][0]["name"]
    validate_original_inputs()
    print("Controls use the original retained Vitest report; no npm or Angular execution.", flush=True)
    program = unittest.main(argv=[sys.argv[0], *unittest_arguments], exit=False)
    unchanged = before == file_snapshot(INPUT_DIRECTORY)
    result = program.result
    report = {
        "scope": "RETAINED_REPORT_AND_DISPOSABLE_GIT_ADMISSION_CONTROLS_ONLY",
        "angularCommandsExecuted": False, "angularEvidenceUnchanged": unchanged,
        "testsRun": result.testsRun, "failures": len(result.failures), "errors": len(result.errors),
        "skipped": len(result.skipped), "status": "PASS" if result.wasSuccessful() and unchanged else "FAIL",
        "productHead": INPUT_RECEIPT["productHead"], "angularTestHead": INPUT_RECEIPT["testHead"],
        "originalAngularCases": RUNNER.ANGULAR_COUNT,
        "originalAngularSha256": hashlib.sha256(ANGULAR_REPORT).hexdigest(),
        **dict.fromkeys(RUNNER.QUALIFICATION_FLAGS, False),
    }
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print("ANGULAR_ADMISSION_CONTROLS_COMPLETE " + json.dumps({"status": report["status"], "testsRun": report["testsRun"],
        "originalAngularSha256": report["originalAngularSha256"]}, sort_keys=True), flush=True)
    sys.exit(0 if result.wasSuccessful() and unchanged else 1)
