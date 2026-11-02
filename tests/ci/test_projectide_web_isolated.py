"""Fail-closed controls for the additional Web candidate composer."""
import tempfile
import subprocess
import unittest
from pathlib import Path
from unittest.mock import patch
import projectide_web_isolated as composer

class WebCompositionTests(unittest.TestCase):
    def test_symbolic_or_abbreviated_refs_are_rejected_before_composition(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(composer,"git") as git:
            root=Path(directory)
            for bad in ["HEAD","abc123","A"*40,"0"*39]:
                with self.assertRaises(ValueError): composer.compose(root,root/"new",root/"receipt.json",bad,"1"*40)
            git.assert_not_called()

    def test_existing_worktree_is_never_reused_or_overwritten(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(composer,"git") as git:
            root=Path(directory)
            with self.assertRaises(ValueError): composer.compose(root,root,root/"receipt.json","0"*40,"1"*40)
            git.assert_not_called()

    def test_product_cannot_absorb_independent_web_tests(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(composer,"changes",return_value=["commit"]), patch.object(composer,"git",return_value="frontend/src/app/features/projectide/example.spec.ts") as git:
            root=Path(directory)
            with self.assertRaisesRegex(ValueError,"Product candidate contains independent Test source"):
                composer.compose(root,root/"new",root/"receipt.json","0"*40,"1"*40)
            self.assertFalse(any("worktree" in call.args for call in git.call_args_list))

    def test_test_branch_cannot_change_frontend_runtime_source(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(composer,"changes",side_effect=[[],["commit"],["commit"]]), patch.object(composer,"git",return_value="frontend/src/app/features/projectide/example.ts") as git:
            root=Path(directory)
            with self.assertRaisesRegex(ValueError,"Test candidate changed frontend Product source"):
                composer.compose(root,root/"new",root/"receipt.json","0"*40,"1"*40)
            self.assertFalse(any("worktree" in call.args for call in git.call_args_list))

    def test_composed_runtime_delta_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(composer,"changes",return_value=[]), patch.object(composer,"git",return_value="src/Coglatas.Domain/injected.cs"):
            root=Path(directory)
            with self.assertRaisesRegex(ValueError,"Composition changed Product runtime source"):
                composer.compose(root,root/"new",root/"receipt.json","0"*40,"1"*40)

class PinnedDependencyMergeTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.git("init", "--quiet")
        self.git("config", "user.name", "Dependency control")
        self.git("config", "user.email", "dependency@example.invalid")
        self.git("config", "core.autocrlf", "false")
        (self.root / "core.txt").write_text("original\n", encoding="utf-8")
        self.git("add", ".")
        self.git("commit", "--quiet", "-m", "Initial Core")
        self.base = self.git("rev-parse", "HEAD")
        (self.root / "core.txt").write_text("updated Core\n", encoding="utf-8")
        self.git("commit", "--quiet", "-am", "Core update")
        self.core = self.git("rev-parse", "HEAD")
        self.git("checkout", "--quiet", "-b", "web", self.base)
        (self.root / "frontend").mkdir()
        (self.root / "frontend/view.ts").write_text("export {};\n", encoding="utf-8")
        self.git("add", ".")
        self.git("commit", "--quiet", "-m", "Independent Web update")
        self.web = self.git("rev-parse", "HEAD")

    def git(self, *arguments):
        return subprocess.check_output(["git", *arguments], cwd=self.root, text=True,
                                       stderr=subprocess.PIPE).strip()

    def merge(self, additional_resolution=False):
        self.git("merge", "--no-ff", "--no-commit", self.core)
        if additional_resolution:
            (self.root / "core.txt").write_text("additional resolution\n", encoding="utf-8")
            self.git("add", "core.txt")
        self.git("commit", "--quiet", "-m", "Merge pinned Core")
        return self.git("rev-parse", "HEAD")

    def test_exact_automatic_dependency_merge_is_recorded_without_being_cherry_picked(self):
        head = self.merge()
        merges = []
        self.assertEqual(composer.changes(self.root, self.core, head, ("frontend/",), self.core, merges), [self.web])
        self.assertEqual(merges, [head])

    def test_an_unpinned_dependency_parent_is_rejected(self):
        head = self.merge()
        with self.assertRaisesRegex(ValueError, "exact pinned Core dependency"):
            composer.changes(self.root, self.base, head, ("frontend/",), self.base)

    def test_manual_resolution_delta_is_rejected(self):
        head = self.merge(additional_resolution=True)
        with self.assertRaisesRegex(ValueError, "additional resolution changes"):
            composer.changes(self.root, self.core, head, ("frontend/",), self.core)

    def test_a_later_web_commit_cannot_modify_core_runtime(self):
        self.merge()
        (self.root / "core.txt").write_text("Web override\n", encoding="utf-8")
        self.git("commit", "--quiet", "-am", "Cross ownership boundary")
        with self.assertRaisesRegex(ValueError, "ownership boundary"):
            composer.changes(self.root, self.core, self.git("rev-parse", "HEAD"), ("frontend/",), self.core)


if __name__ == "__main__": unittest.main()
