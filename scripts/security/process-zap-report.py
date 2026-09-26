#!/usr/bin/env python3
"""Reduce SEC-06 ZAP output to reproducible, secret-free blocking evidence."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
from collections import Counter
from pathlib import Path
from typing import Any, NoReturn
from urllib.parse import parse_qsl, unquote, urlsplit, urlunsplit

FORBIDDEN_VALUES_ENV = "COGLATAS_SECURITY_ZAP_FORBIDDEN_VALUES"
ALLOW_UNSANITIZED_ENV = "COGLATAS_SECURITY_ZAP_ALLOW_UNSANITIZED"
RISK_FROM_CODE = {
    "0": "Informational",
    "1": "Low",
    "2": "Medium",
    "3": "High",
}
RISK_ORDER = ("High", "Medium", "Low", "Informational")


def fail(message: str) -> NoReturn:
    raise SystemExit(f"SEC-06 ZAP report rejected: {message}")


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def safe_text(value: Any, limit: int = 180) -> str:
    text = str(value or "")
    text = re.sub(r"[\x00-\x1f\x7f]+", " ", text).strip()
    return text[:limit]


def normalize_origin(raw: str) -> str:
    try:
        parsed = urlsplit(raw)
        port = parsed.port
    except ValueError as exc:
        fail(f"invalid origin {raw!r}: {exc}")
    if parsed.scheme not in {"http", "https"} or not parsed.hostname:
        fail(f"invalid HTTP(S) origin {raw!r}")
    if parsed.username is not None or parsed.password is not None:
        fail("origin userinfo is forbidden")
    host = parsed.hostname.rstrip(".").lower()
    display_host = f"[{host}]" if ":" in host else host
    default_port = (parsed.scheme == "http" and port in {None, 80}) or (
        parsed.scheme == "https" and port in {None, 443}
    )
    netloc = display_host if default_port else f"{display_host}:{port}"
    return urlunsplit((parsed.scheme.lower(), netloc, "", "", ""))


def safe_location(raw: Any, target_origin: str) -> dict[str, Any] | None:
    if not raw:
        return None
    value = str(raw)
    parsed = urlsplit(value)
    if not parsed.scheme or not parsed.netloc:
        fail("alert instance URI must be absolute")
    location_origin = normalize_origin(value)
    if location_origin != target_origin:
        fail(f"cross-origin alert evidence observed: {location_origin!r}")
    query_names = sorted(
        {key[:80] for key, _ in parse_qsl(parsed.query, keep_blank_values=True) if key}
    )
    return {
        "path": parsed.path or "/",
        "queryParameterNames": query_names,
    }


def normalize_risk(alert: dict[str, Any]) -> str | None:
    riskdesc = safe_text(alert.get("riskdesc"), 64)
    if riskdesc:
        head = riskdesc.split(" ", 1)[0].strip().lower()
        if head == "informational":
            return "Informational"
        if head in {"high", "medium", "low"}:
            return head.title()
    return RISK_FROM_CODE.get(str(alert.get("riskcode", "")))


def load_forbidden_values() -> tuple[list[str], bool]:
    raw = os.environ.get(FORBIDDEN_VALUES_ENV, "")
    allow_unsanitized = os.environ.get(ALLOW_UNSANITIZED_ENV) == "1"
    if not raw:
        if allow_unsanitized:
            return [], True
        fail(
            f"{FORBIDDEN_VALUES_ENV} is not set; SEC-06 cannot prove the evidence "
            "is free of ephemeral authentication material"
        )
    try:
        values = json.loads(raw)
    except json.JSONDecodeError as exc:
        fail(f"{FORBIDDEN_VALUES_ENV} is invalid JSON: {exc}")
    if not isinstance(values, list) or not all(isinstance(value, str) for value in values):
        fail(f"{FORBIDDEN_VALUES_ENV} must be a JSON array of strings")
    filtered = sorted({value for value in values if value})
    if not filtered and not allow_unsanitized:
        fail(
            f"{FORBIDDEN_VALUES_ENV} contains no non-empty values; SEC-06 cannot "
            "prove the evidence is free of ephemeral authentication material"
        )
    return filtered, allow_unsanitized


def site_alerts(site: Any, target_origin: str) -> list[Any]:
    if not isinstance(site, dict):
        fail("site entry must be an object")
    site_name = site.get("@name")
    if site_name and normalize_origin(str(site_name)) != target_origin:
        fail(f"report contains non-target site {site_name!r}")
    alerts = site.get("alerts", []) or []
    if not isinstance(alerts, list):
        fail("site alerts must be an array")
    return alerts


def normalize_instances(instances: Any, target_origin: str) -> list[dict[str, Any]]:
    if not isinstance(instances, list):
        fail("alert instances must be an array")

    safe_instances: list[dict[str, Any]] = []
    for instance in instances:
        if not isinstance(instance, dict):
            fail("alert instance must be an object")
        location = safe_location(instance.get("uri"), target_origin)
        entry: dict[str, Any] = {
            "method": safe_text(instance.get("method"), 16).upper() or "UNKNOWN",
            "parameter": safe_text(instance.get("param"), 120),
        }
        if location is not None:
            entry.update(location)
        safe_instances.append(entry)
    return safe_instances


def normalized_instance_count(alert: dict[str, Any], instance_count: int, plugin_id: str) -> int:
    declared_count = alert.get("count")
    try:
        count = int(declared_count) if declared_count is not None else instance_count
    except (TypeError, ValueError):
        fail(f"invalid instance count for rule {plugin_id!r}")
    return max(count, instance_count, 0)


def normalize_alert(
    alert: Any, target_origin: str
) -> tuple[str, str, int, dict[str, Any]]:
    if not isinstance(alert, dict):
        fail("alert entry must be an object")
    plugin_id = safe_text(alert.get("pluginid") or alert.get("alertRef"), 40) or "unknown"
    risk = normalize_risk(alert)
    if risk is None:
        fail(f"alert {plugin_id!r} has an unrecognized risk classification")
    name = safe_text(alert.get("name") or alert.get("alert"), 160) or "Unnamed ZAP alert"
    instances = alert.get("instances", []) or []
    safe_instances = normalize_instances(instances, target_origin)
    count = normalized_instance_count(alert, len(instances), plugin_id)
    safe_alert = {
        "ruleId": plugin_id,
        "name": name,
        "risk": risk,
        "confidence": safe_text(alert.get("confidence"), 40),
        "cweId": safe_text(alert.get("cweid"), 20),
        "wascId": safe_text(alert.get("wascid"), 20),
        "instanceCount": count,
        "instances": safe_instances[:25],
    }
    return risk, plugin_id, count, safe_alert


def reduce_alerts(
    sites: list[Any], target_origin: str
) -> tuple[Counter[str], Counter[str], Counter[str], list[dict[str, Any]]]:
    """Normalize ZAP alerts into deterministic, metadata-only evidence."""
    risk_counts: Counter[str] = Counter()
    rule_counts: Counter[str] = Counter()
    instance_counts: Counter[str] = Counter()
    safe_alerts: list[dict[str, Any]] = []

    for site in sites:
        for alert in site_alerts(site, target_origin):
            risk, plugin_id, count, safe_alert = normalize_alert(alert, target_origin)
            risk_counts[risk] += 1
            rule_counts[plugin_id] += 1
            instance_counts[plugin_id] += count
            safe_alerts.append(safe_alert)

    safe_alerts.sort(
        key=lambda item: (
            RISK_ORDER.index(item["risk"]) if item["risk"] in RISK_ORDER else 99,
            item["ruleId"],
            item["name"],
        )
    )
    return risk_counts, rule_counts, instance_counts, safe_alerts


def enforce_blocking_policy(scanner_exit: int, risk_counts: Counter[str]) -> None:
    """Apply the SEC-06 fail-closed scanner/high-risk blocking policy."""
    high_alerts = risk_counts.get("High", 0)
    if scanner_exit != 0 and high_alerts:
        fail(
            f"scanner exited {scanner_exit} with "
            f"{high_alerts} High-risk alert type(s); High findings are blocking"
        )
    if scanner_exit != 0:
        fail(
            f"scanner exited non-zero ({scanner_exit}); "
            "ZAP failure/timeout cannot be green"
        )
    if high_alerts:
        fail(f"{high_alerts} High-risk alert type(s) are blocking")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--raw-report", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--metadata", required=True, type=Path)
    parser.add_argument("--role", required=True)
    parser.add_argument("--target", required=True)
    parser.add_argument("--scanner-version", required=True)
    parser.add_argument("--scanner-image", required=True)
    parser.add_argument("--scanner-exit", required=True, type=int)
    parser.add_argument("--contract", required=True, type=Path)
    parser.add_argument("--automation-plan", required=True, type=Path)
    parser.add_argument("--policy", required=True, type=Path)
    parser.add_argument("--addon-list-sha256", required=True)
    return parser.parse_args()


def load_report(args: argparse.Namespace) -> tuple[str, list[Any]]:
    for path in (args.contract, args.automation_plan, args.policy):
        if not path.is_file():
            fail(f"required input is missing: {path}")
    for path in (args.automation_plan, args.policy):
        if path.stat().st_size == 0:
            fail(f"required input is empty: {path}")
    if not args.raw_report.is_file() or args.raw_report.stat().st_size == 0:
        fail("scanner reported without a non-empty JSON report")

    try:
        raw = json.loads(args.raw_report.read_text(encoding="utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        fail(f"raw ZAP report is invalid JSON: {exc}")
    if not isinstance(raw, dict):
        fail("raw ZAP report root must be an object")

    target_origin = normalize_origin(args.target)
    sites = raw.get("site", [])
    if sites is None:
        sites = []
    if not isinstance(sites, list):
        fail("raw ZAP report site field must be an array")
    if args.scanner_exit == 0 and not sites:
        fail("scanner exited successfully without scanned-site coverage")
    return target_origin, sites


def scan_status(scanner_exit: int, high_alerts: int) -> str:
    if high_alerts:
        return "blocked-high"
    if scanner_exit != 0:
        return "scanner-failed"
    return "passed"


def build_evidence(
    args: argparse.Namespace,
    target_origin: str,
    risk_counts: Counter[str],
    rule_counts: Counter[str],
    instance_counts: Counter[str],
    safe_alerts: list[dict[str, Any]],
    forbidden_values: list[str],
    allow_unsanitized: bool,
) -> dict[str, Any]:
    return {
        "schemaVersion": 1,
        "control": "SEC-06",
        "scanner": {
            "name": "OWASP ZAP",
            "version": args.scanner_version,
            "image": args.scanner_image,
            "exitCode": args.scanner_exit,
        },
        "role": args.role,
        "target": {
            "origin": target_origin,
            "isolated": True,
            "externalNetworkAccess": False,
        },
        "inputs": {
            "openApiSha256": sha256_file(args.contract),
            "automationPlanSha256": sha256_file(args.automation_plan),
            "policySha256": sha256_file(args.policy),
            "addonListSha256": args.addon_list_sha256,
        },
        "sanitization": {
            "forbiddenValueCount": len(forbidden_values),
            "unsanitizedAllowed": allow_unsanitized,
        },
        "summary": {
            "uniqueAlertsByRisk": {
                risk: risk_counts.get(risk, 0) for risk in RISK_ORDER
            },
            "uniqueAlertsByRule": dict(sorted(rule_counts.items())),
            "instancesByRule": dict(sorted(instance_counts.items())),
            "blockingHighAlerts": risk_counts.get("High", 0),
        },
        "alerts": safe_alerts,
    }


def assert_sanitized(rendered: str, forbidden_values: list[str]) -> None:
    decoded_rendered = unquote(rendered)
    for value in forbidden_values:
        escaped_value = json.dumps(value)[1:-1]
        if (
            value in rendered
            or escaped_value in rendered
            or value in decoded_rendered
            or escaped_value in decoded_rendered
        ):
            fail("sanitized evidence still contains ephemeral authentication material")


def write_evidence(
    args: argparse.Namespace,
    evidence: dict[str, Any],
    risk_counts: Counter[str],
    forbidden_values: list[str],
) -> None:
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.metadata.parent.mkdir(parents=True, exist_ok=True)
    rendered = json.dumps(evidence, indent=2, sort_keys=True) + "\n"
    assert_sanitized(rendered, forbidden_values)
    args.output.write_text(rendered, encoding="utf-8")

    metadata = {
        "control": "SEC-06",
        "role": args.role,
        "status": scan_status(args.scanner_exit, risk_counts.get("High", 0)),
        "scannerVersion": args.scanner_version,
        "scannerImage": args.scanner_image,
        "openApiSha256": evidence["inputs"]["openApiSha256"],
        "automationPlanSha256": evidence["inputs"]["automationPlanSha256"],
        "policySha256": evidence["inputs"]["policySha256"],
        "addonListSha256": args.addon_list_sha256,
        "forbiddenValueCount": len(forbidden_values),
        "unsanitizedAllowed": evidence["sanitization"]["unsanitizedAllowed"],
        "highAlerts": risk_counts.get("High", 0),
        "mediumAlerts": risk_counts.get("Medium", 0),
        "lowAlerts": risk_counts.get("Low", 0),
        "informationalAlerts": risk_counts.get("Informational", 0),
    }
    args.metadata.write_text(
        json.dumps(metadata, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )


def main() -> None:
    args = parse_args()
    forbidden_values, allow_unsanitized = load_forbidden_values()
    target_origin, sites = load_report(args)
    risk_counts, rule_counts, instance_counts, safe_alerts = reduce_alerts(
        sites, target_origin
    )
    evidence = build_evidence(
        args,
        target_origin,
        risk_counts,
        rule_counts,
        instance_counts,
        safe_alerts,
        forbidden_values,
        allow_unsanitized,
    )
    write_evidence(args, evidence, risk_counts, forbidden_values)
    enforce_blocking_policy(args.scanner_exit, risk_counts)
    print(
        "SEC-06 ZAP report accepted: "
        f"role={args.role} high={risk_counts.get('High', 0)} "
        f"medium={risk_counts.get('Medium', 0)} low={risk_counts.get('Low', 0)} "
        f"info={risk_counts.get('Informational', 0)}"
    )


if __name__ == "__main__":
    main()
