#!/usr/bin/env python3
"""Retained Inspector admission, bootstrap and disposable SDK controls; no new Product cases."""
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
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("inspector_candidate_runner", HERE / "compose_and_verify.py")
assert SPEC is not None and SPEC.loader is not None
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)
NATIVE_TRX = b""
DISCOVERY = ""
ANGULAR_REPORT = b""
ANGULAR_SOURCE = ""
INPUT_RECEIPT = {}
INPUT_DIRECTORY = None
SOURCE_BYTES = {source: (HERE / Path(source).name).read_bytes() for source in RUNNER.SOURCES}
NS = RUNNER.TRX_NAMESPACE


def fixture_environment() -> dict[str, str]:
    # Independent of the subject under test, before any disposable Git write.
    return {key: value for key, value in os.environ.items() if not key.upper().startswith("GIT_")}


def fixture_git(repository: Path, *args: str, data: bytes | None = None) -> str:
    result = subprocess.run(
        ["git", "-c", "core.autocrlf=false", "-c", "commit.gpgsign=false",
         "-c", "core.hooksPath=" + str(repository / "empty-hooks"),
         "-c", "user.name=Inspector admission controls",
         "-c", "user.email=inspector-controls@example.invalid", *args],
        cwd=repository, env=fixture_environment(), input=data, capture_output=True, check=True)
    return result.stdout.decode("utf-8").strip()


def file_snapshot(directory: Path) -> dict[str, bytes]:
    return {str(path.relative_to(directory)): path.read_bytes()
            for path in directory.rglob("*") if path.is_file()}


