"""Native shell refusal and authenticated-archive controls for pinned Grype."""

import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tarfile
import tempfile
import textwrap
import unittest

INSTALLER = Path(__file__).with_name("install-grype.sh").resolve()
API = "https://api.github.com/repos/anchore/grype/releases/assets/532808761"
BROWSER = "https://github.com/anchore/grype/releases/download/v0.118.0/grype_0.118.0_linux_amd64.tar.gz"
PIN = "1d444c5e7360471815f7158f71935fcecc68a3c417d85c7344f770854300bba2"
VERIFIED_ARCHIVE = None

CURL = r'''#!/usr/bin/env python3
import json, os, pathlib, shutil, sys
args = sys.argv[1:]
with pathlib.Path(os.environ["GRYPE_TEST_CALLS"]).open("a") as output:
    output.write(json.dumps(args) + "\n")
url = next(value for value in args if value.startswith("https://"))
destination = pathlib.Path(args[args.index("--output") + 1])
mode = os.environ["GRYPE_TEST_MODE"]
if mode == "both-fail" or ("api.github.com" in url and mode.startswith("api-fail")):
    destination.write_bytes(b"partial transfer")
    sys.exit(22)
if mode == "empty":
    destination.write_bytes(b"")
elif mode == "json":
    destination.write_text('{"name":"grype","state":"uploaded"}')
elif os.environ.get("GRYPE_TEST_ARCHIVE"):
    shutil.copyfile(os.environ["GRYPE_TEST_ARCHIVE"], destination)
else:
    destination.write_bytes(b"unauthenticated archive")
host = "untrusted.invalid" if mode == "untrusted" else "release-assets.githubusercontent.com"
scheme = "http" if mode == "http" else "https"
if mode == "json":
    host = "api.github.com"
print(scheme + "://" + host + "/grype", end="")
'''


class GrypeFixture(unittest.TestCase):
    def run_installer(self, mode, archive=None, extra_environment=None):
        temporary = tempfile.TemporaryDirectory(prefix="grype-install-control-")
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        tools = root / "tools"
        tools.mkdir()
        curl = tools / "curl"
        curl.write_text(CURL, encoding="utf-8")
        curl.chmod(0o755)
        installation = root / "installation"
        installation.mkdir()
        executable = installation / "grype"
        executable.write_bytes(b"original executable must remain unchanged")
        path_file = root / "published-path"
        path_file.touch()
        calls = root / "calls.jsonl"
        marker = root / "extraction-or-install"
        if archive is None:
            for name in ("tar", "install"):
                spy = tools / name
                spy.write_text('#!/usr/bin/env bash\nprintf attempted >> "$GRYPE_TEST_MARKER"\nexit 97\n', encoding="utf-8")
                spy.chmod(0o755)
        environment = {**os.environ, **(extra_environment or {}),
                       "PATH": str(tools) + os.pathsep + os.environ["PATH"],
                       "TMPDIR": str(root), "GITHUB_PATH": str(path_file),
                       "GRYPE_TEST_MODE": mode, "GRYPE_TEST_CALLS": str(calls),
                       "GRYPE_TEST_MARKER": str(marker)}
        environment.pop("GRYPE_TEST_ARCHIVE", None)
        if archive is not None:
            environment["GRYPE_TEST_ARCHIVE"] = str(archive)
        result = subprocess.run(["bash", str(INSTALLER), str(installation)], env=environment,
                                capture_output=True, text=True, check=False, timeout=30)
        recorded = [json.loads(line) for line in calls.read_text().splitlines()]
        for args in recorded:
            for flag, value in (("--proto", "=https"), ("--proto-redir", "=https"),
                                ("--max-redirs", "5"), ("--retry", "3")):
                self.assertEqual(args[args.index(flag) + 1], value)
            self.assertIn("--tlsv1.2", args)
        self.assertIn("Accept: application/octet-stream", recorded[0])
        self.assertIn("X-GitHub-Api-Version: 2022-11-28", recorded[0])
        self.assertEqual(next(a for a in recorded[0] if a.startswith("https://")), API)
        if len(recorded) == 2:
            self.assertEqual(next(a for a in recorded[1] if a.startswith("https://")), BROWSER)
        return result, recorded, executable, path_file, marker

    def assert_refused(self, mode, call_count, extra_environment=None):
        result, calls, executable, published, marker = self.run_installer(mode, extra_environment=extra_environment)
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(len(calls), call_count)
        self.assertEqual(executable.read_bytes(), b"original executable must remain unchanged")
        self.assertEqual(published.read_bytes(), b"")
        self.assertFalse(marker.exists(), "Unauthenticated bytes must never reach extraction or installation.")


