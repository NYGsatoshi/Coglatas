"""Synthetic false-green controls; these do not execute the product transport."""

import copy
from contextlib import redirect_stdout
import io
import json
import os
import sys
from unittest.mock import patch
from datetime import datetime, timezone
from pathlib import Path
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET

import sec17_deep_execution as deep
import sec_arch_evidence as evidence
from sec_arch_assembly_binding import ASSEMBLIES, assembly_path, loaded_assembly_path
import test_sec_arch_evidence as sec_arch_controls

NOW = datetime(2026, 10, 27, 13, tzinfo=timezone.utc)
Q = "{" + evidence.NS["t"] + "}"


def fixture():
    root = ET.Element(Q + "TestRun")
    ET.SubElement(root, Q + "Times", start="2026-10-27T12:00:00Z", finish="2026-10-27T12:01:00Z")
    results = ET.SubElement(root, Q + "Results")
    definitions = ET.SubElement(root, Q + "TestDefinitions")
    definition = ET.SubElement(definitions, Q + "UnitTest", id="one", name=deep.METHOD)
    ET.SubElement(definition, Q + "Execution", id="one")
    group, method = deep.METHOD.rsplit(".", 1)
    ET.SubElement(definition, Q + "TestMethod", className=group, name=method)
    ET.SubElement(results, Q + "UnitTestResult", testId="one", executionId="one",
                  testName=deep.METHOD, outcome="Passed", startTime="2026-10-27T12:00:01Z",
                  endTime="2026-10-27T12:00:50Z")
    ET.SubElement(ET.SubElement(root, Q + "ResultSummary"), Q + "Counters")
    recalculate(root)
    return root


def recalculate(root):
    rows = root.find(Q + "Results")
    passed = sum(row.attrib["outcome"] == "Passed" for row in rows)
    failed = sum(row.attrib["outcome"] == "Failed" for row in rows)
    root.find(Q + "ResultSummary/" + Q + "Counters").attrib.update(
        total=str(len(rows)), passed=str(passed), failed=str(failed), executed=str(passed + failed))


def verify(root):
    return deep.verify(ET.tostring(root), NOW)


