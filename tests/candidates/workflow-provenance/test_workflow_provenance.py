"""Real disposable-Git controls; these are not Product or native UI tests."""

from __future__ import annotations

import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import verify_workflow_provenance as verifier


class WorkflowProvenanceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.repo = Path(self.temporary.name) / "source"
        self.repo.mkdir()
        self.command("init", "-q")
        self.command("config", "user.name", "Synthetic provenance control")
        self.command("config", "user.email", "provenance@example.invalid")
        self.command("config", "core.autocrlf", "false")
        self.path = ".github/workflows/projectide-control-advisory.yml"
        for path, content in (
            (self.path, "name: original workflow\n"),
            (verifier.VERIFIER_PATH, "# committed synthetic verifier bytes\n"),
            (verifier.CONTROLS_PATH, "# committed synthetic control bytes\n"),
        ):
            target = self.repo / path
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(content.encode())
        self.base = self.commit("base")
        (self.repo / self.path).write_bytes(b"name: reviewed workflow\n")
        self.test = self.commit("test")
        self.tree = self.command("rev-parse", "HEAD^{tree}")
        self.workflow = self.merge_commit(self.tree, self.base, self.test)
        self.env = {
            "TEST_HEAD": self.test, "BASE_HEAD": self.base,
            "WORKFLOW_HEAD": self.workflow,
            "WORKFLOW_REF": f"{verifier.REPOSITORY}/{self.path}@refs/pull/321/merge",
            "PULL_REQUEST_NUMBER": "321",
            "PROVENANCE_BLOB": self.command("rev-parse", f"HEAD:{verifier.VERIFIER_PATH}"),
            "PROVENANCE_CONTROLS_BLOB": self.command("rev-parse", f"HEAD:{verifier.CONTROLS_PATH}"),
        }
        self.context = {
            "testHead": self.test, "productHead": "a" * 40,
            "runnerOs": "Synthetic", "executionScope": "CONTROL_ONLY",
            **{flag: False for flag in verifier.FLAGS},
        }
        self.context_path = Path(self.temporary.name) / "workflow-context.json"
        self.context_path.write_text(json.dumps(self.context), encoding="utf-8")
        self.original = self.context_path.read_bytes()

    def command(self, *args, input=None):
        result = subprocess.run(["git", "-C", str(self.repo), *args], input=input,
                                capture_output=True, check=True)
        return result.stdout.decode().strip()

    def commit(self, message):
        self.command("add", "--all")
        self.command("commit", "-qm", message)
        return self.command("rev-parse", "HEAD")

    def merge_commit(self, tree, *parents):
        args = ["commit-tree", tree]
        for parent in parents:
            args.extend(["-p", parent])
        return self.command(*args, input=b"synthetic workflow merge\n")

    def qualify(self, **changes):
        return verifier.qualify(self.repo, self.path, self.context_path, {**self.env, **changes})

    def reject(self, **changes):
        with self.assertRaises(verifier.Refusal):
            self.qualify(**changes)
        self.assertEqual(self.original, self.context_path.read_bytes())

    def test_accepts_actual_merge_with_exact_source_and_preserves_holds(self):
        result = self.qualify()
        self.assertNotEqual(self.test, result["workflowHead"])
        self.assertEqual([self.base, self.test], result["workflowParents"])
        self.assertEqual(self.tree, result["workflowTree"])
        self.assertEqual(self.tree, result["testTree"])
        self.assertEqual("100644", result["workflowMode"])
        self.assertEqual(result["workflowBlob"], result["testWorkflowBlob"])
        self.assertIs(result["workflowFetched"], False)
        for key, value in self.context.items():
            self.assertEqual(value, result[key])
        self.assertEqual(result, json.loads(self.context_path.read_text()))

    def test_rejects_test_head_aliased_as_workflow_head(self):
        self.reject(WORKFLOW_HEAD=self.test)

    def test_rejects_missing_immutable_commit_without_fallback(self):
        missing = "1" * 40
        original_run = subprocess.run
        calls = []

        def unavailable(args, **kwargs):
            if "fetch" in args:
                calls.append(args)
                return subprocess.CompletedProcess(args, 128, b"", b"unavailable")
            return original_run(args, **kwargs)

        with patch.object(verifier.subprocess, "run", side_effect=unavailable):
            self.reject(WORKFLOW_HEAD=missing)
        self.assertEqual(1, len(calls))
        self.assertEqual([verifier.FETCH_URL, missing], calls[0][-2:])
        self.assertNotIn("refs/pull", " ".join(calls[0]))

    def test_fetches_exact_missing_commit_and_checks_real_fetched_objects(self):
        remote = Path(self.temporary.name) / "remote.git"
        subprocess.run(["git", "clone", "--bare", str(self.repo), str(remote)],
                       capture_output=True, check=True)
        # Keep the synthetic merge in the remote, remove only its loose local object.
        self.command("push", str(remote), f"{self.workflow}:refs/heads/workflow")
        object_path = self.repo / ".git/objects" / self.workflow[:2] / self.workflow[2:]
        self.assertTrue(object_path.is_file())
        object_path.chmod(0o600)
        object_path.unlink()
        original_run = subprocess.run
        calls = []

        def local_transport(args, **kwargs):
            if "fetch" in args:
                calls.append(list(args))
                self.assertEqual([verifier.FETCH_URL, self.workflow], args[-2:])
                args = [*args[:-2], str(remote), args[-1]]
            return original_run(args, **kwargs)

        with patch.object(verifier.subprocess, "run", side_effect=local_transport):
            result = self.qualify()
        self.assertEqual(1, len(calls))
        self.assertIs(result["workflowFetched"], True)
        self.assertEqual(self.workflow, result["workflowHead"])

    def test_rejects_noncommit_workflow_object(self):
        self.reject(WORKFLOW_HEAD=self.tree)

    def test_rejects_noncanonical_commit_ids(self):
        for value in ("HEAD", self.workflow[:12], self.workflow + "\n", self.workflow.upper(), ""):
            with self.subTest(value=value):
                self.reject(WORKFLOW_HEAD=value)

    def test_rejects_wrong_repository_ref(self):
        self.reject(WORKFLOW_REF=self.env["WORKFLOW_REF"].replace(verifier.REPOSITORY, "foreign/repository"))

    def test_rejects_wrong_workflow_path_ref(self):
        self.reject(WORKFLOW_REF=self.env["WORKFLOW_REF"].replace("control-advisory", "other-advisory"))

    def test_rejects_moving_head_or_wrong_pr_ref(self):
        for ref in (self.env["WORKFLOW_REF"].replace("/merge", "/head"),
                    self.env["WORKFLOW_REF"].replace("/321/", "/322/")):
            with self.subTest(ref=ref):
                self.reject(WORKFLOW_REF=ref)

    def test_rejects_noncanonical_pr_number(self):
        for value in ("0321", "0", "321\n", "", "-1"):
            with self.subTest(value=value):
                self.reject(PULL_REQUEST_NUMBER=value)

    def test_rejects_noncanonical_path(self):
        with self.assertRaises(verifier.Refusal):
            verifier.qualify(self.repo, "../other.yml", self.context_path, self.env)
        self.assertEqual(self.original, self.context_path.read_bytes())

    def test_rejects_different_checked_out_head(self):
        self.command("checkout", "-q", self.base)
        self.reject()

    def test_rejects_foreign_git_dir_masking_an_incorrect_checkout(self):
        foreign = Path(self.temporary.name) / "foreign.git"
        subprocess.run(["git", "clone", "--bare", str(self.repo), str(foreign)],
                       capture_output=True, check=True)
        self.command("push", str(foreign), f"{self.workflow}:refs/heads/workflow")
        before = (foreign / "HEAD").read_bytes()
        self.command("checkout", "-q", self.base)
        with patch.dict(os.environ, {"GIT_DIR": str(foreign)}):
            self.reject()
        self.assertEqual(self.base, self.command("rev-parse", "HEAD"))
        self.assertEqual(before, (foreign / "HEAD").read_bytes())

    def test_uses_actual_checkout_despite_foreign_git_environment(self):
        foreign = Path(self.temporary.name) / "foreign.git"
        subprocess.run(["git", "clone", "--bare", str(self.repo), str(foreign)],
                       capture_output=True, check=True)
        subprocess.run(["git", "--git-dir", str(foreign), "update-ref", "HEAD", self.base],
                       capture_output=True, check=True)
        before = subprocess.check_output(["git", "--git-dir", str(foreign), "show-ref"])
        original_run = subprocess.run
        seen = []

        def inspect_environment(args, **kwargs):
            seen.append(args)
            self.assertFalse(any(key.upper().startswith("GIT_") for key in kwargs["env"]))
            return original_run(args, **kwargs)

        with patch.dict(os.environ, {
            "GIT_DIR": str(foreign), "GIT_WORK_TREE": str(foreign),
            "GIT_INDEX_FILE": str(foreign / "unrelated-index"),
            "GIT_CONFIG_COUNT": "1", "GIT_CONFIG_KEY_0": "alias.rev-parse",
            "GIT_CONFIG_VALUE_0": "unrelated", "git_unusual": "case-insensitive control",
        }), patch.object(verifier.subprocess, "run", side_effect=inspect_environment):
            self.assertEqual(self.workflow, self.qualify()["workflowHead"])
        self.assertTrue(seen)
        self.assertEqual(before, subprocess.check_output(["git", "--git-dir", str(foreign), "show-ref"]))
        self.assertFalse((foreign / "unrelated-index").exists())

    def test_rejects_reversed_or_extra_merge_parents(self):
        extra = self.merge_commit(self.tree, self.base)
        for parents in ((self.test, self.base), (self.base, self.test, extra)):
            with self.subTest(parents=parents):
                self.reject(WORKFLOW_HEAD=self.merge_commit(self.tree, *parents))

    def test_rejects_changed_base_even_with_unchanged_merge_tree(self):
        other_base = self.merge_commit(self.tree, self.base)
        self.reject(BASE_HEAD=other_base,
                    WORKFLOW_HEAD=self.merge_commit(self.tree, other_base, self.test))

    def test_rejects_changed_workflow_content(self):
        (self.repo / self.path).write_bytes(b"name: unreviewed workflow\n")
        self.command("add", self.path)
        tree = self.command("write-tree")
        self.command("reset", "--hard", self.test)
        self.reject(WORKFLOW_HEAD=self.merge_commit(tree, self.base, self.test))

    def test_rejects_changed_nonworkflow_tree_content(self):
        (self.repo / "foreign.txt").write_bytes(b"base drift\n")
        self.command("add", "foreign.txt")
        tree = self.command("write-tree")
        self.command("reset", "--hard", self.test)
        self.reject(WORKFLOW_HEAD=self.merge_commit(tree, self.base, self.test))

    def test_rejects_executable_workflow_mode(self):
        self.command("update-index", "--chmod=+x", self.path)
        tree = self.command("write-tree")
        self.command("reset", "--hard", self.test)
        self.reject(WORKFLOW_HEAD=self.merge_commit(tree, self.base, self.test))

    def test_rejects_absent_workflow_path(self):
        self.command("rm", self.path)
        tree = self.command("write-tree")
        self.command("reset", "--hard", self.test)
        self.reject(WORKFLOW_HEAD=self.merge_commit(tree, self.base, self.test))

    def test_rejects_changed_verifier_or_controls_bytes(self):
        for path in (verifier.VERIFIER_PATH, verifier.CONTROLS_PATH):
            with self.subTest(path=path):
                source = self.repo / path
                original = source.read_bytes()
                source.write_bytes(original + b"# drift\n")
                self.reject()
                source.write_bytes(original)

    def test_rejects_wrong_pinned_verifier_or_controls_blob(self):
        for key in ("PROVENANCE_BLOB", "PROVENANCE_CONTROLS_BLOB"):
            with self.subTest(key=key):
                self.reject(**{key: "a" * 40})

    def test_rejects_wrong_context_source_without_rewriting_it(self):
        self.context["testHead"] = self.base
        self.context_path.write_text(json.dumps(self.context))
        self.original = self.context_path.read_bytes()
        self.reject()

    def test_rejects_missing_or_promoted_qualification_flags(self):
        for flag in verifier.FLAGS:
            for value in (None, True, 0, "false"):
                with self.subTest(flag=flag, value=value):
                    self.context_path.write_text(json.dumps({**self.context, flag: value}))
                    self.original = self.context_path.read_bytes()
                    self.reject()

    def test_refuses_to_restamp_an_existing_workflow_receipt(self):
        self.qualify()
        self.original = self.context_path.read_bytes()
        self.reject()


if __name__ == "__main__":
    unittest.main()
