"""Exercise missing/stale/flaky evidence and artifact boundary failures."""
import copy
import io
import json
import os
import subprocess
import sys
import tempfile
import textwrap
import unittest
import zipfile
import importlib.util
import urllib.request
import hashlib
from unittest.mock import patch
from datetime import date
from pathlib import Path

from functional_evidence import OWNERS, expected_owners, validate_manifest

SHA = "a" * 40


def complete_manifest():
    lanes = [{
        "schemaVersion": 1, "commitSha": SHA, "gate": "functional-full", "runId": "100", "runAttempt": "1",
        "suite": suite, "startedAt": "2026-10-04T00:00:00Z", "completedAt": "2026-10-04T00:00:10Z",
        "setupSeconds": 10, "testSeconds": 10,
        "journeys": [{"journeyId": owner, "status": "PASS", "attempts": 1, "durationMs": 1000}
                     for owner in expected_owners(suite, "functional-full")],
    } for suite in OWNERS]
    return {"schemaVersion": 1, "commitSha": SHA, "gate": "functional-full", "runId": "100", "runAttempt": "1", "lanes": lanes}


class FunctionalEvidenceTests(unittest.TestCase):
    def validate(self, data):
        return validate_manifest(data, SHA, "functional-full", "100", "1")

    def execute_extended_producer(self, runs, *, snapshots=None, request_seconds=0, target_sha=SHA,
                                  monotonic_advances=()):
        workflow = Path(__file__).resolve().parents[2] / ".github/workflows/functional-extended.yml"
        source = workflow.read_text(encoding="utf-8").split("          python3 - <<'PY'\n", 1)[1].split("          PY", 1)[0]
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "producer-output"
            environment = {"GITHUB_API_URL": "https://api.github.com", "GITHUB_REPOSITORY": "NYGsatoshi/Coglatas",
                           "TARGET_SHA": target_sha, "GITHUB_TOKEN": "synthetic-token", "GITHUB_OUTPUT": str(output)}
            clock = [0]
            observations = snapshots or [runs]
            advances = iter(monotonic_advances)
            self.producer_requests = []
            self.producer_sleeps = []

            def fetch(request, timeout):
                self.producer_requests.append((request.full_url, timeout))
                clock[0] += request_seconds
                value = observations[min(len(self.producer_requests) - 1, len(observations) - 1)]
                if isinstance(value, Exception):
                    raise value
                return io.BytesIO(json.dumps({"workflow_runs": value}).encode())

            def sleep(seconds):
                self.assertGreater(seconds, 0)
                self.producer_sleeps.append(seconds)
                clock[0] += seconds

            def monotonic():
                clock[0] += next(advances, 0)
                return clock[0]

            try:
                with patch.dict(os.environ, environment), patch("urllib.request.urlopen", side_effect=fetch), \
                        patch("time.monotonic", side_effect=monotonic), patch("time.sleep", side_effect=sleep):
                    exec(compile(textwrap.dedent(source), str(workflow), "exec"), {})
            finally:
                self.producer_output = output.read_text(encoding="utf-8") if output.exists() else ""
                self.producer_elapsed = clock[0]
            return self.producer_output

    def main_producer(self, **changes):
        return {"id": 100, "head_sha": SHA, "event": "push", "head_branch": "main",
                "path": ".github/workflows/main-build-artifacts.yml", "status": "completed",
                "conclusion": "success", **changes}

    def test_extended_selects_latest_completed_trusted_producer(self):
        runs = [self.main_producer(id=101), self.main_producer()]
        self.assertEqual(self.execute_extended_producer(runs), "run_id=101\n")

    def test_extended_latest_cancelled_cannot_fall_back_to_older_success(self):
        runs = [self.main_producer(), self.main_producer(id=101, conclusion="cancelled")]
        with self.assertRaisesRegex(AssertionError, "older runs cannot substitute"):
            self.execute_extended_producer(runs)

    def test_extended_latest_incomplete_cannot_fall_back_to_older_success(self):
        for state in ("queued", "in_progress", "completed"):
            with self.subTest(state=state):
                runs = [self.main_producer(), self.main_producer(id=101, status=state, conclusion=None)]
                expected = "older runs cannot substitute" if state == "completed" else "budget expired"
                with self.assertRaisesRegex(AssertionError, expected):
                    self.execute_extended_producer(runs)
                self.assertEqual(self.producer_output, "")
                self.assertLessEqual(self.producer_elapsed, 5 * 60)

    def test_extended_waits_for_same_latest_exact_producer_without_using_older_success(self):
        old = self.main_producer()
        queued = self.main_producer(id=101, status="queued", conclusion=None)
        active = dict(queued, status="in_progress")
        complete = dict(active, status="completed", conclusion="success")
        result = self.execute_extended_producer([], snapshots=[[old, queued], [old, active], [old, complete]])
        self.assertEqual(result, "run_id=101\n")
        self.assertEqual(len(self.producer_requests), 3)
        self.assertEqual(self.producer_sleeps, [1, 1])
        self.assertTrue(all(f"head_sha={SHA}&event=push" in url for url, _ in self.producer_requests))

    def test_extended_preserves_completed_failure_producer_semantics(self):
        for conclusion in ("failure", "timed_out", "action_required", "neutral", "skipped"):
            with self.subTest(conclusion=conclusion):
                self.assertEqual(self.execute_extended_producer([self.main_producer(conclusion=conclusion)]), "run_id=100\n")
                self.assertEqual(self.producer_sleeps, [])

    def test_extended_wait_fails_closed_on_cancelled_missing_invalid_or_superseded_producer(self):
        active = self.main_producer(id=101, status="in_progress", conclusion=None)
        for replacement in ([dict(active, status="completed", conclusion="cancelled")], [],
                            [dict(active, status="completed")], [dict(active, status="unknown")],
                            [dict(active, conclusion="success")], [dict(active, id="101")],
                            [self.main_producer(id=102)], [self.main_producer(id=100)]):
            with self.subTest(replacement=replacement), self.assertRaises(AssertionError):
                self.execute_extended_producer([], snapshots=[[active], replacement])
            self.assertEqual(self.producer_output, "")
            self.assertEqual(len(self.producer_requests), 2)
            self.assertEqual(self.producer_sleeps, [1])

    def test_extended_budget_covers_api_time_and_rejects_completion_after_expiry(self):
        active = self.main_producer(status="in_progress", conclusion=None)
        with self.assertRaisesRegex(AssertionError, "budget expired"):
            self.execute_extended_producer([], snapshots=[[active], [self.main_producer()]], request_seconds=150)
        self.assertEqual(self.producer_output, "")
        self.assertEqual(self.producer_requests[1][1], 30)

    def test_extended_request_timeout_is_clipped_to_remaining_budget(self):
        active = self.main_producer(status="in_progress", conclusion=None)
        with self.assertRaisesRegex(AssertionError, "budget expired"):
            self.execute_extended_producer([active], request_seconds=298)
        self.assertEqual(self.producer_requests[1][1], 1)
        self.assertEqual(self.producer_output, "")

    def test_extended_budget_crossed_before_wait_fails_without_sleep_or_output(self):
        active = self.main_producer(status="in_progress", conclusion=None)
        with self.assertRaisesRegex(AssertionError, "producer prerequisite budget expired"):
            self.execute_extended_producer([active], monotonic_advances=(0, 0, 0, 5 * 60))
        self.assertEqual(self.producer_elapsed, 5 * 60)
        self.assertEqual(len(self.producer_requests), 1)
        self.assertEqual(self.producer_sleeps, [])
        self.assertEqual(self.producer_output, "")

    def test_extended_api_failure_is_not_retried_and_invalid_sha_never_queries(self):
        with self.assertRaisesRegex(RuntimeError, "synthetic API failure"):
            self.execute_extended_producer([], snapshots=[RuntimeError("synthetic API failure")])
        self.assertEqual(len(self.producer_requests), 1)
        self.assertEqual(self.producer_sleeps, [])
        self.assertEqual(self.producer_output, "")
        with self.assertRaisesRegex(AssertionError, "Invalid exact candidate"):
            self.execute_extended_producer([self.main_producer()], target_sha="invalid")
        self.assertEqual(self.producer_requests, [])
        self.assertEqual(self.producer_output, "")

    def test_extended_prerequisite_preserves_candidate_guards_and_five_minute_job_budget(self):
        workflow = (Path(__file__).resolve().parents[2] / ".github/workflows/functional-extended.yml").read_text()
        self.assertIn("timeout-minutes: 5", workflow)
        self.assertIn("deadline = time.monotonic() + 5 * 60", workflow)
        self.assertIn('test "$GITHUB_REF" = refs/heads/main', workflow)
        self.assertIn('git merge-base --is-ancestor "$candidate" origin/main || exit 1', workflow)
        self.assertIn('[[ "$candidate" =~ ^[0-9a-f]{40}$ ]] || exit 1', workflow)

    def test_extended_rejects_wrong_producer_trust_identity(self):
        for field, value in (("head_sha", "b" * 40), ("event", "pull_request"),
                             ("head_branch", "candidate"), ("path", ".github/workflows/ci.yml")):
            with self.subTest(field=field):
                untrusted = self.main_producer(id=101, **{field: value})
                with self.assertRaisesRegex(AssertionError, "No trusted exact-candidate"):
                    self.execute_extended_producer([untrusted])
                self.assertEqual(self.execute_extended_producer([self.main_producer(), untrusted]), "run_id=100\n")

    def test_complete_exact_run_passes(self):
        self.validate(complete_manifest())

    def test_missing_duplicate_and_unknown_domains_fail(self):
        for mutation in (lambda value: value["lanes"].pop(),
                         lambda value: value["lanes"].__setitem__(1, copy.deepcopy(value["lanes"][0]))):
            data = complete_manifest()
            mutation(data)
            with self.assertRaises(ValueError):
                self.validate(data)

    def test_stale_commit_run_attempt_and_schema_fail(self):
        for field, value in (("commitSha", "b" * 40), ("runId", "99"), ("runAttempt", "2"), ("schemaVersion", 2)):
            data = complete_manifest()
            data["lanes"][0][field] = value
            with self.assertRaises(ValueError):
                self.validate(data)

    def test_no_failure_state_or_retry_can_be_green(self):
        for status in ("FAIL", "FLAKY", "SKIPPED", "QUARANTINED", "BLOCKED", "NOT_RUN"):
            data = complete_manifest()
            data["lanes"][0]["journeys"][0]["status"] = status
            with self.assertRaises(ValueError):
                self.validate(data)
        data = complete_manifest()
        data["lanes"][0]["journeys"][0]["attempts"] = 2
        with self.assertRaises(ValueError):
            self.validate(data)

    def test_missing_duplicate_and_protected_owner_fields_fail(self):
        for field, value in (("journeyId", "FUNC-FILE-002"), ("protectedBody", "do not retain")):
            data = complete_manifest()
            data["lanes"][0]["journeys"][0][field] = value
            with self.assertRaises(ValueError):
                self.validate(data)
        data = complete_manifest()
        data["lanes"][0]["journeys"] = []
        with self.assertRaises(ValueError):
            self.validate(data)

    def test_zip_reader_rejects_paths_extra_files_and_oversize(self):
        spec = importlib.util.spec_from_file_location("final_checks", Path(__file__).with_name("verify-mvp-a-final-checks.py"))
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        for paths in (("../manifest.json",), ("manifest.json", "protected.txt")):
            buffer = io.BytesIO()
            with zipfile.ZipFile(buffer, "w") as archive:
                for path in paths:
                    archive.writestr(path, json.dumps(complete_manifest()))
            with self.assertRaises(RuntimeError):
                module.read_manifest_archive(buffer.getvalue())
        with self.assertRaises(RuntimeError):
            module.read_manifest_archive(b"x" * (1024 * 1024 + 1))

    def test_signed_redirect_drops_api_authorization(self):
        spec = importlib.util.spec_from_file_location("final_checks", Path(__file__).with_name("verify-mvp-a-final-checks.py"))
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        request = urllib.request.Request("https://api.github.com/artifact", headers={"Authorization": "Bearer synthetic-token"})
        redirected = module.MetadataRedirectHandler().redirect_request(request, None, 302, "Found", {}, "https://storage.example.test/metadata.zip")
        self.assertFalse(redirected.has_header("Authorization"))

    def test_quarantine_requires_tracking_metadata_and_expiring_review(self):
        spec = importlib.util.spec_from_file_location("quarantine", Path(__file__).with_name("validate-functional-quarantine.py"))
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        entry = {"issue": "https://github.com/NYGsatoshi/Coglatas/issues/611", "reason": "Tracked synthetic instability",
                 "owner": "ci-maintainers", "domain": "core", "quarantinedAt": "2026-10-01", "reviewBy": "2026-10-10",
                 "journeyId": "FUNC-TASK-001", "p0ReadinessImpact": "Required owner remains blocked"}
        self.assertEqual(module.validate({"schemaVersion": 1, "entries": [entry]}, date(2026, 10, 4)), 1)
        for altered in (dict(entry, reviewBy="2026-10-03"), {key: value for key, value in entry.items() if key != "issue"}):
            with self.assertRaises(ValueError):
                module.validate({"schemaVersion": 1, "entries": [altered]}, date(2026, 10, 4))

    def test_live_consumer_binds_latest_trusted_main_run_attempt_and_digest(self):
        spec = importlib.util.spec_from_file_location("final_checks", Path(__file__).with_name("verify-mvp-a-final-checks.py"))
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        check = {"id": 1000, "name": "functional-full", "head_sha": SHA, "status": "completed", "conclusion": "success",
                 "app": {"id": module.GITHUB_ACTIONS_APP_ID, "slug": module.GITHUB_ACTIONS_APP_SLUG},
                 "details_url": "https://github.com/NYGsatoshi/Coglatas/actions/runs/100/job/200"}
        run = {"head_sha": SHA, "event": "push", "head_branch": "main", "path": ".github/workflows/main-build-artifacts.yml",
               "status": "completed", "conclusion": "success", "run_attempt": 1}
        buffer = io.BytesIO()
        with zipfile.ZipFile(buffer, "w") as archive:
            archive.writestr("manifest.json", json.dumps(complete_manifest()))
        archive = buffer.getvalue()
        artifact = {"id": 300, "name": f"functional-evidence-functional-full-{SHA}-1", "expired": False,
                    "digest": "sha256:" + hashlib.sha256(archive).hexdigest()}
        class Opener:
            def open(self, *args, **kwargs):
                return io.BytesIO(archive)
        with patch.object(module, "fetch_page", side_effect=[run, {"artifacts": [artifact], "total_count": 1}]), patch.object(module.urllib.request, "build_opener", return_value=Opener()):
            module.verify_functional_evidence([check], "NYGsatoshi/Coglatas", SHA, "synthetic-token", "https://api.github.com")
        for altered in (dict(run, head_sha="b" * 40), dict(run, event="pull_request"), dict(run, conclusion="cancelled"), dict(run, status="in_progress")):
            with patch.object(module, "fetch_page", return_value=altered), self.assertRaises(RuntimeError):
                module.verify_functional_evidence([check], "NYGsatoshi/Coglatas", SHA, "synthetic-token", "https://api.github.com")
        newer = dict(check, id=1001, status="queued", conclusion=None)
        with self.assertRaises(RuntimeError):
            module.verify_functional_evidence([check, newer], "NYGsatoshi/Coglatas", SHA, "synthetic-token", "https://api.github.com")


