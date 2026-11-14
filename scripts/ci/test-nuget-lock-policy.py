#!/usr/bin/env python3
"""Negative mutation controls for the NuGet dependency lock policy."""
from __future__ import annotations

import importlib.util
import json
import re
import shutil
import tempfile
import unittest
import xml.etree.ElementTree as ET
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

    def manifest_reference(self, project, name):
        """Locate the current version instead of pinning a patch number in negative tests."""
        xml = ET.parse(self.root / project).getroot()
        matches = [
            ref for ref in xml.findall(".//PackageReference")
            if ref.get("Include") == name
        ]
        self.assertEqual(len(matches), 1, (project, name))
        version = matches[0].get("Version") or matches[0].findtext("Version")
        self.assertTrue(version, (project, name))
        return f'<PackageReference Include="{name}" Version="{version}" />'

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
        project = "src/Coglatas.Application/Coglatas.Application.csproj"
        reference = self.manifest_reference(project, "Microsoft.Extensions.Logging.Abstractions")
        self.edit(project, reference, reference.replace(' Version="', ' Version="0.', 1))
        self.assert_violation("requested range differs from project")

    def test_tampered_hash_fails(self):
        file = self.root / "src/Coglatas.Application/packages.lock.json"
        data = json.loads(file.read_text())
        entry = data["dependencies"]["net11.0"]["Microsoft.Extensions.Logging.Abstractions"]
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


    def test_malformed_project_xml_fails_closed(self):
        self.edit("src/Coglatas.Application/Coglatas.Application.csproj",
                  "</Project>", "</Broken>")
        self.assert_violation("invalid project XML")

    def test_malformed_json_lockfile_fails_closed(self):
        file = self.root / "src/Coglatas.Web/packages.lock.json"
        file.write_text("{", encoding="utf-8")
        self.assert_violation("missing or invalid lockfile")

    def test_non_object_json_lockfile_fails_closed(self):
        file = self.root / "src/Coglatas.Web/packages.lock.json"
        file.write_text("[]", encoding="utf-8")
        self.assert_violation("expected a JSON object")

    def test_unlocked_package_restore_is_rejected(self):
        self.edit("src/Coglatas.Infrastructure/Coglatas.Infrastructure.csproj",
                  "<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>",
                  "<RestorePackagesWithLockFile>false</RestorePackagesWithLockFile>")
        self.assert_violation("RestorePackagesWithLockFile must be true")

    def test_invalid_lockfile_dependency_container_is_rejected(self):
        file = self.root / "src/Coglatas.Application/packages.lock.json"
        data = json.loads(file.read_text(encoding="utf-8"))
        data["dependencies"] = []
        file.write_text(json.dumps(data), encoding="utf-8")
        self.assert_violation("lockfile dependencies must be an object")

    def test_invalid_lockfile_version_is_rejected(self):
        file = self.root / "src/Coglatas.Application/packages.lock.json"
        data = json.loads(file.read_text(encoding="utf-8"))
        data["version"] = "1"
        file.write_text(json.dumps(data), encoding="utf-8")
        self.assert_violation("lockfile version must be numeric")

    def test_missing_project_framework_is_rejected(self):
        self.edit("src/Coglatas.Application/Coglatas.Application.csproj",
                  "<TargetFramework>net11.0</TargetFramework>",
                  "<TargetFramework></TargetFramework>")
        self.assert_violation("TargetFramework(s) missing")

    def test_missing_lockfile_framework_is_rejected(self):
        file = self.root / "src/Coglatas.Application/packages.lock.json"
        data = json.loads(file.read_text(encoding="utf-8"))
        data["dependencies"]["net9.0"] = data["dependencies"].pop("net11.0")
        file.write_text(json.dumps(data), encoding="utf-8")
        self.assert_violation("missing target framework net11.0")

    def test_multitarget_frameworks_are_checked_individually(self):
        project = "src/Coglatas.Application/Coglatas.Application.csproj"
        self.edit(project,
                  "<TargetFramework>net11.0</TargetFramework>",
                  "<TargetFrameworks>net11.0;net9.0</TargetFrameworks>")
        self.assert_violation("missing target framework net9.0")

    def test_malformed_dependency_entry_is_rejected(self):
        file = self.root / "src/Coglatas.Application/packages.lock.json"
        data = json.loads(file.read_text(encoding="utf-8"))
        data["dependencies"]["net11.0"]["Microsoft.Extensions.Logging.Abstractions"] = None
        file.write_text(json.dumps(data), encoding="utf-8")
        self.assert_violation("malformed dependency")

    def test_missing_direct_reference_is_rejected(self):
        file = self.root / "src/Coglatas.Application/packages.lock.json"
        data = json.loads(file.read_text(encoding="utf-8"))
        data["dependencies"]["net11.0"].pop("Microsoft.Extensions.Logging.Abstractions")
        file.write_text(json.dumps(data), encoding="utf-8")
        self.assert_violation("direct package microsoft.extensions.logging.abstractions missing")

    def test_extra_direct_reference_is_rejected(self):
        file = self.root / "src/Coglatas.Application/packages.lock.json"
        data = json.loads(file.read_text(encoding="utf-8"))
        example = dict(data["dependencies"]["net11.0"]["Microsoft.Extensions.Logging.Abstractions"])
        data["dependencies"]["net11.0"]["Unexpected.Package"] = example
        file.write_text(json.dumps(data), encoding="utf-8")
        self.assert_violation("unexpected direct package(s)")

    def test_duplicate_manifest_package_is_rejected(self):
        project = "src/Coglatas.Application/Coglatas.Application.csproj"
        reference = self.manifest_reference(project, "Microsoft.Extensions.Logging.Abstractions")
        self.edit(project, reference, reference + reference)
        self.assert_violation("duplicate PackageReference")

    def test_missing_manifest_package_version_is_rejected(self):
        project = "src/Coglatas.Application/Coglatas.Application.csproj"
        reference = self.manifest_reference(project, "Microsoft.Extensions.Logging.Abstractions")
        unversioned = reference.split(' Version="', 1)[0] + " />"
        self.edit(project, reference, unversioned)
        self.assert_violation("PackageReference must have a concrete Include and Version")

    def test_invalid_transitive_hash_is_rejected(self):
        file = self.root / "src/Coglatas.Infrastructure/packages.lock.json"
        data = json.loads(file.read_text(encoding="utf-8"))
        graph = data["dependencies"]["net11.0"]
        transitive = next(entry for entry in graph.values() if entry.get("type") == "Transitive")
        transitive["contentHash"] = None
        file.write_text(json.dumps(data), encoding="utf-8")
        self.assert_violation("invalid SHA512 contentHash")

    def test_docker_restore_force_evaluate_is_rejected(self):
        self.edit("Dockerfile",
                  "dotnet restore src/Coglatas.Web/Coglatas.Web.csproj --locked-mode",
                  "dotnet restore src/Coglatas.Web/Coglatas.Web.csproj --locked-mode --force-evaluate")
        self.assert_violation("never --force-evaluate")

    def test_docker_extra_restore_is_rejected(self):
        docker = self.root / "Dockerfile"
        docker.write_text(
            docker.read_text(encoding="utf-8")
            + "\nRUN dotnet restore src/Coglatas.Web/Coglatas.Web.csproj --locked-mode\n",
            encoding="utf-8",
        )
        self.assert_violation("expected exactly two Web restore commands")

    def test_missing_dockerfile_is_rejected(self):
        (self.root / "Dockerfile").unlink()
        self.assert_violation("Dockerfile:")

    def test_missing_workflow_is_rejected(self):
        (self.root / ".github/workflows/ci.yml").unlink()
        self.assert_violation(".github/workflows/ci.yml:")

    def test_workflow_without_solution_restore_is_rejected(self):
        file = self.root / ".github/workflows/ci.yml"
        file.write_text("name: test\n", encoding="utf-8")
        self.assert_violation("no authoritative solution restore")

    def test_workflow_force_evaluate_is_rejected(self):
        self.edit(".github/workflows/main-build-artifacts.yml",
                  "dotnet restore Coglatas.slnx --locked-mode",
                  "dotnet restore Coglatas.slnx --locked-mode --force-evaluate")
        self.assert_violation("solution restore must use --locked-mode")

    def test_unrelated_projects_are_not_counted_as_package_owners(self):
        project = self.root / "src/NoPackages/NoPackages.csproj"
        project.parent.mkdir(parents=True)
        project.write_text('<Project Sdk="Microsoft.NET.Sdk"/>', encoding="utf-8")
        self.assertEqual([], policy.validate(self.root))

    def test_no_package_owners_cannot_pass(self):
        for project in self.root.rglob("*.csproj"):
            project.unlink()
        self.assert_violation("no PackageReference project files inspected")



if __name__ == "__main__":
    unittest.main()
