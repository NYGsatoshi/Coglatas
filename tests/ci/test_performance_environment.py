from __future__ import annotations

import importlib.util
import io
import json
import os
import secrets
import shutil
import subprocess
import sys
import tempfile
import textwrap
import threading
import unittest
from contextlib import contextmanager, redirect_stderr, redirect_stdout
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
COMMON_PATH = ROOT / "scripts" / "performance" / "common.py"
spec = importlib.util.spec_from_file_location("performance_common_test", COMMON_PATH)
assert spec is not None and spec.loader is not None
common = importlib.util.module_from_spec(spec)
spec.loader.exec_module(common)


class PerformanceEnvironmentContractTests(unittest.TestCase):
    def test_db_opt_in_has_a_distinct_fixture_hash_and_preserves_the_base_version(self) -> None:
        with patch.dict("os.environ", {"COGLATAS_PERFORMANCE_DB_CAPTURE_ENABLED": "false"}):
            base_hash = common.fixture_hash("small")
            self.assertEqual(common.FIXTURE_VERSION, common.active_fixture_version())
        with patch.dict("os.environ", {"COGLATAS_PERFORMANCE_DB_CAPTURE_ENABLED": "true"}):
            db_hash = common.fixture_hash("small")
            self.assertEqual(common.DB_FIXTURE_VERSION, common.active_fixture_version())
        self.assertNotEqual(base_hash, db_hash)
        self.assertEqual(db_hash, common.fixture_hash("small", fixture_version=common.DB_FIXTURE_VERSION))

    def test_load_json_accepts_utf8_bom_but_remains_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            temp = Path(directory)
            bom_json = temp / "fixture.json"
            bom_json.write_bytes(b"\xef\xbb\xbf" + json.dumps({"schemaVersion": 1}).encode("utf-8"))
            self.assertEqual({"schemaVersion": 1}, common.load_json(bom_json))

            malformed = temp / "malformed.json"
            malformed.write_bytes(b"\xef\xbb\xbf{not-json}")
            with self.assertRaises(common.PerformanceContractError):
                common.load_json(malformed)

            invalid_utf8 = temp / "invalid-utf8.json"
            invalid_utf8.write_bytes(b"{\xff}")
            with self.assertRaises(common.PerformanceContractError):
                common.load_json(invalid_utf8)

    def test_fixture_hash_is_stable_and_profile_specific(self) -> None:
        hashes = []
        for profile in ("small", "medium", "large"):
            first = common.fixture_hash(profile)
            second = common.fixture_hash(profile)
            self.assertEqual(first, second)
            self.assertRegex(first, r"^[0-9a-f]{64}$")
            hashes.append(first)
        self.assertEqual(3, len(set(hashes)))

    def test_fixture_evidence_requires_exact_manifest_cardinalities(self) -> None:
        _, profile = common.load_profile("small")
        evidence = {
            "schemaVersion": 1,
            "fixtureVersion": common.FIXTURE_VERSION,
            "seedManifestVersion": 1,
            "profile": "small",
            "seed": profile["seed"],
            "fixtureHash": common.fixture_hash("small"),
            "migrationStatus": "current",
            "complete": True,
            "cardinalities": profile["counts"],
            "focus": profile["focus"],
            "identities": {
                "tenantSlug": "perf-small",
                "operatorEmail": "perf-small-operator@example.test",
                "workspaceId": "00000000-0000-0000-0000-000000000001",
                "taskListProjectId": "00000000-0000-0000-0000-000000000002",
                "ganttProjectId": "00000000-0000-0000-0000-000000000002",
                "kanbanProjectId": "00000000-0000-0000-0000-000000000002",
            },
        }
        common.validate_fixture_evidence(evidence, "small")
        drifted = json.loads(json.dumps(evidence))
        drifted["cardinalities"]["tasks"] += 1
        with self.assertRaises(common.PerformanceContractError):
            common.validate_fixture_evidence(drifted, "small")

    def test_public_and_production_like_targets_are_rejected(self) -> None:
        for allowed in (
            "http://127.0.0.1:18080",
            "http://localhost:18080",
            "http://coglatas-performance:8080",
            "http://performance-app:8080",
        ):
            self.assertEqual(allowed, common.validate_target(allowed))

        for rejected in (
            "https://127.0.0.1:18080",
            "http://example.com:8080",
            "http://school.example.jp:8080",
            "http://127.0.0.1",
            "http://127.0.0.1:18080/api",
            "http://user:secret@127.0.0.1:18080",
        ):
            with self.subTest(rejected=rejected):
                with self.assertRaises(common.PerformanceContractError):
                    common.validate_target(rejected)

    def test_measurement_envelope_fails_closed(self) -> None:
        verifier = ROOT / "scripts" / "performance" / "verify-samples.py"
        environment = {
            "measurement": {
                "minimumSamples": 5,
                "warmupSamplesExcluded": True,
            }
        }
        with tempfile.TemporaryDirectory() as directory:
            temp = Path(directory)
            environment_path = temp / "environment.json"
            results_path = temp / "results.json"
            environment_path.write_text(json.dumps(environment), encoding="utf-8")

            success = {
                "warmupSamplesExcluded": True,
                "measuredSamples": 5,
                "environmentStable": True,
                "benchmarkExitCode": 0,
                "timedOut": False,
            }
            results_path.write_text(json.dumps(success), encoding="utf-8")
            completed = subprocess.run(
                [
                    sys.executable,
                    str(verifier),
                    "--results",
                    str(results_path),
                    "--environment-contract",
                    str(environment_path),
                ],
                check=False,
                capture_output=True,
                text=True,
            )
            self.assertEqual(0, completed.returncode, completed.stderr)

            for mutation in (
                {"measuredSamples": 4},
                {"warmupSamplesExcluded": False},
                {"environmentStable": False},
                {"benchmarkExitCode": 1},
                {"timedOut": True},
            ):
                failing = success | mutation
                results_path.write_text(json.dumps(failing), encoding="utf-8")
                completed = subprocess.run(
                    [
                        sys.executable,
                        str(verifier),
                        "--results",
                        str(results_path),
                        "--environment-contract",
                        str(environment_path),
                    ],
                    check=False,
                    capture_output=True,
                    text=True,
                )
                self.assertNotEqual(0, completed.returncode, mutation)

    def test_environment_contract_keeps_warmup_out_of_measurement(self) -> None:
        environment = json.loads(
            (ROOT / "performance" / "environment.json").read_text(encoding="utf-8")
        )
        self.assertFalse(environment["warmup"]["measured"])
        self.assertTrue(environment["measurement"]["warmupSamplesExcluded"])
        self.assertGreater(environment["warmup"]["iterations"], 0)
        self.assertGreater(environment["measurement"]["minimumSamples"], 0)
        self.assertEqual({"cold", "warm"}, set(environment["warmup"]["browserAssetCachePolicy"]))

    def test_lifecycle_contract_requires_clean_teardown(self) -> None:
        harness = (ROOT / "scripts" / "performance" / "with-environment.sh").read_text(encoding="utf-8")
        self.assertIn("down --volumes --remove-orphans", harness)
        self.assertIn("trap 'status=$?;", harness)
        self.assertIn('timeout "$COMMAND_TIMEOUT"', harness)
        self.assertIn("preflight.py", harness)
        self.assertIn("warmup.py", harness)
        self.assertIn("collect-environment.py", harness)


