#!/usr/bin/env python3
import importlib.util
import contextlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock
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
        for eol in (b"\r\n", b"\n"):
            with self.subTest(eol=eol):
                prefix = b"            var cache = new Dictionary<string, XmlComment>();" + eol
                suffix = eol + eol + b"            return cache;" + eol
                original = prefix + suffix
                self.assertTrue(MODULE.blank_line_delta(original, prefix + b"\n" + suffix))
                self.assertFalse(MODULE.blank_line_delta(original, original))
                self.assertFalse(MODULE.blank_line_delta(original, b"x" + original))
                self.assertFalse(MODULE.blank_line_delta(original, prefix + b"\n\n" + suffix))
                self.assertFalse(MODULE.blank_line_delta(original, original + b"\n"))
                if eol == b"\r\n":
                    wrong_boundary = prefix + eol + eol + b"\n" + b"            return cache;" + eol
                    self.assertFalse(MODULE.blank_line_delta(original, wrong_boundary))

    def test_changed_cache_layout_does_not_silently_match(self):
        with self.assertRaises(ValueError):
            MODULE.blank_line_delta(b"other\r\n", b"other\r\n\n")

    def test_input_inventory_rejects_wrong_fixture_names(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / "inputs.txt"
            path.write_text("PROBE\nother.xml\n" + MODULE.FIXTURE_PREFIX + "1.xml\n")
            self.assertEqual(MODULE.fixture_count(path), 1)
            path.write_text("PROBE\n" + MODULE.FIXTURE_PREFIX + "2.xml\n")
            with self.assertRaises(ValueError):
                MODULE.fixture_count(path)

    def test_compare_binds_original_bytes_and_rejects_stale_receipts(self):
        # Small temporary byte fixtures exercise the real comparator. They do
        # not claim a compiler execution or enter an authenticated handoff.
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            evidence = root / "artifacts/sonar-openapi-inputs"
            info = {"sourceSha": "a" * 40, "treeSha": "b" * 40,
                    "runId": "123", "attempt": "1", "workspace": str(root)}
            prefix = b"            var cache = new Dictionary<string, XmlComment>();\r\n"
            suffix = b"\r\n            return cache;\r\n"
            for count in MODULE.COUNTS:
                folder = evidence / ("sonar-openapi-reproduction-" + str(count) + "-1")
                folder.mkdir(parents=True)
                keys = []
                for project in MODULE.PROJECTS:
                    project_dir = folder / project
                    project_dir.mkdir()
                    generated = prefix + (b"\n" if count == 2 else b"") + suffix
                    (project_dir / MODULE.GENERATOR).write_bytes(generated)
                    inputs = "PROBE\n" + "".join(MODULE.FIXTURE_PREFIX + str(i + 1) + ".xml\n"
                                                  for i in range(count))
                    (project_dir / "inputs.txt").write_text(inputs, encoding="utf-8")
                    for ext in ("dll", "pdb"):
                        name = "assemblies/" + project + "." + ext
                        target = folder / name
                        target.parent.mkdir(exist_ok=True)
                        target.write_bytes((name + ("changed" if count == 2 else "same")).encode())
                        keys.append(name)
                (folder / "mapping.original").write_bytes(b"same mapping")
                manifest = MODULE.manifest(folder)
                receipt = dict(info, xmlCount=count, hashes=manifest,
                               assemblies={name: manifest[name] for name in keys})
                (folder / "receipt.json").write_text(json.dumps(receipt), encoding="utf-8")
            target = evidence / "sonar-openapi-reproduction-2-1" / "receipt.json"
            original = target.read_bytes()
            result_path = root / "artifacts/sonar-openapi-reproduction-result.json"
            with (mock.patch.object(MODULE, "ROOT", root),
                  mock.patch.object(MODULE, "identity", return_value=info),
                  contextlib.redirect_stdout(io.StringIO())):
                MODULE.compare()
                result = json.loads(result_path.read_bytes())
                self.assertTrue(result["historicalPatternReproduced"])
                self.assertEqual(result["matchingFixtureCounts"], ["2"])
                self.assertTrue(result["observations"]["2"]["mappingUnchanged"])
                result_path.unlink()
                for key, value in (("runId", "124"), ("attempt", "2"), ("xmlCount", 1),
                                   ("hashes", {}), ("assemblies", {})):
                    with self.subTest(key=key):
                        altered = json.loads(original)
                        altered[key] = value
                        target.write_text(json.dumps(altered), encoding="utf-8")
                        with self.assertRaisesRegex(ValueError, "Invalid native build receipt"):
                            MODULE.compare()
                        self.assertFalse(result_path.exists())
                target.write_bytes(original)
                source = target.parent / "Coglatas.Web" / MODULE.GENERATOR
                source.write_bytes(source.read_bytes() + b"\n")
                with self.assertRaisesRegex(ValueError, "Invalid native build receipt"):
                    MODULE.compare()
                self.assertFalse(result_path.exists())

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
