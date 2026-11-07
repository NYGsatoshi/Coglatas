"""Reject entry-suite identity loss even when passing case counts are unchanged."""
import copy
import json
from pathlib import Path
import unittest

from projectide_entry_isolated import require_retained_entry_identities

BASELINE = Path(__file__).resolve().parents[1] / "Coglatas.ProjectIde.Entry.Tests/retained-test-identities.v1.json"


class EntryIdentityRetentionTests(unittest.TestCase):
    def setUp(self):
        self.baseline = json.loads(BASELINE.read_text(encoding="utf-8"))
        self.cases = self.baseline["cases"]

    def test_exact_retained_cases_and_additional_cases_are_accepted(self):
        require_retained_entry_identities(self.cases, self.baseline)
        require_retained_entry_identities(self.cases + ["Coglatas.Tests.ProjectIde.Entry.Additional.Case"], self.baseline)

    def test_same_count_passing_replacement_cannot_hide_a_lost_identity(self):
        replaced = self.cases[:-1] + ["Coglatas.Tests.ProjectIde.Entry.Replacement.Case"]
        self.assertEqual(len(self.cases), len(replaced))
        with self.assertRaisesRegex(ValueError, "identity was lost"):
            require_retained_entry_identities(replaced, self.baseline)

    def test_empty_partial_and_duplicated_execution_are_rejected(self):
        for cases in ([], self.cases[:-1], self.cases[:-1] + [self.cases[0]]):
            with self.subTest(cases=cases), self.assertRaises(ValueError):
                require_retained_entry_identities(cases, self.baseline)

    def test_missing_duplicate_or_unpinned_baseline_is_rejected(self):
        for baseline in ({}, [], None):
            with self.subTest(baseline=baseline), self.assertRaises(ValueError):
                require_retained_entry_identities(self.cases, baseline)
        for key, value in (("cases", []), ("cases", self.cases[:-1]),
                           ("cases", self.cases[:-1] + [self.cases[0]]),
                           ("cases", [None] * 45), ("schemaVersion", 2), ("schemaVersion", True),
                           ("sourceTestHead", "f" * 40)):
            baseline = copy.deepcopy(self.baseline)
            baseline[key] = value
            with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                require_retained_entry_identities(self.cases, baseline)


if __name__ == "__main__":
    unittest.main()