class PerformanceEnvironmentRepeatStartTests(unittest.TestCase):
    def setUp(self) -> None:
        self.fixture = {
            "fixtureVersion": common.FIXTURE_VERSION,
            "seedManifestVersion": 1,
            "profile": "small",
            "seed": 592001,
            "fixtureHash": "a" * 64,
            "cardinalities": {"tasks": 120},
            "focus": {"projectTasks": 60},
            "identities": {"workspaceId": "00000000-0000-0000-0000-000000000001"},
        }
        self.encoded_fixture = json.dumps(self.fixture).encode("utf-8-sig")

    def compare_fixtures(self, first: bytes | None, second: bytes | None) -> subprocess.CompletedProcess[str]:
        # Exercise the workflow's actual comparison, not a copy of its logic.
        workflow = (ROOT / ".github/workflows/performance-environment.yml").read_text(encoding="utf-8")
        marker = "      - name: Compare deterministic fixture identity and cardinality\n"
        self.assertEqual(1, workflow.count(marker))
        step = workflow.split(marker, 1)[1].split("\n      - name:", 1)[0]
        script = textwrap.dedent(step.split("          python3 - <<'PY'\n", 1)[1].split("          PY", 1)[0])
        first_literal = "'/tmp/perf02-fixture-first.json'"
        self.assertEqual(1, script.count(first_literal))

        with tempfile.TemporaryDirectory() as directory:
            temp = Path(directory)
            first_path = temp / "first.json"
            second_path = temp / "artifacts/performance/small/fixture.json"
            second_path.parent.mkdir(parents=True)
            if first is not None:
                first_path.write_bytes(first)
            if second is not None:
                second_path.write_bytes(second)
            # Isolate only the fixed first-input path; keep both decoding calls intact.
            script = script.replace(first_literal, repr(str(first_path)))
            return subprocess.run(
                [sys.executable, "-c", script],
                cwd=temp,
                check=False,
                capture_output=True,
                text=True,
                timeout=10,
            )

    def test_repeat_start_accepts_utf8_with_or_without_bom(self) -> None:
        for first_encoding in ("utf-8", "utf-8-sig"):
            for second_encoding in ("utf-8", "utf-8-sig"):
                with self.subTest(first=first_encoding, second=second_encoding):
                    completed = self.compare_fixtures(
                        json.dumps(self.fixture).encode(first_encoding),
                        json.dumps(self.fixture).encode(second_encoding),
                    )
                    self.assertEqual(0, completed.returncode, completed.stderr)
                    self.assertIn("repeat-start deterministic fixture verified:", completed.stdout)
                    self.assertIn(self.fixture["fixtureHash"], completed.stdout)

    def test_repeat_start_rejects_drift_in_every_compared_field(self) -> None:
        mutations = {
            "fixtureVersion": common.FIXTURE_VERSION + 1,
            "seedManifestVersion": 2,
            "profile": "medium",
            "seed": 592002,
            "fixtureHash": "b" * 64,
            "cardinalities": {"tasks": 121},
            "focus": {"projectTasks": 61},
            "identities": {"workspaceId": "00000000-0000-0000-0000-000000000002"},
        }
        for key, changed in mutations.items():
            with self.subTest(key=key):
                second = json.dumps(self.fixture | {key: changed}).encode("utf-8-sig")
                completed = self.compare_fixtures(self.encoded_fixture, second)
                self.assertNotEqual(0, completed.returncode)
                self.assertIn(f"PERF-02 repeat-start drift in {key}", completed.stderr)

    def test_repeat_start_rejects_invalid_json_and_encoding_in_either_input(self) -> None:
        for invalid, error in (
            (b"\xef\xbb\xbf{not-json}", "JSONDecodeError"),
            (b"{\xff}", "UnicodeDecodeError"),
            (b"\xef\xbb\xbf" + self.encoded_fixture, "JSONDecodeError"),
        ):
            for side in (0, 1):
                with self.subTest(invalid=invalid, side=side):
                    inputs = [self.encoded_fixture, self.encoded_fixture]
                    inputs[side] = invalid
                    completed = self.compare_fixtures(*inputs)
                    self.assertNotEqual(0, completed.returncode)
                    self.assertIn(error, completed.stderr)

    def test_repeat_start_rejects_missing_compared_fields_in_either_input(self) -> None:
        for key in self.fixture:
            incomplete = {name: value for name, value in self.fixture.items() if name != key}
            for side in (0, 1):
                with self.subTest(key=key, side=side):
                    inputs = [self.encoded_fixture, self.encoded_fixture]
                    inputs[side] = json.dumps(incomplete).encode("utf-8-sig")
                    completed = self.compare_fixtures(*inputs)
                    self.assertNotEqual(0, completed.returncode)
                    self.assertIn("KeyError", completed.stderr)
                    self.assertIn(key, completed.stderr)

    def test_repeat_start_rejects_missing_files(self) -> None:
        for side in (0, 1):
            with self.subTest(side=side):
                inputs: list[bytes | None] = [self.encoded_fixture, self.encoded_fixture]
                inputs[side] = None
                completed = self.compare_fixtures(*inputs)
                self.assertNotEqual(0, completed.returncode)
                self.assertIn("FileNotFoundError", completed.stderr)


