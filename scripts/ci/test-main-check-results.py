#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


MODULE = Path(__file__).with_name("validate-main-check-results.py")
SPEC = importlib.util.spec_from_file_location("main_check_results", MODULE)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError("Unable to load the Main check result guard")
guard = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(guard)
SHA = "a" * 40


def execution():
    return {"GITHUB_EVENT_NAME": "push", "GITHUB_REF": "refs/heads/main",
            "GITHUB_REPOSITORY": "NYGsatoshi/Coglatas", "GITHUB_SHA": SHA,
            "GITHUB_WORKFLOW_REF": "NYGsatoshi/Coglatas/.github/workflows/main-build-artifacts.yml@refs/heads/main",
            "GITHUB_RUN_ID": "42", "GITHUB_RUN_ATTEMPT": "1"}


def needs(context):
    return {name: {"result": "success", "outputs": {"ignored": "sensitive output"}}
            for name in guard.PREREQUISITES[context]}


class MainCheckResultTests(unittest.TestCase):
    def test_structural_failure_and_missing_dependency_block_main_build(self):
        candidate = needs("build-test")
        self.assertIn("performance-db", candidate)
        for result in ("failure", "skipped", "cancelled"):
            candidate["performance-db"]["result"] = result
            with self.subTest(result=result), self.assertRaises(ValueError):
                guard.validate_main_results("build-test", candidate, execution())
        del candidate["performance-db"]
        with self.assertRaises(ValueError):
            guard.validate_main_results("build-test", candidate, execution())

    def test_actual_success_preserves_exact_execution_and_only_safe_results(self):
        for context in guard.PREREQUISITES:
            with self.subTest(context=context):
                identity = guard.validate_main_results(context, needs(context), execution())
                self.assertEqual(SHA, identity["sourceSha"])
                self.assertEqual(42, identity["workflowRunId"])
                self.assertEqual(1, identity["workflowRunAttempt"])
                self.assertEqual("PASS", identity["decision"])
                self.assertEqual(set(guard.PREREQUISITES[context]), set(identity["prerequisiteResults"]))
                self.assertNotIn("sensitive output", json.dumps(identity))

    def test_every_failed_cancelled_skipped_pending_missing_or_unknown_result_rejects(self):
        for context in guard.PREREQUISITES:
            for job in guard.PREREQUISITES[context]:
                for result in ("failure", "cancelled", "skipped", "neutral", "timed_out",
                               "pending", "in_progress", "success ", "unknown", None, True):
                    candidate = needs(context)
                    candidate[job]["result"] = result
                    with self.subTest(context=context, job=job, result=result), self.assertRaises(ValueError):
                        guard.validate_main_results(context, candidate, execution())
                for item in ({}, None, "success"):
                    candidate = needs(context)
                    candidate[job] = item
                    with self.subTest(context=context, job=job, item=item), self.assertRaises(ValueError):
                        guard.validate_main_results(context, candidate, execution())

    def test_successful_producer_without_real_validation_cannot_pass(self):
        for context in ("build-test", "frontend-test", "security-scan"):
            candidate = needs(context)
            del candidate["main-validation"]
            with self.subTest(context=context), self.assertRaises(ValueError):
                guard.validate_main_results(context, candidate, execution())
        candidate = needs("functional-fast")
        del candidate["functional-fast-domains"]
        with self.assertRaises(ValueError):
            guard.validate_main_results("functional-fast", candidate, execution())

    def test_dependency_substitution_extra_jobs_and_non_object_inputs_reject(self):
        for context in guard.PREREQUISITES:
            candidate = needs(context)
            candidate.pop(next(iter(candidate)))
            candidate["unrelated-green-job"] = {"result": "success"}
            for item in (candidate, needs(context) | {"extra": {"result": "success"}}, [], None):
                with self.subTest(context=context, item=item), self.assertRaises(ValueError):
                    guard.validate_main_results(context, item, execution())
        with self.assertRaises(ValueError):
            guard.validate_main_results("functional-full", {}, execution())

    def test_each_execution_records_the_actual_workflow_attempt(self):
        for attempt in ("1", "2", "19"):
            with self.subTest(attempt=attempt):
                identity = guard.validate_main_results("build-test", needs("build-test"),
                                                       execution() | {"GITHUB_RUN_ATTEMPT": attempt})
                self.assertEqual(int(attempt), identity["workflowRunAttempt"])
                self.assertEqual(42, identity["workflowRunId"])
                self.assertEqual(SHA, identity["sourceSha"])

    def test_foreign_pr_manual_non_main_stale_workflow_and_invalid_execution_rejects(self):
        changes = ({"GITHUB_EVENT_NAME": "pull_request"}, {"GITHUB_EVENT_NAME": "workflow_dispatch"},
                   {"GITHUB_REF": "refs/heads/other"}, {"GITHUB_REPOSITORY": "fork/Coglatas"},
                   {"GITHUB_WORKFLOW_REF": "NYGsatoshi/Coglatas/.github/workflows/ci.yml@refs/heads/main"},
                   {"GITHUB_SHA": "a" * 7}, {"GITHUB_SHA": "$(unsafe)"}, {"GITHUB_RUN_ID": "0"},
                   {"GITHUB_RUN_ID": "missing"}, {"GITHUB_RUN_ATTEMPT": "0"}, {"GITHUB_RUN_ATTEMPT": ""},
                   {"GITHUB_RUN_ATTEMPT": "missing"}, {"GITHUB_RUN_ATTEMPT": "01"})
        for change in changes:
            with self.subTest(change=change), self.assertRaises(ValueError):
                guard.validate_main_results("build-test", needs("build-test"), execution() | change)
        missing = execution()
        del missing["GITHUB_RUN_ATTEMPT"]
        with self.assertRaises(ValueError):
            guard.validate_main_results("build-test", needs("build-test"), missing)

    def test_cli_returns_nonzero_for_missing_malformed_and_failed_evidence_without_echoing_outputs(self):
        for raw in ("not JSON", "null", json.dumps(needs("build-test") | {"main-validation": {"result": "failure"}})):
            result = subprocess.run([sys.executable, str(MODULE)],
                                    env=execution() | {"CHECK_CONTEXT": "build-test", "NEEDS_JSON": raw},
                                    capture_output=True, text=True, check=False)
            with self.subTest(raw=raw):
                self.assertNotEqual(0, result.returncode)
                self.assertNotIn("sensitive output", result.stdout + result.stderr)
                self.assertNotIn('"decision": "PASS"', result.stdout)

    def test_cli_records_attempt_two_in_identity_and_safe_summary(self):
        with tempfile.TemporaryDirectory() as temporary:
            summary = Path(temporary) / "summary.md"
            result = subprocess.run([sys.executable, str(MODULE)],
                                    env=execution() | {"CHECK_CONTEXT": "build-test",
                                                       "NEEDS_JSON": json.dumps(needs("build-test")),
                                                       "GITHUB_RUN_ATTEMPT": "2", "GITHUB_STEP_SUMMARY": str(summary)},
                                    capture_output=True, text=True, check=False)
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertEqual(2, json.loads(result.stdout)["workflowRunAttempt"])
            text = summary.read_text(encoding="utf-8")
            self.assertIn(f"Exact SHA `{SHA}`, run 42, attempt 2.", text)
            self.assertNotIn("sensitive output", result.stdout + text)


if __name__ == "__main__":
    unittest.main()
