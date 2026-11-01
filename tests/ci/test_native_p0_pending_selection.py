"""Pending native evidence never imports coverage or replaces earlier history."""

from __future__ import annotations

from copy import deepcopy
import re
import unittest

from test_native_p0_coverage_artifact import Api, ARTIFACT, BRANCH, JOB, RUN, SHA, subject


class RunScopedApi(Api):
    """Resolve each synthetic job list from its requested run and attempt."""

    def __init__(self):
        super().__init__()
        self.jobs_by_run = {RUN: [self.job]}

    def __call__(self, path):
        if "/jobs?" not in path:
            return super().__call__(path)
        self.calls.append(path)
        match = re.fullmatch(re.escape(subject.PREFIX)
                             + r"actions/runs/([0-9]+)/attempts/1/jobs\?per_page=100&page=1", path)
        if match is None or int(match[1]) not in self.jobs_by_run:
            raise AssertionError("Unexpected synthetic job authority")
        jobs = self.jobs_by_run[int(match[1])]
        return {"total_count": len(jobs), "jobs": deepcopy(jobs)}

    def append_success(self):
        later = Api()
        later.run["id"] = RUN + 1
        later.job.update(id=JOB + 1, run_id=RUN + 1)
        self.runs.append(later.run)
        self.jobs_by_run[RUN + 1] = [later.job]


def pending_api():
    api = RunScopedApi()
    api.run.update(status="in_progress", conclusion=None)
    api.job.update(status="in_progress", conclusion=None)
    for step in api.job["steps"]:
        step["conclusion"] = None
    return api


class PendingSelectionTests(unittest.TestCase):
    def assert_unavailable(self, api):
        self.assertIsNone(subject.resolve(api, SHA, BRANCH))
        self.assertFalse(any("/artifacts?" in path for path in api.calls))

    def test_pending_native_selection_is_unavailable(self):
        self.assert_unavailable(pending_api())

    def test_pending_mobile_step_is_unavailable(self):
        api = pending_api()
        api.job["steps"][0]["conclusion"] = "success"
        api.job["steps"][1]["conclusion"] = "skipped"
        self.assert_unavailable(api)

    def test_pending_p0_step_is_unavailable(self):
        api = pending_api()
        api.job["steps"][0]["conclusion"] = "success"
        api.job["steps"][2]["conclusion"] = "skipped"
        self.assert_unavailable(api)

    def test_completed_malformed_selection_still_fails(self):
        api = RunScopedApi()
        api.job["steps"][0]["conclusion"] = None
        with self.assertRaisesRegex(subject.ArtifactError, "^SELECTION$"):
            subject.resolve(api, SHA, BRANCH)

    def test_pending_job_must_belong_to_its_run(self):
        api = pending_api()
        api.job["run_id"] = RUN + 1
        with self.assertRaisesRegex(subject.ArtifactError, "^EXECUTION$"):
            subject.resolve(api, SHA, BRANCH)

    def test_pending_job_must_have_the_requested_attempt(self):
        api = pending_api()
        api.job["run_attempt"] = 2
        with self.assertRaisesRegex(subject.ArtifactError, "^EXECUTION$"):
            subject.resolve(api, SHA, BRANCH)

    def test_pending_job_must_have_the_requested_source(self):
        api = pending_api()
        api.job["head_sha"] = "4" * 40
        with self.assertRaisesRegex(subject.ArtifactError, "^EXECUTION$"):
            subject.resolve(api, SHA, BRANCH)

    def test_pending_run_must_have_the_owner_actor(self):
        api = pending_api()
        api.run["actor"]["login"] = "unapproved-actor"
        with self.assertRaisesRegex(subject.ArtifactError, "^RUN$"):
            subject.resolve(api, SHA, BRANCH)

    def assert_ambiguous_history(self, api):
        api.append_success()
        with self.assertRaisesRegex(subject.ArtifactError, "^AMBIGUOUS$"):
            subject.resolve(api, SHA, BRANCH)
        requested = [path for path in api.calls if "/jobs?" in path]
        self.assertEqual(requested, [subject.PREFIX + "actions/runs/" + str(run)
                                    + "/attempts/1/jobs?per_page=100&page=1"
                                    for run in (RUN, RUN + 1)])
        self.assertNotEqual(api.jobs_by_run[RUN][0]["id"], api.jobs_by_run[RUN + 1][0]["id"])
        self.assertFalse(any("/artifacts?" in path for path in api.calls))

    def test_pending_history_cannot_be_discarded_to_select_a_later_pass(self):
        self.assert_ambiguous_history(pending_api())

    def test_failed_history_cannot_be_discarded_to_select_a_later_pass(self):
        api = RunScopedApi()
        api.run["conclusion"] = "failure"
        api.job["conclusion"] = "failure"
        api.job["steps"][1]["conclusion"] = "failure"
        self.assert_ambiguous_history(api)

    def test_one_completed_exact_source_success_remains_available(self):
        selected = subject.resolve(RunScopedApi(), SHA, BRANCH)
        self.assertEqual([value["id"] for value in selected], [RUN, JOB, ARTIFACT])


if __name__ == "__main__":
    unittest.main()
