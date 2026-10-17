#!/usr/bin/env python3
"""Fail closed when repository files violate the public-visibility policy."""

from __future__ import annotations

import json
import re
import subprocess
import sys
from pathlib import Path
from typing import Iterable

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_DIR = ROOT / ".github" / "workflows"

REQUIRED_FILES = (
    ROOT / "COPYRIGHT.md",
    ROOT / "CONTRIBUTING.md",
    ROOT / "THIRD_PARTY_NOTICES.md",
    ROOT / ".github" / "SECURITY.md",
    ROOT / ".github" / "CODEOWNERS",
    ROOT / "docs" / "PUBLICATION_RUNBOOK.md",
    ROOT / ".gitleaksignore",
)

FORBIDDEN_EXACT_NAMES = {
    ".env",
    "id_rsa",
    "id_ed25519",
    "syncfusion-license.txt",
    "syncfusion_license.txt",
}
FORBIDDEN_SUFFIXES = (".p12", ".pfx")
SECRET_CONTEXT_PATTERN = re.compile(r"\$\{\{\s*secrets\s*(?:\.|\[)")
SELF_HOSTED_PATTERN = re.compile(
    r"(^|[\s,\[\]{}'\"-])self-hosted(?=$|[\s,\[\]{}'\",])"
)
CODEQL_PR_WORKFLOW = ".github/workflows/codeql.yml"
CODEQL_PR_JOB = "analyze"
CODEQL_PR_WRITE_PATTERN = re.compile(r"^security-events\s*:\s*write$")


def _without_comment(line: str) -> str:
    """Remove ordinary YAML comments without attempting a full YAML parse."""
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
    return len(line) - len(line.lstrip())


def workflow_triggers(text: str, event: str) -> bool:
    """Return whether a simple GitHub Actions ``on`` declaration includes event."""
    lines = text.splitlines()
    event_pattern = re.compile(
        rf"(^|[\s,\[\{{])[\"']?{re.escape(event)}[\"']?([\s,\]:\}}]|$)"
    )

    for index, raw_line in enumerate(lines):
        line = _without_comment(raw_line).rstrip()
        if not re.match(r"^(?:on|'on'|\"on\")\s*:", line):
            continue

        _, value = line.split(":", 1)
        if value.strip():
            return bool(event_pattern.search(value))

        for nested_raw in lines[index + 1 :]:
            nested = _without_comment(nested_raw).rstrip()
            if not nested.strip():
                continue
            indent = _indent(nested)
            if indent == 0:
                break
            stripped = nested.strip()
            if re.match(
                rf"^(?:-\s*)?[\"']?{re.escape(event)}[\"']?(?:\s*:|\s*$)",
                stripped,
            ):
                return True
        return False
    return False


def _job_blocks(text: str) -> list[tuple[str, int, int, int]]:
    """Return job id, line span, and indentation for ordinary ``jobs`` mappings."""
    lines = [_without_comment(line).rstrip() for line in text.splitlines()]
    jobs_index: int | None = None
    jobs_indent = 0

    for index, line in enumerate(lines):
        if re.match(r"^jobs\s*:\s*$", line):
            jobs_index = index
            jobs_indent = _indent(line)
            break

    if jobs_index is None:
        return []

    job_indent: int | None = None
    starts: list[tuple[int, str]] = []
    for index in range(jobs_index + 1, len(lines)):
        line = lines[index]
        if not line.strip():
            continue
        indent = _indent(line)
        if indent <= jobs_indent:
            break
        if re.match(r"^[A-Za-z0-9_.-]+\s*:\s*$", line.strip()):
            if job_indent is None:
                job_indent = indent
            if indent == job_indent:
                starts.append((index, line.strip()[:-1]))

    if job_indent is None:
        return []

    blocks: list[tuple[str, int, int, int]] = []
    for offset, (start, job_id) in enumerate(starts):
        end = starts[offset + 1][0] if offset + 1 < len(starts) else len(lines)
        for index in range(start + 1, end):
            if lines[index].strip() and _indent(lines[index]) <= jobs_indent:
                end = index
                break
        blocks.append((job_id, start, end, job_indent))
    return blocks


def _root_job_field(
    lines: list[str],
    start: int,
    end: int,
    job_indent: int,
    key: str,
) -> tuple[int, str, int] | None:
    """Return a job-root field's line, scalar value, and indentation."""
    clean = [_without_comment(line).rstrip() for line in lines]
    child_indents = [
        _indent(clean[index])
        for index in range(start + 1, end)
        if clean[index].strip() and _indent(clean[index]) > job_indent
    ]
    if not child_indents:
        return None

    child_indent = min(child_indents)
    pattern = re.compile(rf"^{re.escape(key)}\s*:\s*(.*)$")
    for index in range(start + 1, end):
        line = clean[index]
        if not line.strip() or _indent(line) != child_indent:
            continue
        match = pattern.match(line.strip())
        if match:
            return index, match.group(1), child_indent
    return None


