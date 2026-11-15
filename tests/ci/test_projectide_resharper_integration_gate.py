#!/usr/bin/env python3
"""Synthetic refusal controls for P0 #1954's exact-source technical gate."""
from __future__ import annotations

import copy
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))

from scripts.ci.projectide_resharper_integration_gate import (  # noqa: E402
    CHECK_APP, REQUIRED, REQUIRED_STEPS, RESHARPER_JOB, evaluate,
)

REPOSITORY = "NYGsatoshi/Coglatas"
HEAD = "a" * 40
BASE = "b" * 40
RUN_ID = 43593444836
ATTEMPT = 1
PR_NUMBER = 1946


def snapshot() -> dict:
    checks = [{
        "name": name, "head_sha": HEAD, "status": "completed",
        "conclusion": "success", "app": {"id": CHECK_APP},
    } for name in REQUIRED]
    steps = [{
        "name": name, "status": "completed", "conclusion": "success",
        "completed_at": "2026-11-14T12:54:25Z",
    } for name in REQUIRED_STEPS]
    job = {
        "name": RESHARPER_JOB, "run_id": RUN_ID, "head_sha": HEAD,
        "status": "completed", "conclusion": "success", "steps": steps,
    }
    return {
        "pr": {"number": PR_NUMBER, "state": "open", "draft": False,
               "merged": False,
               "head": {"sha": HEAD, "ref": "projectide/typed-project-tests-20261114",
                        "repo": {"full_name": REPOSITORY}},
               "base": {"sha": BASE, "ref": "projectide/integration-20261031"}},
        "checks": {"total_count": len(checks), "check_runs": checks},
        "run": {"id": RUN_ID, "run_attempt": ATTEMPT, "event": "pull_request",
                "name": "PR CI", "head_sha": HEAD,
                "head_branch": "projectide/typed-project-tests-20261114",
                "status": "completed", "conclusion": "success"},
        "jobs": {"total_count": 1, "jobs": [job]},
    }


def evaluate_sample(value: dict):
    return evaluate(value, REPOSITORY, PR_NUMBER, HEAD, BASE, RUN_ID, ATTEMPT)


