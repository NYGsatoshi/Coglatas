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
