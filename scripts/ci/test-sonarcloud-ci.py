#!/usr/bin/env python3
"""Regression checks for SonarCloud trust, attribution and SARIF conversion."""

import importlib.util
import json
import os
from pathlib import Path
import sys
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
    def issue(self, key="one"):
        return {"key": key, "rule": "csharpsquid:S123", "message": "Fix this issue",
                "severity": "CRITICAL", "component": "project:file", "line": 7}

    def report(self, directory, *, server=sonar.SONAR, task="task"):
        report = Path(directory) / "report-task.txt"
        report.write_text(f"serverUrl={server}\nprojectKey=project\nceTaskId={task}\n")
        return report

    def test_conversion_retains_rule_location_severity_and_stable_identity(self):
        result = sonar.to_sarif([self.issue()], [{"key": "project:file", "path": "src/A file.cs"}], "project")
        run = result["runs"][0]
        self.assertEqual("SonarCloud", run["tool"]["driver"]["name"])
        self.assertEqual("error", run["results"][0]["level"])
        self.assertEqual("src/A%20file.cs", run["results"][0]["locations"][0]["physicalLocation"]["artifactLocation"]["uri"])
        self.assertEqual({"startLine": 7}, run["results"][0]["locations"][0]["physicalLocation"]["region"])
        self.assertEqual(64, len(run["results"][0]["partialFingerprints"]["sonarIssueKey/v1"]))

    def test_outside_repository_paths_are_rejected(self):
        for file_path in ("../outside.cs", "/outside.cs", "C:/outside.cs", "src\\outside.cs"):
            candidate = [self.issue()]
            components = [{"key": "project:file", "path": file_path}]
            with self.subTest(path=file_path):
                with self.assertRaises(ValueError):
                    sonar.to_sarif(candidate, components, "project")

    def test_empty_success_is_valid_and_project_issue_has_no_fabricated_location(self):
        self.assertEqual([], sonar.to_sarif([], [], "project")["runs"][0]["results"])
        self.assertNotIn("locations", sonar.to_sarif([self.issue()], [], "project")["runs"][0]["results"][0])

    def test_text_range_and_unknown_severity(self):
        issue = self.issue()
        issue.pop("line")
        issue["textRange"] = {"startLine": 3}
        issue["severity"] = "UNRECOGNIZED"
        result = sonar.to_sarif([issue], [{"key": "project:file", "path": "src/main.py"}], "project")
        self.assertEqual("warning", result["runs"][0]["results"][0]["level"])
        self.assertEqual(3, result["runs"][0]["results"][0]["locations"][0]["physicalLocation"]["region"]["startLine"])

    def test_api_host_and_path_are_strictly_allowlisted(self):
        cases = [(sonar.GITHUB_API, "/repos/owner/repo/pulls/../../issues"),
                 (sonar.GITHUB_API, "/repos/owner/repo/pulls/%2e%2e"),
                 (sonar.SONAR, "/api/ce/task/../../admin"),
                 ("https://evil.invalid", "/api/ce/task")]
        with patch.object(sonar, "urlopen") as network:
            for host, path in cases:
                with self.subTest(host=host, path=path):
                    with self.assertRaises(ValueError):
                        sonar.request(host, path, "secret")
            network.assert_not_called()

    def test_request_formats_validated_query_and_json(self):
        with patch.object(sonar, "urlopen") as network:
            network.return_value.__enter__.return_value.read.return_value = b'{"ok": true}'
            result = sonar.request(sonar.SONAR, "/api/ce/task", "secret", params={"id": "a/b"})
            self.assertEqual({"ok": True}, result)
            url = network.call_args.args[0].full_url
            self.assertIn("id=a%2Fb", url)
            self.assertNotIn("secret", url)
            result = sonar.request(sonar.GITHUB_API, "/repos/owner/repo/code-scanning/sarifs",
                                   "secret", payload={"key": "value"})
            self.assertEqual({"ok": True}, result)
            self.assertEqual("2022-11-28", network.call_args.args[0].get_header("X-github-api-version"))

    def test_request_redacts_server_errors(self):
        with patch.object(sonar, "urlopen", side_effect=HTTPError("uri", 403, "permission", {}, None)):
            with self.assertRaisesRegex(RuntimeError, "HTTP 403") as error:
                sonar.request(sonar.SONAR, "/api/ce/task", "secret")
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

    def test_sonar_api_restricts_service_host(self):
        with patch.object(sonar, "request", return_value={"ok": 1}) as http:
            self.assertEqual({"ok": 1}, sonar.sonar_api("test")("ce/task", id="task"))
            self.assertEqual(sonar.SONAR, http.call_args.args[0])
            self.assertEqual("/api/ce/task", http.call_args.args[1])

    def test_analysis_polling_pending_and_complete(self):
        responses = iter([{"task": {"status": "PENDING"}}, {"task": {"status": "SUCCESS", "analysisId": "a"}}])
        with patch.object(sonar.time, "sleep") as sleep:
            self.assertEqual("a", sonar.wait_for_analysis(lambda *a, **kw: next(responses), "task"))
            sleep.assert_called_once_with(5)

    def test_analysis_polling_fails_closed_on_error_or_timeout(self):
        with self.assertRaisesRegex(ValueError, "did not succeed"):
            sonar.wait_for_analysis(lambda *a, **kw: {"task": {"status": "FAILED"}}, "task")
        with patch.object(sonar.time, "sleep") as sleep:
            with self.assertRaises(TimeoutError):
                sonar.wait_for_analysis(lambda *a, **kw: {"task": {"status": "PENDING"}}, "task")
            self.assertEqual(120, sleep.call_count)

    def test_issue_pagination_success_and_empty(self):
        def api(endpoint, **params):
            if params["p"] == 1:
                return {"paging": {"total": 2}, "issues": [self.issue("one")], "components": []}
            return {"paging": {"total": 2}, "issues": [self.issue("two")], "components": []}
        issues, _ = sonar.unresolved_issues(api, "project", "1235")
        self.assertEqual(2, len(issues))
        self.assertEqual(([], []), sonar.unresolved_issues(lambda *a, **k: {"paging": {"total": 0},
                                                                                  "issues": [], "components": []}, "project", ""))

    def test_issue_pagination_rejects_overflow_duplicates_and_incomplete(self):
        invalid = [{"paging": {"total": 10001}, "issues": []},
                   {"paging": {"total": 2}, "issues": []},
                   {"paging": {"total": 2}, "issues": [self.issue("one"), self.issue("one")]}]
        for reply in invalid:
            with self.subTest(reply=reply["paging"]["total"]):
                with self.assertRaises(ValueError):
                    sonar.unresolved_issues(lambda *a, **kw: reply, "project", "1235")

    def test_pagination_changed_total_fails(self):
        pages = iter([{"paging": {"total": 2}, "issues": [self.issue("one")]},
                      {"paging": {"total": 3}, "issues": [self.issue("two")]}])
        with self.assertRaisesRegex(ValueError, "changed"):
            sonar.unresolved_issues(lambda *a, **kw: next(pages), "project", "1235")

    def test_export_paginates_and_keeps_failed_quality_gate_for_later_enforcement(self):
        with tempfile.TemporaryDirectory() as directory:
            report = self.report(directory)
            output = Path(directory) / "sonar.sarif"
            calls = []
            def api(base, endpoint, token, params):
                calls.append((endpoint, params))
                if endpoint == "/api/ce/task":
                    return {"task": {"status": "SUCCESS", "analysisId": "analysis"}}
                if endpoint == "/api/qualitygates/project_status":
                    return {"projectStatus": {"status": "ERROR"}}
                return {"paging": {"total": 2}, "issues": [self.issue(str(params['p']))], "components": []}
            with patch.dict(os.environ, {"SONAR_PROJECT_KEY": "project", "SONAR_TOKEN": "test", "PR_NUMBER": "1235"}), \
                 patch.object(sonar, "request", side_effect=api), patch.object(sonar, "outputs") as emit:
                sonar.export(report, output)
            emit.assert_called_once_with({"gate": "ERROR"})
            self.assertEqual(2, len(json.loads(output.read_text())["runs"][0]["results"]))
            searches = [params for endpoint, params in calls if endpoint == "/api/issues/search"]
            self.assertEqual([1, 2], [params["p"] for params in searches])
            self.assertTrue(all(params["pullRequest"] == "1235" for params in searches))

    def test_incomplete_export_does_not_create_a_clean_report(self):
        with tempfile.TemporaryDirectory() as directory:
            report = self.report(directory)
            output = Path(directory) / "sonar.sarif"
            responses = [{"task": {"status": "SUCCESS", "analysisId": "analysis"}},
                         {"paging": {"total": 1}, "issues": [], "components": []}]
            with patch.dict(os.environ, {"SONAR_PROJECT_KEY": "project", "SONAR_TOKEN": "test"}), \
                 patch.object(sonar, "request", side_effect=responses):
                with self.assertRaisesRegex(ValueError, "Incomplete"):
                    sonar.export(report, output)
            self.assertFalse(output.exists())

    def test_export_rejects_report_server_substitution_without_transmitting_token(self):
        with tempfile.TemporaryDirectory() as directory:
            report = self.report(directory, server="https://example.invalid")
            with patch.dict(os.environ, {"SONAR_PROJECT_KEY": "project"}), patch.object(sonar, "request") as api:
                with self.assertRaises(ValueError):
                    sonar.export(report, "unused.sarif")
            api.assert_not_called()

    def test_export_rejects_malformed_task_id_before_network(self):
        with tempfile.TemporaryDirectory() as directory:
            report = self.report(directory, task="../../admin")
            with patch.dict(os.environ, {"SONAR_PROJECT_KEY": "project"}), patch.object(sonar, "request") as api:
                with self.assertRaisesRegex(ValueError, "task ID"):
                    sonar.export(report, "unused.sarif")
            api.assert_not_called()

    def test_export_rejects_unknown_gate(self):
        with tempfile.TemporaryDirectory() as directory:
            report = self.report(directory)
            output = Path(directory) / "out.sarif"
            with patch.dict(os.environ, {"SONAR_PROJECT_KEY": "project", "SONAR_TOKEN": "test"}), \
                 patch.object(sonar, "sonar_api", return_value=lambda *a, **kw: {"projectStatus": {"status": "INVALID"}}), \
                 patch.object(sonar, "wait_for_analysis", return_value="analysis"), \
                 patch.object(sonar, "unresolved_issues", return_value=([], [])):
                with self.assertRaisesRegex(ValueError, "Quality Gate"):
                    sonar.export(report, output)
            self.assertFalse(output.exists())

    def test_stale_pr_cannot_publish(self):
        with patch.dict(os.environ, {"ANALYSIS_SHA": SHA, "ANALYSIS_REF": "refs/pull/1235/merge",
                                     "PR_NUMBER": "1235", "PR_HEAD": "b" * 40}), \
             patch.object(sonar, "github", return_value={"state": "open", "head": {"sha": "c" * 40}}) as api:
            with self.assertRaisesRegex(ValueError, "refusing stale"):
                sonar.publish("unused.sarif")
            api.assert_called_once()

    def test_successful_upload_waits_for_github_processing(self):
        with tempfile.TemporaryDirectory() as directory:
            report = Path(directory) / "report.sarif"
            report.write_text(json.dumps(sonar.to_sarif([], [], "project")))
            with patch.dict(os.environ, {"ANALYSIS_SHA": SHA, "ANALYSIS_REF": "refs/heads/main", "PR_NUMBER": ""}), \
                 patch.object(sonar, "github", side_effect=[{"id": "upload"}, {"processing_status": "pending"},
                                                          {"processing_status": "complete"}]) as api, \
                 patch.object(sonar.time, "sleep"):
                sonar.publish(report)
            payload = api.call_args_list[0].kwargs["payload"]
            self.assertEqual(SHA, payload["commit_sha"])
            self.assertEqual("refs/heads/main", payload["ref"])
            self.assertEqual(3, api.call_count)

    def test_publish_rejects_invalid_pr_sha_and_ref(self):
        with patch.dict(os.environ, {"ANALYSIS_SHA": "bad", "ANALYSIS_REF": "refs/heads/main", "PR_NUMBER": ""}):
            with self.assertRaisesRegex(ValueError, "Invalid analysis"):
                sonar.publish("unused")
        with patch.dict(os.environ, {"ANALYSIS_SHA": SHA, "ANALYSIS_REF": "refs/heads/random", "PR_NUMBER": ""}):
            with self.assertRaisesRegex(ValueError, "Unexpected publication ref"):
                sonar.publish("unused")
        with patch.dict(os.environ, {"ANALYSIS_SHA": SHA, "ANALYSIS_REF": "refs/heads/main", "PR_NUMBER": "1/../../"}):
            with self.assertRaisesRegex(ValueError, "Invalid PR"):
                sonar.publish("unused")

    def test_publish_rejects_invalid_report_upload_id_and_processor_failure(self):
        with tempfile.TemporaryDirectory() as directory:
            report = Path(directory) / "sarif.json"
            report.write_text("{}")
            with patch.dict(os.environ, {"ANALYSIS_SHA": SHA, "ANALYSIS_REF": "refs/heads/main", "PR_NUMBER": ""}), \
                 patch.object(sonar, "github") as api:
                with self.assertRaisesRegex(ValueError, "SARIF structure"):
                    sonar.publish(report)
                api.assert_not_called()
                report.write_text(json.dumps(sonar.to_sarif([], [], "project")))
                api.return_value = {"id": "../secret"}
                with self.assertRaisesRegex(ValueError, "upload ID"):
                    sonar.publish(report)
                api.side_effect = [{"id": "abc"}, {"processing_status": "failed"}]
                with self.assertRaisesRegex(ValueError, "rejected SARIF"):
                    sonar.publish(report)

    def test_workspace_paths_must_be_regular_files_under_root(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "sarif").mkdir()
            report = root / "sarif" / "data.sarif"
            report.write_text("{}")
            with patch.dict(os.environ, {"GITHUB_WORKSPACE": directory}):
                self.assertEqual(report, sonar.workspace_file("sarif/data.sarif"))
                self.assertEqual(root / "out.sarif", sonar.workspace_file("out.sarif", for_write=True))
                for candidate in ("../outside", "/tmp/sneaky"):
                    with self.assertRaisesRegex(ValueError, "Invalid workspace"):
                        sonar.workspace_file(candidate)
                with self.assertRaisesRegex(ValueError, "Expected a regular"):
                    sonar.workspace_file("out.sarif")
                (root / "sarif" / "link").symlink_to(report)
                with self.assertRaisesRegex(ValueError, "Symlink"):
                    sonar.workspace_file("sarif/link")

    def test_cli_never_accepts_user_supplied_paths(self):
        with patch.object(sys, "argv", ["sonarcloud-ci.py", "export", "--report", "../../arbitrary"]):
            with self.assertRaises(SystemExit):
                sonar.main()
        with patch.object(sys, "argv", ["sonarcloud-ci.py", "resolve"]), patch.object(sonar, "resolve") as selected:
            sonar.main()
            selected.assert_called_once()


if __name__ == "__main__":
    unittest.main()
