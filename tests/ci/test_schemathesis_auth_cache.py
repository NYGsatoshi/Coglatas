#!/usr/bin/env python3
"""SEC-04 role-local auth cache controls without a network or scanner Docker."""
from __future__ import annotations

import importlib.util
import json
import os
import stat
import sys
import tempfile
import types
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
HOOKS = ROOT / "scripts/security/schemathesis_hooks.py"
AUTH_ENV = "COGLATAS_SECURITY_SCHEMATHESIS_AUTH_FILE"
EVIDENCE_ENV = "COGLATAS_SECURITY_SCHEMATHESIS_EVIDENCE_FILE"
ROLE_ENV = "COGLATAS_SECURITY_SCHEMATHESIS_ROLE"


def load_hooks():
    """Import real hook logic under inert decorator stubs, without HTTP I/O."""
    fake = types.ModuleType("schemathesis")
    fake.hook = lambda fn: fn
    fake.check = lambda fn: fn
    spec = importlib.util.spec_from_file_location("_sec04_cache_qualification", HOOKS)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    with patch.dict(sys.modules, {"schemathesis": fake}):
        spec.loader.exec_module(module)
    return module


class RoleAuthCacheTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.auth = self.root / "auth.json"
        self.evidence = self.root / "evidence.ndjson"
        # A public, noncredential canary exercises the same sensitive-value
        # detection policy without writing an auth token to a test fixture.
        self.canary = "public-sec04-disclosure-canary-123456"
        self.write_auth(self.auth, "alpha-owner")
        self.environment = patch.dict(os.environ, {
            AUTH_ENV: str(self.auth), EVIDENCE_ENV: str(self.evidence), ROLE_ENV: "alpha-owner"
        })
        self.environment.start()
        self.addCleanup(self.environment.stop)
        self.hooks = load_hooks()

    @staticmethod
    def write_auth(path, role):
        """Use role labels and a documented public marker, never test credentials."""
        payload = {
            "role": role, "tenant": "security-alpha",
            "headers": {"X-Tenant-Slug": "security-alpha", "X-Synthetic-Role": role},
            "forbidden_values": ["public-sec04-disclosure-canary-123456"],
        }
        # Test files are owner-only before any content is written.
        flags = os.O_CREAT | os.O_TRUNC | os.O_WRONLY | getattr(os, "O_NOFOLLOW", 0)
        fd = os.open(path, flags, stat.S_IRUSR | stat.S_IWUSR)
        with os.fdopen(fd, "w", encoding="utf-8") as handle:
            os.fchmod(handle.fileno(), stat.S_IRUSR | stat.S_IWUSR)
            json.dump(payload, handle)

    @staticmethod
    def case():
        return types.SimpleNamespace(
            media_type="application/*+json", headers={"X-Original": "preserved"},
            method="GET", body=None,
            operation=types.SimpleNamespace(label="GET /api/auth/me", path="/api/auth/me"),
        )

    def test_repeated_hooks_parse_the_same_role_file_only_once(self):
        response = types.SimpleNamespace(status_code=200, content=b'{"ok":true}')
        for _ in range(250):
            case = self.case()
            self.hooks.before_call(None, case, {})
            self.assertEqual("application/vnd.coglatas+json", case.media_type)
            self.assertEqual("preserved", case.headers["X-Original"])
            self.assertEqual("alpha-owner", case.headers["X-Synthetic-Role"])
            self.hooks.no_sensitive_internal_error_disclosure(None, response, case)
        self.assertEqual(1, self.hooks._read_role_auth.cache_info().misses)
        self.assertEqual(499, self.hooks._read_role_auth.cache_info().hits)

    def test_cache_is_role_path_scoped_and_readonly(self):
        first = self.hooks._load_auth()
        with self.assertRaises(TypeError):
            first["headers"]["X-Synthetic-Role"] = "tampered"
        with self.assertRaises(AttributeError):
            first["forbidden_values"].append("tampered")
        second_path = self.root / "beta.json"
        self.write_auth(second_path, "beta-owner")
        with patch.dict(os.environ, {AUTH_ENV: str(second_path), ROLE_ENV: "beta-owner"}):
            second = self.hooks._load_auth()
            self.assertEqual("beta-owner", second["role"])
            self.assertEqual("beta-owner", second["headers"]["X-Synthetic-Role"])
        self.assertEqual(2, self.hooks._read_role_auth.cache_info().misses)
        # Switching paths back must never substitute the previous role.
        self.assertEqual("alpha-owner", self.hooks._load_auth()["headers"]["X-Synthetic-Role"])
        self.assertEqual(3, self.hooks._read_role_auth.cache_info().misses)

    def test_same_file_role_and_tenant_replacements_invalidate_cached_auth(self):
        """The native SEC-04 role/Tenant negative replay rewrites this same path."""
        first = self.hooks._load_auth()
        self.assertEqual("alpha-owner", first["role"])
        self.write_auth(self.auth, "beta-owner")
        second = self.hooks._load_auth()
        self.assertEqual("beta-owner", second["role"])
        self.assertNotEqual(first["headers"]["X-Synthetic-Role"], second["headers"]["X-Synthetic-Role"])
        self.assertEqual(2, self.hooks._read_role_auth.cache_info().misses)
        self.assertEqual(0o600, stat.S_IMODE(self.auth.stat().st_mode))

        # Retain role input and change just the tenant at the original path.
        payload = json.loads(self.auth.read_text())
        payload["tenant"] = "security-beta"
        self.auth.write_text(json.dumps(payload), encoding="utf-8")
        third = self.hooks._load_auth()
        self.assertEqual("security-beta", third["tenant"])
        self.assertEqual(3, self.hooks._read_role_auth.cache_info().misses)

    def test_missing_malformed_and_unset_auth_fail_closed(self):
        with patch.dict(os.environ, {AUTH_ENV: ""}):
            with self.assertRaises(RuntimeError):
                self.hooks._load_auth()
        # Cache a previously accepted file, then reject its invalid replacement.
        self.hooks._load_auth()
        self.auth.write_text("{not-json", encoding="utf-8")
        with self.assertRaises(json.JSONDecodeError):
            self.hooks._load_auth()
        self.auth.unlink()
        with self.assertRaises(FileNotFoundError):
            self.hooks._load_auth()

    def test_sensitive_response_still_fails_without_leaking_auth(self):
        case = self.case()
        response = types.SimpleNamespace(
            status_code=200, content=('{"message":"' + self.canary + '"}').encode()
        )
        self.hooks.before_call(None, case, {})
        with self.assertRaises(AssertionError) as err:
            self.hooks.no_sensitive_internal_error_disclosure(None, response, case)
        self.assertIn("ephemeral-auth-material", str(err.exception))
        self.assertNotIn(self.canary, str(err.exception))

    def test_all_response_and_network_evidence_remains_append_only(self):
        case = self.case()
        self.hooks.after_call(None, case, types.SimpleNamespace(status_code=403))
        self.hooks.after_network_error(None, case, None)
        events = [json.loads(x) for x in self.evidence.read_text().splitlines()]
        self.assertEqual(["response", "network_error"], [x["event"] for x in events])
        self.assertEqual(["alpha-owner", "alpha-owner"], [x["role"] for x in events])
        self.assertEqual("GET /api/auth/me", events[0]["operation"])
        self.assertEqual(403, events[0]["status"])
        self.assertNotIn(self.canary, self.evidence.read_text())


if __name__ == "__main__":
    unittest.main()
