"""Positive/deliberate-invalid Advisory summary controls; no canonical approval or product runtime claim."""

from copy import deepcopy
from datetime import datetime, timezone
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import specification_contract as adapter
from sec_arch_assembly_binding import (LEGACY_ASSEMBLIES, ASSEMBLIES, assembly_path,
                                      loaded_assembly_path, SIX_ASSEMBLY_SCOPE)

SHA = "a" * 40
NOW = datetime(2026, 10, 10, tzinfo=timezone.utc)


def trx(outcome="Passed"):
    passed, failed = int(outcome == "Passed"), int(outcome == "Failed")
    return f'''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
<Times start="2026-10-09T23:59:00Z" finish="2026-10-10T00:00:00Z" />
<TestDefinitions><UnitTest id="one" name="Synthetic.Tests.ScopeCase">
<TestMethod className="Synthetic.Tests" name="ScopeCase"/><Execution id="run-one"/>
</UnitTest></TestDefinitions><Results><UnitTestResult testId="one" testName="Synthetic.Tests.ScopeCase"
 executionId="run-one" outcome="{outcome}" startTime="2026-10-09T23:59:00Z" endTime="2026-10-10T00:00:00Z"/></Results>
<ResultSummary><Counters total="1" passed="{passed}" failed="{failed}" executed="{passed + failed}"/></ResultSummary></TestRun>'''.encode()


def tool_result():
    return {"valid": True, "normativeReady": False, "approvalStatus": "UNVERIFIED", "executionAttestationStatus": "UNVERIFIED",
            "diagnostics": [], "coverage": {"candidateSha": SHA, "schemaVersion": 1, "registryVersion": 1,
                "activeFamilies": [{"category": "AUTH", "count": 1}], "severities": [{"category": "Blocking", "count": 1}],
                "verificationClasses": [{"category": "ContractTest", "count": 1}], "activeRequirements": 1,
                "deprecatedRequirements": 0, "retiredRequirements": 0, "mappings": 1, "manualMappings": 0,
                "executedPassingLinks": 1, "unresolvedLinks": 0, "requirementsWithKnownLimitations": 1}}


