"""SEC-06/SEC-14: native ZAP risk fields must not contradict each other."""

import runpy
from pathlib import Path
import unittest

MODULE = runpy.run_path(str(Path(__file__).resolve().parents[2] /
                            "scripts/security/process-zap-report.py"))


class NativeZapRiskConsistencyTests(unittest.TestCase):
    def test_matching_high_fields_remain_blocking(self):
        normalize = MODULE["normalize_risk"]
        blocking = MODULE["enforce_blocking_policy"]
        self.assertEqual("High", normalize({"riskcode": "3", "riskdesc": "High (Medium)"}))
        with self.assertRaisesRegex(SystemExit, "High-risk"):
            blocking(0, {"High": 1})

    def test_matching_medium_fields_remain_medium(self):
        self.assertEqual("Medium", MODULE["normalize_risk"](
            {"riskcode": "2", "riskdesc": "Medium (Medium)"}))

    def test_numeric_high_cannot_be_reclassified_low_or_medium(self):
        for description in ("Low (Medium)", "Medium (Medium)", "Informational (Low)"):
            with self.subTest(description=description):
                with self.assertRaisesRegex(SystemExit, "risk code disagree"):
                    MODULE["normalize_risk"]({"riskcode": "3", "riskdesc": description})

    def test_high_description_cannot_disagree_with_low_numeric_risk(self):
        with self.assertRaisesRegex(SystemExit, "risk code disagree"):
            MODULE["normalize_risk"]({"riskcode": "1", "riskdesc": "High (Medium)"})

    def test_missing_description_retains_numeric_risk(self):
        self.assertEqual("High", MODULE["normalize_risk"]({"riskcode": "3"}))

    def test_unrecognized_both_fields_remain_unverified(self):
        self.assertIsNone(MODULE["normalize_risk"](
            {"riskcode": "unexpected", "riskdesc": "Unknown"}))


if __name__ == "__main__":
    unittest.main()
