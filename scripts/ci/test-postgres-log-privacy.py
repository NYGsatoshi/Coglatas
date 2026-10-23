#!/usr/bin/env python3
"""False-green and disclosure controls for isolated PostgreSQL CI diagnostics."""

from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import subprocess
import unittest
from unittest import mock


SPEC = importlib.util.spec_from_file_location("postgres_privacy", Path(__file__).with_name("verify-postgres-log-privacy.py"))
assert SPEC is not None and SPEC.loader is not None
privacy = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(privacy)
ROOT = Path(__file__).resolve().parents[2]
WORKFLOWS = (ROOT / ".github/workflows/ci.yml", ROOT / ".github/workflows/main-validation.yml")


class PrivacyTests(unittest.TestCase):
    def test_every_unsafe_or_missing_effective_setting_fails(self):
        privacy.validate_settings(dict(privacy.EXPECTED))
        for key in privacy.EXPECTED:
            with self.subTest(key=key):
                missing = dict(privacy.EXPECTED)
                del missing[key]
                with self.assertRaises(privacy.PrivacyError):
                    privacy.validate_settings(missing)
                changed = dict(privacy.EXPECTED, **{key: "unsafe"})
                with self.assertRaises(privacy.PrivacyError):
                    privacy.validate_settings(changed)

    def test_detail_statement_and_parameter_canaries_fail_without_echo(self):
        for prefix in ("DETAIL: Failing row contains ", "STATEMENT: INSERT ", "DETAIL: parameters: "):
            for marker in ("private-row", "private-statement"):
                with self.subTest(prefix=prefix, marker=marker):
                    with self.assertRaises(privacy.PrivacyError) as caught:
                        privacy.validate_logs(prefix + marker, "probe", ("private-row", "private-statement"))
                    self.assertNotIn(marker, str(caught.exception))

    def test_empty_unrelated_or_non_error_logs_cannot_supply_positive_control(self):
        for logs in ("", 'ERROR: relation "different" violates check constraint',
                     'LOG: relation "probe" violates check constraint'):
            self.assertFalse(privacy.validate_logs(logs, "probe", ("row", "statement")))
        self.assertTrue(privacy.validate_logs('ERROR: new row for relation "probe" violates check constraint',
                                             "probe", ("private-row", "private-statement")))

    def exercise(self, failure=None):
        calls = []

        def child(arguments, *, sql=None):
            calls.append((arguments, sql))
            if sql and "pg_settings" in sql:
                settings = dict(privacy.EXPECTED)
                if failure == "settings":
                    settings["log_error_verbosity"] = "default"
                return subprocess.CompletedProcess(arguments, 0, json.dumps(settings), "")
            if sql:
                if failure == "probe":
                    return subprocess.CompletedProcess(arguments, 1, "", "protected client output")
                return subprocess.CompletedProcess(arguments, 3, "", "ERROR: 23514")
            table = "ci_log_privacy_" + "a" * 32
            logs = f'ERROR: new row for relation "{table}" violates check constraint'
            if failure == "disclosure":
                logs += "\nDETAIL: ci_private_row_" + "a" * 32
            return subprocess.CompletedProcess(arguments, 1 if failure == "logs" else 0, "", logs)

        with mock.patch.object(privacy, "run", side_effect=child), \
                mock.patch.object(privacy.uuid, "uuid4", return_value=mock.Mock(hex="a" * 32)):
            if failure:
                with self.assertRaises(privacy.PrivacyError):
                    privacy.verify("b" * 64)
            else:
                privacy.verify("b" * 64)
        return calls

    def test_settings_are_verified_before_any_probe_write(self):
        calls = self.exercise("settings")
        self.assertEqual(1, len(calls))
        self.assertIn("pg_settings", calls[0][1])
        self.assertNotIn("CREATE", calls[0][1])

    def test_real_denial_and_live_safe_server_error_are_both_required(self):
        calls = self.exercise()
        self.assertEqual(3, len(calls))
        self.assertIn("CREATE TEMP TABLE", calls[1][1])
        self.assertIn("VERBOSITY sqlstate", calls[1][1])
        self.assertEqual(["docker", "logs", "--since"], calls[2][0][:3])
        for failure in ("probe", "logs", "disclosure"):
            with self.subTest(failure=failure):
                self.exercise(failure)

    def test_silent_logs_fail_after_bounded_retries(self):
        with mock.patch.object(privacy, "run", side_effect=[
                subprocess.CompletedProcess([], 0, json.dumps(privacy.EXPECTED), ""),
                subprocess.CompletedProcess([], 3, "", "23514"),
                *[subprocess.CompletedProcess([], 0, "", "") for _ in range(20)]]), \
                mock.patch.object(privacy.time, "sleep"):
            with self.assertRaisesRegex(privacy.PrivacyError, "positive control"):
                privacy.verify("b" * 64)

    def test_invalid_container_never_executes(self):
        with mock.patch.object(privacy, "run") as child:
            with self.assertRaises(privacy.PrivacyError):
                privacy.verify("--privileged")
            child.assert_not_called()

    def test_failed_subprocess_output_is_not_published(self):
        with mock.patch.object(privacy.subprocess, "run", side_effect=subprocess.TimeoutExpired(
                ["private-command"], 30, output="private-output", stderr="private-stderr")):
            with self.assertRaises(privacy.PrivacyError) as caught:
                privacy.run(["docker"])
        self.assertNotIn("private", str(caught.exception))

    def test_workflows_configure_startup_and_gate_migrations(self):
        for path in WORKFLOWS:
            with self.subTest(path=path.name):
                text = path.read_text(encoding="utf-8")
                self.assertIn("--set=log_error_verbosity=terse", text)
                self.assertIn("--set=log_min_error_statement=panic", text)
                self.assertIn("--set=log_parameter_max_length=0", text)
                self.assertIn("--set=log_parameter_max_length_on_error=0", text)
                guard = text.index("- name: Verify PostgreSQL diagnostic privacy")
                migration = text.index("- name: Apply PostgreSQL migrations")
                self.assertLess(guard, migration)
                guard_step = text[guard:text.index("\n      - name:", guard + 1)]
                self.assertNotIn("if:", guard_step)
                self.assertNotIn("continue-on-error:", guard_step)
                self.assertIn("python3 -B scripts/ci/verify-postgres-log-privacy.py", guard_step)
                self.assertIn("Test PostgreSQL diagnostic privacy controls", text)


if __name__ == "__main__":
    unittest.main()
