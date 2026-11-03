#!/usr/bin/env python3
"""Reject duplicate scanner evidence in Security PR CI artifacts.

This is an ownership/retention contract, not a replacement for native SEC-04
or SEC-06. A real scanner failure must still publish the original evidence
through its existing always() upload.
"""
from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/ci.yml"
UPLOAD = "        uses: actions/upload-artifact@"


def security_uploads(document: str) -> dict[str, tuple[str, tuple[str, ...], str]]:
    """Parse actual job/step-scoped upload declarations without evaluating YAML."""
    result: dict[str, tuple[str, tuple[str, ...], str]] = {}
    job = None
    step = None
    blocks = []
    for line in document.splitlines():
        job_match = re.match(r"^  ([a-z0-9-]+):\s*$", line)
        if job_match:
            job = job_match.group(1)
        if line.startswith("      - name: "):
            if step is not None:
                blocks.append((job_for_step, "\n".join(step)))
            step = [line]
            job_for_step = job
        elif step is not None:
            step.append(line)
    if step is not None:
        blocks.append((job_for_step, "\n".join(step)))
    for owner, block in blocks:
        if UPLOAD not in block:
            continue
        name = re.search(r"(?m)^          name: ([^\n]+)$", block)
        path = re.search(r"(?m)^          path: (.*)$", block)
        if name is None or path is None:
            raise ValueError("Upload step without a literal name/path")
        artifact = name.group(1).strip()
        if artifact in result:
            raise ValueError(f"Duplicate Security CI artifact owner: {artifact}")
        if path.group(1).strip() != "|":
            paths = (path.group(1).strip(),)
        else:
            lines = block[path.end():].splitlines()
            paths = tuple(line.strip() for line in lines if line.startswith("            ")
                          and line.strip() and not line.strip().startswith("#"))
        result[artifact] = (owner, paths, block)
    return result


def step_condition(block: str) -> str:
    """Read the effective, exact step guard; absent/ambiguous guards fail closed."""
    conditions = re.findall(r"(?m)^        if: ([^\r\n]+)$", block)
    if len(conditions) != 1:
        raise ValueError("Security artifact upload requires one explicit step condition")
    return conditions[0].strip()


def validate(document: str) -> None:
    """Require immutable canonical evidence and disjoint failure-only diagnostics."""
    published = security_uploads(document)
    expected = {
        "dotnet-release-build": "dotnet-build",
        "frontend-build-artifacts": "frontend-build",
        "security-openapi-build": "dotnet-build",
        "pr-functional-runtime": "functional-runtime",
        "sec06-zap-evidence": "security-scan",
        "security-scan-reports": "security-scan",
    }
    for name, owner in expected.items():
        if name not in published or published[name][0] != owner:
            raise ValueError(f"Incorrect or missing unique artifact producer: {name}")
    sec_owner, evidence, sec_step = published["sec06-zap-evidence"]
    report_owner, diagnostics, report_step = published["security-scan-reports"]
    required_evidence = {
        "artifacts/security/zap/",
        "artifacts/security/schemathesis/",
        "artifacts/security/sec05-authorization-negative-matrix.json",
    }
    required_diagnostics = {
        "dotnet-vulnerable-packages.txt",
        "trivy-filesystem-report.txt",
        "gitleaks-report.json",
    }
    if set(evidence) != required_evidence:
        raise ValueError("Canonical SEC-04/05/06 evidence must remain complete")
    canonical_paths = {value.rstrip("/") for value in required_evidence}
    # A different upload name cannot republish any canonical path or ancestor
    # directory. This covers duplicate metadata under a new artifact name.
    for artifact, (_, paths, _) in published.items():
        if artifact == "sec06-zap-evidence":
            continue
        for item in paths:
            candidate = item.removeprefix("./").rstrip("/")
            if any(candidate == target or candidate.startswith(target + "/")
                   or target.startswith(candidate + "/") for target in canonical_paths):
                raise ValueError("Canonical Security evidence has multiple artifact owners")
    if set(diagnostics) != required_diagnostics:
        raise ValueError("Security failure reports must contain only scanner-independent diagnostics")
    if set(evidence).intersection(diagnostics):
        raise ValueError("Overlapping canonical evidence and failure report uploads")
    # An always() substring is insufficient: always() && false is not evidence.
    # Preserve the existing PR scope route and non-PR behavior exactly.
    canonical_condition = (
        "always() && (github.event_name != 'pull_request' || "
        "needs.dotnet-build.outputs.security_compose == 'true')"
    )
    missing_policy = re.findall(r"(?m)^          if-no-files-found: ([^\r\n]+)$", sec_step)
    if step_condition(sec_step) != canonical_condition or missing_policy != ["error"]:
        raise ValueError("Canonical Security evidence must remain mandatory and fail closed")
    advisory_continue = re.findall(r"(?m)^        continue-on-error: ([^\r\n]+)$", report_step)
    if step_condition(report_step) != "failure()" or advisory_continue != ["true"]:
        raise ValueError("Advisory diagnostics may not weaken mandatory Security evidence")


class SecurityArtifactOwnershipTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = WORKFLOW.read_text(encoding="utf-8")

    def test_current_ci_has_single_canonical_security_evidence_owner(self):
        validate(self.source)

    def test_duplicate_name_fails(self):
        tampered = self.source.replace("name: security-scan-reports",
                                       "name: sec06-zap-evidence", 1)
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            validate(tampered)

    def test_reintroducing_overlapping_evidence_fails(self):
        tampered = self.source.replace(
            "            gitleaks-report.json\n          if-no-files-found: warn",
            "            gitleaks-report.json\n            artifacts/security/schemathesis/\n"
            "          if-no-files-found: warn", 1,
        )
        self.assertNotEqual(tampered, self.source)
        with self.assertRaisesRegex(ValueError, "only scanner-independent"):
            validate(tampered)

    def test_missing_canonical_zap_or_schemathesis_fails(self):
        for path in ("artifacts/security/zap/", "artifacts/security/schemathesis/"):
            with self.subTest(path=path):
                tampered = self.source.replace("            " + path + "\n", "", 1)
                self.assertNotEqual(tampered, self.source)
                with self.assertRaisesRegex(ValueError, "remain complete"):
                    validate(tampered)

    def test_optional_or_silent_security_evidence_fails(self):
        tampered = self.source.replace(
            "name: sec06-zap-evidence\n          path:",
            "name: sec06-zap-evidence\n          path:", 1,
        ).replace(
            "            artifacts/security/schemathesis/\n          if-no-files-found: error",
            "            artifacts/security/schemathesis/\n          if-no-files-found: ignore", 1,
        )
        self.assertNotEqual(tampered, self.source)
        with self.assertRaisesRegex(ValueError, "fail closed"):
            validate(tampered)

    def test_disabled_canonical_publication_fails_closed(self):
        original = (
            "      - name: Upload SEC-06 sanitized ZAP evidence\n"
            "        if: always() && (github.event_name != 'pull_request' || "
            "needs.dotnet-build.outputs.security_compose == 'true')"
        )
        for invalid in ("always() && false", "always()", "success()"):
            with self.subTest(condition=invalid):
                mutated = self.source.replace(
                    original, "      - name: Upload SEC-06 sanitized ZAP evidence\n"
                    + "        if: " + invalid, 1
                )
                self.assertNotEqual(mutated, self.source)
                with self.assertRaisesRegex(ValueError, "fail closed"):
                    validate(mutated)

    def test_disabled_failure_diagnostic_publication_fails_closed(self):
        original = "      - name: Upload security reports on failure\n        if: failure()"
        for invalid in ("failure() && false", "always() && false", "success()"):
            with self.subTest(condition=invalid):
                mutated = self.source.replace(
                    original, "      - name: Upload security reports on failure\n"
                    + "        if: " + invalid, 1
                )
                self.assertNotEqual(mutated, self.source)
                with self.assertRaisesRegex(ValueError, "Advisory diagnostics"):
                    validate(mutated)

    def test_canonical_missing_condition_fails_closed(self):
        original = (
            "      - name: Upload SEC-06 sanitized ZAP evidence\n"
            "        if: always() && (github.event_name != 'pull_request' || "
            "needs.dotnet-build.outputs.security_compose == 'true')"
        )
        mutated = self.source.replace(original, "      - name: Upload SEC-06 sanitized ZAP evidence", 1)
        self.assertNotEqual(mutated, self.source)
        with self.assertRaisesRegex(ValueError, "explicit step condition"):
            validate(mutated)

    def test_false_green_commented_missing_policy_fails_closed(self):
        marker = "          if-no-files-found: error\n          retention-days: 7"
        mutated = self.source.replace(
            marker,
            "          if-no-files-found: ignore # if-no-files-found: error\n          retention-days: 7",
            1,
        )
        self.assertNotEqual(mutated, self.source)
        with self.assertRaisesRegex(ValueError, "fail closed"):
            validate(mutated)

    def test_second_named_or_parent_directory_owner_fails(self):
        marker = "      - name: Upload security reports on failure\n"
        for path in ("artifacts/security/schemathesis/", "artifacts/security/zap/",
                     "artifacts/security/"):
            with self.subTest(path=path):
                injected = (
                    "      - name: Duplicate scanner evidence\n"
                    "        if: failure()\n"
                    "        uses: actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a\n"
                    "        with:\n"
                    "          name: secondary-security-evidence\n"
                    f"          path: {path}\n"
                    "          if-no-files-found: warn\n"
                )
                mutated = self.source.replace(marker, injected + marker, 1)
                self.assertNotEqual(mutated, self.source)
                with self.assertRaisesRegex(ValueError, "multiple artifact owners"):
                    validate(mutated)

    def test_failure_diagnostics_continue_on_error_must_be_effective(self):
        marker = ("      - name: Upload security reports on failure\n"
                  "        if: failure()\n"
                  "        continue-on-error: true")
        mutation = marker.replace("continue-on-error: true",
                                  "continue-on-error: false # continue-on-error: true")
        modified = self.source.replace(marker, mutation, 1)
        self.assertNotEqual(modified, self.source)
        with self.assertRaisesRegex(ValueError, "Advisory diagnostics"):
            validate(modified)

    def test_registered_required_check_contexts_are_unchanged(self):
        # publication-readiness and performance-fast live in other workflows;
        # specification-contract is an Advisory, not a required status.
        import json
        policy = json.loads((ROOT / "governance/required-checks.json").read_text(encoding="utf-8"))
        actual = {record["context"]: record["workflow"] for record in policy["checks"]}
        required = {
            "build-test": ".github/workflows/ci.yml",
            "frontend-test": ".github/workflows/ci.yml",
            "security-scan": ".github/workflows/ci.yml",
            "functional-fast": ".github/workflows/ci.yml",
            "publication-readiness": ".github/workflows/publication-readiness.yml",
            "performance-fast": ".github/workflows/performance-api.yml",
        }
        self.assertEqual(required, actual)
        for context, workflow in required.items():
            if workflow == ".github/workflows/ci.yml":
                self.assertIn(f"    name: {context}", self.source)


if __name__ == "__main__":
    unittest.main()
