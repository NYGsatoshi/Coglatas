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
        return (MODULE.ROOT / MODULE.WORKFLOW).read_text()

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
        baseline[MODULE.MUTATION_CONTROL] = {"kind": "mutation", "cases": ["fixture"]}
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

    def test_workflow_is_qualification_only(self):
        workflow = (MODULE.ROOT / ".github/workflows/sonar-python-shard-proof.yml").read_text()
        self.assertIn("lane: [baseline, shard-1, shard-2]", workflow)
        self.assertIn("needs: collect", workflow)
        self.assertIn("github.event.pull_request.head.repo.full_name == github.repository", workflow)
        self.assertNotIn("secrets.", workflow)
        self.assertNotIn("SONAR_TOKEN", workflow)
        self.assertNotIn("verify-sonar-python-sharding.py", self.canonical())


if __name__ == "__main__":
    unittest.main()
