#!/usr/bin/env python3
"""Regression tests for the Code Scanning inventory sanitizer."""

from __future__ import annotations

import importlib.util
import pathlib
import unittest

MODULE_PATH = pathlib.Path(__file__).with_name("code-scanning-inventory.py")
SPEC = importlib.util.spec_from_file_location("code_scanning_inventory", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class CodeScanningInventoryTests(unittest.TestCase):
    def sample_alert(self) -> dict:
        return {
            "number": 17,
            "state": "open",
            "created_at": "2026-09-09T00:00:00Z",
            "updated_at": "2026-09-09T00:01:00Z",
            "html_url": "https://github.com/example/repo/security/code-scanning/17",
            "rule": {
                "id": "cs/web/unvalidated-url",
                "severity": "error",
                "security_severity_level": "high",
                "tags": ["security", "external/cwe/cwe-918"],
                "help": "MUST_NOT_SURVIVE secret-like diagnostic text",
            },
            "tool": {"name": "CodeQL", "version": "2.23.0"},
            "most_recent_instance": {
                "classifications": [],
                "message": {"text": "MUST_NOT_SURVIVE bearer-token-like evidence"},
                "location": {
                    "path": "src/Coglatas.Web/Example.cs",
                    "start_line": 42,
                    "end_line": 42,
                },
                "commit_sha": "deadbeef",
            },
        }

    def test_sanitizer_keeps_only_remediation_metadata(self) -> None:
        sanitized = MODULE.sanitize_alert(self.sample_alert())
        self.assertEqual(sanitized["number"], 17)
        self.assertEqual(sanitized["rule_id"], "cs/web/unvalidated-url")
        self.assertEqual(sanitized["language"], "csharp")
        self.assertEqual(sanitized["security_severity"], "high")
        self.assertEqual(sanitized["cwes"], ["CWE-918"])
        serialized = repr(sanitized)
        self.assertNotIn("MUST_NOT_SURVIVE", serialized)
        self.assertNotIn("message", sanitized)
        self.assertNotIn("help", sanitized)
        self.assertNotIn("commit_sha", sanitized)

    def test_inventory_uses_security_severity_and_groups_rules(self) -> None:
        first = self.sample_alert()
        second = self.sample_alert()
        second["number"] = 18
        second["rule"] = dict(second["rule"])
        second["rule"]["security_severity_level"] = "medium"
        second["most_recent_instance"] = dict(second["most_recent_instance"])
        second["most_recent_instance"]["location"] = {
            "path": "src/Coglatas.Web/Other.cs",
            "start_line": 7,
        }

        inventory = MODULE.build_inventory(
            [second, first], repository="example/repo", ref="refs/heads/main"
        )
        self.assertEqual(inventory["open_alert_count"], 2)
        self.assertEqual(inventory["counts"]["by_severity"], {"high": 1, "medium": 1})
        self.assertEqual(
            inventory["counts"]["by_rule"]["CodeQL::csharp::cs/web/unvalidated-url"],
            2,
        )
        self.assertEqual(inventory["alerts"][0]["number"], 17)

    def test_markdown_does_not_render_alert_evidence(self) -> None:
        inventory = MODULE.build_inventory(
            [self.sample_alert()], repository="example/repo", ref="refs/heads/main"
        )
        markdown = MODULE.render_markdown(inventory)
        self.assertIn("#17", markdown)
        self.assertIn("cs/web/unvalidated-url", markdown)
        self.assertIn("Example.cs:42", markdown)
        self.assertNotIn("MUST_NOT_SURVIVE", markdown)

    def test_actions_workflow_path_infers_actions_language(self) -> None:
        alert = self.sample_alert()
        alert["rule"] = dict(alert["rule"])
        alert["rule"]["id"] = "unknown-rule"
        alert["most_recent_instance"] = dict(alert["most_recent_instance"])
        alert["most_recent_instance"]["location"] = {
            "path": ".github/workflows/security.yml",
            "start_line": 12,
        }
        self.assertEqual(MODULE.sanitize_alert(alert)["language"], "actions")


if __name__ == "__main__":
    unittest.main()
