"""Deliberate wrong-run, stale-artifact and false provenance controls."""

import base64
import copy
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import urllib.error

import sec_arch_github_provenance as provenance
import sec_arch_reconcile as binding
import test_sec_arch_reconcile as archive_fixtures

SHA = "1" * 40
NOW = datetime(2026, 10, 10, tzinfo=timezone.utc)


class LiveProvenanceTests(unittest.TestCase):
    def setUp(self):
        self.run = {"id": 10, "run_attempt": 2, "head_sha": SHA, "head_branch": "main", "event": "push",
                    "path": provenance.WORKFLOW, "status": "completed", "conclusion": "success", "workflow_id": 20,
                    "repository": {"id": 30, "full_name": provenance.REPOSITORY},
                    "head_repository": {"id": 30, "full_name": provenance.REPOSITORY},
                    "created_at": "2026-10-09T00:00:00Z", "updated_at": "2026-10-09T01:00:00Z"}
        self.jobs = [{"id": 40 + i, "name": name, "run_id": 10, "run_attempt": 2, "head_sha": SHA,
                      "status": "completed", "conclusion": "success"} for i, name in enumerate(provenance.REQUIRED_JOBS)]
        self.artifacts = [{"id": 50 + i, "name": name, "size_in_bytes": 100,
                           "digest": "sha256:" + str(i + 2) * 64, "expired": False,
                           "workflow_run": {"id": 10, "repository_id": 30, "head_repository_id": 30,
                                            "head_sha": SHA, "head_branch": "main"},
                           "created_at": "2026-10-09T01:00:00Z", "expires_at": "2026-10-11T00:00:00Z"}
                          for i, name in enumerate(provenance.ARTIFACT_NAMES)]
        self.source = {"type": "file", "encoding": "base64", "size": 9,
                       "content": base64.b64encode(b"synthetic").decode()}
        self.calls = []

    def api(self, path):
        self.calls.append(path)
        if "/contents/" in path:
            return copy.deepcopy(self.source)
        if "/workflows/" in path:
            return {"id": 20, "path": provenance.WORKFLOW}
        if "/jobs?" in path:
            return {"total_count": len(self.jobs), "jobs": copy.deepcopy(self.jobs)}
        if "/artifacts?" in path:
            return {"total_count": len(self.artifacts), "artifacts": copy.deepcopy(self.artifacts)}
        return copy.deepcopy(self.run)

    def resolve(self, api=None, **kwargs):
        return provenance.resolve(api or self.api, kwargs.get("candidate", SHA), kwargs.get("run_id", 10),
                                  kwargs.get("attempt", 2), kwargs.get("ids", (50, 51)), NOW,
                                  kwargs.get("reconcile_bytes"))

    def test_live_snapshot_preserves_authority_and_metadata_only_limits(self):
        result = self.resolve()
        self.assertEqual("LIVE_GITHUB_MAIN_PROVENANCE_OBSERVED", result["qualification"])
        self.assertEqual(3, len(result["jobs"]))
        self.assertEqual(hashlib.sha256(b"synthetic").hexdigest(), result["workflowSourceDigest"])
        self.assertIsNone(result["producerByteBinding"])
        self.assertIsNone(result["ownerApproval"])
        self.assertEqual("UNVERIFIED", result["trustedAttestation"])
        self.assertEqual("PRE-AVALONIA SEC-ARCH: BLOCKED", result["preAvaloniaVerdict"])
        self.assertEqual(2, sum("/artifacts?" in path for path in self.calls))

    def test_wrong_run_attempt_candidate_and_boolean_ids_are_rejected(self):
        for kwargs in ({"candidate": "4" * 40}, {"attempt": 1}, {"run_id": 11}, {"run_id": True},
                       {"attempt": True}, {"ids": (True, 51)}, {"ids": (50, 50)}):
            with self.subTest(kwargs=kwargs), self.assertRaises(binding.ReconciliationError):
                self.resolve(**kwargs)

    def test_fork_pull_request_dispatch_or_other_workflow_cannot_be_trusted_main(self):
        for field, value in (("head_branch", "feature"), ("event", "pull_request"), ("event", "workflow_dispatch"),
                             ("path", ".github/workflows/other.yml"), ("status", "in_progress"),
                             ("conclusion", "failure"), ("head_repository", {"id": 31, "full_name": "fork/Coglatas"})):
            with self.subTest(field=field, value=value):
                original = self.run[field]
                self.run[field] = value
                with self.assertRaises(binding.ReconciliationError):
                    self.resolve()
                self.run[field] = original

    def test_required_verifier_missing_disabled_skipped_deleted_and_wrong_attempt_fail(self):
        for mutation in ("missing", "duplicate", "disabled", "skipped", "failed", "candidate", "attempt"):
            original = copy.deepcopy(self.jobs)
            if mutation == "missing": self.jobs.pop()
            elif mutation == "duplicate": self.jobs.append(copy.deepcopy(self.jobs[0]))
            else:
                field, value = {"disabled": ("name", "renamed"), "skipped": ("conclusion", "skipped"),
                                "failed": ("conclusion", "failure"), "candidate": ("head_sha", "4" * 40),
                                "attempt": ("run_attempt", 1)}[mutation]
                self.jobs[0][field] = value
            with self.subTest(mutation=mutation), self.assertRaises(binding.ReconciliationError):
                self.resolve()
            self.jobs = original

    def test_exact_artifact_id_name_digest_scope_and_expiry_are_required(self):
        for field, value in (("id", 55), ("name", "unrelated"), ("digest", None), ("digest", "sha256:bad"),
                             ("expired", True), ("size_in_bytes", True), ("size_in_bytes", binding.MAX_ARCHIVE + 1),
                             ("expires_at", "2026-10-10T00:00:00Z")):
            original = self.artifacts[0][field]
            self.artifacts[0][field] = value
            with self.subTest(field=field, value=value), self.assertRaises(binding.ReconciliationError):
                self.resolve()
            self.artifacts[0][field] = original
        for field, value in (("id", 11), ("repository_id", 31), ("head_repository_id", 31),
                             ("head_sha", "4" * 40), ("head_branch", "feature")):
            original = self.artifacts[0]["workflow_run"][field]
            self.artifacts[0]["workflow_run"][field] = value
            with self.subTest(field=field), self.assertRaises(binding.ReconciliationError):
                self.resolve()
            self.artifacts[0]["workflow_run"][field] = original

    def test_deleted_duplicate_and_replaced_artifacts_do_not_resolve(self):
        for mutation in ("missing", "duplicate-id", "duplicate-name"):
            original = copy.deepcopy(self.artifacts)
            if mutation == "missing": self.artifacts.pop()
            else:
                duplicate = copy.deepcopy(self.artifacts[0])
                if mutation == "duplicate-name": duplicate["id"] = 99
                self.artifacts.append(duplicate)
            with self.subTest(mutation=mutation), self.assertRaises(binding.ReconciliationError):
                self.resolve()
            self.artifacts = original

    def test_authority_changes_while_binding_bytes_are_rejected(self):
        for mutation in ("run", "job", "artifact"):
            original = copy.deepcopy((self.run, self.jobs, self.artifacts))

            def changed(_artifacts):
                if mutation == "run": self.run["run_attempt"] = 3
                elif mutation == "job": self.jobs[0]["conclusion"] = "failure"
                else: self.artifacts[0]["digest"] = "sha256:" + "5" * 64
                return {"qualification": "SYNTHETIC_ONLY"}

            with self.subTest(mutation=mutation), self.assertRaises(binding.ReconciliationError):
                self.resolve(reconcile_bytes=changed)
            self.run, self.jobs, self.artifacts = original

    def test_required_full_list_cannot_be_replaced_with_shrunken_page(self):
        for total in (False, 1001, 4):
            def shrunk(path):
                if "/jobs?" in path: return {"total_count": total, "jobs": copy.deepcopy(self.jobs)}
                return self.api(path)
            with self.subTest(total=total), self.assertRaises(binding.ReconciliationError):
                self.resolve(api=shrunk)

    def test_immutable_workflow_is_pinned_and_malformed_bytes_fail(self):
        for field, value in (("type", "directory"), ("encoding", "none"), ("size", 10), ("content", "bad!")):
            original = self.source[field]
            self.source[field] = value
            with self.subTest(field=field), self.assertRaises((ValueError, binding.ReconciliationError)):
                self.resolve()
            self.source[field] = original
        self.resolve()
        self.assertTrue(any(path.endswith("?ref=" + SHA) for path in self.calls))

    def test_credential_client_rejects_nonfixed_origin_and_redirects(self):
        client = provenance.LiveGitHub("synthetic-secret")
        for path in ("https://evil.example/", "repos/other/repo/actions", "repos/NYGsatoshi/Coglatas/../secret"):
            with self.subTest(path=path), self.assertRaises(binding.ReconciliationError):
                client(path)
        with self.assertRaises(binding.ReconciliationError):
            provenance.NoRedirect().redirect_request(None, None, 302, "", {}, "https://evil.example/")

    def test_cli_preserves_existing_report_and_uses_live_resolver(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "report.json"
            argv = ["sec_arch_github_provenance", "--candidate-sha", SHA, "--run-id", "10", "--run-attempt", "2",
                    "--producer-id", "50", "--execution-id", "51", "--output", str(output)]
            with patch("sys.argv", argv), patch.object(provenance, "LiveGitHub", return_value=self.api), \
                 patch.object(provenance, "datetime") as clock:
                clock.now.return_value = NOW
                clock.fromisoformat.side_effect = datetime.fromisoformat
                self.assertEqual(0, provenance.main())
                original = output.read_bytes()
                self.assertEqual(1, provenance.main())
                self.assertEqual(original, output.read_bytes())
            self.assertEqual("UNVERIFIED", json.loads(original)["trustedAttestation"])

    def test_transport_failure_cannot_echo_secret_or_create_report(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "report.json"
            argv = ["tool", "--candidate-sha", SHA, "--run-id", "10", "--run-attempt", "2",
                    "--producer-id", "50", "--execution-id", "51", "--output", str(output)]
            def unavailable(_path):
                raise urllib.error.URLError("synthetic-secret")
            with patch("sys.argv", argv), patch.object(provenance, "LiveGitHub", return_value=unavailable), \
                 patch("builtins.print") as messages:
                self.assertEqual(1, provenance.main())
                self.assertNotIn("synthetic-secret", str(messages.call_args_list))
                self.assertFalse(output.exists())

    def test_live_cli_binds_real_archive_bytes_and_preserves_failed_receipt(self):
        fixture = archive_fixtures.ProducerBindingTests()
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        fixture.receipt.update(runId="10", runAttempt="2")
        fixture.receipt["observedExecution"]["outcome"] = "FAIL"
        producer, execution, producer_digest, execution_digest = fixture.archives()
        for path, digest, artifact in zip((producer, execution), (producer_digest, execution_digest), self.artifacts):
            artifact.update(digest="sha256:" + digest, size_in_bytes=path.stat().st_size)
        output = fixture.root / "live.json"
        argv = ["tool", "--candidate-sha", SHA, "--run-id", "10", "--run-attempt", "2",
                "--producer-id", "50", "--execution-id", "51", "--producer", str(producer),
                "--execution", str(execution), "--output", str(output)]
        original = execution.read_bytes()
        with patch("sys.argv", argv), patch.object(provenance, "LiveGitHub", return_value=self.api), \
             patch.object(provenance, "datetime") as clock:
            clock.now.return_value = NOW
            clock.fromisoformat.side_effect = datetime.fromisoformat
            self.assertEqual(0, provenance.main())
        result = json.loads(output.read_bytes())
        self.assertEqual("FAIL", result["producerByteBinding"]["originalExecutionOutcome"])
        self.assertEqual(original, execution.read_bytes())
        self.assertEqual("PRE-AVALONIA SEC-ARCH: BLOCKED", result["preAvaloniaVerdict"])

    def test_live_provenance_reconciles_six_assemblies_without_granting_acceptance(self):
        fixture = archive_fixtures.ProducerBindingTests()
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        fixture.use_six_assembly_receipt()
        fixture.receipt.update(runId="10", runAttempt="2")
        producer, execution, producer_digest, execution_digest = fixture.archives()
        for path, digest, artifact in zip((producer, execution), (producer_digest, execution_digest), self.artifacts):
            artifact.update(digest="sha256:" + digest, size_in_bytes=path.stat().st_size)
        def bytes_report(artifacts):
            return binding.reconcile(producer, execution, SHA, "10", "2",
                                     artifacts[0]["digest"].removeprefix("sha256:"),
                                     artifacts[1]["digest"].removeprefix("sha256:"))
        result = self.resolve(reconcile_bytes=bytes_report)
        self.assertEqual(6, result["producerByteBinding"]["boundAssemblyCount"])
        self.assertEqual("SIX_ASSEMBLY_BYTES_RECONCILED", result["producerByteBinding"]["fullDependencyQualification"])
        self.assertEqual("UNVERIFIED", result["trustedAttestation"])
        self.assertIsNone(result["ownerApproval"])

    def test_live_metadata_cannot_qualify_a_wrong_build_or_local_archive(self):
        fixture = archive_fixtures.ProducerBindingTests()
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        fixture.receipt.update(runId="10", runAttempt="2")
        producer, execution, producer_digest, execution_digest = fixture.archives(stamp="4" * 40)
        for path, digest, artifact in zip((producer, execution), (producer_digest, execution_digest), self.artifacts):
            artifact.update(digest="sha256:" + digest, size_in_bytes=path.stat().st_size)
        output = fixture.root / "live.json"
        argv = ["tool", "--candidate-sha", SHA, "--run-id", "10", "--run-attempt", "2",
                "--producer-id", "50", "--execution-id", "51", "--producer", str(producer),
                "--execution", str(execution), "--output", str(output)]
        with patch("sys.argv", argv), patch.object(provenance, "LiveGitHub", return_value=self.api), \
             patch.object(provenance, "datetime") as clock:
            clock.now.return_value = NOW
            clock.fromisoformat.side_effect = datetime.fromisoformat
            self.assertEqual(1, provenance.main())
            self.assertFalse(output.exists())
            self.artifacts[0]["size_in_bytes"] += 1
            self.assertEqual(1, provenance.main())
            self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