@unittest.skipUnless(os.name == "posix", "Native Linux shell controls")
class GrypeRefusalTests(GrypeFixture):
    def test_both_failed_transfers_preserve_the_existing_installation(self):
        self.assert_refused("both-fail", 2)

    def test_checksum_failure_does_not_fall_back_or_extract(self):
        self.assert_refused("invalid", 1)

    def test_empty_successful_response_is_not_an_archive(self):
        self.assert_refused("empty", 1)

    def test_untrusted_effective_host_never_reaches_extraction(self):
        self.assert_refused("untrusted", 1)

    def test_non_https_effective_url_is_refused(self):
        self.assert_refused("http", 1)

    def test_browser_fallback_remains_checksum_pinned(self):
        self.assert_refused("api-fail-invalid", 2)

    def test_api_metadata_cannot_be_installed_as_a_binary(self):
        self.assert_refused("json", 1)

    def test_environment_cannot_replace_the_release_or_checksum(self):
        self.assert_refused("invalid", 1, {"GRYPE_VERSION": "999.0.0",
                            "GRYPE_LINUX_AMD64_ARCHIVE_SHA256": hashlib.sha256(b"unauthenticated archive").hexdigest()})


@unittest.skipUnless(os.name == "posix", "Native Linux authenticated executable controls")
class GrypeVerifiedArchiveTests(GrypeFixture):
    def setUp(self):
        self.assertIsNotNone(VERIFIED_ARCHIVE)
        with VERIFIED_ARCHIVE.open("rb") as archive:
            self.assertEqual(hashlib.file_digest(archive, "sha256").hexdigest(), PIN)

    def assert_installed(self, mode, expected_calls):
        result, calls, executable, published, _ = self.run_installer(mode, VERIFIED_ARCHIVE)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(len(calls), expected_calls)
        self.assertRegex(result.stdout, r"Version:\s+0\.118\.0(?:\s|$)")
        self.assertEqual(published.read_text().splitlines(), [str(executable.parent)])
        with tarfile.open(VERIFIED_ARCHIVE, "r:gz") as archive:
            with archive.extractfile("grype") as original, executable.open("rb") as installed:
                self.assertEqual(hashlib.file_digest(original, "sha256").digest(),
                                 hashlib.file_digest(installed, "sha256").digest())

    def test_official_api_supplies_the_original_authenticated_executable(self):
        self.assert_installed("verified", 1)

    def test_failed_api_transfer_uses_the_same_verified_browser_archive(self):
        self.assert_installed("api-fail-verified", 2)

    def test_valid_archive_from_untrusted_effective_host_is_still_refused(self):
        result, calls, executable, published, _ = self.run_installer("untrusted", VERIFIED_ARCHIVE)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(len(calls), 1)
        self.assertEqual(executable.read_bytes(), b"original executable must remain unchanged")
        self.assertEqual(published.read_bytes(), b"")



