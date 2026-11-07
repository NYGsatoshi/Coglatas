#!/usr/bin/env python3
"""Reject implicit Ubuntu runner image upgrades in active GitHub workflows."""

from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_DIR = ROOT / ".github" / "workflows"
PORTABILITY_WORKFLOW = "os-portability.yml"


def violations(directory: Path = WORKFLOW_DIR) -> list[str]:
    if not directory.is_dir():
        return [f"{directory}: workflow directory is missing"]
    errors: list[str] = []
    workflows = sorted(
        path for path in directory.iterdir()
        if path.is_file() and path.suffix in {".yml", ".yaml"}
    )
    if not workflows:
        return [f"{directory}: no workflow files found"]
    for workflow in workflows:
        if workflow.name == PORTABILITY_WORKFLOW:
            continue
        try:
            lines = workflow.read_text(encoding="utf-8").splitlines()
        except (OSError, UnicodeError) as exc:
            errors.append(f"{workflow}: cannot read workflow: {exc}")
            continue
        for number, line in enumerate(lines, start=1):
            if "ubuntu-latest" in line:
                errors.append(
                    f"{workflow.name}:{number}: implicit ubuntu-latest is forbidden; "
                    "pin ubuntu-24.04 or use a separately reviewed OS qualification workflow"
                )
    return errors


if __name__ == "__main__":
    failures = violations()
    for failure in failures:
        print(failure, file=sys.stderr)
    if failures:
        raise SystemExit(1)
    print("PASS: active non-portability workflows have no implicit ubuntu-latest")