def validate_original_inputs() -> None:
    if not NATIVE_TRX or not DISCOVERY or not ANGULAR_REPORT or not INPUT_RECEIPT:
        raise ValueError("Original Inspector TRX, discovery, Vitest and receipt inputs are required")
    if (INPUT_RECEIPT.get("productHead") != RUNNER.PRODUCT_HEAD
            or INPUT_RECEIPT.get("productTree") != RUNNER.PRODUCT_TREE
            or INPUT_RECEIPT.get("status") != "SYNTHETIC_HEADLESS_PASSED"):
        raise ValueError("Retained inputs do not identify the frozen successful Inspector composition")
    # Historical originals recorded four flags; current receipts record all six.
    for flag in RUNNER.QUALIFICATION_FLAGS:
        required = flag not in ("trustQualified", "releaseQualified")
        if (required or flag in INPUT_RECEIPT) and INPUT_RECEIPT.get(flag) is not False:
            raise ValueError("Retained report unexpectedly claims qualification")
    for destination, source in INPUT_RECEIPT["sources"].items():
        raw = SOURCE_BYTES[source["source"]]
        if (RUNNER.SOURCES[source["source"]] != destination
                or source["gitBlob"] != RUNNER.SOURCE_BLOBS[source["source"]]
                or source["sha256"] != hashlib.sha256(raw).hexdigest()):
            raise ValueError("Retained candidate source differs from the frozen original")
    if set(INPUT_RECEIPT["sources"]) != set(RUNNER.SOURCES.values()):
        raise ValueError("Retained source inventory does not contain both languages")
    for name, expected in INPUT_RECEIPT["retainedSha256"].items():
        if Path(name).name != name or hashlib.sha256((INPUT_DIRECTORY / name).read_bytes()).hexdigest() != expected:
            raise ValueError("Retained original evidence digest mismatch")
    with tempfile.TemporaryDirectory(prefix="inspector-original-inputs-") as temporary:
        directory = Path(temporary)
        trx, angular = directory / "original.trx", directory / "original.json"
        trx.write_bytes(NATIVE_TRX)
        angular.write_bytes(ANGULAR_REPORT)
        native = RUNNER.native_inventory(trx)
        if native != RUNNER.compiled_inventory(DISCOVERY) or native != INPUT_RECEIPT["xunit"]["identities"]:
            raise ValueError("Original native discovery or receipt identity mismatch")
        if RUNNER.angular_inventory(angular, ANGULAR_SOURCE) != INPUT_RECEIPT["vitest"]["identities"]:
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
        temporary = tempfile.TemporaryDirectory(prefix="inspector-report-controls-")
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        self.assertEqual(12, len(self.native(ET.fromstring(NATIVE_TRX))))
        self.assertEqual(11, len(self.angular(json.loads(ANGULAR_REPORT))))

    def native(self, root: ET.Element) -> list[str]:
        path = self.directory / "derived.trx"
        ET.ElementTree(root).write(path, encoding="utf-8", xml_declaration=True)
        return RUNNER.native_inventory(path)

    def angular(self, report: dict) -> list[str]:
        path = self.directory / "derived.json"
        path.write_text(json.dumps(report), encoding="utf-8")
        return RUNNER.angular_inventory(path, ANGULAR_SOURCE)

    def test_original_native_reports_and_frozen_source_blobs_match(self) -> None:
        validate_original_inputs()
        self.assertEqual(12, len(RUNNER.EXPECTED_IDENTITIES))
        self.assertEqual(11, len(RUNNER.ANGULAR_IDENTITIES))
        for source, raw in SOURCE_BYTES.items():
            blob = hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest()
            self.assertEqual(RUNNER.SOURCE_BLOBS[source], blob)

    def test_failed_summary_and_contradictory_counters_are_rejected(self) -> None:
        for key in ("total", "executed", "passed", *RUNNER.ZERO_COUNTERS, "unknownIncomplete"):
            with self.subTest(counter=key):
                root = ET.fromstring(NATIVE_TRX)
                self.native(root)
                root.find(NS + "ResultSummary/" + NS + "Counters").set(key, "1")
                self.reject("Native TRX must contain exactly twelve complete passing Inspector cases",
                            lambda: self.native(root))
        root = ET.fromstring(NATIVE_TRX)
        root.find(NS + "ResultSummary").set("outcome", "Failed")
        self.reject("Native TRX must contain exactly twelve complete passing Inspector cases", lambda: self.native(root))

    def test_missing_and_duplicate_native_containers_are_rejected(self) -> None:
        for name in ("Results", "TestDefinitions", "TestEntries", "ResultSummary"):
            for duplicate in (False, True):
                with self.subTest(container=name, duplicate=duplicate):
                    root = ET.fromstring(NATIVE_TRX)
                    self.native(root)
                    element = root.find(NS + name)
                    if duplicate:
                        root.append(copy.deepcopy(element))
                    else:
                        root.remove(element)
                    self.reject("Native TRX requires one " + name + " element", lambda: self.native(root))

    def test_wrong_native_namespace_is_rejected(self) -> None:
        root = ET.fromstring(NATIVE_TRX.replace(
            b"http://microsoft.com/schemas/VisualStudio/TeamTest/2010", b"urn:foreign-trx"))
        self.reject("Native TRX root or namespace is invalid", lambda: self.native(root))

    def test_foreign_duplicate_and_whitespace_native_names_are_rejected(self) -> None:
        for variant in ("foreign", "duplicate", "whitespace"):
            with self.subTest(variant=variant):
                root = ET.fromstring(NATIVE_TRX)
                self.native(root)
                cases = root.find(NS + "Results")
                name = {"foreign": "Foreign.Unrelated.Passed",
                        "duplicate": cases[1].get("testName"),
                        "whitespace": cases[0].get("testName") + " "}[variant]
                cases[0].set("testName", name)
                self.reject("Native TRX does not match the exact Inspector case roster", lambda: self.native(root))

    def test_missing_duplicate_and_nonbijective_native_ids_are_rejected(self) -> None:
        for container, attribute in (("Results", "executionId"), ("Results", "testId"),
                                     ("TestEntries", "executionId"), ("TestEntries", "testId")):
            for variant in ("missing", "duplicate", "foreign"):
                with self.subTest(container=container, attribute=attribute, variant=variant):
                    root = ET.fromstring(NATIVE_TRX)
                    self.native(root)
                    rows = root.find(NS + container)
                    value = {"missing": "", "duplicate": rows[1].get(attribute),
                             "foreign": "00000000-0000-0000-0000-000000009999"}[variant]
                    rows[0].set(attribute, value)
                    reason = ("Native TRX result, definition and entry identities are not bijective"
                              if variant == "foreign" else
                              "Native TRX contains missing or duplicate test/execution identities")
                    self.reject(reason, lambda: self.native(root))

    def test_wrong_class_and_display_used_as_bare_method_are_rejected(self) -> None:
        for variant in ("foreign-class", "full-display-method", "split-class-method", "wrong-bare-method"):
            with self.subTest(variant=variant):
                root = ET.fromstring(NATIVE_TRX)
                definition = root.find(NS + "TestDefinitions")[0]
                method = definition.find(NS + "TestMethod")
                if variant == "foreign-class":
                    method.set("className", "Foreign.Class")
                elif variant == "full-display-method":
                    method.set("name", definition.get("name"))
                elif variant == "split-class-method":
                    method.set("className", "Coglatas.Tests.ProjectIde")
                    method.set("name", "InspectorIdentityCandidateTests." + method.get("name"))
                else:
                    method.set("name", "ForeignBareMethod")
                self.reject("Native TRX method metadata disagrees with its frozen case identity",
                            lambda: self.native(root))

    def test_native_definition_names_cannot_be_rebound_to_other_results(self) -> None:
        root = ET.fromstring(NATIVE_TRX)
        definitions = [row for row in root.find(NS + "TestDefinitions")
                       if "ForeignLocationCannotReuseTheAcceptedFinding(" in row.get("name")]
        first, second = definitions[:2]
        first_name = first.get("name")
        first.set("name", second.get("name"))
        second.set("name", first_name)
        self.reject("Native TRX definition names disagree with their original results", lambda: self.native(root))

    def test_native_error_information_is_rejected_despite_passed_outcomes(self) -> None:
        root = ET.fromstring(NATIVE_TRX)
        ET.SubElement(root.find(NS + "Results")[0], NS + "ErrorInfo").text = "Contradictory native failure"
        self.reject("Native TRX contains contradictory failure information", lambda: self.native(root))

    def test_nested_native_results_are_rejected_with_unchanged_passing_roster(self) -> None:
        for outcome in ("Passed", "NotExecuted", "Failed"):
            for location in ("Results", "TestDefinitions", "TestEntries", "ResultSummary", "TestRun"):
                for kind in ("UnitTestResult", "GenericTestResult", "TestResultAggregation"):
                    for namespace in (NS, "{urn:foreign-trx}"):
                        for wrapped in (False, True):
                            with self.subTest(outcome=outcome, location=location, kind=kind,
                                              namespace=namespace, wrapped=wrapped):
                                root = ET.fromstring(NATIVE_TRX)
                                record = root if location == "TestRun" else root.find(NS + location)
                                if location in ("Results", "TestDefinitions", "TestEntries"):
                                    record = record[0]
                                parent = ET.SubElement(record, namespace + "InnerResults") if wrapped else record
                                ET.SubElement(parent, namespace + kind, outcome=outcome,
                                              testName="Foreign.HiddenResult", testId="hidden", executionId="hidden")
                                self.reject("Native TRX contains nested result records", lambda: self.native(root))

    def test_run_level_failures_cannot_hide_behind_passing_counters(self) -> None:
        positive = ET.fromstring(NATIVE_TRX)
        infos = ET.SubElement(positive.find(NS + "ResultSummary"), NS + "RunInfos")
        ET.SubElement(infos, NS + "RunInfo", outcome="Information")
        self.assertEqual(sorted(RUNNER.EXPECTED_IDENTITIES), self.native(positive))
        for outcome in ("Failed", "Error", "Aborted", "Timeout", "NotExecuted", "Warning", "Passed", "Completed", "unknown"):
            with self.subTest(outcome=outcome):
                root = ET.fromstring(NATIVE_TRX)
                infos = ET.SubElement(root.find(NS + "ResultSummary"), NS + "RunInfos")
                ET.SubElement(infos, NS + "RunInfo", outcome=outcome)
                self.reject("Native TRX contains contradictory failure information", lambda: self.native(root))

    def test_information_messages_are_bound_to_native_result_summary_only(self) -> None:
        root = ET.fromstring(NATIVE_TRX)
        summary = root.find(NS + "ResultSummary")
        infos = ET.SubElement(summary, NS + "RunInfos")
        ET.SubElement(infos, NS + "RunInfo", outcome="Information")
        self.assertEqual(sorted(RUNNER.EXPECTED_IDENTITIES), self.native(root))
        for location in ("root", "case", "definition", "direct-summary", "foreign-summary", "empty-case"):
            with self.subTest(location=location):
                root = ET.fromstring(NATIVE_TRX)
                summary = root.find(NS + "ResultSummary")
                if location == "direct-summary":
                    ET.SubElement(summary, NS + "RunInfo", outcome="Information")
                else:
                    parent = {"root": root, "case": root.find(NS + "Results")[0],
                              "definition": root.find(NS + "TestDefinitions")[0],
                              "foreign-summary": summary,
                              "empty-case": root.find(NS + "Results")[0]}[location]
                    namespace = "{urn:foreign-trx}" if location == "foreign-summary" else NS
                    container = ET.SubElement(parent, namespace + "RunInfos")
                    if location != "empty-case":
                        ET.SubElement(container, namespace + "RunInfo", outcome="Information")
                self.reject("Native TRX contains contradictory failure information", lambda: self.native(root))

    def test_duplicate_or_unsupported_summary_run_infos_are_refused(self) -> None:
        for change in ("duplicate", "unsupported-child"):
            with self.subTest(change=change):
                root = ET.fromstring(NATIVE_TRX)
                summary = root.find(NS + "ResultSummary")
                infos = ET.SubElement(summary, NS + "RunInfos")
                ET.SubElement(infos, NS + "RunInfo", outcome="Information")
                if change == "duplicate":
                    another = ET.SubElement(summary, NS + "RunInfos")
                    ET.SubElement(another, NS + "RunInfo", outcome="Information")
                else:
                    ET.SubElement(infos, "{urn:foreign-trx}Opaque")
                self.reject("Native TRX contains contradictory failure information", lambda: self.native(root))

    def test_discovery_rejects_longer_substrings_foreign_extra_and_missing_cases(self) -> None:
        names = sorted(RUNNER.EXPECTED_IDENTITIES)
        variants = {
            "longer-substring": DISCOVERY.replace(names[0], names[0] + "_Unexpected"),
            "foreign-extra": DISCOVERY + "\n    Foreign.Unrelated.Passed\n",
            "missing": DISCOVERY.replace("    " + names[0], ""),
            "duplicate": DISCOVERY.replace(names[0], names[1]),
            "whitespace": DISCOVERY.replace(names[0], names[0] + " "),
        }
        for name, listing in variants.items():
            with self.subTest(variant=name):
                self.assertEqual(names, RUNNER.compiled_inventory(DISCOVERY))
                self.reject("Compiled discovery does not match the exact twelve-case Inspector roster",
                            lambda: RUNNER.compiled_inventory(listing))

    def test_discovery_requires_its_header_and_exact_record_indentation(self) -> None:
        marker = "The following Tests are available:"
        for listing in (DISCOVERY.replace(marker, ""), DISCOVERY + "\n" + marker):
            self.reject("Compiled discovery requires one native inventory header",
                        lambda: RUNNER.compiled_inventory(listing))
        self.reject("Compiled discovery contains unsupported inventory records",
                    lambda: RUNNER.compiled_inventory(DISCOVERY + "\n\tForeign.Hidden.Passed\n"))

    def test_angular_counters_errors_and_success_are_consistent(self) -> None:
        original = json.loads(ANGULAR_REPORT)
        for key in ("numTotalTestSuites", "numPassedTestSuites", "numFailedTestSuites", "numPendingTestSuites",
                    "numTotalTests", "numPassedTests", "numFailedTests", "numPendingTests", "numTodoTests"):
            with self.subTest(counter=key):
                report = copy.deepcopy(original)
                report[key] = report[key] + 1
                self.reject("Angular report must contain exactly eleven complete passing Inspector cases",
                            lambda: self.angular(report))
        for key, value in (("success", False), ("errors", ["unhandled failure"]), ("numTodoTests", False)):
            report = copy.deepcopy(original)
            report[key] = value
            self.reject("Angular report must contain exactly eleven complete passing Inspector cases",
                        lambda: self.angular(report))

    def test_angular_requires_one_exact_source_filename(self) -> None:
        report = json.loads(ANGULAR_REPORT)
        report["testResults"].append(copy.deepcopy(report["testResults"][0]))
        self.reject("Angular report requires exactly one Inspector source file", lambda: self.angular(report))
        for filename in ("Foreign.spec.ts", ANGULAR_SOURCE + ".other", "/foreign/" + RUNNER.ANGULAR_DESTINATION):
            report = json.loads(ANGULAR_REPORT)
            report["testResults"][0]["name"] = filename
            self.reject("Angular report source filename differs from the composed candidate", lambda: self.angular(report))

    def test_angular_foreign_or_duplicate_assertion_names_are_rejected(self) -> None:
        for duplicate in (False, True):
            report = json.loads(ANGULAR_REPORT)
            cases = report["testResults"][0]["assertionResults"]
            cases[0]["fullName"] = cases[1]["fullName"] if duplicate else "Foreign unrelated passing assertion"
            self.reject("Angular report does not match the exact Inspector assertion roster", lambda: self.angular(report))

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
    """Actual main and disposable Git; only native process responses are controlled."""

    @classmethod
    def setUpClass(cls) -> None:
        validate_original_inputs()

    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="inspector-main-controls-")
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
        (repository / " spaced\u2028product.txt").write_text("Raw NUL path control\n", encoding="utf-8")
        (repository / ".gitignore").write_text("bin/\nobj/\nnode_modules/\n.angular/\ndata/\n.local/\n", encoding="utf-8")
        project = repository / "tests/Coglatas.Tests/Coglatas.Tests.csproj"
        project.parent.mkdir(parents=True)
        project.write_text('<Project Sdk="Microsoft.NET.Sdk" />\n', encoding="utf-8")
        fixture_git(repository, "add", "--force", "--", "product.txt", " spaced\u2028product.txt", ".gitignore", "tests/Coglatas.Tests/Coglatas.Tests.csproj")
        fixture_git(repository, "commit", "--quiet", "-m", "Disposable Product fixture")
        product, product_tree = fixture_git(repository, "rev-parse", "HEAD"), fixture_git(repository, "rev-parse", "HEAD^{tree}")
        for source, raw in SOURCE_BYTES.items():
            output = repository / source
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_bytes(raw)
            fixture_git(repository, "add", "--force", "--", source)
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
        # Case identities/counters stay native-derived; this is never native execution.
        original_angular["testResults"][0]["name"] = str(target / RUNNER.ANGULAR_DESTINATION)

        def controlled_run(args, cwd, log=None, *, preserve_output=False):
            if args[0] == "git":
                return real_run(args, cwd, log, preserve_output=preserve_output)
            if args[0] == "dotnet":
                command_stage = "sdk" if args[1] == "--version" else "discovery" if "--list-tests" in args else args[1]
                command_stage = "xunit" if command_stage == "test" else command_stage
            elif args[0] in ("npm", "npm.cmd"):
                command_stage = "npm-ci" if args[1] == "ci" else "vitest"
            else:
                raise AssertionError("Unexpected controlled external command")
            stages.append(command_stage)
            output = "Controlled retained-output response; no native execution.\n"
            if command_stage == "sdk":
                output = "10.0.403\n"
            elif command_stage == "xunit":
                (evidence / "inspector.trx").write_bytes(NATIVE_TRX)
            elif command_stage == "discovery":
                output = DISCOVERY
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
            self.assertEqual("FAILED_OR_INCOMPLETE" if reason or command_failure else "SYNTHETIC_HEADLESS_PASSED",
                             receipt["status"])
            self.assertEqual(self.product, receipt["productHead"])
            self.assertEqual(self.test_head, receipt["testHead"])
            self.assertEqual(self.product_tree, receipt["productTree"])
            for flag in RUNNER.QUALIFICATION_FLAGS:
                self.assertIs(False, receipt[flag])
            if reason or command_failure:
                self.assertEqual(stage, stages[-1])
            else:
                self.assertEqual(["sdk", "restore", "xunit", "discovery", "npm-ci", "vitest"], stages)
                self.assertEqual(receipt["composedTree"], fixture_git(target, "write-tree"))
                self.assertEqual(sorted(RUNNER.EXPECTED_IDENTITIES), receipt["xunit"]["identities"])
                self.assertEqual(sorted(RUNNER.ANGULAR_IDENTITIES), receipt["vitest"]["identities"])
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

    def test_main_rechecks_both_source_files_after_every_external_command(self) -> None:
        for stage in ("sdk", "restore", "xunit", "discovery", "npm-ci", "vitest"):
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
        self.run_main(stage="restore", mutate=change, reason="Composed Product HEAD changed")

    def test_main_rejects_candidate_index_identity_and_mode_changes(self) -> None:
        for mode in ("100644", "100755"):
            def change(target, evidence):
                source, destination = next(iter(RUNNER.SOURCES.items()))
                blob = (fixture_git(target, "hash-object", "-w", "--stdin", data=b"Different staged source\n")
                        if mode == "100644" else RUNNER.SOURCE_BLOBS[source])
                fixture_git(target, "update-index", "--cacheinfo", mode, blob, destination)
            self.run_main(stage="xunit", mutate=change, reason="Composed candidate index identity or mode changed")

    def test_main_rejects_tracked_and_index_only_product_drift(self) -> None:
        for index_only in (False, True):
            def change(target, evidence):
                product = target / "product.txt"
                original = product.read_bytes()
                product.write_bytes(original + b"Unapproved Product change\n")
                if index_only:
                    fixture_git(target, "add", "--force", "--", "product.txt")
                    product.write_bytes(original)
            self.run_main(stage="npm-ci", mutate=change,
                          reason="Tracked Product source changed during independent composition")

    def test_main_rejects_untracked_compile_input(self) -> None:
        def change(target, evidence):
            output = target / "tests/Coglatas.Tests/Injected.cs"
            output.write_text("// Untracked compiler input.\n", encoding="utf-8")
        self.run_main(stage="restore", mutate=change,
                      reason="Unexpected untracked source appeared during independent composition")

    def test_index_masks_cannot_hide_changed_product_bytes(self) -> None:
        for flag in ("assume-unchanged", "skip-worktree"):
            for stage in ("restore", "xunit", "discovery", "npm-ci", "vitest"):
                with self.subTest(flag=flag, stage=stage):
                    def change(target, evidence):
                        path = target / " spaced\u2028product.txt"
                        original_blob = fixture_git(target, "rev-parse", ": spaced\u2028product.txt")
                        fixture_git(target, "update-index", "--" + flag, "--", " spaced\u2028product.txt")
                        path.write_bytes(path.read_bytes() + b"Hidden changed Product input\n")
                        self.assertEqual(original_blob, fixture_git(target, "rev-parse", ": spaced\u2028product.txt"))
                        self.assertEqual("", fixture_git(target, "diff", "--name-only", "--"))
                    self.run_main(stage=stage, mutate=change, reason="Git index flags hide tracked Product source")

    def test_ignored_native_and_angular_compile_inputs_are_rejected(self) -> None:
        for relative in ("tests/Coglatas.Tests/data/Injected.cs",
                         "frontend/src/app/.local/Injected.ts",
                         "tests/Coglatas.Tests/ProjectIde/bin/Injected.cs",
                         "frontend/src/app/node_modules/Injected.ts"):
            for stage in ("restore", "npm-ci", "vitest"):
                with self.subTest(relative=relative, stage=stage):
                    def change(target, evidence):
                        output = target / relative
                        output.parent.mkdir(parents=True, exist_ok=True)
                        output.write_text("// Ignored source is still a compiler input.\n", encoding="utf-8")
                        self.assertEqual(relative, fixture_git(target, "check-ignore", "--", relative))
                        self.assertEqual("", fixture_git(target, "ls-files", "--others", "--exclude-standard"))
                    self.run_main(stage=stage, mutate=change,
                                  reason="Unexpected untracked source appeared during independent composition")

    def test_known_outputs_are_admitted_only_after_their_own_phase(self) -> None:
        for stage, relative in (("restore", "tests/Coglatas.Tests/obj/GeneratedAssemblyInfo.cs"),
                                ("xunit", "tests/Coglatas.Tests/bin/Release/result.dll"),
                                ("npm-ci", "frontend/node_modules/package/index.js"),
                                ("vitest", "frontend/.angular/cache/result.bin")):
            with self.subTest(stage=stage, relative=relative):
                def create(target, evidence):
                    output = target / relative
                    output.parent.mkdir(parents=True, exist_ok=True)
                    output.write_text("Disposable output control\n", encoding="utf-8")
                self.run_main(stage=stage, mutate=create)
        for stage, relative in (("sdk", "tests/Coglatas.Tests/obj/Injected.cs"),
                                ("restore", "frontend/node_modules/package/index.js"),
                                ("npm-ci", "frontend/.angular/cache/result.bin")):
            with self.subTest(stage=stage, relative=relative):
                def create(target, evidence):
                    output = target / relative
                    output.parent.mkdir(parents=True, exist_ok=True)
                    output.write_text("Unexpected premature output\n", encoding="utf-8")
                self.run_main(stage=stage, mutate=create,
                              reason="Unexpected untracked source appeared during independent composition")

    def test_main_rejects_malformed_native_discovery_and_angular_outputs(self) -> None:
        def malformed_native(target, evidence):
            path = evidence / "inspector.trx"
            root = ET.fromstring(path.read_bytes())
            root.find(NS + "ResultSummary").set("outcome", "Failed")
            ET.ElementTree(root).write(path, encoding="utf-8")
        self.run_main(stage="xunit", mutate=malformed_native,
                      reason="Native TRX must contain exactly twelve complete passing Inspector cases")
        self.run_main(stage="discovery", mutate=lambda target, evidence: DISCOVERY + "\n    Foreign.Extra.Passed\n",
                      reason="Compiled discovery does not match the exact twelve-case Inspector roster")
        def malformed_angular(target, evidence):
            path = evidence / "vitest.json"
            report = json.loads(path.read_text())
            report["numFailedTests"] = 1
            path.write_text(json.dumps(report), encoding="utf-8")
        self.run_main(stage="vitest", mutate=malformed_angular,
                      reason="Angular report must contain exactly eleven complete passing Inspector cases")

    def test_main_preserves_command_failure(self) -> None:
        self.run_main(stage="xunit", command_failure=True)

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

    def test_staged_product_deletion_cannot_hide_as_candidate_rename(self) -> None:
        original = "tests/Coglatas.Tests/OriginalInspector.cs"
        fixture_git(self.repository, "checkout", "--quiet", "--detach", self.product)
        source = next(path for path in SOURCE_BYTES if path.endswith(".cs"))
        (self.repository / original).write_bytes(SOURCE_BYTES[source])
        fixture_git(self.repository, "add", "--force", "--", original)
        fixture_git(self.repository, "commit", "--quiet", "-m", "Product source with candidate-identical bytes")
        self.product = fixture_git(self.repository, "rev-parse", "HEAD")
        self.product_tree = fixture_git(self.repository, "rev-parse", "HEAD^{tree}")
        for path, raw in SOURCE_BYTES.items():
            output = self.repository / path
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_bytes(raw)
            fixture_git(self.repository, "add", "--force", "--", path)
        fixture_git(self.repository, "commit", "--quiet", "-m", "Independent Test sources")
        self.test_head = fixture_git(self.repository, "rev-parse", "HEAD")
        fixture_git(self.repository, "config", "diff.renames", "true")
        self.run_main()
        def remove_product_source(target, evidence):
            fixture_git(target, "rm", "--", original)
            expected = sorted(RUNNER.SOURCES.values())
            self.assertEqual(expected, fixture_git(target, "diff", "--cached", "--name-only", "HEAD", "--").splitlines())
            self.assertEqual(sorted([*expected, original]), fixture_git(
                target, "diff", "--cached", "--no-renames", "--name-only", "HEAD", "--").splitlines())
        self.run_main(stage="restore", mutate=remove_product_source,
                      reason="Tracked Product source changed during independent composition")
        self.run_main()



