#!/usr/bin/env python3
"""Fail-closed contract for the integration-only Qodana signed shadow."""

from __future__ import annotations

import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/qodana-signed-shadow.yml"
TRUST = ROOT / "governance/workflow-trust-policy.json"
PINS = ROOT / "governance/github-actions-allowlist.json"
NAME = ".github/workflows/qodana-signed-shadow.yml"
ATTEST_SHA = "1e69f48acb82d1966a394da916b4c1698aa569d6"
ALLOWED_EVENTS = {"push"}
ALLOWED_SIGNING = {"id-token:write", "attestations:write"}


def sections(source: str) -> dict[str, str]:
    labels = (
        "  shadow-producer:\n",
        "  shadow-attestor:\n",
        "  shadow-verification:\n",
    )
    positions = [source.find(label) for label in labels]
    if any(p < 0 for p in positions) or positions != sorted(positions):
        raise ValueError("Missing or reordered independent signing jobs")
    return {
        label.strip().rstrip(":"): source[start:end]
        for label, start, end in zip(labels, positions, positions[1:] + [len(source)])
    }


def assert_shadow_contract(source: str) -> None:
    start = source.split("\npermissions:", 1)[0]
    if "on:\n  push:\n    branches: [SecurityTestImplimation]" not in start:
        raise ValueError("Missing exact integration-only push trigger")
    if re.search(r"(?m)^  (pull_request|pull_request_target|merge_group|workflow_run|schedule|release|workflow_dispatch):", start):
        raise ValueError("Untrusted or extra trigger on write-capable signer")
    if re.search(r"(?m)^    branches: \[[^\]]*,", start):
        raise ValueError("Unexpected multiple eligible signing branches")
    if not re.search(r"(?m)^permissions:\n  contents: read\n", source):
        raise ValueError("Top-level token scope broadened")

    jobs = sections(source)
    prod = jobs["shadow-producer"]
    sign = jobs["shadow-attestor"]
    verify = jobs["shadow-verification"]
    if "id-token: write" in prod or "attestations: write" in prod:
        raise ValueError("Qodana analyzer must not receive OIDC signing authority")
    if "JetBrains/qodana-action@" not in prod or "JetBrains/qodana-action@" in sign:
        raise ValueError("Unexpected Qodana runner trust boundary")
    if "node scripts/quality/check-qodana-project-model.mjs" not in prod:
        raise ValueError("Missing established project-model gate")
    if "read_report(Path(os.environ" not in prod:
        raise ValueError("Missing exact SHA/run SARIF provenance check")
    if "if-no-files-found: error" not in prod:
        raise ValueError("Missing original report is not fail-closed")

    if "needs: [shadow-producer]" not in sign or "needs: [shadow-attestor]" not in verify:
        raise ValueError("Original scan/signature dependency chain broken")
    if sign.count("id-token: write") != 1 or sign.count("attestations: write") != 1:
        raise ValueError("Missing or duplicate signing capability")
    if "actions: read" not in sign or "contents: read" not in sign:
        raise ValueError("Signing job must have only required read permissions")
    if "actions/attest@" + ATTEST_SHA + " # v4.2.2" not in sign:
        raise ValueError("Unreviewed signature Action pin")
    if "subject-path: " not in sign or "/shadow-to-sign/qodana.sarif.json" not in sign:
        raise ValueError("Attested subject must be the original downloaded Qodana SARIF")
    if "create-storage-record: false" not in sign:
        raise ValueError("Artifact metadata writes must remain disabled")
    if "GH_TOKEN:" in sign or "secrets." in sign:
        raise ValueError("Cloud/API secret must not enter privileged signer")
    if "attestations: write" in verify or "id-token: write" in verify:
        raise ValueError("Independent verifier must not have signing authority")
    if "scripts/quality/verify_qodana_shadow_signature.py" not in verify:
        raise ValueError("Missing independent Sigstore subject verification")
    if "GH_TOKEN: " not in verify or "attestations: read" not in verify:
        raise ValueError("Verifier requires only read-only attestation access")
    if "needs: [shadow-producer]" in verify:
        raise ValueError("Verifier must require successful separate signer")
    if "QODANA_TOKEN" in source or "pull_request_target" in source.replace("pull_request_target is forbidden", ""):
        raise ValueError("Cloud credentials or a dangerous PR context introduced")
    # A signer must not run directly for a fork, main or a pull request.
    for job in jobs.values():
        if "github.ref == 'refs/heads/SecurityTestImplimation'" not in job:
            raise ValueError("Missing exact signing branch guard")
        if "github.repository_id == '1261244608'" not in job:
            raise ValueError("Missing stable repository identity guard")
        if "github.event_name == 'push'" not in job:
            raise ValueError("Missing trusted push-only guard")


