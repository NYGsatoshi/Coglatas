from __future__ import annotations

import importlib.util
import json
import re
import subprocess
import sys
import tempfile
import zipfile
import unittest
from pathlib import Path
from unittest.mock import Mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts/performance"))
from common import PerformanceContractError, DB_FIXTURE_VERSION, fixture_hash, load_json
from db_gate import index_kinds
from test_performance_db import capture, module as comparison

spec = importlib.util.spec_from_file_location("performance_db_ci", ROOT / "scripts/performance/db-ci.py")
ci = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ci)
HEAD = "a" * 40
BASE = "b" * 40


class PerformanceDbRoutingTests(unittest.TestCase):
    def git(self, paths=None, failure=False):
        rev = subprocess.CompletedProcess([], 0, stdout=HEAD + "\n")
        diff = subprocess.CalledProcessError(128, []) if failure else subprocess.CompletedProcess([], 0, stdout=b"\0".join(p.encode() for p in (paths or [])) + b"\0")
        return Mock(side_effect=[rev, diff])

    def test_only_explicit_unrelated_changes_are_not_applicable(self):
        result = ci.route(HEAD, BASE, "pull_request", self.git(["docs/ARCHITECTURE.md", "frontend/src/app/main.ts"]))
        self.assertFalse(result["required"])
        self.assertEqual("validated-not-applicable", result["reasonCode"])
        self.assertEqual(HEAD, result["headSha"])
        self.assertEqual(2, result["changedFileCount"])
        self.assertRegex(result["changedPathsHash"], "^[a-f0-9]{64}$")

    def test_backend_dependency_workflow_and_unknown_changes_run(self):
        for path in ("src/Coglatas.Web/Program.cs", "Directory.Packages.props", ".github/workflows/ci.yml", "new-feature/config.json", "performance/environment.json"):
            with self.subTest(path=path):
                self.assertTrue(ci.route(HEAD, BASE, "pull_request", self.git([path]))["required"])

    def test_missing_failed_or_empty_diff_runs_conservatively(self):
        for event, base in (("schedule", None), ("workflow_dispatch", None), ("push", "0" * 40), ("pull_request", None)):
            self.assertTrue(ci.route(HEAD, base, event, self.git())["required"])
        self.assertTrue(ci.route(HEAD, BASE, "pull_request", self.git([]))["required"])
        result = ci.route(HEAD, BASE, "pull_request", self.git(failure=True))
        self.assertEqual("conservative-diff-failure", result["reasonCode"])
        self.assertTrue(result["required"])

    def test_route_cannot_describe_a_different_checkout(self):
        git = Mock(return_value=subprocess.CompletedProcess([], 0, stdout=BASE))
        with self.assertRaises(PerformanceContractError):
            ci.route(HEAD, BASE, "pull_request", git)

    def test_applicable_missing_cancelled_skipped_or_failed_measurements_never_pass(self):
        route = {"schemaVersion": 1, "headSha": HEAD, "required": True, "reasonCode": "relevant-change"}
        for status in ("failure", "cancelled", "skipped", ""):
            with self.assertRaises(PerformanceContractError):
                ci.gate_decision(route, status, None, HEAD)
        for measurement in (None, {"schemaVersion": 1, "headSha": BASE, "decision": "pass"}):
            with self.assertRaises(PerformanceContractError):
                ci.gate_decision(route, "success", measurement, HEAD)
        self.assertEqual("pass", ci.gate_decision(route, "success", {"schemaVersion": 1, "headSha": HEAD, "decision": "pass"}, HEAD))
        for decision in ("regression", "invalid", "unstable", "insufficient-data"):
            self.assertEqual(decision, ci.gate_decision(route, "success", {"schemaVersion": 1, "headSha": HEAD, "decision": decision}, HEAD))

    def test_not_applicable_requires_route_evidence_and_skipped_collector(self):
        route = {"schemaVersion": 1, "headSha": HEAD, "required": False, "reasonCode": "validated-not-applicable"}
        self.assertEqual("not-applicable", ci.gate_decision(route, "skipped", None, HEAD))
        for route_change, status in (({"reasonCode": "unknown"}, "skipped"), ({"headSha": BASE}, "skipped"), ({}, "success")):
            with self.assertRaises(PerformanceContractError):
                ci.gate_decision(route | route_change, status, None, HEAD)

    def test_route_only_named_artifact_layout_reaches_the_actual_gate_cli(self):
        # Download-artifact v8 flattens a one-item pattern into its target.
        # Exercise the workflow's explicit named target with the actual ZIP/CLI.
        workflow = (ROOT / ".github/workflows/performance-db.yml").read_text().split("  gate:", 1)[1]
        named_target = re.search(r"name: perf05-route\s+path: (\S+)", workflow)
        self.assertIsNotNone(named_target, "Route evidence needs an explicit named download target")
        route_argument = re.search(r"--route (\S+)", workflow).group(1)
        routing = {"schemaVersion": 1, "headSha": HEAD, "required": False,
                   "reasonCode": "validated-not-applicable"}
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            archive = root / "route.zip"
            with zipfile.ZipFile(archive, "w") as artifact:
                artifact.writestr("route.json", json.dumps(routing))
            with zipfile.ZipFile(archive) as artifact:
                artifact.extractall(root / named_target.group(1))
            output = root / "gate.json"
            command = [sys.executable, str(ROOT / "scripts/performance/db-ci.py"), "gate",
                       "--head", HEAD, "--route", route_argument, "--collector-result", "skipped",
                       "--measurement", "absent.json", "--output", str(output)]
            result = subprocess.run(command, cwd=root, capture_output=True, text=True)
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertEqual("not-applicable", json.loads(output.read_text())["decision"])
            (root / route_argument).unlink()
            missing = subprocess.run(command, cwd=root, capture_output=True, text=True)
            self.assertNotEqual(0, missing.returncode)
            self.assertEqual("invalid", json.loads(output.read_text())["decision"])

    def test_stable_aggregate_survives_doc_only_changes(self):
        workflow = (ROOT / ".github/workflows/performance-db.yml").read_text()
        triggers = workflow.split("permissions:", 1)[0]
        self.assertNotIn("paths:", triggers)
        self.assertIn("name: PostgreSQL query regression gate", workflow)
        self.assertIn("needs: [route, collect]", workflow)
        self.assertIn("--expected-sha", workflow)
        self.assertIn("Finalize exact-SHA aggregate evidence\n        if: always()", workflow)


