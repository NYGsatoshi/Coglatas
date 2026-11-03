#!/usr/bin/env python3
"""Positive and fail-closed controls for offline PR CI measurement."""
from __future__ import annotations

from contextlib import redirect_stdout
from datetime import datetime, timedelta, timezone
import copy
import importlib.util
import io
import json
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
script = ROOT / "scripts/ci/profile-pr-ci-jobs.py"
spec = importlib.util.spec_from_file_location("ci_profile", script)
assert spec is not None and spec.loader is not None
profile = importlib.util.module_from_spec(spec)
spec.loader.exec_module(profile)
SHA = "a" * 40
EPOCH = datetime(2026, 11, 4, tzinfo=timezone.utc)
DURATIONS = {
    "CI preflight + route": 10,
    ".NET build producer": 20,
    "Frontend build producer": 30,
    "build-test": 40,
    "frontend-test": 60,
    "security-scan": 50,
    "functional-fast": 3,
}


def stamp(i):
    return (EPOCH + timedelta(seconds=i)).isoformat().replace("+00:00", "Z")


def fixture():
    run = {
        "id": 1234, "run_attempt": 1, "name": "PR CI",
        "path": ".github/workflows/ci.yml",
        "event": "pull_request", "head_sha": SHA,
        "head_commit": {"id": SHA},
        "status": "completed", "conclusion": "success",
    }
    stage_names = {
        "build-test": ["Test"],
        "frontend-test": ["Run frontend checks in parallel"],
        "security-scan": [
            "Run isolated security runtime lanes in parallel",
            "Generate and verify SEC-01 / AV-MIG contract",
            "Download redistributed .NET build for security contract",
            "Build reusable security runtime image",
        ],
    }
    rows = []
    for index, (name, duration) in enumerate(DURATIONS.items(), start=1):
        steps = [{"name": step, "status": "completed",
                  "conclusion": "success", "started_at": stamp(0),
                  "completed_at": stamp(1)}
                 for step in stage_names.get(name, [])]
        rows.append({
            "id": index, "run_id": 1234, "run_attempt": 1,
            "head_sha": SHA, "name": name, "status": "completed",
            "conclusion": "success",
            "started_at": stamp(0), "completed_at": stamp(duration),
            "steps": steps,
        })
    return run, {"total_count": len(rows), "jobs": rows}