SDK_IMPORT_CONTROLS_EXECUTED = False
POWERSHELL_CONTROL_METHODS: set[str] = set()


class SdkImportControls(unittest.TestCase):
    """Exercise real SDK import behavior with a disposable project, never Product tests."""

    def test_restore_test_and_discovery_exclude_external_props_and_targets(self) -> None:
        global SDK_IMPORT_CONTROLS_EXECUTED
        if shutil.which("dotnet") is None:
            if os.environ.get("GITHUB_ACTIONS") == "true":
                self.fail("Hosted SDK import controls require dotnet")
            self.skipTest("dotnet is unavailable; hosted SDK import execution remains required")
        with tempfile.TemporaryDirectory(prefix="source-only-sdk-controls-") as temporary:
            parent = Path(temporary)
            product = parent / "product"
            product.mkdir()
            (product / "global.json").write_text(json.dumps({
                "sdk": {"version": "10.0.403", "rollForward": "disable", "allowPrerelease": False}
            }), encoding="utf-8")
            version = subprocess.run(["dotnet", "--version"], cwd=product,
                                     capture_output=True, text=True, timeout=30)
            self.assertEqual(0, version.returncode, version.stdout + version.stderr)
            self.assertEqual("10.0.403", version.stdout.strip())
            project = product / "Control.csproj"
            project.write_text(
                '<Project><Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />'
                '<PropertyGroup><TargetFramework>net10.0</TargetFramework>'
                '<EnableDefaultItems>false</EnableDefaultItems></PropertyGroup>'
                '<Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" />'
                '<Target Name="VSTest"><Message Text="SOURCE_ONLY_SDK_CONTROL" Importance="high" />'
                '</Target></Project>', encoding="utf-8")
            (product / "NuGet.Config").write_text(
                '<configuration><packageSources><clear /></packageSources></configuration>', encoding="utf-8")
            for filename in ("Directory.Build.props", "Directory.Build.targets"):
                external = parent / filename
                external.write_text(
                    '<Project><Target Name="UnreviewedAncestorInput" BeforeTargets="Restore;VSTest">'
                    '<Error Text="UNREVIEWED_ANCESTOR_IMPORT" /></Target></Project>', encoding="utf-8")
                for action, arguments in (
                    ("restore", (str(project), "--nologo")),
                    ("test", (str(project), "--no-restore", "--nologo")),
                    ("test", (str(project), "--no-build", "--list-tests", "--nologo")),
                ):
                    with self.subTest(filename=filename, action=action, arguments=arguments):
                        unprotected = subprocess.run(["dotnet", action, *arguments], cwd=product,
                                                     capture_output=True, text=True, timeout=90)
                        self.assertNotEqual(0, unprotected.returncode)
                        self.assertIn("UNREVIEWED_ANCESTOR_IMPORT", unprotected.stdout + unprotected.stderr)
                        protected = subprocess.run(RUNNER.source_only_dotnet(action, *arguments), cwd=product,
                                                   capture_output=True, text=True, timeout=90)
                        self.assertEqual(0, protected.returncode, protected.stdout + protected.stderr)
                        self.assertNotIn("UNREVIEWED_ANCESTOR_IMPORT", protected.stdout + protected.stderr)
                external.unlink()
        SDK_IMPORT_CONTROLS_EXECUTED = True


