#!/usr/bin/env python3
"""Regression checks for the native SonarCloud app scanner revision boundary."""

import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from urllib.error import HTTPError

SPEC = importlib.util.spec_from_file_location("sonarcloud_ci", Path(__file__).with_name("sonarcloud-ci.py"))
sonar = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(sonar)
SHA = "a" * 40
REPO = "NYGsatoshi/Coglatas"


def reviewer_policy():
    return {"protection_rules": [{"type": "required_reviewers", "reviewers": [{"id": 1}]}]}


def pr_snapshot(*, sha=SHA, head="b" * 40, mergeable=True, repository=REPO):
    return {"state": "open", "base": {"ref": "main"},
            "head": {"ref": "feature", "sha": head, "repo": {"full_name": repository}},
            "mergeable": mergeable, "merge_commit_sha": sha}


class SonarCloudTests(unittest.TestCase):
    def test_api_host_and_path_are_allowlisted(self):
        cases = [(sonar.GITHUB_API, "/repos/owner/repo/pulls/../../issues"),
                 (sonar.GITHUB_API, "/repos/owner/repo/pulls/%2e%2e"),
                 ("https://evil.invalid", "/repos/owner/repo/pulls"),
                 (sonar.GITHUB_API, "/repos/owner/repo/code-scanning/sarifs")]
        with patch.object(sonar, "urlopen") as network:
            for host, path in cases:
                with self.subTest(host=host, path=path):
                    with self.assertRaises(ValueError):
                        sonar.request(host, path, "secret")
            network.assert_not_called()

    def test_request_formats_validated_query(self):
        with patch.object(sonar, "urlopen") as network:
            network.return_value.__enter__.return_value.read.return_value = b'{"ok": true}'
            result = sonar.request(sonar.GITHUB_API, "/repos/owner/repo/pulls",
                                   "secret", params={"head": "owner:feature/a"})
            self.assertEqual({"ok": True}, result)
            request = network.call_args.args[0]
            self.assertIn("head=owner%3Afeature%2Fa", request.full_url)
            self.assertNotIn("secret", request.full_url)
            self.assertEqual("2022-11-28", request.get_header("X-github-api-version"))

    def test_request_redacts_server_errors(self):
        with patch.object(sonar, "urlopen",
                          side_effect=HTTPError("uri", 403, "permission", {}, None)):
            with self.assertRaisesRegex(RuntimeError, "HTTP 403") as error:
                sonar.request(sonar.GITHUB_API, "/repos/owner/repo/pulls", "secret")
            self.assertNotIn("secret", str(error.exception))

    def test_github_validates_repository_and_api_endpoint(self):
        with patch.dict(os.environ, {"GITHUB_REPOSITORY": "../not/repo", "GH_TOKEN": "secret"}), \
             patch.object(sonar, "request") as api:
            with self.assertRaisesRegex(ValueError, "Unexpected GitHub"):
                sonar.github("/pulls")
            api.assert_not_called()
        with patch.dict(os.environ, {"GITHUB_REPOSITORY": REPO, "GH_TOKEN": "secret"}), \
             patch.object(sonar, "request") as api:
            with self.assertRaisesRegex(ValueError, "Unexpected GitHub"):
                sonar.github("/pulls/../admin")
            api.assert_not_called()
            sonar.github("/pulls/1235")
            self.assertEqual(f"/repos/{REPO}/pulls/1235", api.call_args.args[1])

    def test_outputs_rejects_multiline_and_writes_valid_values(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "out.txt"
            with patch.dict(os.environ, {"GITHUB_OUTPUT": str(output)}):
                sonar.outputs({"sha": SHA})
                self.assertIn(SHA, output.read_text())
                with self.assertRaisesRegex(ValueError, "newline"):
                    sonar.outputs({"head": "bad\ninjection"})

    def test_unprotected_environment_stops_before_any_checkout_revision_is_selected(self):
        with patch.object(sonar, "github", return_value={"protection_rules": []}):
            with self.assertRaisesRegex(ValueError, "requires reviewers"):
                sonar.resolve()

    def test_reviewed_environment_allows_revision(self):
        with patch.object(sonar, "github", return_value=reviewer_policy()):
            sonar.require_reviewed_environment()

    def test_workflow_run_cannot_process_forks_or_failed_runs(self):
        run = {"event": "pull_request", "conclusion": "success", "head_repository":
               {"full_name": "other/fork", "owner": {"login": "other"}}, "head_branch": "topic"}
        with patch.dict(os.environ, {"GITHUB_REPOSITORY": REPO}):
            with self.assertRaisesRegex(ValueError, "Ineligible upstream"):
                sonar.requested_revision({"workflow_run": run})
            run["head_repository"]["full_name"] = REPO
            run["conclusion"] = "failure"
            with self.assertRaisesRegex(ValueError, "Ineligible upstream"):
                sonar.requested_revision({"workflow_run": run})

    def test_successful_workflow_run_selects_exact_pr(self):
        run = {"event": "pull_request", "conclusion": "success", "head_repository":
               {"full_name": REPO, "owner": {"login": "NYGsatoshi"}}, "head_branch": "topic"}
        with patch.dict(os.environ, {"GITHUB_REPOSITORY": REPO}), \
             patch.object(sonar, "github", return_value=[{"number": 1235}]) as api:
            number, original_run = sonar.requested_revision({"workflow_run": run})
            self.assertEqual("1235", number)
            self.assertIs(run, original_run)
            self.assertEqual("main", api.call_args.kwargs["params"]["base"])

    def test_workflow_run_rejects_ambiguous_pr(self):
        run = {"event": "pull_request", "conclusion": "success", "head_repository":
               {"full_name": REPO, "owner": {"login": "NYGsatoshi"}}, "head_branch": "topic"}
        with patch.dict(os.environ, {"GITHUB_REPOSITORY": REPO}), patch.object(sonar, "github", return_value=[]):
            with self.assertRaisesRegex(ValueError, "Expected one"):
                sonar.requested_revision({"workflow_run": run})

    def test_pr_snapshot_good_and_guarded(self):
        with patch.dict(os.environ, {"GITHUB_REPOSITORY": REPO}), \
             patch.object(sonar, "github", return_value=pr_snapshot()):
            values = sonar.pr_revision("1235", None, {})
            self.assertEqual(SHA, values["sha"])
            self.assertEqual("refs/pull/1235/merge", values["ref"])

    def test_pr_snapshot_rejects_unsafe_inputs(self):
        with patch.dict(os.environ, {"GITHUB_REPOSITORY": REPO}), patch.object(sonar, "github") as api:
            for number in ("０３", "1235/../", "0", "x"):
                with self.subTest(number=number):
                    with self.assertRaisesRegex(ValueError, "digits"):
                        sonar.pr_revision(number, None, {})
            api.assert_not_called()
            api.return_value = pr_snapshot(repository="other/fork")
            with self.assertRaisesRegex(ValueError, "same-repository"):
                sonar.pr_revision("1235", None, {})
            api.return_value = pr_snapshot(mergeable=False)
            with self.assertRaisesRegex(ValueError, "mergeability"):
                sonar.pr_revision("1235", None, {})

    def test_stale_pr_and_bootstrap_cannot_select_new_head(self):
        with patch.dict(os.environ, {"GITHUB_REPOSITORY": REPO}), \
             patch.object(sonar, "github", return_value=pr_snapshot()):
            with self.assertRaisesRegex(ValueError, "PR moved since upstream"):
                sonar.pr_revision("1235", {"head_sha": "c" * 40}, {})
            with self.assertRaisesRegex(ValueError, "bootstrap push"):
                sonar.pr_revision("1235", None, {"after": SHA})

    def test_resolve_main_pins_revision_and_emits_summary(self):
        with tempfile.TemporaryDirectory() as directory:
            event = Path(directory) / "event.json"
            event.write_text(json.dumps({"after": SHA}))
            summary = Path(directory) / "summary.txt"
            with patch.dict(os.environ, {"GITHUB_EVENT_PATH": str(event), "GITHUB_STEP_SUMMARY": str(summary),
                                       "REQUESTED_PR": ""}), \
                 patch.object(sonar, "require_reviewed_environment"), patch.object(sonar, "outputs") as emit:
                sonar.resolve()
                self.assertEqual(SHA, emit.call_args.args[0]["sha"])
                self.assertIn(SHA, summary.read_text())

    def test_resolve_missing_main_sha_fails_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            event = Path(directory) / "event.json"
            event.write_text("{}")
            with patch.dict(os.environ, {"GITHUB_EVENT_PATH": str(event), "REQUESTED_PR": ""}), \
                 patch.object(sonar, "require_reviewed_environment"), \
                 patch.object(sonar, "github", return_value={"object": {"sha": "malformed"}}), \
                 patch.object(sonar, "outputs") as emit:
                with self.assertRaisesRegex(ValueError, "immutable"):
                    sonar.resolve()
                emit.assert_not_called()


if __name__ == "__main__":
    unittest.main()
