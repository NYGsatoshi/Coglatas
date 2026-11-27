#!/usr/bin/env python3
import importlib.util
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
