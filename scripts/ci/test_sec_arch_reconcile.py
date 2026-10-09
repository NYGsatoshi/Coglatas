"""Actual archive and CLI controls for independent SEC-ARCH producer binding."""

import copy
import hashlib
import io
import json
from pathlib import Path
import subprocess
import sys
import tarfile
import tempfile
import unittest
from unittest.mock import patch
import warnings
import zipfile

import sec_arch_reconcile as binding
from sec_arch_assembly_binding import ASSEMBLIES, SIX_ASSEMBLY_SCOPE, assembly_member, loaded_assembly_member

SHA = "1" * 40
RUN = "123"
ATTEMPT = "1"


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


class ProducerBindingTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.assemblies = {name: ("synthetic compiled " + name).encode() for name in binding.ASSEMBLIES}
        self.environment = {"fixture": "SEC02_SYNTHETIC", "system": "Linux"}
        self.receipt = {"schemaVersion": 1, "verifierId": "SEC-ARCH-EXECUTION-COVERAGE", "verifierVersion": "1",
                        "candidateSha": SHA, "runId": RUN, "runAttempt": ATTEMPT,
                        "buildStampMatchesCandidate": True, "environment": self.environment,
                        "environmentFingerprint": digest(json.dumps(self.environment, sort_keys=True).encode()),
                        "assemblyDigests": {name: digest(data) for name, data in self.assemblies.items()},
                        "observedExecution": {"outcome": "PASS"}}

    def archives(self, receipt=None, source=SHA, stamp=SHA, members=None, extra_zip=None, raw_receipt=None):
        rows = [("artifacts/ci/dotnet-build-sha", (stamp + "\n").encode())]
        rows += [(assembly_member(name), data) for name, data in self.assemblies.items()]
        if self.receipt["schemaVersion"] == 2:
            rows += [(loaded_assembly_member(name), data) for name, data in self.assemblies.items()
                     if assembly_member(name) != loaded_assembly_member(name)]
        if members is not None:
            rows = members(rows)
        tar = io.BytesIO()
        with tarfile.open(fileobj=tar, mode="w") as archive:
            for name, data in rows:
                member = tarfile.TarInfo(name)
                if isinstance(data, bytes):
                    member.size = len(data)
                    archive.addfile(member, io.BytesIO(data))
                else:
                    member.type, member.linkname = tarfile.SYMTYPE, data
                    archive.addfile(member)
        producer, execution = self.root / "producer.zip", self.root / "execution.zip"
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", UserWarning)
            with zipfile.ZipFile(producer, "w") as archive:
                archive.writestr("source-sha", source + "\n")
                archive.writestr("dotnet-release-build.tar", tar.getvalue())
                if extra_zip is not None:
                    archive.writestr(*extra_zip)
        with zipfile.ZipFile(execution, "w") as archive:
            archive.writestr("execution.json", raw_receipt if raw_receipt is not None else
                             json.dumps(self.receipt if receipt is None else receipt))
        return producer, execution, digest(producer.read_bytes()), digest(execution.read_bytes())

    def reconcile(self, values, candidate=SHA, run=RUN, attempt=ATTEMPT):
        producer, execution, producer_digest, execution_digest = values
        return binding.reconcile(producer, execution, candidate, run, attempt, producer_digest, execution_digest)

    def test_original_artifact_bytes_bind_without_creating_acceptance(self):
        values = self.archives()
        original = values[1].read_bytes()
        result = self.reconcile(values)
        self.assertEqual("PRODUCER_BYTES_RECONCILED", result["qualification"])
        self.assertEqual("UNVERIFIED", result["trustedAttestation"])
        self.assertEqual("PRE-AVALONIA SEC-ARCH: BLOCKED", result["preAvaloniaVerdict"])
        self.assertIsNone(result["ownerApproval"])
        self.assertEqual(original, values[1].read_bytes())
        self.assertEqual(5, result["boundAssemblyCount"])
        self.assertEqual("UNVERIFIED", result["fullDependencyQualification"])

    def use_six_assembly_receipt(self):
        self.assemblies = {name: ("synthetic compiled " + name).encode() for name in ASSEMBLIES}
        self.receipt.update(schemaVersion=2, verifierVersion="2", assemblyBindingScope=SIX_ASSEMBLY_SCOPE,
                            assemblyDigests={name: digest(data) for name, data in self.assemblies.items()})

    def test_current_six_assembly_receipt_binds_actual_copied_verifier_and_dependencies(self):
        self.use_six_assembly_receipt()
        values = self.archives()
        original = values[1].read_bytes()
        result = self.reconcile(values)
        self.assertEqual(6, result["boundAssemblyCount"])
        self.assertEqual(SIX_ASSEMBLY_SCOPE, result["assemblyBindingScope"])
        self.assertEqual("SIX_ASSEMBLY_BYTES_RECONCILED", result["fullDependencyQualification"])
        self.assertEqual(self.receipt["assemblyDigests"], result["assemblyDigests"])
        self.assertEqual(original, values[1].read_bytes())
        self.assertIsNone(result["ownerApproval"])

    def test_changed_or_missing_verifier_dll_cannot_reuse_five_assembly_green(self):
        self.use_six_assembly_receipt()
        member = assembly_member("Coglatas.SecurityArchitecture")
        for mutation in ("missing", "changed"):
            def change(rows):
                return [(name, b"changed verifier" if name == member else data) for name, data in rows
                        if mutation != "missing" or name != member]
            with self.subTest(mutation=mutation), self.assertRaises(binding.ReconciliationError):
                self.reconcile(self.archives(members=change))

    def test_copied_product_dll_drift_or_missing_copy_cannot_qualify_loaded_build(self):
        self.use_six_assembly_receipt()
        for assembly in ("Coglatas.SecurityArchitecture", "Coglatas.Web", "Coglatas.Application", "Coglatas.Infrastructure", "Coglatas.Domain"):
            member = loaded_assembly_member(assembly)
            for mutation in ("missing", "changed"):
                def change(rows):
                    return [(name, b"different loaded build" if name == member else data) for name, data in rows
                            if mutation != "missing" or name != member]
                with self.subTest(assembly=assembly, mutation=mutation), self.assertRaises(binding.ReconciliationError):
                    self.reconcile(self.archives(members=change))

    def test_legacy_receipt_cannot_self_upgrade_or_claim_full_scope(self):
        for mutation in ("scope", "version", "six-digests"):
            receipt = copy.deepcopy(self.receipt)
            if mutation == "scope": receipt["assemblyBindingScope"] = SIX_ASSEMBLY_SCOPE
            elif mutation == "version": receipt.update(schemaVersion=2, verifierVersion="2", assemblyBindingScope=SIX_ASSEMBLY_SCOPE)
            else: receipt["assemblyDigests"]["Coglatas.SecurityArchitecture"] = "f" * 64
            with self.subTest(mutation=mutation), self.assertRaises(binding.ReconciliationError):
                self.reconcile(self.archives(receipt=receipt))

    def test_six_assembly_missing_scope_unknown_schema_and_bad_verifier_version_are_rejected(self):
        self.use_six_assembly_receipt()
        for mutation in ("scope", "schema", "verifier"):
            receipt = copy.deepcopy(self.receipt)
            if mutation == "scope": receipt.pop("assemblyBindingScope")
            elif mutation == "schema": receipt["schemaVersion"] = 3
            else: receipt["verifierVersion"] = "1"
            with self.subTest(mutation=mutation), self.assertRaises(binding.ReconciliationError):
                self.reconcile(self.archives(receipt=receipt))

    def test_failed_erroneous_and_missing_execution_are_never_rewritten_as_pass(self):
        for outcome in ("FAIL", "ERROR", "UNVERIFIED"):
            with self.subTest(outcome=outcome):
                receipt = copy.deepcopy(self.receipt)
                receipt["observedExecution"]["outcome"] = outcome
                self.assertEqual(outcome, self.reconcile(self.archives(receipt))["originalExecutionOutcome"])

    def test_self_reported_identity_and_build_mutations_are_rejected(self):
        for field, value in (("candidateSha", "2" * 40), ("runId", "124"), ("runAttempt", "2"),
                             ("buildStampMatchesCandidate", False), ("verifierId", "DISABLED"),
                             ("verifierVersion", "2"), ("schemaVersion", True),
                             ("environmentFingerprint", "0" * 64)):
            with self.subTest(field=field):
                receipt = copy.deepcopy(self.receipt)
                receipt[field] = value
                with self.assertRaises(binding.ReconciliationError):
                    self.reconcile(self.archives(receipt))

    def test_historical_or_pr_head_cannot_qualify_another_candidate_or_attempt(self):
        values = self.archives()
        for context in (("2" * 40, RUN, ATTEMPT), (SHA, "124", ATTEMPT), (SHA, RUN, "2")):
            with self.subTest(context=context), self.assertRaises(binding.ReconciliationError):
                self.reconcile(values, *context)

    def test_independent_zip_digest_cannot_be_replaced_by_claimed_binding(self):
        values = self.archives()
        for index in (2, 3):
            invalid = list(values)
            invalid[index] = "0" * 64
            with self.subTest(index=index), self.assertRaises(binding.ReconciliationError):
                self.reconcile(invalid)

    def test_producer_source_stamp_missing_and_changed_assembly_are_rejected(self):
        mutations = ({"source": "2" * 40}, {"stamp": "2" * 40},
                     {"members": lambda rows: rows[:-1]},
                     {"members": lambda rows: rows[:-1] + [(rows[-1][0], b"other compiled bytes")]})
        for mutation in mutations:
            with self.subTest(mutation=list(mutation)), self.assertRaises(binding.ReconciliationError):
                self.reconcile(self.archives(**mutation))

    def test_duplicate_tar_members_links_and_traversal_are_rejected_without_extraction(self):
        outside = self.root.parent / (self.root.name + "-outside")
        for extra in (("../" + outside.name, b"payload"), ("/absolute", b"payload"),
                      ("src\\alias", b"payload"), ("src/link", "outside")):
            with self.subTest(extra=extra[0]), self.assertRaises(binding.ReconciliationError):
                self.reconcile(self.archives(members=lambda rows: rows + [extra]))
        with self.assertRaises(binding.ReconciliationError):
            self.reconcile(self.archives(members=lambda rows: rows + [rows[-1]]))
        self.assertFalse(outside.exists())

    def test_duplicate_zip_and_unsafe_paths_are_rejected(self):
        for extra in (("source-sha", "duplicate"), ("../escape", "payload"), ("./source-sha", "alias")):
            with self.subTest(extra=extra[0]), self.assertRaises(binding.ReconciliationError):
                self.reconcile(self.archives(extra_zip=extra))

    def test_duplicate_json_keys_and_non_finite_claims_are_rejected(self):
        for data in ('{"candidateSha":"first","candidateSha":"second"}', '{"value":NaN}'):
            with self.subTest(data=data), self.assertRaises(binding.ReconciliationError):
                self.reconcile(self.archives(raw_receipt=data))

    def test_linked_zip_member_is_rejected(self):
        link = zipfile.ZipInfo("source-link")
        link.create_system = 3
        link.external_attr = 0o120777 << 16
        with self.assertRaises(binding.ReconciliationError):
            self.reconcile(self.archives(extra_zip=(link, "outside")))

    def test_actual_archive_sizes_and_expansion_are_bounded(self):
        values = self.archives()
        with patch.object(binding, "MAX_ARCHIVE", values[0].stat().st_size - 1):
            with self.assertRaises(binding.ReconciliationError):
                self.reconcile(values)
        with patch.object(binding, "MAX_EXPANDED", 1024):
            with self.assertRaises(binding.ReconciliationError):
                self.reconcile(values)

    def test_missing_or_falsified_not_applicable_execution_is_rejected(self):
        for value in (None, {}, {"outcome": "NOT_APPLICABLE"}):
            receipt = copy.deepcopy(self.receipt)
            receipt["observedExecution"] = value
            with self.subTest(value=value), self.assertRaises(binding.ReconciliationError):
                self.reconcile(self.archives(receipt))

    def test_verified_snapshot_survives_replacement_of_caller_input_path(self):
        values = self.archives()
        with binding.verified_zip(values[0], values[2]) as archive:
            values[0].write_bytes(b"replacement input")
            self.assertEqual((SHA + "\n").encode(), archive.read("source-sha"))

    def test_real_cli_preserves_existing_output_and_sanitizes_invalid_bytes(self):
        values = self.archives()
        output = self.root / "result.json"
        command = [sys.executable, str(Path(binding.__file__)), "--producer", str(values[0]),
                   "--execution", str(values[1]), "--candidate-sha", SHA, "--run-id", RUN,
                   "--run-attempt", ATTEMPT, "--producer-digest", values[2],
                   "--execution-digest", values[3], "--output", str(output)]
        first = subprocess.run(command, capture_output=True, text=True, timeout=10)
        self.assertEqual(0, first.returncode, first.stdout + first.stderr)
        original = output.read_bytes()
        second = subprocess.run(command, capture_output=True, text=True, timeout=10)
        self.assertEqual(1, second.returncode)
        self.assertEqual(original, output.read_bytes())
        values[1].write_bytes(b"private synthetic invalid payload")
        command[command.index("--execution-digest") + 1] = digest(values[1].read_bytes())
        third = subprocess.run(command, capture_output=True, text=True, timeout=10)
        self.assertEqual(1, third.returncode)
        self.assertNotIn("private synthetic", third.stdout + third.stderr)
        self.assertNotIn(str(self.root), third.stdout + third.stderr)

    def test_real_cli_rejects_deep_json_without_traceback_or_output(self):
        values = self.archives(raw_receipt="[" * 2048 + "0" + "]" * 2048)
        output = self.root / "deep-result.json"
        command = [sys.executable, str(Path(binding.__file__)), "--producer", str(values[0]),
                   "--execution", str(values[1]), "--candidate-sha", SHA, "--run-id", RUN,
                   "--run-attempt", ATTEMPT, "--producer-digest", values[2],
                   "--execution-digest", values[3], "--output", str(output)]
        result = subprocess.run(command, capture_output=True, text=True, timeout=10)
        self.assertEqual(1, result.returncode)
        self.assertEqual("ERROR", json.loads(result.stdout)["outcome"])
        self.assertEqual("", result.stderr)
        self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
