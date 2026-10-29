"""Exercise proven Workspace icon attribution through the real SEC-04 policy."""
import copy
import json
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "security"))
from schemathesis_policy import IntegrationInputAttribution


class WorkspaceIconPolicyTests(unittest.TestCase):
    identity = "f922f81c-a79e-4900-8d56-2c09b7efa5cb"
    markers = ("sqlstate", "System.InvalidOperationException", "Npgsql.PostgresException",
               "Microsoft.EntityFrameworkCore.Database", "Traceback (most recent call last):", "at System.Service.Execute()")

    def setUp(self):
        self.policy = IntegrationInputAttribution()
        self.scope = {"role": "alpha-owner", "tenant": "security-alpha"}

    def row(self, icon="sqlstate", **extra):
        return {"id": self.identity, "name": "owned synthetic Workspace", "description": None, "icon": icon, **extra}

    def check(self, document, request=None, method="POST", status=201, path="/api/workspaces", forbidden=()):
        content = document if isinstance(document, bytes) else json.dumps(document).encode()
        return self.policy.reason(content, forbidden, request_body=request, method=method, status=status, path=path, **self.scope)

    def seed(self, icon="sqlstate"):
        self.assertIsNone(self.check({"data": self.row(icon)}, {"icon": icon}))

    def test_recorded_icon_post_and_later_collection(self):
        self.seed()
        self.assertIsNone(self.check([self.row()], method="GET", status=200))

    def test_exact_other_markers_are_input_not_generated_errors(self):
        for marker in self.markers:
            with self.subTest(marker=marker):
                self.seed(marker)
                self.assertIsNone(self.check([self.row(marker)], method="GET", status=200))

    def test_ordinary_input_remains_valid(self):
        self.seed("ordinary-icon")
        self.assertIsNone(self.check([self.row("ordinary-icon")], method="GET", status=200))

    def test_unproven_collection_is_blocked(self):
        self.assertEqual(self.check([self.row()], method="GET", status=200), "sqlstate")

    def test_post_requires_exact_string_input_without_trim_approximation(self):
        for request in (None, [], {}, {"icon": None}, {"icon": 1}, {"icon": "different"}, {"icon": " sqlstate "}):
            with self.subTest(request=request):
                self.assertEqual(self.check({"data": self.row()}, request), "sqlstate")

    def test_post_requires_canonical_nonempty_identity(self):
        for identity in (None, 1, "invalid", "00000000-0000-0000-0000-000000000000", self.identity.upper()):
            with self.subTest(identity=identity):
                self.assertEqual(self.check({"data": self.row(id=identity)}, {"icon": "sqlstate"}), "sqlstate")

    def test_changed_identity_or_icon_never_reuses_proof(self):
        self.seed()
        for row in (self.row(id="bd39c655-1d53-4d9c-a7f1-2a3ccaa3b020"), self.row("SQLSTATE changed")):
            self.assertEqual(self.check([row], method="GET", status=200), "sqlstate")

    def test_role_and_tenant_are_separate_scopes(self):
        self.seed()
        for scope in ({"role": "beta-owner", "tenant": "security-alpha"}, {"role": "alpha-owner", "tenant": "security-beta"}, {"role": "", "tenant": "security-alpha"}):
            self.scope = scope
            self.assertEqual(self.check([self.row()], method="GET", status=200), "sqlstate")

    def test_other_routes_methods_and_statuses_stay_blocking(self):
        self.seed()
        for path, method, status, document in (("/api/projects", "POST", 201, {"data": self.row()}), ("/api/workspaces/" + self.identity, "GET", 200, self.row()), ("/api/workspaces", "PATCH", 201, {"data": self.row()}), ("/api/workspaces", "POST", 200, {"data": self.row()}), ("/api/workspaces", "GET", 201, [self.row()]), ("/api/workspaces", "POST", 500, {"data": self.row()})):
            self.assertEqual(self.check(document, {"icon": "sqlstate"}, path=path, method=method, status=status), "sqlstate")

    def test_wrong_envelope_and_collection_shapes_stay_blocking(self):
        for document in (self.row(), {"data": [self.row()]}, [{"data": self.row()}]):
            self.assertEqual(self.check(document, {"icon": "sqlstate"}), "sqlstate")
        self.seed()
        self.assertEqual(self.check({"data": [self.row()]}, method="GET", status=200), "sqlstate")

    def test_other_and_nested_fields_cannot_be_hidden(self):
        self.seed()
        for extra in ({"name": "sqlstate"}, {"description": "sqlstate"}, {"memberPreview": [{"displayName": "sqlstate"}]}, {"error": {"detail": "sqlstate"}}, {"stackTrace": "private synthetic stack"}):
            self.assertIsNotNone(self.check([self.row(**extra)], method="GET", status=200))

    def test_invalid_post_cannot_seed_a_future_collection(self):
        self.assertEqual(self.check({"data": self.row(), "error": "sqlstate"}, {"icon": "sqlstate"}), "sqlstate")
        self.assertEqual(self.check([self.row()], method="GET", status=200), "sqlstate")

    def test_auth_material_raw_or_escaped_is_never_attributable(self):
        secret = "csrf-canary-123456"
        for encoded in (False, True):
            content = json.dumps({"data": self.row(secret)})
            if encoded:
                content = content.replace(secret, "".join("\\u%04x" % ord(c) for c in secret))
            self.assertEqual(self.check(content.encode(), {"icon": secret}, forbidden=[secret]), "ephemeral-auth-material")

    def test_duplicate_fields_cannot_establish_provenance(self):
        content = ('{"data":{"id":"' + self.identity + '","icon":"sqlstate","icon":"sqlstate"}}').encode()
        self.assertEqual(self.check(content, {"icon": "sqlstate"}), "sqlstate")
        self.assertEqual(self.check([self.row()], method="GET", status=200), "sqlstate")

    def test_cache_capacity_is_shared_with_existing_integration_proof(self):
        self.policy = IntegrationInputAttribution(capacity=1)
        integration = {"id": self.identity, "displayName": "sqlstate"}
        self.assertIsNone(self.check(integration, {"displayName": "sqlstate"}, status=200, path="/api/tenant/integrations"))
        self.seed()
        self.assertEqual(self.check([self.row()], method="GET", status=200), "sqlstate")

    def test_resource_families_never_share_proof_even_with_equal_id_and_value(self):
        integration = {"id": self.identity, "displayName": "sqlstate"}
        self.assertIsNone(self.check(integration, {"displayName": "sqlstate"}, status=200, path="/api/tenant/integrations"))
        self.assertEqual(self.check([self.row()], method="GET", status=200), "sqlstate")
        self.policy = IntegrationInputAttribution()
        self.seed()
        self.assertEqual(self.check([integration], method="GET", status=200, path="/api/tenant/integrations"), "sqlstate")

    def test_payloads_remain_immutable(self):
        response, request = {"data": self.row()}, {"icon": "sqlstate"}
        before = copy.deepcopy((response, request))
        self.assertIsNone(self.check(response, request))
        self.assertEqual((response, request), before)


if __name__ == "__main__":
    unittest.main()
