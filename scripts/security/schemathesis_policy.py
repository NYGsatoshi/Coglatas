#!/usr/bin/env python3
"""SEC-04 response-disclosure policy shared by Schemathesis hooks and tests."""

from __future__ import annotations

import hashlib
import json
import re
from collections.abc import Iterable
from uuid import UUID

_DISCLOSURE_PATTERNS: tuple[tuple[str, re.Pattern[str]], ...] = (
    ("dotnet-exception", re.compile(r"\b(?:System\.)?[A-Z][A-Za-z0-9_.`+]*Exception\b")),
    ("dotnet-stack", re.compile(r"(?m)^\s*at\s+[A-Za-z0-9_.`+<>]+\([^\n]*\)(?:\s+in\s+[^\n]+:line\s+\d+)?\s*$")),
    ("python-traceback", re.compile(r"Traceback \(most recent call last\):")),
    ("ef-core-internal", re.compile(r"\bMicrosoft\.EntityFrameworkCore(?:\.|\b)")),
    ("npgsql-internal", re.compile(r"\bNpgsql(?:\.|Exception\b)")),
    ("sqlstate", re.compile(r"\bSQLSTATE\b", re.IGNORECASE)),
    ("stack-trace-field", re.compile(r'(?i)["\']stack(?:trace)?["\']\s*:')),
)


def disclosure_reason(content: bytes, forbidden_values: Iterable[str] = ()) -> str | None:
    """Return a non-secret reason when a response discloses internals or auth material."""

    text = content.decode("utf-8", errors="replace")
    for value in forbidden_values:
        if len(value) >= 8 and value in text:
            return "ephemeral-auth-material"

    for name, pattern in _DISCLOSURE_PATTERNS:
        if pattern.search(text):
            return name
    return None


class IntegrationInputAttribution:
    """Attribute only proven integration names and Workspace icons in this scanner scope.

    Successful creations provide the ID/value proof for later collection reads.
    Proof is bounded, process-local and hashed; unknown input remains blocking.
    Authentication material is never eligible for attribution.
    """

    def __init__(self, capacity: int = 4096):
        self._capacity = capacity
        self._created: set[tuple[str, str, str, str, str, bytes]] = set()

    @staticmethod
    def _unique_object(pairs):
        document = {}
        for key, value in pairs:
            if key in document:
                raise ValueError("Duplicate JSON field cannot establish input provenance")
            document[key] = value
        return document

    @staticmethod
    def _key(row, scope, field):
        if not isinstance(row, dict) or not isinstance(row.get(field), str):
            return None
        identity = row.get("id")
        if not isinstance(identity, str):
            return None
        try:
            identifier = UUID(identity)
        except ValueError:
            return None
        if identifier.int == 0 or str(identifier) != identity:
            return None
        name_bytes = row[field].encode("utf-8", errors="surrogatepass")
        return (*scope, identity, hashlib.sha256(name_bytes).digest())

    @staticmethod
    def _json_reason(document, forbidden):
        encoded = json.dumps(document, ensure_ascii=False).encode("utf-8", errors="surrogatepass")
        reason = disclosure_reason(encoded, forbidden)
        if reason is not None:
            return reason
        # Inspect decoded scalar text too: JSON escaping must not conceal a
        # multiline stack or Unicode-escaped diagnostic inside another field.
        pending = [document]
        while pending:
            value = pending.pop()
            if isinstance(value, dict):
                pending.extend(value.values())
            elif isinstance(value, list):
                pending.extend(value)
            elif isinstance(value, str):
                reason = disclosure_reason(value.encode("utf-8", errors="surrogatepass"), forbidden)
                if reason is not None:
                    return reason
        return None

    @staticmethod
    def _target(document, path, method, status):
        if path == "/api/tenant/integrations" and status == 200:
            return document, "displayName"
        if path == "/api/workspaces":
            if method == "POST" and status == 201 and isinstance(document, dict):
                return document.get("data"), "icon"
            if method == "GET" and status == 200:
                return document, "icon"
        return None, None

    def reason(self, content, forbidden, *, role, tenant, path, method, status, request_body):
        forbidden = tuple(forbidden)
        original_reason = disclosure_reason(content, forbidden)
        if original_reason == "ephemeral-auth-material":
            return original_reason
        try:
            document = json.loads(content, object_pairs_hook=self._unique_object)
        except (ValueError, UnicodeError, RecursionError):
            return original_reason
        decoded = json.dumps(document, ensure_ascii=False).encode("utf-8", errors="surrogatepass")
        if disclosure_reason(decoded, forbidden) == "ephemeral-auth-material":
            return "ephemeral-auth-material"

        scope_valid = all(isinstance(value, str) and value for value in (role, tenant))
        target, field = self._target(document, path, method, status)
        if not scope_valid or field is None:
            return original_reason or self._json_reason(document, forbidden)
        # A matching ID/value in another resource family cannot establish proof.
        scope = (role, tenant, path, field)
        created_key = None
        if method == "POST" and isinstance(request_body, dict):
            key = self._key(target, scope, field)
            value = request_body.get(field)
            if key is not None and isinstance(value, str) and value == target[field]:
                created_key = key
                target[field] = ""
        elif method == "GET" and isinstance(target, list):
            for row in target:
                key = self._key(row, scope, field)
                if key is not None and key in self._created:
                    row[field] = ""

        reason = self._json_reason(document, forbidden)
        if reason is None and created_key is not None and len(self._created) < self._capacity:
            self._created.add(created_key)
        return reason
