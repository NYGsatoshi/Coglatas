"""Synthetic transport/provenance rejection controls; never valid Sonar coverage."""

from __future__ import annotations

from copy import deepcopy
from contextlib import redirect_stderr, redirect_stdout
import hashlib
import io
import json
import os
from pathlib import Path
import secrets
import stat
import sys
import tempfile
import unittest
from unittest.mock import patch
import urllib.error
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts/ci"))
import native_p0_coverage_artifact as subject

SHA, TREE = "1" * 40, "2" * 40
BRANCH = "test/synthetic-native"
RUN, JOB, ARTIFACT, WORKFLOW = 101, 201, 301, 348966932


def fixture_files():
    source = {"schemaVersion": 1, "sourceSha": SHA, "treeSha": TREE, "suite": "p0",
              "nativeCoverage": True, "runId": str(RUN), "attempt": 1}
    execution = {"sourceSha": SHA, "treeSha": TREE, "runId": str(RUN), "attempt": 1,
                 "retries": 0, "requiredCases": 8, "junitCases": 8, "status": "PASS"}
    environment = {"sourceSha": SHA, "hostNodeVersion": "v24.22.1", "postgresVersionNum": 180006,
                   "backendImage": "sha256:" + "3" * 64,
                   "qualification": "Synthetic Compose database version only; no application-role RLS claim."}
    browser = {"sourceSha": SHA, "nodeVersion": "v24.21.0", "suite": "p0"}
    # These literal files exercise transport only and must never enter Sonar.
    return {
        "sonar-native-host/source.json": json.dumps(source).encode(),
        "sonar-native-host/execution.json": json.dumps(execution).encode(),
        "sonar-real-backend-native/environment.json": json.dumps(environment).encode(),
        "sonar-real-backend-native/browser.json": json.dumps(browser).encode(),
        "sonar-native-host/lcov.info": b"SYNTHETIC_NOT_COVERAGE",
        "sonar-real-backend-native/lcov.info": b"SYNTHETIC_NOT_COVERAGE",
        "sonar-native-host/raw/coverage-1-100-0.json": b'{"result":[]}',
        "sonar-real-backend-native/raw/coverage-2-100-0.json": b'{"result":[]}',
    }


def archive(files=None, extra=None):
    output = io.BytesIO()
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as handle:
        for name, data in (files if files is not None else fixture_files()).items():
            handle.writestr(name, data)
        if extra:
            handle.writestr(*extra)
    return output.getvalue()


class Api:
    def __init__(self):
        self.run = {"id": RUN, "head_sha": SHA, "head_branch": BRANCH, "event": "workflow_dispatch",
                    "path": subject.WORKFLOW, "workflow_id": WORKFLOW, "run_attempt": 1,
                    "status": "completed", "conclusion": "success", "actor": {"login": "NYGsatoshi"},
                    "repository": {"id": subject.REPOSITORY_ID, "full_name": subject.REPOSITORY},
                    "head_repository": {"id": subject.REPOSITORY_ID, "full_name": subject.REPOSITORY}}
        self.job = {"id": JOB, "name": "licensed-real-backend", "run_id": RUN, "head_sha": SHA,
                    "run_attempt": 1, "status": "completed",
                    "conclusion": "success", "steps": [
                        {"name": "Validate closed native coverage selection", "conclusion": "success"},
                        {"name": "Run P0 acceptance", "conclusion": "success"},
                        {"name": "Run independent real-backend MVP0 Mobile acceptance", "conclusion": "skipped"}]}
        data = archive()
        self.artifact = {"id": ARTIFACT, "name": "sonar-real-backend-p0-native-" + SHA, "expired": False,
                         "size_in_bytes": len(data), "digest": "sha256:" + hashlib.sha256(data).hexdigest(),
                         "workflow_run": {"id": RUN, "head_sha": SHA}}
        self.runs, self.jobs, self.artifacts = [self.run], [self.job], [self.artifact]
        self.workflow = {"id": WORKFLOW, "path": subject.WORKFLOW, "state": "active"}
        self.calls = []

    def __call__(self, path):
        self.calls.append(path)
        if path.endswith("actions/workflows/licensed-real-backend-acceptance.yml"):
            return deepcopy(self.workflow)
        if "/runs?" in path:
            values, key = self.runs, "workflow_runs"
        elif "/jobs?" in path:
            values, key = self.jobs, "jobs"
        elif "/artifacts?" in path:
            values, key = self.artifacts, "artifacts"
        else:
            raise AssertionError("Unexpected synthetic API path")
        return {"total_count": len(values), key: deepcopy(values)}


