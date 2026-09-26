#!/usr/bin/env python3
"""Export a secret-safe inventory of open GitHub Code Scanning alerts.

The exporter deliberately keeps only metadata needed for remediation planning.
It never persists alert messages, source snippets, data-flow paths, rule help,
or request/response content.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
from collections import Counter
from pathlib import Path
from typing import Any, Iterable

API_ROOT = "https://api.github.com"
API_VERSION = "2022-11-28"
SCHEMA_VERSION = 1
SEVERITY_ORDER = {
    "critical": 0,
    "high": 1,
    "medium": 2,
    "low": 3,
    "error": 4,
    "warning": 5,
    "note": 6,
    "unknown": 7,
}


def _language(rule_id: str, path: str) -> str:
    prefix = rule_id.split("/", 1)[0].lower()
    if prefix in {"cs", "csharp"}:
        return "csharp"
    if prefix in {"js", "javascript", "typescript", "ts"}:
        return "javascript-typescript"
    if prefix in {"py", "python"}:
        return "python"
    if prefix in {"actions", "github-actions"}:
        return "actions"

    lowered = path.lower()
    if lowered.endswith((".cs", ".csx")):
        return "csharp"
    if lowered.endswith((".js", ".jsx", ".mjs", ".cjs", ".ts", ".tsx")):
        return "javascript-typescript"
    if lowered.endswith(".py"):
        return "python"
    if lowered.startswith(".github/workflows/") and lowered.endswith((".yml", ".yaml")):
        return "actions"
    return "unknown"


def _cwes(tags: Iterable[Any]) -> list[str]:
    result: list[str] = []
    for raw in tags:
        if not isinstance(raw, str):
            continue
        marker = "external/cwe/cwe-"
        lowered = raw.lower()
        if lowered.startswith(marker):
            result.append("CWE-" + raw[len(marker) :].upper())
    return sorted(set(result))


def sanitize_alert(alert: dict[str, Any]) -> dict[str, Any]:
    """Return only remediation metadata; intentionally discard alert evidence."""
    rule = alert.get("rule") if isinstance(alert.get("rule"), dict) else {}
    tool = alert.get("tool") if isinstance(alert.get("tool"), dict) else {}
    instance = (
        alert.get("most_recent_instance")
        if isinstance(alert.get("most_recent_instance"), dict)
        else {}
    )
    location = instance.get("location") if isinstance(instance.get("location"), dict) else {}

    rule_id = str(rule.get("id") or "unknown")
    path = str(location.get("path") or "unknown")
    security_severity = str(rule.get("security_severity_level") or "").lower()
    severity = str(rule.get("severity") or "unknown").lower()

    return {
        "number": alert.get("number"),
        "state": str(alert.get("state") or "unknown"),
        "tool": str(tool.get("name") or "unknown"),
        "tool_version": str(tool.get("version") or "unknown"),
        "rule_id": rule_id,
        "severity": severity,
        "security_severity": security_severity or None,
        "language": _language(rule_id, path),
        "cwes": _cwes(rule.get("tags") if isinstance(rule.get("tags"), list) else []),
        "path": path,
        "start_line": location.get("start_line"),
        "classifications": sorted(
            value
            for value in instance.get("classifications", [])
            if isinstance(value, str)
        ),
        "created_at": alert.get("created_at"),
        "updated_at": alert.get("updated_at"),
        "html_url": alert.get("html_url"),
    }


def _effective_severity(alert: dict[str, Any]) -> str:
    security = alert.get("security_severity")
    if isinstance(security, str) and security:
        return security
    severity = alert.get("severity")
    return str(severity or "unknown").lower()


def build_inventory(
    alerts: Iterable[dict[str, Any]], *, repository: str, ref: str
) -> dict[str, Any]:
    sanitized = [sanitize_alert(alert) for alert in alerts]
    sanitized.sort(
        key=lambda item: (
            SEVERITY_ORDER.get(_effective_severity(item), 99),
            str(item.get("tool")),
            str(item.get("language")),
            str(item.get("rule_id")),
            str(item.get("path")),
            int(item.get("number") or 0),
        )
    )

    by_tool = Counter(str(item["tool"]) for item in sanitized)
    by_language = Counter(str(item["language"]) for item in sanitized)
    by_severity = Counter(_effective_severity(item) for item in sanitized)
    by_rule = Counter(
        f"{item['tool']}::{item['language']}::{item['rule_id']}" for item in sanitized
    )

    return {
        "schema_version": SCHEMA_VERSION,
        "repository": repository,
        "ref": ref,
        "open_alert_count": len(sanitized),
        "counts": {
            "by_tool": dict(sorted(by_tool.items())),
            "by_language": dict(sorted(by_language.items())),
            "by_severity": dict(
                sorted(
                    by_severity.items(),
                    key=lambda pair: (SEVERITY_ORDER.get(pair[0], 99), pair[0]),
                )
            ),
            "by_rule": dict(sorted(by_rule.items())),
        },
        "alerts": sanitized,
    }


def render_markdown(inventory: dict[str, Any]) -> str:
    lines = [
        "# Code Scanning open-alert inventory",
        "",
        f"- Repository: `{inventory['repository']}`",
        f"- Ref: `{inventory['ref']}`",
        f"- Open alerts: **{inventory['open_alert_count']}**",
        "",
        "## Counts by severity",
        "",
    ]
    for severity, count in inventory["counts"]["by_severity"].items():
        lines.append(f"- `{severity}`: {count}")

    lines.extend(["", "## Counts by language", ""])
    for language, count in inventory["counts"]["by_language"].items():
        lines.append(f"- `{language}`: {count}")

    lines.extend(["", "## Counts by rule", ""])
    for rule, count in inventory["counts"]["by_rule"].items():
        lines.append(f"- `{rule}`: {count}")

    lines.extend(
        [
            "",
            "## Safe alert metadata",
            "",
            "| Alert | Severity | Tool | Language | Rule | Location | CWE |",
            "|---:|---|---|---|---|---|---|",
        ]
    )
    for alert in inventory["alerts"]:
        number = alert.get("number")
        severity = _effective_severity(alert)
        cwes = ", ".join(alert.get("cwes") or []) or "-"
        path = str(alert.get("path") or "unknown").replace("|", "\\|")
        line = alert.get("start_line")
        location = f"`{path}:{line}`" if line else f"`{path}`"
        lines.append(
            "| "
            f"#{number} | `{severity}` | `{alert['tool']}` | `{alert['language']}` | "
            f"`{alert['rule_id']}` | {location} | {cwes} |"
        )

    lines.extend(
        [
            "",
            "> Safety contract: this inventory excludes alert messages, source snippets, data-flow paths, rule help, and raw evidence.",
        ]
    )
    return "\n".join(lines) + "\n"


def _request_page(url: str, token: str) -> list[dict[str, Any]]:
    request = urllib.request.Request(
        url,
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {token}",
            "X-GitHub-Api-Version": API_VERSION,
            "User-Agent": "Coglatas-code-scanning-inventory",
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            payload = json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as exc:
        raise RuntimeError(
            f"GitHub Code Scanning API returned HTTP {exc.code}"
        ) from exc
    except (urllib.error.URLError, json.JSONDecodeError) as exc:
        raise RuntimeError("GitHub Code Scanning API request failed") from exc

    if not isinstance(payload, list):
        raise RuntimeError("GitHub Code Scanning API returned a non-list payload")
    return [item for item in payload if isinstance(item, dict)]


def fetch_open_alerts(repository: str, ref: str, token: str) -> list[dict[str, Any]]:
    alerts: list[dict[str, Any]] = []
    page = 1
    while True:
        query = urllib.parse.urlencode(
            {"state": "open", "ref": ref, "per_page": 100, "page": page}
        )
        url = f"{API_ROOT}/repos/{repository}/code-scanning/alerts?{query}"
        batch = _request_page(url, token)
        alerts.extend(batch)
        if len(batch) < 100:
            return alerts
        page += 1
        if page > 100:
            raise RuntimeError("Refusing to paginate beyond 10,000 alerts")


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", default=os.environ.get("GITHUB_REPOSITORY"))
    parser.add_argument("--ref", default="refs/heads/main")
    parser.add_argument("--json-out", type=Path)
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv or sys.argv[1:])
    if not args.repo or "/" not in args.repo:
        print("A valid owner/repository is required", file=sys.stderr)
        return 2

    token = os.environ.get("GITHUB_TOKEN")
    if not token:
        print("GITHUB_TOKEN is required", file=sys.stderr)
        return 2

    try:
        alerts = fetch_open_alerts(args.repo, args.ref, token)
        inventory = build_inventory(alerts, repository=args.repo, ref=args.ref)
    except RuntimeError as exc:
        print(f"Code Scanning inventory failed: {exc}", file=sys.stderr)
        return 1

    if args.json_out:
        args.json_out.parent.mkdir(parents=True, exist_ok=True)
        args.json_out.write_text(
            json.dumps(inventory, indent=2, sort_keys=True) + "\n", encoding="utf-8"
        )

    print(render_markdown(inventory), end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
