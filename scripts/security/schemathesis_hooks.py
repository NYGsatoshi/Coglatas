#!/usr/bin/env python3
"""Schemathesis hooks for SEC-04 isolated contract fuzzing."""

from __future__ import annotations

import json
import os
import sys
from pathlib import Path
from typing import Any

import schemathesis

sys.path.insert(0, str(Path(__file__).resolve().parent))
from schemathesis_policy import disclosure_reason  # noqa: E402

_AUTH_FILE_ENV = "COGLATAS_SECURITY_SCHEMATHESIS_AUTH_FILE"
_EVIDENCE_FILE_ENV = "COGLATAS_SECURITY_SCHEMATHESIS_EVIDENCE_FILE"
_ROLE_ENV = "COGLATAS_SECURITY_SCHEMATHESIS_ROLE"
_STRUCTURED_JSON_MEDIA_RANGE = "application/*+json"
_STRUCTURED_JSON_EXAMPLE = "application/vnd.coglatas+json"


def _load_auth() -> dict[str, Any]:
    path = os.environ.get(_AUTH_FILE_ENV)
    if not path:
        raise RuntimeError(f"{_AUTH_FILE_ENV} is required")
    with open(path, encoding="utf-8") as handle:
        payload = json.load(handle)
    if not isinstance(payload, dict):
        raise RuntimeError("SEC-04 auth payload must be an object")
    headers = payload.get("headers")
    forbidden = payload.get("forbidden_values")
    if not isinstance(headers, dict) or not all(isinstance(k, str) and isinstance(v, str) for k, v in headers.items()):
        raise RuntimeError("SEC-04 auth headers are invalid")
    if not isinstance(forbidden, list) or not all(isinstance(item, str) for item in forbidden):
        raise RuntimeError("SEC-04 forbidden value list is invalid")
    return payload


def _append_evidence(event: dict[str, Any]) -> None:
    path = os.environ.get(_EVIDENCE_FILE_ENV)
    if not path:
        raise RuntimeError(f"{_EVIDENCE_FILE_ENV} is required")
    event = {"role": os.environ.get(_ROLE_ENV, "unknown"), **event}
    with open(path, "a", encoding="utf-8") as handle:
        handle.write(json.dumps(event, separators=(",", ":"), sort_keys=True))
        handle.write("\n")


@schemathesis.hook
def before_call(ctx, case, kwargs):
    auth = _load_auth()
    # OpenAPI media ranges describe a family, but Schemathesis 4.25.2 sends the
    # wildcard literally as Content-Type. Exercise the same family with a
    # concrete vendor subtype that ASP.NET Core can negotiate.
    if case.media_type == _STRUCTURED_JSON_MEDIA_RANGE:
        case.media_type = _STRUCTURED_JSON_EXAMPLE
    headers = dict(case.headers or {})
    headers.update(auth["headers"])
    case.headers = headers


@schemathesis.hook
def after_call(ctx, case, response):
    _append_evidence(
        {
            "event": "response",
            "operation": case.operation.label,
            "method": case.method,
            "status": response.status_code,
        }
    )


@schemathesis.hook
def after_network_error(ctx, case, request):
    _append_evidence(
        {
            "event": "network_error",
            "operation": case.operation.label,
            "method": case.method,
        }
    )


@schemathesis.check
def no_sensitive_internal_error_disclosure(ctx, response, case):
    auth = _load_auth()
    reason = disclosure_reason(response.content, auth["forbidden_values"])
    if reason is not None:
        raise AssertionError(f"SEC-04 response disclosure policy violation: {reason}")
