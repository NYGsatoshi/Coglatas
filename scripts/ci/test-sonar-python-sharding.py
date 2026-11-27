#!/usr/bin/env python3
"""Regression controls for the credential-free Python qualification."""

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location(
    "python_sharding", Path(__file__).with_name("verify-sonar-python-sharding.py"))
MODULE = importlib.util.module_from_spec(spec)
spec.loader.exec_module(MODULE)


class PythonShardingTests(unittest.TestCase):
    def canonical(self):
        return (MODULE.ROOT / MODULE.CONTROL_LOOP).read_text()

    def test_partition_is_complete_and_uses_original_dispatch(self):
        controls, dispatch = MODULE.parse_loop(self.canonical())
        first = MODULE.selection("shard-1", controls)
        second = MODULE.selection("shard-2", controls)
        self.assertEqual(set(first) | set(second), set(controls))
        self.assertFalse(set(first) & set(second))
        self.assertEqual(MODULE.selection("baseline", controls), controls)
        self.assertIn('python3 -B -m coverage run "$control"', dispatch)
        self.assertIn("unittest discover", dispatch)
        self.assertIn(MODULE.MUTATION_CONTROL, controls)
        new_control = "scripts/ci/test-new-proof.py"
        self.assertIn(new_control, MODULE.selection("shard-1", controls + [new_control]))
        with self.assertRaises(ValueError):
            MODULE.selection("shard-2", [name for name in controls if name not in MODULE.SECOND_GROUP])
        with self.assertRaises(ValueError):
            MODULE.parse_loop(self.canonical().replace("          for control in ", "          for item in ", 1))

    def test_unittest_execution_requires_real_nonempty_all_passed_summary(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            control = "scripts/ci/test-a.py"
            self.assertEqual(MODULE.execution_result(control, "...\nRan 3 tests in 0.1s\n\nOK\n", root),
                             {"kind": "unittest", "tests": 3})
            for output in ("Ran 0 tests in 0.1s\nOK", "Ran 3 tests in 0.1s\nOK (skipped=1)",
                           "Ran 3 tests in 0.1s\nFAILED", "OK", "Ran 1 test in 0.1s\nOK\nRan 1 test in 0.1s\nOK"):
                with self.assertRaises(ValueError):
                    MODULE.execution_result(control, output, root)

    def test_native_mutation_ledger_preserves_case_identity(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            report = root / MODULE.MUTATION_REPORT
            report.parent.mkdir(parents=True)
            row = {"mutation": "negative-control", "expectedExit": 1, "actualExit": 1, "result": "PASS"}
            report.write_text(json.dumps([row]))
            self.assertEqual(MODULE.execution_result(MODULE.MUTATION_CONTROL, "", root),
                             {"kind": "mutation", "cases": [row]})
            for rows in ([], [row, row], [row | {"actualExit": 0}], [row | {"result": "FALSE_PASS"}]):
                report.write_text(json.dumps(rows))
                with self.assertRaises(ValueError):
                    MODULE.execution_result(MODULE.MUTATION_CONTROL, "", root)

    def test_execution_union_requires_exact_control_and_result_inventory(self):
        controls, _ = MODULE.parse_loop(self.canonical())
        baseline = {name: {"kind": "unittest", "tests": 2} for name in controls}
        baseline[MODULE.MUTATION_CONTROL] = {"kind": "mutation", "cases": [
            {"mutation": "fixture", "expectedExit": 1, "actualExit": 1, "result": "PASS"}]}
        shards = {lane: {name: baseline[name] for name in MODULE.selection(lane, controls)}
                  for lane in MODULE.LANES[1:]}
        result = MODULE.require_execution_equivalence(controls, baseline, shards)
        self.assertEqual(result["controls"], len(controls))
        self.assertEqual(result["unittestCases"], (len(controls) - 1) * 2)
        first = next(iter(shards["shard-1"]))
        for changed in (
            {"shard-1": shards["shard-1"]},
            shards | {"shard-1": {name: row for name, row in shards["shard-1"].items() if name != first}},
            shards | {"shard-1": shards["shard-1"] | {first: {"kind": "unittest", "tests": 999}}},
            shards | {"shard-2": shards["shard-2"] | {first: baseline[first]}},
        ):
            with self.assertRaises(ValueError):
                MODULE.require_execution_equivalence(controls, baseline, changed)

    def test_single_pass_preserves_full_canonical_control_inventory(self):
        controls, _ = MODULE.parse_loop(self.canonical())
        baseline = {name: {"kind": "unittest", "tests": 3} for name in controls}
        baseline[MODULE.MUTATION_CONTROL] = {"kind": "mutation", "cases": [
            {"mutation": "fixture", "expectedExit": 1, "actualExit": 1, "result": "PASS"}]}
        shards = {lane: {name: baseline[name] for name in MODULE.selection(lane, controls)}
                  for lane in MODULE.LANES[1:]}
        self.assertEqual(MODULE.expected_lanes("full"), MODULE.LANES)
        self.assertEqual(MODULE.expected_lanes("single-pass"), MODULE.LANES[1:])
        self.assertEqual(MODULE.require_execution_equivalence(controls, None, shards),
                         MODULE.require_execution_equivalence(controls, baseline, shards))
        with self.assertRaises(ValueError):
            MODULE.expected_lanes("unknown")
        first = next(name for name in shards["shard-1"] if name != MODULE.MUTATION_CONTROL)
        for wrong in (
            {"shard-1": shards["shard-1"]},
            shards | {"shard-1": {name: row for name, row in shards["shard-1"].items() if name != first}},
            shards | {"shard-1": shards["shard-1"] | {first: {"kind": "unittest", "tests": 0}}},
            shards | {"shard-1": shards["shard-1"] | {first: {"kind": "unittest", "tests": True}}},
            shards | {"shard-1": shards["shard-1"] | {first: {"kind": "mutation", "cases": []}}},
            shards | {"shard-2": shards["shard-2"] | {first: baseline[first]}},
        ):
            with self.assertRaises(ValueError):
                MODULE.require_execution_equivalence(controls, None, wrong)
        mutation_lane = next(lane for lane in MODULE.LANES[1:]
                             if MODULE.MUTATION_CONTROL in shards[lane])
        for cases in ([], [{"mutation": "x", "expectedExit": 1, "actualExit": 0, "result": "PASS"}],
                      [baseline[MODULE.MUTATION_CONTROL]["cases"][0]] * 2):
            with self.assertRaises(ValueError):
                MODULE.require_execution_equivalence(
                    controls, None, shards | {mutation_lane: shards[mutation_lane] |
                        {MODULE.MUTATION_CONTROL: {"kind": "mutation", "cases": cases}}})

    def test_native_union_keeps_all_original_line_and_arc_identities(self):
        first = {"a.py": (frozenset({1, 2}), frozenset({(1, 2)}))}
        second = {"a.py": (frozenset({2, 3}), frozenset({(2, 3)})),
                  "b.py": (frozenset({4}), frozenset({(4, -1)}))}
        combined = MODULE.native_union({"shard-1": first, "shard-2": second})
        self.assertEqual(combined, {
            "a.py": (frozenset({1, 2, 3}), frozenset({(1, 2), (2, 3)})),
            "b.py": second["b.py"]})
        for wrong in ({"shard-1": first}, {"shard-1": first, "shard-2": {}},
                      {"shard-1": first, "shard-2": second, "baseline": first}):
            with self.assertRaises(ValueError):
                MODULE.native_union(wrong)

    def test_manifest_rejects_missing_empty_and_linked_native_bytes(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            for name in MODULE.NATIVE_REPORTS:
                (folder / name).write_bytes(b"fixture")
            initial = MODULE.file_manifest(folder)
            (folder / MODULE.NATIVE_REPORTS[0]).write_bytes(b"changed")
            self.assertNotEqual(initial, MODULE.file_manifest(folder))
            (folder / MODULE.NATIVE_REPORTS[0]).write_bytes(b"")
            with self.assertRaises(ValueError):
                MODULE.file_manifest(folder)
            (folder / MODULE.NATIVE_REPORTS[0]).unlink()
            with self.assertRaises(ValueError):
                MODULE.file_manifest(folder)
            (folder / MODULE.NATIVE_REPORTS[0]).symlink_to(folder / MODULE.NATIVE_REPORTS[1])
            with self.assertRaises(ValueError):
                MODULE.file_manifest(folder)

    def test_native_report_validation_never_normalizes_coverage_points(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            json_path = root / "coverage.native.json"
            xml_path = root / "coverage.xml"
            file_data = {"scripts/ci/fixture.py": {
                "executed_lines": [1], "missing_lines": [2],
                "executed_branches": [[1, 2]], "missing_branches": [[1, 3]],
            }}
            payload = {"meta": {"branch_coverage": True, "version": "7.17.0"},
                       "files": file_data}
            json_path.write_text(json.dumps(payload), encoding="utf-8")
            self.assertEqual(MODULE.native_json(json_path), file_data)
            for invalid in (payload | {"files": {}},
                            payload | {"meta": {"branch_coverage": False, "version": "7.17.0"}},
                            payload | {"meta": {"branch_coverage": True, "version": "7.0"}}):
                json_path.write_text(json.dumps(invalid), encoding="utf-8")
                with self.assertRaises(ValueError):
                    MODULE.native_json(json_path)
            xml = ('<coverage timestamp="1"><packages><package><classes>'
                   '<class filename="fixture.py"><lines><line number="1" hits="1"/>'
                   '</lines></class></classes></package></packages></coverage>')
            xml_path.write_text(xml, encoding="utf-8")
            baseline = MODULE.native_xml(xml_path)
            xml_path.write_text(xml.replace('timestamp="1"', 'timestamp="2"'), encoding="utf-8")
            self.assertEqual(MODULE.native_xml(xml_path), baseline)
            xml_path.write_text(xml.replace('hits="1"', 'hits="0"'), encoding="utf-8")
            self.assertNotEqual(MODULE.native_xml(xml_path), baseline)
            xml_path.write_text("<coverage/>", encoding="utf-8")
            with self.assertRaises(ValueError):
                MODULE.native_xml(xml_path)

    def test_production_admission_preserves_the_credential_free_proof(self):
        workflow = (MODULE.ROOT / ".github/workflows/sonar-python-shard-proof.yml").read_text()
        self.assertIn("lane: [baseline, shard-1, shard-2]", workflow)
        self.assertIn("needs: collect", workflow)
        self.assertIn("github.event.pull_request.head.repo.full_name == github.repository", workflow)
        self.assertNotIn("secrets.", workflow)
        self.assertNotIn("SONAR_TOKEN", workflow)
        production = (MODULE.ROOT / MODULE.WORKFLOW).read_text()
        lanes = production.split("  python-native-lanes:\n", 1)[1].split("\n  python-coverage:\n", 1)[0]
        join = production.split("  python-coverage:\n", 1)[1].split("\n  javascript-coverage:\n", 1)[0]
        scanner = production.split("  sonarcloud:\n", 1)[1]
        for job in (lanes, join):
            self.assertIn("contents: read", job)
            self.assertNotIn("secrets.", job)
            self.assertNotIn("SONAR_TOKEN", job)
            self.assertNotIn("environment: sonarcloud-analysis", job)
        self.assertIn("analysis_kind == 'pull_request'", lanes)
        self.assertIn('["shard-1","shard-2"]', lanes)
        self.assertIn('["baseline","shard-1","shard-2"]', lanes)
        self.assertIn("analysis_kind == 'pull_request' && 'single-pass' || 'full'", join)
        self.assertIn('compare --mode "$PROOF_MODE"', join)
        self.assertIn("Prove PR single-pass output matches the full independent baseline", workflow)
        self.assertIn("needs: [target, python-native-lanes]", join)
        self.assertIn('collect --lane "$PROOF_LANE"', lanes)
        self.assertIn("verify-sonar-python-sharding.py compare", join)
        self.assertLess(join.index("Require complete native Python coverage admission"),
                        join.index("Stage native coverage.py combined evidence"))
        self.assertLess(join.index("Stage native coverage.py combined evidence"),
                        join.index("Bind Python coverage handoff to exact execution"))
        self.assertIn("artifacts/sonar-python/receipt.json", scanner)
        self.assertIn("artifacts/sonar-python/coverage.xml,artifacts/sonar-gcp/coverage.xml", scanner)
        self.assertNotIn("          for control in ", production)
        self.assertEqual(self.canonical().count("          for control in "), 1)


if __name__ == "__main__":
    unittest.main()
