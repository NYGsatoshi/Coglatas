"""Real Git negative controls for exact-candidate verification prerequisites."""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

from projectide_web_verify import assert_candidate, verify


class WebVerificationCandidateTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name) / "repository"
        self.root.mkdir()
        self.git("init", "--quiet")
        self.git("config", "user.name", "Candidate control")
        self.git("config", "user.email", "candidate@example.invalid")
        self.source = self.root / "source.txt"
        self.source.write_text("original\n", encoding="utf-8")
        self.git("add", "source.txt")
        self.git("commit", "--quiet", "-m", "Initial candidate")
        self.value = dict(combinedCommit=self.git("rev-parse", "HEAD"),
                          combinedTree=self.git("rev-parse", "HEAD^{tree}"))

    def git(self, *arguments):
        return subprocess.check_output(["git", *arguments], cwd=self.root, text=True).strip()

    def test_clean_exact_commit_and_tree_are_accepted(self):
        assert_candidate(self.root, self.value)

    def test_unstaged_source_change_is_rejected_with_the_same_head_tree(self):
        self.source.write_text("substituted\n", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "uncommitted changes"):
            assert_candidate(self.root, self.value)

    def test_staged_source_change_is_rejected_with_the_same_head_tree(self):
        self.source.write_text("substituted\n", encoding="utf-8")
        self.git("add", "source.txt")
        with self.assertRaisesRegex(ValueError, "uncommitted changes"):
            assert_candidate(self.root, self.value)

    def test_another_commit_with_the_same_tree_is_not_the_recorded_candidate(self):
        self.git("commit", "--quiet", "--allow-empty", "-m", "Another commit")
        self.assertEqual(self.value["combinedTree"], self.git("rev-parse", "HEAD^{tree}"))
        with self.assertRaisesRegex(ValueError, "commit mismatch"):
            assert_candidate(self.root, self.value)

    def test_a_substituted_receipt_tree_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "tree mismatch"):
            assert_candidate(self.root, dict(self.value, combinedTree="0" * 40))

    def test_failed_candidate_precondition_is_retained_without_executing_tests(self):
        self.source.write_text("substituted\n", encoding="utf-8")
        receipt = Path(self.directory.name) / "evidence" / "receipt.json"
        receipt.parent.mkdir()
        receipt.write_text(json.dumps(self.value), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "uncommitted changes"):
            verify(self.root, receipt)
        actual = json.loads(receipt.read_text(encoding="utf-8"))
        self.assertEqual(actual["status"], "FAILED")
        self.assertEqual(actual["commands"], [])
        self.assertIn("uncommitted changes", actual["failure"])


if __name__ == "__main__":
    unittest.main()
