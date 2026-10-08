#!/usr/bin/env python3
"""Replay SEC-04's stored input false positive through the actual pinned hook."""

from __future__ import annotations

import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parent))
import schemathesis_hooks as hooks  # noqa: E402

CONTRACT_PATH = Path(sys.argv.pop(1))


class IntegrationInputAttributionTests(unittest.TestCase):
    integration_id = "bd39c655-1d53-4d9c-a7f1-2a3ccaa3b020"
    markers = (
        "sqlstate",
        "System.InvalidOperationException",
        "Npgsql.PostgresException SQLSTATE 23505",
        "Microsoft.EntityFrameworkCore.Database",
        "Traceback (most recent call last):",
        "at System.Service.Execute()",
    )

    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.auth_file = Path(self.directory.name) / "auth.json"
        os.environ["COGLATAS_SECURITY_SCHEMATHESIS_AUTH_FILE"] = str(self.auth_file)
        self.set_scope("alpha-owner", "security-alpha")
        if hasattr(hooks, "_integration_inputs"):
            hooks._integration_inputs = hooks.IntegrationInputAttribution()

    def set_scope(self, role, tenant):
        self.auth_file.write_text(json.dumps({
            "role": role,
            "tenant": tenant,
            "headers": {},
            "forbidden_values": ["csrf-canary-123456"],
        }), encoding="utf-8")

    def row(self, name="sqlstate", **extra):
        return {"id": self.integration_id, "displayName": name, "provider": 474, **extra}

    def check(self, response_body, request_body=None, method="POST", status=200,
              path="/api/tenant/integrations"):
        content = response_body if isinstance(response_body, bytes) else json.dumps(response_body).encode()
        response = SimpleNamespace(content=content, status_code=status)
        case = SimpleNamespace(method=method, body=request_body, operation=SimpleNamespace(path=path))
        hooks.no_sensitive_internal_error_disclosure(None, response, case)

    def seed(self, name="sqlstate"):
        self.check(self.row(name), {"displayName": name})

    def test_recorded_post_input_is_not_a_server_diagnostic(self):
        self.seed()

    def test_recorded_get_matches_only_successfully_created_input(self):
        self.seed()
        self.check([self.row()], method="GET")

    def test_other_diagnostic_markers_can_be_exact_input(self):
        for marker in self.markers:
            with self.subTest(marker=marker):
                self.seed(marker)
                self.check([self.row(marker)], method="GET")

    def test_unproven_get_disclosure_is_blocking(self):
        with self.assertRaisesRegex(AssertionError, "sqlstate"):
            self.check([self.row()], method="GET")

    def test_post_requires_exact_request_value(self):
        with self.assertRaisesRegex(AssertionError, "sqlstate"):
            self.check(self.row(), {"displayName": "ordinary name"})

    def test_post_requires_a_string_input(self):
        for body in (None, [], {"displayName": None}, {"displayName": 42}):
            with self.subTest(body=body), self.assertRaisesRegex(AssertionError, "sqlstate"):
                self.check(self.row(), body)

    def test_error_field_remains_blocking_alongside_proven_input(self):
        for marker in self.markers:
            with self.subTest(marker=marker), self.assertRaises(AssertionError):
                self.check(self.row(error=marker), {"displayName": "sqlstate"})

    def test_stack_field_remains_blocking(self):
        with self.assertRaisesRegex(AssertionError, "stack-trace-field"):
            self.check(self.row(stackTrace="private stack"), {"displayName": "sqlstate"})

    def test_failed_creation_does_not_prove_future_reads(self):
        with self.assertRaises(AssertionError):
            self.check(self.row(error="Npgsql.PostgresException"), {"displayName": "sqlstate"})
        with self.assertRaises(AssertionError):
            self.check([self.row()], method="GET")

    def test_unsuccessful_status_cannot_attribute_input(self):
        for status in (201, 400, 401, 403, 404, 500):
            with self.subTest(status=status), self.assertRaises(AssertionError):
                self.check(self.row(), {"displayName": "sqlstate"}, status=status)

    def test_different_route_and_method_remain_blocking(self):
        for path, method in (("/api/tenant/integrations/" + self.integration_id, "POST"),
                             ("/api/projects", "POST"), ("/api/tenant/integrations", "PATCH")):
            with self.subTest(path=path, method=method), self.assertRaises(AssertionError):
                self.check(self.row(), {"displayName": "sqlstate"}, path=path, method=method)

    def test_get_requires_exact_created_identity_and_value(self):
        self.seed()
        for row in (self.row(id="b370405d-4a9f-426f-8155-beea721ba419"),
                    self.row("sqlstate changed")):
            with self.subTest(row=row), self.assertRaises(AssertionError):
                self.check([row], method="GET")

    def test_other_row_and_field_disclosures_remain_blocking(self):
        self.seed()
        for rows in ([self.row(), {"error": "SQLSTATE 23505"}],
                     [self.row(settingsJson="SQLSTATE 23505")]):
            with self.subTest(rows=rows), self.assertRaises(AssertionError):
                self.check(rows, method="GET")

    def test_role_and_tenant_changes_cannot_reuse_input_proof(self):
        self.seed()
        for role, tenant in (("beta-owner", "security-beta"),
                             ("alpha-restricted", "security-alpha"),
                             ("alpha-owner", "security-beta")):
            self.set_scope(role, tenant)
            with self.subTest(role=role, tenant=tenant), self.assertRaises(AssertionError):
                self.check([self.row()], method="GET")

    def test_missing_scope_cannot_attribute_input(self):
        for role, tenant in ((None, "security-alpha"), ("alpha-owner", None), ("", "")):
            self.set_scope(role, tenant)
            with self.subTest(role=role, tenant=tenant), self.assertRaises(AssertionError):
                self.seed()

    def test_invalid_identity_cannot_attribute_input(self):
        for identity in (None, "", "not-a-uuid", "00000000-0000-0000-0000-000000000000"):
            with self.subTest(identity=identity), self.assertRaises(AssertionError):
                self.check(self.row(id=identity), {"displayName": "sqlstate"})

    def test_auth_material_is_blocking_even_as_exact_input(self):
        with self.assertRaisesRegex(AssertionError, "ephemeral-auth-material"):
            self.seed("csrf-canary-123456")

    def test_auth_material_in_other_field_is_blocking(self):
        with self.assertRaisesRegex(AssertionError, "ephemeral-auth-material"):
            self.check(self.row(error="csrf-canary-123456"), {"displayName": "sqlstate"})

    def test_malformed_and_non_json_responses_remain_blocking(self):
        for content in (b'SQLSTATE 23505', b'{"displayName":"sqlstate",'):
            with self.subTest(content=content), self.assertRaises(AssertionError):
                self.check(content, {"displayName": "sqlstate"})

    def test_response_shapes_cannot_move_input_to_a_diagnostic_field(self):
        for response in ({"error": "sqlstate"}, {"data": self.row()}, [self.row()]):
            with self.subTest(response=response), self.assertRaises(AssertionError):
                self.check(response, {"displayName": "sqlstate"})

    def test_escaped_auth_material_remains_blocking(self):
        content = json.dumps(self.row("csrf-canary-123456")).replace("csrf", "\\u0063srf").encode()
        with self.assertRaisesRegex(AssertionError, "ephemeral-auth-material"):
            self.check(content, {"displayName": "csrf-canary-123456"})

    def test_multiline_and_escaped_diagnostics_remain_blocking(self):
        for error in ("at System.Service.Execute()\n", "System.InvalidOperationException"):
            content = json.dumps(self.row(error=error)).replace("System", "\\u0053ystem").encode()
            with self.subTest(error=error), self.assertRaises(AssertionError):
                self.check(content, {"displayName": "sqlstate"})

    def test_capacity_does_not_allow_unrecorded_get(self):
        hooks._integration_inputs = hooks.IntegrationInputAttribution(capacity=1)
        self.seed()
        second = self.row(id="b370405d-4a9f-426f-8155-beea721ba419")
        self.check(second, {"displayName": "sqlstate"})
        self.check([self.row()], method="GET")
        with self.assertRaises(AssertionError):
            self.check([second], method="GET")

    def test_attribution_does_not_mutate_request_or_response(self):
        request = {"displayName": "sqlstate"}
        response = self.row()
        self.check(response, request)
        self.assertEqual(request["displayName"], "sqlstate")
        self.assertEqual(response["displayName"], "sqlstate")

    def test_json_surrogates_do_not_crash_attribution(self):
        self.seed("sqlstate\ud800")
        self.check([self.row("sqlstate\ud800")], method="GET")

    def test_duplicate_json_fields_cannot_hide_diagnostics(self):
        row_json = json.dumps(self.row())[:-1]
        content = (row_json + ',"error":"Npgsql.PostgresException","error":"safe"}').encode()
        with self.assertRaises(AssertionError):
            self.check(content, {"displayName": "sqlstate"})

    def test_generated_contract_uses_actual_case_metadata(self):
        document = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        operations = hooks.schemathesis.openapi.from_dict(document)["/api/tenant/integrations"]
        post = operations["POST"].Case(
            body={"displayName": "sqlstate", "provider": 474}, media_type="application/json")
        response = SimpleNamespace(content=json.dumps(self.row()).encode(), status_code=200)
        hooks.no_sensitive_internal_error_disclosure(None, response, post)
        get = operations["GET"].Case()
        response.content = json.dumps([self.row()]).encode()
        hooks.no_sensitive_internal_error_disclosure(None, response, get)


if __name__ == "__main__":
    unittest.main()
