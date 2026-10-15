#!/usr/bin/env python3
"""Convert a Component Detection NuGet scan to a GitHub dependency snapshot.

Run only in the read-only detection job. The publishing job receives JSON, not code.
"""

from __future__ import annotations

import argparse
import json
import re
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import quote

DETECTOR = {
    "name": "Component Detection",
    "version": "0.0.1",
    "url": "https://github.com/advanced-security/component-detection-dependency-submission-action",
}
CORRELATOR = "nuget-github-hosted"


def package_url(raw: object) -> str | None:
    if not isinstance(raw, dict):
        return None
    if raw.get("Scheme") != "pkg" or str(raw.get("Type", "")).lower() != "nuget":
        return None
    name = raw.get("Name")
    if not isinstance(name, str) or not name.strip():
        return None
    namespace = raw.get("Namespace")
    path = quote(name, safe="-._~")
    if isinstance(namespace, str) and namespace:
        path = f"{quote(namespace, safe='-._~/')}/{path}"
    purl = f"pkg:nuget/{path}"
    version = raw.get("Version")
    if isinstance(version, str) and version:
        purl += f"@{quote(version, safe='-._~+')}"
    qualifiers = raw.get("Qualifiers")
    if isinstance(qualifiers, dict) and qualifiers:
        purl += "?" + "&".join(
            f"{quote(str(key), safe='-._~')}={quote(str(value), safe='-._~')}"
            for key, value in sorted(qualifiers.items())
        )
    return purl


def relative_manifest_path(raw: str, repo_root: Path) -> str:
    candidate = Path(raw.replace("\\", "/"))
    absolute = (candidate if candidate.is_absolute() else repo_root / candidate).resolve()
    try:
        normalized = absolute.relative_to(repo_root.resolve()).as_posix()
    except ValueError as error:
        raise ValueError(f"Dependency manifest is outside the checkout: {raw!r}") from error
    if normalized in ("", "."):
        raise ValueError(f"Invalid dependency manifest path: {raw!r}")
    return normalized


def build_snapshot(document: dict, repo_root: Path, sha: str, ref: str, run_id: str) -> dict:
    if not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise ValueError("Invalid commit SHA")
    if not (ref.startswith("refs/heads/") or ref.startswith("refs/tags/")):
        raise ValueError("Invalid dispatch ref")
    if not run_id.isdecimal():
        raise ValueError("Invalid run ID")
    components = document.get("componentsFound")
    graphs = document.get("dependencyGraphs")
    if not isinstance(components, list) or not isinstance(graphs, dict):
        raise ValueError("Component Detection result is missing componentsFound or dependencyGraphs")

    packages: dict[str, tuple[str, bool]] = {}
    for item in components:
        if not isinstance(item, dict) or not isinstance(item.get("component"), dict):
            continue
        component = item["component"]
        component_id = component.get("id")
        purl = package_url(component.get("packageUrl"))
        if isinstance(component_id, str) and purl:
            packages[component_id] = (purl, item.get("isDevelopmentDependency") is True)

    manifests: dict[str, dict] = {}
    for location, graph_data in graphs.items():
        if not isinstance(location, str) or not isinstance(graph_data, dict):
            raise ValueError("Invalid dependency graph entry")
        graph = graph_data.get("graph")
        if not isinstance(graph, dict):
            raise ValueError(f"Dependency graph is missing entries: {location!r}")
        source_location = relative_manifest_path(location, repo_root)
        direct_ids = set(graph_data.get("explicitlyReferencedComponentIds") or [])
        runtime_ids = set(graph_data.get("dependencies") or [])
        development_ids = set(graph_data.get("developmentDependencies") or [])
        resolved: dict[str, dict] = {}
        present = {component_id: packages[component_id] for component_id in graph if component_id in packages}
        for component_id, (purl, is_dev) in present.items():
            children = graph[component_id] or []
            if not isinstance(children, list):
                raise ValueError(f"Invalid dependency edges: {component_id!r}")
            dependencies = sorted({
                present[child][0] for child in children
                if isinstance(child, str) and child in present and present[child][0] != purl
            })
            scope = (
                "runtime" if component_id in runtime_ids else
                "development" if component_id in development_ids or is_dev else "runtime"
            )
            resolved[purl] = {
                "package_url": purl,
                "relationship": "direct" if component_id in direct_ids else "indirect",
                "scope": scope,
                "dependencies": dependencies,
            }
        manifests[source_location] = {
            "name": source_location,
            "file": {"source_location": source_location},
            "resolved": resolved,
        }

    if not any(manifest["resolved"] for manifest in manifests.values()):
        raise ValueError("Component Detection did not resolve any NuGet dependencies")
    return {
        "version": 0,
        "sha": sha,
        "ref": ref,
        "job": {"correlator": CORRELATOR, "id": run_id},
        "detector": DETECTOR,
        "scanned": datetime.now(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z"),
        "manifests": manifests,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scan", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--repo-root", type=Path, required=True)
    parser.add_argument("--sha", required=True)
    parser.add_argument("--ref", required=True)
    parser.add_argument("--run-id", required=True)
    args = parser.parse_args()
    result = build_snapshot(
        json.loads(args.scan.read_text(encoding="utf-8")),
        args.repo_root,
        args.sha,
        args.ref,
        args.run_id,
    )
    args.output.write_text(json.dumps(result, sort_keys=True, separators=(",", ":")) + "\n", encoding="utf-8")
    print(f"Prepared {len(result['manifests'])} NuGet manifests for separate submission")


if __name__ == "__main__":
    main()
