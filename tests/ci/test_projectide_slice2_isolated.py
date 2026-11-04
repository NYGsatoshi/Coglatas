"""Negative controls for the additional isolated verification path."""

from pathlib import Path
import tempfile
import unittest

from projectide_slice2_isolated import changes, counters, git, sha


class IsolatedEvidenceTests(unittest.TestCase):
    def test_zero_failed_skipped_and_incomplete_runs_cannot_pass(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "results.trx"
            for total, executed, passed, failed, skipped in (
                (0, 0, 0, 0, 0), (3, 3, 2, 1, 0), (3, 2, 2, 0, 1), (3, 2, 3, 0, 0),
            ):
                with self.subTest(total=total, failed=failed, skipped=skipped):
                    path.write_text(f'<TestRun xmlns="fixture"><ResultSummary><Counters total="{total}" '
                                    f'executed="{executed}" passed="{passed}" failed="{failed}" '
                                    f'notExecuted="{skipped}"/></ResultSummary></TestRun>', encoding="utf-8")
                    with self.assertRaises(ValueError):
                        counters(path)
            path.write_text('<TestRun xmlns="fixture"><ResultSummary><Counters total="3" executed="3" '
                            'passed="3" failed="0" notExecuted="0"/></ResultSummary></TestRun>', encoding="utf-8")
            self.assertEqual(3, counters(path)["passed"])

    def test_test_changes_cannot_copy_product_source(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            git(root, "init", "-q")
            git(root, "config", "user.name", "Fixture")
            git(root, "config", "user.email", "fixture@example.invalid")
            (root / "base.txt").write_text("base", encoding="utf-8")
            git(root, "add", "base.txt")
            git(root, "commit", "-qm", "base")
            base = git(root, "rev-parse", "HEAD")
            (root / "tests").mkdir()
            (root / "tests" / "fixture.cs").write_text("fixture", encoding="utf-8")
            git(root, "add", "tests/fixture.cs")
            git(root, "commit", "-qm", "test")
            head = git(root, "rev-parse", "HEAD")
            self.assertEqual([head], changes(root, base, head, ("tests/",)))
            (root / "src").mkdir()
            (root / "src" / "product.cs").write_text("product", encoding="utf-8")
            git(root, "add", "src/product.cs")
            git(root, "commit", "-qm", "illegal source copy")
            with self.assertRaises(ValueError):
                changes(root, base, git(root, "rev-parse", "HEAD"), ("tests/",))

    def test_branch_names_and_shell_text_are_not_candidate_shas(self):
        for invalid in ("main", "HEAD", "a" * 39, "A" * 40, "a" * 40 + ";echo secret", "a" * 40 + "\n"):
            with self.subTest(value=invalid), self.assertRaises(ValueError):
                sha(invalid)


if __name__ == "__main__":
    unittest.main()
