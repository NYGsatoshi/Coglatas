"""Real Git negative controls for exact-candidate verification prerequisites."""
import json
import copy
from pathlib import Path
import subprocess
import tempfile
import unittest

from projectide_web_verify import assert_candidate, verify, browser_inventory, vitest_inventory


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


class WebInventoryIntegrityTests(unittest.TestCase):
    @staticmethod
    def vitest():
        return dict(success=True, numTotalTests=1, numPassedTests=1, numFailedTests=0,
                    numPendingTests=0, numTodoTests=0,
                    testResults=[dict(name="preview.spec.ts", assertionResults=[dict(fullName="case", status="passed")])])

    def test_vitest_preserves_actual_executed_identity(self):
        self.assertEqual(vitest_inventory(self.vitest()), [dict(file="preview.spec.ts", name="case", status="passed")])

    def test_vitest_false_green_counts_duplicates_and_nonpassed_assertions_are_rejected(self):
        for kind in ["passed-count", "missing-case", "duplicate", "failed-case", "empty-name", "summary-failure"]:
            with self.subTest(kind=kind):
                value = self.vitest()
                cases = value["testResults"][0]["assertionResults"]
                if kind == "passed-count": value["numPassedTests"] = 0
                if kind == "missing-case": cases.clear()
                if kind == "duplicate":
                    cases.append(copy.deepcopy(cases[0])); value["numPassedTests"] = 2; value["numTotalTests"] = 2
                if kind == "failed-case": cases[0]["status"] = "failed"
                if kind == "empty-name": cases[0]["fullName"] = ""
                if kind == "summary-failure": value["numFailedTests"] = 1
                with self.assertRaises(ValueError): vitest_inventory(value)

    @staticmethod
    def junit():
        return ('<testsuites tests="2" failures="0" skipped="0" errors="0">'
                '<testsuite hostname="chromium-desktop" tests="1"><testcase name="case" classname="preview.spec.ts"/></testsuite>'
                '<testsuite hostname="chromium-mobile" tests="1"><testcase name="case" classname="preview.spec.ts"/></testsuite></testsuites>')

    def test_browser_inventory_keeps_identical_names_distinct_by_project(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "browser.xml"; path.write_text(self.junit(), encoding="utf-8")
            tests = browser_inventory(path)
            self.assertEqual({t["project"] for t in tests}, {"chromium-desktop", "chromium-mobile"})
            self.assertEqual(len(tests), 2)

    def test_browser_duplicate_projects_missing_binding_and_false_green_counters_are_rejected(self):
        original = self.junit()
        variants = [original.replace('hostname="chromium-mobile"', 'hostname="chromium-desktop"'),
                    original.replace('hostname="chromium-mobile"', ''),
                    original.replace('tests="2"', 'tests="3"'),
                    original.replace('name="case" classname="preview.spec.ts"/>', 'name="case" classname="preview.spec.ts"><skipped/></testcase>', 1),
                    original.replace('classname="preview.spec.ts"', '', 1)]
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "browser.xml"
            for value in variants:
                with self.subTest(xml=value):
                    path.write_text(value, encoding="utf-8")
                    with self.assertRaises(ValueError): browser_inventory(path)


if __name__ == "__main__":
    unittest.main()
