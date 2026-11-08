#!/usr/bin/env python3
"""Check committed NuGet locks and the build paths that must consume them.

This is a source-level guard, not a substitute for NuGet's --locked-mode restore.
"""
from __future__ import annotations

import base64
import binascii
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


def validate(root: Path) -> list[str]:
    errors: list[str] = []
    projects: list[Path] = []
    for group in ("src", "tests", "tools"):
        top = root / group
        if top.is_dir():
            projects.extend(
                p for p in top.rglob("*.csproj")
                if not {"bin", "obj", ".tmp"}.intersection(p.relative_to(top).parts)
            )

    with_references = 0
    for project in sorted(projects):
        rel = project.relative_to(root).as_posix()
        try:
            xml = ET.parse(project).getroot()
        except (OSError, ET.ParseError) as exc:
            errors.append(f"{rel}: invalid project XML: {exc}")
            continue
        refs = xml.findall(".//PackageReference")
        if not refs:
            continue

        with_references += 1
        lock_path = project.parent / "packages.lock.json"
        lock_rel = lock_path.relative_to(root).as_posix()
        try:
            lock = json.loads(lock_path.read_text(encoding="utf-8"))
        except (OSError, ValueError) as exc:
            errors.append(f"{lock_rel}: missing or invalid lockfile: {exc}")
            continue

        enabled = [
            el for el in xml.findall(".//RestorePackagesWithLockFile")
            if (el.text or "").strip().lower() == "true"
        ]
        if not enabled:
            errors.append(f"{rel}: RestorePackagesWithLockFile must be true")

        locked = [
            el for el in xml.findall(".//RestoreLockedMode")
            if (el.text or "").strip().lower() == "true" and
            re.sub(r"\s+", "", el.get("Condition", "")) == "'$(CI)'=='true'"
        ]
        if not locked:
            errors.append(f"{rel}: RestoreLockedMode must be true when CI=true")

        targets = xml.findall(".//TargetFramework")
        frameworks = [(el.text or "").strip() for el in targets]
        frameworks.extend(
            fw.strip()
            for el in xml.findall(".//TargetFrameworks")
            for fw in (el.text or "").split(";")
        )
        frameworks = [fw for fw in frameworks if fw]
        lock_targets = lock.get("dependencies", {})
        if not isinstance(lock_targets, dict) or not lock_targets:
            errors.append(f"{lock_rel}: lockfile dependencies must be an object")
            continue
        if not isinstance(lock.get("version"), int):
            errors.append(f"{lock_rel}: lockfile version must be numeric")

        declared: dict[str, str] = {}
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

        if not frameworks:
            errors.append(f"{rel}: TargetFramework(s) missing")
        for framework in frameworks:
            graph = lock_targets.get(framework)
            if not isinstance(graph, dict):
                errors.append(f"{lock_rel}: missing target framework {framework}")
                continue
            direct: dict[str, dict] = {}
            for name, entry in graph.items():
                if not isinstance(entry, dict):
                    errors.append(f"{lock_rel}: malformed dependency {name}")
                    continue
                if entry.get("type") == "Direct":
                    direct[name.lower()] = entry
                if entry.get("type") != "Project":
                    content_hash = entry.get("contentHash")
                    try:
                        digest = base64.b64decode(content_hash, validate=True)
                    except (ValueError, TypeError, binascii.Error):
                        digest = b""
                    if len(digest) != 64:
                        errors.append(f"{lock_rel}: invalid SHA512 contentHash for {name}")
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

    if with_references == 0:
        errors.append("no PackageReference project files inspected")

    docker_file = root / "Dockerfile"
    try:
        docker = docker_file.read_text(encoding="utf-8")
    except OSError as exc:
        errors.append(f"Dockerfile: {exc}")
        docker = ""
    restore_commands = list(re.finditer(
        r"\bdotnet\s+restore\s+src/Coglatas\.Web/Coglatas\.Web\.csproj[^\n]*", docker
    ))
    if len(restore_commands) != 2:
        errors.append("Dockerfile: expected exactly two Web restore commands")
    for command in restore_commands:
        if "--locked-mode" not in command.group() or "--force-evaluate" in command.group():
            errors.append("Dockerfile: every Web restore must use --locked-mode, never --force-evaluate")
    first_restore = restore_commands[0].start() if restore_commands else 0
    for folder in PRODUCTION:
        needle = f"COPY {folder}/packages.lock.json {folder}/"
        pos = docker.find(needle)
        if pos < 0 or pos >= first_restore:
            errors.append(f"Dockerfile: missing pre-restore COPY for {folder}/packages.lock.json")
    ui_project_copy = "COPY src/Coglatas.UI.Core/Coglatas.UI.Core.csproj src/Coglatas.UI.Core/"
    pos = docker.find(ui_project_copy)
    if pos < 0 or pos >= first_restore:
        errors.append("Dockerfile: UI.Core project file must be copied before restore")

    for workflow in POLICY_PATHS:
        try:
            source = (root / workflow).read_text(encoding="utf-8")
        except OSError as exc:
            errors.append(f"{workflow}: {exc}")
            continue
        restores = re.findall(r"\bdotnet\s+restore\s+Coglatas\.slnx[^\n]*", source)
        if not restores:
            errors.append(f"{workflow}: no authoritative solution restore")
        for command in restores:
            if "--locked-mode" not in command or "--force-evaluate" in command:
                errors.append(f"{workflow}: solution restore must use --locked-mode")
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
