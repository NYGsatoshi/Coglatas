#!/usr/bin/env python3
"""Repository and negative controls for hosted Ubuntu runner selection."""

from __future__ import annotations

import importlib.util
import tempfile
import unittest
from pathlib import Path

MODULE = Path(__file__).with_name("check-ubuntu-hosted-runner-pins.py")
SPEC = importlib.util.spec_from_file_location("ubuntu_runner_pins", MODULE)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError("Cannot load Ubuntu runner pin guard")
guard = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(guard)


class UbuntuRunnerPinTests(unittest.TestCase):
    def test_repository_has_no_implicit_latest_linux_runner(self) -> None:
        self.assertEqual([], guard.violations())

    def test_direct_latest_linux_runner_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as root:
            workflow = Path(root) / "ci.yml"
            workflow.write_text("jobs:\n  test:\n    runs-on: ubuntu-latest\n", encoding="utf-8")
            errors = guard.violations(Path(root))
            self.assertEqual(1, len(errors))
            self.assertIn("ci.yml:3", errors[0])

    def test_matrix_or_dynamic_latest_linux_runner_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as root:
            workflow = Path(root) / "codeql.yml"
            workflow.write_text(
                "jobs:\n  analyze:\n    runs-on: " + "$" + "{{ matrix.os }}\n"
                "    strategy:\n      matrix:\n        os: [ubuntu-latest, windows-latest]\n",
                encoding="utf-8",
            )
            self.assertEqual(1, len(guard.violations(Path(root))))

    def test_pinned_runner_and_intentional_future_os_probe_are_allowed(self) -> None:
        with tempfile.TemporaryDirectory() as root:
            workflow = Path(root) / "ci.yml"
            workflow.write_text("jobs:\n  test:\n    runs-on: ubuntu-24.04\n", encoding="utf-8")
            probe = Path(root) / "ubuntu26-qualification.yml"
            probe.write_text("jobs:\n  probe:\n    runs-on: ubuntu-26.04\n", encoding="utf-8")
            self.assertEqual([], guard.violations(Path(root)))

    def test_original_three_os_portability_contract_is_exempt(self) -> None:
        with tempfile.TemporaryDirectory() as root:
            portability = Path(root) / "os-portability.yml"
            portability.write_text(
                "jobs:\n  portability:\n    strategy:\n"
                "      matrix:\n        os: [ubuntu-latest, windows-latest, macos-latest]\n"
                "    runs-on: ubuntu-latest\n",
                encoding="utf-8",
            )
            self.assertEqual([], guard.violations(Path(root)))


if __name__ == "__main__":
    unittest.main()
