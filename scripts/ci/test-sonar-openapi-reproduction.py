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


    def test_collect_binds_real_git_identity_and_observed_compiler_fixture(self):
        """Run the production collector with isolated compiler-output fixtures."""
        import os
        import shutil
        import subprocess

        native_path = MODULE.ROOT / "scripts/ci/verify-sonar-windows-coverage.py"
        spec = importlib.util.spec_from_file_location("fixture_native", native_path)
        native = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(native)
        for fault in (None, "wrong-input-count", "missing-generator", "missing-mapping", "dirty-source"):
            with self.subTest(fault=fault), tempfile.TemporaryDirectory() as directory:
                root = Path(directory).resolve()
                scripts = root / "scripts/ci"
                scripts.mkdir(parents=True)
                shutil.copyfile(native_path, scripts / native_path.name)
                tracked = root / "tracked.txt"
                tracked.write_text("original\n", encoding="utf-8")
                for args in (["init", "--quiet"], ["add", "scripts", "tracked.txt"],
                             ["-c", "user.name=Coverage fixture", "-c", "user.email=coverage@example.test",
                              "-c", "commit.gpgsign=false", "commit", "--quiet", "-m", "Compiler fixture"]):
                    subprocess.run(["git", *args], cwd=root, check=True, capture_output=True)
                sha = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
                runner = root / "runner"
                runner.mkdir()
                env = {"PROOF_SOURCE_SHA": sha, "GITHUB_RUN_ID": "123", "GITHUB_RUN_ATTEMPT": "1",
                       "GITHUB_WORKSPACE": str(root), "RUNNER_TEMP": str(runner), "PROOF_XML_COUNT": "2"}
                built = []

                def execute(command, **kwargs):
                    if command[0] != "dotnet":
                        return subprocess.run(command, **kwargs)
                    # Only the external compiler boundary is substituted. All
                    # fixture generation, copying, digests and Git checks are real.
                    built.append(command)
                    self.assertEqual(kwargs["cwd"], root)
                    self.assertTrue(kwargs["check"])
                    self.assertEqual(command[:3], ["dotnet", "build", "Coglatas.slnx"])
                    self.assertIn("--no-restore", command)
                    self.assertIn("-p:ContinuousIntegrationBuild=true", command)
                    self.assertIn("-p:DeterministicSourcePaths=true", command)
                    self.assertIn("-p:EmitCompilerGeneratedFiles=true", command)
                    fixture = Path(next(arg.split("=", 1)[1] for arg in command
                                        if arg.startswith("-p:CustomAfterMicrosoftCommonTargets=")))
                    names = sorted(path.name for path in fixture.parent.glob("*.xml"))
                    self.assertEqual(len(names), 2)
                    self.assertEqual(len(ET.parse(fixture).findall(".//AdditionalFiles")), 2)
                    for name, project in native.ASSEMBLIES.items():
                        for ext in ("dll", "pdb"):
                            data = (name + ext + sha).encode()
                            for prefix in (project, "tests/Coglatas.Tests"):
                                path = root / prefix / ("bin/Release/net11.0/" + name + "." + ext)
                                path.parent.mkdir(parents=True, exist_ok=True)
                                path.write_bytes(data)
                    for name, project in MODULE.PROJECTS.items():
                        obj = root / project / "obj/Release/net11.0"
                        obj.mkdir(parents=True, exist_ok=True)
                        if not (fault == "missing-generator" and name == "Coglatas.Web"):
                            (obj / MODULE.GENERATOR).write_bytes(("fixture generated " + name).encode())
                        observed = names[:1] if fault == "wrong-input-count" else names
                        (obj / "sonar-openapi-additional-files.txt").write_text(
                            "PROBE\n" + "\n".join(observed) + "\n", encoding="utf-8")
                    mapping = root / "tests/Coglatas.Tests/bin/Release/net11.0/CoverletSourceRootsMapping_Coglatas.Tests"
                    if fault != "missing-mapping":
                        mapping.write_bytes(b"fixture deterministic mapping")
                    if fault == "dirty-source":
                        tracked.write_text("changed\n", encoding="utf-8")
                    return subprocess.CompletedProcess(command, 0)

                external = mock.Mock(wraps=subprocess)
                external.run.side_effect = execute
                output = root / "artifacts/sonar-openapi-reproduction"
                with (mock.patch.object(MODULE, "ROOT", root),
                      mock.patch.object(MODULE, "subprocess", external),
                      mock.patch.dict(os.environ, env)):
                    if fault == "dirty-source":
                        with self.assertRaises(subprocess.CalledProcessError):
                            MODULE.collect()
                    elif fault is not None:
                        messages = {"wrong-input-count": "Injected XML count differs",
                                    "missing-generator": "Unexpected generated file inventory",
                                    "missing-mapping": "Missing or linked native evidence"}
                        with self.assertRaisesRegex(ValueError, messages[fault]):
                            MODULE.collect()
                    else:
                        MODULE.collect()
                        record = json.loads((output / "receipt.json").read_bytes())
                        self.assertEqual(record["sourceSha"], sha)
                        self.assertEqual(record["treeSha"], subprocess.check_output(
                            ["git", "rev-parse", "HEAD^{tree}"], cwd=root, text=True).strip())
                        self.assertEqual(record["xmlCount"], 2)
                        self.assertEqual(record["hashes"], MODULE.manifest(output))
                        self.assertEqual(len(record["assemblies"]), 2 * len(native.ASSEMBLIES))
                        self.assertEqual((output / "mapping.original").read_bytes(),
                                         b"fixture deterministic mapping")
                        for project in MODULE.PROJECTS:
                            self.assertEqual(MODULE.fixture_count(output / project / "inputs.txt"), 2)
                    if fault is not None:
                        self.assertFalse((output / "receipt.json").exists())
                    self.assertEqual(len(built), 1)


if __name__ == "__main__":
    unittest.main()