@unittest.skipUnless(os.name == "posix", "Native workflow fixture acquisition controls")
class GrypeFixtureAcquisitionTests(GrypeFixture):
    def run_acquisition(self, mode, authenticated=True):
        temporary = tempfile.TemporaryDirectory(prefix="grype-workflow-control-")
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        tools = root / "tools"
        tools.mkdir()
        curl = tools / "curl"
        curl.write_text(CURL, encoding="utf-8")
        curl.chmod(0o755)
        calls = root / "calls.jsonl"
        workflow = INSTALLER.parents[2] / ".github/workflows/ci-routing-self-test.yml"
        source = workflow.read_text(encoding="utf-8")
        start = source.index('          archive="$RUNNER_TEMP/grype-')
        end = source.index("          python3 -B scripts/ci/test-install-grype.py --verified-archive", start)
        script = "set -euo pipefail\n" + textwrap.dedent(source[start:end])
        environment = {**os.environ, "PATH": str(tools) + os.pathsep + os.environ["PATH"],
                       "RUNNER_TEMP": str(root), "GRYPE_TEST_MODE": mode,
                       "GRYPE_TEST_CALLS": str(calls)}
        environment.pop("GRYPE_TEST_ARCHIVE", None)
        if authenticated:
            environment["GRYPE_TEST_ARCHIVE"] = str(VERIFIED_ARCHIVE)
        result = subprocess.run(["bash", "-c", script], env=environment,
                                capture_output=True, text=True, check=False, timeout=30)
        recorded = [json.loads(line) for line in calls.read_text().splitlines()]
        self.assertEqual(next(a for a in recorded[0] if a.startswith("https://")), API)
        for args in recorded:
            for flag, value in (("--proto", "=https"), ("--proto-redir", "=https"),
                                ("--max-redirs", "5"), ("--max-time", "45"),
                                ("--retry", "1"), ("--retry-max-time", "95")):
                self.assertEqual(args[args.index(flag) + 1], value)
            self.assertIn("--tlsv1.2", args)
        if len(recorded) == 2:
            self.assertEqual(next(a for a in recorded[1] if a.startswith("https://")), BROWSER)
        return result, recorded, root / "grype-0.118.0-test-archive.tar.gz"

    def assert_acquisition(self, mode, successful, count, authenticated=True):
        result, calls, archive = self.run_acquisition(mode, authenticated)
        self.assertEqual(result.returncode == 0, successful, result.stdout + result.stderr)
        self.assertEqual(len(calls), count)
        if successful:
            with archive.open("rb") as original:
                self.assertEqual(hashlib.file_digest(original, "sha256").hexdigest(), PIN)

    def test_workflow_api_acquisition_accepts_only_the_original_archive(self):
        self.assert_acquisition("verified", True, 1)

    def test_workflow_failed_api_transfer_falls_back_to_browser(self):
        self.assert_acquisition("api-fail-verified", True, 2)

    def test_workflow_both_transfer_failures_are_rejected(self):
        self.assert_acquisition("both-fail", False, 2)

    def test_workflow_checksum_rejection_does_not_fall_back(self):
        self.assert_acquisition("invalid", False, 1, authenticated=False)

    def test_workflow_valid_archive_from_untrusted_host_does_not_fall_back(self):
        self.assert_acquisition("untrusted", False, 1)

    def test_workflow_non_https_effective_url_does_not_fall_back(self):
        self.assert_acquisition("http", False, 1)

    def test_workflow_both_route_budgets_leave_time_for_controls(self):
        workflow = (INSTALLER.parents[2] / ".github/workflows/ci-routing-self-test.yml").read_text()
        self.assertIn("timeout-minutes: 5", workflow)
        # Two attempts per route with one second between retries, then control overhead.
        self.assertLessEqual(2 * (2 * 45 + 1) + 60, 5 * 60)

if __name__ == "__main__":
    selected = "GrypeRefusalTests"
    if len(sys.argv) == 3 and sys.argv[1] == "--verified-archive":
        VERIFIED_ARCHIVE = Path(sys.argv[2]).resolve(strict=True)
        selected = ["GrypeVerifiedArchiveTests", "GrypeFixtureAcquisitionTests"]
        sys.argv = sys.argv[:1]
    unittest.main(defaultTest=selected)