class ProvenanceTests(unittest.TestCase):
    def test_one_complete_exact_source_run_is_selected(self):
        api = Api()
        selected = subject.resolve(api, SHA, BRANCH)
        self.assertEqual([value["id"] for value in selected], [RUN, JOB, ARTIFACT])
        self.assertTrue(all(path.startswith(subject.PREFIX) for path in api.calls))

    def test_absent_unselected_mobile_and_pending_do_not_claim_available(self):
        for kind in ("absent", "unselected", "mobile", "pending", "no_job"):
            with self.subTest(kind=kind):
                api = Api()
                if kind == "absent": api.runs = []
                elif kind == "no_job": api.jobs = []
                elif kind == "unselected": api.job["steps"][0]["conclusion"] = "skipped"
                elif kind == "mobile":
                    api.job["steps"][1]["conclusion"] = "skipped"
                    api.job["steps"][2]["conclusion"] = "success"
                else: api.run["status"] = "in_progress"
                self.assertIsNone(subject.resolve(api, SHA, BRANCH))

    def test_wrong_run_authority_source_attempt_or_outcome_is_rejected(self):
        mutations = [lambda a: a.run.update(head_sha="4" * 40), lambda a: a.run.update(head_branch="main"),
                     lambda a: a.run.update(event="pull_request"), lambda a: a.run.update(path="untrusted.yml"),
                     lambda a: a.run.update(workflow_id=0), lambda a: a.run["repository"].update(id=0),
                     lambda a: a.run["head_repository"].update(full_name="other/repository"),
                     lambda a: a.run["actor"].update(login="unapproved-actor"),
                     lambda a: a.run.update(run_attempt=2), lambda a: a.run.update(run_attempt=True),
                     lambda a: a.run.update(conclusion="failure"), lambda a: a.job.update(head_sha="4" * 40),
                     lambda a: a.job.update(run_id=0), lambda a: a.job.update(conclusion="failure"),
                     lambda a: a.job.update(run_attempt=2), lambda a: a.job.update(run_attempt=True),
                     lambda a: a.job.update(status="in_progress"),
                     lambda a: a.job["steps"][0].update(conclusion="failure"),
                     lambda a: a.job["steps"][1].update(conclusion="failure"),
                     lambda a: a.workflow.update(state="disabled_manually")]
        for index, mutate in enumerate(mutations):
            with self.subTest(index=index):
                api = Api(); mutate(api)
                with self.assertRaises(subject.ArtifactError): subject.resolve(api, SHA, BRANCH)

    def test_ambiguous_run_job_steps_and_artifacts_are_rejected(self):
        for kind in ("run", "job", "step", "artifact"):
            with self.subTest(kind=kind):
                api = Api()
                if kind == "run": api.runs.append({**api.run, "id": RUN + 1})
                elif kind == "job": api.jobs.append({**api.job, "id": JOB + 1})
                elif kind == "step": api.job["steps"].append(deepcopy(api.job["steps"][0]))
                else: api.artifacts.append({**api.artifact, "id": ARTIFACT + 1})
                with self.assertRaises(subject.ArtifactError): subject.resolve(api, SHA, BRANCH)

    def test_missing_expired_foreign_or_unhashed_artifact_is_rejected(self):
        for key, value in (("expired", True), ("size_in_bytes", 0), ("size_in_bytes", subject.MAX_ZIP + 1),
                           ("digest", "not-a-digest"), ("workflow_run", {"id": 0, "head_sha": SHA}),
                           ("workflow_run", {"id": RUN, "head_sha": "4" * 40})):
            with self.subTest(key=key, value=value):
                api = Api(); api.artifact[key] = value
                with self.assertRaises(subject.ArtifactError): subject.resolve(api, SHA, BRANCH)
        api = Api(); api.artifacts = []
        with self.assertRaises(subject.ArtifactError): subject.resolve(api, SHA, BRANCH)

    def test_list_completeness_bounds_and_unique_ids_are_enforced(self):
        for response in ({"total_count": 1001, "rows": []}, {"total_count": 2, "rows": [{"id": 1}]},
                         {"total_count": 2, "rows": [{"id": 1}, {"id": 1}]},
                         {"total_count": 1, "rows": [{"id": True}]}):
            with self.assertRaises(subject.ArtifactError): subject.collect(lambda _: response, subject.PREFIX + "x", "rows")
        first = [{"id": number} for number in range(1, 101)]
        rows = subject.collect(lambda path: {"total_count": 101, "rows": first if path.endswith("page=1") else [{"id": 101}]},
                               subject.PREFIX + "x?head_sha=" + SHA, "rows")
        self.assertEqual(len(rows), 101)


