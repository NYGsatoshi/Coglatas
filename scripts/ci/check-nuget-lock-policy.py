#!/usr/bin/env python3
"""Check committed NuGet locks and the build paths that must consume them.

This is a source-level guard, not a substitute for NuGet's --locked-mode restore.
"""
from __future__ import annotations

import base64
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

PRODUCTION = (
    "src/Coglatas.Application",
    "src/Coglatas.Infrastructure",
    "src/Coglatas.Web",
)
POLICY_PATHS = (
    ".github/workflows/ci.yml",
    ".github/workflows/main-build-artifacts.yml",
)


def _project_files(root: Path) -> list[Path]:
    projects: list[Path] = []
    for group in ("src", "tests", "tools"):
        top = root / group
        if not top.is_dir():
            continue
        projects.extend(
            project for project in top.rglob("*.csproj")
            if not {"bin", "obj", ".tmp"}.intersection(project.relative_to(top).parts)
        )
    return sorted(projects)


def _project_flags(xml: ET.Element, rel: str) -> list[str]:
    errors: list[str] = []
    enabled = any(
        (el.text or "").strip().lower() == "true"
        for el in xml.findall(".//RestorePackagesWithLockFile")
    )
    if not enabled:
        errors.append(f"{rel}: RestorePackagesWithLockFile must be true")

    locked = any(
        (el.text or "").strip().lower() == "true"
        and re.sub(r"\s+", "", el.get("Condition", "")) == "'$(CI)'=='true'"
        for el in xml.findall(".//RestoreLockedMode")
    )
    if not locked:
        errors.append(f"{rel}: RestoreLockedMode must be true when CI=true")
    return errors


def _target_frameworks(xml: ET.Element) -> list[str]:
    frameworks = [(el.text or "").strip() for el in xml.findall(".//TargetFramework")]
    frameworks.extend(
        framework.strip()
        for el in xml.findall(".//TargetFrameworks")
        for framework in (el.text or "").split(";")
    )
    return [framework for framework in frameworks if framework]


def _declared_references(refs: list[ET.Element], rel: str) -> tuple[dict[str, str], list[str]]:
    declared: dict[str, str] = {}
    errors: list[str] = []
    for el in refs:
        name = el.get("Include", "")
        version = el.get("Version") or el.findtext("Version") or ""
        if not name or not version or "$(" in version:
            errors.append(f"{rel}: PackageReference must have a concrete Include and Version")
            continue
        lowered = name.lower()
        if lowered in declared:
            errors.append(f"{rel}: duplicate PackageReference {name}")
        declared[lowered] = version
    return declared, errors


def _valid_sha512(content_hash: object) -> bool:
    try:
        digest = base64.b64decode(content_hash, validate=True)
    except (TypeError, ValueError):
        # binascii.Error derives from ValueError; catching it again is redundant.
        return False
    return len(digest) == 64


def _graph_entries(graph: dict, lock_rel: str) -> tuple[dict[str, dict], list[str]]:
    direct: dict[str, dict] = {}
    errors: list[str] = []
    for name, entry in graph.items():
        if not isinstance(entry, dict):
            errors.append(f"{lock_rel}: malformed dependency {name}")
            continue
        if entry.get("type") == "Direct":
            direct[name.lower()] = entry
        if entry.get("type") != "Project" and not _valid_sha512(entry.get("contentHash")):
            errors.append(f"{lock_rel}: invalid SHA512 contentHash for {name}")
    return direct, errors


def _direct_reference_errors(
    declared: dict[str, str], direct: dict[str, dict], lock_rel: str
) -> list[str]:
    errors: list[str] = []
    for name, version in declared.items():
        entry = direct.get(name)
        if entry is None:
            errors.append(f"{lock_rel}: direct package {name} missing")
            continue
        if entry.get("requested") != f"[{version}, )":
            errors.append(f"{lock_rel}: direct package {name} requested range differs from project")
    extras = set(direct) - set(declared)
    if extras:
        errors.append(f"{lock_rel}: unexpected direct package(s): {', '.join(sorted(extras))}")
    return errors