class PerformanceBrowserPreparationTests(unittest.TestCase):
    def prepare(self, image_state: str, pull_exit: int = 0) -> subprocess.CompletedProcess[str]:
        harness = (ROOT / "scripts/performance/with-environment.sh").read_text(encoding="utf-8")
        start = harness.index('echo "PERF-02: preparing configured performance-browser image (missing only)"')
        end = harness.index('\nif [[ "$REUSE_PREBUILT_APP"', start)
        self.assertLess(start, harness.index('python3 "$ROOT/scripts/performance/collect-environment.py"'))
        # Execute the actual preparation phase, with a stateful Compose stand-in.
        # A failing pull must prevent the next collection phase from running.
        script = f'''set -euo pipefail
image_state={image_state}
compose() {{
  [[ "$*" == "--profile tooling pull --policy missing performance-browser" ]] || return 77
  if (( {pull_exit} != 0 )); then return {pull_exit}; fi
  if [[ "$image_state" == missing ]]; then
    echo acquired-configured-image
    image_state=present
  fi
}}
{harness[start:end]}
[[ "$image_state" == present ]]
echo fingerprint-started
'''
        bash = shutil.which("bash")
        self.assertIsNotNone(bash, "Bash is required to execute the performance harness control")
        return subprocess.run([bash, "-c", script], capture_output=True, text=True, timeout=10, check=False)

    def test_missing_browser_image_is_prepared_before_collection(self) -> None:
        completed = self.prepare("missing")
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertIn("acquired-configured-image", completed.stdout)
        self.assertLess(completed.stdout.index("acquired-configured-image"), completed.stdout.index("fingerprint-started"))

    def test_existing_browser_image_is_not_refreshed(self) -> None:
        completed = self.prepare("present")
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertNotIn("acquired-configured-image", completed.stdout)
        self.assertIn("fingerprint-started", completed.stdout)

    def test_browser_preparation_failure_cannot_start_collection_or_report_prepared(self) -> None:
        completed = self.prepare("missing", pull_exit=1)
        self.assertEqual(2, completed.returncode)
        self.assertIn("performance-browser image preparation failed", completed.stderr)
        self.assertNotIn("performance-browser image prepared", completed.stdout)
        self.assertNotIn("fingerprint-started", completed.stdout)



