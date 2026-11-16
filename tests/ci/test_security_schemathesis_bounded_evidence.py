"""SEC-12: native SEC-04 artifacts must be bounded before JSON parsing."""

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SOURCE = Path(__file__).resolve().parents[2] / "scripts/security/process-schemathesis-report.py"
spec = importlib.util.spec_from_file_location("sec04_bounded_evidence", SOURCE)
assert spec and spec.loader
sut = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sut)


class BoundedSchemathesisEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.raw = self.root / "raw.ndjson"
        self.safe = self.root / "sanitized.ndjson"
        self.evidence = self.root / "evidence.ndjson"
        self.seed = 123
        self.initialize = {"Initialize": {"schemathesis_version": sut.EXPECTED_VERSION,
                                           "seed": self.seed}}

    def write_raw(self, *events):
        self.raw.write_bytes(b"".join(
            (json.dumps(event) + "\n").encode("utf-8") for event in events))

    def assert_clean_refusal(self, pattern):
        with self.assertRaisesRegex(SystemExit, pattern) as caught:
            sut.parse_report(self.raw, self.safe, {"synthetic-secret-123"}, self.seed)
        self.assertNotIn("synthetic-secret-123", str(caught.exception))
        self.assertFalse(self.safe.exists())
        self.assertEqual(["raw.ndjson"], [p.name for p in self.root.iterdir()])

    def test_accepted_input_preserves_sanitization_and_pinned_seed(self):
        self.write_raw(self.initialize, {"ScenarioFinished": {
            "safe": "ok", "note": "synthetic-secret-123"}})
        self.assertEqual((1, sut.EXPECTED_VERSION),
                         sut.parse_report(self.raw, self.safe, {"synthetic-secret-123"}, self.seed))
        result = self.safe.read_text(encoding="utf-8")
        self.assertNotIn("synthetic-secret-123", result)
        self.assertIn("[REDACTED]", result)
        self.assertIn('"safe":"ok"', result)

    def test_long_raw_line_fails_before_unbounded_parse(self):
        self.write_raw(self.initialize, {"ScenarioFinished": {"body": "x" * 128}})
        with patch.object(sut, "MAX_NDJSON_LINE_BYTES", 100):
            self.assert_clean_refusal("raw report exceeds bounded input")

    def test_total_raw_limit_cannot_be_bypassed_with_short_lines(self):
        self.write_raw(self.initialize, *({"ScenarioFinished": {"step": index}} for index in range(8)))
        with patch.object(sut, "MAX_RAW_NDJSON_BYTES", 135):
            self.assert_clean_refusal("raw report exceeds bounded input")

    def test_excessive_record_count_fails_closed(self):
        self.write_raw(self.initialize, {"ScenarioFinished": {"safe": True}})
        with patch.object(sut, "MAX_NDJSON_RECORDS", 1):
            self.assert_clean_refusal("raw report exceeds bounded input")

    def test_sanitized_output_expansion_cannot_create_unbounded_artifact(self):
        self.write_raw(self.initialize, {"ScenarioFinished": {"text": "\u2603" * 60}})
        with patch.object(sut, "MAX_SAFE_NDJSON_BYTES", 140):
            self.assert_clean_refusal("sanitized report exceeds bounded output")

    def test_non_utf8_event_rejected_without_persistent_receipt(self):
        self.raw.write_bytes(b'{"Initialize":' + b"\xff" + b"}\n")
        self.assert_clean_refusal("raw report has invalid UTF-8")

    def test_execution_evidence_counts_real_declared_responses(self):
        self.evidence.write_text(json.dumps({"event": "response", "method": "GET",
            "operation": "GET /api/auth/me", "role": "alpha-owner", "status": 200}) + "\n",
            encoding="utf-8")
        self.assertEqual((1, 0, ["GET /api/auth/me"]),
            sut.parse_evidence(self.evidence, "alpha-owner", {"GET /api/auth/me"}))

    def test_execution_evidence_oversize_and_unsafe_method_remain_rejected(self):
        self.evidence.write_text(json.dumps({"event": "response", "method": "GET",
            "operation": "GET /api/auth/me", "role": "alpha-owner", "status": 200}) + "\n",
            encoding="utf-8")
        with patch.object(sut, "MAX_EXECUTION_NDJSON_BYTES", 24):
            with self.assertRaisesRegex(SystemExit, "execution evidence exceeds bounded input"):
                sut.parse_evidence(self.evidence, "alpha-owner", {"GET /api/auth/me"})
        self.evidence.write_text(json.dumps({"event": "response", "method": "BAD\\nCOOKIE",
            "operation": "GET /api/auth/me", "role": "alpha-owner", "status": 200}) + "\n",
            encoding="utf-8")
        with self.assertRaisesRegex(SystemExit, "HTTP method is invalid"):
            sut.parse_evidence(self.evidence, "alpha-owner", {"GET /api/auth/me"})


if __name__ == "__main__":
    unittest.main()