class WorkflowBootstrapControls(unittest.TestCase):
    """Execute the actual advisory bootstrap over disposable helper bytes, not Product code."""

    HELPER = "tests/candidates/workflow-provenance/verify_workflow_provenance.py"
    HELPER_BYTES = (b"import json, os, sys\nfrom pathlib import Path\n"
                    b"Path(os.environ['BOOTSTRAP_RESULT']).write_text(json.dumps({'isolated': sys.flags.isolated}))\n")

    def setUp(self) -> None:
        self.pwsh = shutil.which("pwsh")
        if self.pwsh is None:
            if os.environ.get("GITHUB_ACTIONS") == "true":
                self.fail("Hosted workflow bootstrap controls require PowerShell")
            self.skipTest("PowerShell is unavailable; hosted bootstrap execution remains required")
        temporary = tempfile.TemporaryDirectory(prefix="workflow-bootstrap-controls-")
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        self.repository = self.directory / "source"
        self.repository.mkdir()
        fixture_git(self.repository, "init", "--quiet", "--template=")
        self.helper = self.repository / self.HELPER
        self.helper.parent.mkdir(parents=True)
        self.helper.write_bytes(self.HELPER_BYTES)
        fixture_git(self.repository, "add", "--force", "--", self.HELPER)
        fixture_git(self.repository, "commit", "--quiet", "-m", "Disposable pinned helper")
        self.pinned = fixture_git(self.repository, "rev-parse", "HEAD:" + self.HELPER)
        self.result = self.directory / "executed.json"
        self.shadow = self.directory / "shadow-loaded.txt"
        workflow = Path(__file__).resolve().parents[3] / ".github/workflows/projectide-inspector-composition-advisory.yml"
        self.scripts = re.findall(
            r"      - name: Bind the immutable executing workflow to the reviewed Test source\n"
            r"        shell: pwsh\n        run: \|\n((?:          [^\n]*\n)+)",
            workflow.read_text(encoding="utf-8"))
        self.assertEqual(2, len(self.scripts), "Both hosted bootstrap blocks must be exercised")

    def execute(self, script: str) -> subprocess.CompletedProcess:
        path = self.directory / "bootstrap.ps1"
        path.write_text("\n".join(line[10:] for line in script.splitlines()) + "\n", encoding="utf-8")
        environment = {key: value for key, value in os.environ.items()
                       if not key.upper().startswith("GIT_")}
        environment.update(PROVENANCE_BLOB=self.pinned, RUNNER_TEMP=str(self.directory),
                           BOOTSTRAP_RESULT=str(self.result), BOOTSTRAP_SHADOW=str(self.shadow))
        return subprocess.run([self.pwsh, "-NoLogo", "-NoProfile", "-NonInteractive", "-File", str(path)],
                              cwd=self.repository, env=environment, capture_output=True, text=True, timeout=30)

    def test_changed_helper_is_refused_before_any_helper_instruction(self) -> None:
        changed = self.HELPER_BYTES + b"# Different executable bytes.\n"
        for kind in ("working", "normalized-working", "committed"):
            with self.subTest(kind=kind):
                fixture_git(self.repository, "config", "core.autocrlf", "false")
                self.helper.write_bytes(changed)
                if kind == "normalized-working":
                    fixture_git(self.repository, "config", "core.autocrlf", "true")
                    self.helper.write_bytes(self.HELPER_BYTES.replace(b"\n", b"\r\n"))
                    self.assertEqual(self.pinned, fixture_git(
                        self.repository, "-c", "core.autocrlf=true", "hash-object", "--", self.HELPER))
                if kind == "committed":
                    fixture_git(self.repository, "add", "--force", "--", self.HELPER)
                    fixture_git(self.repository, "commit", "--quiet", "-m", "Different committed helper")
                for script in self.scripts:
                    result = self.execute(script)
                    self.assertNotEqual(0, result.returncode)
                    reason = "Provenance helper blob mismatch" if kind == "committed" else "Provenance helper working bytes mismatch"
                    self.assertIn(reason, result.stdout + result.stderr)
                    self.assertFalse(self.result.exists(), "Changed executable must not reach its first instruction")

    def test_original_helper_load_ignores_neighboring_import_shadow(self) -> None:
        shadow = self.helper.with_name("json.py")
        shadow.write_text("import os\nfrom pathlib import Path\n"
                          "Path(os.environ['BOOTSTRAP_SHADOW']).write_text('shadow executed')\n"
                          "raise RuntimeError('Neighboring import shadow executed')\n", encoding="utf-8")
        for script in self.scripts:
            result = self.execute(script)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertEqual({"isolated": 1}, json.loads(self.result.read_text(encoding="utf-8")))
            self.assertFalse(self.shadow.exists())
            self.result.unlink()


    def tearDown(self) -> None:
        POWERSHELL_CONTROL_METHODS.add(type(self).__name__ + "." + self._testMethodName)



