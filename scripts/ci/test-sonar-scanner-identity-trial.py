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
        self.assertIn("sonar.cs.opencover.reportsPaths=artifacts/sonar-dotnet-shard-inputs/files-db/verified.opencover.xml,artifacts/sonar-dotnet-shard-inputs/other-db/verified.opencover.xml,artifacts/sonar-dotnet-shard-inputs/security-aq/verified.opencover.xml,artifacts/sonar-dotnet-shard-inputs/remaining/verified.opencover.xml", scanner)
        self.assertLess(scanner.index("Generate .NET coverage without analysis credentials"),
                        scanner.index("shard-admit"))
        self.assertLess(scanner.index("shard-admit"), scanner.index("Publish SonarCloud report"))
        self.assertIn("shard-admit-rebuilt", scanner)
        self.assertIn("if: env.SONAR_ANALYSIS_KIND == 'pull_request'", scanner)
        self.assertLess(scanner.index("shard-admit-rebuilt"), scanner.index("Publish SonarCloud report"))
        self.assertIn("Verify executed tests and production coverage before publication", scanner)
        self.assertIn("Publish SonarCloud report", scanner)


    def test_bind_prepare_and_compare_actual_on_disk_diagnostic_evidence(self):
        """Verify orchestration, original bytes, negative results, and sanitized observations."""
        import contextlib
        import io
        import os
        import shutil
        import subprocess
        from unittest import mock

        source = MODULE.ROOT / "scripts/ci"
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            scripts = root / "scripts/ci"
            scripts.mkdir(parents=True)
            for name in ("verify-sonar-openapi-reproduction.py", "verify-sonar-windows-coverage.py"):
                shutil.copyfile(source / name, scripts / name)
            for args in (["init", "--quiet"], ["add", "scripts"],
                         ["-c", "user.name=Coverage fixture", "-c", "user.email=coverage@example.test",
                          "-c", "commit.gpgsign=false", "commit", "--quiet", "-m", "Scanner fixture"]):
                subprocess.run(["git", *args], cwd=root, check=True, capture_output=True)
            sha = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
            runner = root / "runner"
            runner.mkdir()
            github_output = runner / "output"
            folder = root / "artifacts/sonar-openapi-reproduction"
            folder.mkdir(parents=True)
            output = root / "artifacts/sonar-scanner-identity-result.json"
            env = {"PROOF_SOURCE_SHA": sha, "GITHUB_RUN_ID": "123", "GITHUB_RUN_ATTEMPT": "1",
                   "GITHUB_WORKSPACE": str(root), "RUNNER_TEMP": str(runner),
                   "GITHUB_OUTPUT": str(github_output), "PROOF_XML_COUNT": "2"}
            with (mock.patch.object(MODULE, "ROOT", root),
                  mock.patch.object(MODULE, "FOLDER", folder),
                  mock.patch.dict(os.environ, env), contextlib.redirect_stdout(io.StringIO())):
                helper = MODULE.reproduction()
                verifier = MODULE.native()
                info = helper.identity()
                for name, project in verifier.ASSEMBLIES.items():
                    for ext in ("dll", "pdb"):
                        relative = "bin/Release/net11.0/" + name + "." + ext
                        data = (name + ext + sha).encode()
                        for prefix in (project, "tests/Coglatas.Tests"):
                            path = root / prefix / relative
                            path.parent.mkdir(parents=True, exist_ok=True)
                            path.write_bytes(data)
                assemblies = verifier.assemblies(root, folder, sha, True)
                mapping = root / "tests/Coglatas.Tests/bin/Release/net11.0/CoverletSourceRootsMapping_Coglatas.Tests"
                mapping.write_bytes(b"fixture mapping")
                (folder / "mapping.original").write_bytes(mapping.read_bytes())
                for name, project in helper.PROJECTS.items():
                    producer = folder / name
                    producer.mkdir()
                    obj = root / project / "obj/Release/net11.0"
                    obj.mkdir(parents=True)
                    data = ("fixture generated " + name).encode()
                    (producer / helper.GENERATOR).write_bytes(data)
                    (obj / helper.GENERATOR).write_bytes(data)
                    (producer / "inputs.txt").write_text(
                        "PROBE\n" + helper.FIXTURE_PREFIX + "1.xml\n" +
                        helper.FIXTURE_PREFIX + "2.xml\n", encoding="utf-8")
                    (obj / MODULE.INVENTORY_NAME).write_text(
                        "PROBE\nSCANNER=True\nSonarLint.xml\nSonarProjectConfig.xml\n"
                        "private-fixture.txt\n", encoding="utf-8")
                receipt = dict(info, xmlCount=2, hashes=helper.manifest(folder), assemblies=assemblies)
                receipt_file = folder / "receipt.json"
                receipt_file.write_text(json.dumps(receipt), encoding="utf-8")
                digest = helper.digest(receipt_file)

                MODULE.bind()
                self.assertEqual(github_output.read_text(encoding="utf-8"), "digest=" + digest + "\n")
                os.environ["EXPECTED_RECEIPT_DIGEST"] = "0" * 64
                with self.assertRaisesRegex(ValueError, "Diagnostic producer receipt digest differs"):
                    MODULE.prepare()
                observer = runner / "sonar-scanner-input-observer/observer.targets"
                self.assertFalse(observer.exists())
                os.environ["EXPECTED_RECEIPT_DIGEST"] = digest
                MODULE.prepare()
                self.assertTrue(observer.is_file())
                self.assertEqual(helper.manifest(folder), receipt["hashes"])

                MODULE.compare()
                result = json.loads(output.read_bytes())
                self.assertTrue(result["byteIdentical"])
                self.assertTrue(result["mappingIdentical"])
                self.assertEqual(result["changedAssemblyFiles"], [])
                self.assertEqual(result["scannerAssemblyDigests"], assemblies)
                self.assertEqual(result["producerReceiptSha256"], digest)
                self.assertEqual(result["scope"], "DIAGNOSTIC_ONLY_SCANNER_NATIVE_TESTS_REMAIN_REQUIRED")
                self.assertNotIn("private-fixture.txt", output.read_text(encoding="utf-8"))
                self.assertEqual(result["generated"]["Coglatas.Web"]["scannerInputs"]["nonXmlCount"], 1)
                output.unlink()

                def negative(streams, suffix, assertion):
                    # Accept only already-open fixture streams, not paths from
                    # receipts, compiler inventories or arbitrary mapping keys.
                    originals = [(stream, stream.read()) for stream in streams]

                    def replace_bytes(stream, value):
                        stream.seek(0)
                        stream.write(value)
                        stream.truncate()
                        stream.flush()

                    try:
                        for stream, value in originals:
                            replace_bytes(stream, value + suffix)
                        MODULE.compare()
                        altered = json.loads(output.read_bytes())
                        self.assertFalse(altered["byteIdentical"])
                        assertion(altered)
                        output.unlink()
                    finally:
                        for stream, value in originals:
                            replace_bytes(stream, value)
                            stream.seek(0)
                            self.assertEqual(stream.read(), value)

                # The temporary directory is created by this test. Bind
                # each mutation to an explicit, locally created fixture file,
                # independently of the contents of any captured evidence.
                with (root / "src/Coglatas.Web/obj/Release/net11.0/OpenApiXmlCommentSupport.generated.cs").open("r+b") as generated:
                    negative((generated,), b"\n",
                             lambda altered: self.assertFalse(altered["generated"]["Coglatas.Web"]["identical"]))
                with (root / "tests/Coglatas.Tests/bin/Release/net11.0/CoverletSourceRootsMapping_Coglatas.Tests").open("r+b") as source_map:
                    negative((source_map,), b" changed",
                             lambda altered: self.assertFalse(altered["mappingIdentical"]))
                with ((root / "src/Coglatas.Web/bin/Release/net11.0/Coglatas.Web.dll").open("r+b") as project_output,
                      (root / "tests/Coglatas.Tests/bin/Release/net11.0/Coglatas.Web.dll").open("r+b") as executed_copy):
                    negative((project_output, executed_copy), b" changed",
                             lambda altered: self.assertEqual(altered["changedAssemblyFiles"],
                                                              ["assemblies/Coglatas.Web.dll"]))
                inventory = root / "src/Coglatas.Web/obj/Release/net11.0/sonar-scanner-additional-files.txt"
                raw = inventory.read_bytes()
                try:
                    inventory.write_bytes(raw + b"/private/path.xml\n")
                    with self.assertRaisesRegex(ValueError, "Unexpected non-basename compiler input"):
                        MODULE.compare()
                    self.assertFalse(output.exists())
                finally:
                    inventory.write_bytes(raw)
                self.assertEqual(helper.manifest(folder), receipt["hashes"])
                self.assertEqual(helper.digest(receipt_file), digest)
                MODULE.compare()
                self.assertEqual(json.loads(output.read_bytes()), result)

    def test_collect_requires_the_qualified_two_xml_fixture(self):
        import os
        from unittest import mock

        helper = mock.Mock()
        with mock.patch.object(MODULE, "reproduction", return_value=helper):
            with mock.patch.dict(os.environ, {"PROOF_XML_COUNT": "1"}):
                with self.assertRaisesRegex(ValueError, "Expected qualified two-XML diagnostic fixture"):
                    MODULE.collect()
                helper.collect.assert_not_called()
            with mock.patch.dict(os.environ, {"PROOF_XML_COUNT": "2"}):
                MODULE.collect()
                helper.collect.assert_called_once_with()


if __name__ == "__main__":
    unittest.main()
