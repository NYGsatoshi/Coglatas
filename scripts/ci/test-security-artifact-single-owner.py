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
    if set(diagnostics) != required_diagnostics:
        raise ValueError("Security failure reports must contain only scanner-independent diagnostics")
    if set(evidence).intersection(diagnostics):
        raise ValueError("Overlapping canonical evidence and failure report uploads")
    if "if: always()" not in sec_step or "if-no-files-found: error" not in sec_step:
        raise ValueError("Canonical Security evidence must remain mandatory and fail closed")
    if "if: failure()" not in report_step or "continue-on-error: true" not in report_step:
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

    def test_main_required_check_contexts_are_untouched(self):
        for context in ("build-test", "frontend-test", "security-scan",
                        "functional-fast", "specification-contract", "performance-fast"):
            self.assertIn(f"    name: {context}", self.source)


if __name__ == "__main__":
    unittest.main()