class WorkflowExecutionControls(unittest.TestCase):
    """Execute real guarded blocks with a distinct identity for every pinned script."""

    SCRIPTS = ('tests/candidates/projectide-inspector/compose_and_verify.py', 'tests/candidates/projectide-inspector/test_compose_and_verify.py', 'tests/candidates/workflow-provenance/verify_workflow_provenance.py', 'tests/candidates/workflow-provenance/test_workflow_provenance.py')
    PIN_KEYS = ('RUNNER_BLOB', 'CONTROLS_BLOB', 'PROVENANCE_BLOB', 'PROVENANCE_CONTROLS_BLOB')

    def setUp(self) -> None:
        if shutil.which("pwsh") is None:
            if os.environ.get("GITHUB_ACTIONS") == "true":
                self.fail("Hosted workflow execution controls require PowerShell")
            self.skipTest("PowerShell is unavailable; hosted execution controls remain required")
        temporary = tempfile.TemporaryDirectory(prefix="workflow-execution-controls-")
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        self.repository = self.directory / "repository"
        self.repository.mkdir()
        self.git("init", "--quiet", "--template=")
        self.canaries = {}
        for relative in self.SCRIPTS:
            raw = ("import json, os, sys\nfrom pathlib import Path\n"
                   "output = Path(os.environ['CONTROL_OUTPUT'])\n"
                   "observed = json.loads(output.read_text()) if output.exists() else []\n"
                   + "observed.append({'script': " + repr(relative) + ", 'isolated': sys.flags.isolated})\n"
                   + "output.write_text(json.dumps(observed))\n").encode("utf-8")
            self.canaries[relative] = raw
            target = self.repository / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(raw)
        self.git("add", "--force", "--", *self.SCRIPTS)
        self.git("commit", "--quiet", "-m", "Distinct disposable Python execution canaries")
        self.head = self.git("rev-parse", "HEAD").strip()
        self.blobs = {relative: self.git("rev-parse", "HEAD:" + relative).strip()
                      for relative in self.SCRIPTS}
        self.assertEqual(len(self.SCRIPTS), len(set(self.blobs.values())))
        self.assertNotIn(self.head, self.blobs.values())
        workflow = RUNNER.ROOT / ".github/workflows/projectide-inspector-composition-advisory.yml"
        workflow_text = workflow.read_text(encoding="utf-8")
        for key, relative in zip(self.PIN_KEYS, self.SCRIPTS):
            expected = fixture_git(RUNNER.ROOT, "hash-object", "--no-filters", "--", relative)
            pins = re.findall(rf"(?m)^[ \t]+{key}: ([0-9a-f]+)$", workflow_text)
            self.assertTrue(pins)
            self.assertEqual({expected}, set(pins))
        lines = workflow_text.splitlines()
        self.blocks = []
        roles = []
        for index, line in enumerate(lines):
            command = line.strip()
            if command.startswith("python -I -B " + self.SCRIPTS[0] + " "):
                required, executed, role = (self.SCRIPTS[0],), (self.SCRIPTS[0],), "runner"
            elif command.startswith("python -I -B " + self.SCRIPTS[1] + " "):
                required, executed, role = self.SCRIPTS[:2], (self.SCRIPTS[1],), "controls"
            elif command.startswith("python -I -B -c ") and self.SCRIPTS[3] in command:
                required, executed, role = self.SCRIPTS[2:], self.SCRIPTS[2:], "shared"
            else:
                continue
            first = index
            while first and lines[first - 1].strip().startswith(("$committed =", "$working =", "if ($LASTEXITCODE")):
                first -= 1
            while first < index and not lines[first].strip().startswith("$committed ="):
                first += 1
            block = "\n".join(item[10:] for item in lines[first:index + 1])
            for relative in required:
                key = self.PIN_KEYS[self.SCRIPTS.index(relative)]
                self.assertIn(f'$committed = git rev-parse "$($env:TEST_HEAD):{relative}"', block)
                self.assertIn(f"$committed -ne $env:{key})", block)
                self.assertIn(f"$working = git hash-object --no-filters -- '{relative}'", block)
                self.assertIn(f"$working -ne $env:{key})", block)
            self.blocks.append((block, required, executed))
            roles.append(role)
        self.assertEqual(['controls', 'controls', 'runner', 'runner', 'shared', 'shared'], sorted(roles), "Both hosts must bind every required script")

    def tearDown(self) -> None:
        POWERSHELL_CONTROL_METHODS.add(type(self).__name__ + "." + self._testMethodName)

    def git(self, *args: str) -> str:
        environment = {key: value for key, value in os.environ.items()
                       if not key.upper().startswith("GIT_")}
        result = subprocess.run(
            ["git", "-c", "core.autocrlf=false", "-c", "commit.gpgsign=false",
             "-c", "core.hooksPath=" + str(self.repository / "empty-hooks"),
             "-c", "user.name=Workflow execution controls",
             "-c", "user.email=execution-controls@example.invalid", *args],
            cwd=self.repository, env=environment, capture_output=True, text=True, check=True)
        return result.stdout

    def execute(self, block: str) -> tuple[subprocess.CompletedProcess, Path]:
        marker = self.directory / "executed.json"
        marker.unlink(missing_ok=True)
        script = self.directory / "execute.ps1"
        script.write_text("$ErrorActionPreference = 'Stop'\n"
                          + "$log = Join-Path $env:RUNNER_TEMP 'controlled-output.log'\n"
                          + block + "\nif ($LASTEXITCODE -ne 0) { throw 'Canary execution failed' }\n",
                          encoding="utf-8")
        environment = {key: value for key, value in os.environ.items()
                       if not key.upper().startswith("GIT_")}
        environment.update({key: self.blobs[relative] for key, relative in zip(self.PIN_KEYS, self.SCRIPTS)})
        environment.update(TEST_HEAD=self.head, RUNNER_TEMP=str(self.directory), CONTROL_OUTPUT=str(marker),
                           PYTHONPATH=str(self.repository / Path(self.SCRIPTS[0]).parent))
        result = subprocess.run(["pwsh", "-NoProfile", "-NonInteractive", "-File", str(script)],
                                cwd=self.repository, env=environment, capture_output=True,
                                text=True, timeout=60)
        return result, marker

    def assert_execution(self, result, marker, executed) -> None:
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual([{"script": relative, "isolated": 1} for relative in executed],
                         json.loads(marker.read_text(encoding="utf-8")))

    def test_actual_blocks_use_isolated_python_despite_import_shadow(self) -> None:
        for parent in {Path(relative).parent for relative in self.SCRIPTS}:
            (self.repository / parent / "json.py").write_text(
                "raise RuntimeError('UNREVIEWED_IMPORT_SHADOW')\n", encoding="utf-8")
        for number, (block, required, executed) in enumerate(self.blocks):
            with self.subTest(block=number):
                self.assert_execution(*self.execute(block), executed)

    def test_changed_execution_bytes_are_rejected_before_canary_runs(self) -> None:
        for number, (block, required, executed) in enumerate(self.blocks):
            for relative in required:
                with self.subTest(block=number, script=relative):
                    self.assertIn(relative, block, "Required script mutation must never be skipped")
                    target = self.repository / relative
                    target.write_bytes(self.canaries[relative] + b"# Unreviewed execution bytes.\n")
                    result, marker = self.execute(block)
                    self.assertNotEqual(0, result.returncode)
                    self.assertIn("Working execution script identity mismatch", result.stdout + result.stderr)
                    self.assertFalse(marker.exists())
                    target.write_bytes(self.canaries[relative])
                    self.assert_execution(*self.execute(block), executed)

    def test_committed_and_normalized_working_bytes_are_refused(self) -> None:
        for number, (block, required, executed) in enumerate(self.blocks):
            for relative in required:
                self.assertIn(relative, block, "Required script mutation must never be skipped")
                for kind in ("normalized", "committed"):
                    with self.subTest(block=number, script=relative, kind=kind):
                        target = self.repository / relative
                        if kind == "normalized":
                            self.git("config", "core.autocrlf", "true")
                            target.write_bytes(self.canaries[relative].replace(b"\n", b"\r\n"))
                            self.assertEqual(self.blobs[relative], self.git("-c", "core.autocrlf=true", "hash-object", "--", relative).strip())
                        else:
                            target.write_bytes(self.canaries[relative] + b"# Changed committed instruction file.\n")
                            self.git("add", "--force", "--", relative)
                            self.git("commit", "--quiet", "-m", "Different committed canary")
                            self.head = self.git("rev-parse", "HEAD").strip()
                        result, marker = self.execute(block)
                        self.assertNotEqual(0, result.returncode)
                        expected = ("Working execution script identity mismatch" if kind == "normalized"
                                    else "Committed execution script identity mismatch")
                        self.assertIn(expected, result.stdout + result.stderr)
                        self.assertFalse(marker.exists())
                        self.git("config", "core.autocrlf", "false")
                        target.write_bytes(self.canaries[relative])
                        if kind == "committed":
                            self.git("add", "--force", "--", relative)
                            self.git("commit", "--quiet", "-m", "Restore original pinned canary")
                            self.head = self.git("rev-parse", "HEAD").strip()
                        self.assert_execution(*self.execute(block), executed)