def _dependency_errors(
    xml: ET.Element, refs: list[ET.Element], lock: dict, rel: str, lock_rel: str
) -> list[str]:
    errors: list[str] = []
    frameworks = _target_frameworks(xml)
    targets = lock.get("dependencies")
    if not isinstance(targets, dict) or not targets:
        return [f"{lock_rel}: lockfile dependencies must be an object"]
    if not isinstance(lock.get("version"), int):
        errors.append(f"{lock_rel}: lockfile version must be numeric")
    declared, ref_errors = _declared_references(refs, rel)
    errors.extend(ref_errors)
    if not frameworks:
        errors.append(f"{rel}: TargetFramework(s) missing")

    for framework in frameworks:
        graph = targets.get(framework)
        if not isinstance(graph, dict):
            errors.append(f"{lock_rel}: missing target framework {framework}")
            continue
        direct, graph_errors = _graph_entries(graph, lock_rel)
        errors.extend(graph_errors)
        errors.extend(_direct_reference_errors(declared, direct, lock_rel))
    return errors


def _inspect_project(project: Path, root: Path) -> tuple[bool, list[str]]:
    rel = project.relative_to(root).as_posix()
    try:
        xml = ET.parse(project).getroot()
    except (OSError, ET.ParseError) as exc:
        return False, [f"{rel}: invalid project XML: {exc}"]

    refs = xml.findall(".//PackageReference")
    if not refs:
        return False, []

    lock_path = project.parent / "packages.lock.json"
    lock_rel = lock_path.relative_to(root).as_posix()
    try:
        lock = json.loads(lock_path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        return True, [f"{lock_rel}: missing or invalid lockfile: {exc}"]
    if not isinstance(lock, dict):
        return True, [f"{lock_rel}: missing or invalid lockfile: expected a JSON object"]

    errors = _project_flags(xml, rel)
    errors.extend(_dependency_errors(xml, refs, lock, rel, lock_rel))
    return True, errors


def _docker_restore_errors(docker: str) -> tuple[int, list[str]]:
    commands = list(re.finditer(
        r"\bdotnet\s+restore\s+src/Coglatas\.Web/Coglatas\.Web\.csproj[^\n]*", docker
    ))
    errors: list[str] = []
    if len(commands) != 2:
        errors.append("Dockerfile: expected exactly two Web restore commands")
    for command in commands:
        if "--locked-mode" not in command.group() or "--force-evaluate" in command.group():
            errors.append("Dockerfile: every Web restore must use --locked-mode, never --force-evaluate")
    first_restore = commands[0].start() if commands else 0
    return first_restore, errors


def _docker_copy_errors(docker: str, first_restore: int) -> list[str]:
    errors: list[str] = []
    for folder in PRODUCTION:
        needle = f"COPY {folder}/packages.lock.json {folder}/"
        position = docker.find(needle)
        if position < 0 or position >= first_restore:
            errors.append(f"Dockerfile: missing pre-restore COPY for {folder}/packages.lock.json")

    ui_project_copy = "COPY src/Coglatas.UI.Core/Coglatas.UI.Core.csproj src/Coglatas.UI.Core/"
    position = docker.find(ui_project_copy)
    if position < 0 or position >= first_restore:
        errors.append("Dockerfile: UI.Core project file must be copied before restore")
    return errors


def _docker_errors(root: Path) -> list[str]:
    errors: list[str] = []
    try:
        docker = (root / "Dockerfile").read_text(encoding="utf-8")
    except OSError as exc:
        errors.append(f"Dockerfile: {exc}")
        docker = ""
    first_restore, restore_errors = _docker_restore_errors(docker)
    errors.extend(restore_errors)
    errors.extend(_docker_copy_errors(docker, first_restore))
    return errors


def _workflow_errors(root: Path, workflow: str) -> list[str]:
    try:
        source = (root / workflow).read_text(encoding="utf-8")
    except OSError as exc:
        return [f"{workflow}: {exc}"]

    errors: list[str] = []
    restores = re.findall(r"\bdotnet\s+restore\s+Coglatas\.slnx[^\n]*", source)
    if not restores:
        errors.append(f"{workflow}: no authoritative solution restore")
    for command in restores:
        if "--locked-mode" not in command or "--force-evaluate" in command:
            errors.append(f"{workflow}: solution restore must use --locked-mode")
    return errors


def validate(root: Path) -> list[str]:
    """Report every policy violation; do not short-circuit unrelated owners."""
    errors: list[str] = []
    with_references = 0
    for project in _project_files(root):
        has_references, project_errors = _inspect_project(project, root)
        with_references += int(has_references)
        errors.extend(project_errors)
    if with_references == 0:
        errors.append("no PackageReference project files inspected")
    errors.extend(_docker_errors(root))
    for workflow in POLICY_PATHS:
        errors.extend(_workflow_errors(root, workflow))
    return errors


def main() -> int:
    errors = validate(ROOT)
    if errors:
        for error in errors:
            print(f"NuGet lock policy: {error}", file=sys.stderr)
        return 1
    print("NuGet lock policy: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
