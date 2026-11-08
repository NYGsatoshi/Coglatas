"""Execute refusal controls against the real checksum-pinned shell installer."""

import os
from pathlib import Path
import subprocess
import tempfile
import unittest


INSTALLER = Path(__file__).with_name("install-qodana-cli.sh").resolve()


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


if __name__ == "__main__":
    unittest.main()
