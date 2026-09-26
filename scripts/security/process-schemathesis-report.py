#!/usr/bin/env python3
"""Sanitize and validate SEC-04 Schemathesis NDJSON evidence."""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import re
import tempfile
from pathlib import Path
from typing import Any

EXPECTED_VERSION = "4.25.2"
SENSITIVE_KEY = re.compile(
    r"(?:token|secret|password|passwd|cookie|session|csrf|credential)"
    r"|(?:^|[-_])auth(?:orization)?(?:$|[-_])"
    r"|private[-_]?key|api[-_]?key",
    re.IGNORECASE,
)


def fail(message: str) -> None:
    raise SystemExit(f"SEC-04 Schemathesis evidence failed: {message}")


def load_json(path: Path) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        fail(f"invalid JSON in {path.name}: {exc}")
    if not isinstance(value, dict):
        fail(f"{path.name} must contain an object")
    return value


def secret_literals(auth: dict[str, Any]) -> set[str]:
    values = {os.environ.get("COGLATAS_SECURITY_CI_PASSWORD", "")}
    for value in auth.get("forbidden_values", []):
        if isinstance(value, str):
            values.add(value)
    headers = auth.get("headers", {})
    if isinstance(headers, dict):
        values.update(
            value
            for key, value in headers.items()
            if isinstance(value, str) and SENSITIVE_KEY.search(str(key))
        )
    values.discard("")
    return {value for value in values if len(value) >= 4}


def replace_literals(value: str, secrets: set[str]) -> str:
    result = value
    for secret in sorted(secrets, key=len, reverse=True):
        result = result.replace(secret, "[REDACTED]")
        encoded = base64.b64encode(secret.encode("utf-8")).decode("ascii")
        result = result.replace(encoded, "[REDACTED_BASE64]")
    return result


def sanitize(value: Any, secrets: set[str]) -> Any:
    if isinstance(value, dict):
        output: dict[str, Any] = {}
        is_response = {"status_code", "headers", "content", "elapsed"}.issubset(value)
        for key, item in value.items():
            key_text = str(key)
            if SENSITIVE_KEY.search(key_text):
                output[key_text] = "[REDACTED]"
            elif key_text == "content" and is_response:
                output[key_text] = "[REDACTED_RESPONSE_CONTENT]"
            else:
                output[key_text] = sanitize(item, secrets)
        return output
    if isinstance(value, list):
        return [sanitize(item, secrets) for item in value]
    if isinstance(value, str):
        return replace_literals(value, secrets)
    return value


def parse_report(raw_report: Path, output: Path, secrets: set[str], expected_seed: int) -> tuple[int, str]:
    if not raw_report.is_file() or raw_report.stat().st_size == 0:
        fail("raw NDJSON report is missing or empty")

    initialize_count = 0
    observed_version = ""
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile("w", encoding="utf-8", dir=output.parent, delete=False) as temp:
        temp_path = Path(temp.name)
        try:
            with raw_report.open(encoding="utf-8") as source:
                for line_number, line in enumerate(source, start=1):
                    try:
                        event = json.loads(line)
                    except json.JSONDecodeError as exc:
                        fail(f"invalid NDJSON at line {line_number}: {exc}")
                    if not isinstance(event, dict):
                        fail(f"NDJSON line {line_number} is not an object")
                    initialize = event.get("Initialize")
                    if initialize is not None:
                        initialize_count += 1
                        if not isinstance(initialize, dict):
                            fail("Initialize event is not an object")
                        observed_version = str(initialize.get("schemathesis_version", ""))
                        if observed_version != EXPECTED_VERSION:
                            fail(f"expected Schemathesis {EXPECTED_VERSION}, got {observed_version!r}")
                        if initialize.get("seed") != expected_seed:
                            fail(f"report seed {initialize.get('seed')!r} does not match expected {expected_seed}")
                    safe = sanitize(event, secrets)
                    temp.write(json.dumps(safe, separators=(",", ":"), sort_keys=True))
                    temp.write("\n")
        except BaseException:
            temp_path.unlink(missing_ok=True)
            raise
    if initialize_count != 1:
        temp_path.unlink(missing_ok=True)
        fail(f"expected exactly one Initialize event, got {initialize_count}")

    safe_text = temp_path.read_text(encoding="utf-8")
    for secret in secrets:
        if secret in safe_text or base64.b64encode(secret.encode("utf-8")).decode("ascii") in safe_text:
            temp_path.unlink(missing_ok=True)
            fail("sanitized report still contains ephemeral authentication material")
    temp_path.replace(output)
    return initialize_count, observed_version


def parse_evidence(path: Path, expected_role: str) -> tuple[int, int, list[str]]:
    if not path.is_file() or path.stat().st_size == 0:
        fail(f"scanner produced no execution evidence for role {expected_role}")
    responses = 0
    network_errors = 0
    operations: set[str] = set()
    with path.open(encoding="utf-8") as handle:
        for line_number, line in enumerate(handle, start=1):
            try:
                event = json.loads(line)
            except json.JSONDecodeError as exc:
                fail(f"invalid execution evidence at line {line_number}: {exc}")
            if not isinstance(event, dict) or event.get("role") != expected_role:
                fail(f"execution evidence role mismatch at line {line_number}")
            kind = event.get("event")
            if kind == "response":
                responses += 1
            elif kind == "network_error":
                network_errors += 1
            else:
                fail(f"unknown execution evidence event {kind!r}")
            operation = event.get("operation")
            if isinstance(operation, str) and operation:
                operations.add(operation)
    if responses == 0:
        fail(f"scanner sent no successful HTTP request for role {expected_role}")
    if network_errors:
        fail(f"scanner observed {network_errors} network errors for role {expected_role}")
    return responses, network_errors, sorted(operations)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--raw-report", required=True, type=Path)
    parser.add_argument("--evidence", required=True, type=Path)
    parser.add_argument("--auth-file", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--metadata", required=True, type=Path)
    parser.add_argument("--role", required=True)
    parser.add_argument("--lane", required=True, choices=("pr", "deep"))
    parser.add_argument("--seed", required=True, type=int)
    parser.add_argument("--contract", required=True, type=Path)
    parser.add_argument("--scanner-exit", required=True, type=int)
    args = parser.parse_args()

    auth = load_json(args.auth_file)
    if auth.get("role") != args.role:
        fail("auth file role does not match scanner role")
    secrets = secret_literals(auth)
    _, version = parse_report(args.raw_report, args.output, secrets, args.seed)
    responses, network_errors, operations = parse_evidence(args.evidence, args.role)

    if not args.contract.is_file():
        fail("authoritative OpenAPI contract disappeared before evidence processing")
    contract_sha256 = hashlib.sha256(args.contract.read_bytes()).hexdigest()

    metadata = {
        "contract_sha256": contract_sha256,
        "lane": args.lane,
        "network_errors": network_errors,
        "operation_count": len(operations),
        "operations": operations,
        "request_count": responses,
        "role": args.role,
        "scanner_exit": args.scanner_exit,
        "schemathesis_version": version,
        "seed": args.seed,
    }
    args.metadata.parent.mkdir(parents=True, exist_ok=True)
    args.metadata.write_text(json.dumps(metadata, indent=2, sort_keys=True) + "\n", encoding="utf-8")

    print(
        f"SEC-04 evidence verified: role={args.role} seed={args.seed} "
        f"requests={responses} operations={len(operations)} scanner_exit={args.scanner_exit}"
    )


if __name__ == "__main__":
    main()
