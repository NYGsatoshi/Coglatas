#!/usr/bin/env python3
"""Offline malicious-input controls for the future signed Qodana subject gate."""

from __future__ import annotations

import copy
import hashlib
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts" / "quality"))
import verify_qodana_signed_subject as policy  # noqa: E402

SHA = "a" * 40
RUN_ID = "40649034671"
GUID = "11111111-1111-4111-8111-111111111111"


class AttestedSubjectTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.file = self.root / "qodana.sarif.json"
        self.cli = self.root / "gh"
        self.cli.write_bytes(b"test-double")
        self.cli.chmod(0o755)
        self.report = {
            "$schema": "https://schemastore.azurewebsites.net/schemas/json/sarif-2.1.0-rtm.6.json",
            "version": "2.1.0",
            "runs": [{
                "automationDetails": {
                    "id": "QDNETC/qodana", "guid": GUID,
                    "properties": {"jobUrl": f"https://github.com/{policy.REPO}/actions/runs/{RUN_ID}"},
                },
                "versionControlProvenance": [{
                    "repositoryUri": policy.REPOSITORY, "revisionId": SHA, "branch": "main",
                }],
                "invocations": [{"executionSuccessful": True, "exitCode": 0}],
                "tool": {"driver": {"name": "QDNETC", "version": "2026.2",
                                    "rules": [{"id": "SyntheticRule"}]}},
                "results": [{"ruleId": "SyntheticRule", "message": {"text": "Test only"}}],
                "artifacts": [{"location": {"uri": "src/Example.cs"}}],
            }],
        }
        self.claim = {}
        self._write_report()
        self._renew_claim()
        self.commands: list[list[str]] = []
        self.returncode = 0

    def _write_report(self):
        self.file.write_text(json.dumps(self.report, sort_keys=True), encoding="utf-8")

    def _renew_claim(self):
        digest = hashlib.sha256(self.file.read_bytes()).hexdigest()
        self.claim = {
            "verificationResult": {
                "signature": {"certificate": {
                    "sourceRepositoryIdentifier": policy.REPO_ID,
                    "sourceRepositoryDigest": SHA,
                    "githubWorkflowSHA": SHA,
                    "githubWorkflowRef": policy.SIGNER_REF,
                    "githubWorkflowTrigger": "push",
                    "runnerInvocationURI": (
                        f"https://github.com/{policy.REPO}/actions/runs/{RUN_ID}/attempts/1"
                    ),
                    "runnerEnvironment": "github-hosted",
                }},
                "verifiedTimestamps": [{"kind": "transparency-log"}],
                "statement": {
                    "predicateType": policy.PROVENANCE_TYPE,
                    "subject": [{"name": "qodana.sarif.json", "digest": {"sha256": digest}}],
                    "predicate": {"metadata": {"provenance": "test-data-untrusted"}},
                },
            },
            "attestation": {"testSynthetic": True},
        }

    def _fake_gh(self, cmd, **kwargs):
        self.commands.append(cmd)
        return subprocess.CompletedProcess(cmd, self.returncode, stdout=json.dumps([self.claim]).encode())

    def _verify(self):
        self._write_report()
        return policy.verify_signed_subject(
            self.file, SHA, RUN_ID, 1, cli=str(self.cli), runner=self._fake_gh,
        )

    def _reject(self):
        with self.assertRaises(policy.ParityError):
            self._verify()

    def test_valid_synthetic_cli_result_is_nonqualifying(self):
        x = self._verify()
        self.assertIs(x["signedSubjectVerified"], True)
        self.assertIs(x["qualifiedForGateReuse"], False)
        self.assertIs(x["producerJobAttested"], False)
        self.assertIs(x["cloudReplayVerified"], False)
        self.assertEqual(x["status"], "SIGNED_SARIF_SUBJECT_VERIFIED_ONLY")
        cmd = self.commands[0]
        self.assertIn("--signer-workflow", cmd)
        self.assertEqual(cmd[cmd.index("--signer-workflow") + 1], policy.SIGNER_WORKFLOW)
        self.assertEqual(cmd[cmd.index("--source-digest") + 1], SHA)
        self.assertEqual(cmd[cmd.index("--signer-digest") + 1], SHA)
        self.assertEqual(cmd[cmd.index("--source-ref") + 1], "refs/heads/main")
        self.assertEqual(cmd[cmd.index("--repo") + 1], policy.REPO)
        self.assertNotIn("send", cmd)

    def test_source_claim_mutations_are_rejected(self):
        for key, bad in (
            ("sourceRepositoryIdentifier", "987"),
            ("sourceRepositoryDigest", "b" * 40),
            ("githubWorkflowSHA", "b" * 40),
            ("githubWorkflowRef", policy.SIGNER_REF + "-untrusted"),
            ("githubWorkflowTrigger", "pull_request"),
            ("runnerInvocationURI", f"https://github.com/{policy.REPO}/actions/runs/{RUN_ID}/attempts/2"),
            ("runnerEnvironment", "self-hosted"),
        ):
            with self.subTest(key=key):
                self._renew_claim()
                self.claim["verificationResult"]["signature"]["certificate"][key] = bad
                self._reject()

    def test_missing_or_wrong_certificate_is_rejected(self):
        for value in (None, {}, {"sourceRepositoryIdentifier": policy.REPO_ID}):
            with self.subTest(value=value):
                self._renew_claim()
                self.claim["verificationResult"]["signature"]["certificate"] = value
                self._reject()

    def test_signed_subject_mismatch_or_extra_subject_rejected(self):
        cases = [
            [{"name": "qodana.sarif.json", "digest": {"sha256": "f" * 64}}],
            [{"name": "another.sarif.json", "digest": {"sha256": "f" * 64}}],
            [],
        ]
        for case in cases:
            with self.subTest(case=case):
                self._renew_claim()
                self.claim["verificationResult"]["statement"]["subject"] = case
                self._reject()
        self._renew_claim()
        self.claim["verificationResult"]["statement"]["subject"].append(
            {"name": "other.bin", "digest": {"sha256": "f" * 64}}
        )
        self._reject()

    def test_timestamp_and_predicate_are_required(self):
        self.claim["verificationResult"]["verifiedTimestamps"] = []
        self._reject()
        self._renew_claim()
        self.claim["verificationResult"]["statement"]["predicateType"] = "untrusted"
        self._reject()

    def test_report_changed_after_certificate_preparation_fails(self):
        self.report["runs"][0]["results"][0]["message"]["text"] = "Changed report"
        self._reject()

    def test_nonzero_gh_result_rejected_without_fallback(self):
        self.returncode = 1
        self._reject()

    def test_missing_binary_is_rejected(self):
        with self.assertRaises(policy.ParityError):
            policy.verify_signed_subject(
                self.file, SHA, RUN_ID, 1, cli=str(self.root / "missing"),
                runner=self._fake_gh,
            )

    def test_invalid_expected_sha_run_attempt_are_rejected(self):
        for sha, run, attempt in [
            ("z" * 40, RUN_ID, 1), (SHA, "0", 1), (SHA, RUN_ID, 0), (SHA, RUN_ID, 101),
            (SHA, RUN_ID + "suffix", 1),
        ]:
            with self.subTest(sha=sha, run=run, attempt=attempt):
                with self.assertRaises(policy.ParityError):
                    policy.verify_signed_subject(
                        self.file, sha, run, attempt, cli=str(self.cli), runner=self._fake_gh,
                    )

    def test_bad_original_sarif_is_rejected(self):
        self.report["runs"][0]["invocations"][0]["exitCode"] = 1
        self._reject()

    def test_symlinked_original_report_is_rejected(self):
        actual = self.root / "untrusted.json"
        self.file.rename(actual)
        self.file.symlink_to(actual)
        with self.assertRaises(policy.ParityError):
            policy.verify_signed_subject(
                self.file, SHA, RUN_ID, 1, cli=str(self.cli), runner=self._fake_gh,
            )

    def test_oversized_or_malformed_gh_output_is_rejected(self):
        for response in (b"[]", b"{}", b'{"bad":NaN}', b'{"x":1,"x":2}', b"\xff", b"x" * (policy.MAX_VERIFICATION_BYTES + 1)):
            with self.subTest(sample=response[:15]):
                def bad_gh(cmd, **kwargs):
                    return subprocess.CompletedProcess(cmd, 0, stdout=response)
                with self.assertRaises(policy.ParityError):
                    policy.verify_signed_subject(
                        self.file, SHA, RUN_ID, 1, cli=str(self.cli), runner=bad_gh,
                    )

    def test_missing_gh_data_is_rejected(self):
        self.claim["verificationResult"].pop("signature")
        self._reject()

    def test_cli_dryrun_fails_without_signed_attestation(self):
        output = self.root / "receipt.json"
        # The GitHub-hosted runner may contain a functioning gh binary. This
        # self-test never contacts the attestation service or uses a token.
        with patch.object(policy.shutil, "which", return_value=None):
            self.assertEqual(policy.main([
                "--report", str(self.file), "--source-sha", SHA,
                "--run-id", RUN_ID, "--run-attempt", "1",
                "--output", str(output),
            ]), 1)
        self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
