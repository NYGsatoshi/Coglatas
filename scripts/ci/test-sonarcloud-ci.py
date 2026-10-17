#!/usr/bin/env python3
"""Regression checks for SonarCloud trust, attribution and SARIF conversion."""

import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("sonarcloud_ci", Path(__file__).with_name("sonarcloud-ci.py"))
sonar = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(sonar)
SHA = "a" * 40


class SonarCloudTests(unittest.TestCase):
    def issue(self, key="one"):
        return {"key": key, "rule": "csharpsquid:S123", "message": "Fix this issue",
                "severity": "CRITICAL", "component": "project:file", "line": 7}

    def test_conversion_retains_rule_location_severity_and_stable_identity(self):
        result = sonar.to_sarif([self.issue()], [{"key": "project:file", "path": "src/A file.cs"}], "project")
        run = result["runs"][0]
        self.assertEqual("SonarCloud", run["tool"]["driver"]["name"])
        self.assertEqual("error", run["results"][0]["level"])
        self.assertEqual("src/A%20file.cs", run["results"][0]["locations"][0]["physicalLocation"]["artifactLocation"]["uri"])
        self.assertEqual({"startLine": 7}, run["results"][0]["locations"][0]["physicalLocation"]["region"])
        self.assertEqual(64, len(run["results"][0]["partialFingerprints"]["sonarIssueKey/v1"]))

    def test_outside_repository_paths_are_rejected(self):
        for path in ("../outside.cs", "/outside.cs", "C:/outside.cs", "src\\outside.cs"):
            with self.subTest(path=path), self.assertRaises(ValueError):
                sonar.to_sarif([self.issue()], [{"key": "project:file", "path": path}], "project")

    def test_empty_success_is_valid_and_project_issue_has_no_fabricated_location(self):
        self.assertEqual([], sonar.to_sarif([], [], "project")["runs"][0]["results"])
        self.assertNotIn("locations", sonar.to_sarif([self.issue()], [], "project")["runs"][0]["results"][0])

    def test_unprotected_environment_stops_before_any_checkout_revision_is_selected(self):
        with patch.object(sonar, "github", return_value={"protection_rules": []}), self.assertRaisesRegex(ValueError, "requires reviewers"):
            sonar.resolve()

    def test_stale_pr_cannot_publish(self):
        with patch.dict(os.environ, {"ANALYSIS_SHA": SHA, "ANALYSIS_REF": "refs/pull/1235/merge",
                                    "PR_NUMBER": "1235", "PR_HEAD": "b" * 40}), \
             patch.object(sonar, "github", return_value={"state": "open", "head": {"sha": "c" * 40}}) as api:
            with self.assertRaisesRegex(ValueError, "refusing stale"):
                sonar.publish("unused.sarif")
            api.assert_called_once()

    def test_fork_pr_cannot_enter_the_secret_bearing_job(self):
        with tempfile.TemporaryDirectory() as directory:
            event = Path(directory) / "event.json"
            event.write_text("{}")
            responses = [{"protection_rules": [{"type": "required_reviewers", "reviewers": [{"id": 1}]}]},
                         {"state": "open", "base": {"ref": "main"},
                          "head": {"repo": {"full_name": "other/fork"}}}]
            with patch.dict(os.environ, {"GITHUB_EVENT_PATH": str(event), "REQUESTED_PR": "1235",
                                        "GITHUB_REPOSITORY": "NYGsatoshi/Coglatas"}), \
                 patch.object(sonar, "github", side_effect=responses), patch.object(sonar, "outputs") as emit:
                with self.assertRaisesRegex(ValueError, "same-repository"):
                    sonar.resolve()
                emit.assert_not_called()

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

    def test_export_paginates_and_keeps_failed_quality_gate_for_later_enforcement(self):
        with tempfile.TemporaryDirectory() as directory:
            report = Path(directory) / "report-task.txt"
            output = Path(directory) / "sonar.sarif"
            report.write_text("serverUrl=https://sonarcloud.io\nprojectKey=project\nceTaskId=task\n")
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
            report = Path(directory) / "report-task.txt"
            output = Path(directory) / "sonar.sarif"
            report.write_text("serverUrl=https://sonarcloud.io\nprojectKey=project\nceTaskId=task\n")
            responses = [{"task": {"status": "SUCCESS", "analysisId": "analysis"}},
                         {"paging": {"total": 1}, "issues": [], "components": []}]
            with patch.dict(os.environ, {"SONAR_PROJECT_KEY": "project", "SONAR_TOKEN": "test"}), \
                 patch.object(sonar, "request", side_effect=responses), self.assertRaisesRegex(ValueError, "Incomplete"):
                sonar.export(report, output)
            self.assertFalse(output.exists())

    def test_export_rejects_report_server_substitution_without_transmitting_token(self):
        with tempfile.TemporaryDirectory() as directory:
            report = Path(directory) / "report-task.txt"
            report.write_text("serverUrl=https://example.invalid\nprojectKey=project\nceTaskId=task\n")
            with patch.dict(os.environ, {"SONAR_PROJECT_KEY": "project"}), \
                 patch.object(sonar, "request") as api, self.assertRaises(ValueError):
                sonar.export(report, "unused.sarif")
            api.assert_not_called()


if __name__ == "__main__":
    unittest.main()
