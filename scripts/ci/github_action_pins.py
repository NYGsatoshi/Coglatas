#!/usr/bin/env python3
"""Validate external GitHub Action refs for Issue #627.

The validator inventories every external ``uses:`` reference under
``.github/workflows``. Unknown action repositories are rejected everywhere.
Workflows that are required by repository policy or that carry privileged
capabilities (GitHub secrets, protected environments, inherited secrets, or
write permissions) must use an allowlisted full 40-character commit SHA and a
human-readable version comment.

Allowlist entries may additionally restrict a repository to one exact action or
reusable-workflow path. This prevents approval of one reviewed same-repository
workflow from implicitly approving every ``uses:`` target in that repository.

Unprivileged helper workflows may temporarily retain mutable refs while the
repository migrates them incrementally, but they cannot introduce an unknown
external action. This keeps the security boundary focused on code that can
block merges or receive elevated authority without recreating the retired
``ci/governance`` aggregate.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any

DEFAULT_POLICY_RELATIVE = Path("governance/github-actions-allowlist.json")
WORKFLOW_RELATIVE = Path(".github/workflows")

FULL_SHA = re.compile(r"^[0-9a-f]{40}$")
SECRET_CONTEXT = re.compile(r"\$\{\{\s*secrets\s*(?:\.|\[)")
SECRETS_INHERIT = re.compile(r"^\s*secrets\s*:\s*inherit\s*$")
ENVIRONMENT_FIELD = re.compile(r"^\s*environment\s*:")
PERMISSIONS_FIELD = re.compile(r"^(?P<indent>\s*)permissions\s*:\s*(?P<value>.*)$")
WRITE_VALUE = re.compile(r"(?:^|[,{\s])(?:[A-Za-z0-9_-]+\s*:\s*)?write(?:-all)?(?:$|[,}\s])")
# Match canonical and YAML-quoted "uses" keys alike. A quoted spelling
# must receive precisely the same immutable-pin and allowlist validation.
USES_KEY = r"(?:uses|'uses'|\"uses\")"
USES_LINE = re.compile(
    rf"^\s*(?:-\s*)?{USES_KEY}\s*:\s*(?P<quote>['\"]?)(?P<target>[^'\"#\s]+)(?P=quote)"
    r"\s*(?:#\s*(?P<comment>.*?))?\s*$"
)
USES_PREFIX = re.compile(rf"^\s*(?:-\s*)?{USES_KEY}\s*:")

# The physical-line validator cannot safely inventory YAML flow-style
# jobs/steps. Reject those forms rather than silently missing an action.
FLOW_STYLE_JOBS = re.compile(r"^jobs\s*:\s*[\[{]")
FLOW_STYLE_JOB = re.compile(r"^  [A-Za-z][A-Za-z0-9_-]*\s*:\s*\{")
FLOW_STYLE_STEPS = re.compile(r"^    steps\s*:\s*[\[{]")
FLOW_STYLE_STEP = re.compile(r"^      -\s*\{")


@dataclass(frozen=True)
class UseReference:
    workflow: str
    line: int
    target: str
    repository: str | None
    target_path: str | None
    ref: str | None
    version_comment: str | None
    local: bool


def _without_comment(line: str) -> str:
    """Strip an unquoted YAML comment while preserving quoted hash characters."""
    quote: str | None = None
    escaped = False
    result: list[str] = []
    for char in line:
        if escaped:
            result.append(char)
            escaped = False
            continue
        if char == "\\" and quote == '"':
            result.append(char)
            escaped = True
            continue
        if char in {"'", '"'}:
            if quote is None:
                quote = char
            elif quote == char:
                quote = None
            result.append(char)
            continue
        if char == "#" and quote is None:
            break
        result.append(char)
    return "".join(result)


def _indent(line: str) -> int:
    """Return the number of leading whitespace characters in a line."""
    return len(line) - len(line.lstrip())


def _requests_write_permission(text: str) -> bool:
    """Conservatively identify workflow/job permission blocks with write access."""
    lines = text.splitlines()
    clean = [_without_comment(line).rstrip() for line in lines]

    for index, line in enumerate(clean):
        match = PERMISSIONS_FIELD.match(line)
        if not match:
            continue
        field_indent = len(match.group("indent"))
        value = match.group("value").strip()
        if value and WRITE_VALUE.search(value):
            return True
        if value:
            continue

        for nested in clean[index + 1 :]:
            if not nested.strip():
                continue
            if _indent(nested) <= field_indent:
                break
            stripped = nested.strip()
            if re.match(r"^[A-Za-z0-9_-]+\s*:\s*write\s*$", stripped):
                return True
    return False


def _valid_reference_path(value: object) -> bool:
    """Return whether an allowlisted ``uses:`` path is a safe relative path."""
    if not isinstance(value, str) or not value or value.startswith("/"):
        return False
    return all(part not in {"", ".", ".."} for part in value.split("/"))


def _load_policy(path: Path) -> tuple[dict[str, Any], list[str]]:
    """Load and structurally validate the GitHub Action allowlist policy."""
    errors: list[str] = []
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        return {}, [f"{path}: cannot parse policy JSON: {exc}"]

    if data.get("schemaVersion") != 1:
        errors.append(f"{path}: schemaVersion must be 1")

    required = data.get("requiredWorkflows")
    if not isinstance(required, list) or not all(isinstance(item, str) for item in required):
        errors.append(f"{path}: requiredWorkflows must be an array of paths")

    actions = data.get("actions")
    if not isinstance(actions, dict) or not actions:
        errors.append(f"{path}: actions must be a non-empty object")
        return data, errors

    for repository, entry in sorted(actions.items()):
        if not re.fullmatch(r"[^/\s]+/[^/\s]+", repository):
            errors.append(f"{path}: invalid action repository key {repository!r}")
            continue
        if not isinstance(entry, dict):
            errors.append(f"{path}: {repository} policy must be an object")
            continue
        sha = entry.get("sha")
        version = entry.get("version")
        purpose = entry.get("purpose")
        privileged = entry.get("privilegedAllowed")
        reference_path = entry.get("path")
        if not isinstance(sha, str) or not FULL_SHA.fullmatch(sha):
            errors.append(f"{path}: {repository}.sha must be a lowercase 40-character SHA")
        if not isinstance(version, str) or not version.strip():
            errors.append(f"{path}: {repository}.version is required")
        if not isinstance(purpose, str) or not purpose.strip():
            errors.append(f"{path}: {repository}.purpose is required")
        if not isinstance(privileged, bool):
            errors.append(f"{path}: {repository}.privilegedAllowed must be boolean")
        if reference_path is not None and not _valid_reference_path(reference_path):
            errors.append(f"{path}: {repository}.path must be a safe relative uses path")

    return data, errors


def _parse_uses(workflow: str, text: str) -> tuple[list[UseReference], list[str]]:
    """Parse workflow ``uses:`` references and return fail-closed parse errors."""
    references: list[UseReference] = []
    errors: list[str] = []
    in_jobs = False

    for line_number, raw in enumerate(text.splitlines(), start=1):
        # A flow-style step or job can conceal a uses reference from the
        # line-oriented inventory. Fail closed on unsupported forms, including
        # quoted/escaped mapping keys that would otherwise be invisible.
        if re.match(r"^jobs\s*:", raw):
            in_jobs = True
        elif raw and not raw[0].isspace() and not raw.startswith("#"):
            in_jobs = False
        if FLOW_STYLE_JOBS.match(raw) or (in_jobs and (
            FLOW_STYLE_JOB.match(raw)
            or FLOW_STYLE_STEPS.match(raw)
            or FLOW_STYLE_STEP.match(raw)
        )):
            errors.append(
                f"{workflow}:{line_number}: unsupported flow-style job or step fail-closed"
            )
            continue

        clean = _without_comment(raw).strip()
        if not USES_PREFIX.match(clean):
            continue

        match = USES_LINE.match(raw)
        if not match:
            errors.append(f"{workflow}:{line_number}: cannot parse uses reference fail-closed")
            continue

        target = match.group("target")
        comment = (match.group("comment") or "").strip() or None
        if target.startswith("./"):
            references.append(
                UseReference(workflow, line_number, target, None, None, None, comment, True)
            )
            continue

        if "@" not in target:
            errors.append(f"{workflow}:{line_number}: external uses reference lacks @ref: {target}")
            references.append(
                UseReference(workflow, line_number, target, None, None, None, comment, False)
            )
            continue

        action_path, ref = target.rsplit("@", 1)
        parts = action_path.split("/")
        if len(parts) < 2 or not parts[0] or not parts[1]:
            errors.append(f"{workflow}:{line_number}: invalid external action path: {target}")
            repository = None
            target_path = None
        else:
            repository = f"{parts[0]}/{parts[1]}"
            target_path = "/".join(parts[2:]) or None

        references.append(
            UseReference(workflow, line_number, target, repository, target_path, ref, comment, False)
        )

    return references, errors


def _scope_reasons(relative: str, text: str, required: set[str]) -> list[str]:
    """Return reasons a workflow must obey protected immutable-reference policy."""
    reasons: list[str] = []
    clean_text = "\n".join(_without_comment(line) for line in text.splitlines())
    if relative in required:
        reasons.append("required-workflow")
    if SECRET_CONTEXT.search(clean_text) or any(
        SECRETS_INHERIT.match(line) for line in clean_text.splitlines()
    ):
        reasons.append("secret-bearing")
    if any(ENVIRONMENT_FIELD.match(line) for line in clean_text.splitlines()):
        reasons.append("protected-environment")
    if _requests_write_permission(text):
        reasons.append("write-permission")
    return reasons


def validate_repository(
    root: Path,
    policy_path: Path | None = None,
) -> tuple[list[str], list[dict[str, Any]]]:
    """Validate workflow references against policy and return errors plus inventory."""
    root = root.resolve()
    policy_path = (policy_path or root / DEFAULT_POLICY_RELATIVE).resolve()
    policy, errors = _load_policy(policy_path)
    if errors and not policy:
        return errors, []

    actions = policy.get("actions", {}) if isinstance(policy.get("actions"), dict) else {}
    required_items = policy.get("requiredWorkflows", [])
    required = set(required_items if isinstance(required_items, list) else [])
    workflow_dir = root / WORKFLOW_RELATIVE

    if not workflow_dir.is_dir():
        return errors + [f"{WORKFLOW_RELATIVE.as_posix()}: workflow directory missing"], []

    inventory: list[dict[str, Any]] = []
    seen_workflows: set[str] = set()

    for path in sorted([*workflow_dir.glob("*.yml"), *workflow_dir.glob("*.yaml")]):
        relative = path.relative_to(root).as_posix()
        seen_workflows.add(relative)
        text = path.read_text(encoding="utf-8")
        scope_reasons = _scope_reasons(relative, text, required)
        protected = bool(scope_reasons)
        references, parse_errors = _parse_uses(relative, text)
        errors.extend(parse_errors)

        for reference in references:
            if reference.local:
                inventory.append(
                    {
                        "workflow": relative,
                        "line": reference.line,
                        "target": reference.target,
                        "scope": "local",
                        "protected": protected,
                        "scopeReasons": scope_reasons,
                    }
                )
                continue

            entry = actions.get(reference.repository) if reference.repository else None
            state = "external"
            if not isinstance(entry, dict):
                errors.append(
                    f"{relative}:{reference.line}: external action repository is not allowlisted: "
                    f"{reference.repository or reference.target}"
                )
                state = "unknown"
            else:
                expected_path = entry.get("path")
                if expected_path is not None and reference.target_path != expected_path:
                    errors.append(
                        f"{relative}:{reference.line}: {reference.repository} path "
                        f"{reference.target_path or '<repository-root>'} does not match reviewed allowlist path "
                        f"{expected_path}"
                    )
                    state = "unreviewed-path"

                if protected:
                    if entry.get("privilegedAllowed") is not True:
                        errors.append(
                            f"{relative}:{reference.line}: {reference.repository} is not approved for protected workflows"
                        )
                    expected_sha = entry.get("sha")
                    expected_version = entry.get("version")
                    if not reference.ref or not FULL_SHA.fullmatch(reference.ref):
                        errors.append(
                            f"{relative}:{reference.line}: protected external action must use a full immutable SHA: "
                            f"{reference.target}"
                        )
                        state = "mutable"
                    elif reference.ref != expected_sha:
                        errors.append(
                            f"{relative}:{reference.line}: {reference.repository} SHA {reference.ref} does not match "
                            f"reviewed allowlist SHA {expected_sha}"
                        )
                        state = "unreviewed-sha"
                    version_token = (reference.version_comment or "").split(maxsplit=1)[0] if reference.version_comment else ""
                    if version_token != expected_version:
                        errors.append(
                            f"{relative}:{reference.line}: protected action pin requires version comment '# {expected_version}'"
                        )

            inventory.append(
                {
                    "workflow": relative,
                    "line": reference.line,
                    "target": reference.target,
                    "repository": reference.repository,
                    "path": reference.target_path,
                    "ref": reference.ref,
                    "versionComment": reference.version_comment,
                    "scope": state,
                    "protected": protected,
                    "scopeReasons": scope_reasons,
                }
            )

    for required_path in sorted(required - seen_workflows):
        errors.append(f"{required_path}: required workflow missing")

    inventory.sort(key=lambda item: (item["workflow"], item["line"], item["target"]))
    return sorted(set(errors)), inventory


def main(argv: list[str] | None = None) -> int:
    """Run the validator CLI and return a process exit code."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--policy", type=Path)
    parser.add_argument("--inventory-out", type=Path)
    args = parser.parse_args(argv)

    root = args.root.resolve()
    policy = args.policy.resolve() if args.policy else root / DEFAULT_POLICY_RELATIVE
    errors, inventory = validate_repository(root, policy)

    if args.inventory_out:
        output = args.inventory_out
        if not output.is_absolute():
            output = root / output
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(
            json.dumps(
                {
                    "schemaVersion": 1,
                    "policy": policy.relative_to(root).as_posix() if policy.is_relative_to(root) else str(policy),
                    "uses": inventory,
                },
                indent=2,
                sort_keys=True,
            )
            + "\n",
            encoding="utf-8",
        )

    if errors:
        print("GitHub Action pin policy failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    protected_workflows = len({item["workflow"] for item in inventory if item.get("protected")})
    external = sum(1 for item in inventory if item.get("scope") != "local")
    print(
        "GitHub Action pin policy passed: "
        f"{external} external uses reference(s) inventoried; "
        f"{protected_workflows} protected workflow(s) immutable and allowlisted."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
