"""SEC-04 / SEC-12: evidence operation identity is constrained to canonical OpenAPI."""

import json
from pathlib import Path
import runpy
import tempfile
import unittest

SUT = runpy.run_path(str(Path(__file__).resolve().parents[2] /
                         "scripts/security/process-schemathesis-report.py"))
operations = SUT["contract_operations"]
parse = SUT["parse_evidence"]

CONTRACT = {
    "openapi": "3.1.0",
    "paths": {
        "/api/auth/me": {"get": {"responses": {"200": {"description": "OK"}}}},
        "/api/tasks/{taskId}": {"post": {"responses": {"200": {"description": "OK"}}}},
    },
}
ROLE = "alpha-owner"


class SecurityOperationEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.file = Path(self.directory.name) / "evidence.ndjson"
        self.allowed = operations(json.dumps(CONTRACT).encode("utf-8"))

    def record(self, **overrides):
        event = {"event": "response", "method": "GET",
                 "operation": "GET /api/auth/me", "role": ROLE, "status": 200}
        event.update(overrides)
        self.file.write_text(json.dumps(event) + "\n", encoding="utf-8")

    def test_canonical_observed_operation_counts_as_coverage(self):
        self.record()
        self.assertEqual((1, 0, ["GET /api/auth/me"]),
                         parse(self.file, ROLE, self.allowed))

    def test_scanner_label_cannot_invent_protected_operation_or_persist_canary(self):
        for label in ("GET /api/absent", "GET /api/auth/me?token=synthetic-canary",
                      "GET /api/auth/me\nCookie:synthetic-canary", "GET /api/auth/me "):
            with self.subTest(label=label):
                self.record(operation=label)
                with self.assertRaisesRegex(SystemExit, "not bound to authoritative OpenAPI") as context:
                    parse(self.file, ROLE, self.allowed)
                self.assertNotIn("synthetic-canary", str(context.exception))

    def test_method_and_role_mismatch_fail_closed(self):
        self.record(method="POST")
        with self.assertRaises(SystemExit):
            parse(self.file, ROLE, self.allowed)
        self.record(role="beta-owner")
        with self.assertRaises(SystemExit):
            parse(self.file, ROLE, self.allowed)

    def test_network_error_is_not_credited_as_scan_success(self):
        self.record(event="network_error")
        with self.assertRaisesRegex(SystemExit, "network errors"):
            parse(self.file, ROLE, self.allowed)

    def test_contract_rejects_unknown_and_empty_inputs_without_echo(self):
        for raw in (b"", b"not-json synthetic-canary", b'{"paths":{}}',
                    b'{"paths":{"/api/x":{"get":null}}}', b'{"paths":{"bad":{"get":{}}}}'):
            with self.subTest(raw=raw):
                with self.assertRaises(SystemExit) as context:
                    operations(raw)
                self.assertNotIn("synthetic-canary", str(context.exception))

    def test_documented_methods_remain_distinct(self):
        self.assertEqual({"GET /api/auth/me", "POST /api/tasks/{taskId}"}, self.allowed)


if __name__ == "__main__":
    unittest.main()