EXPECTED_CONTROL_COUNT = 45
REPORT_REGRESSION_CLI_CASES: list[dict] = []


def expected_control_methods() -> set[str]:
    # A fresh loader is unaffected by unittest CLI -k selection on the default loader.
    loader = unittest.TestLoader()
    return {value.__name__ + "." + name
            for value in globals().values()
            if isinstance(value, type) and issubclass(value, unittest.TestCase)
            and value.__module__ == __name__
            for name in loader.getTestCaseNames(value)}


class RecordingResult(unittest.TextTestResult):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.executed_methods: list[str] = []

    def startTest(self, test):
        self.executed_methods.append(type(test).__name__ + "." + test._testMethodName)
        super().startTest(test)


class RecordingRunner(unittest.TextTestRunner):
    resultclass = RecordingResult


def control_report_status(result, *, unchanged: bool, sdk_executed: bool,
                          powershell_methods: set[str]) -> str:
    """PASS requires every committed control, no skips, and both native-tool control scopes."""
    if not result.wasSuccessful() or not unchanged:
        return "FAIL"
    required = expected_control_methods()
    required_powershell = {name for name in required
                           if name.startswith(("WorkflowExecutionControls.", "WorkflowBootstrapControls."))}
    executed = result.executed_methods
    if (result.testsRun != EXPECTED_CONTROL_COUNT or not executed
            or len(required) != EXPECTED_CONTROL_COUNT or len(executed) != EXPECTED_CONTROL_COUNT
            or set(executed) != required or result.skipped
            or not sdk_executed or powershell_methods != required_powershell):
        return "INCOMPLETE"
    return "PASS"