class PerformanceDbBuildResolverTests(unittest.TestCase):
    def build_run(self, **changes):
        return {"id": 17, "run_attempt": 1, "head_sha": HEAD, "head_branch": "main",
                "event": "push", "status": "completed", "conclusion": "success",
                "path": ".github/workflows/main-build-artifacts.yml"} | changes

    def artifact(self, **changes):
        return {"id": 27, "name": "main-build-artifacts", "expired": False,
                "size_in_bytes": 100, "digest": "sha256:" + "c" * 64,
                "workflow_run": {"id": 17, "head_sha": HEAD}} | changes

    def api(self, *, run_change=None, job_result="success", artifact_change=None):
        run = self.build_run(**(run_change or {}))
        artifact = self.artifact(**(artifact_change or {}))
        def fetch(path):
            if "/workflows/" in path:
                return {"workflow_runs": [run]}
            if "/jobs?" in path:
                self.assertIn("/attempts/1/jobs?", path)
                return {"jobs": [{"name": "Main runtime artifact assembler", "status": "completed",
                                  "conclusion": job_result}]}
            return {"artifacts": [artifact]}
        return fetch

    def test_exact_main_artifact_requires_its_successful_producer(self):
        self.assertEqual("17", ci.resolve_build("NYGsatoshi/Coglatas", HEAD, self.api()))
        for status in ("failure", "cancelled", "skipped"):
            with self.assertRaises(PerformanceContractError):
                ci.resolve_build("NYGsatoshi/Coglatas", HEAD, self.api(job_result=status))

    def test_pr_wrong_sha_expired_and_wrong_workflow_artifacts_are_rejected(self):
        for changes in ({"event": "pull_request"}, {"head_sha": BASE}, {"head_branch": "feature"},
                        {"path": ".github/workflows/ci.yml"}, {"conclusion": "cancelled"}):
            with self.assertRaises(PerformanceContractError):
                ci.resolve_build("NYGsatoshi/Coglatas", HEAD, self.api(run_change=changes))
        for changes in ({"expired": True}, {"workflow_run": {"id": 17, "head_sha": BASE}},
                        {"workflow_run": {"id": 18, "head_sha": HEAD}}, {"name": "other"},
                        {"size_in_bytes": 0}, {"digest": None}, {"id": True}):
            with self.assertRaises((PerformanceContractError, TypeError)):
                ci.resolve_build("NYGsatoshi/Coglatas", HEAD, self.api(artifact_change=changes))

    def resolve_sequence(self, snapshots, jobs=None, artifacts=None, request_seconds=0):
        clock = [0.0]
        self.calls = []
        self.sleeps = []
        self.evidence = {}
        runs = iter(snapshots)
        latest = None
        def api(path):
            nonlocal latest
            self.calls.append(path)
            clock[0] += request_seconds
            if "/workflows/" in path:
                latest = next(runs, latest)
                if isinstance(latest, Exception):
                    raise latest
                return {"workflow_runs": latest}
            if "/jobs?" in path:
                return {"jobs": jobs(clock[0]) if callable(jobs) else jobs or []}
            return {"artifacts": artifacts if artifacts is not None else [self.artifact()]}
        def sleep(seconds):
            self.sleeps.append(seconds)
            clock[0] += seconds
        try:
            return ci.resolve_build("NYGsatoshi/Coglatas", HEAD, api, clock=lambda: clock[0],
                                    sleep=sleep, evidence=self.evidence)
        finally:
            self.elapsed = clock[0]

    def assembler(self, **changes):
        return {"name": "Main runtime artifact assembler", "status": "completed",
                "conclusion": "success"} | changes

    def test_waits_for_pinned_attempt_assembler_not_unrelated_main_consumers(self):
        old = self.build_run(id=16)
        active = self.build_run(status="in_progress", conclusion=None)
        result = self.resolve_sequence([[old, active]],
                    jobs=lambda seconds: [] if seconds < 2 else [self.assembler()])
        self.assertEqual("17", result)
        self.assertEqual([1, 1], self.sleeps)
        self.assertEqual(17, self.evidence["producerRunId"])
        self.assertEqual(1, self.evidence["producerRunAttempt"])
        self.assertEqual("resolved", self.evidence["decision"])
        self.assertTrue(all("status=completed" not in path for path in self.calls))
        self.assertTrue(all("/attempts/1/jobs?" in path for path in self.calls if "/jobs?" in path))

    def test_successful_assembler_survives_unrelated_completed_consumer_failure(self):
        self.assertEqual("17", ci.resolve_build("NYGsatoshi/Coglatas", HEAD,
                         self.api(run_change={"conclusion": "failure"})))

    def test_latest_failed_cancelled_or_incomplete_never_uses_older_success(self):
        old = self.build_run(id=16)
        for changes in ({"status": "completed", "conclusion": "cancelled"},
                        {"status": "completed", "conclusion": "failure"},
                        {"status": "in_progress", "conclusion": None}):
            with self.subTest(changes=changes), self.assertRaises(PerformanceContractError):
                self.resolve_sequence([[old, self.build_run(**changes)]])
            self.assertEqual("invalid", self.evidence["decision"])
            self.assertLessEqual(self.elapsed, 300)

    def test_replacement_rerun_disappearance_and_identity_drift_fail_closed(self):
        active = self.build_run(status="in_progress", conclusion=None)
        for next_runs in ([], [self.build_run(id=16)], [self.build_run(id=18)],
                          [dict(active, run_attempt=2)], [dict(active, head_sha=BASE)],
                          [dict(active, status="completed", conclusion="cancelled")]):
            with self.subTest(next_runs=next_runs), self.assertRaises(PerformanceContractError):
                self.resolve_sequence([[active], next_runs])
            self.assertEqual([1], self.sleeps)
            self.assertEqual("invalid", self.evidence["decision"])

    def test_final_identity_check_catches_cancellation_during_artifact_lookup(self):
        active = self.build_run(status="in_progress", conclusion=None)
        for final in ([], [self.build_run(id=18)], [dict(active, run_attempt=2)],
                      [dict(active, status="completed", conclusion="cancelled")]):
            with self.subTest(final=final), self.assertRaises(PerformanceContractError):
                self.resolve_sequence([[active], final], jobs=[self.assembler()])
            self.assertEqual("invalid", self.evidence["decision"])

    def test_assembler_failure_and_invalid_states_are_not_retried(self):
        active = self.build_run(status="in_progress", conclusion=None)
        for job in (self.assembler(conclusion="cancelled"), self.assembler(conclusion="failure"),
                    self.assembler(status="unknown", conclusion=None)):
            with self.subTest(job=job), self.assertRaises(PerformanceContractError):
                self.resolve_sequence([[active]], jobs=[job])
            self.assertEqual([], self.sleeps)

    def test_duplicate_missing_and_incomplete_artifacts_cannot_resolve(self):
        for artifacts in ([], [self.artifact(), self.artifact(id=28)],
                          [self.artifact(expired=True)], [self.artifact(digest="unknown")]):
            with self.subTest(artifacts=artifacts), self.assertRaises(PerformanceContractError):
                self.resolve_sequence([[self.build_run()]], jobs=[self.assembler()], artifacts=artifacts)
            self.assertEqual("invalid", self.evidence["decision"])

    def test_monotonic_budget_includes_requests_and_rejects_late_completion(self):
        active = self.build_run(status="in_progress", conclusion=None)
        with self.assertRaises(PerformanceContractError):
            self.resolve_sequence([[active]], jobs=[self.assembler()], request_seconds=100)
        self.assertEqual("invalid", self.evidence["decision"])
        self.assertEqual([], self.sleeps)
        self.assertEqual(300, self.elapsed)

    def test_api_error_and_partial_pages_fail_without_retry(self):
        with self.assertRaisesRegex(RuntimeError, "synthetic API failure"):
            self.resolve_sequence([RuntimeError("synthetic API failure")])
        self.assertEqual([], self.sleeps)
        for key, items in (("workflow_runs", [self.build_run()]),):
            api = Mock(return_value={key: items, "total_count": 101})
            with self.assertRaises(PerformanceContractError):
                ci.resolve_build("NYGsatoshi/Coglatas", HEAD, api)
        self.assertEqual(1, api.call_count)

    def test_invalid_run_identity_never_resolves(self):
        for change in ({"id": True}, {"id": "17"}, {"id": -1},
                       {"run_attempt": None}, {"run_attempt": True},
                       {"status": "unknown"}, {"status": "in_progress", "conclusion": "success"}):
            with self.subTest(change=change), self.assertRaises(PerformanceContractError):
                self.resolve_sequence([[self.build_run(**change)]], jobs=[self.assembler()])


