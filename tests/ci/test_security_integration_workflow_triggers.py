#!/usr/bin/env python3
"""Ensure security scans cover the existing test-integration branch.

Branch-filter regressions can silently omit CodeQL/OSV/Trivy from PRs targeting
SecurityTestImplimation. Do not change required Main checks, permissions, scan
severity, or scheduling as part of this contract.
"""

from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TARGETS = frozenset({"main", "SecurityTestImplimation"})
REQUIRED = {
    "codeql.yml": ("pull_request", "push"),
    "osv-scanner.yml": ("pull_request", "merge_group", "push"),
    "trivy-repository.yml": ("pull_request", "push"),
    "workflow-static-analysis.yml": ("pull_request", "push"),
    "security-misconfiguration.yml": ("pull_request", "push"),
    "socket-dependency-security.yml": ("pull_request", "push"),
    "legacy-name-guard.yml": ("push",),
}
ROUTING_TEST = "tests/ci/test_security_integration_workflow_triggers.py"


def parse_on_events(text: str) -> dict[str, str]:
    """Read top-level trigger sections without requiring an extra YAML package."""
    lines = text.splitlines()
    try:
        start = next(i for i, line in enumerate(lines) if line == "on:")
    except StopIteration as exc:
        raise ValueError("Missing GitHub Actions on: block") from exc

    sections: dict[str, list[str]] = {}
    event = None
    for line in lines[start + 1 :]:
        if line.strip() and not line[0].isspace() and not line.startswith("#"):
            break
        match = re.fullmatch(r"  ([a-z_]+):(?:\s*#.*)?", line)
        if match:
            event = match.group(1)
            if event in sections:
                raise ValueError(f"Duplicate event: {event}")
            sections[event] = []
        elif event is not None:
            sections[event].append(line)

    return {name: "\n".join(value) for name, value in sections.items()}


def branch_set(events: dict[str, str], event: str) -> set[str]:
    if event not in events:
        raise ValueError(f"Missing {event} event")
    match = re.search(r"^    branches:\s*\[([^\]\n]+)\]\s*(?:#.*)?$", events[event], re.MULTILINE)
    if match is None:
        raise ValueError(f"Missing explicit branch restriction for {event}")
    branches = [value.strip().strip("'\"") for value in match.group(1).split(",")]
    if not branches or len(branches) != len(set(branches)) or any(not name for name in branches):
        raise ValueError(f"Invalid/duplicate branches for {event}")
    return set(branches)


class SecurityIntegrationScanTriggerTests(unittest.TestCase):
    def test_reviewed_security_scanners_cover_both_branches(self):
        for filename, required_events in REQUIRED.items():
            with self.subTest(workflow=filename):
                source = (ROOT / ".github/workflows" / filename).read_text(encoding="utf-8")
                events = parse_on_events(source)
                for event in required_events:
                    with self.subTest(event=event):
                        self.assertEqual(branch_set(events, event), TARGETS)

    def test_codeql_keeps_pr_change_filter_and_languages(self):
        text = (ROOT / ".github/workflows/codeql.yml").read_text(encoding="utf-8")
        events = parse_on_events(text)
        self.assertIn("    paths:\n", events["pull_request"] + "\n")
        for language in ("actions", "csharp", "javascript-typescript", "python"):
            self.assertIn(f"- language: {language}", text)
        self.assertIn("security-events: write", text)

    def test_osv_keeps_base_candidate_comparison_and_advisory_inventory(self):
        text = (ROOT / ".github/workflows/osv-scanner.yml").read_text(encoding="utf-8")
        self.assertIn("--old=base-results.json", text)
        self.assertIn("--new=candidate-results.json", text)
        self.assertIn("--fail-on-vuln=true", text)
        self.assertIn("--fail-on-vuln=false", text)

    def test_trivy_remains_report_only_not_false_enforcement(self):
        text = (ROOT / ".github/workflows/trivy-repository.yml").read_text(encoding="utf-8")
        self.assertIn("scanners: vuln", text)
        self.assertIn('exit-code: "0"', text)

    def test_static_analysis_owns_parity_test_on_both_events(self):
        text = (ROOT / ".github/workflows/workflow-static-analysis.yml").read_text(encoding="utf-8")
        events = parse_on_events(text)
        for event in ("pull_request", "push"):
            with self.subTest(event=event):
                self.assertIn(f'      - "{ROUTING_TEST}"', events[event])
                self.assertIn("    paths:\n", events[event] + "\n")
        self.assertIn(f"python3 -B {ROUTING_TEST}", text)
        self.assertIn("actionlint", text)
        self.assertIn("zizmor", text)

    def test_legacy_guard_preserves_unfiltered_pr_and_merge_queue(self):
        source = (ROOT / ".github/workflows/legacy-name-guard.yml").read_text(encoding="utf-8")
        events = parse_on_events(source)
        self.assertIn("pull_request", events)
        self.assertIn("merge_group", events)
        self.assertEqual(branch_set(events, "push"), TARGETS)

    def test_missing_integration_branch_is_rejected(self):
        example = {"pull_request": "    branches: [main]\n"}
        self.assertNotEqual(branch_set(example, "pull_request"), TARGETS)

    def test_missing_explicit_filter_is_rejected(self):
        with self.assertRaises(ValueError):
            branch_set({"push": ""}, "push")
        with self.assertRaises(ValueError):
            branch_set({}, "pull_request")

    def test_duplicate_event_is_rejected(self):
        with self.assertRaises(ValueError):
            parse_on_events("name: example\non:\n  push:\n    branches: [main]\n  push:\n    branches: [main]\njobs:\n")


if __name__ == "__main__":
    unittest.main()
