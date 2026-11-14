#!/usr/bin/env python3
"""Loopback-only, secret-free controls for the draft physical-recovery HTTP client."""

from __future__ import annotations

from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
from pathlib import Path
import tempfile
import threading
import unittest


ROOT = Path(__file__).resolve().parents[2]
PROBE_FILE = ROOT / "scripts/ci/synthetic-physical-recovery-probe.py"
SPEC = importlib.util.spec_from_file_location("synthetic_physical_recovery_probe", PROBE_FILE)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError("Cannot import the recovery probe.")
PROBE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PROBE)


class LocalRedirectFixture(ThreadingHTTPServer):
    redirect_status = 302
    unexpected_requests = 0


class SyntheticHandler(BaseHTTPRequestHandler):
    def do_GET(self) -> None:
        self.respond()

    def do_POST(self) -> None:
        self.respond()

    def respond(self) -> None:
        if self.path == "/redirect":
            self.send_response(self.server.redirect_status)
            self.send_header("Location", f"http://127.0.0.1:{self.server.server_port}/unexpected")
            self.end_headers()
        elif self.path == "/unexpected":
            self.server.unexpected_requests += 1
            self.send_response(200)
            self.end_headers()
            self.wfile.write(b"redirect was followed")
        elif self.path == "/ok":
            self.send_response(200)
            self.end_headers()
            self.wfile.write(b"synthetic-ok")
        else:
            self.send_response(403)
            self.end_headers()
            self.wfile.write(b"denied")

    def log_message(self, format: str, *args: object) -> None:
        pass


class RecoveryHttpRedirectTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.server = LocalRedirectFixture(("127.0.0.1", 0), SyntheticHandler)
        cls.thread = threading.Thread(target=cls.server.serve_forever, daemon=True)
        cls.thread.start()

    @classmethod
    def tearDownClass(cls) -> None:
        cls.server.shutdown()
        cls.server.server_close()
        cls.thread.join(timeout=5)

    def test_all_supported_redirects_fail_closed_for_get_and_post(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            app = PROBE.SyntheticApplication(ROOT, Path(tmp))
            app.origin = f"http://127.0.0.1:{self.server.server_port}"
            for code in (301, 302, 303, 307, 308):
                for body in (None, {"syntheticPassword": "not-a-credential"}):
                    with self.subTest(redirect_status=code, method="POST" if body else "GET"):
                        self.server.redirect_status = code
                        with self.assertRaisesRegex(PROBE.ProbeFailure, "^APPLICATION_REDIRECT_FORBIDDEN$"):
                            app.request("/redirect", body)
            self.assertEqual(self.server.unexpected_requests, 0)

    def test_allowed_loopback_success_and_nonredirect_denial(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            app = PROBE.SyntheticApplication(ROOT, Path(tmp))
            app.origin = f"http://127.0.0.1:{self.server.server_port}"
            self.assertEqual(app.request("/ok"), (200, b"synthetic-ok"))
            self.assertEqual(app.request("/deny"), (403, b"denied"))
            self.assertEqual(self.server.unexpected_requests, 0)


if __name__ == "__main__":
    unittest.main()
