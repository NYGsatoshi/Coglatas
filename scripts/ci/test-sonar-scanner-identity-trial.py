#!/usr/bin/env python3
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

spec = importlib.util.spec_from_file_location(
    "trial", Path(__file__).with_name("verify-sonar-scanner-identity-trial.py"))
MODULE = importlib.util.module_from_spec(spec)
spec.loader.exec_module(MODULE)


class ScannerIdentityTrialTests(unittest.TestCase):
    def test_observer_never_modifies_compiler_inputs(self):
        with tempfile.TemporaryDirectory() as directory:
            path = MODULE.write_observer(Path(directory) / "probe")
            tree = ET.parse(path)
            self.assertEqual(tree.find("Target").attrib["AfterTargets"], "CoreCompile")
            self.assertEqual(len(tree.findall(".//ItemGroup")), 0)
            self.assertEqual(len(tree.findall(".//AdditionalFiles")), 0)
            lines = tree.find(".//WriteLinesToFile").attrib["Lines"]
            self.assertIn("%(Filename)%(Extension)", lines)
            self.assertNotIn("%(FullPath)", lines)
            self.assertIn("SCANNER=$(SonarQubeTargetsImported)", lines)

    def test_only_known_names_and_counts_are_exported(self):
        result = MODULE.summarize_inputs(
            "PROBE\nSCANNER=True\nSonarLint.xml\nSonarProjectConfig.xml\n"
            "private-description.xml\nprivate-file.txt\n")
        self.assertEqual(result["xmlCount"], 3)
        self.assertEqual(result["otherXmlCount"], 1)
        self.assertEqual(result["nonXmlCount"], 1)
        self.assertEqual(result["knownSonarXmlCounts"], {"sonarlint.xml": 1, "sonarprojectconfig.xml": 1})
        self.assertNotIn("private", str(result))
        for invalid in ("PROBE\nSCANNER=false\n", "PROBE\n", "PROBE\nSCANNER=true\n/a.xml\n",
                        "PROBE\nSCANNER=true\nC:\\a.xml\n"):
            with self.assertRaises(ValueError):
                MODULE.summarize_inputs(invalid)

    def test_byte_difference_is_never_equivalence(self):
        self.assertEqual(MODULE.compare_digests({"a.dll": "x"}, {"a.dll": "x"}), [])
        self.assertEqual(MODULE.compare_digests({"a.dll": "x"}, {"a.dll": "y"}), ["a.dll"])
        with self.assertRaises(ValueError):
            MODULE.compare_digests({"a.dll": "x"}, {"b.dll": "x"})

    def test_producer_receipt_requires_exact_attempt_and_original_native_bytes(self):
        helper = MODULE.reproduction()
        verifier = MODULE.native()
        info = {"sourceSha": "a" * 40, "treeSha": "b" * 40,
                "runId": "123", "attempt": "1", "workspace": "/fixture"}
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            keys = {"assemblies/" + name + "." + ext
                    for name in verifier.ASSEMBLIES for ext in ("dll", "pdb")}
            for name in keys:
                path = folder / name
                path.parent.mkdir(exist_ok=True)
                path.write_bytes(("fixture " + name).encode())
            (folder / "mapping.original").write_bytes(b"fixture mapping")
            for name in helper.PROJECTS:
                project = folder / name
                project.mkdir()
                (project / helper.GENERATOR).write_bytes(b"fixture generated source")
                (project / "inputs.txt").write_text(
                    "PROBE\n" + helper.FIXTURE_PREFIX + "1.xml\n" +
                    helper.FIXTURE_PREFIX + "2.xml\n", encoding="utf-8")
            manifest = helper.manifest(folder)
            receipt = dict(info, xmlCount=2, hashes=manifest,
                           assemblies={name: manifest[name] for name in keys})
            receipt_path = folder / "receipt.json"
            receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
            digest = helper.digest(receipt_path)
            self.assertEqual(MODULE.producer_receipt(folder, info, digest), receipt)
            for invalid in ("", "0" * 64):
                with self.assertRaises(ValueError):
                    MODULE.producer_receipt(folder, info, invalid)
            for key, value in (("sourceSha", "c" * 40), ("runId", "124"), ("attempt", "2")):
                with self.assertRaisesRegex(ValueError, "Diagnostic producer evidence differs"):
                    MODULE.producer_receipt(folder, info | {key: value}, digest)
            path = folder / sorted(keys)[0]
            original = path.read_bytes()
            path.write_bytes(original + b"tampered")
            with self.assertRaisesRegex(ValueError, "Diagnostic producer evidence differs"):
                MODULE.producer_receipt(folder, info, digest)
            path.write_bytes(original)
            (folder / "unexpected.bin").write_bytes(b"fixture")
            with self.assertRaisesRegex(ValueError, "Diagnostic producer evidence differs"):
                MODULE.producer_receipt(folder, info, digest)

    def test_diagnostic_producer_does_not_supply_scanner_coverage(self):
        workflow = (MODULE.ROOT / ".github/workflows/sonarcloud-ci.yml").read_text()
        producer = workflow.split("  dotnet-identity-probe:\n", 1)[1].split("\n  sonarcloud:\n", 1)[0]
        scanner = workflow.split("  sonarcloud:\n", 1)[1]
        self.assertIn("needs: target", producer)
        self.assertIn("contents: read", producer)
        self.assertNotIn("secrets.", producer)
        self.assertNotIn("SONAR_TOKEN", producer)
        self.assertNotIn("environment: sonarcloud-analysis", producer)
        self.assertIn("dotnet-identity-probe]", scanner)
        self.assertEqual(scanner.count("secrets.SONAR_TOKEN"), 2)
        self.assertLess(scanner.index("Verify diagnostic bytes"), scanner.index("Begin pinned SonarCloud analysis"))
        self.assertLess(scanner.index("Build inside the SonarScanner"), scanner.index("Compare actual scanner build"))
        self.assertLess(scanner.index("Compare actual scanner build"),
                        scanner.index("Generate .NET coverage without analysis credentials"))
        self.assertIn("dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj", scanner)
        self.assertIn("sonar.cs.opencover.reportsPaths=artifacts/sonar-dotnet/verified.opencover.xml", scanner)
        self.assertIn("Verify executed tests and production coverage before publication", scanner)
        self.assertIn("Publish SonarCloud report", scanner)


if __name__ == "__main__":
    unittest.main()