class NativeProfileTests(unittest.TestCase):
    def verify(self, run, jobs, sha=SHA):
        return profile.profile(run, jobs, expected_sha=sha)

    def test_full_native_success_only_produces_sanitized_timing(self):
        run, jobs = fixture()
        report = self.verify(run, jobs)
        self.assertEqual(213, report["totalRunnerSeconds"])
        self.assertEqual({"security": 80, "frontend": 100},
                         report["pathDurationSumsSeconds"])
        self.assertEqual("JOB_TIMING_ONLY", report["status"])
        self.assertEqual("NOT_PERFORMED", report["nativeTestAndScannerParity"])
        self.assertEqual("NOT_PERFORMED", report["requiredSixCheckAttestation"])
        self.assertEqual("NOT_ESTABLISHED", report["speedupAttribution"])
        self.assertNotIn("runner_name", json.dumps(report))

    def test_unknown_or_cross_source_revision_refused(self):
        for which, key, value in (
            ("run", "head_sha", "b" * 40),
            ("run", "path", ".github/workflows/main-validation.yml"),
            ("run", "conclusion", "failure"),
            ("run", "event", "push"),
            ("run", "run_attempt", True),
            ("job", "head_sha", "b" * 40),
            ("job", "run_id", True),
            ("job", "run_attempt", 2),
        ):
            with self.subTest(which=which, key=key):
                run, inventory = fixture()
                target = run if which == "run" else inventory["jobs"][0]
                target[key] = value
                with self.assertRaises(profile.IncompleteProfile):
                    self.verify(run, inventory)

    def test_missing_pagination_duplicate_and_skipped_owner_fail(self):
        for mode in ("missing", "truncated", "duplicate-id", "duplicate-name",
                     "skipped-required", "failed-job"):
            with self.subTest(mode=mode):
                run, inventory = fixture()
                rows = inventory["jobs"]
                if mode == "missing":
                    rows.pop()
                    inventory["total_count"] -= 1
                elif mode == "truncated":
                    inventory["total_count"] += 1
                elif mode == "duplicate-id":
                    rows[1]["id"] = rows[0]["id"]
                elif mode == "duplicate-name":
                    rows[1]["name"] = rows[0]["name"]
                elif mode == "skipped-required":
                    rows[0]["conclusion"] = "skipped"
                elif mode == "failed-job":
                    rows[0]["conclusion"] = "failure"
                with self.assertRaises(profile.IncompleteProfile):
                    self.verify(run, inventory)

    def test_required_native_execution_cannot_be_inferred_from_green_job(self):
        for owner, target in profile.OWNING_STEPS.items():
            with self.subTest(owner=owner):
                run, inventory = fixture()
                row = next(j for j in inventory["jobs"] if j["name"] == owner)
                row["steps"] = [x for x in row["steps"] if x["name"] != target]
                with self.assertRaises(profile.IncompleteProfile):
                    self.verify(run, inventory)

    def test_duplicate_or_unsuccessful_security_step_rejected(self):
        for mode in ("duplicate", "skipped", "missing-times"):
            with self.subTest(mode=mode):
                run, inventory = fixture()
                job = next(j for j in inventory["jobs"] if j["name"] == "security-scan")
                step = job["steps"][0]
                if mode == "duplicate":
                    job["steps"].append(copy.deepcopy(step))
                elif mode == "skipped":
                    step["conclusion"] = "skipped"
                else:
                    step["completed_at"] = None
                with self.assertRaises(profile.IncompleteProfile):
                    self.verify(run, inventory)

    def test_invalid_timestamps_and_excessive_duration_rejected(self):
        for start, end in (
            ("2026-11-04T00:00:00", stamp(1)),
            (stamp(2), stamp(1)),
            (stamp(0), stamp(profile.MAX_JOB_DURATION_SECONDS + 1)),
            (123, stamp(1)),
        ):
            with self.subTest(start=start, end=end):
                run, inventory = fixture()
                inventory["jobs"][0]["started_at"] = start
                inventory["jobs"][0]["completed_at"] = end
                with self.assertRaises(profile.IncompleteProfile):
                    self.verify(run, inventory)

    def test_boolean_and_float_identifiers_not_promoted(self):
        for key in ("id", "run_id", "run_attempt"):
            for value in (True, 1.0, "1"):
                with self.subTest(key=key, value=value):
                    run, inventory = fixture()
                    inventory["jobs"][0][key] = value
                    with self.assertRaises(profile.IncompleteProfile):
                        self.verify(run, inventory)

    def test_duplicate_json_fields_and_nonfinite_values_fail(self):
        with self.assertRaisesRegex(profile.IncompleteProfile, "DUPLICATE_JSON_FIELD"):
            json.loads('{"id":1,"id":2}', object_pairs_hook=profile.unique_pairs)
        with self.assertRaisesRegex(profile.IncompleteProfile, "NONFINITE_JSON"):
            json.loads('{"id":NaN}', parse_constant=profile.refuse_constant)

    def test_reader_rejects_symlinks_and_large_untrusted_json(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            path = folder / "run.json"
            path.write_text('{"id":1}', encoding="utf-8")
            self.assertEqual({"id": 1}, profile.read_json(path, 100))
            with self.assertRaises(profile.IncompleteProfile):
                profile.read_json(path, 2)
            link = folder / "link.json"
            link.symlink_to(path)
            with self.assertRaises((OSError, profile.IncompleteProfile)):
                profile.read_json(link, 100)

    def test_public_native_run_metadata_samples_replay_exact_duration_sums(self):
        # Selected original REST job metadata; no test logs, runner names or secrets.
        fixture = ROOT / "tests/ci/pr-ci-profile-public-fixtures.json"
        history = json.loads(fixture.read_text(encoding="utf-8"))
        self.assertEqual("coglatas.pr-ci-public-job-metadata-regression.v1",
                         history["schema"])
        self.assertEqual(3, len(history["captures"]))
        for sample in history["captures"]:
            with self.subTest(run=sample["run"]["id"]):
                actual = self.verify(
                    sample["run"], sample["jobs"],
                    sha=sample["run"]["head_sha"])
                expected = sample["expected"]
                self.assertEqual(expected["totalRunnerSeconds"],
                                 actual["totalRunnerSeconds"])
                self.assertEqual(expected["securityPathSeconds"],
                                 actual["pathDurationSumsSeconds"]["security"])
                self.assertEqual(expected["frontendPathSeconds"],
                                 actual["pathDurationSumsSeconds"]["frontend"])
                self.assertEqual(len(sample["jobs"]["jobs"]), actual["jobCount"])
                self.assertEqual("JOB_TIMING_ONLY", actual["status"])
                self.assertEqual("NOT_ESTABLISHED", actual["speedupAttribution"])
                changed = copy.deepcopy(sample["jobs"])
                changed["jobs"][0]["head_sha"] = "b" * 40
                with self.assertRaises(profile.IncompleteProfile):
                    self.verify(sample["run"], changed,
                                sha=sample["run"]["head_sha"])

    def test_cli_reports_only_closed_metrics(self):
        run, jobs = fixture()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            p1, p2 = root / "run.json", root / "jobs.json"
            p1.write_text(json.dumps(run), encoding="utf-8")
            p2.write_text(json.dumps(jobs), encoding="utf-8")
            with io.StringIO() as stream, redirect_stdout(stream):
                exit_code = profile.main([
                    "--run-json", str(p1), "--jobs-json", str(p2), "--sha", SHA])
                message = stream.getvalue()
            self.assertEqual(0, exit_code)
            self.assertEqual(213, json.loads(message)["totalRunnerSeconds"])
            self.assertNotIn("CI preflight", message)
            self.assertNotIn("runner_name", message)


if __name__ == "__main__":
    unittest.main()
