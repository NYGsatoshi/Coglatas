"""False-green controls for observed SEC-ARCH execution capture."""

import copy
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

import sec_arch_evidence as evidence
import sec_arch_assembly_binding as assemblies

NOW = datetime(2026, 10, 9, 13, tzinfo=timezone.utc)
Q = "{" + evidence.NS["t"] + "}"


def fixture() -> ET.Element:
    root = ET.Element(Q + "TestRun")
    ET.SubElement(root, Q + "Times", start="2026-10-09T12:00:00Z", finish="2026-10-09T12:01:00Z")
    results, definitions = ET.SubElement(root, Q + "Results"), ET.SubElement(root, Q + "TestDefinitions")
    for method, count in evidence.EXPECTED.items():
        for index in range(count):
            name = list(evidence.THEORY_CASES[method].values())[index] if method in evidence.THEORY_CASES else method + "(case:" + str(index) + ")"
            identity = str(len(results))
            definition = ET.SubElement(definitions, Q + "UnitTest", id=identity, name=name)
            ET.SubElement(definition, Q + "Execution", id=identity)
            group, member = method.rsplit(".", 1)
            ET.SubElement(definition, Q + "TestMethod", className=group, name=member)
            ET.SubElement(results, Q + "UnitTestResult", testId=identity, executionId=identity,
                          testName=name, outcome="Passed", startTime="2026-10-09T12:00:01Z", endTime="2026-10-09T12:00:02Z")
    ET.SubElement(ET.SubElement(root, Q + "ResultSummary"), Q + "Counters")
    recalculate(root)
    return root


def recalculate(root: ET.Element) -> None:
    results = root.find(Q + "Results")
    counts = {outcome: sum(row.attrib["outcome"] == outcome for row in results)
              for outcome in ("Passed", "Failed")}
    root.find(Q + "ResultSummary/" + Q + "Counters").attrib.update(
        total=str(len(results)), passed=str(counts["Passed"]), failed=str(counts["Failed"]),
        executed=str(counts["Passed"] + counts["Failed"]))


def observe(root: ET.Element) -> dict:
    return evidence.observed_trx(ET.tostring(root), NOW)


