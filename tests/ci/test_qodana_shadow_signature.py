#!/usr/bin/env python3
"""No-network mutation tests for the integration Qodana signed subject."""

from __future__ import annotations

import hashlib
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts" / "quality"))
import verify_qodana_shadow_signature as policy  # noqa: E402

SHA = "a" * 40
RUN = "40649034671"
GUID = "11111111-1111-4111-8111-111111111111"


class ShadowQodanaSignerTests(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.report = self.root / "qodana.sarif.json"
        self.binary = self.root / "gh"
        self.binary.write_bytes(b"fake-gh-for-unit-test")
        self.binary.chmod(0o755)
        self.doc = {
            "$schema": "https://schemastore.azurewebsites.net/schemas/json/sarif-2.1.0-rtm.6.json",
            "version": "2.1.0",
            "runs": [{
                "automationDetails": {"id": "QDNETC/qodana", "guid": GUID,
                    "properties": {"jobUrl": f"https://github.com/{policy.REPO}/actions/runs/{RUN}"}},
                "versionControlProvenance": [{"repositoryUri":
                    f"https://github.com/{policy.REPO}.git", "revisionId": SHA, "branch": policy.BRANCH}],
                "invocations": [{"executionSuccessful": True, "exitCode": 0}],
                "tool": {"driver": {"name": "QDNETC", "version": "2026.2",
                                    "rules": [{"id": "SyntheticRule"}]}},
                "results": [{"ruleId": "SyntheticRule", "message": {"text": "Synthetic"}}],
                "artifacts": [{"location": {"uri": "src/Example.cs"}}],
            }],
        }
        self.claim = {}
        self.commands = []
        self.exitcode = 0
        self.write_report()
        self.make_claim()

    def write_report(self):
        self.report.write_text(json.dumps(self.doc, sort_keys=True), encoding="utf-8")

    def make_claim(self):
        digest = hashlib.sha256(self.report.read_bytes()).hexdigest()
        self.claim = {"verificationResult": {
            "signature": {"certificate": {
                "sourceRepositoryIdentifier": policy.REPO_ID,
                "sourceRepositoryDigest": SHA,
                "githubWorkflowSHA": SHA,
                "githubWorkflowRef": policy.WORKFLOW_REF,
                "githubWorkflowTrigger": "push",
                "runnerInvocationURI": f"https://github.com/{policy.REPO}/actions/runs/{RUN}/attempts/1",
                "runnerEnvironment": "github-hosted",
            }},
            "verifiedTimestamps": [{"kind": "transparency-log"}],
            "statement": {
                "predicateType": policy.PREDICATE,
                "subject": [{"name": "qodana.sarif.json", "digest": {"sha256": digest}}],
                "predicate": {"syntheticUntrusted": True},
            },
        }}

    def gh(self, command, **_options):
        self.commands.append(command)
        return subprocess.CompletedProcess(command, self.exitcode,
                                           stdout=json.dumps([self.claim]).encode())

    def verify(self):
        self.write_report()
        return policy.verify(self.report, SHA, RUN, 1,
                             cli=str(self.binary), runner=self.gh)

    def fail(self):
        with self.assertRaises(policy.ParityError):
            self.verify()

    def test_synthetic_valid_verification_is_shadow_only(self):
        output = self.verify()
        self.assertTrue(output["signedSubjectVerified"])
        for field in ("producerJobAttested", "mainProducerAttested",
                      "cloudReplayVerified", "qualifiedForGateReuse"):
            self.assertIs(output[field], False)
        self.assertEqual(output["status"], "SECURITY_BRANCH_SHADOW_SIGNATURE_VERIFIED")
        command = self.commands[0]
        self.assertEqual(command[0], str(self.binary))
        self.assertEqual(command[command.index("--signer-workflow") + 1], policy.WORKFLOW)
        self.assertEqual(command[command.index("--signer-digest") + 1], SHA)
        self.assertEqual(command[command.index("--source-digest") + 1], SHA)
        self.assertEqual(command[command.index("--source-ref") + 1], "refs/heads/SecurityTestImplimation")
        self.assertEqual(command[command.index("--repo") + 1], policy.REPO)
        self.assertNotIn("send", command)

    def test_mutated_signed_identity_rejected(self):
        fields = {
            "sourceRepositoryIdentifier": "0",
            "sourceRepositoryDigest": "b"*40,
            "githubWorkflowSHA": "b"*40,
            "githubWorkflowRef": policy.WORKFLOW_REF + "/untrusted",
            "githubWorkflowTrigger": "pull_request",
            "runnerInvocationURI": f"https://github.com/{policy.REPO}/actions/runs/{RUN}/attempts/2",
            "runnerEnvironment": "self-hosted",
        }
        for name, invalid in fields.items():
            with self.subTest(name=name):
                self.make_claim()
                self.claim["verificationResult"]["signature"]["certificate"][name] = invalid
                self.fail()

    def test_untrusted_predicate_cannot_override_certificate(self):
        self.make_claim()
        self.claim["verificationResult"]["signature"]["certificate"]["runnerEnvironment"] = "self-hosted"
        self.claim["verificationResult"]["statement"]["predicate"]["runnerEnvironment"] = "github-hosted"
        self.fail()

    def test_missing_or_wrong_cert_rejected(self):
        for invalid in (None, {}, {"sourceRepositoryIdentifier": policy.REPO_ID}):
            with self.subTest(invalid=invalid):
                self.make_claim()
                self.claim["verificationResult"]["signature"]["certificate"] = invalid
                self.fail()

    def test_subject_digest_or_extra_subject_rejected(self):
        self.claim["verificationResult"]["statement"]["subject"][0]["digest"]["sha256"] = "b"*64
        self.fail()
        self.make_claim()
        self.claim["verificationResult"]["statement"]["subject"].append({"name": "extra.bin", "digest": {"sha256": "b"*64}})
        self.fail()

    def test_missing_timestamp_and_wrong_predicate_rejected(self):
        self.claim["verificationResult"]["verifiedTimestamps"] = []
        self.fail()
        self.make_claim()
        self.claim["verificationResult"]["statement"]["predicateType"] = "unreviewed"
        self.fail()

    def test_mutated_original_sarif_rejected(self):
        self.doc["runs"][0]["results"][0]["message"]["text"] = "Different"
        self.fail()

    def test_other_branch_or_other_scan_result_rejected(self):
        self.doc["runs"][0]["versionControlProvenance"][0]["branch"] = "main"
        self.fail()
        self.doc["runs"][0]["versionControlProvenance"][0]["branch"] = policy.BRANCH
        self.doc["runs"][0]["invocations"][0]["executionSuccessful"] = False
        self.fail()

    def test_missing_verifier_or_nonzero_signature_result_rejected(self):
        self.exitcode = 1
        self.fail()
        self.exitcode = 0
        with self.assertRaises(policy.ParityError):
            policy.verify(self.report, SHA, RUN, 1,
                          cli=str(self.root / "absent-gh"), runner=self.gh)

    def test_bad_sha_run_attempt_rejected(self):
        cases = [("z"*40, RUN, 1), (SHA, "0", 1), (SHA, RUN, 0), (SHA, RUN, 101),
                 (SHA, RUN+"extra", 1)]
        for sha, run, attempt in cases:
            with self.subTest(sha=sha,run=run,attempt=attempt):
                with self.assertRaises(policy.ParityError):
                    policy.verify(self.report, sha, run, attempt,
                                  cli=str(self.binary),runner=self.gh)

    def test_symlink_report_rejected(self):
        other=self.root/"untrusted.sarif"
        self.report.rename(other)
        self.report.symlink_to(other)
        with self.assertRaises(policy.ParityError):
            policy.verify(self.report, SHA, RUN, 1,
                          cli=str(self.binary),runner=self.gh)

    def test_malformed_or_duplicate_json_rejected(self):
        for body in (b"[]", b"{}", b'{"x":1,"x":2}', b'[NaN]', b"x"* (policy.MAX_GH_BYTES+1)):
            with self.subTest(prefix=body[:8]):
                def fake_gh(cmd,**kwargs):
                    return subprocess.CompletedProcess(cmd,0,stdout=body)
                with self.assertRaises(policy.ParityError):
                    policy.verify(self.report,SHA,RUN,1,
                                  cli=str(self.binary),runner=fake_gh)

    def test_cli_missing_verifier_does_not_emit_success_evidence(self):
        path=self.root/"output.json"
        with patch.object(policy.shutil, "which", return_value=None):
            self.assertEqual(policy.main(["--report",str(self.report),"--source-sha",SHA,
                         "--run-id",RUN,"--run-attempt","1","--output",str(path)]),1)
        self.assertFalse(path.exists())


if __name__ == "__main__":
    unittest.main()
