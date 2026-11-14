#!/usr/bin/env python3
"""Select Socket Firewall npm installs without weakening the full-scan fallback.

Only changes to one of the four independently locked npm projects may narrow
its install scope. Unknown paths, unavailable history and tool changes run all
four projects. No skip-only/empty matrix is ever emitted.
"""

from __future__ import annotations

import json
import os
import re
import subprocess
from pathlib import Path

SCOPES = (".", "frontend", "coglatas-frontend", "tools/frontend-inspections")
ROUTING_INPUTS = frozenset(
    {
        ".github/workflows/socket-dependency-security.yml",
        "scripts/ci/route-socket-dependency-scopes.py",
        "scripts/ci/test-route-socket-dependency-scopes.py",
    }
)
NPM_INPUTS = frozenset({"package.json", "package-lock.json", "npm-shrinkwrap.json", ".npmrc"})
SHA = re.compile(r"[0-9a-f]{40}\Z")


def scopes_for_paths(paths: list[str]) -> list[str]:
    """Narrow only when *every* changed path maps to a known npm project.

    Unknown npm manifests must not silently become uncovered by the scanner:
    fall back to the existing four-project sweep for any unclassified input.
    """
    if not paths:
        return list(SCOPES)

    chosen: set[str] = set()
    for path in paths:
        if path in ROUTING_INPUTS or path == ".npmrc":
            return list(SCOPES)

        # Git diffs may include arbitrary filenames. A non-npm change can be
        # present alongside a manifest change; it must not suppress that scan.
        name = path.rsplit("/", 1)[-1]
        if name not in NPM_INPUTS:
            continue
        directory = path.rsplit("/", 1)[0] if "/" in path else "."
        if directory not in SCOPES:
            return list(SCOPES)
        chosen.add(directory)

    return [scope for scope in SCOPES if scope in chosen] or list(SCOPES)


def select_scopes(event_name: str, base_sha: str, head_sha: str, *, cwd: str | Path = ".") -> tuple[list[str], str]:
    """Return selected projects and a short auditable routing reason.

    Both invalid revisions and failing history/diff commands are handled by
    scanning everything, not by emitting an empty success matrix.
    """
    if event_name not in {"pull_request", "push"}:
        return list(SCOPES), "manual-or-unknown-event-full"
    if not SHA.fullmatch(base_sha or "") or not SHA.fullmatch(head_sha or ""):
        return list(SCOPES), "invalid-revisions-full"
    if base_sha == head_sha:
        return list(SCOPES), "unchanged-revisions-full"

    try:
        base = base_sha
        if event_name == "pull_request":
            base = subprocess.run(
                ["git", "merge-base", base_sha, head_sha], cwd=cwd,
                check=True, capture_output=True, text=True, timeout=20,
            ).stdout.strip()
            if not SHA.fullmatch(base):
                return list(SCOPES), "invalid-merge-base-full"
        diff = subprocess.run(
            ["git", "diff", "--name-only", "-z", "--diff-filter=ACMRD", base, head_sha, "--"],
            cwd=cwd, check=True, capture_output=True, timeout=30,
        ).stdout
        if len(diff) > 1024 * 1024:
            return list(SCOPES), "oversized-diff-full"
        paths = [path.decode("utf-8") for path in diff.split(b"\0") if path]
    except (OSError, UnicodeError, subprocess.CalledProcessError, subprocess.TimeoutExpired):
        return list(SCOPES), "unavailable-diff-full"

    selected = scopes_for_paths(paths)
    return selected, "changed-scope" if len(selected) < len(SCOPES) else "all-projects"


def main() -> None:
    scopes, reason = select_scopes(
        os.environ.get("GITHUB_EVENT_NAME", ""),
        os.environ.get("BASE_SHA", ""),
        os.environ.get("HEAD_SHA", ""),
    )
    output = os.environ["GITHUB_OUTPUT"]
    with open(output, "a", encoding="utf-8") as stream:
        stream.write("scopes=" + json.dumps(scopes, separators=(",", ":")) + "\n")
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as stream:
            stream.write("### Socket Firewall install routing\n")
            stream.write(f"Reason: {reason}; selected projects: {', '.join(scopes)}\n")
    print(f"Socket Firewall routing: {reason}; scopes={scopes}")


if __name__ == "__main__":
    main()
