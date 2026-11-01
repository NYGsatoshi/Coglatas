#!/usr/bin/env python3
"""Schemathesis hooks for SEC-04 isolated contract fuzzing."""

from __future__ import annotations

import json
import os
import sys
from functools import lru_cache
from pathlib import Path
from types import MappingProxyType
from typing import Any

import schemathesis

sys.path.insert(0, str(Path(__file__).resolve().parent))
from schemathesis_policy import IntegrationInputAttribution  # noqa: E402

_AUTH_FILE_ENV = "COGLATAS_SECURITY_SCHEMATHESIS_AUTH_FILE"
_EVIDENCE_FILE_ENV = "COGLATAS_SECURITY_SCHEMATHESIS_EVIDENCE_FILE"
_ROLE_ENV = "COGLATAS_SECURITY_SCHEMATHESIS_ROLE"
_STRUCTURED_JSON_MEDIA_RANGE = "application/*+json"
_STRUCTURED_JSON_EXAMPLE = "application/vnd.coglatas+json"
_integration_inputs = IntegrationInputAttribution()


@lru_cache(maxsize=1)
def _read_role_auth(path: str) -> dict[str, Any]:
    """Validate one immutable SEC-03 role fixture in this scanner process.

    SEC-04 executes each role in its own one-worker scanner container. Its
    authenticated scanner never performs login/logout/password rotation; SEC-03
    owns that lifecycle. Never persist these short-lived credentials to a
    global/multi-run cache or expose their values in CI logs.
    """
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
    # Never allow request hooks or checks to mutate the shared frozen fixture.
    return {**payload, "headers": MappingProxyType(dict(headers)), "forbidden_values": tuple(forbidden)}


def _load_auth() -> dict[str, Any]:
    path = os.environ.get(_AUTH_FILE_ENV)
    if not path:
        raise RuntimeError(f"{_AUTH_FILE_ENV} is required")
    return _read_role_auth(path)


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
    reason = _integration_inputs.reason(
        response.content,
        auth["forbidden_values"],
        role=auth.get("role"),
        tenant=auth.get("tenant"),
        path=case.operation.path,
        method=case.method,
        status=response.status_code,
        request_body=case.body,
    )
    if reason is not None:
        raise AssertionError(f"SEC-04 response disclosure policy violation: {reason}")
