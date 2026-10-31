"""Synthetic admission controls; run against the combined Product/Test checkout."""

import copy
import importlib.util
import io
import json
from pathlib import Path
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("gcp_loopback", ROOT / "deploy/gcp/verify-loopback.py")
GUARD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(GUARD)
BASE = {"services": {"app": {"ports": [{
    "target": 8080, "published": "8080", "host_ip": "127.0.0.1", "protocol": "tcp"
}]}}}


class GcpLoopbackAdmissionTests(unittest.TestCase):
    def cli(self, document, expected):
        output, error = io.StringIO(), io.StringIO()
        with patch("sys.stdin", io.StringIO(document)), patch("sys.stdout", output), patch("sys.stderr", error):
            self.assertEqual(GUARD.main(), expected)
        self.assertNotIn("Traceback", error.getvalue())
        self.assertNotIn("synthetic-private-config-canary", output.getvalue() + error.getvalue())

    def test_explicit_ipv4_and_ipv6_loopback_are_admitted(self):
        for address in ["127.0.0.1", "::1"]:
            with self.subTest(address=address):
                value = copy.deepcopy(BASE)
                value["services"]["app"]["ports"][0]["host_ip"] = address
                self.cli(json.dumps(value), 0)

    def test_default_bridge_render_is_admitted(self):
        value = copy.deepcopy(BASE)
        value["networks"] = {"default": {"name": "synthetic_default", "driver": "bridge"}}
        self.cli(json.dumps(value), 0)

    def test_public_missing_and_invalid_bindings_are_denied(self):
        for address in ["", "0.0.0.0", "::", "203.0.113.4", "localhost", None, ["127.0.0.1"]]:
            with self.subTest(address=address):
                value = copy.deepcopy(BASE)
                value["services"]["app"]["ports"][0]["host_ip"] = address
                self.cli(json.dumps(value), 1)
        value = copy.deepcopy(BASE)
        del value["services"]["app"]["ports"][0]["host_ip"]
        self.cli(json.dumps(value), 1)

    def test_invalid_published_ports_are_denied(self):
        for published in ["", "0", "65536", "8080-8090", True]:
            with self.subTest(published=published):
                value = copy.deepcopy(BASE)
                value["services"]["app"]["ports"][0]["published"] = published
                self.cli(json.dumps(value), 1)

    def test_extra_public_app_or_database_mapping_is_denied(self):
        for service, target in [("app", 8080), ("postgres", 5432)]:
            with self.subTest(service=service):
                value = copy.deepcopy(BASE)
                value["services"].setdefault(service, {}).setdefault("ports", []).append({
                    "target": target, "published": "8081", "host_ip": "0.0.0.0"
                })
                self.cli(json.dumps(value), 1)

    def test_network_namespace_sharing_is_denied(self):
        for mode in ["host", "service:proxy", "container:outside"]:
            with self.subTest(mode=mode):
                value = copy.deepcopy(BASE)
                value["services"]["app"]["network_mode"] = mode
                self.cli(json.dumps(value), 1)

    def test_routed_trusted_external_and_nonbridge_networks_are_denied(self):
        for network in [
            {"driver_opts": {"com.docker.network.bridge.gateway_mode_ipv4": "routed"}},
            {"driver_opts": {"com.docker.network.bridge.gateway_mode_ipv6": "routed"}},
            {"driver_opts": {"com.docker.network.bridge.trusted_host_interfaces": "eth0"}},
            {"external": True}, {"driver": "macvlan"}, None,
        ]:
            with self.subTest(network=network):
                value = copy.deepcopy(BASE)
                value["networks"] = {"default": network}
                self.cli(json.dumps(value), 1)

    def test_missing_listener_or_invalid_structure_is_denied(self):
        for value in [4, [], {}, {"services": {}}, {"services": {"app": {}}},
                      {"services": {"app": None}}, {"services": {"app": {"ports": {}}}},
                      {"services": {"app": {"ports": ["8080:8080"]}}},
                      {**BASE, "networks": []}]:
            with self.subTest(value=value):
                self.cli(json.dumps(value), 1)

    def test_malformed_deep_and_oversized_input_does_not_leak(self):
        for document in ['{"secret":"synthetic-private-config-canary"',
                         "synthetic-private-config-canary" * 50000,
                         "[" * 2000 + "]" * 2000]:
            with self.subTest(size=len(document)):
                self.cli(document, 1)


if __name__ == "__main__":
    unittest.main()