def _field_block_text(
    lines: list[str],
    field_index: int,
    field_value: str,
    field_indent: int,
    end: int,
) -> str:
    values = [field_value.strip()] if field_value.strip() else []
    clean = [_without_comment(line).rstrip() for line in lines]
    for index in range(field_index + 1, end):
        line = clean[index]
        if not line.strip():
            continue
        if _indent(line) <= field_indent:
            break
        values.append(line.strip())
    return " ".join(values)


def _static_environment_name(
    lines: list[str],
    start: int,
    end: int,
    job_indent: int,
) -> str | None:
    field = _root_job_field(lines, start, end, job_indent, "environment")
    if field is None:
        return None

    field_index, value, environment_indent = field
    value = value.strip()
    if value:
        if "${{" in value:
            return None
        return value.strip("'\"") or None

    clean = [_without_comment(line).rstrip() for line in lines]
    nested_indents = [
        _indent(clean[index])
        for index in range(field_index + 1, end)
        if clean[index].strip() and _indent(clean[index]) > environment_indent
    ]
    if not nested_indents:
        return None

    nested_indent = min(nested_indents)
    for index in range(field_index + 1, end):
        line = clean[index]
        if not line.strip():
            continue
        indent = _indent(line)
        if indent <= environment_indent:
            break
        if indent != nested_indent:
            continue
        match = re.match(r"^name\s*:\s*(.+)$", line.strip())
        if not match:
            continue
        value = match.group(1).strip()
        if "${{" in value:
            return None
        return value.strip("'\"") or None
    return None


def _job_inherits_secrets(
    lines: list[str],
    start: int,
    end: int,
    job_indent: int,
) -> bool:
    field = _root_job_field(lines, start, end, job_indent, "secrets")
    return field is not None and field[1].strip() == "inherit"


def _permission_write_lines(text: str) -> list[int]:
    """Return line numbers where workflow/job permissions request write access."""
    lines = text.splitlines()
    clean = [_without_comment(line).rstrip() for line in lines]
    fields: list[tuple[int, str, int, int]] = []

    for index, line in enumerate(clean):
        match = re.match(r"^permissions\s*:\s*(.*)$", line)
        if match:
            fields.append((index, match.group(1), 0, len(lines)))
            break

    for _, start, end, job_indent in _job_blocks(text):
        field = _root_job_field(lines, start, end, job_indent, "permissions")
        if field is not None:
            index, value, indent = field
            fields.append((index, value, indent, end))

    result: list[int] = []
    for index, value, field_indent, end in fields:
        value = value.strip()
        if value:
            if re.search(r"\bwrite-all\b", value) or re.search(r":\s*write\b", value):
                result.append(index + 1)
            continue

        for nested_index in range(index + 1, end):
            line = clean[nested_index]
            if not line.strip():
                continue
            if _indent(line) <= field_indent:
                break
            if re.match(
                r"^\s*[A-Za-z0-9_-]+\s*:\s*(?:write|write-all)\s*$",
                line,
            ):
                result.append(nested_index + 1)
    return result


def _allowed_pull_request_write(
    relative: str,
    lines: list[str],
    job_blocks: list[tuple[str, int, int, int]],
    line_number: int,
) -> bool:
    """Allow only CodeQL's SARIF publication permission on its canonical PR job."""
    if relative != CODEQL_PR_WORKFLOW:
        return False

    index = line_number - 1
    if not (0 <= index < len(lines)):
        return False
    if not CODEQL_PR_WRITE_PATTERN.fullmatch(_without_comment(lines[index]).strip()):
        return False

    return any(
        job_id == CODEQL_PR_JOB and start < index < end
        for job_id, start, end, _ in job_blocks
    )


def tracked_files() -> list[str]:
    result = subprocess.run(
        ["git", "ls-files", "-z"],
        cwd=ROOT,
        check=True,
        stdout=subprocess.PIPE,
    )
    return [entry.decode("utf-8") for entry in result.stdout.split(b"\0") if entry]


def workflow_errors(path: Path, text: str) -> list[str]:
    errors: list[str] = []
    relative = path.relative_to(ROOT).as_posix()
    triggers_pr = workflow_triggers(text, "pull_request")
    triggers_pr_target = workflow_triggers(text, "pull_request_target")
    lines = text.splitlines()
    job_blocks = _job_blocks(text)

    if triggers_pr_target:
        errors.append(f"{relative}: pull_request_target is forbidden")

    covered_lines: set[int] = set()
    for job_id, start, end, job_indent in job_blocks:
        covered_lines.update(range(start, end))

        runs_on = _root_job_field(lines, start, end, job_indent, "runs-on")
        if runs_on is not None:
            field_index, value, field_indent = runs_on
            runner_text = _field_block_text(
                lines, field_index, value, field_indent, end
            )
            if SELF_HOSTED_PATTERN.search(runner_text):
                errors.append(
                    f"{relative}:{field_index + 1}: persistent self-hosted runner routing is forbidden"
                )

        job_text = "\n".join(_without_comment(line) for line in lines[start:end])
        uses_secret = bool(SECRET_CONTEXT_PATTERN.search(job_text))
        inherits_secret = _job_inherits_secrets(lines, start, end, job_indent)
        if triggers_pr and (uses_secret or inherits_secret):
            errors.append(
                f"{relative}: pull-request job '{job_id}' references or inherits a secret"
            )
        if (uses_secret or inherits_secret) and _static_environment_name(
            lines, start, end, job_indent
        ) is None:
            errors.append(
                f"{relative}: secret-bearing job '{job_id}' lacks a static protected environment"
            )

    for index, raw_line in enumerate(lines):
        if index in covered_lines:
            continue
        if SECRET_CONTEXT_PATTERN.search(_without_comment(raw_line)):
            errors.append(
                f"{relative}:{index + 1}: secret reference outside a protected job is forbidden"
            )

    if triggers_pr:
        for line_number in _permission_write_lines(text):
            if _allowed_pull_request_write(relative, lines, job_blocks, line_number):
                continue
            errors.append(
                f"{relative}:{line_number}: pull-request workflow requests write permission"
            )

    return errors