class PublicPrComposeSdkImageContractTests(unittest.TestCase):
    """The runtime image must be checked after Compose's overlay is applied."""

    @classmethod
    def setUpClass(cls) -> None:
        verifier_path = ROOT / "scripts/ci/verify-performance-environment.py"
        spec = importlib.util.spec_from_file_location("performance_sdk_image_contract", verifier_path)
        assert spec is not None and spec.loader is not None
        cls.verifier = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.verifier)
        cls.sdk_version = json.loads((ROOT / "global.json").read_text())["sdk"]["version"]

    def rendered(self, app_image: str, migrate_image: str | None = None) -> dict:
        image = migrate_image or app_image
        return {"services": {"app": {"image": app_image}, "migrate": {"image": image}}}

    def test_valid_pinned_sdk_images(self) -> None:
        image = f"mcr.microsoft.com/dotnet/sdk:{self.sdk_version}@sha256:{'a' * 64}"
        self.verifier.require_source_sdk_image(self.rendered(image), self.sdk_version)

    def test_unpinned_and_mismatched_active_services_fail_closed(self) -> None:
        pinned = f"mcr.microsoft.com/dotnet/sdk:{self.sdk_version}@sha256:{'a' * 64}"
        mutations = (
            f"mcr.microsoft.com/dotnet/sdk:{self.sdk_version}",
            f"mcr.microsoft.com/dotnet/sdk:{self.sdk_version}@sha256:{'a' * 63}",
            f"mcr.microsoft.com/dotnet/sdk:{self.sdk_version}@sha256:{'a' * 64}extra",
            f"mcr.microsoft.com/dotnet/sdk:10.0.999@sha256:{'a' * 64}",
            f"example.test/dotnet/sdk:{self.sdk_version}@sha256:{'a' * 64}",
            f"mcr.microsoft.com/dotnet/sdk:{self.sdk_version}@sha256:{'g' * 64}",
        )
        for invalid in mutations:
            for kind in ("app", "migrate"):
                with self.subTest(kind=kind, invalid=invalid):
                    rendered = self.rendered(pinned)
                    rendered["services"][kind]["image"] = invalid
                    with self.assertRaises(SystemExit):
                        self.verifier.require_source_sdk_image(rendered, self.sdk_version)

    def test_comment_cannot_mask_unpinned_runtime_image(self) -> None:
        # The rendered output comes from the Compose config command, so an
        # unused comment with a valid digest cannot authorize an unpinned image.
        image = f"mcr.microsoft.com/dotnet/sdk:{self.sdk_version}@sha256:{'a' * 64}"
        rendered = self.rendered(f"mcr.microsoft.com/dotnet/sdk:{self.sdk_version}", image)
        with self.assertRaises(SystemExit):
            self.verifier.require_source_sdk_image(rendered, self.sdk_version)

    def test_rejects_missing_services_and_accidental_license_build(self) -> None:
        image = f"mcr.microsoft.com/dotnet/sdk:{self.sdk_version}@sha256:{'a' * 64}"
        for document in ({"services": {}}, {"services": {"app": {"image": image}}},
                         self.rendered(image) | {"services": {"app": {"image": image, "build": {"context": "."}},
                                                              "migrate": {"image": image}}}):
            with self.subTest(document=document), self.assertRaises(SystemExit):
                self.verifier.require_source_sdk_image(document, self.sdk_version)

    def test_render_uses_actual_compose_config_without_credentials(self) -> None:
        image = f"mcr.microsoft.com/dotnet/sdk:{self.sdk_version}@sha256:{'a' * 64}"
        expected = self.rendered(image)
        with patch.object(self.verifier.subprocess, "run",
                          return_value=subprocess.CompletedProcess(
                              ["docker"], 0, json.dumps(expected), "")) as mocked:
            result = self.verifier.render_source_compose(Path("/tmp/base.yml"), Path("/tmp/overlay.yml"))
        self.assertEqual(expected, result)
        args, kwargs = mocked.call_args
        self.assertEqual(["docker", "compose"], args[0][:2])
        self.assertEqual(["config", "--format", "json"], args[0][-3:])
        self.assertNotIn("SYNCFUSION_LICENSE", kwargs["env"])
        self.assertTrue(kwargs["check"] is False)

    def test_render_rejects_invalid_or_failing_docker_config(self) -> None:
        for code, stdout in ((1, ""), (0, "not-json"), (0, "[]")):
            with self.subTest(code=code, stdout=stdout), patch.object(
                    self.verifier.subprocess, "run",
                    return_value=subprocess.CompletedProcess(["docker"], code, stdout, "")):
                with self.assertRaises(SystemExit):
                    self.verifier.render_source_compose(Path("/tmp/base.yml"), Path("/tmp/overlay.yml"))