class ArchiveTests(unittest.TestCase):
    def test_synthetic_transport_preserves_each_original_member(self):
        self.assertEqual(subject.validate_zip(archive(), SHA, TREE, RUN, 8), fixture_files())

    def test_source_execution_and_runtime_mismatches_are_rejected(self):
        mutations = [
            ("sonar-native-host/source.json", "sourceSha", "4" * 40),
            ("sonar-native-host/source.json", "treeSha", "4" * 40),
            ("sonar-native-host/source.json", "attempt", True),
            ("sonar-native-host/source.json", "schemaVersion", True),
            ("sonar-native-host/source.json", "nativeCoverage", 1),
            ("sonar-native-host/execution.json", "retries", 1),
            ("sonar-native-host/execution.json", "junitCases", 9),
            ("sonar-native-host/execution.json", "requiredCases", True),
            ("sonar-native-host/execution.json", "status", "SKIP"),
            ("sonar-real-backend-native/environment.json", "postgresVersionNum", 170006),
            ("sonar-real-backend-native/environment.json", "backendImage", "unbound"),
            ("sonar-real-backend-native/environment.json", "hostNodeVersion", "v26.0.0"),
            ("sonar-real-backend-native/browser.json", "nodeVersion", "v26.0.0"),
            ("sonar-real-backend-native/browser.json", "suite", "mobile-mvp0"),
        ]
        for path, key, value in mutations:
            with self.subTest(path=path, key=key):
                files = fixture_files(); data = json.loads(files[path]); data[key] = value
                files[path] = json.dumps(data).encode()
                with self.assertRaises(subject.ArtifactError): subject.validate_zip(archive(files), SHA, TREE, RUN, 8)

    def test_missing_and_duplicate_members_unknown_paths_and_symlinks_are_rejected(self):
        files = fixture_files(); del files["sonar-native-host/source.json"]
        with self.assertRaises(subject.ArtifactError): subject.validate_zip(archive(files), SHA, TREE, RUN, 8)
        for name in ("../outside", "/outside", "sonar-native-host/../outside", "sonar-native-host\\outside", "unapproved.json"):
            with self.subTest(name=name):
                with self.assertRaises(subject.ArtifactError): subject.validate_zip(archive(extra=(name, b"fixture")), SHA, TREE, RUN, 8)
        link = zipfile.ZipInfo("sonar-native-host/raw/coverage-9-100-0.json")
        link.create_system = 3; link.external_attr = (stat.S_IFLNK | 0o777) << 16
        with self.assertRaises(subject.ArtifactError): subject.validate_zip(archive(extra=(link, b"/outside")), SHA, TREE, RUN, 8)
        with self.assertWarns(UserWarning): duplicate = archive(extra=("sonar-native-host/source.json", b"fixture"))
        with self.assertRaises(subject.ArtifactError): subject.validate_zip(duplicate, SHA, TREE, RUN, 8)

    def test_duplicate_or_nonfinite_json_cannot_override_provenance(self):
        for data in (b'{"sourceSha":"first","sourceSha":"second"}', b'{"schemaVersion":NaN}', b'[]'):
            files = fixture_files(); files["sonar-native-host/source.json"] = data
            with self.assertRaises((subject.ArtifactError, subject.binding.ReconciliationError)):
                subject.validate_zip(archive(files), SHA, TREE, RUN, 8)

    def test_output_is_owned_and_existing_receipts_are_not_overwritten(self):
        files = subject.validate_zip(archive(), SHA, TREE, RUN, 8)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve(); (root / "artifacts").mkdir()
            subject.retain(root, files, {"sourceSha": SHA})
            for name, data in files.items(): self.assertEqual((root / "artifacts/sonar-native-import" / name).read_bytes(), data)
            with self.assertRaises(FileExistsError): subject.retain(root, files, {"sourceSha": SHA})


