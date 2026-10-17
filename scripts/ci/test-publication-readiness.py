#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
import unittest
from pathlib import Path

MODULE_PATH = Path(__file__).with_name("check-publication-readiness.py")
SPEC = importlib.util.spec_from_file_location("publication_guard", MODULE_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError("Unable to load publication-readiness guard")
guard = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(guard)


class WorkflowParserTests(unittest.TestCase):
    def errors(self, text: str, path: str = "test.yml") -> list[str]:
        return guard.workflow_errors(
            guard.ROOT / ".github" / "workflows" / path, text
        )

    def test_block_pull_request_trigger(self) -> None:
        text = """
name: test
on:
  push:
  pull_request:
jobs: {}
"""
        self.assertTrue(guard.workflow_triggers(text, "pull_request"))

    def test_quoted_pull_request_trigger(self) -> None:
        text = """
on:
  "pull_request":
jobs: {}
"""
        self.assertTrue(guard.workflow_triggers(text, "pull_request"))

    def test_inline_trigger(self) -> None:
        self.assertTrue(
            guard.workflow_triggers("on: [push, pull_request]\njobs: {}\n", "pull_request")
        )

    def test_canonical_sonarcloud_pr_secret_is_allowed(self) -> None:
        text = """
on:
  pull_request:
    branches: [main]
permissions:
  contents: read
jobs:
  analyze:
    if: github.event.pull_request.head.repo.full_name == github.repository
    runs-on: ubuntu-latest
    environment: sonarcloud-pr-analysis
    steps:
      - uses: actions/checkout@approved
        with:
          persist-credentials: false
      - env:
          SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
      - env:
          SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
      - env:
          SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
"""
        self.assertEqual([], self.errors(text, "sonarcloud-pr.yml"))

    def test_sonarcloud_pr_wrong_environment_is_rejected(self) -> None:
        text = self._canonical_sonar_pr().replace(
            "environment: sonarcloud-pr-analysis",
            "environment: other",
        )
        errors = self.errors(text, "sonarcloud-pr.yml")
        self.assertTrue(any("references or inherits a secret" in error for error in errors))

    def test_sonarcloud_pr_missing_same_repo_guard_is_rejected(self) -> None:
        text = self._canonical_sonar_pr().replace(
            "    if: github.event.pull_request.head.repo.full_name == github.repository\n",
            "",
        )
        errors = self.errors(text, "sonarcloud-pr.yml")
        self.assertTrue(any("references or inherits a secret" in error for error in errors))

    def test_sonarcloud_pr_extra_secret_is_rejected(self) -> None:
        text = self._canonical_sonar_pr().replace(
            "      - env:\n          SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}\n      - env:",
            "      - env:\n          SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}\n          EXTRA: ${{ secrets.EXTRA }}\n      - env:",
        )
        errors = self.errors(text, "sonarcloud-pr.yml")
        self.assertTrue(any("references or inherits a secret" in error for error in errors))

    def _canonical_sonar_pr(self) -> str:
        return """
on:
  pull_request:
    branches: [main]
permissions:
  contents: read
jobs:
  analyze:
    if: github.event.pull_request.head.repo.full_name == github.repository
    runs-on: ubuntu-latest
    environment: sonarcloud-pr-analysis
    steps:
      - uses: actions/checkout@approved
        with:
          persist-credentials: false
      - env:
          SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
      - env:
          SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
      - env:
          SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
"""

    def test_manual_only_secret_requires_same_job_environment(self) -> None:
        text = """
on:
  workflow_dispatch:
jobs:
  licensed:
    runs-on: ubuntu-latest
    environment: licensed
    env:
      VALUE: ${{ secrets.VALUE }}
"""
        self.assertEqual([], self.errors(text))

    def test_environment_in_other_job_does_not_protect_secret(self) -> None:
        text = """
on:
  workflow_dispatch:
jobs:
  protected:
    runs-on: ubuntu-latest
    environment: licensed
  unsafe:
    runs-on: ubuntu-latest
    env:
      VALUE: ${{ secrets.VALUE }}
"""
        errors = self.errors(text)
        self.assertTrue(any("secret-bearing job 'unsafe'" in error for error in errors))

    def test_dynamic_environment_does_not_protect_secret(self) -> None:
        text = """
on:
  workflow_dispatch:
jobs:
  unsafe:
    runs-on: ubuntu-latest
    environment: ${{ github.ref_name }}
    env:
      VALUE: ${{ secrets.VALUE }}
"""
        errors = self.errors(text)
        self.assertTrue(any("lacks a static protected environment" in error for error in errors))

    def test_pull_request_bracket_secret_is_rejected(self) -> None:
        text = """
on:
  pull_request:
jobs:
  unsafe:
    runs-on: ubuntu-latest
    environment: licensed
    env:
      VALUE: ${{ secrets['VALUE'] }}
"""
        errors = self.errors(text)
        self.assertTrue(any("references or inherits a secret" in error for error in errors))

    def test_pull_request_inherited_secrets_are_rejected(self) -> None:
        text = """
on:
  pull_request:
jobs:
  unsafe:
    uses: owner/repo/.github/workflows/reusable.yml@main
    secrets: inherit
"""
        errors = self.errors(text)
        self.assertTrue(any("references or inherits a secret" in error for error in errors))

    def test_multiline_self_hosted_is_rejected(self) -> None:
        text = """
on:
  workflow_dispatch:
jobs:
  unsafe:
    runs-on:
      - self-hosted
      - linux
"""
        errors = self.errors(text)
        self.assertTrue(any("self-hosted" in error for error in errors))

    def test_inline_self_hosted_is_rejected(self) -> None:
        text = """
on:
  workflow_dispatch:
jobs:
  unsafe:
    runs-on: [self-hosted, linux]
"""
        errors = self.errors(text)
        self.assertTrue(any("self-hosted" in error for error in errors))

    def test_pull_request_inline_write_permission_is_rejected(self) -> None:
        text = """
on: [pull_request]
permissions: {contents: read, pull-requests: write}
jobs:
  safe:
    runs-on: ubuntu-latest
"""
        errors = self.errors(text)
        self.assertTrue(any("write permission" in error for error in errors))

    def test_pull_request_job_write_permission_is_rejected(self) -> None:
        text = """
on:
  pull_request:
permissions:
  contents: read
jobs:
  unsafe:
    runs-on: ubuntu-latest
    permissions:
      contents: write
"""
        errors = self.errors(text)
        self.assertTrue(any("write permission" in error for error in errors))

    def test_codeql_analyze_security_events_write_is_allowed(self) -> None:
        text = """
on:
  pull_request:
permissions:
  contents: read
jobs:
  analyze:
    runs-on: ubuntu-latest
    permissions:
      security-events: write
      contents: read
"""
        self.assertEqual([], self.errors(text, "codeql.yml"))

    def test_codeql_other_write_permission_is_rejected(self) -> None:
        text = """
on:
  pull_request:
permissions:
  contents: read
jobs:
  analyze:
    runs-on: ubuntu-latest
    permissions:
      security-events: write
      contents: write
"""
        errors = self.errors(text, "codeql.yml")
        self.assertTrue(any("write permission" in error for error in errors))

    def test_security_events_write_in_other_workflow_is_rejected(self) -> None:
        text = """
on:
  pull_request:
jobs:
  analyze:
    runs-on: ubuntu-latest
    permissions:
      security-events: write
"""
        errors = self.errors(text)
        self.assertTrue(any("write permission" in error for error in errors))

    def test_security_events_write_in_other_codeql_job_is_rejected(self) -> None:
        text = """
on:
  pull_request:
jobs:
  publish:
    runs-on: ubuntu-latest
    permissions:
      security-events: write
"""
        errors = self.errors(text, "codeql.yml")
        self.assertTrue(any("write permission" in error for error in errors))

    def test_pull_request_target_is_rejected(self) -> None:
        text = """
on:
  pull_request_target:
jobs:
  unsafe:
    runs-on: ubuntu-latest
"""
        errors = self.errors(text)
        self.assertTrue(any("pull_request_target" in error for error in errors))

    def test_gitleaks_history_scans_are_bound_to_checked_out_ref(self) -> None:
        for workflow_name in ("ci.yml", "publication-readiness.yml"):
            workflow = (guard.WORKFLOW_DIR / workflow_name).read_text(encoding="utf-8")
            with self.subTest(workflow=workflow_name):
                self.assertIn('scan_log_opts="$GITHUB_SHA"', workflow)
                self.assertIn(
                    'scan_log_opts="${merge_base}..${PR_HEAD_SHA}"',
                    workflow,
                )
                self.assertIn('--log-opts="$scan_log_opts"', workflow)


if __name__ == "__main__":
    unittest.main()