class PerformanceHttpRedirectTests(unittest.TestCase):
    """Real owned-loopback transport controls; no benchmark or backend acceptance."""

    def setUp(self):
        sys.path.insert(0, str(ROOT / 'scripts/performance'))
        self.addCleanup(sys.path.remove, str(ROOT / 'scripts/performance'))
        import preflight
        import warmup
        self.preflight = preflight
        self.warmup = warmup
        self.canary = secrets.token_hex(16)
        self.enterContext(patch.dict(os.environ, {
            'NO_PROXY': '*', 'no_proxy': '*',
            'COGLATAS_PERFORMANCE_DB_CAPTURE_ENABLED': 'false',
            'COGLATAS_PERFORMANCE_PASSWORD': self.canary,
        }))
        self.enterContext(patch.object(preflight.urllib.request, '_opener', None))

    @contextmanager
    def server(self, responder, host='127.0.0.1'):
        class Handler(BaseHTTPRequestHandler):
            def do_GET(self):
                responder(self)

            def do_POST(self):
                responder(self)

            def log_message(self, *_):
                pass

        server = ThreadingHTTPServer((host, 0), Handler)
        server.daemon_threads = True
        thread = threading.Thread(target=server.serve_forever,
                                  kwargs={'poll_interval': 0.01}, daemon=True)
        thread.start()
        try:
            yield f'http://{host}:{server.server_port}'
        finally:
            server.shutdown()
            server.server_close()
            thread.join(timeout=2)
            self.assertFalse(thread.is_alive(), 'Owned HTTP listener must stop.')

    @staticmethod
    def reply(request, status=200, document=None, headers=()):
        body = json.dumps(document or {}).encode()
        request.send_response(status)
        request.send_header('Content-Type', 'application/json')
        request.send_header('Content-Length', str(len(body)))
        for name, value in headers:
            request.send_header(name, value)
        request.end_headers()
        try:
            request.wfile.write(body)
        except (BrokenPipeError, ConnectionResetError):
            pass  # Rejected responses may be closed before the owned server writes its body.

    def invoke_warmup(self, base):
        _, profile = common.load_profile('small')
        evidence = {
            'schemaVersion': 1, 'fixtureVersion': common.FIXTURE_VERSION,
            'seedManifestVersion': 1, 'profile': 'small', 'seed': profile['seed'],
            'fixtureHash': common.fixture_hash('small'), 'migrationStatus': 'current',
            'complete': True, 'cardinalities': profile['counts'], 'focus': profile['focus'],
            'identities': {key: 'owned-performance-control' for key in (
                'tenantSlug', 'operatorEmail', 'workspaceId', 'taskListProjectId',
                'ganttProjectId', 'kanbanProjectId')},
        }
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = root / 'fixture.json'
            fixture.write_text(json.dumps(evidence), encoding='utf-8')
            output = root / 'warmup.json'
            arguments = ['warmup.py', '--base-url', base, '--profile', 'small',
                         '--fixture-evidence', str(fixture), '--output', str(output)]
            stdout, stderr = io.StringIO(), io.StringIO()
            with patch.object(sys, 'argv', arguments), redirect_stdout(stdout), redirect_stderr(stderr):
                status = self.warmup.main()
            document = json.loads(output.read_text()) if output.exists() else None
            return status, document, stdout.getvalue(), stderr.getvalue()

    def responder(self, records, redirect_stage=None, location=None, code=302):
        def respond(request):
            stage = ('csrf' if request.path == '/api/security/csrf-token' else
                     'login' if request.path == '/api/auth/login' else 'read')
            records.append({'stage': stage,
                            'csrf': request.headers.get('X-Owned-CSRF') == self.canary,
                            'cookie': f'owned-session={self.canary}' in request.headers.get('Cookie', '')})
            if stage == redirect_stage:
                self.reply(request, code, {'private': self.canary}, [('Location', location)])
            elif stage == 'csrf':
                self.reply(request, document={'headerName': 'X-Owned-CSRF', 'token': self.canary})
            elif stage == 'login':
                self.reply(request, headers=[('Set-Cookie', f'owned-session={self.canary}; Path=/; HttpOnly')])
            else:
                self.reply(request)
        return respond

    def test_direct_readiness_and_http_failure_keep_their_status_and_body(self):
        for status in (200, 403):
            with self.subTest(status=status):
                with self.server(lambda request: self.reply(request, status, {'status': status})) as base:
                    result, body = self.preflight.request_ok(common.validate_target(base) + '/health/ready',
                                                             'owned-control', 10)
                self.assertEqual(status, result)
                self.assertEqual({'status': status}, json.loads(body))

    def test_direct_warmup_keeps_csrf_cookie_authentication_and_output(self):
        records = []
        with self.server(self.responder(records)) as base:
            status, document, stdout, stderr = self.invoke_warmup(common.validate_target(base))
        self.assertEqual(0, status)
        self.assertEqual('', stderr)
        self.assertFalse(document['measured'])
        self.assertTrue(any(row['stage'] == 'login' and row['csrf'] for row in records))
        self.assertTrue(any(row['stage'] == 'read' for row in records))
        self.assertTrue(all(row['cookie'] for row in records if row['stage'] == 'read'))
        self.assertNotIn(self.canary, stdout + stderr + json.dumps(document))

    def test_readiness_rejects_redirect_before_forbidden_owned_loopback_target(self):
        hits = []
        with self.server(lambda request: (hits.append(True), self.reply(request)), '127.0.0.2') as target:
            with self.assertRaises(common.PerformanceContractError):
                common.validate_target(target)
            with self.server(lambda request: self.reply(request, 302, headers=[('Location', target)])) as base:
                try:
                    self.preflight.request_ok(common.validate_target(base) + '/health/ready', self.canary, 10)
                except self.preflight.PerformanceContractError:
                    pass
        self.assertEqual(0, len(hits), 'A forbidden owned redirect listener must never be contacted.')

    def assert_warmup_redirect_rejected(self, stage):
        hits = []
        with self.server(lambda request: (hits.append(True), self.reply(request,
                         document={'headerName': 'X-Owned-CSRF', 'token': self.canary})), '127.0.0.2') as target:
            with self.server(self.responder([], stage, target + '/' + self.canary)) as base:
                status, document, stdout, stderr = self.invoke_warmup(common.validate_target(base))
        self.assertEqual(0, len(hits), 'A forbidden owned redirect listener must never be contacted.')
        self.assertEqual(2, status)
        self.assertIsNone(document, 'A redirected warm-up must not publish successful output.')
        self.assertNotIn(self.canary, stdout + stderr)

    def test_csrf_bootstrap_rejects_forbidden_redirect(self):
        self.assert_warmup_redirect_rejected('csrf')

    def test_login_rejects_forbidden_redirect(self):
        self.assert_warmup_redirect_rejected('login')

    def test_authenticated_reads_reject_forbidden_redirect(self):
        self.assert_warmup_redirect_rejected('read')


if __name__ == "__main__":
    unittest.main()
