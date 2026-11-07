"""False-green controls for ordinary Independent Test inspection evidence."""
import copy
import importlib.util
import io
import json
from contextlib import redirect_stdout
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("entry_quality", ROOT / "scripts/quality/projectide_entry_resharper.py")
quality = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(quality)


class InspectionEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name).resolve()
        self.report = self.root / "report.json"
        self.document = {"version": "2.1.0", "runs": [{
            "tool": {"driver": {"name": "InspectCode", "fullName": "JetBrains Inspect Code 2026.2.2"}},
            "invocations": [{"executionSuccessful": True}], "results": [],
            "versionControlProvenance": [{"revisionId": "a" * 40}],
            "originalUriBaseIds": {"solutionDir": {"uri": self.root.as_uri() + "/"}}}]}

    def validate(self, value):
        self.report.write_text(json.dumps(value))
        return quality.validate_report(self.report, self.root, "a" * 40)

    def test_complete_zero_finding_report_still_requires_separate_scope_probe(self):
        self.assertEqual([], self.validate(self.document)["results"])

    def test_missing_empty_malformed_and_unsuccessful_reports_are_rejected(self):
        mutations = [None, {}, {"version": "2.1.0", "runs": []}]
        for key, value in (("results", None), ("invocations", []),
                           ("invocations", [{"executionSuccessful": False}]),
                           ("invocations", [{}]), ("tool", {}),
                           ("results", [{}]), ("versionControlProvenance", [{"revisionId": "b" * 40}]),
                           ("originalUriBaseIds", {})):
            document = copy.deepcopy(self.document)
            document["runs"][0][key] = value
            mutations.append(document)
        for document in mutations:
            with self.subTest(document=document), self.assertRaises(ValueError):
                self.validate(document)
        self.report.write_text("{broken")
        with self.assertRaises(ValueError):
            quality.validate_report(self.report, self.root, "a" * 40)
        self.report.unlink()
        with self.assertRaises(FileNotFoundError):
            quality.validate_report(self.report, self.root, "a" * 40)

    def test_warning_in_changed_test_source_fails_existing_gate(self):
        gate = quality.load_gate()
        path = "tests/Coglatas.ProjectIde.Entry.Tests/EntryContractTests.cs"
        changed = self.root / "changed.nul"
        changed.write_bytes(path.encode() + b"\0")
        document = copy.deepcopy(self.document)
        document["runs"][0]["results"] = [{"ruleId": "InconsistentNaming", "level": "warning",
            "message": {"text": "deliberate warning"}, "locations": [{"physicalLocation": {
                "artifactLocation": {"uri": path}, "region": {"startLine": 1}}}]}]
        self.validate(document)
        with patch("sys.argv", ["gate", str(self.report), str(changed)]):
            self.assertEqual(1, gate.main())

    def test_malformed_or_unbound_reports_return_a_formatted_report_error(self):
        gate = quality.load_gate()
        changed = self.root / "changed.nul"
        changed.write_bytes(b"tests/Coglatas.ProjectIde.Entry.Tests/EntryContractTests.cs\0")
        mutations = [None, {"version": "2.1.0", "runs": [{"tool": None}]}]
        for key, value in (("invocations", [None]), ("results", [{"ruleId": "UnboundWarning",
                           "message": {"text": "cannot establish changed-file scope"}}])):
            document = copy.deepcopy(self.document)
            document["runs"][0][key] = value
            mutations.append(document)
        result = {"ruleId": "Warning", "message": {"text": "synthetic warning"}, "locations": [{
            "physicalLocation": {"artifactLocation": {"uri": "tests/unchanged.cs"}, "region": {"startLine": 1}}}]}
        for key, value in (("message", "invalid"), ("message", ["invalid"]), ("locations", [{}]),
                           ("locations", [None]), ("locations", [{"physicalLocation": "invalid"}]),
                           ("locations", [{"physicalLocation": {"artifactLocation": {"uri": "https://example.invalid/source"}}}]),
                           ("locations", [{"physicalLocation": {"artifactLocation": {"uri": "tests/source.cs"},
                                                               "region": {"startLine": "invalid"}}}])):
            finding = copy.deepcopy(result)
            finding[key] = value
            document = copy.deepcopy(self.document)
            document["runs"][0]["results"] = [finding]
            mutations.append(document)
        for document in mutations:
            self.report.write_text(json.dumps(document))
            output = io.StringIO()
            with self.subTest(document=document), redirect_stdout(output), \
                    patch("sys.argv", ["gate", str(self.report), str(changed)]):
                self.assertEqual(2, gate.main())
            self.assertTrue(output.getvalue().startswith("::error::"))
        self.report.write_text("{broken")
        with redirect_stdout(io.StringIO()), patch("sys.argv", ["gate", str(self.report), str(changed)]):
            self.assertEqual(2, gate.main())

    def test_valid_warning_in_unchanged_source_preserves_existing_threshold(self):
        gate = quality.load_gate()
        changed = self.root / "changed.nul"
        changed.write_bytes(b"tests/changed.cs\0")
        document = copy.deepcopy(self.document)
        document["runs"][0]["results"] = [{"ruleId": "Warning", "message": {"text": "synthetic warning"},
            "locations": [{"physicalLocation": {"artifactLocation": {"uri": "tests/unchanged.cs"}}}]}]
        self.report.write_text(json.dumps(document))
        with redirect_stdout(io.StringIO()), patch("sys.argv", ["gate", str(self.report), str(changed)]):
            self.assertEqual(0, gate.main())

    def test_empty_probe_cannot_certify_changed_source_and_restores_original(self):
        from argparse import Namespace
        path = "tests/Coglatas.ProjectIde.Entry.Tests/EntryContractTests.cs"
        source = self.root / path
        source.parent.mkdir(parents=True)
        source.write_bytes(b"// exact original\r\n")
        receipt = {"compositionCommit": "a" * 40, "projects": ["Coglatas.ProjectIde.Entry.Tests"],
                   "changedCompiledSources": {path: quality.digest(source)}}
        (self.root / "composition.json").write_text(json.dumps(receipt))
        (self.root / "inspectcode.sarif.json").write_text(json.dumps(self.document))
        def inspect(*unused):
            (self.root / "scope-probe.sarif.json").write_text(json.dumps(self.document))
        with patch.dict("os.environ", {"RESHARPER_ROOT": str(self.root), "RESHARPER_JB": "jb"}), \
                patch.object(quality, "execute", side_effect=inspect), self.assertRaisesRegex(ValueError, "every changed source"):
            quality.verify(Namespace(report=self.root))
        self.assertEqual(b"// exact original\r\n", source.read_bytes())
        self.assertNotIn("scopeProbe", json.loads((self.root / "composition.json").read_text()))


if __name__ == "__main__":
    unittest.main()
