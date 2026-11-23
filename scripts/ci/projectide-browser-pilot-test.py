"""Controls for selection and outcome refusal in the ProjectIDE browser pilot."""

import importlib.util
import re
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location(
    "projectide_browser_pilot", Path(__file__).with_name("projectide-browser-pilot.py"),
)
pilot = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pilot)


def sample(test_id, status=None, expected="passed"):
    attempts = [{"retry": 0, "status": status, "duration_ms": 1}] if status else []
    return {"id": test_id, "project": "chromium-desktop", "file": "projectide-sample.spec.ts",
            "line": 3, "title": "reads a fixture in en", "expected": expected,
            "status": "expected", "attempts": attempts}


class BrowserPilotControls(unittest.TestCase):
    def test_shared_selection_is_reflected_with_added_or_removed_supplemental_specs(self):
        workbench = "tests/ui/projectide-workbench.spec.ts"
        added = "tests/ui/projectide-added.spec.ts"
        runner = f"""const staticSuiteFiles = ['{workbench}', "tests/ui/angular-smoke.spec.ts",];"""
        for tracked, expected in (([workbench, added], {added}), ([workbench], set())):
            with self.subTest(tracked=tracked):
                self.assertEqual(set(tracked) - set(pilot.shared_projectide_specs(runner, tracked)), expected)

    def test_unrecognized_shared_selection_and_missing_paths_fail_closed(self):
        for selector in ("staticSuiteFiles = importedList;", "const staticSuiteFiles = [dynamic(),];",
                         "const staticSuiteFiles = ['tests/ui/projectide-missing.spec.ts'];"):
            with self.subTest(selector=selector), self.assertRaises(ValueError):
                pilot.shared_projectide_specs(selector, ["tests/ui/projectide-workbench.spec.ts"])

    def test_playwright_argument_matches_only_the_exact_relative_path(self):
        pattern, = pilot.spec_arguments(["tests/ui/projectide-added.spec.ts"])
        self.assertIsNotNone(re.search(pattern, "/repo/tests/ui/projectide-added.spec.ts"))
        for path in ("tests/ui/projectide-addedXspecXts", "tests/ui/projectide-added.spec.ts.bak"):
            self.assertIsNone(re.search(pattern, path))

    def test_distinct_shards_cover_the_supplemental_roster(self):
        pilot.verify_partition({"shared", "one", "two", "new"}, {"shared"}, [{"one", "new"}, {"two"}])
        pilot.verify_partition({"shared", "one", "two"}, {"shared"}, [{"one"}, {"two"}])

    def test_empty_duplicate_missing_and_shared_shard_ids_are_rejected(self):
        for shards in ([{"one"}, set()], [{"one"}, {"one"}], [{"one"}],
                       [{"one", "shared"}, {"two"}], [{"one", "extra"}, {"two"}]):
            with self.subTest(shards=shards), self.assertRaises(ValueError):
                pilot.verify_partition({"shared", "one", "two"}, {"shared"}, shards)

    def test_skip_retry_and_unexecuted_are_distinct(self):
        expected = {key: sample(key) for key in ("initial", "retry", "skipped", "missing")}
        retried = sample("retry", "failed")
        retried["attempts"].append({"retry": 1, "status": "passed", "duration_ms": 5})
        actual = {"initial": sample("initial", "passed"), "retry": retried,
                  "skipped": sample("skipped", "skipped")}
        rows = pilot.attempts_for_shard(expected, actual)
        self.assertEqual([row["outcome"] for row in rows], ["PASS", "RETRY_PASS", "SKIP", "UNEXECUTED"])
        pilot.verify_case_coverage(rows[:2])
        with self.assertRaises(ValueError):
            pilot.verify_case_coverage(rows)

    def test_unexpected_and_changed_execution_identities_are_rejected(self):
        for actual in ({"extra": sample("extra", "passed")},
                       {"selected": {**sample("selected", "passed"), "line": 4}}):
            with self.subTest(actual=actual), self.assertRaises(ValueError):
                pilot.attempts_for_shard({"selected": sample("selected")}, actual)


if __name__ == "__main__":
    unittest.main()
