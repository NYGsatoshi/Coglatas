#!/usr/bin/env python3
"""Run the actual required Functional gate Bash snippet against safe/unsafe cases.

The routing helper has a reviewed 'validated-ci-tooling-only' exemption.
The required aggregate must recognize exactly that exemption while rejecting
unknown reasons, missing producer evidence, failed prerequisites and skips
when Functional is selected. No real stack, network, privilege or Docker.
"""
from __future__ import annotations

import os
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/ci.yml"


def required_gate_command() -> str:
    source = WORKFLOW.read_text(encoding="utf-8")
    marker = "\n  functional-fast:\n"
    tail = "\n  build-test:\n"
    if source.count(marker) != 1:
        raise AssertionError("functional-fast required job is missing or duplicated")
    _, remainder = source.split(marker, 1)
    if remainder.count(tail) != 1:
        raise AssertionError("Functional gate boundary changed")
    block, _ = remainder.split(tail, 1)
    # Use the actual required gate, not a separately copied approximation.
    step = "        run: |\n"
    if block.count(step) != 1:
        raise AssertionError("Required Functional gate Bash step changed")
    after = block.split(step, 1)[1]
    commands: list[str] = []
    for line in after.splitlines():
        if line.startswith("          "):
            commands.append(line[10:])
        elif not line.strip():
            commands.append("")
        else:
            raise AssertionError("Unexpected required gate shell indentation")
    if not commands or not commands[0].startswith("set -euo pipefail"):
        raise AssertionError("Functional gate must retain fail-closed Bash execution")
    return "\n".join(commands) + "\n"


class RequiredFunctionalFastGateTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.command = required_gate_command()

    def invoke(
        self, required: str, reason: str, runtime: str, functional: str,
        route: str = "success",
    ) -> tuple[int, str]:
        with tempfile.TemporaryDirectory() as td:
            summary = Path(td) / "summary.md"
            env = dict(os.environ)
            env.update({
                "ROUTE_RESULT": route,
                "REQUIRED": required,
                "ROUTING_REASON": reason,
                "RUNTIME_RESULT": runtime,
                "FUNCTIONAL_RESULT": functional,
                "PR_HEAD_SHA": "a" * 40,
                "GITHUB_SHA": "b" * 40,
                "GITHUB_STEP_SUMMARY": str(summary),
            })
            proc = subprocess.run(
                ["bash", "-e", "-o", "pipefail", "-c", self.command],
                cwd=ROOT, env=env, capture_output=True, timeout=8, check=False,
            )
            return proc.returncode, summary.read_text(encoding="utf-8") if summary.exists() else ""

    def test_genuine_functional_execution_requires_both_successful_owners(self) -> None:
        rc, summary = self.invoke("true", "runtime-or-unclassified-change", "success", "success")
        self.assertEqual(0, rc)
        self.assertNotIn("NOT_APPLICABLE", summary)

    def test_documentation_only_exemption_preserves_original_contract(self) -> None:
        rc, summary = self.invoke("false", "validated-documentation-only", "skipped", "skipped")
        self.assertEqual(0, rc)
        self.assertIn("NOT_APPLICABLE: validated documentation-only change", summary)

    def test_reviewed_static_ci_tooling_exemption_requires_both_skips(self) -> None:
        rc, summary = self.invoke("false", "validated-ci-tooling-only", "skipped", "skipped")
        self.assertEqual(0, rc)
        self.assertIn("NOT_APPLICABLE: validated static CI-tooling-only change", summary)

    def test_bad_exemption_reason_or_missing_route_rejected(self) -> None:
        for reason in (
            "", "unknown-diff-run-all", "runtime-or-unclassified-change",
            "validated-ci-tooling-only\nuntrusted", "VALIDATED-CI-TOOLING-ONLY",
            "validated-ci-tooling-only ", "skip-all", "security-only",
        ):
            with self.subTest(reason=reason):
                self.assertNotEqual(
                    0, self.invoke("false", reason, "skipped", "skipped")[0],
                )
        for route in ("", "failure", "skipped", "cancelled", "neutral"):
            with self.subTest(route=route):
                self.assertNotEqual(
                    0, self.invoke("false", "validated-ci-tooling-only", "skipped", "skipped", route)[0],
                )

    def test_missing_or_failed_functional_owners_rejected(self) -> None:
        for reason in ("validated-documentation-only", "validated-ci-tooling-only"):
            for runtime, functional in (
                ("success", "skipped"),
                ("skipped", "success"),
                ("failure", "skipped"),
                ("skipped", "failure"),
                ("cancelled", "skipped"),
                ("skipped", "cancelled"),
                ("", "skipped"),
                ("skipped", ""),
            ):
                with self.subTest(reason=reason, runtime=runtime, functional=functional):
                    self.assertNotEqual(0, self.invoke("false", reason, runtime, functional)[0])

    def test_required_owner_failures_never_use_exemption(self) -> None:
        for required in ("true", "", "unknown"):
            for runtime, functional in (("skipped", "skipped"), ("failure", "success"), ("success", "failure")):
                with self.subTest(required=required, runtime=runtime, functional=functional):
                    self.assertNotEqual(
                        0,
                        self.invoke(required, "validated-ci-tooling-only", runtime, functional)[0],
                    )


if __name__ == "__main__":
    unittest.main()
