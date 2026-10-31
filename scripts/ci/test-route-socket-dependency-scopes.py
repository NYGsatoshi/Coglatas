#!/usr/bin/env python3
"""Fail-closed regression tests for Socket Firewall's selective install route."""

from __future__ import annotations

import importlib.util
import subprocess
import tempfile
import unittest
from pathlib import Path

MODULE_PATH = Path(__file__).with_name("route-socket-dependency-scopes.py")
SPEC = importlib.util.spec_from_file_location("socket_scopes", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
ROUTER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ROUTER)
FULL = list(ROUTER.SCOPES)


def git(repo: Path, *args: str) -> str:
    return subprocess.run(["git", *args], cwd=repo, check=True, capture_output=True, text=True).stdout.strip()


class SocketScopeTests(unittest.TestCase):
    def test_one_frontend_manifest_only_scans_frontend(self):
        self.assertEqual(ROUTER.scopes_for_paths(["frontend/package-lock.json"]), ["frontend"])

    def test_root_manifest_only_scans_root(self):
        self.assertEqual(ROUTER.scopes_for_paths(["package.json"]), ["."])

    def test_multiple_manifests_select_ordered_unique_projects(self):
        self.assertEqual(
            ROUTER.scopes_for_paths([
                "tools/frontend-inspections/package.json",
                "frontend/package-lock.json",
                "frontend/package.json",
            ]),
            ["frontend", "tools/frontend-inspections"],
        )

    def test_project_npmrc_and_shrinkwrap_are_dependency_inputs(self):
        self.assertEqual(ROUTER.scopes_for_paths(["frontend/.npmrc"]), ["frontend"])
        self.assertEqual(ROUTER.scopes_for_paths(["coglatas-frontend/npm-shrinkwrap.json"]), ["coglatas-frontend"])

    def test_root_npmrc_always_scans_everything(self):
        self.assertEqual(ROUTER.scopes_for_paths([".npmrc", "frontend/package.json"]), FULL)

    def test_unknown_npm_project_does_not_disappear(self):
        self.assertEqual(ROUTER.scopes_for_paths(["tools/new-project/package.json"]), FULL)

    def test_workflow_router_and_test_changes_scan_everything(self):
        for path in ROUTER.ROUTING_INPUTS:
            with self.subTest(path=path):
                self.assertEqual(ROUTER.scopes_for_paths([path]), FULL)

    def test_workflow_contract_tracks_router_inputs_and_full_history(self):
        workflow = (MODULE_PATH.parents[2] / ".github/workflows/socket-dependency-security.yml").read_text(encoding="utf-8")
        self.assertIn("package_dir: " + chr(36) + "{{ fromJSON(needs.route.outputs.scopes) }}", workflow)
        self.assertIn("fetch-depth: 0", workflow)
        self.assertIn("needs: route", workflow)
        self.assertEqual(workflow.count("branches: [main, SecurityTestImplimation]"), 2)
        for path in ROUTER.ROUTING_INPUTS:
            with self.subTest(path=path):
                self.assertEqual(workflow.count('"' + path + '"'), 2)
        for pattern in ('**/package.json', '**/package-lock.json', '**/npm-shrinkwrap.json', '**/.npmrc'):
            with self.subTest(pattern=pattern):
                self.assertEqual(workflow.count('"' + pattern + '"'), 2)

    def test_legacy_guard_covers_security_test_base_and_merge_queue(self):
        guard = (MODULE_PATH.parents[2] / ".github/workflows/legacy-name-guard.yml").read_text(encoding="utf-8")
        self.assertIn("  pull_request:\n", guard)
        self.assertIn("  merge_group:\n", guard)
        self.assertIn("    branches: [main, SecurityTestImplimation]", guard)

    def test_irrelevant_or_empty_paths_fall_back_to_full(self):
        self.assertEqual(ROUTER.scopes_for_paths(["README.md"]), FULL)
        self.assertEqual(ROUTER.scopes_for_paths([]), FULL)

    def test_mixed_unrelated_changes_preserve_targeted_scan(self):
        self.assertEqual(ROUTER.scopes_for_paths(["docs/notice.md", "frontend/package.json"]), ["frontend"])

    def test_malformed_sha_or_manual_event_scan_all(self):
        self.assertEqual(ROUTER.select_scopes("pull_request", "evil", "1" * 40)[0], FULL)
        self.assertEqual(ROUTER.select_scopes("push", "0" * 40, "1" * 40)[0], FULL)
        self.assertEqual(ROUTER.select_scopes("workflow_dispatch", "", "")[0], FULL)

    def test_same_sha_scans_all(self):
        self.assertEqual(ROUTER.select_scopes("push", "f" * 40, "f" * 40)[0], FULL)

    def test_git_diff_failure_scans_all(self):
        with tempfile.TemporaryDirectory() as tmp:
            self.assertEqual(ROUTER.select_scopes("push", "a" * 40, "b" * 40, cwd=tmp)[0], FULL)

    def test_git_commit_diff_and_pr_merge_base(self):
        with tempfile.TemporaryDirectory() as temp:
            repo = Path(temp)
            git(repo, "init", "-q")
            git(repo, "config", "user.email", "ci@example.invalid")
            git(repo, "config", "user.name", "CI")
            (repo / "frontend").mkdir()
            (repo / "frontend" / "package-lock.json").write_text("{}\n", encoding="utf-8")
            git(repo, "add", ".")
            git(repo, "commit", "-qm", "base")
            base = git(repo, "rev-parse", "HEAD")
            (repo / "frontend" / "package-lock.json").write_text('{"updated":true}\n', encoding="utf-8")
            git(repo, "add", ".")
            git(repo, "commit", "-qm", "update frontend lockfile")
            head = git(repo, "rev-parse", "HEAD")
            self.assertEqual(ROUTER.select_scopes("push", base, head, cwd=repo)[0], ["frontend"])
            self.assertEqual(ROUTER.select_scopes("pull_request", base, head, cwd=repo)[0], ["frontend"])
            # Unknown dependency project must fail closed on a real git diff.
            (repo / "new-project").mkdir()
            (repo / "new-project" / "package.json").write_text("{}\n", encoding="utf-8")
            git(repo, "add", ".")
            git(repo, "commit", "-qm", "new project")
            unknown_head = git(repo, "rev-parse", "HEAD")
            self.assertEqual(ROUTER.select_scopes("push", head, unknown_head, cwd=repo)[0], FULL)


if __name__ == "__main__":
    unittest.main()
