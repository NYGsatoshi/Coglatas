"""False-green controls for ordinary Independent Test inspection evidence."""
import copy
import importlib.util
import io
import json
import os
import subprocess
import sys
import textwrap
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


    def test_resharper_scope_covers_entry_witnesses_and_preserves_existing_routes(self):
        # Exercise the actual embedded runner routing rather than a copy of its condition.
        workflow = (ROOT / ".github/workflows/resharper_pr.yml").read_text(encoding="utf-8")
        marker = """          python3 -B - <<'PY' >> "$GITHUB_OUTPUT"\n"""
        self.assertEqual(1, workflow.count(marker))
        fragment = workflow.split(marker, 1)[1].split("\n          PY", 1)[0]
        script = textwrap.dedent(fragment)
        changed = self.root / "changed.nul"
        projects = self.root / "projects.txt"
        environment = {**os.environ, "CHANGED_FILES_PATH": str(changed), "PROJECTS_FILE": str(projects),
                       "PYTHONPATH": str(ROOT / "scripts/quality")}

        entry = "Coglatas.ProjectIde.Entry.Tests"
        domain = "Coglatas.Domain"
        entry_project = f"tests/{entry}/{entry}.csproj"
        domain_project = f"src/{domain}/{domain}.csproj"
        entry_source = f"tests/{entry}/EntryContractTests.cs"
        entry_lock = f"tests/{entry}/packages.lock.json"
        entry_roster = f"tests/{entry}/retained-test-identities.v1.json"
        domain_source = f"src/{domain}/ProjectIde/History.cs"
        unrelated_source = "tests/Coglatas.Tests/ProjectIde/OtherTests.cs"
        tests_project = "tests/Coglatas.Tests/Coglatas.Tests.csproj"
        sarif_gate = "scripts/quality/check-resharper-pr-sarif.py"
        entry_helper = "scripts/quality/projectide_entry_resharper.py"
        workflow_path = ".github/workflows/resharper_pr.yml"
        witnesses = [entry_source, f"tests/{entry}/SourceAndRunEntryTests.cs"]

        for file in (entry_project, domain_project, tests_project):
            project = self.root / file
            project.parent.mkdir(parents=True)
            project.touch()

        def classify(paths, integrated=True):
            solution = self.root / "Coglatas.slnx"
            project_entry = f'<Project Path="{entry_project}" />' if integrated else ""
            solution.write_text(f'<Solution><Project Path="{domain_project}" />{project_entry}</Solution>',
                                encoding="utf-8")
            changed.write_bytes(b"".join(path.encode() + b"\0" for path in paths))
            result = subprocess.run([sys.executable, "-B", "-c", script], cwd=self.root,
                                    env=environment, text=True, capture_output=True, check=True)
            outputs = dict(line.split("=", 1) for line in result.stdout.splitlines())
            return outputs["full_solution"], projects.read_text(encoding="utf-8").splitlines()

        cases = (
            ((sarif_gate,), [entry], witnesses),
            ((entry_helper,), [entry], witnesses),
            ((entry_lock,), [entry], witnesses),
            ((entry_roster,), [entry], witnesses),
            ((entry_lock, domain_source), [domain, entry], witnesses),
            ((entry_roster, domain_source), [domain, entry], witnesses),
            ((entry_project,), [entry], witnesses),
            ((entry_source,), [entry], [entry_source]),
            ((entry_source, domain_source), [domain, entry], [entry_source]),
            ((domain_source,), [domain], []),
            ((unrelated_source,), ["Coglatas.Tests"], []),
            ((".github/workflows/documentation-integrity.yml",), [domain], []),
            (("tests/ci/test_projectide_entry_resharper.py",), [domain], []),
            ((workflow_path,), [], witnesses),
            (("Coglatas.slnx",), [], witnesses),
            (("global.json",), [], []),
            (("src/Unknown.cs", entry_lock), [], witnesses),
        )
        for paths, expected_projects, expected_witnesses in cases:
            with self.subTest(paths=paths):
                self.assertEqual(("false" if expected_projects else "true", expected_projects),
                                 classify(paths))
                self.assertEqual(expected_witnesses, quality.entry_source_witnesses(paths))

        # Verifier inputs on the historical solution must keep the bounded representative.
        self.assertEqual(("false", [domain]), classify((sarif_gate,), integrated=False))
        self.assertEqual(("false", [domain]), classify((entry_helper,), integrated=False))
        self.assertEqual(("true", []), classify((workflow_path,), integrated=False))


if __name__ == "__main__":
    unittest.main()
