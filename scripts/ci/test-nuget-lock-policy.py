#!/usr/bin/env python3
"""Negative mutation controls for the NuGet dependency lock policy."""
from __future__ import annotations

import importlib.util
import json
import re
import shutil
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("check-nuget-lock-policy.py")
REPO = SCRIPT.parents[2]
spec = importlib.util.spec_from_file_location("nuget_lock_policy", SCRIPT)
assert spec and spec.loader
policy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(policy)


class NugetLockPolicyTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="nuget-lock-policy-")
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        for project in (
            *policy.PRODUCTION,
            "tests/Coglatas.Architecture.Tests",
            "tests/Coglatas.Tests",
        ):
            folder = Path(project)
            csproj = folder / f"{folder.name}.csproj"
            for item in (csproj, folder / "packages.lock.json"):
                target = self.root / item
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(REPO / item, target)
        for file in ("Dockerfile", *policy.POLICY_PATHS):
            target = self.root / file
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(REPO / file, target)

    def edit(self, rel, old, new):
        path = self.root / rel
        original = path.read_text(encoding="utf-8")
        self.assertIn(old, original, rel)
        path.write_text(original.replace(old, new, 1), encoding="utf-8")

    def assert_violation(self, snippet):
        violations = policy.validate(self.root)
        self.assertTrue(
            any(snippet in error for error in violations),
            f"expected {snippet!r} in {violations!r}",
        )

    def test_current_policy_passes(self):
        self.assertEqual([], policy.validate(self.root))

    def test_project_without_external_package_reference_needs_no_lock(self):
        file = self.root / "src/Coglatas.Domain/Coglatas.Domain.csproj"
        file.parent.mkdir(parents=True)
        file.write_text('<Project Sdk="Microsoft.NET.Sdk"></Project>\n')
        self.assertEqual([], policy.validate(self.root))

    def test_missing_lockfile_is_rejected(self):
        (self.root / "tests/Coglatas.Tests/packages.lock.json").unlink()
        self.assert_violation("missing or invalid lockfile")

    def test_new_project_package_reference_requires_lockfile(self):
        file = self.root / "tools/SomeTool/SomeTool.csproj"
        file.parent.mkdir(parents=True)
        file.write_text('<Project Sdk="Microsoft.NET.Sdk"><ItemGroup>'
                        '<PackageReference Include="Example" Version="1.2.3"/>'
                        '</ItemGroup></Project>\n')
        self.assert_violation("missing or invalid lockfile")

    def test_manifest_version_drift_fails(self):
        self.edit("src/Coglatas.Application/Coglatas.Application.csproj",
                  'Version="10.0.14"', 'Version="10.0.13"')
        self.assert_violation("requested range differs from project")

    def test_tampered_hash_fails(self):
        file = self.root / "src/Coglatas.Application/packages.lock.json"
        data = json.loads(file.read_text())
        entry = data["dependencies"]["net10.0"]["Microsoft.Extensions.Logging.Abstractions"]
        entry["contentHash"] = "invalid"
        file.write_text(json.dumps(data))
        self.assert_violation("invalid SHA512 contentHash")

    def test_missing_ci_guard_fails(self):
        self.edit("src/Coglatas.Web/Coglatas.Web.csproj",
                  "'$(CI)' == 'true'", "'$(CI)' == 'false'")
        self.assert_violation("RestoreLockedMode must be true when CI=true")

    def test_docker_missing_lock_copy_fails(self):
        self.edit("Dockerfile",
                  "COPY src/Coglatas.Web/packages.lock.json src/Coglatas.Web/",
                  "# removed Web lockfile COPY")
        self.assert_violation("missing pre-restore COPY")

    def test_docker_missing_ui_core_copy_fails(self):
        self.edit("Dockerfile",
                  "COPY src/Coglatas.UI.Core/Coglatas.UI.Core.csproj src/Coglatas.UI.Core/",
                  "# removed UI.Core COPY")
        self.assert_violation("UI.Core project file must be copied")

    def test_docker_unlocked_restore_fails(self):
        self.edit("Dockerfile",
                  "dotnet restore src/Coglatas.Web/Coglatas.Web.csproj --locked-mode",
                  "dotnet restore src/Coglatas.Web/Coglatas.Web.csproj")
        self.assert_violation("every Web restore must use --locked-mode")

    def test_ci_unlocked_solution_restore_fails(self):
        self.edit(".github/workflows/ci.yml",
                  "dotnet restore Coglatas.slnx --locked-mode",
                  "dotnet restore Coglatas.slnx")
        self.assert_violation("solution restore must use --locked-mode")

    def test_main_unlocked_solution_restore_fails(self):
        self.edit(".github/workflows/main-build-artifacts.yml",
                  "dotnet restore Coglatas.slnx --locked-mode",
                  "dotnet restore Coglatas.slnx")
        self.assert_violation("solution restore must use --locked-mode")


if __name__ == "__main__":
    unittest.main()