class QodanaSignedShadowContractTests(unittest.TestCase):
    def setUp(self) -> None:
        self.source = WORKFLOW.read_text(encoding="utf-8")
        self.allowlist = json.loads(PINS.read_text(encoding="utf-8"))
        self.trust = json.loads(TRUST.read_text(encoding="utf-8"))

    def test_three_job_chain_and_no_privileged_analysis(self):
        assert_shadow_contract(self.source)

    def test_trust_policy_is_narrow_and_exact(self):
        matches = [entry for entry in self.trust["write_permissions"] if entry.get("workflow") == NAME]
        self.assertEqual(len(matches), 1)
        entry = matches[0]
        self.assertEqual(entry["jobs"], ["shadow-attestor"])
        self.assertEqual(set(entry["events"]), ALLOWED_EVENTS)
        self.assertEqual(set(entry["permissions"]), ALLOWED_SIGNING)
        self.assertNotIn("pull_request", entry["events"])

    def test_attest_pin_is_verified_release_sha(self):
        pin = self.allowlist["actions"]["actions/attest"]
        self.assertEqual(pin["sha"], ATTEST_SHA)
        self.assertEqual(pin["version"], "v4.2.2")
        self.assertIs(pin["privilegedAllowed"], True)

    def test_repository_identity_and_signer_guard_cannot_be_removed(self):
        cases = [
            ("github.repository_id == '1261244608'", "true"),
            ("github.ref == 'refs/heads/SecurityTestImplimation'", "true"),
            ("github.event_name == 'push'", "true"),
        ]
        for original, replacement in cases:
            with self.subTest(source=original):
                with self.assertRaises(ValueError):
                    assert_shadow_contract(self.source.replace(original, replacement))

    def test_pr_or_main_trigger_expansion_is_rejected(self):
        for injection in ("  pull_request:\n", "  workflow_dispatch:\n", "  merge_group:\n",
                          "  push:\n    branches: [main, SecurityTestImplimation]\n"):
            with self.subTest(injection=injection.strip()):
                mutation = self.source.replace("permissions:\n  contents: read", injection+"permissions:\n  contents: read",1)
                with self.assertRaises(ValueError):
                    assert_shadow_contract(mutation)

    def test_untrusted_qodana_container_must_not_receive_oidc(self):
        mutation = self.source.replace(
            "  shadow-producer:\n", "  shadow-producer:\n    permissions:\n      id-token: write\n", 1
        )
        with self.assertRaises(ValueError):
            assert_shadow_contract(mutation)

    def test_signer_must_not_use_mutable_action_or_cloud_secrets(self):
        for mutation in (
            self.source.replace("actions/attest@"+ATTEST_SHA, "actions/attest@v4"),
            self.source.replace("create-storage-record: false", "create-storage-record: true"),
            self.source.replace("      id-token: write", "      id-token: read"),
            self.source.replace("      attestations: write", "      attestations: read"),
            self.source.replace("  shadow-attestor:\n", "  shadow-attestor:\n    env:\n      GH_TOKEN: secret\n"),
        ):
            with self.subTest(sample=mutation[:40]):
                with self.assertRaises(ValueError):
                    assert_shadow_contract(mutation)

    def test_project_model_guard_and_signature_must_not_be_skipped(self):
        for marker in (
            "node scripts/quality/check-qodana-project-model.mjs",
            "read_report(Path(os.environ",
            "needs: [shadow-producer]",
            "needs: [shadow-attestor]",
            "scripts/quality/verify_qodana_shadow_signature.py",
        ):
            with self.subTest(marker=marker):
                with self.assertRaises(ValueError):
                    assert_shadow_contract(self.source.replace(marker, "disabled"))

    def test_static_analysis_trigger_owns_policy_and_signer_tests(self):
        text = (ROOT / ".github/workflows/workflow-static-analysis.yml").read_text(encoding="utf-8")
        for marker in ("tests/ci/test_qodana_shadow_signature.py",
                       "tests/ci/test_qodana_shadow_workflow.py",
                       "scripts/quality/verify_qodana_shadow_signature.py",
                       "governance/github-actions-allowlist.json",
                       "governance/workflow-trust-policy.json"):
            self.assertEqual(text.count('      - "'+marker+'"'), 2)
        self.assertIn("python3 -B tests/ci/test_qodana_shadow_signature.py", text)
        self.assertIn("python3 -B tests/ci/test_qodana_shadow_workflow.py", text)


if __name__ == "__main__":
    unittest.main()