@unittest.skipUnless(shutil.which("git"), "Actual capture subprocess controls require Git.")
class CaptureInvocationProcessTests(unittest.TestCase):
    """Actual imports and Git guards; synthetic bytes grant no runtime coverage."""

    @staticmethod
    def prepare(root: Path) -> str:
        source = Path(__file__).resolve().parent
        scripts = root / "scripts/ci"
        scripts.mkdir(parents=True)
        for name in ("sec_arch_evidence.py", "sec_arch_assembly_binding.py", "sec_arch_http_theory_cases.py"):
            shutil.copyfile(source / name, scripts / name)
        (root / ".gitignore").write_text("artifacts/\n**/bin/\n", encoding="utf-8")
        for arguments in (("init",), ("config", "user.email", "capture@example.invalid"),
                          ("config", "user.name", "Synthetic capture fixture"),
                          ("add", "."), ("commit", "-m", "Synthetic capture source")):
            subprocess.run(["git", "-C", str(root), *arguments], check=True,
                           capture_output=True, text=True, timeout=20)
        sha = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
        for name in assemblies.ASSEMBLIES:
            for path in {assemblies.assembly_path(root, name), assemblies.loaded_assembly_path(root, name)}:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(("synthetic invocation fixture " + name).encode())
        stamp = root / "artifacts/ci/dotnet-build-sha"
        stamp.parent.mkdir(parents=True)
        stamp.write_text(sha, encoding="utf-8")
        (root / "artifacts/execution.trx").write_bytes(ET.tostring(fixture()))
        return sha

    @staticmethod
    def run_capture(root: Path, sha: str, flags: list[str]) -> subprocess.CompletedProcess:
        # sys.path is explicit because some native embedded Python distributions
        # omit the current directory. The actual imported capture source is unchanged.
        bootstrap = """
import json, sys
from pathlib import Path
from datetime import datetime, timezone
root = Path(sys.argv[1])
sys.path.insert(0, str(root / 'scripts/ci'))
import sec_arch_evidence as evidence
try:
    report = evidence.capture(root, root / 'artifacts/execution.trx', sys.argv[2],
        datetime(2026, 10, 9, 13, tzinfo=timezone.utc), {'fixture': 'SYNTHETIC_INVOCATION_ONLY'})
except ValueError as error:
    print(str(error))
    raise SystemExit(1)
print(json.dumps({'candidateSha': report['candidateSha'],
    'assemblyCount': len(report['assemblyDigests']), 'outcome': report['observedExecution']['outcome']}))
"""
        environment = os.environ.copy()
        for variable in ("PYTHONDONTWRITEBYTECODE", "PYTHONPYCACHEPREFIX"):
            environment.pop(variable, None)
        environment["GITHUB_ACTIONS"] = "true"
        return subprocess.run([sys.executable, *flags, "-c", bootstrap, str(root), sha],
            env=environment, capture_output=True, text=True, timeout=20)

    def test_default_import_reproduces_untracked_bytecode_and_exact_clean_rejection(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            sha = self.prepare(root)
            result = self.run_capture(root, sha, [])
            self.assertEqual(1, result.returncode, result.stderr)
            self.assertEqual("Expected candidate requires an exact clean checkout.", result.stdout.strip())
            self.assertTrue(list((root / "scripts/ci/__pycache__").glob("*.pyc")))
            self.assertIn("scripts/ci/__pycache__/", subprocess.check_output(
                ["git", "-C", str(root), "status", "--porcelain"], text=True))

    def test_both_workflow_capture_flags_preserve_clean_checkout_in_actual_process(self):
        repository = Path(__file__).resolve().parents[2]
        for workflow in ("ci.yml", "main-validation.yml"):
            with self.subTest(workflow=workflow), tempfile.TemporaryDirectory() as directory:
                lines = (repository / ".github/workflows" / workflow).read_text(encoding="utf-8").splitlines()
                calls = [line.strip().removeprefix("run: ").split() for line in lines
                         if "run: python3" in line and "scripts/ci/sec_arch_evidence.py" in line]
                self.assertEqual(1, len(calls))
                self.assertEqual(["python3", "-B", "scripts/ci/sec_arch_evidence.py"], calls[0][:3])
                root = Path(directory)
                sha = self.prepare(root)
                result = self.run_capture(root, sha, calls[0][1:2])
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertEqual({"candidateSha": sha, "assemblyCount": 6, "outcome": "PASS"}, json.loads(result.stdout))
                self.assertFalse((root / "scripts/ci/__pycache__").exists())
                self.assertEqual("", subprocess.check_output(
                    ["git", "-C", str(root), "status", "--porcelain"], text=True).strip())

    def test_bytecode_disabled_process_still_rejects_candidate_source_and_loaded_assembly_changes(self):
        for mutation in ("wrong_sha", "tracked_source", "untracked_source", "loaded_assembly"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                sha = self.prepare(root)
                if mutation == "wrong_sha": sha = "a" * 40
                elif mutation == "tracked_source":
                    with (root / "scripts/ci/sec_arch_evidence.py").open("a", encoding="utf-8") as source:
                        source.write("\n# Synthetic changed source.\n")
                elif mutation == "untracked_source": (root / "untracked.txt").write_text("synthetic")
                else: assemblies.loaded_assembly_path(root, "Coglatas.Web").write_bytes(b"changed loaded assembly")
                result = self.run_capture(root, sha, ["-B"])
                self.assertEqual(1, result.returncode, result.stderr)
                expected = ("Loaded dependency copy differs from the producer build." if mutation == "loaded_assembly"
                            else "Expected candidate requires an exact clean checkout.")
                self.assertEqual(expected, result.stdout.strip())
                self.assertFalse((root / "scripts/ci/__pycache__").exists())


class ExecutionEvidenceTests(unittest.TestCase):
    def test_current_capture_binds_copied_verifier_and_rejects_changed_loaded_dll(self):
        sha = "a" * 40
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name in assemblies.ASSEMBLIES:
                for path in {assemblies.assembly_path(root, name), assemblies.loaded_assembly_path(root, name)}:
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_bytes(("synthetic assembly " + name).encode())
            stamp = root / "artifacts/ci/dotnet-build-sha"
            stamp.parent.mkdir(parents=True)
            stamp.write_text(sha)
            trx = root / "execution.trx"
            trx.write_bytes(ET.tostring(fixture()))
            with patch.object(evidence.subprocess, "check_output", side_effect=[sha, "", sha, ""]):
                report = evidence.capture(root, trx, sha, NOW, {"fixture": "synthetic"})
            self.assertEqual(2, report["schemaVersion"])
            self.assertEqual("2", report["verifierVersion"])
            self.assertEqual(6, len(report["assemblyDigests"]))
            self.assertEqual(assemblies.SIX_ASSEMBLY_SCOPE, report["assemblyBindingScope"])
            for name in ("Coglatas.SecurityArchitecture", "Coglatas.Web"):
                copied = assemblies.loaded_assembly_path(root, name)
                original = copied.read_bytes()
                copied.write_bytes(b"changed copied dependency")
                with self.subTest(name=name), patch.object(evidence.subprocess, "check_output", side_effect=[sha, ""]):
                    with self.assertRaises(ValueError):
                        evidence.capture(root, trx, sha, NOW, {})
                copied.write_bytes(original)

    def test_assembly_reader_rejects_oversized_empty_and_unknown_inputs(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "assembly.dll"
            path.write_bytes(b"")
            with self.assertRaises(ValueError): assemblies.file_digest(path)
            path.write_bytes(b"oversized")
            with patch.object(assemblies, "MAX_ASSEMBLY_BYTES", 3), self.assertRaises(ValueError):
                assemblies.file_digest(path)
            with self.assertRaises(ValueError): assemblies.assembly_path(Path(directory), "../../outside")

    def test_capture_command_preserves_existing_receipt_bytes(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "receipt.json"
            original = b'{"outcome":"FAIL","historical":true}\n'
            output.write_bytes(original)
            arguments = ["capture", "--candidate-sha", "a" * 40, "--trx", "synthetic.trx", "--output", str(output)]
            with patch.object(sys, "argv", arguments), patch.object(evidence, "capture", return_value={"outcome": "PASS"}), \
                    patch.object(evidence.subprocess, "check_output", return_value="synthetic-sdk"):
                with self.assertRaises(FileExistsError):
                    evidence.main()
            self.assertEqual(original, output.read_bytes())

    def test_positive_complete_observation_is_sanitized_and_exact(self):
        result = observe(fixture())
        self.assertEqual("PASS", result["outcome"])
        self.assertEqual(sum(evidence.EXPECTED.values()), result["observedCaseCount"])
        self.assertEqual(254, result["observedCaseCount"])
        self.assertEqual([], result["missingMethods"])
        for row in result["cases"]:
            expected = {"method", "caseDigest", "outcome"} | ({"verifierCaseId"} if row["method"] in evidence.THEORY_CASES else set())
            self.assertEqual(expected, set(row))
            if row["method"] in evidence.THEORY_CASES:
                self.assertIn(row["verifierCaseId"], evidence.THEORY_CASES[row["method"]])

    def test_reused_http_catalogue_matches_explicit_runtime_selection(self):
        root = Path(__file__).resolve().parents[2]
        launcher = (root / "scripts/security/run-sec-arch-runtime.mjs").read_text(encoding="utf-8")
        selected = launcher.split("const reusedHttpMethods = [", 1)[1].split("];", 1)[0]
        self.assertEqual(list(evidence.REUSED_HTTP_METHODS), re.findall(r"'([A-Za-z]+)'", selected))
        for method in evidence.REUSED_HTTP_METHODS:
            self.assertEqual(1, evidence.EXPECTED["Coglatas.Tests.Tenancy.HttpTenantIsolationTests." + method])
        theories = launcher.split("const reusedHttpTheoryMethods = [", 1)[1].split("];", 1)[0]
        self.assertEqual([method.rsplit(".", 1)[1] for method in evidence.THEORY_CASES], re.findall(r"'([A-Za-z]+)'", theories))
        self.assertTrue(all(evidence.EXPECTED[method] == 3 for method in evidence.THEORY_CASES))

    def test_missing_or_renamed_reused_http_assertions_remain_unverified(self):
        root = fixture()
        results = root.find(Q + "Results")
        for row in list(results):
            if row.attrib["testName"].startswith("Coglatas.Tests.Tenancy."):
                results.remove(row)
        recalculate(root)
        result = observe(root)
        self.assertEqual("UNVERIFIED", result["outcome"])
        self.assertEqual(16, len(result["missingMethods"]))
        root = fixture()
        definition = next(item for item in root.find(Q + "TestDefinitions")
                          if item.attrib["name"].startswith("Coglatas.Tests.Tenancy."))
        method = definition.find(Q + "TestMethod")
        method.attrib["name"] = "RenamedHttpControl"
        name = method.attrib["className"] + "." + method.attrib["name"]
        definition.attrib["name"] = name
        next(row for row in root.find(Q + "Results") if row.attrib["testId"] == definition.attrib["id"]).attrib["testName"] = name
        result = observe(root)
        self.assertEqual("UNVERIFIED", result["outcome"])
        self.assertEqual(1, len(result["missingMethods"]))

    def test_finite_http_theories_require_exact_parameter_names_and_case_cardinality(self):
        root = fixture()
        method = next(iter(evidence.THEORY_CASES))
        result = observe(root)
        self.assertEqual(set(evidence.THEORY_CASES[method]), {row['verifierCaseId'] for row in result['cases'] if row['method'] == method})
        definition = next(row for row in root.find(Q + 'TestDefinitions') if row.find(Q + 'TestMethod').attrib['name'] == method.rsplit('.', 1)[1])
        execution = next(row for row in root.find(Q + 'Results') if row.attrib['testId'] == definition.attrib['id'])
        definition.attrib['name'] = method + '(query: "?pageSize=500")'
        execution.attrib['testName'] = definition.attrib['name']
        with self.assertRaises(ValueError):
            observe(root)
        root = fixture()
        selected = next(row for row in root.find(Q + 'Results') if row.attrib['testName'] in evidence.THEORY_CASES[method].values())
        root.find(Q + 'Results').remove(selected)
        recalculate(root)
        result = observe(root)
        self.assertEqual('UNVERIFIED', result['outcome'])
        self.assertIn(method, result['missingMethods'])

    def test_disabled_missing_verifier_is_unverified(self):
        root = fixture()
        root.find(Q + "Results").remove(root.find(Q + "Results")[-1])
        recalculate(root)
        result = observe(root)
        self.assertEqual("UNVERIFIED", result["outcome"])
        self.assertEqual(1, len(result["missingMethods"]))

    def test_missing_same_tenant_and_current_resource_transport_controls_is_unverified(self):
        root = fixture()
        results = root.find(Q + "Results")
        for row in list(results):
            if any(method in row.attrib["testName"] for method in (
                "SameTenantHiddenResourcesRejectSubscriptionAndDeliveryWithLivePeers",
                "CurrentResourceReadChangesPreventEveryApplicableCatalogueDeliveryAndRestore",
            )):
                results.remove(row)
        recalculate(root)
        result = observe(root)
        self.assertEqual("UNVERIFIED", result["outcome"])
        self.assertEqual(2, len(result["missingMethods"]))

    def test_missing_current_http_authority_controls_are_unverified(self):
        root = fixture()
        results = root.find(Q + "Results")
        for row in list(results):
            if ".SecurityArchitectureApiCurrentAuthorityTests." in row.attrib["testName"]:
                results.remove(row)
        recalculate(root)
        result = observe(root)
        self.assertEqual("UNVERIFIED", result["outcome"])
        self.assertEqual(2, len(result["missingMethods"]))

    def test_missing_parent_policy_execution_cannot_use_other_passes_as_coverage(self):
        root = fixture()
        results = root.find(Q + "Results")
        for row in list(results):
            if ".SecurityArchitectureParentRlsTests." in row.attrib["testName"]:
                results.remove(row)
        recalculate(root)
        result = observe(root)
        self.assertEqual("UNVERIFIED", result["outcome"])
        self.assertEqual(2, len(result["missingMethods"]))

    def test_missing_prepared_policy_catalogue_is_unverified(self):
        root = fixture()
        results = root.find(Q + "Results")
        for row in list(results):
            if ".SecurityArchitectureRlsCatalogTests." in row.attrib["testName"]:
                results.remove(row)
        recalculate(root)
        result = observe(root)
        self.assertEqual("UNVERIFIED", result["outcome"])
        self.assertEqual(1, len(result["missingMethods"]))

    def test_missing_live_origin_controls_are_unverified(self):
        root = fixture()
        results = root.find(Q + "Results")
        for row in list(results):
            if any(method in row.attrib["testName"] for method in (
                "ProductTransportRejectsUnapprovedOriginsWithAuthenticatedLiveControls",
                "ProductTransportApprovedOriginRetainsSessionAndResourceAuthorization",
            )):
                results.remove(row)
        recalculate(root)
        result = observe(root)
        self.assertEqual("UNVERIFIED", result["outcome"])
        self.assertEqual(2, len(result["missingMethods"]))

    def test_missing_phase2_http_and_event_controls_are_unverified(self):
        root = fixture()
        results = root.find(Q + "Results")
        new_methods = (
            "EveryComposedProtectedHttpEndpointRejectsAnonymousRequestsAfterValidCsrf",
            "ProductTransportReconnectUsesCurrentHttpCatchUpAuthority",
            "ProductTransportTenantCookieSwitchCannotRetargetExistingOrNewSubscriptions",
            "EveryDeclaredEventHasLiveTenantAndCurrentMembershipControls",
            "ProjectAndWorkspaceUnsubscriptionOnlyRemovesCallingConnection",
        )
        for row in list(results):
            if any(method in row.attrib["testName"] for method in new_methods):
                results.remove(row)
        recalculate(root)
        result = observe(root)
        self.assertEqual("UNVERIFIED", result["outcome"])
        self.assertEqual(5, len(result["missingMethods"]))

    def test_failed_and_skipped_executions_never_pass(self):
        for actual, expected in (("Failed", "FAIL"), ("NotExecuted", "UNVERIFIED"), ("Aborted", "ERROR")):
            with self.subTest(actual=actual):
                root = fixture()
                root.find(Q + "Results")[0].attrib["outcome"] = actual
                recalculate(root)
                self.assertEqual(expected, observe(root)["outcome"])

    def test_omitted_manual_replay_provider_controls_are_unverified(self):
        root = fixture()
        results = root.find(Q + "Results")
        for row in list(results):
            if row.attrib["testName"].startswith(evidence.REPLAY_PREFIX):
                results.remove(row)
        recalculate(root)
        result = observe(root)
        self.assertEqual("UNVERIFIED", result["outcome"])
        self.assertEqual(6, len(result["missingMethods"]))

    def test_unclassified_manual_replay_method_cannot_replace_required_execution(self):
        root = fixture()
        definition = next(item for item in root.find(Q + "TestDefinitions")
                          if item.attrib["name"].startswith(evidence.REPLAY_PREFIX))
        definition.find(Q + "TestMethod").attrib["name"] = "UnclassifiedReplayControl"
        with self.assertRaises(ValueError):
            observe(root)

    def test_injected_duplicate_result_is_rejected(self):
        root = fixture()
        root.find(Q + "Results").append(copy.deepcopy(root.find(Q + "Results")[0]))
        recalculate(root)
        with self.assertRaises(ValueError):
            observe(root)

    def test_falsified_pass_counter_is_rejected(self):
        root = fixture()
        root.find(Q + "Results")[0].attrib["outcome"] = "Failed"
        with self.assertRaises(ValueError):
            observe(root)

    def test_identity_alias_or_unclassified_method_is_rejected(self):
        for field, value in (("executionId", "another-run"), ("testName", "secret-input")):
            root = fixture()
            root.find(Q + "Results")[0].attrib[field] = value
            with self.assertRaises(ValueError):
                observe(root)
        root = fixture()
        root.find(Q + "TestDefinitions")[0].find(Q + "TestMethod").attrib["name"] = "DisabledVerifier"
        with self.assertRaises(ValueError):
            observe(root)

    def test_stale_future_and_naive_timestamp_are_rejected(self):
        for start, finish in (("2026-10-01T12:00:00Z", "2026-10-01T12:01:00Z"),
                              ("2026-10-10T12:00:00Z", "2026-10-10T12:01:00Z"),
                              ("2026-10-09T12:00:00", "2026-10-09T12:01:00Z")):
            root = fixture()
            root.find(Q + "Times").attrib.update(start=start, finish=finish)
            with self.assertRaises(ValueError):
                observe(root)

    def test_case_outside_recorded_run_is_rejected(self):
        root = fixture()
        root.find(Q + "Results")[0].attrib["endTime"] = "2026-10-09T12:02:00Z"
        with self.assertRaises(ValueError):
            observe(root)

    def test_xml_entities_manual_json_and_unknown_namespace_are_rejected(self):
        for data in (b'<!DOCTYPE TestRun [<!ENTITY x "private">]><TestRun/>',
                     b'{"outcome":"PASS"}', b'<TestRun/>'):
            with self.assertRaises((ValueError, ET.ParseError)):
                evidence.observed_trx(data, NOW)

    def test_wrong_sha_environment_run_attempt_or_missing_build_binding_is_rejected(self):
        receipt = {"candidateSha": "a" * 40, "environmentFingerprint": "b" * 64,
                   "runId": "123", "runAttempt": "1", "buildStampMatchesCandidate": True}
        evidence.reconcile_identity(receipt, "a" * 40, "b" * 64, "123", "1")
        for field, value in (("candidateSha", "c" * 40), ("environmentFingerprint", "d" * 64),
                             ("runId", "124"), ("runAttempt", "2"), ("buildStampMatchesCandidate", False),
                             ("buildStampMatchesCandidate", "true")):
            invalid = dict(receipt, **{field: value})
            with self.assertRaises(ValueError):
                evidence.reconcile_identity(invalid, "a" * 40, "b" * 64, "123", "1")


def load_tests(loader, standard_tests, pattern):
    # Keep this deterministic advisory suite in the existing specification checks.
    standard_tests.addTests(loader.loadTestsFromName("test_sec_arch_http_accounting"))
    standard_tests.addTests(loader.loadTestsFromName("test_sec_arch_signalr_accounting"))
    standard_tests.addTests(loader.loadTestsFromName("test_sec_arch_manual_replay"))
    return standard_tests


if __name__ == "__main__":
    unittest.main()