def manifest_errors(path: Path) -> list[str]:
    relative = path.relative_to(ROOT).as_posix()
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        return [f"{relative}: cannot parse JSON: {exc}"]

    errors: list[str] = []
    if data.get("private") is not True:
        errors.append(f"{relative}: package must remain private")
    if data.get("license") != "UNLICENSED":
        errors.append(f"{relative}: license must be UNLICENSED")
    return errors


def repository_errors(paths: Iterable[str]) -> list[str]:
    errors: list[str] = []
    tracked = list(paths)

    for required in REQUIRED_FILES:
        if not required.is_file():
            errors.append(f"{required.relative_to(ROOT).as_posix()}: required file missing")

    ignore_path = ROOT / ".gitleaksignore"
    if ignore_path.is_file():
        fingerprint_pattern = re.compile(
            r"^[0-9a-f]{40}:[^:\n]+:[a-z0-9-]+:[1-9][0-9]*$"
        )
        for line_number, raw_line in enumerate(
            ignore_path.read_text(encoding="utf-8").splitlines(), start=1
        ):
            entry = raw_line.strip()
            if not entry or entry.startswith("#"):
                continue
            if not fingerprint_pattern.fullmatch(entry):
                errors.append(
                    f".gitleaksignore:{line_number}: only exact finding fingerprints are allowed"
                )

    readme = (ROOT / "README.md").read_text(encoding="utf-8")
    if "Public source, not open source" not in readme:
        errors.append("README.md: public source notice missing")

    for manifest in (
        ROOT / "package.json",
        ROOT / "frontend" / "package.json",
        ROOT / "coglatas-frontend" / "package.json",
    ):
        errors.extend(manifest_errors(manifest))

    for lock_path in (
        ROOT / "package-lock.json",
        ROOT / "frontend" / "package-lock.json",
        ROOT / "coglatas-frontend" / "package-lock.json",
    ):
        relative = lock_path.relative_to(ROOT).as_posix()
        try:
            lock_data = json.loads(lock_path.read_text(encoding="utf-8"))
            root_package = lock_data.get("packages", {}).get("", {})
            if root_package.get("license") != "UNLICENSED":
                errors.append(f"{relative}: root package license must be UNLICENSED")
        except (OSError, json.JSONDecodeError) as exc:
            errors.append(f"{relative}: cannot parse lock file: {exc}")

    for entry in tracked:
        path = Path(entry)
        lowered_name = path.name.lower()
        lowered_parts = {part.lower() for part in path.parts}

        if "secrets" in lowered_parts:
            errors.append(f"{entry}: tracked secrets directory is forbidden")
        if lowered_name in FORBIDDEN_EXACT_NAMES:
            errors.append(f"{entry}: tracked sensitive filename is forbidden")
        if lowered_name.startswith(".env.") and lowered_name != ".env.example":
            errors.append(f"{entry}: tracked environment file is forbidden")
        if lowered_name.endswith(FORBIDDEN_SUFFIXES):
            errors.append(f"{entry}: tracked private-key container is forbidden")

    for workflow in sorted(WORKFLOW_DIR.glob("*.y*ml")):
        errors.extend(workflow_errors(workflow, workflow.read_text(encoding="utf-8")))

    source_roots = (ROOT / "frontend" / "src",)
    for source_root in source_roots:
        for path in source_root.rglob("*"):
            if path.is_file() and path.suffix.lower() in {".ts", ".js", ".mjs"}:
                text = path.read_text(encoding="utf-8", errors="replace")
                if re.search(r"\bregisterLicense\s*\(", text):
                    errors.append(
                        f"{path.relative_to(ROOT).as_posix()}: browser-side Syncfusion license registration is forbidden"
                    )

    return errors


def main() -> int:
    errors = repository_errors(tracked_files())
    if errors:
        print("Publication-readiness policy failed:", file=sys.stderr)
        for error in sorted(set(errors)):
            print(f"- {error}", file=sys.stderr)
        return 1

    workflows = list(WORKFLOW_DIR.glob("*.y*ml"))
    print(
        "Publication-readiness policy passed: "
        f"{len(workflows)} workflow(s) checked; no active self-hosted routing, "
        "no PR secret access, required governance files present."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())