class ReportCompletionControls(unittest.TestCase):
    def test_complete_report_refuses_missing_repeated_skipped_or_unexecuted_controls(self) -> None:
        expected = sorted(expected_control_methods())
        powershell = {name for name in expected
                      if name.startswith(("WorkflowExecutionControls.", "WorkflowBootstrapControls."))}
        result = unittest.TestResult()
        result.testsRun = len(expected)
        result.executed_methods = expected.copy()
        def status(*, sdk=True, ps=None, unchanged=True):
            return control_report_status(result, unchanged=unchanged, sdk_executed=sdk,
                                         powershell_methods=powershell if ps is None else ps)
        self.assertEqual("PASS", status())
        for methods in ([], expected[:-1], [expected[0], *expected[:-1]]):
            with self.subTest(methods=len(methods)):
                result.executed_methods = methods
                result.testsRun = len(methods)
                self.assertEqual("INCOMPLETE", status())
        result.testsRun = len(expected)
        result.executed_methods = expected.copy()
        result.skipped = [(self, "Missing SDK or PowerShell")]
        self.assertEqual("INCOMPLETE", status())
        result.skipped = []
        self.assertEqual("INCOMPLETE", status(sdk=False))
        self.assertEqual("INCOMPLETE", status(ps=set()))
        self.assertEqual("INCOMPLETE", status(ps=set(sorted(powershell)[1:])))
        self.assertEqual("FAIL", status(unchanged=False))
        result.failures.append((self, "A failed required control"))
        self.assertEqual("FAIL", status())

    def test_actual_cli_refuses_zero_filtered_and_missing_tool_runs(self) -> None:
        with tempfile.TemporaryDirectory(prefix="inspector-report-controls-") as temporary:
            directory = Path(temporary)
            empty_path = directory / "empty-path"
            empty_path.mkdir()
            cases = (
                ("zero", ["-k", "NoSuchAdmissionControl"], 0, 0, False),
                ("filtered", ["RetainedReportControls.test_original_native_reports_and_frozen_source_blobs_match"], 1, 0, False),
                ("missing-sdk", ["SdkImportControls"], 1, 1, True),
                ("missing-pwsh", ["WorkflowExecutionControls"], 3, 3, True),
            )
            for name, selected, count, skipped, no_tools in cases:
                with self.subTest(case=name):
                    report = directory / (name + ".json")
                    environment = dict(os.environ)
                    if no_tools:
                        environment.update(PATH=str(empty_path), GITHUB_ACTIONS="false")
                    completed = subprocess.run(
                        [sys.executable, "-I", "-B", str(Path(__file__).resolve()),
                         "--evidence", str(INPUT_DIRECTORY), "--report", str(report), *selected, "-v"],
                        cwd=RUNNER.ROOT, env=environment, capture_output=True, text=True, timeout=60)
                    self.assertNotEqual(0, completed.returncode, completed.stdout + completed.stderr)
                    observed = json.loads(report.read_text(encoding="utf-8"))
                    self.assertEqual("INCOMPLETE", observed["status"])
                    self.assertEqual(count, observed["testsRun"])
                    self.assertEqual(skipped, observed["skipped"])
                    self.assertEqual(0, observed["failures"])
                    self.assertEqual(0, observed["errors"])
                    self.assertIs(False, observed["nativeProductCommandsExecuted"])
                    self.assertIs(False, observed["disposableSdkControlsExecuted"])
                    self.assertIs(False, observed["powershellControlsExecuted"])
                    self.assertIs(True, observed["nativeEvidenceUnchanged"])
                    REPORT_REGRESSION_CLI_CASES.append({"case": name, "exitCode": completed.returncode,
                        "status": observed["status"], "testsRun": count, "skipped": skipped,
                        "nativeProductCommandsExecuted": observed["nativeProductCommandsExecuted"]})


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence", required=True, type=Path)
    parser.add_argument("--report", required=True, type=Path)
    arguments, unittest_arguments = parser.parse_known_args()
    INPUT_DIRECTORY = arguments.evidence.resolve()
    report_path = arguments.report.resolve()
    if report_path == INPUT_DIRECTORY or INPUT_DIRECTORY in report_path.parents or report_path.exists():
        raise ValueError("Controls report must be a fresh file outside the unchanged native evidence directory")
    before = file_snapshot(INPUT_DIRECTORY)
    NATIVE_TRX = (INPUT_DIRECTORY / "inspector.trx").read_bytes()
    DISCOVERY = (INPUT_DIRECTORY / "compiled-discovery.log").read_bytes().decode("utf-8")
    ANGULAR_REPORT = (INPUT_DIRECTORY / "vitest.json").read_bytes()
    INPUT_RECEIPT = json.loads((INPUT_DIRECTORY / "receipt.json").read_text(encoding="utf-8"))
    ANGULAR_SOURCE = json.loads(ANGULAR_REPORT)["testResults"][0]["name"]
    validate_original_inputs()
    print("Controls reuse retained native reports; only a disposable SDK probe runs dotnet, never Product cases or npm.", flush=True)
    program = unittest.main(argv=[sys.argv[0], *unittest_arguments], testRunner=RecordingRunner, exit=False)
    unchanged = before == file_snapshot(INPUT_DIRECTORY)
    result = program.result
    status = control_report_status(result, unchanged=unchanged, sdk_executed=SDK_IMPORT_CONTROLS_EXECUTED,
                                   powershell_methods=POWERSHELL_CONTROL_METHODS)
    required_powershell = {name for name in expected_control_methods()
                           if name.startswith(("WorkflowExecutionControls.", "WorkflowBootstrapControls."))}
    report = {
        "scope": "RETAINED_REPORT_GIT_BOOTSTRAP_AND_DISPOSABLE_SDK_CONTROLS_ONLY",
        "nativeProductCommandsExecuted": False, "disposableSdkControlsExecuted": SDK_IMPORT_CONTROLS_EXECUTED,
        "nativeEvidenceUnchanged": unchanged,
        "testsRun": result.testsRun, "failures": len(result.failures), "errors": len(result.errors),
        "skipped": len(result.skipped), "status": status,
        "expectedTests": EXPECTED_CONTROL_COUNT, "executedControlMethods": result.executed_methods,
        "powershellControlsExecuted": POWERSHELL_CONTROL_METHODS == required_powershell,
        "powershellControlMethods": sorted(POWERSHELL_CONTROL_METHODS),
        "reportRegressionCliCases": REPORT_REGRESSION_CLI_CASES,
        "productHead": INPUT_RECEIPT["productHead"], "nativeTestHead": INPUT_RECEIPT["testHead"],
        "originalNativeCases": 12, "originalAngularCases": 11,
        "originalTrxSha256": hashlib.sha256(NATIVE_TRX).hexdigest(),
        "originalDiscoverySha256": hashlib.sha256((INPUT_DIRECTORY / "compiled-discovery.log").read_bytes()).hexdigest(),
        "originalAngularSha256": hashlib.sha256(ANGULAR_REPORT).hexdigest(),
        **dict.fromkeys(RUNNER.QUALIFICATION_FLAGS, False),
    }
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    sys.exit(0 if status == "PASS" else 2 if status == "INCOMPLETE" else 1)
