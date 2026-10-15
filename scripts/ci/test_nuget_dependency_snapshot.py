#!/usr/bin/env python3
"""Focused, offline contracts for read-only NuGet snapshot generation."""

from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from nuget_dependency_snapshot import build_snapshot, package_url

SHA = "a" * 40
REF = "refs/heads/main"


def component(identifier: str, name: str, *, development: bool = False) -> dict:
    return {
        "component": {
            "id": identifier,
            "packageUrl": {"Scheme": "pkg", "Type": "NuGet", "Name": name, "Version": "1.0.0"},
        },
        "isDevelopmentDependency": development,
    }


class NuGetSnapshotTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name).resolve()
        self.addCleanup(self.temp.cleanup)

    def test_direct_transitive_edges_and_sdk_exclusion(self) -> None:
        doc = {
            "componentsFound": [component("A", "First"), component("B", "Second")],
            "dependencyGraphs": {
                str(self.root / "src/App.csproj"): {
                    "graph": {"dotnet-sdk": ["A"], "A": ["B"], "B": None},
                    "explicitlyReferencedComponentIds": ["A"],
                    "dependencies": ["A", "B"],
                    "developmentDependencies": [],
                }
            },
        }
        snapshot = build_snapshot(doc, self.root, SHA, REF, "123")
        resolved = snapshot["manifests"]["src/App.csproj"]["resolved"]
        self.assertEqual(snapshot["job"], {"correlator": "nuget-github-hosted", "id": "123"})
        self.assertEqual(len(resolved), 2)
        self.assertEqual(resolved["pkg:nuget/First@1.0.0"]["relationship"], "direct")
        self.assertEqual(resolved["pkg:nuget/First@1.0.0"]["dependencies"], ["pkg:nuget/Second@1.0.0"])
        self.assertEqual(resolved["pkg:nuget/Second@1.0.0"]["relationship"], "indirect")

    def test_manifest_isolation_and_development_scope(self) -> None:
        doc = {
            "componentsFound": [component("A", "Shared"), component("B", "OnlyOne", development=True)],
            "dependencyGraphs": {
                "one.csproj": {
                    "graph": {"A": ["B"], "B": None},
                    "explicitlyReferencedComponentIds": ["A"],
                    "developmentDependencies": ["A", "B"],
                    "dependencies": [],
                },
                "two.csproj": {
                    "graph": {"A": None},
                    "explicitlyReferencedComponentIds": [],
                    "developmentDependencies": [],
                    "dependencies": ["A"],
                },
            },
        }
        manifests = build_snapshot(doc, self.root, SHA, REF, "1")["manifests"]
        self.assertEqual(manifests["one.csproj"]["resolved"]["pkg:nuget/Shared@1.0.0"]["scope"], "development")
        self.assertEqual(manifests["two.csproj"]["resolved"]["pkg:nuget/Shared@1.0.0"]["scope"], "runtime")
        self.assertEqual(manifests["two.csproj"]["resolved"]["pkg:nuget/Shared@1.0.0"]["dependencies"], [])

    def test_reject_paths_outside_checkout(self) -> None:
        doc = {
            "componentsFound": [component("A", "First")],
            "dependencyGraphs": {"../escape.csproj": {"graph": {"A": None}}},
        }
        with self.assertRaisesRegex(ValueError, "outside the checkout"):
            build_snapshot(doc, self.root, SHA, REF, "1")

    def test_fail_closed_on_empty_scan(self) -> None:
        doc = {"componentsFound": [], "dependencyGraphs": {"project.csproj": {"graph": {}}}}
        with self.assertRaisesRegex(ValueError, "did not resolve"):
            build_snapshot(doc, self.root, SHA, REF, "1")

    def test_nuget_package_only(self) -> None:
        self.assertEqual(package_url({"Scheme": "pkg", "Type": "NuGet", "Name": "A B", "Version": "2.0"}), "pkg:nuget/A%20B@2.0")
        self.assertIsNone(package_url({"Scheme": "pkg", "Type": "npm", "Name": "Other"}))


if __name__ == "__main__":
    unittest.main()
