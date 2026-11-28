"""Reject entry-suite identity loss even when passing case counts are unchanged."""
import copy
from fnmatch import fnmatchcase
import json
import re
from pathlib import Path
import unittest

from projectide_entry_isolated import TEST_PATHS, require_retained_entry_identities

BASELINE = Path(__file__).resolve().parents[1] / "Coglatas.ProjectIde.Entry.Tests/retained-test-identities.v1.json"

RUNTIME_ROUTES = (
    "src/Coglatas.Application/ProjectIde/Runtime/**",
    "src/Coglatas.Infrastructure/Persistence/ProjectTestJob*.cs",
    "tests/Coglatas.Tests/PostgreSql/ProjectTestRuntimePostgreSqlTests.cs",
)


def require_runtime_routes(workflow):
    lines = workflow.replace("\r\n", "\n").splitlines()
    marker = ["on:", "  pull_request:", "    paths:"]
    try:
        start = lines.index(marker[0])
        if lines[start:start + len(marker)] != marker:
            raise ValueError("Missing pull request paths filter")
        patterns = []
        for line in lines[start + len(marker):]:
            if line.startswith("      - "):
                patterns.append(line.removeprefix("      - ").strip().strip("\"'"))
            elif line.strip() and not line.lstrip().startswith("#"):
                break
    except ValueError as error:
        raise ValueError("Missing pull request paths filter") from error
    if any(pattern.startswith("!") for pattern in patterns):
        raise ValueError("Entry routes must retain inclusion-only patterns")
    if len(patterns) != len(set(patterns)):
        raise ValueError("Duplicate Entry route")
    for required in RUNTIME_ROUTES:
        if required not in patterns:
            raise ValueError("Missing Runtime route: " + required)
    return patterns


class EntryRuntimeRoutingTests(unittest.TestCase):
    def setUp(self):
        root = Path(__file__).resolve().parents[2]
        self.workflow = (root / ".github/workflows/projectide-entry-isolated.yml").read_text(encoding="utf-8")

    def test_runtime_service_store_provisioner_and_postgresql_only_changes_select_the_paired_build(self):
        patterns = require_runtime_routes(self.workflow)
        for path in (
            "src/Coglatas.Application/ProjectIde/Runtime/ProjectTestRuntime.cs",
            "src/Coglatas.Application/ProjectIde/Runtime/ProjectIdeOperationAuthorization.cs",
            "src/Coglatas.Infrastructure/Persistence/ProjectTestJobStore.cs",
            "src/Coglatas.Infrastructure/Persistence/ProjectTestJobDevelopmentProvisioning.cs",
            "tests/Coglatas.Tests/PostgreSql/ProjectTestRuntimePostgreSqlTests.cs",
        ):
            with self.subTest(path=path):
                self.assertTrue(any(fnmatchcase(path, pattern) for pattern in patterns))
        self.assertFalse(any(fnmatchcase("docs/unrelated.md", pattern) for pattern in patterns))

    def test_each_missing_runtime_trigger_is_rejected(self):
        for path in RUNTIME_ROUTES:
            with self.subTest(path=path):
                changed = self.workflow.replace("      - " + path + "\n", "")
                with self.assertRaisesRegex(ValueError, "Missing Runtime route"):
                    require_runtime_routes(changed)

    def test_matching_exclusions_cannot_disable_runtime_routes(self):
        excluded = self.workflow.replace("    paths:\n", "    paths:\n      - '!src/**'\n", 1)
        with self.assertRaisesRegex(ValueError, "inclusion-only"):
            require_runtime_routes(excluded)

    def test_missing_event_filter_and_duplicate_selectors_are_rejected(self):
        for changed in (
            self.workflow.replace("  pull_request:\n", "  push:\n", 1),
            self.workflow.replace("    paths:\n", "    paths-ignore:\n", 1),
        ):
            with self.subTest(changed=changed[:60]), self.assertRaisesRegex(ValueError, "Missing pull request"):
                require_runtime_routes(changed)
        duplicated = self.workflow.replace("    paths:\n", "    paths:\n      - global.json\n", 1)
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            require_runtime_routes(duplicated)

    def test_crlf_checkout_keeps_the_same_runtime_routes(self):
        self.assertEqual(require_runtime_routes(self.workflow),
                         require_runtime_routes(self.workflow.replace("\n", "\r\n")))


class EntryIdentityRetentionTests(unittest.TestCase):
    def setUp(self):
        self.baseline = json.loads(BASELINE.read_text(encoding="utf-8"))
        self.cases = self.baseline["cases"]

    def test_exact_retained_cases_and_additional_cases_are_accepted(self):
        require_retained_entry_identities(self.cases, self.baseline)
        require_retained_entry_identities(self.cases + ["Coglatas.Tests.ProjectIde.Entry.Additional.Case"], self.baseline)

    def test_every_owned_path_triggers_the_composition_workflow(self):
        root = Path(__file__).resolve().parents[2]
        workflow = (root / ".github/workflows/projectide-entry-isolated.yml").read_text(encoding="utf-8")
        block = workflow.split("    paths:\n", 1)[1].split("\npermissions:", 1)[0]
        triggers = {line.strip()[2:] for line in block.splitlines() if line.strip().startswith("- ")}
        for path in TEST_PATHS:
            with self.subTest(path=path):
                self.assertIn(path + "**" if path.endswith("/") else path, triggers)

    def test_future_internal_projectide_prs_are_not_limited_to_promoted_branch(self):
        root = Path(__file__).resolve().parents[2]
        workflow = (root / ".github/workflows/projectide-entry-isolated.yml").read_text(encoding="utf-8")
        self.assertIn("on:\n  pull_request:", workflow)
        blocks = re.findall(r"(?m)^    if: >-\n((?:      [^\n]*\n)+)", workflow)
        self.assertEqual(2, len(blocks), "Both Windows and Ubuntu jobs must be gated consistently")
        for condition in blocks:
            with self.subTest(condition=condition):
                self.assertIn("github.event.pull_request.head.repo.full_name == github.repository", condition)
                self.assertIn("github.base_ref == 'projectide/integration-20261031'", condition)
                self.assertNotIn("github.head_ref", condition)
        trigger_block = workflow.split("    paths:\n", 1)[1].split("\npermissions:", 1)[0]
        triggers = {line.strip()[2:] for line in trigger_block.splitlines()
                    if line.strip().startswith("- ")}
        for owned_source in (
            "tests/Coglatas.ProjectIde.Entry.Tests/**",
            "src/Coglatas.Domain/ProjectIde/**",
            "tools/Coglatas.SecurityArchitecture/SourceEntryValidator.cs",
            "tools/Coglatas.SecurityArchitecture/SpecTraceabilityValidator.cs",
            "tests/Coglatas.Tests/ProjectIde/**",
            "global.json",
            "Coglatas.slnx",
            "tests/ci/projectide_core_isolated.py",
            "tests/ci/projectide_slice2_isolated.py",
            "tests/ci/projectide_integration_inventory.json",
            "tests/ci/test_projectide_core_isolated.py",
            "tests/ci/test_projectide_integration_isolated.py",
            "tests/ci/test_projectide_slice2_isolated.py",
            "tests/Coglatas.Tests/Coglatas.Tests.csproj",
            "tests/Coglatas.Tests/packages.lock.json",
            "tests/Coglatas.Architecture.Tests/Coglatas.Architecture.Tests.csproj",
            "tests/Coglatas.Architecture.Tests/packages.lock.json",
        ):
            with self.subTest(owned_source=owned_source):
                self.assertIn(owned_source, triggers)

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
