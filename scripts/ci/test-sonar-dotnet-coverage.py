#!/usr/bin/env python3
"""Positive and negative controls for real OpenCover production evidence."""

from __future__ import annotations

import importlib.util
import io
import json
from contextlib import redirect_stdout
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

SCRIPT = Path(__file__).with_name("verify-sonar-dotnet-coverage.py")
SPEC = importlib.util.spec_from_file_location("sonar_coverage", SCRIPT)
assert SPEC and SPEC.loader
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class CoverageTests(unittest.TestCase):
    def setUp(self) -> None:
        self.work = tempfile.TemporaryDirectory()
        self.addCleanup(self.work.cleanup)
        self.root = Path(self.work.name)
        self.source = self.root / "src/App/Logic.cs"
        self.source.parent.mkdir(parents=True)
        self.source.write_text("public class Logic {}", encoding="utf-8")
        self.report = self.root / "coverage.opencover.xml"

    def write_report(self, *, path: Path | None = None, visits: str = "2",
                     fileid: str = "1") -> None:
        document = ET.Element("CoverageSession")
        module = ET.SubElement(ET.SubElement(document, "Modules"), "Module")
        files = ET.SubElement(module, "Files")
        ET.SubElement(files, "File", uid="1", fullPath=str(path or self.source))
        points = ET.SubElement(module, "SequencePoints")
        ET.SubElement(points, "SequencePoint", fileid=fileid, vc=visits)
        ET.SubElement(points, "SequencePoint", fileid="1", vc="0")
        ET.ElementTree(document).write(self.report, encoding="utf-8")

    def test_visited_and_unvisited_production_points_are_retained(self) -> None:
        self.write_report()
        result = MODULE.verify_report(self.report, self.root)
        self.assertEqual(result["productionFiles"], 1)
        self.assertEqual(result["sequencePoints"], 2)
        self.assertEqual(result["visitedSequencePoints"], 1)
        self.assertEqual(len(result["reportSha256"]), 64)

    def test_empty_or_summary_only_report_fails(self) -> None:
        self.report.write_text('<CoverageSession><Summary visitedSequencePoints="42"/></CoverageSession>')
        with self.assertRaises(ValueError):
            MODULE.verify_report(self.report, self.root)

    def test_deterministic_paths_bind_to_checkout_without_rewriting_native_bytes(self) -> None:
        self.write_report()
        document = ET.parse(self.report)
        document.find("./Modules/Module/Files/File").set("fullPath", "/_/src/App/Logic.cs")
        document.write(self.report, encoding="utf-8")
        original = self.report.read_bytes()
        result = MODULE.verify_report(self.report, self.root)
        self.assertEqual(result["productionFiles"], 1)
        self.assertEqual(result["sequencePoints"], 2)
        self.assertEqual(result["visitedSequencePoints"], 1)
        self.assertEqual(self.report.read_bytes(), original)

    def test_deterministic_paths_reject_aliases_missing_sources_and_test_credit(self) -> None:
        for path in ("/_/src/../App/Logic.cs", "/_/src//App/Logic.cs",
                     "/_/src/App/Missing.cs", "/_/tests/PassingTests.cs",
                     "/_/src/App/obj/Generated.cs", "/_/src/App/Logic.cs/../Logic.cs"):
            with self.subTest(path=path):
                self.write_report()
                document = ET.parse(self.report)
                document.find("./Modules/Module/Files/File").set("fullPath", path)
                document.write(self.report, encoding="utf-8")
                with self.assertRaises(ValueError):
                    MODULE.verify_report(self.report, self.root)

    def test_all_unvisited_points_fail(self) -> None:
        self.write_report(visits="0")
        with self.assertRaises(ValueError):
            MODULE.verify_report(self.report, self.root)

    def test_tests_only_coverage_cannot_satisfy_production_evidence(self) -> None:
        self.write_report(path=self.root / "tests/PassingTests.cs")
        with self.assertRaises(ValueError):
            MODULE.verify_report(self.report, self.root)

    def test_unknown_file_reference_cannot_supply_visits(self) -> None:
        self.write_report(fileid="99")
        with self.assertRaises(ValueError):
            MODULE.verify_report(self.report, self.root)

    def test_missing_production_source_fails(self) -> None:
        self.write_report(path=self.root / "src/App/Missing.cs")
        with self.assertRaises(ValueError):
            MODULE.verify_report(self.report, self.root)

    def test_another_checkout_cannot_supply_source_paths(self) -> None:
        self.write_report(path=self.root.parent / "other-checkout/src/Logic.cs")
        with self.assertRaises(ValueError):
            MODULE.verify_report(self.report, self.root)

    def test_malformed_or_wrong_format_report_fails(self) -> None:
        for content in ("broken", "<coverage/>"):
            with self.subTest(content=content):
                self.report.write_text(content)
                with self.assertRaises((ValueError, ET.ParseError)):
                    MODULE.verify_report(self.report, self.root)

    def test_generated_points_cannot_supply_production_visits(self) -> None:
        generated = self.root / "src/App/obj/Release/Generated.cs"
        generated.parent.mkdir(parents=True)
        generated.write_text("// generated", encoding="utf-8")
        self.write_report(path=generated)
        with self.assertRaises(ValueError):
            MODULE.verify_report(self.report, self.root)

    def test_missing_or_multiple_reports_fail_before_receipt(self) -> None:
        original_root = MODULE.ROOT
        self.addCleanup(setattr, MODULE, "ROOT", original_root)
        MODULE.ROOT = self.root
        with self.assertRaises(ValueError):
            MODULE.main()
        self.write_report()
        for folder in ("a", "b"):
            destination = self.root / "artifacts/sonar-dotnet" / folder
            destination.mkdir(parents=True)
            (destination / "coverage.opencover.xml").write_bytes(self.report.read_bytes())
        with self.assertRaises(ValueError):
            MODULE.main()
        self.assertFalse((self.root / "artifacts/sonar-dotnet/receipt.json").exists())

    def test_receipt_binds_real_report_and_git_checkout(self) -> None:
        original_root = MODULE.ROOT
        self.addCleanup(setattr, MODULE, "ROOT", original_root)
        MODULE.ROOT = self.root
        for args in (["init", "--quiet"], ["add", "src"],
                     ["-c", "user.name=Coverage control", "-c",
                      "user.email=coverage@example.test", "-c", "commit.gpgsign=false",
                      "commit", "--quiet", "-m", "Synthetic coverage fixture"]):
            subprocess.run(["git", *args], cwd=self.root, check=True, capture_output=True)
        self.write_report()
        destination = self.root / "artifacts/sonar-dotnet/993ff674-8ddf-4916-ac9a-b6f614e902a2"
        destination.mkdir(parents=True)
        (destination / "coverage.opencover.xml").write_bytes(self.report.read_bytes())
        output = io.StringIO()
        with redirect_stdout(output):
            MODULE.main()
        result = json.loads((destination.parent / "receipt.json").read_text())
        self.assertEqual(json.loads(output.getvalue()), result)
        expected_sha = subprocess.check_output(
            ["git", "rev-parse", "HEAD"], cwd=self.root, text=True).strip()
        self.assertEqual(result["sourceSha"], expected_sha)
        self.assertEqual(len(result["treeSha"]), 40)
        self.assertEqual(result["visitedSequencePoints"], 1)
        self.assertEqual(result["identicalVSTestCopies"], 0)
        self.assertEqual((destination.parent / "verified.opencover.xml").read_bytes(), self.report.read_bytes())
        self.assertEqual(result["reportSha256"], MODULE.verify_report(self.report, self.root)["reportSha256"])
        self.assertEqual(result["scope"], "DOTNET_COVERAGE_IMPORT_ONLY_NOT_RELEASE_ACCEPTANCE")

    def collector_fixture(self) -> tuple[Path, Path, Path]:
        self.write_report()
        folder = self.root / "artifacts/sonar-dotnet"
        primary = folder / "993ff674-8ddf-4916-ac9a-b6f614e902a2/coverage.opencover.xml"
        attachment = folder / "_runner_2026-10-28_22_40_45/In/runner/coverage.opencover.xml"
        for path in (primary, attachment):
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(self.report.read_bytes())
        return folder, primary, attachment

    def test_native_vstest_identical_attachment_is_not_a_second_collector(self) -> None:
        folder, primary, _ = self.collector_fixture()
        self.assertEqual(MODULE.select_collector_report(folder), (primary, 1))

    def test_changed_vstest_attachment_fails(self) -> None:
        folder, _, attachment = self.collector_fixture()
        attachment.write_bytes(attachment.read_bytes() + b" ")
        with self.assertRaisesRegex(ValueError, "differs"):
            MODULE.select_collector_report(folder)

    def test_two_collectors_fail_even_with_identical_bytes(self) -> None:
        folder, primary, _ = self.collector_fixture()
        second = folder / "22222222-2222-4222-8222-222222222222/coverage.opencover.xml"
        second.parent.mkdir()
        second.write_bytes(primary.read_bytes())
        with self.assertRaisesRegex(ValueError, "one fresh"):
            MODULE.select_collector_report(folder)

    def test_attachment_without_collector_fails(self) -> None:
        folder, primary, _ = self.collector_fixture()
        primary.unlink()
        with self.assertRaisesRegex(ValueError, "one fresh"):
            MODULE.select_collector_report(folder)

    def test_unexpected_nested_report_fails_even_with_identical_bytes(self) -> None:
        folder, primary, _ = self.collector_fixture()
        extra = folder / "unrecognized/coverage.opencover.xml"
        extra.parent.mkdir()
        extra.write_bytes(primary.read_bytes())
        with self.assertRaisesRegex(ValueError, "layout"):
            MODULE.select_collector_report(folder)

    def test_attachment_runner_identity_must_match_directory(self) -> None:
        folder, _, attachment = self.collector_fixture()
        wrong = folder / "_other_2026-10-28_22_40_45/In/runner/coverage.opencover.xml"
        wrong.parent.mkdir(parents=True)
        wrong.write_bytes(attachment.read_bytes())
        with self.assertRaisesRegex(ValueError, "layout"):
            MODULE.select_collector_report(folder)

    def test_invalid_visit_counts_fail(self) -> None:
        for visits in ("-1", "invalid"):
            with self.subTest(visits=visits):
                self.write_report(visits=visits)
                with self.assertRaises(ValueError):
                    MODULE.verify_report(self.report, self.root)


    def parallel_fixture(self):
        for args in (["init", "--quiet"], ["add", "src"],
                     ["-c", "user.name=Coverage control", "-c",
                      "user.email=coverage@example.test", "-c", "commit.gpgsign=false",
                      "commit", "--quiet", "-m", "Synthetic coverage fixture"]):
            subprocess.run(["git", *args], cwd=self.root, check=True, capture_output=True)
        sha = subprocess.check_output(["git", "rev-parse", "HEAD"],
                                      cwd=self.root, text=True).strip()
        tree = subprocess.check_output(["git", "rev-parse", "HEAD^{tree}"],
                                       cwd=self.root, text=True).strip()
        native = MODULE._native_verifier()
        folder = self.root / "artifacts/sonar-dotnet"
        folder.mkdir(parents=True)
        self.write_report()
        report = folder / "verified.opencover.xml"
        report.write_bytes(self.report.read_bytes())
        receipt = MODULE.verify_report(report, self.root)
        receipt.update(sourceSha=sha, treeSha=tree,
                       scope="DOTNET_COVERAGE_IMPORT_ONLY_NOT_RELEASE_ACCEPTANCE")
        (folder / "receipt.json").write_text(json.dumps(receipt))
        trx = b"synthetic TRX fixture for handoff binding"
        mapping = b"synthetic source mapping"
        (folder / "backend-tests.trx").write_bytes(trx)
        (folder / "deterministic-source-roots.original").write_bytes(mapping)
        digests = {}
        for name, project in native.ASSEMBLIES.items():
            for ext in ("dll", "pdb"):
                output = "bin/Release/net11.0/" + name + "." + ext
                data = (name + ext + sha).encode()
                for prefix in (project, "tests/Coglatas.Tests"):
                    path = self.root / prefix / output
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_bytes(data)
                digests["assemblies/" + name + "." + ext] = native.digest(data)
        mapping_path = self.root / "tests/Coglatas.Tests/bin/Release/net11.0/CoverletSourceRootsMapping_Coglatas.Tests"
        mapping_path.write_bytes(mapping)
        (folder / "pre-test-rebuild").mkdir()
        (folder / "pre-test-rebuild/outputs.json").write_text(
            json.dumps(dict(sourceSha=sha, artifacts=digests)))
        executed = dict(sourceSha=sha, runId="100", attempt="1",
                        productionBinariesUnchanged=True,
                        trxSha256=native.digest(trx), sourceMappingSha256=native.digest(mapping),
                        executedArtifacts=digests.copy())
        executed["executedArtifacts"]["assemblies/Coglatas.Tests.dll"] = "0" * 64
        (folder / "binary-provenance.json").write_text(json.dumps(executed))
        return folder, sha

    def test_parallel_handoff_requires_exact_execution_and_scanner_build(self):
        folder, sha = self.parallel_fixture()
        MODULE.verify_parallel_handoff(self.root, folder, sha, "100", "1")
        MODULE.verify_parallel_handoff(self.root, folder, sha, "100", "1", compare_build=True)
        for identity in (("0" * 40, "100", "1"), (sha, "101", "1"),
                         (sha, "100", "2"), (sha, "0", "1")):
            with self.subTest(identity=identity), self.assertRaises(ValueError):
                MODULE.verify_parallel_handoff(self.root, folder, *identity)

    def test_parallel_handoff_rejects_receipt_and_native_evidence_changes(self):
        folder, sha = self.parallel_fixture()
        for name in ("verified.opencover.xml", "backend-tests.trx",
                     "deterministic-source-roots.original", "receipt.json",
                     "pre-test-rebuild/outputs.json", "binary-provenance.json"):
            path = folder / name
            original = path.read_bytes()
            with self.subTest(name=name):
                path.write_bytes(b"")
                with self.assertRaises(ValueError):
                    MODULE.verify_parallel_handoff(self.root, folder, sha, "100", "1")
                path.write_bytes(original)
        for name in ("verified.opencover.xml", "backend-tests.trx",
                     "deterministic-source-roots.original"):
            path = folder / name
            original = path.read_bytes()
            with self.subTest(name=name):
                path.write_bytes(original + b" ")
                with self.assertRaises(ValueError):
                    MODULE.verify_parallel_handoff(self.root, folder, sha, "100", "1")
                path.write_bytes(original)
        path = folder / "binary-provenance.json"
        original = path.read_bytes()
        for key, replacement in (
            ("productionBinariesUnchanged", False),
            ("executedArtifacts", {}),
            ("sourceMappingSha256", "1" * 64),
            ("executedArtifacts", json.loads(original)["executedArtifacts"] |
             {"assemblies/Coglatas.Domain.dll": "1" * 64}),
        ):
            with self.subTest(key=key):
                value = json.loads(original)
                value[key] = replacement
                path.write_text(json.dumps(value))
                with self.assertRaises(ValueError):
                    MODULE.verify_parallel_handoff(self.root, folder, sha, "100", "1")
        path.write_bytes(original)
        MODULE.verify_parallel_handoff(self.root, folder, sha, "100", "1")

    def test_parallel_handoff_rejects_scanner_binary_copy_and_mapping_mismatches(self):
        folder, sha = self.parallel_fixture()
        paths = (
            "src/Coglatas.Domain/bin/Release/net11.0/Coglatas.Domain.dll",
            "tests/Coglatas.Tests/bin/Release/net11.0/Coglatas.Domain.pdb",
            "tests/Coglatas.Tests/bin/Release/net11.0/CoverletSourceRootsMapping_Coglatas.Tests",
        )
        for name in paths:
            path = self.root / name
            original = path.read_bytes()
            with self.subTest(name=name):
                path.write_bytes(original + b"changed")
                with self.assertRaises(ValueError):
                    MODULE.verify_parallel_handoff(self.root, folder, sha, "100", "1", compare_build=True)
                path.write_bytes(original)
        MODULE.verify_parallel_handoff(self.root, folder, sha, "100", "1", compare_build=True)


    def test_scanner_compatible_fixture_binds_prebuild_and_fresh_physical_inputs(self):
        import importlib.util

        spec = importlib.util.spec_from_file_location(
            "scanner_trial_test", SCRIPT.with_name("verify-sonar-scanner-identity-trial.py"))
        trial = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(trial)
        reproduction = trial.reproduction()
        native = trial.native()
        folder = self.root / "artifacts/sonar-dotnet"
        archive = folder / "compiler-identity"
        archive.mkdir(parents=True)
        identity = {"sourceSha": "a" * 40, "treeSha": "b" * 40,
                    "runId": "100", "attempt": "1", "workspace": str(self.root)}
        keys = {"assemblies/" + name + "." + ext
                for name in native.ASSEMBLIES for ext in ("dll", "pdb")}
        for name in keys:
            path = archive / name
            path.parent.mkdir(exist_ok=True)
            path.write_bytes(("fixture " + name).encode())
        mapping = b"original deterministic mapping"
        (archive / "mapping.original").write_bytes(mapping)
        inventory = ("PROBE\n" + reproduction.FIXTURE_PREFIX + "1.xml\n" +
                     reproduction.FIXTURE_PREFIX + "2.xml\n")
        for name in reproduction.PROJECTS:
            target = archive / name
            target.mkdir()
            (target / reproduction.GENERATOR).write_bytes(b"fixture generated source")
            (target / "inputs.txt").write_text(inventory, encoding="utf-8")
        hashes = reproduction.manifest(archive)
        before = {name: hashes[name] for name in keys}
        receipt = dict(identity, xmlCount=2, hashes=hashes, assemblies=before)
        (archive / "receipt.json").write_text(json.dumps(receipt), encoding="utf-8")
        physical = folder / "physical-compiler-inputs.txt"
        physical.write_text(inventory, encoding="utf-8")
        physical_map = folder / "physical-test-source-roots.original"
        physical_map.write_bytes(b"physical mapping")
        result = MODULE.scanner_compatible_fixture(self.root, folder, identity, before, mapping)
        self.assertEqual(result["fixtureXmlCount"], 2)
        self.assertEqual(result["physicalCompilerInputsSha256"], native.digest(physical.read_bytes()))
        for invalid in (
            identity | {"attempt": "2"},
            identity | {"sourceSha": "c" * 40},
        ):
            with self.assertRaises(ValueError):
                MODULE.scanner_compatible_fixture(self.root, folder, invalid, before, mapping)
        with self.assertRaises(ValueError):
            MODULE.scanner_compatible_fixture(self.root, folder, identity, before, b"changed mapping")
        with self.assertRaises(ValueError):
            MODULE.scanner_compatible_fixture(self.root, folder, identity, before | {next(iter(keys)): "0" * 64}, mapping)
        original = physical.read_bytes()
        physical.write_text("PROBE\n" + reproduction.FIXTURE_PREFIX + "1.xml\n", encoding="utf-8")
        with self.assertRaises(ValueError):
            MODULE.scanner_compatible_fixture(self.root, folder, identity, before, mapping)
        physical.write_bytes(original)
        physical_map.write_bytes(b"")
        with self.assertRaises(ValueError):
            MODULE.scanner_compatible_fixture(self.root, folder, identity, before, mapping)

    def test_shard_commands_preserve_native_collector_and_unfiltered_baseline(self):
        baseline = MODULE.shard_command("baseline")
        self.assertNotIn("--filter", baseline)
        self.assertIn("XPlat Code Coverage;Format=opencover", baseline)
        self.assertIn("trx;LogFileName=backend-tests.trx", baseline)
        for lane in MODULE.SHARD_LANES[1:]:
            command = MODULE.shard_command(lane)
            self.assertEqual(command[:-2], baseline)
            self.assertEqual(command[-2:], ["--filter", MODULE.shard_filter(lane)])
        self.assertIn("FullyQualifiedName!~Coglatas.Tests.PostgreSql.",
                      MODULE.shard_filter("remaining"))
        with self.assertRaises(ValueError):
            MODULE.shard_command("unknown")

    def test_native_shard_union_rejects_missing_duplicate_extra_and_changed_coverage(self):
        from collections import Counter

        tests = Counter({("Class" + str(i), "Method", "Test" + str(i)): 2
                         for i in range(4)})
        seq = ("SequencePoint", "Module", "Class", "Method", "src/App/Logic.cs", 1)
        branch = ("BranchPoint", "Module", "Class", "Method", "src/App/Logic.cs", 1)
        baseline = {seq: True, branch: True}
        shards = {lane: (Counter({key: 2}), {seq: i == 0, branch: i == 1})
                  for i, (lane, key) in enumerate(zip(MODULE.SHARD_LANES[1:], tests))}
        result = MODULE.require_shard_equivalence(tests, baseline, shards)
        self.assertEqual(result["passedTests"], 8)
        self.assertEqual(result["branchVisited"], 1)
        self.assertEqual(result["sequenceVisited"], 1)
        lane = MODULE.SHARD_LANES[1]
        key = next(iter(tests))
        second = MODULE.SHARD_LANES[2]
        for changed in (
            {name: value for name, value in shards.items() if name != lane},
            shards | {lane: (Counter({key: 1}), shards[lane][1])},
            shards | {lane: (shards[second][0], shards[lane][1])},
            shards | {lane: (Counter({("Unexpected", "Method", "Test"): 2}), shards[lane][1])},
            shards | {lane: (shards[lane][0], {seq: False, branch: False})},
            shards | {lane: (shards[lane][0], {seq: True, branch: False, ("Extra",): False})},
        ):
            with self.assertRaises(ValueError):
                MODULE.require_shard_equivalence(tests, baseline, changed)

    def test_native_point_identity_includes_branch_and_sequence_coordinates(self):
        raw = b"""<CoverageSession><Modules><Module><ModuleName>App</ModuleName>
<Files><File uid="1" fullPath="/_/src/App/Logic.cs"/></Files>
<Classes><Class><FullName>Logic</FullName><Methods><Method><Name>Run()</Name>
<FileRef uid="1"/><SequencePoints><SequencePoint vc="1" uspid="1" ordinal="0" sl="1" sc="1" el="1" ec="3" bec="2" bev="1" fileid="1"/></SequencePoints>
<BranchPoints><BranchPoint vc="0" uspid="1" sl="1" offset="0" offsetend="2" path="0" ordinal="0" fileid="1"/>
<BranchPoint vc="1" uspid="1" sl="1" offset="0" offsetend="4" path="1" ordinal="1" fileid="1"/></BranchPoints>
</Method></Methods></Class></Classes></Module></Modules></CoverageSession>"""
        tracked = {"src/App/Logic.cs"}
        points = MODULE.native_source_points(raw, self.root, tracked)
        self.assertEqual(len(points), 3)
        self.assertEqual(sum(points.values()), 2)
        # Real pinned Coverlet reports contain no sequence-point offset.
        self.assertIn(b'ordinal="0" sl="1"', raw)
        self.assertNotIn(b'offset=', raw.split(b'</SequencePoints>')[0])
        for invalid in (raw.replace(b' uspid="1"', b'', 1),
                        raw.replace(b' offsetend="2"', b'', 1),
                        raw.replace(b'</SequencePoints>', raw.split(b'<SequencePoints>')[1]
                                    .split(b'</SequencePoints>')[0] + b'</SequencePoints>')):
            with self.assertRaises(ValueError):
                MODULE.native_source_points(invalid, self.root, tracked)
        changed = MODULE.native_source_points(raw.replace(b'path="1"', b'path="2"'),
                                               self.root, tracked)
        self.assertNotEqual(points, changed)
        with self.assertRaises(ValueError):
            MODULE.native_source_points(raw.replace(b'fileid="1"', b'fileid="9"'),
                                        self.root, tracked)
        with self.assertRaises(ValueError):
            MODULE.native_source_points(raw, self.root, set())

    def test_shard_trx_identity_preserves_theory_row_multiplicity(self):
        path = str(self.root / MODULE.TEST_ASSEMBLY)
        raw = f"""<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
<Results><UnitTestResult testId="1" testName="Case(1)" outcome="Passed"/>
<UnitTestResult testId="1" testName="Case(1)" outcome="Passed"/></Results>
<TestDefinitions><UnitTest id="1"><TestMethod className="Fixture" name="Case" codeBase="{path}"/></UnitTest></TestDefinitions>
<ResultSummary outcome="Completed"><Counters total="2" executed="2" passed="2" failed="0"/></ResultSummary></TestRun>""".encode()
        self.assertEqual(MODULE.trx_identities(raw, self.root)[("Fixture", "Case", "Case(1)")], 2)
        for invalid in (raw.replace(b'passed="2"', b'passed="1"'),
                        raw.replace(b'outcome="Passed"', b'outcome="Failed"'),
                        raw.replace(b'codeBase="', b'codeBase="/wrong'),
                        raw.replace(b'testId="1"', b'testId="2"')):
            with self.assertRaises(ValueError):
                MODULE.trx_identities(invalid, self.root)


if __name__ == "__main__":
    unittest.main()