class PerformanceDbComparisonIdentityTests(unittest.TestCase):
    def profile(self, name, contract):
        data = {"schemaVersion": 1, "fixtureVersion": DB_FIXTURE_VERSION, "fixtureHash": fixture_hash(name, fixture_version=DB_FIXTURE_VERSION),
                "profile": name, "headSha": HEAD, "collectionComplete": True, "warmupSamplesExcluded": True,
                "scenarios": [], "plans": []}
        for scenario in contract["scenarios"]:
            sizes = [5, 10] if scenario["paged"] else [0]
            samples = []
            for size in sizes:
                for page in ([1, 2] if scenario["paged"] else [1]):
                    for iteration in range(1, 6):
                        evidence = capture(rows=size + 1)
                        for command in evidence["commands"]:
                            command["rootTable"] = scenario["rootTable"]
                        evidence["slowestCommands"] = evidence["commands"][:5]
                        samples.append({"pageSize": size, "page": page, "iteration": iteration, "capture": evidence})
            data["scenarios"].append({"id": scenario["id"], "cardinality": 60 if name == "small" else 260, "failures": [], "samples": samples})
        if name == "medium":
            data["plans"] = [{"id": "task.id-index-lookup", "tableRows": 3000, "requiredKeyLookupPresent": True}]
        return data

    def test_same_wrong_sha_pair_cannot_satisfy_target(self):
        contract = load_json(ROOT / "performance/db-scenarios.json")
        small, medium = (self.profile(name, contract) for name in ("small", "medium"))
        self.assertEqual("pass", comparison.evaluate(small, medium, contract, HEAD)["decision"])
        with self.assertRaises(PerformanceContractError):
            comparison.evaluate(small, medium, contract, BASE)

    def test_missing_invalid_or_unstable_duration_cannot_turn_main_green(self):
        for results in ([], [{"decision": "invalid"}], [{"decision": "unstable"}], [{"decision": "insufficient-data"}], [{"decision": "regression"}]):
            self.assertNotEqual("pass", comparison.duration_decision(results))
        self.assertEqual("pass", comparison.duration_decision([{"decision": "pass"}]))

    def test_duration_inventory_rejects_missing_duplicate_and_wrong_page_streams(self):
        contract = load_json(ROOT / "performance/db-scenarios.json")
        measurements = [{"scenario": s["id"], "pageSize": 5 if s["paged"] else 0} for s in contract["scenarios"]]
        comparison.validate_duration_inventory({"measurements": measurements}, contract)
        for invalid in (measurements[:-1], measurements + [measurements[0]], measurements[:-1] + [measurements[-1] | {"pageSize": 10}]):
            with self.assertRaises(PerformanceContractError):
                comparison.validate_duration_inventory({"measurements": invalid}, contract)

    def test_index_diagnostics_never_export_names_conditions_or_unrelated_indexes(self):
        plan = {"Plan": {"Node Type": "Nested Loop", "Plans": [
            {"Node Type": "Index Scan", "Relation Name": "task_items", "Index Name": "PK_task_items", "Index Cond": "protected-id"},
            {"Node Type": "Bitmap Heap Scan", "Relation Name": "task_items", "Plans": [
                {"Node Type": "Bitmap Index Scan", "Index Name": "AK_task_items_Id_ProjectId", "Index Cond": "protected-id"}]},
            {"Node Type": "Index Only Scan", "Relation Name": "task_items", "Index Name": "protected-name"},
            {"Node Type": "Index Scan", "Relation Name": "unrelated", "Index Name": "protected-name"}]}}
        self.assertEqual(["other", "primary-key", "task-project-key"], index_kinds(plan, "task_items"))
        self.assertNotIn("protected", json.dumps(index_kinds(plan, "task_items")))
        self.assertEqual([], index_kinds({"Node Type": "Seq Scan", "Relation Name": "task_items"}, "task_items"))


if __name__ == "__main__":
    unittest.main()