class SpecificationContractTests(unittest.TestCase):
    def test_unavailable_inputs_preserve_unknown_coverage_and_synthetic_separation(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(adapter.subprocess, "check_output", side_effect=[SHA, "", SHA, ""]):
            report = adapter.capture(Path(directory), SHA, NOW)
        self.assertEqual("UNAVAILABLE", report["canonicalInputs"]["status"])
        self.assertIsNone(report["canonicalInputs"]["declaredDraftCoverage"])
        self.assertIsNone(report["qualifiedNormativeRequirementCount"])
        self.assertFalse(report["normativeReady"])
        self.assertEqual("UNVERIFIED", report["ownerApproval"])
        self.assertNotIn("NOT_APPLICABLE", json.dumps(report))
        self.assertIn("SYNTHETIC_TOOLING_CONTROLS_ARE_NOT_PRODUCT_ACCEPTANCE", report["blindSpots"])

    def test_existing_trx_is_reused_without_becoming_canonical_relationships(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(adapter.subprocess, "check_output", side_effect=[SHA, "", SHA, ""]):
            root = Path(directory)
            stamp = root / "artifacts/ci/dotnet-build-sha"
            stamp.parent.mkdir(parents=True)
            stamp.write_text(SHA)
            path = root / "observed.trx"
            path.write_bytes(trx())
            report = adapter.capture(root, SHA, NOW, path, path)
        self.assertEqual("OBSERVED_PASS", report["existingVerifierLanes"]["backend"]["status"])
        self.assertEqual("BUILD_STAMP_BOUND", report["existingVerifierLanes"]["backend"]["candidateBinding"])
        self.assertEqual("UNRESOLVED", report["existingVerifierLanes"]["backend"]["canonicalSpecMappings"])
        self.assertFalse(report["normativeReady"])

    def test_skipped_and_failed_lane_outcomes_are_preserved(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "synthetic.trx"
            for observed, expected in (("NotExecuted", "UNVERIFIED"), ("Failed", "FAIL")):
                with self.subTest(observed=observed):
                    path.write_bytes(trx(observed))
                    self.assertEqual(expected, adapter.execution_lane(path, NOW, True)["status"])

    def test_private_tool_projection_publishes_only_counts_and_opaque_references(self):
        result = tool_result()
        result["valid"] = False
        result["diagnostics"] = [{"ruleId": "SPEC_MISSING_EXECUTION", "subject": "private-spec-do-not-publish",
                                  "reason": "private-normative-statement-do-not-publish"}]
        projected = adapter.project_tool_result(json.dumps(result).encode(), SHA)
        serialized = json.dumps(projected)
        self.assertNotIn("private-spec-do-not-publish", serialized)
        self.assertNotIn("private-normative-statement-do-not-publish", serialized)
        self.assertEqual(1, projected["diagnosticRuleCounts"]["SPEC_MISSING_EXECUTION"])
        self.assertEqual(1, projected["declaredDraftCoverage"]["activeFamilies"]["AUTH"])
        self.assertEqual("INVALID_OR_INCOMPLETE", projected["status"])

    def test_fake_approval_wrong_candidate_unknown_schema_and_false_manual_pass_fail(self):
        for mutation in ("approval", "normative", "attestation", "candidate", "schema", "counter", "duplicate-class", "manual-pass", "validity"):
            with self.subTest(mutation=mutation):
                result = deepcopy(tool_result())
                if mutation == "approval": result["approvalStatus"] = "APPROVED"
                if mutation == "normative": result["normativeReady"] = True
                if mutation == "attestation": result["executionAttestationStatus"] = "PASS"
                if mutation == "candidate": result["coverage"]["candidateSha"] = "b" * 40
                if mutation == "schema": result["coverage"]["schemaVersion"] = 2
                if mutation == "counter": result["coverage"]["activeRequirements"] = 2
                if mutation == "duplicate-class": result["coverage"]["verificationClasses"] *= 2
                if mutation == "manual-pass":
                    result["coverage"]["verificationClasses"] = [{"category": "Manual", "count": 1}]
                    result["coverage"]["manualMappings"] = 1
                if mutation == "validity": result["valid"] = False
                with self.assertRaises(ValueError): adapter.project_tool_result(json.dumps(result).encode(), SHA)

    def test_corrupted_stale_renamed_deleted_and_duplicate_verifier_results_fail(self):
        for mutation in ("counter", "stale", "renamed", "deleted", "duplicate", "entity"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as directory:
                data = trx()
                if mutation == "counter": data = data.replace(b'total="1"', b'total="2"')
                if mutation == "stale": data = data.replace(b"2026-10-09", b"2026-10-01")
                if mutation == "renamed": data = data.replace(b'testName="Synthetic.Tests.ScopeCase"', b'testName="Renamed"')
                if mutation == "deleted": data = data.replace(b'<Execution id="run-one"/>', b"")
                if mutation == "duplicate": data = data.replace(b"</UnitTest></TestDefinitions>", b'</UnitTest><UnitTest id="one" name="duplicate"/></TestDefinitions>')
                if mutation == "entity": data = b'<!DOCTYPE TestRun [<!ENTITY private "private-do-not-echo">]>' + data
                path = Path(directory) / "mutated.trx"
                path.write_bytes(data)
                with self.assertRaises(ValueError): adapter.execution_lane(path, NOW, True)

    def test_candidate_checkout_and_build_mutations_fail(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for response in (("b" * 40, ""), (SHA, " M private-source")):
                with self.subTest(response=response), patch.object(adapter.subprocess, "check_output", side_effect=response):
                    with self.assertRaises(ValueError): adapter.capture(root, SHA, NOW)
            stamp = root / "artifacts/ci/dotnet-build-sha"
            stamp.parent.mkdir(parents=True)
            stamp.write_text("b" * 40)
            with patch.object(adapter.subprocess, "check_output", side_effect=[SHA, ""]):
                with self.assertRaises(ValueError): adapter.capture(root, SHA, NOW)

    def test_canonical_mode_invokes_real_cli_contract_with_explicit_source_and_digest_inputs(self):
        with tempfile.TemporaryDirectory() as directory:
            inputs = {key: Path(directory) / key for key in ("registry", "manifest", "contracts", "tool_assembly")}
            for key, path in inputs.items(): path.write_bytes(("synthetic-" + key).encode())
            inputs.update(spec_root=Path(directory), implementation_root=str(Path(directory)), spec_source_sha="c" * 40)
            calls = []
            def fake_process(command):
                calls.append(command)
                return 0, json.dumps(tool_result()).encode()
            report = adapter.canonical_inputs(inputs, SHA, NOW, fake_process)
            self.assertEqual("traceability-check", calls[0][2])
            self.assertIn("c" * 40, calls[0])
            self.assertEqual("STRUCTURALLY_VALID_DRAFT", report["status"])
            self.assertEqual(4, len(report["inputDigests"]))
            with self.assertRaises(ValueError):
                adapter.canonical_inputs(inputs, SHA, NOW, fake_process, expected_tool_digest="f" * 64)
            with self.assertRaises(ValueError):
                adapter.canonical_inputs(inputs, SHA, NOW, lambda _: (1, json.dumps(tool_result()).encode()))
            with self.assertRaises(ValueError): adapter.canonical_inputs({"registry": inputs["registry"]}, SHA, NOW)
            # This process stub verifies adapter wiring only. Real CLI controls belong to the existing .NET lane.

    def test_changed_canonical_input_or_verifier_cannot_keep_old_fingerprints(self):
        with tempfile.TemporaryDirectory() as directory:
            inputs = {key: Path(directory) / key for key in ("registry", "manifest", "contracts", "tool_assembly", "execution_links")}
            inputs.update(spec_root=Path(directory), implementation_root=directory, spec_source_sha="c" * 40)
            for changed in ("registry", "manifest", "contracts", "tool_assembly", "execution_links"):
                for key in ("registry", "manifest", "contracts", "tool_assembly", "execution_links"):
                    inputs[key].write_bytes(("synthetic-" + key).encode())
                self.assertEqual("STRUCTURALLY_VALID_DRAFT", adapter.canonical_inputs(
                    inputs, SHA, NOW, lambda _: (0, json.dumps(tool_result()).encode()))["status"])
                def mutate(_command):
                    inputs[changed].write_bytes(b"different synthetic bytes")
                    return 0, json.dumps(tool_result()).encode()
                with self.subTest(changed=changed), self.assertRaisesRegex(ValueError, "Canonical input changed"):
                    adapter.canonical_inputs(inputs, SHA, NOW, mutate)

    def test_transition_mode_requires_and_fingerprints_the_explicit_retained_baseline(self):
        with tempfile.TemporaryDirectory() as directory:
            inputs = {key: Path(directory) / key for key in ("registry", "manifest", "contracts", "tool_assembly", "baseline_registry")}
            for key, path in inputs.items(): path.write_bytes(("synthetic-" + key).encode())
            inputs.update(spec_root=Path(directory), implementation_root=directory, spec_source_sha="c" * 40)
            calls = []
            def process(command):
                calls.append(command)
                return 0, json.dumps(tool_result()).encode()
            report = adapter.canonical_inputs(inputs, SHA, NOW, process)
            self.assertEqual("traceability-transition-check", calls[0][2])
            self.assertEqual(str(inputs["baseline_registry"]), calls[0][6])
            self.assertEqual(adapter.digest(b"synthetic-baseline_registry"), report["inputDigests"]["baseline_registry"])
            def mutate(command):
                inputs["baseline_registry"].write_bytes(b"changed retained baseline")
                return process(command)
            with self.assertRaisesRegex(ValueError, "Canonical input changed"):
                adapter.canonical_inputs(inputs, SHA, NOW, mutate)
            inputs["baseline_registry"].unlink()
            with self.assertRaises(OSError): adapter.canonical_inputs(inputs, SHA, NOW, process)

    def test_nonfinite_and_excessively_nested_json_cannot_enter_the_adapter(self):
        for data in (b'{"value":NaN}', b'{"value":Infinity}', b'{"value":-Infinity}',
                     b'{"value":' + b'[' * 129 + b'0' + b']' * 129 + b'}', b'[' * 2048 + b'0' + b']' * 2048):
            with self.subTest(data=data[:30]), self.assertRaises(ValueError):
                adapter.read_json(data)

    def test_wrong_workflow_attempt_and_forged_representative_counts_fail(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            stamp = root / "artifacts/ci/dotnet-build-sha"
            stamp.parent.mkdir(parents=True)
            stamp.write_text(SHA)
            path = root / "lane.trx"
            path.write_bytes(trx())
            assemblies = {}
            for name in LEGACY_ASSEMBLIES:
                compiled = assembly_path(root, name)
                compiled.parent.mkdir(parents=True, exist_ok=True)
                compiled.write_bytes(name.encode())
                assemblies[name] = adapter.digest(name.encode())
            receipt = {"schemaVersion": 1, "verifierId": "SEC-ARCH-EXECUTION-COVERAGE", "verifierVersion": "1", "candidateSha": SHA,
                       "assemblyDigests": assemblies,
                       "runId": "LOCAL", "runAttempt": "1", "buildStampMatchesCandidate": True,
                       "executionDigest": adapter.digest(trx()), "observedExecution": adapter.observed_trx(trx(), NOW)}
            receipt_path = root / "receipt.json"
            receipt_path.write_text(json.dumps(receipt))
            with patch.object(adapter.subprocess, "check_output", side_effect=[SHA, "", SHA, ""]):
                positive = adapter.capture(root, SHA, NOW, backend_trx=path, sec_arch_execution=receipt_path)
            self.assertEqual("REPRESENTATIVE_UNVERIFIED", positive["secArchRepresentativeEvidence"]["status"])
            self.assertEqual(0, positive["secArchRepresentativeEvidence"]["observedKinds"]["tooling"])
            for mutation in ("attempt", "candidate", "digest", "count", "outcome"):
                with self.subTest(mutation=mutation):
                    changed = deepcopy(receipt)
                    if mutation == "attempt": changed["runAttempt"] = "2"
                    if mutation == "candidate": changed["candidateSha"] = "b" * 40
                    if mutation == "digest": changed["executionDigest"] = "b" * 64
                    if mutation == "count": changed["observedExecution"]["observedCaseCount"] = 99999
                    if mutation == "outcome": changed["observedExecution"]["outcome"] = "PASS"
                    receipt_path.write_text(json.dumps(changed))
                    with patch.object(adapter.subprocess, "check_output", side_effect=[SHA, ""]):
                        with self.assertRaises(ValueError): adapter.capture(root, SHA, NOW, backend_trx=path, sec_arch_execution=receipt_path)

    def test_duplicate_json_unsupported_shape_and_unbounded_inputs_fail(self):
        with self.assertRaises(ValueError): adapter.read_json(b'{"schemaVersion":1,"schemaVersion":1}')
        with self.assertRaises(ValueError): adapter.read_json(b'[]')
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "oversize"
            with path.open("wb") as stream: stream.truncate(adapter.MAXIMUM_BYTES + 1)
            with self.assertRaises(ValueError): adapter.bounded_bytes(path)

    def test_advisory_summary_reuses_v2_execution_and_rejects_changed_copied_verifier(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            stamp = root / "artifacts/ci/dotnet-build-sha"
            stamp.parent.mkdir(parents=True)
            stamp.write_text(SHA)
            path = root / "backend.trx"
            path.write_bytes(trx())
            hashes = {}
            for name in ASSEMBLIES:
                for compiled in {assembly_path(root, name), loaded_assembly_path(root, name)}:
                    compiled.parent.mkdir(parents=True, exist_ok=True)
                    compiled.write_bytes(name.encode())
                hashes[name] = adapter.digest(name.encode())
            receipt = {"schemaVersion": 2, "verifierId": "SEC-ARCH-EXECUTION-COVERAGE", "verifierVersion": "2",
                       "assemblyBindingScope": SIX_ASSEMBLY_SCOPE, "candidateSha": SHA,
                       "runId": "LOCAL", "runAttempt": "1", "buildStampMatchesCandidate": True,
                       "executionDigest": adapter.digest(trx()), "observedExecution": adapter.observed_trx(trx(), NOW),
                       "assemblyDigests": hashes}
            receipt_path = root / "receipt.json"
            receipt_path.write_text(json.dumps(receipt))
            with patch.object(adapter.subprocess, "check_output", side_effect=[SHA, "", SHA, ""]):
                report = adapter.capture(root, SHA, NOW, backend_trx=path, sec_arch_execution=receipt_path)
            self.assertEqual("REPRESENTATIVE_UNVERIFIED", report["secArchRepresentativeEvidence"]["status"])
            self.assertFalse(report["normativeReady"])
            loaded_assembly_path(root, "Coglatas.SecurityArchitecture").write_bytes(b"changed copied verifier")
            with patch.object(adapter.subprocess, "check_output", side_effect=[SHA, ""]), self.assertRaises(ValueError):
                adapter.capture(root, SHA, NOW, backend_trx=path, sec_arch_execution=receipt_path)

    def test_cli_writes_advisory_artifacts_and_preserves_retained_output(self):
        with tempfile.TemporaryDirectory() as directory:
            output, summary = Path(directory) / "new.json", Path(directory) / "new.md"
            arguments = ["specification_contract", "--candidate-sha", SHA, "--output", str(output), "--markdown", str(summary)]
            capture = adapter.capture
            # Keep this CLI fixture independent of retained local/CI build stamps.
            with (
                patch.object(adapter, "capture", side_effect=lambda _root, *args, **kwargs: capture(Path(directory), *args, **kwargs)),
                patch("sys.argv", arguments),
                patch.object(adapter.subprocess, "check_output", side_effect=[SHA, "", SHA, ""]),
                patch("sys.stdout", new_callable=io.StringIO),
            ):
                self.assertEqual(0, adapter.main())
            original = output.read_bytes()
            self.assertEqual("ADVISORY", json.loads(original)["rollout"])
            rendered = summary.read_text()
            self.assertIn("UNVERIFIED", rendered)
            self.assertIn("AUTHORIZED_PRIVATE_REGISTRY_MANIFEST_CONTRACT_SOURCE_AND_REVIEW", rendered)
            self.assertIn("Specification source revision: `UNKNOWN`", rendered)
            with patch("sys.argv", arguments), patch("sys.stdout", new_callable=io.StringIO):
                self.assertEqual(1, adapter.main())
            self.assertEqual(original, output.read_bytes())

    def test_cli_integrity_error_receipt_does_not_echo_input_paths_or_private_values(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "error.json"
            arguments = ["specification_contract", "--candidate-sha", "private-value-do-not-echo", "--output", str(output)]
            with patch("sys.argv", arguments), patch("sys.stdout", new_callable=io.StringIO) as printed:
                self.assertEqual(1, adapter.main())
            serialized = output.read_text()
            self.assertEqual("ERROR", json.loads(serialized)["status"])
            self.assertNotIn("private-value-do-not-echo", serialized + printed.getvalue())
            self.assertNotIn(directory, serialized + printed.getvalue())


if __name__ == "__main__":
    unittest.main()
