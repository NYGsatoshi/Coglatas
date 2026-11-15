#!/usr/bin/env python3
"""Offline fail-closed controls for read-only obsolete-run evidence (P0 #1974)."""
from __future__ import annotations

from copy import deepcopy
from pathlib import Path
import sys
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
from scripts.ci.projectide_obsolete_run_evidence import (  # noqa: E402
    Decision, EXPECTED_WORKFLOW_ID, WORKFLOW_PATH, evaluate, live_snapshot,
)

REPO = "NYGsatoshi/Coglatas"
PR = 1972
OLD = "a" * 40
NEW = "b" * 40
BASE = "projectide/integration-20261031"
BRANCH = "projectide/compiler-recording-web-20261115"
OLD_ID = 43670058840
NEW_ID = 43671221166


def run(run_id: int, sha: str, status: str, created: str) -> dict:
    return {
        "id": run_id, "name": "PR CI", "workflow_id": EXPECTED_WORKFLOW_ID,
        "path": WORKFLOW_PATH, "event": "pull_request", "run_attempt": 1,
        "head_sha": sha, "head_branch": BRANCH,
        "repository": {"full_name": REPO},
        "head_repository": {"full_name": REPO},
        # GitHub can legitimately return [] on the PR CI run itself.
        "pull_requests": [], "status": status, "conclusion": None,
        "created_at": created,
    }


def fixture() -> dict:
    pr = {
        "number": PR, "state": "open", "merged": False, "draft": False,
        "head": {"sha": NEW, "ref": BRANCH, "repo": {"full_name": REPO}},
        "base": {"ref": BASE, "repo": {"full_name": REPO}},
    }
    older = run(OLD_ID, OLD, "in_progress", "2026-11-15T02:56:20Z")
    successor = run(NEW_ID, NEW, "pending", "2026-11-15T03:11:29Z")
    return {
        "pr_before": pr,
        "pr_after": deepcopy(pr),
        "obsolete": older,
        "successor": successor,
        "obsolete_again": deepcopy(older),
        "successor_again": deepcopy(successor),
        "old_commit_prs": [{"number": PR, "head": {"sha": NEW}}],
        "obsolete_jobs": {"total_count": 2, "jobs": [
            {"name": "ReSharper PR fast lane / ReSharper InspectCode / PR",
             "status": "completed", "conclusion": "failure"},
            {"name": "frontend-test", "status": "in_progress", "conclusion": None},
        ]},
        "successor_jobs": {"total_count": 0, "jobs": []},
    }


def decide(data: dict, **changes) -> Decision:
    arguments = {
        "repository": REPO, "pr_number": PR,
        "old_run_id": OLD_ID, "new_run_id": NEW_ID,
        "old_head": OLD, "current_head": NEW,
        "base": BASE, "old_attempt": 1, "new_attempt": 1,
    }
    arguments.update(changes)
    return evaluate(data, **arguments)


