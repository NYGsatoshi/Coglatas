#!/usr/bin/env python3
import importlib.util
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

spec = importlib.util.spec_from_file_location(
    "proof", Path(__file__).with_name("verify-sonar-openapi-reproduction.py"))
MODULE = importlib.util.module_from_spec(spec)
spec.loader.exec_module(MODULE)


class ReproductionTests(unittest.TestCase):
    def test_fixture_counts_and_no_project_changes(self):
        for count in MODULE.COUNTS:
            with tempfile.TemporaryDirectory() as root:
                folder = Path(root) / "fixture"
                path = MODULE.write_fixture(folder, count)
                tree = ET.parse(path)
                self.assertEqual(len(tree.findall(".//AdditionalFiles")), count)
                self.assertEqual(len(list(folder.glob("*.xml"))), count)
                self.assertIn("BeforeTargets", tree.find("Target").attrib)
        with tempfile.TemporaryDirectory() as root:
            with self.assertRaises(ValueError):
                MODULE.write_fixture(Path(root) / "fixture", 3)

    def test_blank_line_is_not_equivalence_or_arbitrary_normalization(self):
        original = b"cache\n            return cache;\n"
        self.assertTrue(MODULE.blank_line_delta(original, b"cache\n\n            return cache;\n"))
        self.assertFalse(MODULE.blank_line_delta(original, original))
        self.assertFalse(MODULE.blank_line_delta(original, b"xcache\n            return cache;\n"))
        self.assertFalse(MODULE.blank_line_delta(original, b"cache\n\n\n            return cache;\n"))

    def test_input_inventory_rejects_wrong_fixture_names(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / "inputs.txt"
            path.write_text("PROBE\nother.xml\n" + MODULE.FIXTURE_PREFIX + "1.xml\n")
            self.assertEqual(MODULE.fixture_count(path), 1)
            path.write_text("PROBE\n" + MODULE.FIXTURE_PREFIX + "2.xml\n")
            with self.assertRaises(ValueError):
                MODULE.fixture_count(path)

    def test_workflow_is_read_only_and_independent(self):
        workflow = (MODULE.ROOT / ".github/workflows/sonar-openapi-reproduction.yml").read_text()
        production = (MODULE.ROOT / ".github/workflows/sonarcloud-ci.yml").read_text()
        self.assertIn("count: [0, 1, 2]", workflow)
        self.assertIn("needs: build", workflow)
        self.assertNotIn("secrets.", workflow)
        self.assertNotIn("SONAR_TOKEN", workflow)
        self.assertNotIn("verify-sonar-openapi-reproduction.py", production)


if __name__ == "__main__":
    unittest.main()