class FunctionalLaneSummaryCliTests(unittest.TestCase):
    """Exercise the real failure-capable summary command and its privacy boundary."""

    def lane(self):
        data = next(lane for lane in complete_manifest()["lanes"] if lane["suite"] == "authz-negative")
        data["setupSeconds"] = 11
        data["testSeconds"] = 23
        data["journeys"][0]["durationMs"] = 3000
        data["journeys"][1]["durationMs"] = 9000
        return data

    def execute_finalizer(self, data=None, *, raw=None, diagnostics=None):
        script = Path(__file__).resolve().with_name("finalize-functional-lane.py")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            metadata = root / "artifacts/functional/lane-authz-negative.json"
            original = raw if raw is not None else (json.dumps(data, indent=4) + "\n").encode() if data is not None else None
            if original is not None:
                metadata.parent.mkdir(parents=True)
                metadata.write_bytes(original)
            summary = root / "summary.md"
            summary.write_text("Existing workflow summary.\n", encoding="utf-8")
            runtime = root / "runner"
            runtime.mkdir()
            if diagnostics:
                diagnostic = runtime / "functional-diagnostics-authz-negative/functional-container-states.ndjson"
                diagnostic.parent.mkdir()
                if diagnostics == "file":
                    diagnostic.write_text("protected-diagnostic-body-must-not-appear", encoding="utf-8")
                else:
                    diagnostic.mkdir()
            environment = {**os.environ, "COGLATAS_FUNCTIONAL_DOMAIN": "authz-negative",
                           "COGLATAS_FUNCTIONAL_SELECTED_GATES": "functional-full", "TARGET_SHA": SHA,
                           "GITHUB_RUN_ID": "100", "GITHUB_RUN_ATTEMPT": "1",
                           "GITHUB_STEP_SUMMARY": str(summary), "RUNNER_TEMP": str(runtime),
                           "PYTHONIOENCODING": "utf-8"}
            result = subprocess.run([sys.executable, "-B", str(script)], cwd=root, env=environment,
                                    capture_output=True, text=True, encoding="utf-8", timeout=15, check=False)
            retained = metadata.read_bytes() if metadata.is_file() else None
            return result, summary.read_text(encoding="utf-8"), original, retained

    def assert_counts(self, summary, **counts):
        for state in ("PASS", "FAIL", "FLAKY", "SKIPPED", "QUARANTINED", "BLOCKED"):
            self.assertIn(f"| {state} | {counts.get(state, 0)} |", summary)

    def assert_refused(self, *, data=None, raw=None):
        result, summary, original, retained = self.execute_finalizer(data, raw=raw)
        self.assertEqual(1, result.returncode, result.stdout + result.stderr)
        self.assertIn("REFUSED", summary)
        self.assertIn("First-attempt qualification: NO-GO", summary)
        self.assertNotRegex(summary, r"(?<!NO-)\bGO\b")
        self.assertTrue(summary.startswith("Existing workflow summary.\n"))
        self.assertEqual(original, retained)
        output = summary + result.stdout + result.stderr
        self.assertNotIn("protected-payload-must-not-appear", output)
        self.assertNotIn("Traceback", output)

    def test_pass_summary_is_bound_complete_and_does_not_rewrite_metadata(self):
        result, summary, original, retained = self.execute_finalizer(self.lane())
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual(original, retained)
        self.assertTrue(summary.startswith("Existing workflow summary.\n"))
        for value in (SHA, "functional-full", "authz-negative"):
            self.assertIn(value, summary)
        self.assertRegex(summary, r"(?i)run[^\n]*\b100\b")
        self.assertRegex(summary, r"(?i)attempt[^\n]*\b1\b")
        self.assertRegex(summary, r"(?i)setup[^\n]*\b11(?:\.0+)?\b")
        self.assertRegex(summary, r"(?i)test[^\n]*\b23(?:\.0+)?\b")
        self.assertIn("Retry count: 0", summary)
        self.assert_counts(summary, PASS=2)
        self.assertRegex(summary, r"FUNC-AUTHZ-001: PASS; attempts=1; duration=3000(?:\.0+)? ms")
        self.assertNotRegex(summary, r"\b(?:GO|NO-GO)\b")

    def test_failed_and_flaky_owners_are_reported_without_acceptance(self):
        for status, attempts, other, retries in (("FAIL", 1, "PASS", 0), ("FLAKY", 2, "BLOCKED", 1),
                                                 ("QUARANTINED", 0, "SKIPPED", 0)):
            with self.subTest(status=status):
                data = self.lane()
                data["journeys"][0].update(status=status, attempts=attempts)
                data["journeys"][1].update(status=other, attempts=0 if other in ("BLOCKED", "SKIPPED") else 1)
                result, summary, original, retained = self.execute_finalizer(data)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertEqual(original, retained)
                self.assert_counts(summary, **{status: 1, other: 1})
                self.assertIn(f"Retry count: {retries}", summary)
                self.assertRegex(summary, rf"FUNC-AUTHZ-001: {status}; attempts={attempts}; duration=3000(?:\.0+)? ms")
                self.assertIn("First-attempt qualification: NO-GO", summary)
                self.assertNotRegex(summary, r"(?<!NO-)\bGO\b")

    def test_retried_pass_retains_metadata_but_cannot_qualify_first_attempt(self):
        data = self.lane()
        data["journeys"][0]["attempts"] = 2
        result, summary, original, retained = self.execute_finalizer(data)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual(original, retained)
        self.assert_counts(summary, PASS=2)
        self.assertIn("Retry count: 1", summary)
        self.assertRegex(summary, r"FUNC-AUTHZ-001: PASS; attempts=2; duration=3000(?:\.0+)? ms")
        self.assertIn("First-attempt qualification: NO-GO", summary)
        self.assertNotRegex(summary, r"(?<!NO-)\bGO\b")
        manifest = complete_manifest()
        manifest["lanes"] = [data if lane["suite"] == "authz-negative" else lane for lane in manifest["lanes"]]
        with self.assertRaisesRegex(ValueError, "did not pass its first attempt"):
            validate_manifest(manifest, SHA, "functional-full", "100", "1")

    def test_slowest_owners_order_by_duration_then_stable_id(self):
        for tied in (False, True):
            with self.subTest(tied=tied):
                data = self.lane()
                if tied:
                    data["journeys"][1]["durationMs"] = 3000
                    data["journeys"].reverse()
                result, summary, original, retained = self.execute_finalizer(data)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertEqual(original, retained)
                slowest = summary.split("Slowest owners:", 1)[1].split("\n", 1)[0]
                first, second = ("FUNC-AUTHZ-001", "FUNC-AUTHZ-002") if tied else ("FUNC-AUTHZ-002", "FUNC-AUTHZ-001")
                self.assertLess(slowest.index(first), slowest.index(second))

    def test_missing_metadata_keeps_the_existing_blocked_fallback(self):
        result, summary, original, retained = self.execute_finalizer()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIsNone(original)
        data = json.loads(retained)
        for field, value in (("commitSha", SHA), ("gate", "functional-full"), ("suite", "authz-negative"),
                             ("runId", "100"), ("runAttempt", "1"), ("setupSeconds", 0), ("testSeconds", 0)):
            self.assertEqual(value, data[field])
        self.assertEqual(["FUNC-AUTHZ-001", "FUNC-AUTHZ-002"], [owner["journeyId"] for owner in data["journeys"]])
        self.assertTrue(all(owner["status"] == "BLOCKED" and owner["attempts"] == 0 for owner in data["journeys"]))
        self.assert_counts(summary, BLOCKED=2)
        self.assertIn("Retry count: 0", summary)
        self.assertIn("First-attempt qualification: NO-GO", summary)
        self.assertNotRegex(summary, r"(?<!NO-)\bGO\b")

    def test_artifact_names_distinguish_planned_upload_from_local_diagnostic_file(self):
        lane_name = "functional-lane-functional-full-authz-negative-1"
        diagnostic_name = "functional-diagnostics-functional-full-authz-negative-1"
        for diagnostics in (None, "directory", "file"):
            with self.subTest(diagnostics=diagnostics):
                result, summary, original, retained = self.execute_finalizer(self.lane(), diagnostics=diagnostics)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertEqual(original, retained)
                lane_line = next(line for line in summary.splitlines() if lane_name in line)
                self.assertRegex(lane_line, r"(?i)(planned|subsequent)")
                self.assertEqual(diagnostics == "file", diagnostic_name in summary)
                self.assertNotRegex(summary, r"(?i)(?:was|is|successfully)\s+uploaded|uploaded\s+(?:artifact|successfully)")
                self.assertNotIn("protected-diagnostic-body-must-not-appear", summary + result.stdout + result.stderr)

    def test_other_commit_gate_domain_run_and_attempt_are_refused(self):
        for field, value in (("commitSha", "b" * 40), ("gate", "functional-fast"), ("suite", "core"),
                             ("runId", "99"), ("runAttempt", "2")):
            with self.subTest(field=field):
                data = self.lane()
                data[field] = value
                self.assert_refused(data=data)

    def test_missing_duplicate_unknown_and_protected_fields_are_refused_privately(self):
        mutations = (
            lambda data: data.pop("setupSeconds"),
            lambda data: data.update(protectedBody="protected-payload-must-not-appear"),
            lambda data: data.update(journeys=[]),
            lambda data: data["journeys"].append(copy.deepcopy(data["journeys"][0])),
            lambda data: data["journeys"].__setitem__(1, copy.deepcopy(data["journeys"][0])),
            lambda data: data["journeys"][0].pop("attempts"),
            lambda data: data["journeys"][0].update(journeyId="FUNC-TASK-001"),
            lambda data: data["journeys"][0].update(journeyId="protected-payload-must-not-appear"),
            lambda data: data["journeys"][0].update(status="protected-payload-must-not-appear"),
            lambda data: data["journeys"][0].update(headers="protected-payload-must-not-appear"),
            lambda data: data.update(startedAt="protected-payload-must-not-appear"),
            lambda data: data.update(startedAt="protected-payload-must-not-appear" * 1000),
            lambda data: data.update(journeys="protected-payload-must-not-appear"),
        )
        for index, mutation in enumerate(mutations):
            with self.subTest(case=index):
                data = self.lane()
                mutation(data)
                self.assert_refused(data=data)

    def test_boolean_nonfinite_negative_and_oversized_scalars_are_refused(self):
        for field, values in (("schemaVersion", (True, 2)), ("setupSeconds", (True, -1, float("nan"), float("inf"), 86400)),
                              ("testSeconds", (False, float("-inf"), 86400))):
            for value in values:
                with self.subTest(field=field, value=value):
                    data = self.lane()
                    data[field] = value
                    self.assert_refused(data=data)
        for field, values in (("attempts", (True, 1.5, -1, 2 ** 64)),
                              ("durationMs", (False, -1, float("nan"), float("inf"), 86400000))):
            for value in values:
                with self.subTest(field=field, value=value):
                    data = self.lane()
                    data["journeys"][0][field] = value
                    self.assert_refused(data=data)

    def test_malformed_duplicate_key_and_oversized_metadata_are_refused_privately(self):
        original = (json.dumps(self.lane(), indent=4) + "\n").encode()
        duplicate_key = original.replace(b'"runId": "100"', b'"runId": "100", "runId": "100"', 1)
        for raw in (b'{"protectedBody": "protected-payload-must-not-appear"', b"\xffprotected-payload-must-not-appear",
                    b"null", b"[]", duplicate_key,
                    original + b" " * (256 * 1024)):
            with self.subTest(size=len(raw)):
                self.assert_refused(raw=raw)


if __name__ == "__main__":
    unittest.main()
