"""Fail-closed controls for the additional Web candidate composer."""
import tempfile
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

if __name__ == "__main__": unittest.main()