class ReSharperPremergeGateTests(unittest.TestCase):
    def test_exact_head_base_attempt_six_checks_and_independent_probe_can_qualify(self):
        decision = evaluate_sample(snapshot())
        self.assertTrue(decision.qualified)
        self.assertEqual("TECHNICAL_CI_EVIDENCE_QUALIFIED", decision.code)

    def test_six_green_checks_with_running_resharper_job_are_refused(self):
        value = snapshot()
        value["jobs"]["jobs"][0]["status"] = "in_progress"
        value["jobs"]["jobs"][0]["conclusion"] = None
        self.assertEqual("RESHARPER_JOB_INCOMPLETE", evaluate_sample(value).code)

    def test_six_green_checks_with_missing_or_cancelled_independent_probe_are_refused(self):
        for mutation in ("missing", "cancelled"):
            with self.subTest(mutation=mutation):
                value = snapshot()
                steps = value["jobs"]["jobs"][0]["steps"]
                if mutation == "missing":
                    steps[:] = [item for item in steps if
                                item["name"] != "Verify independent inspection scope and complete SARIF"]
                    self.assertEqual("RESHARPER_PROBE_MISSING", evaluate_sample(value).code)
                else:
                    steps[2]["status"] = "completed"
                    steps[2]["conclusion"] = "cancelled"
                    self.assertEqual("RESHARPER_PROBE_INCOMPLETE", evaluate_sample(value).code)

    def test_finished_probe_without_final_changed_file_gate_is_refused(self):
        value = snapshot()
        value["jobs"]["jobs"][0]["steps"][3]["status"] = "in_progress"
        value["jobs"]["jobs"][0]["steps"][3]["conclusion"] = None
        self.assertEqual("RESHARPER_PROBE_INCOMPLETE", evaluate_sample(value).code)

    def test_wrong_pr_head_or_base_ref_and_source_prevent_old_source_credit(self):
        for field in ("head", "base", "target"):
            with self.subTest(field=field):
                value = snapshot()
                if field == "head":
                    value["pr"]["head"]["sha"] = "c" * 40
                    expected = "HEAD_DRIFT"
                elif field == "base":
                    value["pr"]["base"]["sha"] = "c" * 40
                    expected = "BASE_DRIFT"
                else:
                    value["pr"]["base"]["ref"] = "main"
                    expected = "BASE_DRIFT"
                self.assertEqual(expected, evaluate_sample(value).code)

    def test_wrong_run_attempt_or_head_is_not_reused(self):
        for kind in ("attempt", "head", "branch"):
            with self.subTest(kind=kind):
                value = snapshot()
                if kind == "attempt":
                    value["run"]["run_attempt"] += 1
                elif kind == "head":
                    value["run"]["head_sha"] = "c" * 40
                else:
                    value["run"]["head_branch"] = "projectide/unrelated"
                self.assertEqual("CI_PROVENANCE_MISMATCH", evaluate_sample(value).code)

    def test_wrong_app_failed_required_check_and_duplicate_producer_are_refused(self):
        for kind in ("wrong-app", "failed", "duplicate"):
            with self.subTest(kind=kind):
                value = snapshot()
                checks = value["checks"]["check_runs"]
                if kind == "wrong-app":
                    checks[0]["app"]["id"] = 1
                    expected = "REQUIRED_CHECK_INCOMPLETE"
                elif kind == "failed":
                    checks[0]["conclusion"] = "failure"
                    expected = "REQUIRED_CHECK_INCOMPLETE"
                else:
                    checks.append(copy.deepcopy(checks[0]))
                    value["checks"]["total_count"] += 1
                    expected = "REQUIRED_CHECK_AMBIGUOUS"
                self.assertEqual(expected, evaluate_sample(value).code)

    def test_incomplete_check_or_job_pages_refuse_partial_api_results(self):
        for key in ("checks", "jobs"):
            with self.subTest(key=key):
                value = snapshot()
                value[key]["total_count"] += 1
                expected = "INCOMPLETE_CHECK_PAGES" if key == "checks" else "INCOMPLETE_JOB_PAGES"
                self.assertEqual(expected, evaluate_sample(value).code)

    def test_resharper_job_must_bind_same_head_run_and_complete(self):
        for kind in ("wrong-sha", "wrong-run", "failed"):
            with self.subTest(kind=kind):
                value = snapshot()
                job = value["jobs"]["jobs"][0]
                if kind == "wrong-sha":
                    job["head_sha"] = "c" * 40
                elif kind == "wrong-run":
                    job["run_id"] = RUN_ID + 10
                else:
                    job["conclusion"] = "failure"
                self.assertEqual("RESHARPER_JOB_INCOMPLETE", evaluate_sample(value).code)

    def test_probe_order_and_missing_last_artifact_refuse(self):
        for kind in ("order", "upload"):
            with self.subTest(kind=kind):
                value = snapshot()
                steps = value["jobs"]["jobs"][0]["steps"]
                if kind == "order":
                    steps[1], steps[2] = steps[2], steps[1]
                    expected = "PROBE_ORDER_DRIFT"
                else:
                    steps[-1]["completed_at"] = None
                    expected = "RESHARPER_PROBE_INCOMPLETE"
                self.assertEqual(expected, evaluate_sample(value).code)

    def test_cancelled_or_failed_pr_ci_is_not_success(self):
        for state in ("cancelled", "failure", "in_progress"):
            with self.subTest(state=state):
                value = snapshot()
                run = value["run"]
                run["status"] = "completed" if state != "in_progress" else "in_progress"
                run["conclusion"] = state if state != "in_progress" else None
                self.assertEqual("CI_RUN_INCOMPLETE", evaluate_sample(value).code)

    def test_draft_closed_or_foreign_repository_is_never_ready(self):
        for kind in ("draft", "closed", "foreign"):
            with self.subTest(kind=kind):
                value = snapshot()
                if kind == "draft":
                    value["pr"]["draft"] = True
                    expected = "PR_NOT_OPEN_READY"
                elif kind == "closed":
                    value["pr"]["state"] = "closed"
                    expected = "PR_NOT_OPEN_READY"
                else:
                    value["pr"]["head"]["repo"]["full_name"] = "other/owner"
                    expected = "HEAD_DRIFT"
                self.assertEqual(expected, evaluate_sample(value).code)

    def test_bad_types_and_no_test_discovery_cannot_claim_success(self):
        value = snapshot()
        value["checks"]["check_runs"] = []
        self.assertEqual("INCOMPLETE_CHECK_PAGES", evaluate_sample(value).code)
        self.assertEqual("MALFORMED", evaluate_sample({"pr": []}).code)
        self.assertEqual("INVALID_INPUT",
                         evaluate(snapshot(), REPOSITORY, PR_NUMBER, "notasha", BASE, RUN_ID, ATTEMPT).code)


if __name__ == "__main__":
    unittest.main()
