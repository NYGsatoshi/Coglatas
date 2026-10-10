#!/usr/bin/env python3
"""Verify that a Grype JSON report is bound to the canonical CycloneDX SBOM."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, NoReturn


class SourceBindingError(ValueError):
    pass


def fail(message: str) -> NoReturn:
    raise SourceBindingError(message)


def read_json(path: Path, label: str) -> dict[str, Any]:
    if not path.is_file() or path.stat().st_size == 0:
        fail(f"{label} is missing or empty: {path}")
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        fail(f"{label} is not valid UTF-8 JSON: {path}: {exc}")
    if not isinstance(value, dict):
        fail(f"{label} root must be a JSON object")
    return value


def canonical_image_identity(sbom: dict[str, Any]) -> tuple[str, str, str]:
    metadata = sbom.get("metadata")
    component = metadata.get("component") if isinstance(metadata, dict) else None
    if not isinstance(component, dict) or component.get("type") != "container":
        fail("canonical SBOM is missing its container root component identity")

    name = component.get("name")
    version = component.get("version")
    bom_ref = component.get("bom-ref")
    if not all(isinstance(value, str) and value.strip() for value in (name, version, bom_ref)):
        fail("canonical SBOM container root identity is incomplete")
    return name.strip(), version.strip(), bom_ref.strip()


def command(args: argparse.Namespace) -> None:
    report_path = Path(args.report).resolve()
    sbom_path = Path(args.sbom).resolve()
    report = read_json(report_path, "Grype report")
    sbom = read_json(sbom_path, "canonical SBOM")
    validate_source(report, sbom, sbom_path)


def validate_source(report: dict[str, Any], sbom: dict[str, Any], sbom_path: Path) -> None:
    """Validate already captured documents without rereading mutable inputs."""

    source = report.get("source")
    if not isinstance(source, dict):
        fail("Grype report source metadata is missing")

    source_type = source.get("type")
    target = source.get("target")

    # Grype reports an explicit sbom-file source for SBOMs without embedded
    # source identity. In that shape, bind directly to the canonical path.
    if source_type == "sbom-file":
        if not isinstance(target, str) or not target.strip():
            fail("Grype SBOM source target is missing")
        target_path = Path(target).resolve()
        if target_path != sbom_path:
            fail(
                "Grype report SBOM source mismatch: "
                f"report={target_path}, expected={sbom_path}"
            )
        return

    # Grype 0.118 reconstructs the original image source identity when a
    # CycloneDX image SBOM contains Syft's metadata.component root. The trusted
    # workflow still invokes Grype with sbom:<canonical path>; in this output
    # shape, require all root identity fields to match that exact SBOM instead
    # of weakening the gate to accept arbitrary image-source reports.
    if source_type == "image":
        if not isinstance(target, dict):
            fail("Grype image source target is malformed")
        expected_name, expected_version, expected_bom_ref = canonical_image_identity(sbom)
        actual_name = target.get("userInput")
        actual_version = target.get("manifestDigest")
        actual_bom_ref = target.get("imageID")
        if (
            actual_name != expected_name
            or actual_version != expected_version
            or actual_bom_ref != expected_bom_ref
        ):
            fail(
                "Grype reconstructed image source does not match the canonical SBOM root identity"
            )
        return

    fail(
        "Grype report was not produced from a supported canonical SBOM source shape: "
        f"source.type={source_type!r}"
    )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser()
    parser.add_argument("--report", required=True)
    parser.add_argument("--sbom", required=True)
    return parser


def main() -> int:
    try:
        command(build_parser().parse_args())
    except SourceBindingError as exc:
        print(f"SEC-10 Grype source binding failed: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
