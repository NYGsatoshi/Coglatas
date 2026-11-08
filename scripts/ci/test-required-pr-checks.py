#!/usr/bin/env python3
from __future__ import annotations

import copy
import datetime as dt
import io
import importlib.util
import json
import tempfile
import unittest
import urllib.request
from unittest import mock
from pathlib import Path
from typing import Any

import required_check_contract as required

MODULE_PATH = Path(__file__).with_name("check-required-pr-checks.py")
SPEC = importlib.util.spec_from_file_location("required_check_guard", MODULE_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError("Unable to load required PR check guard")
guard = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(guard)

REGISTRY = guard.load_required_check_registry()
HEAD = "a" * 40
OLD_HEAD = "b" * 40
NOW = dt.datetime(2026, 9, 4, 5, 30, tzinfo=dt.timezone.utc)


def registry_for_contexts(*contexts: str) -> dict[str, Any]:
    result = copy.deepcopy(REGISTRY)
    wanted = set(contexts)
    result["checks"] = [item for item in result["checks"] if item["context"] in wanted]
    return result


BUILD_REGISTRY = registry_for_contexts("build-test")


def dual_registry(context: str = "build-test-v2") -> dict[str, Any]:
    result = copy.deepcopy(REGISTRY)
    build = next(item for item in result["checks"] if item["gate_id"] == "GOV-GATE-BUILD-001")
    build["rename"] = {
        "state": "dual-publish",
        "previous_context": "build-test",
        "previous_workflow": ".github/workflows/ci.yml",
        "previous_job": "build-test-old",
        "migration_issue": 900,
    }
    build["context"] = context
    build["job"] = "build-test-v2"
    return result


class RegistryTests(unittest.TestCase):
    def test_registry_matches_governance_policy(self) -> None:
        self.assertEqual(6, len(REGISTRY["checks"]))
        self.assertEqual(
            ["build-test", "frontend-test", "security-scan", "publication-readiness", "functional-fast", "performance-fast"],
            [item["context"] for item in REGISTRY["checks"]],
        )

    def test_dual_publish_expands_previous_identity(self) -> None:
        registry = dual_registry()
        expanded = [item for item in guard.expanded_checks(registry) if item["source_gate_id"] == "GOV-GATE-BUILD-001"]
        self.assertEqual(2, len(expanded))
        self.assertEqual({"build-test", "build-test-v2"}, {item["context"] for item in expanded})

    def test_stable_rename_cannot_keep_previous_metadata(self) -> None:
        bad = copy.deepcopy(REGISTRY)
        bad["checks"][0]["rename"]["previous_context"] = "old"
        with tempfile.TemporaryDirectory() as td:
            p = Path(td)
            registry_path = p / "registry.json"
            policy_path = p / "policy.json"
            registry_path.write_text(json.dumps(bad), encoding="utf-8")
            policy_path.write_text(Path(guard.POLICY_PATH).read_text(encoding="utf-8"), encoding="utf-8")
            with self.assertRaisesRegex(RuntimeError, "stable state"):
                guard.load_required_check_registry(registry_path, policy_path)


class StaticTopologyTests(unittest.TestCase):
    def routed_workflow(self, condition: str = "always()", route: str = "needs.route.outputs.required == 'true'") -> str:
        return f"""
on:
  pull_request:
jobs:
  route:
    runs-on: ubuntu-latest
  benchmark:
    if: {route}
    needs: route
    runs-on: ubuntu-latest
  gate:
    name: performance-fast
    if: {condition}
    needs: [route, benchmark]
    runs-on: ubuntu-latest
    timeout-minutes: 5
"""

    def test_routed_prerequisite_requires_always_aggregate(self) -> None:
        registry = registry_for_contexts("performance-fast")
        errors = guard.required_check_errors(
            ".github/workflows/performance-api.yml", self.routed_workflow(), registry
        )
        self.assertEqual([], errors)
        errors = guard.required_check_errors(
            ".github/workflows/performance-api.yml", self.routed_workflow("success()"), registry
        )
        self.assertTrue(any("routed aggregate must use always()" in error for error in errors))

    def test_routed_prerequisite_rejects_changed_predicate(self) -> None:
        registry = registry_for_contexts("performance-fast")
        errors = guard.required_check_errors(
            ".github/workflows/performance-api.yml",
            self.routed_workflow(route="needs.route.outputs.required != 'false'"), registry,
        )
        self.assertTrue(any("prerequisite job 'benchmark'" in error for error in errors))

    def test_routed_prerequisite_does_not_allow_filtered_router(self) -> None:
        registry = registry_for_contexts("performance-fast")
        text = self.routed_workflow().replace(
            "  route:\n    runs-on:", "  route:\n    if: github.actor != 'x'\n    runs-on:"
        )
        errors = guard.required_check_errors(".github/workflows/performance-api.yml", text, registry)
        self.assertTrue(any("prerequisite job 'route'" in error for error in errors))

    def test_routed_prerequisite_rejects_failure_masking(self) -> None:
        registry = registry_for_contexts("performance-fast")
        text = self.routed_workflow().replace("  benchmark:\n", "  benchmark:\n    continue-on-error: true\n")
        errors = guard.required_check_errors(".github/workflows/performance-api.yml", text, registry)
        self.assertTrue(any("must not use continue-on-error" in error for error in errors))

    def test_unfiltered_required_job_passes(self) -> None:
        text = """
name: Publication Readiness
on:
  pull_request:
jobs:
  publication-readiness:
    name: publication-readiness
    runs-on: ubuntu-latest
    timeout-minutes: 20
"""
        self.assertEqual([], guard.required_check_errors(".github/workflows/publication-readiness.yml", text, REGISTRY))

    def test_paths_filter_is_rejected(self) -> None:
        text = """
on:
  pull_request:
    paths: ["src/**"]
jobs:
  publication-readiness:
    name: publication-readiness
    runs-on: ubuntu-latest
    timeout-minutes: 20
"""
        errors = guard.required_check_errors(".github/workflows/publication-readiness.yml", text, REGISTRY)
        self.assertTrue(any("unfiltered pull_request" in error for error in errors))

    def test_job_level_if_is_rejected(self) -> None:
        text = """
on:
  pull_request:
jobs:
  publication-readiness:
    name: publication-readiness
    if: github.actor != 'x'
    runs-on: ubuntu-latest
    timeout-minutes: 20
"""
        errors = guard.required_check_errors(".github/workflows/publication-readiness.yml", text, REGISTRY)
        self.assertTrue(any("job-level if" in error for error in errors))

    def test_needs_is_rejected(self) -> None:
        text = """
on:
  pull_request:
jobs:
  publication-readiness:
    name: publication-readiness
    needs: prepare
    runs-on: ubuntu-latest
    timeout-minutes: 20
"""
        errors = guard.required_check_errors(".github/workflows/publication-readiness.yml", text, REGISTRY)
        self.assertTrue(any("must not depend" in error for error in errors))

    def test_declared_build_prerequisite_passes(self) -> None:
        text = """
on:
  pull_request:
jobs:
  dotnet-build:
    name: .NET build producer
    runs-on: ubuntu-latest
  build-test:
    name: build-test
    needs: dotnet-build
    runs-on: ubuntu-latest
    timeout-minutes: 120
"""
        errors = guard.required_check_errors(".github/workflows/ci.yml", text, BUILD_REGISTRY)
        self.assertEqual([], errors)

    def test_declared_prerequisite_may_depend_on_unconditional_preflight(self) -> None:
        text = """
on:
  pull_request:
jobs:
  changes:
    name: CI preflight + route
    runs-on: ubuntu-latest
  dotnet-build:
    name: .NET build producer
    needs: changes
    runs-on: ubuntu-latest
  build-test:
    name: build-test
    needs: dotnet-build
    runs-on: ubuntu-latest
    timeout-minutes: 120
"""
        errors = guard.required_check_errors(".github/workflows/ci.yml", text, BUILD_REGISTRY)
        self.assertEqual([], errors)

    def test_transitive_preflight_with_job_if_is_rejected(self) -> None:
        text = """
on:
  pull_request:
jobs:
  changes:
    name: CI preflight + route
    if: github.actor != 'x'
    runs-on: ubuntu-latest
  dotnet-build:
    name: .NET build producer
    needs: changes
    runs-on: ubuntu-latest
  build-test:
    name: build-test
    needs: dotnet-build
    runs-on: ubuntu-latest
    timeout-minutes: 120
"""
        errors = guard.required_check_errors(".github/workflows/ci.yml", text, BUILD_REGISTRY)
        self.assertTrue(any("prerequisite job 'changes' must not use job-level if" in error for error in errors))

    def test_block_sequence_prerequisite_is_traversed(self) -> None:
        text = """
on:
  pull_request:
jobs:
  changes:
    name: CI preflight + route
    if: github.actor != 'x'
    runs-on: ubuntu-latest
  dotnet-build:
    name: .NET build producer
    needs:
      - changes
    runs-on: ubuntu-latest
  build-test:
    name: build-test
    needs: dotnet-build
    runs-on: ubuntu-latest
    timeout-minutes: 120
"""
        errors = guard.required_check_errors(".github/workflows/ci.yml", text, BUILD_REGISTRY)
        self.assertTrue(any("prerequisite job 'changes' must not use job-level if" in error for error in errors))

    def test_duplicate_prerequisite_needs_key_is_rejected(self) -> None:
        text = """
on:
  pull_request:
jobs:
  changes:
    name: CI preflight + route
    runs-on: ubuntu-latest
  dotnet-build:
    name: .NET build producer
    needs: changes
    needs:
      - changes
    runs-on: ubuntu-latest
  build-test:
    name: build-test
    needs: dotnet-build
    runs-on: ubuntu-latest
    timeout-minutes: 120
"""
        errors = guard.required_check_errors(".github/workflows/ci.yml", text, BUILD_REGISTRY)
        self.assertTrue(any("duplicate job keys" in error or "invalid needs" in error for error in errors))

    def test_declared_prerequisite_with_job_if_is_rejected(self) -> None:
        text = """
on:
  pull_request:
jobs:
  dotnet-build:
    name: .NET build producer
    if: github.actor != 'x'
    runs-on: ubuntu-latest
  build-test:
    name: build-test
    needs: dotnet-build
    runs-on: ubuntu-latest
    timeout-minutes: 120
"""
        errors = guard.required_check_errors(".github/workflows/ci.yml", text, BUILD_REGISTRY)
        self.assertTrue(any("prerequisite job 'dotnet-build' must not use job-level if" in error for error in errors))

    def test_continue_on_error_is_rejected(self) -> None:
        text = """
on:
  pull_request:
jobs:
  publication-readiness:
    name: publication-readiness
    continue-on-error: true
    runs-on: ubuntu-latest
    timeout-minutes: 20
"""
        errors = guard.required_check_errors(".github/workflows/publication-readiness.yml", text, REGISTRY)
        self.assertTrue(any("continue-on-error" in error for error in errors))

    def test_dual_publish_requires_both_jobs_statically(self) -> None:
        registry = dual_registry()
        text = """
on:
  pull_request:
jobs:
  build-test-v2:
    name: build-test-v2
    runs-on: ubuntu-latest
    timeout-minutes: 120
"""
        errors = guard.required_check_errors(".github/workflows/ci.yml", text, registry)
        self.assertTrue(any("build-test-old" in error for error in errors))


class LiveRulesetTests(unittest.TestCase):
    def live_ruleset(self, registry: dict[str, Any] = REGISTRY) -> dict[str, Any]:
        checks = []
        for context, integration in guard.live_expected(registry):
            value: dict[str, Any] = {"context": context}
            if integration is not None:
                value["integration_id"] = integration
            checks.append(value)
        return {
            "name": registry["ruleset"]["name"],
            "target": "branch",
            "enforcement": "active",
            "conditions": {"ref_name": {"exclude": [], "include": ["~DEFAULT_BRANCH"]}},
            "rules": [{"type": "required_status_checks", "parameters": {"strict_required_status_checks_policy": True, "required_status_checks": checks}}],
        }

    def test_exact_live_ruleset_passes(self) -> None:
        self.assertEqual([], guard.live_ruleset_errors(REGISTRY, self.live_ruleset()))

    def test_missing_required_context_fails(self) -> None:
        live = self.live_ruleset()
        live["rules"][0]["parameters"]["required_status_checks"].pop()
        self.assertTrue(any("missing required context" in e for e in guard.live_ruleset_errors(REGISTRY, live)))

    def test_wrong_integration_fails(self) -> None:
        live = self.live_ruleset()
        build = next(item for item in live["rules"][0]["parameters"]["required_status_checks"] if item["context"] == "build-test")
        build["integration_id"] = 999
        self.assertTrue(any("producer integration drift" in e for e in guard.live_ruleset_errors(REGISTRY, live)))

    def test_live_ruleset_cannot_relax_its_branch_or_enforcement_contract(self) -> None:
        changes = (
            ({"name": "unregistered"}, "ruleset name drift"),
            ({"target": "tag"}, "target must be branch"),
            ({"enforcement": "evaluate"}, "must be active"),
            ({"conditions": {"ref_name": {"include": ["refs/heads/feature"]}}}, "include ~DEFAULT_BRANCH"),
        )
        for fields, expected in changes:
            with self.subTest(fields=fields):
                live = self.live_ruleset()
                live.update(fields)
                self.assertTrue(any(expected in e for e in guard.live_ruleset_errors(REGISTRY, live)))

    def test_live_ruleset_rejects_duplicate_unknown_and_malformed_contexts(self) -> None:
        for kind in ("duplicate", "unknown", "malformed"):
            with self.subTest(kind=kind):
                live = self.live_ruleset()
                contexts = live["rules"][0]["parameters"]["required_status_checks"]
                extra = copy.deepcopy(contexts[0])
                if kind == "unknown":
                    extra["context"] = "unregistered-check"
                elif kind == "malformed":
                    extra["context"] = ""
                contexts.append(extra)
                expected = {"duplicate": "duplicate required context", "unknown": "unknown required context", "malformed": "is malformed"}[kind]
                self.assertTrue(any(expected in e for e in guard.live_ruleset_errors(REGISTRY, live)))

    def test_missing_or_non_strict_live_status_rules_fail_closed(self) -> None:
        for kind in ("missing", "duplicate", "parameters", "strict", "contexts"):
            with self.subTest(kind=kind):
                live = self.live_ruleset()
                if kind == "missing":
                    live["rules"] = []
                elif kind == "duplicate":
                    live["rules"].append(copy.deepcopy(live["rules"][0]))
                elif kind == "parameters":
                    live["rules"][0]["parameters"] = None
                elif kind == "strict":
                    live["rules"][0]["parameters"]["strict_required_status_checks_policy"] = False
                else:
                    live["rules"][0]["parameters"]["required_status_checks"] = None
                self.assertNotEqual([], guard.live_ruleset_errors(REGISTRY, live))

    def test_dual_publish_old_and_new_are_both_expected(self) -> None:
        registry = dual_registry()
        live = self.live_ruleset(registry)
        contexts = {item["context"] for item in live["rules"][0]["parameters"]["required_status_checks"]}
        self.assertIn("build-test", contexts)
        self.assertIn("build-test-v2", contexts)
        self.assertEqual([], guard.live_ruleset_errors(registry, live))

    def test_dual_publish_missing_old_context_fails(self) -> None:
        registry = dual_registry()
        live = self.live_ruleset(registry)
        values = live["rules"][0]["parameters"]["required_status_checks"]
        values[:] = [item for item in values if item["context"] != "build-test"]
        errors = guard.live_ruleset_errors(registry, live)
        self.assertTrue(any("missing required context 'build-test'" in e for e in errors))


class ExactHeadTests(unittest.TestCase):
    def success_evidence(self, registry: dict[str, Any] = REGISTRY, head: str = HEAD) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
        checks: list[dict[str, Any]] = []
        statuses: list[dict[str, Any]] = []
        for offset, item in enumerate(guard.exact_entries(registry)):
            timestamp = f"2026-09-04T05:{10 + offset:02d}:00Z"
            if item["kind"] == "workflow-job":
                checks.append({
                    "id": offset + 1,
                    "name": item["context"],
                    "head_sha": head,
                    "status": "completed",
                    "conclusion": "success",
                    "completed_at": timestamp,
                    "app": {"id": item["producer"]["integration_id"], "slug": item["producer"]["app_slug"]},
                    "workflow": item["workflow"],
                    "workflow_event": "pull_request",
                    "workflow_head_sha": head,
                })
            else:
                statuses.append({
                    "id": offset + 1,
                    "context": item["context"],
                    "sha": head,
                    "state": "success",
                    "updated_at": timestamp,
                    "creator": {"login": item["producer"]["creator_login"]},
                    "workflow": item["workflow"],
                    "workflow_event": "workflow_run",
                    "workflow_head_branch": "main",
                })
        return checks, statuses

    def test_current_head_success_passes(self) -> None:
        checks, statuses = self.success_evidence()
        report = guard.exact_head_report(REGISTRY, HEAD, checks, statuses, now=NOW, trusted_base_ref="main")
        self.assertEqual("pass", report["decision"])

    def test_previous_head_only_fails(self) -> None:
        checks, statuses = self.success_evidence()
        build = next(item for item in checks if item["name"] == "build-test")
        build["head_sha"] = OLD_HEAD
        build["workflow_head_sha"] = OLD_HEAD
        report = guard.exact_head_report(REGISTRY, HEAD, checks, statuses, now=NOW, trusted_base_ref="main")
        gate = next(item for item in report["gates"] if item["context"] == "build-test")
        self.assertEqual("previous-head-only", gate["reason"])
        self.assertEqual("fail", report["decision"])

    def test_skipped_cancelled_neutral_fail(self) -> None:
        for conclusion in ("skipped", "cancelled", "neutral"):
            with self.subTest(conclusion=conclusion):
                checks, statuses = self.success_evidence()
                next(item for item in checks if item["name"] == "build-test")["conclusion"] = conclusion
                report = guard.exact_head_report(REGISTRY, HEAD, checks, statuses, now=NOW, trusted_base_ref="main")
                self.assertEqual("fail", report["decision"])

    def test_pending_before_timeout_is_pending(self) -> None:
        checks, statuses = self.success_evidence()
        build = next(item for item in checks if item["name"] == "build-test")
        build.update(status="in_progress", conclusion=None, started_at="2026-09-04T05:00:00Z")
        report = guard.exact_head_report(REGISTRY, HEAD, checks, statuses, now=NOW, trusted_base_ref="main")
        self.assertEqual("pending", report["decision"])

    def test_pending_after_timeout_fails(self) -> None:
        checks, statuses = self.success_evidence()
        publication = next(item for item in checks if item["name"] == "publication-readiness")
        publication.update(status="in_progress", conclusion=None, started_at="2026-09-04T04:00:00Z")
        report = guard.exact_head_report(REGISTRY, HEAD, checks, statuses, now=NOW, trusted_base_ref="main")
        self.assertEqual("fail", report["decision"])
        gate = next(item for item in report["gates"] if item["context"] == "publication-readiness")
        self.assertEqual("pending-timeout", gate["reason"])

    def test_wrong_producer_fails(self) -> None:
        checks, statuses = self.success_evidence()
        next(item for item in checks if item["name"] == "build-test")["app"] = {"id": 1, "slug": "other"}
        report = guard.exact_head_report(REGISTRY, HEAD, checks, statuses, now=NOW, trusted_base_ref="main")
        self.assertEqual("fail", report["decision"])

    def test_workflow_metadata_drift_cannot_borrow_a_successful_check(self) -> None:
        changes = (
            ({"workflow": ".github/workflows/unregistered.yml"}, "workflow-producer-drift"),
            ({"workflow_head_sha": OLD_HEAD}, "workflow-run-head-drift"),
            ({"workflow_event": "push"}, "workflow-event-drift"),
            ({"app": None}, "check-app-drift"),
            ({"app": {"id": 15368, "slug": "unregistered"}}, "check-app-drift"),
        )
        for fields, error in changes:
            with self.subTest(fields=fields):
                checks, statuses = self.success_evidence()
                next(item for item in checks if item["name"] == "build-test").update(fields)
                report = guard.exact_head_report(REGISTRY, HEAD, checks, statuses, now=NOW)
                gate = next(item for item in report["gates"] if item["context"] == "build-test")
                self.assertEqual("fail", report["decision"])
                self.assertEqual("producer-drift", gate["reason"])
                self.assertEqual([error], gate["producer_errors"])

    def test_latest_authoritative_rejection_wins_over_an_older_success(self) -> None:
        checks, statuses = self.success_evidence()
        build = next(item for item in checks if item["name"] == "build-test")
        for conclusion in ("failure", "cancelled"):
            for ordering in (False, True):
                with self.subTest(conclusion=conclusion, reverse_input=ordering):
                    rejection = dict(build, id=999, conclusion=conclusion, completed_at="2026-09-04T05:29:00Z")
                    candidates = checks + [rejection]
                    if ordering:
                        candidates.reverse()
                    report = guard.exact_head_report(REGISTRY, HEAD, candidates, statuses, now=NOW)
                    gate = next(item for item in report["gates"] if item["context"] == "build-test")
                    self.assertEqual("fail", report["decision"])
                    self.assertEqual(conclusion, gate["observed_state"])
                    self.assertEqual("rejected-conclusion", gate["reason"])

    def test_same_timestamp_uses_the_newer_check_identity(self) -> None:
        checks, statuses = self.success_evidence()
        build = next(item for item in checks if item["name"] == "build-test")
        for ordering in (False, True):
            with self.subTest(reverse_input=ordering):
                candidates = checks + [dict(build, id=999, conclusion="cancelled")]
                if ordering:
                    candidates.reverse()
                report = guard.exact_head_report(REGISTRY, HEAD, candidates, statuses, now=NOW)
                gate = next(item for item in report["gates"] if item["context"] == "build-test")
                self.assertEqual("fail", report["decision"])
                self.assertEqual("cancelled", gate["observed_state"])

    def test_unknown_completed_state_and_missing_pending_clock_fail_closed(self) -> None:
        changes = (
            {"status": "completed", "conclusion": None},
            {"status": "waiting", "conclusion": "success"},
            {"status": "in_progress", "conclusion": None},
            {"status": "queued", "conclusion": None, "started_at": "invalid", "created_at": "invalid"},
        )
        for fields in changes:
            with self.subTest(fields=fields):
                checks, statuses = self.success_evidence()
                next(item for item in checks if item["name"] == "build-test").update(fields)
                report = guard.exact_head_report(REGISTRY, HEAD, checks, statuses, now=NOW)
                gate = next(item for item in report["gates"] if item["context"] == "build-test")
                self.assertEqual("fail", report["decision"])
                expected = "pending-timeout" if fields["status"] in {"queued", "in_progress"} else "unknown-state"
                self.assertEqual(expected, gate["reason"])

    def test_unregistered_success_and_non_object_payloads_do_not_supply_missing_gate(self) -> None:
        checks, statuses = self.success_evidence()
        build = next(item for item in checks if item["name"] == "build-test")
        build["name"] = "build-test-unregistered"
        report = guard.exact_head_report(REGISTRY, HEAD, [None, "success", *checks], [False, *statuses], now=NOW)
        gate = next(item for item in report["gates"] if item["context"] == "build-test")
        self.assertEqual("fail", report["decision"])
        self.assertEqual("missing-current-head", gate["reason"])

    def test_dual_publish_requires_old_and_new_exact_head(self) -> None:
        registry = dual_registry()
        checks, statuses = self.success_evidence(registry)
        report = guard.exact_head_report(registry, HEAD, checks, statuses, now=NOW, trusted_base_ref="main")
        build_gates = [g for g in report["gates"] if g["source_gate_id"] == "GOV-GATE-BUILD-001"]
        self.assertEqual(2, len(build_gates))
        self.assertEqual("pass", report["decision"])
        checks[:] = [item for item in checks if item["name"] != "build-test"]
        report = guard.exact_head_report(registry, HEAD, checks, statuses, now=NOW, trusted_base_ref="main")
        self.assertEqual("fail", report["decision"])
        old = next(g for g in report["gates"] if g["context"] == "build-test")
        self.assertEqual("missing-current-head", old["reason"])


class FakeApi:
    def __init__(self, responses: dict[str, Any]) -> None:
        self.responses = responses
        self.calls: list[str] = []

    def get(self, path: str) -> Any:
        self.calls.append(path)
        if path not in self.responses:
            raise RuntimeError(f"unexpected API call: {path}")
        return copy.deepcopy(self.responses[path])


class LiveEvaluatorTests(ExactHeadTests, LiveRulesetTests):
    def test_authoritative_head_and_base_are_refetched(self) -> None:
        checks, statuses = self.success_evidence(head=HEAD)
        live = self.live_ruleset()
        responses: dict[str, Any] = {
            "repos/Coglatas/Coglatas/pulls/42": {"state": "open", "head": {"sha": HEAD}, "base": {"ref": "main"}},
            "repos/Coglatas/Coglatas/rulesets": [{"id": 123, "name": REGISTRY["ruleset"]["name"]}],
            "repos/Coglatas/Coglatas/rulesets/123": live,
            f"repos/Coglatas/Coglatas/commits/{HEAD}/check-runs?filter=latest&per_page=100": {"total_count": len(checks), "check_runs": checks},
            f"repos/Coglatas/Coglatas/commits/{HEAD}/statuses?per_page=100": statuses,
        }
        for candidate in checks:
            run_id = 1000 + candidate["id"]
            candidate["details_url"] = f"https://github.com/Coglatas/Coglatas/actions/runs/{run_id}/job/{candidate['id']}"
            responses[f"repos/Coglatas/Coglatas/actions/runs/{run_id}"] = {
                "path": candidate["workflow"], "event": "pull_request", "head_sha": HEAD, "head_branch": "feature"
            }
        for candidate in statuses:
            run_id = 2000 + candidate["id"]
            candidate["target_url"] = f"https://github.com/Coglatas/Coglatas/actions/runs/{run_id}"
            responses[f"repos/Coglatas/Coglatas/actions/runs/{run_id}"] = {
                "path": candidate["workflow"], "event": candidate["workflow_event"], "head_sha": "c" * 40, "head_branch": "main"
            }
        responses[f"repos/Coglatas/Coglatas/commits/{HEAD}/check-runs?filter=latest&per_page=100"] = {"total_count": len(checks), "check_runs": checks}
        responses[f"repos/Coglatas/Coglatas/commits/{HEAD}/statuses?per_page=100"] = statuses
        report = guard.evaluate_live_pr(FakeApi(responses), "Coglatas/Coglatas", 42, REGISTRY, now=NOW)
        self.assertEqual(HEAD, report["authoritative_head_sha"])
        self.assertEqual("main", report["authoritative_base_ref"])
        self.assertEqual("pass", report["decision"])

    def test_api_failure_is_fail_closed_by_caller(self) -> None:
        repository = "Coglatas/Coglatas"
        api = FakeApi({})
        with self.assertRaises(RuntimeError):
            guard.evaluate_live_pr(api, repository, 42, REGISTRY, now=NOW)

    def complete_live_api(self) -> FakeApi:
        checks, statuses = self.success_evidence()
        self.assertEqual([], statuses)
        prefix = "repos/Coglatas/Coglatas"
        responses = {
            f"{prefix}/pulls/42": {"state": "open", "head": {"sha": HEAD}, "base": {"ref": "main"}},
            f"{prefix}/rulesets": [{"id": 123, "name": REGISTRY["ruleset"]["name"]}],
            f"{prefix}/rulesets/123": self.live_ruleset(),
            f"{prefix}/commits/{HEAD}/check-runs?filter=latest&per_page=100": {"total_count": len(checks), "check_runs": checks},
            f"{prefix}/commits/{HEAD}/statuses?per_page=100": [],
        }
        for check in checks:
            run_id = 1000 + check["id"]
            check["details_url"] = f"https://github.com/Coglatas/Coglatas/actions/runs/{run_id}"
            responses[f"{prefix}/actions/runs/{run_id}"] = {
                "path": check["workflow"], "event": "pull_request", "head_sha": HEAD, "head_branch": "feature"
            }
        return FakeApi(responses)

    def test_actual_run_metadata_overrides_successful_check_claims(self) -> None:
        for field, value, error in (
            ("path", ".github/workflows/unregistered.yml", "workflow-producer-drift"),
            ("event", "push", "workflow-event-drift"),
            ("head_sha", OLD_HEAD, "workflow-run-head-drift"),
        ):
            with self.subTest(field=field):
                api = self.complete_live_api()
                api.responses["repos/Coglatas/Coglatas/actions/runs/1001"][field] = value
                report = guard.evaluate_live_pr(api, "Coglatas/Coglatas", 42, REGISTRY, now=NOW)
                gate = next(row for row in report["exact_head"]["gates"] if row["context"] == "build-test")
                self.assertEqual("fail", report["decision"])
                self.assertEqual("producer-drift", gate["reason"])
                self.assertEqual([error], gate["producer_errors"])

    def test_shared_ci_run_is_resolved_once_for_all_of_its_required_checks(self) -> None:
        api = self.complete_live_api()
        checks = api.responses[f"repos/Coglatas/Coglatas/commits/{HEAD}/check-runs?filter=latest&per_page=100"]["check_runs"]
        shared = [row for row in checks if row["workflow"] == ".github/workflows/ci.yml"]
        self.assertGreaterEqual(len(shared), 2)
        for check in shared:
            check["details_url"] = "https://github.com/Coglatas/Coglatas/actions/runs/7777"
        path = "repos/Coglatas/Coglatas/actions/runs/7777"
        api.responses[path] = {"path": ".github/workflows/ci.yml", "event": "pull_request", "head_sha": HEAD}
        report = guard.evaluate_live_pr(api, "Coglatas/Coglatas", 42, REGISTRY, now=NOW)
        self.assertEqual("pass", report["decision"])
        self.assertEqual(1, api.calls.count(path))

    def test_required_checks_on_a_later_api_page_are_collected(self) -> None:
        api = self.complete_live_api()
        path = f"repos/Coglatas/Coglatas/commits/{HEAD}/check-runs?filter=latest&per_page=100"
        required_checks = api.responses[path]["check_runs"]
        unrelated = [{"id": 5000 + i, "name": "unregistered", "head_sha": HEAD} for i in range(100)]
        api.responses[path] = {"total_count": 100 + len(required_checks), "check_runs": unrelated}
        api.responses[path + "&page=2"] = {"total_count": 100 + len(required_checks), "check_runs": required_checks}
        report = guard.evaluate_live_pr(api, "Coglatas/Coglatas", 42, REGISTRY, now=NOW)
        self.assertEqual("pass", report["decision"])
        self.assertIn(path + "&page=2", api.calls)
        self.assertEqual(6, len(report["exact_head"]["gates"]))

    def test_invalid_authoritative_pr_stops_before_downstream_api_reads(self) -> None:
        for payload in (
            None,
            {"state": "closed"},
            {"state": "open", "head": {"sha": "invalid"}, "base": {"ref": "main"}},
            {"state": "open", "head": {"sha": HEAD}, "base": {"ref": ""}},
        ):
            with self.subTest(payload=payload):
                path = "repos/Coglatas/Coglatas/pulls/42"
                api = FakeApi({path: payload})
                with self.assertRaisesRegex(RuntimeError, "missing or not open|authoritative head/base"):
                    guard.evaluate_live_pr(api, "Coglatas/Coglatas", 42, REGISTRY, now=NOW)
                self.assertEqual([path], api.calls)

    def test_malformed_authoritative_api_payloads_cannot_supply_success(self) -> None:
        prefix = "repos/Coglatas/Coglatas"
        for path, payload, error in (
            (f"{prefix}/rulesets", {}, "ruleset list response"),
            (f"{prefix}/rulesets/123", [], "ruleset detail response"),
            (f"{prefix}/commits/{HEAD}/check-runs?filter=latest&per_page=100", {"check_runs": None}, "paginated response"),
            (f"{prefix}/commits/{HEAD}/statuses?per_page=100", None, "paginated response"),
            (f"{prefix}/actions/runs/1001", [], "run 1001 response is malformed"),
        ):
            with self.subTest(path=path):
                api = self.complete_live_api()
                api.responses[path] = payload
                with self.assertRaisesRegex(RuntimeError, error):
                    guard.evaluate_live_pr(api, "Coglatas/Coglatas", 42, REGISTRY, now=NOW)

    def test_cli_preserves_live_decisions_in_exit_codes_and_json(self) -> None:
        for decision, exit_code in (("pass", 0), ("pending", 2), ("fail", 1)):
            with self.subTest(decision=decision):
                output = io.StringIO()
                report = {"decision": decision, "authoritative_head_sha": HEAD}
                with mock.patch.dict(required.os.environ, {"GITHUB_TOKEN": "", "GH_TOKEN": ""}), mock.patch.object(required, "GitHubApi"), mock.patch.object(required, "evaluate_live_pr", return_value=report) as evaluate, mock.patch.object(required.os.sys, "stdout", output):
                    result = required.run_cli(["--live-pr", "42", "--repository", "Coglatas/Coglatas", "--json"])
                self.assertEqual(exit_code, result)
                self.assertEqual(report, json.loads(output.getvalue()))
                self.assertEqual(("Coglatas/Coglatas", 42), evaluate.call_args.args[1:3])

    def test_cli_serializes_unavailable_evidence_as_failure_without_network(self) -> None:
        output = io.StringIO()
        with mock.patch.dict(required.os.environ, {"GITHUB_TOKEN": "", "GH_TOKEN": ""}), mock.patch.object(required, "GitHubApi"), mock.patch.object(required, "evaluate_live_pr", side_effect=RuntimeError("controlled unavailable evidence")), mock.patch.object(required.os.sys, "stdout", output):
            exit_code = required.run_cli(["--live-pr", "42", "--repository", "Coglatas/Coglatas", "--json"])
        report = json.loads(output.getvalue())
        self.assertEqual(1, exit_code)
        self.assertEqual("fail", report["decision"])
        self.assertEqual("controlled unavailable evidence", report["error"])

    def test_missing_run_head_identity_cannot_supply_current_head_success(self) -> None:
        for missing in (False, True):
            with self.subTest(field_missing=missing):
                api = self.complete_live_api()
                run = api.responses["repos/Coglatas/Coglatas/actions/runs/1001"]
                if missing:
                    del run["head_sha"]
                else:
                    run["head_sha"] = None
                report = guard.evaluate_live_pr(api, "Coglatas/Coglatas", 42, REGISTRY, now=NOW)
                gate = next(row for row in report["exact_head"]["gates"] if row["context"] == "build-test")
                self.assertEqual("fail", report["decision"])
                self.assertEqual("producer-drift", gate["reason"])
                self.assertEqual(["workflow-run-head-drift"], gate["producer_errors"])

    def test_cli_reports_malformed_ref_conditions_from_real_evaluator_as_json_failure(self) -> None:
        for conditions in (
            None,
            {"ref_name": None},
            {"ref_name": []},
            {"ref_name": {"include": None}},
            {"ref_name": {"include": "~DEFAULT_BRANCH"}},
        ):
            with self.subTest(conditions=conditions):
                api = self.complete_live_api()
                api.responses["repos/Coglatas/Coglatas/rulesets/123"]["conditions"] = conditions
                output = io.StringIO()
                with mock.patch.dict(required.os.environ, {"GITHUB_TOKEN": "", "GH_TOKEN": ""}), mock.patch.object(required, "GitHubApi", return_value=api), mock.patch.object(required.os.sys, "stdout", output):
                    exit_code = required.run_cli(["--live-pr", "42", "--repository", "Coglatas/Coglatas", "--json"])
                report = json.loads(output.getvalue())
                self.assertEqual(1, exit_code)
                self.assertEqual("fail", report["decision"])
                self.assertEqual("pass", report["exact_head"]["decision"])
                self.assertEqual("fail", report["live_ruleset"]["decision"])
                self.assertTrue(any("include ~DEFAULT_BRANCH" in error for error in report["live_ruleset"]["errors"]))



    def test_cli_refuses_unverified_ruleset_exclusions(self) -> None:
        for excluded in ("missing", None, "refs/heads/main", ["refs/heads/main"],
                         ["~DEFAULT_BRANCH"], ["~ALL"], ["refs/heads/*"],
                         ["refs/heads/other"], [None]):
            with self.subTest(excluded=excluded):
                api = self.complete_live_api()
                ref = api.responses["repos/Coglatas/Coglatas/rulesets/123"]["conditions"]["ref_name"]
                if excluded == "missing":
                    del ref["exclude"]
                else:
                    ref["exclude"] = excluded
                output = io.StringIO()
                with mock.patch.dict(required.os.environ, {"GITHUB_TOKEN": "", "GH_TOKEN": ""}), mock.patch.object(required, "GitHubApi", return_value=api), mock.patch.object(required.os.sys, "stdout", output):
                    exit_code = required.run_cli(["--live-pr", "42", "--repository", "Coglatas/Coglatas", "--json"])
                report = json.loads(output.getvalue())
                self.assertEqual(1, exit_code)
                self.assertEqual("fail", report["decision"])
                self.assertEqual("pass", report["exact_head"]["decision"])
                self.assertEqual("fail", report["live_ruleset"]["decision"])
                self.assertTrue(any("empty exclusion array" in error for error in report["live_ruleset"]["errors"]))


    def test_unresolved_run_cannot_retain_check_supplied_metadata(self) -> None:
        for url in (None, "missing", "https://github.com/Coglatas/Coglatas/checks/1"):
            with self.subTest(url=url):
                api = self.complete_live_api()
                check = api.responses[f"repos/Coglatas/Coglatas/commits/{HEAD}/check-runs?filter=latest&per_page=100"]["check_runs"][0]
                if url == "missing":
                    del check["details_url"]
                else:
                    check["details_url"] = url
                report = guard.evaluate_live_pr(api, "Coglatas/Coglatas", 42, REGISTRY, now=NOW)
                gate = next(row for row in report["exact_head"]["gates"] if row["context"] == "build-test")
                self.assertEqual("fail", report["decision"])
                self.assertEqual("producer-drift", gate["reason"])
                self.assertEqual(["workflow-producer-drift"], gate["producer_errors"])
                self.assertNotIn("repos/Coglatas/Coglatas/actions/runs/1001", api.calls)

    def test_missing_check_head_is_not_filled_from_requested_endpoint(self) -> None:
        api = self.complete_live_api()
        check = api.responses[f"repos/Coglatas/Coglatas/commits/{HEAD}/check-runs?filter=latest&per_page=100"]["check_runs"][0]
        del check["head_sha"]
        report = guard.evaluate_live_pr(api, "Coglatas/Coglatas", 42, REGISTRY, now=NOW)
        gate = next(row for row in report["exact_head"]["gates"] if row["context"] == "build-test")
        self.assertEqual("fail", report["decision"])
        self.assertEqual("previous-head-only", gate["reason"])
        self.assertEqual("fail", gate["decision"])

    def test_cli_reports_malformed_integration_identity_as_json_failure(self) -> None:
        for integration in ([], {}, "15368", 15368.0, True):
            with self.subTest(integration=integration):
                api = self.complete_live_api()
                api.responses["repos/Coglatas/Coglatas/rulesets/123"]["rules"][0]["parameters"]["required_status_checks"][0]["integration_id"] = integration
                output = io.StringIO()
                with mock.patch.dict(required.os.environ, {"GITHUB_TOKEN": "", "GH_TOKEN": ""}), mock.patch.object(required, "GitHubApi", return_value=api), mock.patch.object(required.os.sys, "stdout", output):
                    exit_code = required.run_cli(["--live-pr", "42", "--repository", "Coglatas/Coglatas", "--json"])
                report = json.loads(output.getvalue())
                self.assertEqual(1, exit_code)
                self.assertEqual("fail", report["decision"])
                self.assertEqual("pass", report["exact_head"]["decision"])
                self.assertTrue(any("malformed integration_id" in error for error in report["live_ruleset"]["errors"]))

    def test_default_branch_marker_cannot_hide_malformed_include_entries(self) -> None:
        for entry in (None, [], ""):
            with self.subTest(entry=entry):
                live = self.live_ruleset()
                live["conditions"]["ref_name"]["include"].append(entry)
                self.assertTrue(any("include ~DEFAULT_BRANCH" in error for error in guard.live_ruleset_errors(REGISTRY, live)))


    def test_check_run_cannot_use_commit_status_sha_field(self) -> None:
        api = self.complete_live_api()
        check = api.responses[f"repos/Coglatas/Coglatas/commits/{HEAD}/check-runs?filter=latest&per_page=100"]["check_runs"][0]
        del check["head_sha"]
        check["sha"] = HEAD
        report = guard.evaluate_live_pr(api, "Coglatas/Coglatas", 42, REGISTRY, now=NOW)
        self.assertEqual("fail", report["decision"])
        self.assertEqual("previous-head-only", report["exact_head"]["gates"][0]["reason"])

    def commit_status_api(self) -> tuple[dict[str, Any], FakeApi]:
        registry = registry_for_contexts("build-test")
        item = registry["checks"][0]
        item["kind"] = "commit-status"
        item["producer"] = {"type": "github-actions-status", "creator_login": "github-actions[bot]", "workflow_path_required": True}
        item["trigger"] = {"mode": "trusted-default-branch", "events": ["workflow_run"], "source_workflow": "PR CI"}
        item["prerequisites"] = []
        item.pop("conditional_prerequisites", None)
        item["ruleset_integration_id"] = None
        _, statuses = self.success_evidence(registry)
        status = statuses[0]
        for key in ("sha", "workflow", "workflow_event", "workflow_head_branch"):
            del status[key]
        status["target_url"] = "https://github.com/Coglatas/Coglatas/actions/runs/2001"
        prefix = "repos/Coglatas/Coglatas"
        api = FakeApi({
            f"{prefix}/pulls/42": {"state": "open", "head": {"sha": HEAD}, "base": {"ref": "main"}},
            f"{prefix}/rulesets": [{"id": 123, "name": registry["ruleset"]["name"]}],
            f"{prefix}/rulesets/123": self.live_ruleset(registry),
            f"{prefix}/commits/{HEAD}/check-runs?filter=latest&per_page=100": {"total_count": 0, "check_runs": []},
            f"{prefix}/commits/{HEAD}/statuses?per_page=100": statuses,
            f"{prefix}/commits/{HEAD}/status?per_page=100": {"sha": HEAD, "total_count": 1, "statuses": statuses},
            f"{prefix}/actions/runs/2001": {"path": item["workflow"], "event": "workflow_run", "head_sha": OLD_HEAD, "head_branch": "main"},
        })
        return registry, api

    def run_validated_fake_cli(self, registry: dict[str, Any], api: FakeApi) -> tuple[int, dict[str, Any]]:
        policy = json.loads(required.POLICY_PATH.read_text(encoding="utf-8"))
        control = next(value for value in policy["controls"] if value["id"] == "GOV-CHECKS-001")
        control["expected"]["required"] = [{key: item[key] for key in ("kind", "workflow", "job", "context")} for item in registry["checks"]]
        with tempfile.TemporaryDirectory(prefix="required-status-contract-") as folder:
            registry_path, policy_path = Path(folder) / "registry.json", Path(folder) / "policy.json"
            registry_path.write_text(json.dumps(registry), encoding="utf-8")
            policy_path.write_text(json.dumps(policy), encoding="utf-8")
            self.assertEqual(registry, required.load_required_check_registry(registry_path, policy_path))
            output = io.StringIO()
            with mock.patch.dict(required.os.environ, {"GITHUB_TOKEN": "", "GH_TOKEN": ""}), mock.patch.object(required, "GitHubApi", return_value=api), mock.patch.object(required.os.sys, "stdout", output):
                code = required.run_cli(["--live-pr", "42", "--repository", "Coglatas/Coglatas", "--registry", str(registry_path), "--policy", str(policy_path), "--json"])
            return code, json.loads(output.getvalue())

    def test_github_shaped_commit_status_uses_verified_combined_head(self) -> None:
        registry, api = self.commit_status_api()
        code, report = self.run_validated_fake_cli(registry, api)
        self.assertEqual(0, code)
        self.assertEqual("pass", report["decision"])
        self.assertIn(f"repos/Coglatas/Coglatas/commits/{HEAD}/status?per_page=100", api.calls)
        self.assertNotIn(f"repos/Coglatas/Coglatas/commits/{HEAD}/statuses?per_page=100", api.calls)

    def test_commit_status_combined_identity_missing_wrong_or_conflicting_fails(self) -> None:
        for kind in ("missing", "null", "previous", "conflicting"):
            with self.subTest(kind=kind):
                registry, api = self.commit_status_api()
                combined = api.responses[f"repos/Coglatas/Coglatas/commits/{HEAD}/status?per_page=100"]
                if kind == "missing":
                    del combined["sha"]
                elif kind == "conflicting":
                    combined["statuses"][0]["sha"] = OLD_HEAD
                else:
                    combined["sha"] = None if kind == "null" else OLD_HEAD
                with self.assertRaisesRegex(RuntimeError, "head identity"):
                    guard.evaluate_live_pr(api, "Coglatas/Coglatas", 42, registry, now=NOW)

    def test_commit_status_cannot_use_check_run_head_field(self) -> None:
        registry, _ = self.commit_status_api()
        _, statuses = self.success_evidence(registry, head=OLD_HEAD)
        statuses[0]["head_sha"] = HEAD
        report = guard.exact_head_report(registry, HEAD, [], statuses, now=NOW, trusted_base_ref="main")
        self.assertEqual("fail", report["decision"])

    def test_combined_status_page_two_must_retain_authoritative_head(self) -> None:
        registry, api = self.commit_status_api()
        path = f"repos/Coglatas/Coglatas/commits/{HEAD}/status?per_page=100"
        combined = api.responses[path]
        combined["statuses"] = [{**combined["statuses"][0], "id": index + 1} for index in range(100)]
        combined["total_count"] = 101
        api.responses[path + "&page=2"] = {"sha": OLD_HEAD, "total_count": 101, "statuses": []}
        with self.assertRaisesRegex(RuntimeError, "head identity"):
            guard.evaluate_live_pr(api, "Coglatas/Coglatas", 42, registry, now=NOW)


    def test_malformed_page_entries_fail_through_validated_cli(self) -> None:
        for kind in ("check-runs", "statuses"):
            with self.subTest(kind=kind):
                if kind == "check-runs":
                    registry, api = REGISTRY, self.complete_live_api()
                    page = api.responses[f"repos/Coglatas/Coglatas/commits/{HEAD}/check-runs?filter=latest&per_page=100"]
                    page["check_runs"] += [copy.deepcopy(page["check_runs"][0])] * 93 + [None]
                else:
                    registry, api = self.commit_status_api()
                    page = api.responses[f"repos/Coglatas/Coglatas/commits/{HEAD}/status?per_page=100"]
                    page["statuses"] = page["statuses"] * 99 + [None]
                page["total_count"] = 101
                code, report = self.run_validated_fake_cli(registry, api)
                self.assertEqual(1, code)
                self.assertEqual("fail", report["decision"])
                self.assertIn("page entries", report["error"])

    def test_short_page_cannot_hide_later_combined_status_head_drift(self) -> None:
        registry, api = self.commit_status_api()
        path = f"repos/Coglatas/Coglatas/commits/{HEAD}/status?per_page=100"
        api.responses[path]["total_count"] = 2
        api.responses[path + "&page=2"] = {"sha": OLD_HEAD, "total_count": 2, "statuses": []}
        code, report = self.run_validated_fake_cli(registry, api)
        self.assertEqual(1, code)
        self.assertIn("head identity", report["error"])
        self.assertIn(path + "&page=2", api.calls)

    def test_invalid_or_incomplete_pagination_totals_fail_as_json(self) -> None:
        for total in (None, "missing", True, -1, "1", 0, 2):
            with self.subTest(total=total):
                registry, api = self.commit_status_api()
                path = f"repos/Coglatas/Coglatas/commits/{HEAD}/status?per_page=100"
                if total == "missing":
                    del api.responses[path]["total_count"]
                else:
                    api.responses[path]["total_count"] = total
                api.responses[path + "&page=2"] = {"sha": HEAD, "total_count": total, "statuses": []}
                code, report = self.run_validated_fake_cli(registry, api)
                self.assertEqual(1, code)
                self.assertIn("pagination", report["error"])

    def test_commit_status_missing_creator_fails_closed_without_identity_inference(self) -> None:
        registry, api = self.commit_status_api()
        del api.responses[f"repos/Coglatas/Coglatas/commits/{HEAD}/status?per_page=100"]["statuses"][0]["creator"]
        code, report = self.run_validated_fake_cli(registry, api)
        self.assertEqual(1, code)
        self.assertEqual(["status-creator-drift"], report["exact_head"]["gates"][0]["producer_errors"])


    def test_combined_status_total_cannot_change_between_pages(self) -> None:
        registry, api = self.commit_status_api()
        path = f"repos/Coglatas/Coglatas/commits/{HEAD}/status?per_page=100"
        api.responses[path]["statuses"] = [{**api.responses[path]["statuses"][0], "id": index + 1} for index in range(100)]
        api.responses[path]["total_count"] = 101
        api.responses[path + "&page=2"] = {"sha": HEAD, "total_count": 100, "statuses": []}
        code, report = self.run_validated_fake_cli(registry, api)
        self.assertEqual(1, code)
        self.assertIn("pagination", report["error"])


    def test_duplicate_api_ids_cannot_fill_declared_total(self) -> None:
        for kind in ("check-runs", "statuses"):
            with self.subTest(kind=kind):
                if kind == "check-runs":
                    registry, api = REGISTRY, self.complete_live_api()
                    path = f"repos/Coglatas/Coglatas/commits/{HEAD}/check-runs?filter=latest&per_page=100"
                    rows = api.responses[path]["check_runs"]
                    rows += [{**rows[0], "id": index + 7} for index in range(94)]
                    api.responses[path]["total_count"] = 101
                    api.responses[path + "&page=2"] = {"total_count": 101, "check_runs": [copy.deepcopy(rows[0])]}
                else:
                    registry, api = self.commit_status_api()
                    path = f"repos/Coglatas/Coglatas/commits/{HEAD}/status?per_page=100"
                    row = api.responses[path]["statuses"][0]
                    api.responses[path] = {"sha": HEAD, "total_count": 101, "statuses": [{**row, "id": index + 1} for index in range(100)]}
                    api.responses[path + "&page=2"] = {"sha": HEAD, "total_count": 101, "statuses": [copy.deepcopy(row)]}
                code, report = self.run_validated_fake_cli(registry, api)
                self.assertEqual(1, code)
                self.assertIn("duplicate or invalid entry ID", report["error"])
                self.assertIn(path + "&page=2", api.calls)

    def test_malformed_api_ids_fail_through_validated_cli(self) -> None:
        for identity in (None, True, [], 0, "1"):
            with self.subTest(identity=identity):
                registry, api = self.commit_status_api()
                api.responses[f"repos/Coglatas/Coglatas/commits/{HEAD}/status?per_page=100"]["statuses"][0]["id"] = identity
                code, report = self.run_validated_fake_cli(registry, api)
                self.assertEqual(1, code)
                self.assertIn("duplicate or invalid entry ID", report["error"])


    def test_unordered_rejection_cannot_hide_behind_timestamped_success(self) -> None:
        for timestamp in (None, "invalid", []):
            with self.subTest(timestamp=timestamp):
                api = self.complete_live_api()
                page = api.responses[f"repos/Coglatas/Coglatas/commits/{HEAD}/check-runs?filter=latest&per_page=100"]
                rejection = {**page["check_runs"][0], "id": 999, "conclusion": "failure"}
                for key in ("completed_at", "updated_at", "started_at", "created_at"):
                    rejection.pop(key, None)
                    if timestamp is not None:
                        rejection[key] = timestamp
                page["check_runs"].append(rejection)
                page["total_count"] += 1
                code, report = self.run_validated_fake_cli(REGISTRY, api)
                self.assertEqual(1, code)
                self.assertIn("ordering timestamp", report["error"])

    def test_malformed_state_returns_json_failure_before_selection(self) -> None:
        for kind in ("check-runs", "statuses"):
            for state in ([], {}, True):
                with self.subTest(kind=kind, state=state):
                    if kind == "check-runs":
                        registry, api = REGISTRY, self.complete_live_api()
                        page = api.responses[f"repos/Coglatas/Coglatas/commits/{HEAD}/check-runs?filter=latest&per_page=100"]
                        page["check_runs"].append({**page["check_runs"][0], "id": 999, "status": state})
                    else:
                        registry, api = self.commit_status_api()
                        page = api.responses[f"repos/Coglatas/Coglatas/commits/{HEAD}/status?per_page=100"]
                        page["statuses"].append({**page["statuses"][0], "id": 999, "state": state})
                    page["total_count"] += 1
                    code, report = self.run_validated_fake_cli(registry, api)
                    self.assertEqual(1, code)
                    self.assertIn("state", report["error"])


    def test_completed_failure_cannot_use_an_earlier_start_timestamp(self) -> None:
        for timestamp in (None, "invalid", []):
            with self.subTest(timestamp=timestamp):
                api = self.complete_live_api()
                page = api.responses[f"repos/Coglatas/Coglatas/commits/{HEAD}/check-runs?filter=latest&per_page=100"]
                rejection = {**page["check_runs"][0], "id": 999, "conclusion": "failure", "completed_at": timestamp, "started_at": "2026-09-04T05:00:00Z"}
                page["check_runs"].append(rejection)
                page["total_count"] += 1
                code, report = self.run_validated_fake_cli(REGISTRY, api)
                self.assertEqual(1, code)
                self.assertIn("ordering timestamp", report["error"])

    def test_malformed_context_identity_returns_json_before_membership(self) -> None:
        for kind in ("check-runs", "statuses"):
            for context in ([], {}, True, None, ""):
                with self.subTest(kind=kind, context=context):
                    if kind == "check-runs":
                        registry, api = REGISTRY, self.complete_live_api()
                        api.responses[f"repos/Coglatas/Coglatas/commits/{HEAD}/check-runs?filter=latest&per_page=100"]["check_runs"][0]["name"] = context
                    else:
                        registry, api = self.commit_status_api()
                        api.responses[f"repos/Coglatas/Coglatas/commits/{HEAD}/status?per_page=100"]["statuses"][0]["context"] = context
                    code, report = self.run_validated_fake_cli(registry, api)
                    self.assertEqual(1, code)
                    self.assertIn("context identity", report["error"])

    def test_timestamp_utc_normalization_overflow_returns_json_failure(self) -> None:
        for timestamp in ("0001-01-01T00:00:00+01:00", "9999-12-31T23:59:59-01:00"):
            with self.subTest(timestamp=timestamp):
                api = self.complete_live_api()
                page = api.responses[f"repos/Coglatas/Coglatas/commits/{HEAD}/check-runs?filter=latest&per_page=100"]
                page["check_runs"][0]["completed_at"] = timestamp
                code, report = self.run_validated_fake_cli(REGISTRY, api)
                self.assertEqual(1, code)
                self.assertIn("ordering timestamp", report["error"])


    def test_check_conclusions_cannot_masquerade_as_lifecycle_status(self) -> None:
        for status in ("success", "failure", "cancelled"):
            with self.subTest(status=status):
                api = self.complete_live_api()
                check = api.responses[f"repos/Coglatas/Coglatas/commits/{HEAD}/check-runs?filter=latest&per_page=100"]["check_runs"][0]
                check.update(status=status, conclusion=None, started_at="2026-09-04T05:00:00Z")
                code, report = self.run_validated_fake_cli(REGISTRY, api)
                self.assertEqual(1, code)
                self.assertEqual("unknown-state", report["exact_head"]["gates"][0]["reason"])

    def test_overlapping_pending_check_cannot_hide_behind_completed_success(self) -> None:
        for reverse in (False, True):
            with self.subTest(reverse=reverse):
                api = self.complete_live_api()
                page = api.responses[f"repos/Coglatas/Coglatas/commits/{HEAD}/check-runs?filter=latest&per_page=100"]
                now = dt.datetime.now(dt.timezone.utc)
                build = page["check_runs"][0]
                build.update(started_at=(now - dt.timedelta(seconds=30)).isoformat(), completed_at=(now - dt.timedelta(seconds=5)).isoformat())
                page["check_runs"].append({**build, "id": 999, "status": "in_progress", "conclusion": None, "started_at": (now - dt.timedelta(seconds=10)).isoformat(), "completed_at": None})
                page["total_count"] += 1
                if reverse:
                    page["check_runs"].reverse()
                code, report = self.run_validated_fake_cli(REGISTRY, api)
                self.assertEqual(2, code)
                self.assertEqual("pending", report["decision"])
                self.assertEqual("current-head-pending", report["exact_head"]["gates"][0]["reason"])

    def test_expired_pending_check_cannot_hide_behind_success_or_fresh_pending(self) -> None:
        api = self.complete_live_api()
        page = api.responses[f"repos/Coglatas/Coglatas/commits/{HEAD}/check-runs?filter=latest&per_page=100"]
        build = page["check_runs"][0]
        now = dt.datetime.now(dt.timezone.utc)
        for identity, age in ((999, 1), (1000, 100000)):
            page["check_runs"].append({**build, "id": identity, "status": "in_progress", "conclusion": None, "completed_at": None, "started_at": (now - dt.timedelta(minutes=age)).isoformat()})
        page["total_count"] += 2
        code, report = self.run_validated_fake_cli(REGISTRY, api)
        self.assertEqual(1, code)
        self.assertEqual("pending-timeout", report["exact_head"]["gates"][0]["reason"])

    def test_malformed_trusted_status_run_event_fails_as_producer_drift(self) -> None:
        for event in ([], {}):
            with self.subTest(event=event):
                registry, api = self.commit_status_api()
                api.responses["repos/Coglatas/Coglatas/actions/runs/2001"]["event"] = event
                code, report = self.run_validated_fake_cli(registry, api)
                self.assertEqual(1, code)
                self.assertEqual(["trusted-status-event-drift"], report["exact_head"]["gates"][0]["producer_errors"])


class BackendDatabaseBootstrapTests(unittest.TestCase):
    """The PostgreSQL service must be initialized whenever routed PR tests run."""

    @staticmethod
    def condition(workflow: str, step_name: str) -> str:
        marker = f"      - name: {step_name}\\n".replace("\\n", "\n")
        if workflow.count(marker) != 1:
            raise AssertionError(f"Expected exactly one backend CI step: {step_name}")
        block = workflow.split(marker, 1)[1].split("      - name:", 1)[0]
        return next((line.strip() for line in block.splitlines() if line.strip().startswith("if: ")), "")

    def test_postgresql_migrations_and_ef_tooling_run_for_backend_tests_without_ef_changes(self) -> None:
        workflow = (Path(__file__).resolve().parents[2] / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        for step in ("Restore .NET tools", "Apply PostgreSQL migrations"):
            with self.subTest(step=step):
                condition = self.condition(workflow, step)
                self.assertIn("github.event_name != 'pull_request'", condition)
                self.assertIn("needs.dotnet-build.outputs.backend_ef == 'true'", condition)
                self.assertIn("needs.dotnet-build.outputs.backend_tests == 'true'", condition)

    def test_migration_model_drift_validation_still_uses_ef_scope(self) -> None:
        workflow = (Path(__file__).resolve().parents[2] / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        condition = self.condition(workflow, "Reject pending EF Core model changes")
        self.assertIn("needs.dotnet-build.outputs.backend_ef == 'true'", condition)


class GitHubApiSecurityTests(unittest.TestCase):
    def test_relative_query_and_enterprise_api_prefix_are_preserved(self) -> None:
        self.assertEqual(
            "https://api.github.com/repos/owner/repo?per_page=100",
            required._build_validated_github_api_url(
                "https://api.github.com", "repos/owner/repo?per_page=100"
            ),
        )
        self.assertEqual(
            "https://ghe.example/api/v3/repos/owner/repo?per_page=2",
            required._build_validated_github_api_url(
                "https://ghe.example/api/v3", "repos/owner/repo?per_page=2"
            ),
        )
        self.assertEqual(
            "https://ghe.example/api/v3/repos/owner/repo?page=2",
            required._build_validated_github_api_url(
                "https://ghe.example/api/v3",
                "https://ghe.example/api/v3/repos/owner/repo?page=2",
            ),
        )

    def test_untrusted_host_credentials_ports_and_traversal_are_rejected(self) -> None:
        for untrusted in (
            "https://evil.example/steal",
            "https://api.github.com.evil.example/path",
            "https://evil.example@api.github.com/path",
            "https://api.github.com:444/repos/owner/repo",
            "http://api.github.com/repos/owner/repo",
            "//evil.example/path",
            "repos/owner/../private",
            "repos/owner/%2e%2e/private",
            "repos/owner/%252e%252e/private",
            "repos\\\\owner",
            "repos/owner#fragment",
        ):
            with self.subTest(path=untrusted):
                with self.assertRaises(ValueError):
                    required._build_validated_github_api_url("https://api.github.com", untrusted)
        with self.assertRaises(ValueError):
            required._build_validated_github_api_url(
                "https://ghe.example/api/v3", "https://ghe.example/repos/owner/repo"
            )

    def test_nested_encoding_and_control_bytes_are_rejected(self) -> None:
        deeply_encoded_dot = "%2e"
        for _ in range(10):
            deeply_encoded_dot = deeply_encoded_dot.replace("%", "%25")
        for untrusted in (
            "repos/owner/%25252e%25252e/private",
            "repos/owner%25252f..%25252fprivate",
            f"repos/owner/{deeply_encoded_dot}{deeply_encoded_dot}/private",
            "repos/owner/%250a/private",
            "repos/owner/%7f/private",
        ):
            with self.subTest(path=untrusted):
                with self.assertRaises(ValueError):
                    required._build_validated_github_api_url("https://api.github.com", untrusted)
        with self.assertRaises(ValueError):
            required._build_validated_github_api_url(
                "https://ghe.example/api/v3",
                "https://ghe.example/api/v3/%25252e%25252e/private",
            )

    def test_safe_encoded_path_and_query_are_preserved(self) -> None:
        url = required._build_validated_github_api_url(
            "https://api.github.com", "repos/owner/a%20b?filter=latest%26extended"
        )
        self.assertEqual(
            "https://api.github.com/repos/owner/a%20b?filter=latest%26extended", url
        )

    def test_rejected_url_never_constructs_a_network_opener(self) -> None:
        with mock.patch.object(required.urllib.request, "build_opener") as builder:
            with self.assertRaisesRegex(RuntimeError, "URL was rejected"):
                required.GitHubApi(token="synthetic-token").get("https://evil.example/path")
        builder.assert_not_called()

    def test_redirects_are_rejected_before_forwarding_authorization(self) -> None:
        handler = required._RejectGitHubRedirects()
        request = urllib.request.Request(
            "https://api.github.com/repos/owner/repo",
            headers={"Authorization": "Bearer synthetic-token"},
        )
        self.assertIsNone(
            handler.redirect_request(request, None, 302, "Found", {}, "https://evil.example/steal")
        )

    def test_get_uses_redirect_rejecting_opener_without_network(self) -> None:
        opener = mock.Mock()
        opener.open.return_value = io.BytesIO(b'{"ok":true}')
        with mock.patch.object(required.urllib.request, "build_opener", return_value=opener) as builder:
            result = required.GitHubApi(token="synthetic-token").get("repos/owner/repo?per_page=2")
        self.assertEqual({"ok": True}, result)
        self.assertIsInstance(builder.call_args.args[0], required._RejectGitHubRedirects)
        request = opener.open.call_args.args[0]
        self.assertEqual("https://api.github.com/repos/owner/repo?per_page=2", request.full_url)
        self.assertEqual("Bearer synthetic-token", request.get_header("Authorization"))


if __name__ == "__main__":
    unittest.main()
