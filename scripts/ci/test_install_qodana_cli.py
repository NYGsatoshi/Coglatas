"""Execute refusal controls against the real checksum-pinned shell installer."""

import os
import hashlib
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest


INSTALLER = Path(__file__).with_name("install-qodana-cli.sh").resolve()
VERIFIED_ARCHIVE = None


@unittest.skipUnless(os.name == "posix", "Native Linux shell controls")
class QodanaCliInstallTests(unittest.TestCase):
    def test_rejected_archives_never_extract_or_modify_destination(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            invalid = root / "invalid.tar.gz"
            invalid.write_bytes(b"not the authenticated Qodana release")
            empty = root / "empty.tar.gz"
            empty.touch()
            directory = root / "directory"
            directory.mkdir()
            link = root / "symlink.tar.gz"
            link.symlink_to(invalid)
            destination = root / "installation"
            destination.mkdir()
            sentinel = destination / "qodana"
            sentinel.write_bytes(b"existing executable must remain unchanged")
            for archive in (root / "missing", invalid, empty, directory, link):
                with self.subTest(archive=archive.name):
                    result = subprocess.run(
                        ["bash", "-c", 'source "$1"; install_verified_qodana_archive "$2" "$3"',
                         "qodana-refusal", str(INSTALLER), str(archive), str(destination)],
                        capture_output=True, text=True, check=False,
                    )
                    self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
                    self.assertEqual(list(destination.iterdir()), [sentinel])
                    self.assertEqual(sentinel.read_bytes(), b"existing executable must remain unchanged")

    def test_absent_runner_context_does_not_publish_path(self):
        with tempfile.TemporaryDirectory() as temporary:
            path_file = Path(temporary) / "path"
            path_file.touch()
            environment = {**os.environ, "GITHUB_PATH": str(path_file)}
            environment.pop("RUNNER_TEMP", None)
            result = subprocess.run(["bash", str(INSTALLER)], env=environment,
                                    capture_output=True, text=True, check=False)
            self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertEqual(path_file.read_bytes(), b"")

    def test_arguments_cannot_replace_release_identity(self):
        result = subprocess.run(["bash", str(INSTALLER), "another-release"],
                                capture_output=True, text=True, check=False)
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_failed_transfer_does_not_publish_or_execute_download(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            path_file = root / "path"
            path_file.touch()
            tools = root / "tools"
            tools.mkdir()
            curl = tools / "curl"
            curl.write_text("#!/usr/bin/env bash\nexit 22\n", encoding="utf-8")
            curl.chmod(0o755)
            environment = {**os.environ, "RUNNER_TEMP": str(root),
                           "GITHUB_PATH": str(path_file),
                           "PATH": str(tools) + os.pathsep + os.environ["PATH"]}
            result = subprocess.run(["bash", str(INSTALLER)], env=environment,
                                    capture_output=True, text=True, check=False)
            self.assertEqual(result.returncode, 22, result.stdout + result.stderr)
            self.assertEqual(path_file.read_bytes(), b"")
            self.assertEqual(list(root.glob("qodana-cli-*/qodana")), [])


class QodanaCliBrowserFallbackTests(unittest.TestCase):
    def test_api_failure_falls_back_to_the_same_authenticated_executable(self):
        self.assertIsNotNone(VERIFIED_ARCHIVE)
        original_cli = Path(shutil.which("qodana"))
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            path_file = root / "path"
            path_file.touch()
            tools = root / "tools"
            tools.mkdir()
            curl = tools / "curl"
            curl.write_text("""#!/usr/bin/env bash
set -euo pipefail
printf '%s\\n' call >> "$QODANA_TEST_TRANSFERS"
output=''
browser=0
while (( $# )); do
  case "$1" in
    --output) output="$2"; shift 2 ;;
    https://api.github.com/repos/JetBrains/qodana-cli/releases/assets/585558698) exit 22 ;;
    https://github.com/JetBrains/qodana-cli/releases/download/v2026.2.2/qodana_linux_x86_64.tar.gz) browser=1; shift ;;
    *) shift ;;
  esac
done
test -n "$output"
test "$browser" -eq 1
cp -- "$QODANA_TEST_VERIFIED_ARCHIVE" "$output"
""", encoding="utf-8")
            curl.chmod(0o755)
            transfers = root / "transfers"
            environment = {**os.environ, "RUNNER_TEMP": str(root),
                           "GITHUB_PATH": str(path_file),
                           "PATH": str(tools) + os.pathsep + os.environ["PATH"],
                           "QODANA_TEST_VERIFIED_ARCHIVE": str(VERIFIED_ARCHIVE),
                           "QODANA_TEST_TRANSFERS": str(transfers)}
            result = subprocess.run(["bash", str(INSTALLER)], env=environment,
                                    capture_output=True, text=True, check=False)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertEqual(transfers.read_text().splitlines(), ["call", "call"])
            self.assertIn("qodana version 2026.2.2", result.stdout)
            installed = Path(path_file.read_text().strip()) / "qodana"
            with installed.open("rb") as actual, original_cli.open("rb") as expected:
                self.assertEqual(hashlib.file_digest(actual, "sha256").digest(),
                                 hashlib.file_digest(expected, "sha256").digest())


if __name__ == "__main__":
    suite = "QodanaCliInstallTests"
    if len(sys.argv) == 3 and sys.argv[1] == "--verified-archive":
        VERIFIED_ARCHIVE = Path(sys.argv[2]).resolve(strict=True)
        suite = "QodanaCliBrowserFallbackTests"
        sys.argv = sys.argv[:1]
    unittest.main(defaultTest=suite)
