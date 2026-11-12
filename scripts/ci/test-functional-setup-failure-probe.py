#!/usr/bin/env python3
"""No-Docker negative/positive controls for the Functional setup snapshot."""
import importlib.util
import json
import unittest
from pathlib import Path

MODULE_PATH = Path(__file__).with_name("functional-setup-failure-probe.py")
SPEC = importlib.util.spec_from_file_location("functional_setup_failure_probe", MODULE_PATH)
probe = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(probe)

COMPOSE = ["docker", "compose", "-p", "synthetic-only", "-f", "test.yml"]
SECRET = "PRIVATE_PASSWORD_TOKEN_COOKIES_AND_URL"
PINNED = "registry.example.invalid/sdk@sha256:" + "a" * 64

class ProbeTest(unittest.TestCase):
    def fake(self, *, daemon=True, config=True, image=True, image_ref=PINNED):
        calls = []
        document = {"services": {"migrate": {"image": image_ref},
                    "app": {"environment": {"PASSWORD": SECRET}}}}
        def runner(argv, capture=False):
            calls.append((tuple(argv), capture))
            if argv == ["docker", "info"]:
                return daemon, b""
            if argv[-3:] == ["config", "--format", "json"]:
                return config, json.dumps(document).encode() if config else b""
            if argv[:3] == ["docker", "image", "inspect"]:
                return image, b""
            raise AssertionError("Unexpected Docker command")
        return runner, calls

    def test_pinned_local_image_and_reachable_daemon_have_finite_provenance(self):
        runner, calls = self.fake()
        value = probe.observe(COMPOSE, runner)
        self.assertEqual(value, {
            "schemaVersion": 1, "phase": "apply-migrations",
            "daemonReachable": "yes", "migrateImagePinned": "yes",
            "migrateImageCache": "verified",
        })
        self.assertEqual(len(calls), 3)
        self.assertNotIn(SECRET, json.dumps(value))
        self.assertNotIn(PINNED, json.dumps(value))

    def test_missing_or_broken_image_inspection_never_claims_cache(self):
        runner, calls = self.fake(image=False)
        value = probe.observe(COMPOSE, runner)
        self.assertEqual(value["migrateImageCache"], "not_verified")
        self.assertEqual(len(calls), 3)
        self.assertNotIn(SECRET, json.dumps(value))

    def test_daemon_unreachable_never_makes_cached_image_claim(self):
        runner, calls = self.fake(daemon=False)
        value = probe.observe(COMPOSE, runner)
        self.assertEqual(value["daemonReachable"], "not_reachable")
        self.assertEqual(value["migrateImageCache"], "unknown")
        self.assertEqual(len(calls), 2)

    def test_unpinned_image_is_not_inspected_or_published(self):
        runner, calls = self.fake(image_ref="registry.example.invalid/sdk:untrusted")
        value = probe.observe(COMPOSE, runner)
        self.assertEqual(value["migrateImagePinned"], "no")
        self.assertEqual(value["migrateImageCache"], "unknown")
        self.assertEqual(len(calls), 2)

    def test_option_like_pinned_reference_is_not_passed_to_docker(self):
        runner, calls = self.fake(image_ref="--unsafe@sha256:" + "a" * 64)
        value = probe.observe(COMPOSE, runner)
        self.assertEqual(value["migrateImagePinned"], "no")
        self.assertEqual(value["migrateImageCache"], "unknown")
        self.assertEqual(len(calls), 2)

    def test_unavailable_config_invalid_command_and_malformed_json_fail_closed(self):
        runner, _ = self.fake(config=False)
        self.assertEqual(probe.observe(COMPOSE, runner)["migrateImagePinned"], "unknown")
        def malformed(argv, capture=False):
            if argv == ["docker", "info"]:
                return True, b""
            return True, b"{\"PASSWORD\":\"" + SECRET.encode() + b"\"}"
        result = probe.observe(COMPOSE, malformed)
        self.assertEqual(result["migrateImageCache"], "unknown")
        self.assertNotIn(SECRET, json.dumps(result))
        def forbidden(argv, capture=False):
            raise AssertionError("Invalid executable must never be invoked")
        self.assertEqual(probe.observe(["curl", SECRET], forbidden), probe.unknown())

    def test_oversized_compose_config_is_not_serialized_or_inspected(self):
        def enormous(argv, capture=False):
            if argv == ["docker", "info"]:
                return True, b""
            return True, SECRET.encode() * (probe.MAX_CONFIG_BYTES // len(SECRET) + 1)
        value = probe.observe(COMPOSE, enormous)
        self.assertEqual(value["migrateImagePinned"], "unknown")
        self.assertEqual(value["migrateImageCache"], "unknown")
        self.assertNotIn(SECRET, json.dumps(value))

if __name__ == "__main__":
    unittest.main()
