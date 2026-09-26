#!/usr/bin/env python3
"""Validate Syft SBOMs and emit deterministic supply-chain evidence."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable

EVIDENCE_SCHEMA = "coglatas-sbom-evidence-v1"
NORMALIZED_SCHEMA = "coglatas-sbom-normalized-components-v1"
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
GIT_SHA_RE = re.compile(r"^[0-9a-f]{40}$")


class SbomValidationError(ValueError):
    pass


def fail(message: str) -> "NoReturn":
    raise SbomValidationError(message)


def read_json(path: Path) -> dict[str, Any]:
    if not path.is_file() or path.stat().st_size == 0:
        fail(f"SBOM is missing or empty: {path}")
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        fail(f"SBOM is not valid UTF-8 JSON: {path}: {exc}")
    if not isinstance(value, dict):
        fail(f"SBOM document root must be a JSON object: {path}")
    return value


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _as_text(value: Any) -> str:
    return value if isinstance(value, str) else ""


def _spdx_purl(package: dict[str, Any]) -> str:
    refs = package.get("externalRefs", [])
    if not isinstance(refs, list):
        return ""
    for ref in refs:
        if not isinstance(ref, dict):
            continue
        ref_type = _as_text(ref.get("referenceType")).casefold()
        locator = _as_text(ref.get("referenceLocator"))
        if ref_type == "purl" or locator.startswith("pkg:"):
            return locator
    return ""


def validate_cyclonedx(document: dict[str, Any]) -> tuple[str, list[dict[str, str]]]:
    if document.get("bomFormat") != "CycloneDX":
        fail("CycloneDX SBOM has an invalid or missing bomFormat")
    spec_version = _as_text(document.get("specVersion"))
    if not spec_version:
        fail("CycloneDX SBOM has no specVersion")
    components = document.get("components")
    if not isinstance(components, list) or not components:
        fail("CycloneDX SBOM has no components")

    normalized: list[dict[str, str]] = []
    for component in components:
        if not isinstance(component, dict):
            fail("CycloneDX components must be JSON objects")
        name = _as_text(component.get("name"))
        if not name:
            fail("CycloneDX component has no name")
        normalized.append(
            {
                "type": _as_text(component.get("type")),
                "name": name,
                "version": _as_text(component.get("version")),
                "purl": _as_text(component.get("purl")),
            }
        )
    return spec_version, normalized


def validate_spdx(document: dict[str, Any]) -> tuple[str, list[dict[str, str]]]:
    spdx_version = _as_text(document.get("spdxVersion"))
    if not spdx_version.startswith("SPDX-"):
        fail("SPDX SBOM has an invalid or missing spdxVersion")
    if document.get("SPDXID") != "SPDXRef-DOCUMENT":
        fail("SPDX SBOM has an invalid or missing document SPDXID")
    packages = document.get("packages")
    if not isinstance(packages, list) or not packages:
        fail("SPDX SBOM has no packages")

    normalized: list[dict[str, str]] = []
    for package in packages:
        if not isinstance(package, dict):
            fail("SPDX packages must be JSON objects")
        name = _as_text(package.get("name"))
        if not name:
            fail("SPDX package has no name")
        normalized.append(
            {
                "type": "",
                "name": name,
                "version": _as_text(package.get("versionInfo")),
                "purl": _spdx_purl(package),
            }
        )
    return spdx_version, normalized


def package_names(components: Iterable[dict[str, str]]) -> set[str]:
    return {item["name"].casefold() for item in components}


def require_packages(
    label: str, components: list[dict[str, str]], required: list[str]
) -> None:
    names = package_names(components)
    missing = [name for name in required if name.casefold() not in names]
    if missing:
        fail(f"{label} SBOM is missing required packages: {', '.join(missing)}")


def ensure_forbidden_values_absent(paths: list[Path], forbidden_values: list[str]) -> None:
    values = [value for value in forbidden_values if value]
    for value in values:
        if len(value) < 8:
            fail("Refusing to secret-scan for a marker shorter than 8 characters")
        needle = value.encode("utf-8")
        for path in paths:
            if needle in path.read_bytes():
                fail(f"Forbidden secret marker was found in SBOM output: {path.name}")


def normalized_projection(
    kind: str,
    cyclonedx_components: list[dict[str, str]],
    spdx_components: list[dict[str, str]],
) -> dict[str, Any]:
    def project(components: list[dict[str, str]]) -> list[dict[str, Any]]:
        identities = Counter(
            (
                item.get("type", ""),
                item.get("name", ""),
                item.get("version", ""),
                item.get("purl", ""),
            )
            for item in components
        )
        return [
            {
                "type": identity[0],
                "name": identity[1],
                "version": identity[2],
                "purl": identity[3],
                "occurrences": count,
            }
            for identity, count in sorted(
                identities.items(),
                key=lambda entry: tuple(part.casefold() for part in entry[0]),
            )
        ]

    return {
        "schema": NORMALIZED_SCHEMA,
        "sourceKind": kind,
        "cyclonedx": project(cyclonedx_components),
        "spdx": project(spdx_components),
    }


def write_json(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )


def validate_command(args: argparse.Namespace) -> None:
    cdx_path = Path(args.cyclonedx).resolve()
    spdx_path = Path(args.spdx).resolve()
    metadata_path = Path(args.metadata_out).resolve()
    normalized_path = Path(args.normalized_out).resolve()

    if not GIT_SHA_RE.fullmatch(args.repository_sha):
        fail("repository SHA must be a lowercase 40-character Git SHA")
    if args.kind == "image" and not re.fullmatch(
        r"sha256:[0-9a-f]{64}", args.identity_digest or ""
    ):
        fail("image SBOM requires an immutable sha256 image digest")
    if not re.fullmatch(
        r"\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?", args.syft_version
    ):
        fail("Syft version must be an explicit semantic version")

    cdx = read_json(cdx_path)
    spdx = read_json(spdx_path)
    cdx_schema, cdx_components = validate_cyclonedx(cdx)
    spdx_schema, spdx_components = validate_spdx(spdx)
    require_packages("CycloneDX", cdx_components, args.require_package)
    require_packages("SPDX", spdx_components, args.require_package)
    ensure_forbidden_values_absent([cdx_path, spdx_path], args.forbid_value)

    projection = normalized_projection(args.kind, cdx_components, spdx_components)
    write_json(normalized_path, projection)
    normalized_hash = sha256_file(normalized_path)

    generated_at = (
        datetime.now(timezone.utc)
        .replace(microsecond=0)
        .isoformat()
        .replace("+00:00", "Z")
    )
    metadata: dict[str, Any] = {
        "schema": EVIDENCE_SCHEMA,
        "sourceKind": args.kind,
        "repositoryCommit": args.repository_sha,
        "runIdentity": args.run_identity,
        "imageOrReleaseDigest": args.identity_digest or None,
        "syftVersion": args.syft_version,
        "generationTimestampUtc": generated_at,
        "reproducibilityPolicy": (
            "Raw SBOM timestamps, UUIDs, document namespaces, and component order are excluded "
            "from the machine reproducibility projection. Component identities are sorted and "
            "duplicate identities are represented with an occurrence count."
        ),
        "formats": {
            "cyclonedx-json": {
                "schemaVersion": cdx_schema,
                "componentCount": len(cdx_components),
                "file": cdx_path.name,
                "sha256": sha256_file(cdx_path),
            },
            "spdx-json": {
                "schemaVersion": spdx_schema,
                "componentCount": len(spdx_components),
                "file": spdx_path.name,
                "sha256": sha256_file(spdx_path),
            },
        },
        "normalizedProjection": {
            "schemaVersion": NORMALIZED_SCHEMA,
            "file": normalized_path.name,
            "sha256": normalized_hash,
            "cyclonedxIdentityCount": len(projection["cyclonedx"]),
            "spdxIdentityCount": len(projection["spdx"]),
        },
    }
    write_json(metadata_path, metadata)


def verify_hashes_command(args: argparse.Namespace) -> None:
    metadata_path = Path(args.metadata).resolve()
    metadata = read_json(metadata_path)
    if metadata.get("schema") != EVIDENCE_SCHEMA:
        fail("SBOM metadata has an unexpected evidence schema")
    base = metadata_path.parent

    checks: list[tuple[str, str]] = []
    formats = metadata.get("formats")
    if not isinstance(formats, dict):
        fail("SBOM metadata has no formats map")
    for format_name in ("cyclonedx-json", "spdx-json"):
        entry = formats.get(format_name)
        if not isinstance(entry, dict):
            fail(f"SBOM metadata is missing {format_name}")
        file_name = _as_text(entry.get("file"))
        expected_hash = _as_text(entry.get("sha256"))
        checks.append((file_name, expected_hash))

    projection = metadata.get("normalizedProjection")
    if not isinstance(projection, dict):
        fail("SBOM metadata has no normalizedProjection")
    checks.append(
        (_as_text(projection.get("file")), _as_text(projection.get("sha256")))
    )

    for file_name, expected_hash in checks:
        if not file_name or Path(file_name).name != file_name:
            fail("SBOM metadata contains an unsafe or missing artifact file name")
        if not SHA256_RE.fullmatch(expected_hash):
            fail(f"SBOM metadata contains an invalid SHA-256 for {file_name}")
        path = base / file_name
        if not path.is_file() or path.stat().st_size == 0:
            fail(f"SBOM evidence artifact is missing or empty: {file_name}")
        actual_hash = sha256_file(path)
        if actual_hash != expected_hash:
            fail(
                f"SBOM evidence hash mismatch for {file_name}: "
                f"expected {expected_hash}, got {actual_hash}"
            )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)

    validate = subparsers.add_parser("validate")
    validate.add_argument("--cyclonedx", required=True)
    validate.add_argument("--spdx", required=True)
    validate.add_argument("--kind", choices=("source", "image"), required=True)
    validate.add_argument("--repository-sha", required=True)
    validate.add_argument("--run-identity", required=True)
    validate.add_argument("--syft-version", required=True)
    validate.add_argument("--identity-digest")
    validate.add_argument("--metadata-out", required=True)
    validate.add_argument("--normalized-out", required=True)
    validate.add_argument("--require-package", action="append", default=[])
    validate.add_argument("--forbid-value", action="append", default=[])
    validate.set_defaults(func=validate_command)

    verify = subparsers.add_parser("verify-hashes")
    verify.add_argument("--metadata", required=True)
    verify.set_defaults(func=verify_hashes_command)
    return parser


def main() -> int:
    try:
        args = build_parser().parse_args()
        args.func(args)
    except SbomValidationError as exc:
        print(f"SEC-09 SBOM validation failed: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
