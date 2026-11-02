"""Tampered/partial TRX must never acquire supplemental acceptance credit."""
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

from projectide_core_isolated import inventory


def valid_trx():
    return ET.fromstring('''<TestRun><ResultSummary><Counters total="1" executed="1" passed="1"/></ResultSummary>
      <Results><UnitTestResult testId="test" executionId="run" testName="fixture" outcome="Passed"/></Results>
      <TestDefinitions><UnitTest id="test"><Execution id="run"/></UnitTest></TestDefinitions>
      <TestEntries><TestEntry testId="test" executionId="run"/></TestEntries></TestRun>''')


class CoreInventoryTests(unittest.TestCase):
    def test_complete_bijection_is_required(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "result.trx"
            for mutation in ("valid", "no-result", "no-definition", "no-entry", "wrong-entry", "not-run", "failed", "zero", "duplicate", "error"):
                with self.subTest(mutation=mutation):
                    root = valid_trx()
                    if mutation.startswith("no-"):
                        root.remove(root.find({"no-result": "Results", "no-definition": "TestDefinitions", "no-entry": "TestEntries"}[mutation]))
                    elif mutation == "wrong-entry": root.find("TestEntries/TestEntry").set("testId", "other")
                    elif mutation == "not-run": root.find("Results/UnitTestResult").set("outcome", "NotExecuted")
                    elif mutation == "failed": root.find("ResultSummary/Counters").set("failed", "1")
                    elif mutation == "zero": root.find("ResultSummary/Counters").set("total", "0")
                    elif mutation == "duplicate": root.find("Results").append(root.find("Results/UnitTestResult"))
                    elif mutation == "error": root.find("ResultSummary/Counters").set("error", "1")
                    ET.ElementTree(root).write(path)
                    if mutation == "valid": self.assertEqual(["fixture"], inventory(path))
                    else:
                        with self.assertRaises(ValueError): inventory(path)


if __name__ == "__main__":
    unittest.main()

class ProductDependencyMergeTests(unittest.TestCase):
    def test_only_the_exact_reconciled_parent_and_unchanged_resolution_tree_are_allowed(self):
        from unittest.mock import patch
        from projectide_core_isolated import product_changes
        merge = "610f9911f9ce31739188437972f7eeb018f611da"
        parents = merge + " e7a0b7b9da9a9f761b9c018c62b72e0fe7e901a4 5ef8b9caa4cbb0228a48b5c13b4f74bb07837547"
        for candidate, actual_parents, delta, accepted in [
                (merge, parents, "", True), ("a"*40, parents, "", False),
                (merge, parents.replace("5ef8b9ca", "0ef8b9ca"), "", False),
                (merge, parents, "unexpected resolution edit", False)]:
            with self.subTest(candidate=candidate, delta=delta):
                with patch("projectide_core_isolated.git", side_effect=["", candidate, actual_parents, delta]):
                    if accepted: self.assertEqual([merge], product_changes(Path("."), "b"*40, "c"*40))
                    else:
                        with self.assertRaisesRegex(ValueError, "Unapproved Product dependency"):
                            product_changes(Path("."), "b"*40, "c"*40)
