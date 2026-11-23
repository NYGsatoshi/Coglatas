"""Regression controls for isolated ProjectIDE workflow routing."""

from fnmatch import fnmatchcase
from pathlib import Path
import unittest


class IntegratedTestRoutingTests(unittest.TestCase):
    def test_workbench_and_navigation_only_changes_trigger_inventory_verification(self):
        workflow = Path(__file__).resolve().parents[2] / ".github/workflows/projectide-core-isolated.yml"
        contents = workflow.read_text(encoding="utf-8")
        block = contents.split("  pull_request:\n    paths:\n", 1)[1].split("\n\n", 1)[0]
        patterns = [line.removeprefix("      - ") for line in block.splitlines() if line.startswith("      - ")]
        for test_path in (
            "frontend/src/app/features/projectide/projectide-navigation.spec.ts",
            "frontend/scripts/av-mig-source.node-test.mjs",
            "tests/Coglatas.Tests/Coglatas.Tests.csproj",
            "tests/ui/projectide-workbench.spec.ts",
            "tests/ui/run-angular-playwright.mjs",
        ):
            with self.subTest(test_path=test_path):
                self.assertTrue(any(fnmatchcase(test_path, pattern) for pattern in patterns))

        product_path = "frontend/src/app/features/projectide/projectide-workbench.store.ts"
        self.assertFalse(any(fnmatchcase(product_path, pattern) for pattern in patterns))