class DeepExecutionTests(unittest.TestCase):
    def test_only_exact_registered_native_owner_receives_execution_credit(self):
        self.assertEqual(1, evidence.EXPECTED[deep.METHOD])
        self.assertEqual("PASS", verify(fixture())["outcome"])

    def test_missing_skipped_failed_aborted_or_unknown_owner_cannot_pass(self):
        for outcome in ("missing", "NotExecuted", "Failed", "Aborted", "Unknown"):
            with self.subTest(outcome=outcome):
                root = fixture()
                rows = root.find(Q + "Results")
                if outcome == "missing":
                    rows.remove(rows[0])
                else:
                    rows[0].attrib["outcome"] = outcome
                recalculate(root)
                with self.assertRaises(ValueError):
                    verify(root)

    def test_other_passing_method_or_renamed_owner_cannot_substitute(self):
        root = fixture()
        definition = root.find(Q + "TestDefinitions")[0]
        definition.find(Q + "TestMethod").attrib["name"] = "OtherPassingMethod"
        definition.attrib["name"] = "OtherPassingMethod"
        root.find(Q + "Results")[0].attrib["testName"] = "OtherPassingMethod"
        with self.assertRaises(ValueError):
            verify(root)

    def test_duplicate_execution_and_forged_counters_cannot_pass(self):
        root = fixture()
        root.find(Q + "Results").append(copy.deepcopy(root.find(Q + "Results")[0]))
        recalculate(root)
        with self.assertRaises(ValueError):
            verify(root)
        root = fixture()
        root.find(Q + "ResultSummary/" + Q + "Counters").attrib["passed"] = "2"
        with self.assertRaises(ValueError):
            verify(root)

    def test_alias_execution_identity_and_unsupported_xml_cannot_pass(self):
        root = fixture()
        root.find(Q + "Results")[0].attrib["executionId"] = "another"
        with self.assertRaises(ValueError):
            verify(root)
        for data in (b'{"outcome":"PASS"}', b"<TestRun/>", b"<!DOCTYPE TestRun><TestRun/>"):
            with self.assertRaises((ValueError, ET.ParseError)):
                deep.verify(data, NOW)

    def test_stale_or_out_of_run_execution_cannot_pass(self):
        root = fixture()
        root.find(Q + "Results")[0].attrib["endTime"] = "2026-10-27T12:02:00Z"
        with self.assertRaises(ValueError):
            verify(root)
        with self.assertRaises(ValueError):
            deep.verify(ET.tostring(fixture()), datetime(2026, 10, 29, tzinfo=timezone.utc))

    def test_failure_skip_and_selection_remain_distinct_without_raw_output(self):
        for native, outcome, selection in (
                ("Failed", "FAIL", "EXECUTED"),
                ("NotExecuted", "UNVERIFIED", "SKIPPED_OR_NOT_EXECUTED"),
                ("Aborted", "ERROR", "EXECUTED")):
            root = fixture()
            row = root.find(Q + "Results")[0]
            row.attrib["outcome"] = native
            diagnostic = ET.SubElement(row, Q + "Output")
            ET.SubElement(diagnostic, Q + "StdOut").text = "sensitive-native-output-marker"
            ET.SubElement(ET.SubElement(diagnostic, Q + "ErrorInfo"), Q + "Message").text = "sensitive-native-error-marker"
            recalculate(root)
            observed = deep.observe(ET.tostring(root), NOW)
            self.assertEqual(outcome, observed["outcome"])
            self.assertEqual(selection, deep.selection_status(observed))
            self.assertNotIn("sensitive-native-output-marker", json.dumps(observed))
            self.assertNotIn("sensitive-native-error-marker", json.dumps(observed))
        root = fixture()
        definition = root.find(Q + "TestDefinitions")[0]
        definition.find(Q + "TestMethod").attrib["name"] = "OtherPassingMethod"
        definition.attrib["name"] = "OtherPassingMethod"
        root.find(Q + "Results")[0].attrib["testName"] = "OtherPassingMethod"
        observed = deep.observe(ET.tostring(root), NOW)
        self.assertEqual("UNVERIFIED", observed["outcome"])
        self.assertEqual("NOT_SELECTED", deep.selection_status(observed))

    def test_extra_passing_owner_cannot_hide_an_unselected_deep_case(self):
        root = fixture()
        row = copy.deepcopy(root.find(Q + "Results")[0])
        row.attrib.update(testId="two", executionId="two", testName="OtherPassingMethod")
        definition = copy.deepcopy(root.find(Q + "TestDefinitions")[0])
        definition.attrib.update(id="two", name="OtherPassingMethod")
        definition.find(Q + "Execution").attrib["id"] = "two"
        definition.find(Q + "TestMethod").attrib["name"] = "OtherPassingMethod"
        root.find(Q + "Results").append(row)
        root.find(Q + "TestDefinitions").append(definition)
        recalculate(root)
        with self.assertRaises(ValueError):
            verify(root)

    def test_capture_error_writes_closed_failure_without_exception_or_raw_trx(self):
        sec_arch_controls.assert_closed_cli_error_receipt(
            self, deep, "SEC17_CLOSED_EXECUTION_RECEIPT_WRITTEN", "SEC-17 bounded deep")

    def test_failure_summary_accepts_only_canonical_producer_trx(self):
        sec_arch_controls.assert_canonical_failure_summary(self, deep, deep.SEC17_DEEP_TRX_RELATIVE)

    def test_cyclic_symlink_is_a_closed_failure_receipt(self):
        sec_arch_controls.assert_cyclic_symlink_closed_receipt(self, deep)

    def test_actual_git_build_stamp_source_and_loaded_copy_bindings_fail_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            (root / ".gitignore").write_text("artifacts/\n**/bin/\n", encoding="utf-8")
            source = root / deep.SOURCE
            source.parent.mkdir(parents=True)
            source.write_text("public Task " + deep.METHOD.rsplit(".", 1)[1] + "() => Synthetic();", encoding="utf-8")

            def git(*args):
                return subprocess.check_output(["git", "-C", str(root), *args], text=True).strip()

            git("init")
            git("config", "user.email", "synthetic@example.invalid")
            git("config", "user.name", "Synthetic verifier control")
            git("add", ".")
            git("commit", "-m", "Synthetic SEC-17 verifier source")
            candidate = git("rev-parse", "HEAD")
            stamp = root / "artifacts/ci/dotnet-build-sha"
            stamp.parent.mkdir(parents=True)
            stamp.write_text(candidate)
            trx = root / deep.SEC17_DEEP_TRX_RELATIVE
            trx.parent.mkdir(parents=True, exist_ok=True)
            trx.write_bytes(ET.tostring(fixture()))
            for name in ASSEMBLIES:
                for path in {assembly_path(root, name), loaded_assembly_path(root, name)}:
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_bytes(("synthetic-verifier-" + name).encode())
            if os.name != "posix":
                sec_arch_controls.assert_non_posix_trusted_capture_is_closed(
                    self, deep, root, trx, candidate, NOW)
                return
            receipt = deep.capture(root, trx, candidate, NOW)
            sec_arch_controls.assert_unavailable_capture_is_closed(self, deep, root, candidate, NOW)
            self.assertEqual(set(ASSEMBLIES), set(receipt["assemblyDigests"]))
            self.assertEqual("UNVERIFIED", receipt["contractCompletion"])
            self.assertIsNone(receipt["ownerApproval"])
            output = root / "artifacts/execution.json"
            arguments = ["capture", "--trx", str(trx), "--candidate-sha", candidate, "--output", str(output)]
            captured = io.StringIO()
            with redirect_stdout(captured), patch.object(sys, "argv", arguments), patch.object(
                    deep, "capture", return_value=receipt):
                self.assertEqual(0, deep.main())
            public_log = captured.getvalue()
            sec_arch_controls.assert_owned_source_mutation_is_rejected(self, deep, root, trx, candidate, NOW, source)
            sec_arch_controls.assert_native_capture_identity_and_mutations(
                self, deep, root, trx, candidate, NOW, stamp, receipt, output,
                public_log, "SEC17_CLOSED_EXECUTION_RECEIPT_WRITTEN", "SEC-17 bounded deep")


if __name__ == "__main__":
    unittest.main()
