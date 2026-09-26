#!/usr/bin/env python3
"""Emit explicit SEC-10 inventory warnings for untracked SBOM component identities.

This audit is intentionally evidence-oriented rather than a blanket blocker: Syft can
legitimately emit non-package components without package coordinates. Malformed
CycloneDX fails closed, while package-like components with incomplete identity are
recorded so they cannot be silently ignored by vulnerability policy review.
"""

from __future__ import annotations

import argparse
import json
import sys
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any

SCHEMA = "coglatas-sbom-component-inventory-audit-v1"
PACKAGE_LIKE_TYPES = {"library", "framework", "operating-system"}


class InventoryAuditError(ValueError):
    pass


def fail(message: str) -> "NoReturn":
    raise InventoryAuditError(message)


def text(value: Any) -> str:
    return value.strip() if isinstance(value, str) else ""


def read_sbom(path: Path) -> dict[str, Any]:
    if not path.is_file() or path.stat().st_size == 0:
        fail(f"CycloneDX SBOM is missing or empty: {path}")
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        fail(f"CycloneDX SBOM is not valid UTF-8 JSON: {path}: {exc}")
    if not isinstance(value, dict):
        fail("CycloneDX SBOM root must be a JSON object")
    if value.get("bomFormat") != "CycloneDX":
        fail("SBOM has an invalid or missing CycloneDX bomFormat")
    components = value.get("components")
    if not isinstance(components, list) or not components:
        fail("CycloneDX SBOM has no components")
    return value


def audit(document: dict[str, Any]) -> dict[str, Any]:
    components = document["components"]
    normalized: list[dict[str, str]] = []
    warnings: list[dict[str, Any]] = []

    for index, component in enumerate(components):
        if not isinstance(component, dict):
            fail(f"CycloneDX component #{index} is not an object")
        name = text(component.get("name"))
        if not name:
            fail(f"CycloneDX component #{index} has no name")
        component_type = text(component.get("type")).casefold()
        version = text(component.get("version"))
        purl = text(component.get("purl"))
        bom_ref = text(component.get("bom-ref"))
        if purl and not purl.startswith("pkg:"):
            warnings.append(
                {
                    "kind": "unknown-package-origin",
                    "name": name,
                    "version": version,
                    "type": component_type,
                    "purl": purl,
                    "bomRef": bom_ref,
                    "reason": "component purl is present but is not a package URL",
                }
            )
        package_like = purl.startswith("pkg:") or component_type in PACKAGE_LIKE_TYPES
        if package_like and not version:
            warnings.append(
                {
                    "kind": "missing-version",
                    "name": name,
                    "version": "",
                    "type": component_type,
                    "purl": purl,
                    "bomRef": bom_ref,
                    "reason": "package-like component has no installed version",
                }
            )
        if component_type in PACKAGE_LIKE_TYPES and not purl:
            warnings.append(
                {
                    "kind": "unknown-package-origin",
                    "name": name,
                    "version": version,
                    "type": component_type,
                    "purl": "",
                    "bomRef": bom_ref,
                    "reason": "package-like component has no package URL origin",
                }
            )
        normalized.append(
            {
                "name": name,
                "version": version,
                "type": component_type,
                "purl": purl,
                "bomRef": bom_ref,
            }
        )

    exact_counts = Counter(
        (item["type"], item["name"].casefold(), item["version"], item["purl"])
        for item in normalized
    )
    for identity, count in sorted(exact_counts.items()):
        if count > 1:
            sample = next(
                item
                for item in normalized
                if (
                    item["type"],
                    item["name"].casefold(),
                    item["version"],
                    item["purl"],
                )
                == identity
            )
            warnings.append(
                {
                    "kind": "duplicate-component-identity",
                    "name": sample["name"],
                    "version": sample["version"],
                    "type": sample["type"],
                    "purl": sample["purl"],
                    "occurrences": count,
                    "reason": "exact component identity occurs more than once",
                }
            )

    origins: dict[tuple[str, str], set[str]] = defaultdict(set)
    original_names: dict[tuple[str, str], str] = {}
    for item in normalized:
        key = (item["name"].casefold(), item["version"])
        original_names.setdefault(key, item["name"])
        origin = item["purl"] or (f"type:{item['type']}" if item["type"] else "<unknown>")
        origins[key].add(origin)
    for key, values in sorted(origins.items()):
        if len(values) > 1:
            warnings.append(
                {
                    "kind": "ambiguous-component-identity",
                    "name": original_names[key],
                    "version": key[1],
                    "origins": sorted(values),
                    "reason": "same name/version resolves to multiple package origins",
                }
            )

    warnings.sort(
        key=lambda item: (
            item["kind"],
            str(item.get("name", "")).casefold(),
            str(item.get("version", "")),
            str(item.get("purl", "")),
        )
    )
    counts = Counter(item["kind"] for item in warnings)
    return {
        "schema": SCHEMA,
        "componentCount": len(normalized),
        "warningCount": len(warnings),
        "warningCounts": dict(sorted(counts.items())),
        "warnings": warnings,
    }


def command(args: argparse.Namespace) -> None:
    report = audit(read_sbom(Path(args.sbom).resolve()))
    output = Path(args.out).resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(
        json.dumps(report, indent=2, sort_keys=True, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    print(
        "SEC-10 SBOM component inventory: "
        f"components={report['componentCount']} warnings={report['warningCount']}"
    )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser()
    parser.add_argument("--sbom", required=True)
    parser.add_argument("--out", required=True)
    return parser


def main() -> int:
    try:
        command(build_parser().parse_args())
    except InventoryAuditError as exc:
        print(f"SEC-10 SBOM component inventory audit failed: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