class ObsoleteRunReadOnlyEvidenceTests(unittest.TestCase):
    def test_exact_stale_run_and_waiting_successor_is_observation_not_authorization(self):
        decision = decide(fixture())
        self.assertTrue(decision.observed)
        self.assertEqual("OBSOLETE_CI_QUEUE_BLOCK_OBSERVED", decision.code)
        self.assertFalse(decision.cancellation_authorized)
        self.assertFalse(decision.required_check_qualified)

    def test_old_run_must_not_equal_current_head(self):
        self.assertEqual("INVALID_INPUT", decide(fixture(), current_head=OLD).code)

    def test_current_head_run_is_never_an_obsolete_candidate(self):
        value = fixture()
        value["obsolete"]["head_sha"] = NEW
        self.assertEqual("RUN_SOURCE_MISMATCH", decide(value).code)

    def test_changed_pr_head_on_second_read_is_refused(self):
        value = fixture()
        value["pr_after"]["head"]["sha"] = "c" * 40
        self.assertFalse(decide(value).observed)

    def test_closed_or_merged_pr_is_refused(self):
        for change in ({"state": "closed"}, {"merged": True}):
            with self.subTest(change=change):
                value = fixture()
                value["pr_before"].update(change)
                self.assertFalse(decide(value).observed)

    def test_other_pr_is_refused_even_when_run_branch_matches(self):
        value = fixture()
        value["pr_before"]["number"] = PR + 1
        self.assertFalse(decide(value).observed)

    def test_other_repository_cannot_be_targeted(self):
        value = fixture()
        value["obsolete"]["head_repository"]["full_name"] = "elsewhere/else"
        self.assertEqual("WRONG_REPOSITORY", decide(value).code)

    def test_base_branch_or_head_branch_drift_is_refused(self):
        for path, value in (("base", "main"), ("branch", "feature/other")):
            with self.subTest(path=path):
                data = fixture()
                if path == "base":
                    data["pr_after"]["base"]["ref"] = value
                else:
                    data["successor"]["head_branch"] = value
                self.assertFalse(decide(data).observed)

    def test_unknown_workflow_id_or_event_is_refused(self):
        for field, value in (("workflow_id", 9), ("event", "workflow_dispatch"),
                             ("path", ".github/workflows/other.yml")):
            with self.subTest(field=field):
                data = fixture()
                data["obsolete"][field] = value
                self.assertEqual("WRONG_WORKFLOW_OR_EVENT", decide(data).code)

    def test_wrong_run_id_and_attempt_are_refused(self):
        for field, value in (("id", OLD_ID + 1), ("run_attempt", 2)):
            with self.subTest(field=field):
                data = fixture()
                data["obsolete"][field] = value
                self.assertEqual("RUN_ATTEMPT_OR_ID_MISMATCH", decide(data).code)

    def test_missing_commit_pr_association_is_refused_even_if_run_branch_matches(self):
        for associations in ([], [{"number": 99}], [{}]):
            with self.subTest(associations=associations):
                data = fixture()
                data["old_commit_prs"] = associations
                self.assertFalse(decide(data).observed)

    def test_run_pr_association_to_another_pr_is_refused(self):
        data = fixture()
        data["obsolete"]["pull_requests"] = [{"number": PR + 1}]
        self.assertFalse(decide(data).observed)

    def test_cancelled_completed_or_failed_run_is_not_actionable(self):
        for state, conclusion in (("completed", "cancelled"), ("completed", "failure"),
                                  ("completed", "success"), ("queued", None)):
            with self.subTest(state=state):
                data = fixture()
                data["obsolete"]["status"] = state
                data["obsolete"]["conclusion"] = conclusion
                self.assertFalse(decide(data).observed)

    def test_successor_already_running_or_completed_does_not_need_queue_release(self):
        for state, conclusion in (("in_progress", None), ("completed", "success"),
                                  ("completed", "cancelled")):
            with self.subTest(state=state):
                data = fixture()
                data["successor"]["status"] = state
                data["successor"]["conclusion"] = conclusion
                self.assertFalse(decide(data).observed)

    def test_successor_dispatched_jobs_refuse_unnecessary_cancellation(self):
        data = fixture()
        data["successor_jobs"] = {"total_count": 1, "jobs": [
            {"name": "CI preflight + route", "status": "queued"},
        ]}
        self.assertEqual("SUCCESSOR_ALREADY_DISPATCHED", decide(data).code)

    def test_incomplete_paginated_jobs_never_qualify(self):
        data = fixture()
        data["obsolete_jobs"]["total_count"] = 3
        self.assertEqual("INCOMPLETE_OR_PAGINATED_EVIDENCE", decide(data).code)
        data = fixture()
        data["obsolete_jobs"] = {"total_count": 100, "jobs": [{"name": "frontend-test"}] * 100}
        self.assertFalse(decide(data).observed)

    def test_frontend_job_must_be_exactly_one_and_still_active(self):
        for change in ("missing", "complete", "duplicate"):
            with self.subTest(change=change):
                data = fixture()
                jobs = data["obsolete_jobs"]["jobs"]
                if change == "missing":
                    jobs.pop()
                elif change == "complete":
                    jobs[-1]["status"] = "completed"
                    jobs[-1]["conclusion"] = "cancelled"
                else:
                    jobs.append(deepcopy(jobs[-1]))
                data["obsolete_jobs"]["total_count"] = len(jobs)
                self.assertFalse(decide(data).observed)

    def test_rechecked_run_status_and_attempt_must_still_match(self):
        for key, field, value in (("obsolete_again", "status", "completed"),
                                  ("successor_again", "status", "in_progress"),
                                  ("obsolete_again", "run_attempt", 2)):
            with self.subTest(key=key, field=field):
                data = fixture()
                data[key][field] = value
                self.assertFalse(decide(data).observed)

    def test_clock_order_and_bad_time_are_fail_closed(self):
        for timestamp in ("2026-11-15T02:00:00Z", "not-an-instant", ""):
            with self.subTest(timestamp=timestamp):
                data = fixture()
                data["successor"]["created_at"] = timestamp
                self.assertFalse(decide(data).observed)

    def test_malformed_response_never_raises_or_qualifies(self):
        for malformed in (None, [], {}, {"pr_before": 4}):
            with self.subTest(malformed=malformed):
                self.assertEqual("MALFORMED_EVIDENCE", decide(malformed).code)

    def test_live_reader_uses_only_fixed_get_paths_and_no_cancel_call(self):
        value = fixture()
        observed = []
        responses = [
            value["pr_before"], value["obsolete"], value["successor"],
            value["old_commit_prs"], value["obsolete_jobs"], value["successor_jobs"],
            value["pr_after"], value["obsolete_again"], value["successor_again"],
        ]
        def fake_get(path):
            observed.append(path)
            return responses[len(observed) - 1]
        with patch("scripts.ci.projectide_obsolete_run_evidence.github_get", side_effect=fake_get):
            result = live_snapshot(REPO, PR, OLD_ID, NEW_ID, OLD)
        self.assertEqual(9, len(observed))
        self.assertTrue(all(path.startswith(f"/repos/{REPO}/") for path in observed))
        self.assertTrue(all("cancel" not in path and "dispatch" not in path
                            for path in observed))
        self.assertTrue(decide(result).observed)


if __name__ == "__main__":
    unittest.main()