class DownloadTests(unittest.TestCase):
    class Response(io.BytesIO):
        status = 200

    def test_authorization_is_never_forwarded_to_signed_blob(self):
        api = Api(); data = archive(); requests = []
        token = secrets.token_urlsafe(16)
        def open_request(request, timeout):
            requests.append(request)
            if len(requests) == 1:
                raise urllib.error.HTTPError(request.full_url, 302, "synthetic redirect",
                                             {"Location": "https://syntheticfixture.blob.core.windows.net/report?not-a-live-signature"}, io.BytesIO())
            return self.Response(data)
        with patch.object(subject.urllib.request, "build_opener") as factory:
            factory.return_value.open.side_effect = open_request
            self.assertEqual(subject.download(api.artifact, token), data)
        self.assertEqual(requests[0].get_header("Authorization"), "Bearer " + token)
        self.assertIsNone(requests[1].get_header("Authorization"))

    def test_foreign_or_credentialed_redirects_and_digest_mismatch_are_rejected(self):
        for url in ("http://syntheticfixture.blob.core.windows.net/report", "https://unapproved.test/report",
                    "https://user@syntheticfixture.blob.core.windows.net/report", "https://syntheticfixture.blob.core.windows.net.evil.test/report"):
            with self.subTest(url=url), patch.object(subject.urllib.request, "build_opener") as factory:
                factory.return_value.open.side_effect = urllib.error.HTTPError("https://api.github.com/", 302, "synthetic", {"Location": url}, io.BytesIO())
                with self.assertRaises(subject.ArtifactError): subject.download(Api().artifact, secrets.token_urlsafe(16))
        with patch.object(subject.urllib.request, "build_opener") as factory:
            factory.return_value.open.return_value = self.Response(b"SYNTHETIC_WRONG_ARCHIVE")
            with self.assertRaises(subject.ArtifactError): subject.download(Api().artifact, secrets.token_urlsafe(16))


class CliTests(unittest.TestCase):
    def test_unavailable_and_valid_metadata_are_reported_without_network(self):
        for available in (False, True):
            with self.subTest(available=available), tempfile.TemporaryDirectory() as directory:
                root = Path(directory).resolve(); (root / "artifacts").mkdir()
                manifest = root / "scripts/ci/real-backend-pr-p0-required-tests.txt"
                manifest.parent.mkdir(parents=True); manifest.write_text("\n".join("synthetic " + str(i) for i in range(8)))
                api = Api()
                if not available: api.runs = []
                output = io.StringIO()
                with patch.object(sys, "argv", ["reader", "--sha", SHA, "--tree", TREE, "--branch", BRANCH]), \
                        patch.object(subject, "LiveGitHub", return_value=api), patch.object(subject, "download", return_value=archive()), \
                        patch.object(subject.Path, "cwd", return_value=root), redirect_stdout(output):
                    self.assertEqual(subject.main(), 0)
                self.assertIn("available=" + str(available).lower(), output.getvalue())

    def test_failure_is_sanitized(self):
        output = io.StringIO()
        api = Api(); api.run["actor"]["login"] = "unapproved-actor"
        with patch.object(sys, "argv", ["reader", "--sha", SHA, "--tree", TREE, "--branch", BRANCH]), \
                patch.object(subject, "LiveGitHub", return_value=api), redirect_stderr(output):
            self.assertEqual(subject.main(), 1)
        self.assertEqual(output.getvalue(), "Native coverage artifact did not qualify.\n")


if __name__ == "__main__":
    unittest.main()
