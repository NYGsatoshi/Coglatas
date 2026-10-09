#!/usr/bin/env python3
"""Exercise the artifact restore boundary; hosted collectors verify the real runtime."""
from __future__ import annotations

import gzip
import io
import os
import subprocess
import tarfile
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SHA = "a" * 40
IMAGE_ID = "sha256:" + "b" * 64


class RuntimeArtifactTests(unittest.TestCase):
    def restore(self, *, source=SHA, dotnet=SHA, image=None, identity=IMAGE_ID,
                actual=IMAGE_ID, event="push", missing=None):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            artifact, workspace, commands = (root / name for name in ("artifact", "workspace", "bin"))
            for path in (artifact, workspace, commands):
                path.mkdir()
            (artifact / "source-sha").write_text(source + "\n")
            family = "coglatas-pr-functional" if event == "pull_request" else "coglatas-main-runtime"
            (artifact / "runtime-image-name").write_text((image or family + ":" + SHA) + "\n")
            (artifact / "runtime-image-id").write_text(identity + "\n")
            with tarfile.open(artifact / "dotnet-release-build.tar", "w") as archive:
                content = (dotnet + "\n").encode()
                entry = tarfile.TarInfo("artifacts/ci/dotnet-build-sha")
                entry.size = len(content)
                archive.addfile(entry, io.BytesIO(content))
            with gzip.open(artifact / "runtime-image.tar.gz", "wb") as archive:
                archive.write(b"unit fixture for docker load")
            docker = commands / "docker"
            docker.write_text('#!/bin/sh\nif [ "$1" = load ]; then cat >/dev/null; exit 0; fi\n'
                              'if [ "$1" = image ] && [ "$2" = inspect ]; then\n'
                              '  printf "%s\\n" "$TEST_IMAGE_ID"; exit 0\nfi\nexit 1\n')
            docker.chmod(0o755)
            if missing:
                (artifact / missing).unlink()
            exported = root / "exported-env"
            result = subprocess.run(["bash", str(ROOT / "scripts/ci/restore-main-build-artifacts.sh"),
                                     str(artifact), SHA, str(workspace)],
                                    env=os.environ | {"PATH": str(commands) + os.pathsep + os.environ["PATH"],
                                                      "GITHUB_EVENT_NAME": event,
                                                      "GITHUB_ENV": str(exported), "TEST_IMAGE_ID": actual},
                                    capture_output=True, text=True)
            return result, exported.read_text() if exported.exists() else ""

    def test_matching_main_and_pr_artifacts_restore(self):
        for event in ("push", "pull_request"):
            result, exported = self.restore(event=event)
            with self.subTest(event=event):
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertIn("COGLATAS_REUSE_PREBUILT_APP_IMAGE=1", exported)

    def test_wrong_source_dotnet_tag_family_or_loaded_image_fails_before_export(self):
        changes = ({"source": "c" * 40}, {"dotnet": "c" * 40},
                   {"image": "coglatas-main-runtime:" + "c" * 40},
                   {"image": "coglatas-pr-functional:" + SHA},
                   {"actual": "sha256:" + "c" * 64}, {"identity": "invalid"})
        for change in changes:
            result, exported = self.restore(**change)
            with self.subTest(change=change):
                self.assertNotEqual(0, result.returncode)
                self.assertEqual("", exported)

    def test_missing_required_artifact_members_fail_before_export(self):
        for member in ("source-sha", "runtime-image-name", "runtime-image-id",
                       "runtime-image.tar.gz", "dotnet-release-build.tar"):
            result, exported = self.restore(missing=member)
            with self.subTest(member=member):
                self.assertNotEqual(0, result.returncode)
                self.assertEqual("", exported)

    def test_main_calls_exact_same_run_structural_collector_and_aggregates_its_result(self):
        workflow = (ROOT / ".github/workflows/main-build-artifacts.yml").read_text()
        call = workflow.split("  performance-db:\n", 1)[1].split("\n  licensed-real-backend:", 1)[0]
        for required in ("if: always()", "needs: main-build-artifacts", "actions: read",
                         "uses: ./.github/workflows/performance-db.yml",
                         "reuse_main_artifacts: true", "producer_run_id: ${{ github.run_id }}",
                         "target_sha: ${{ github.sha }}",
                         "producer_result: ${{ needs.main-build-artifacts.result }}"):
            self.assertIn(required, call)
        build = workflow.split("  build-test:\n", 1)[1].split("\n  frontend-test:", 1)[0]
        self.assertIn("needs: [main-dotnet-build, main-build-artifacts, main-validation, performance-db]", build)
        self.assertIn("if: always()", build)
        self.assertNotIn("--duration", call)
        for producer in (workflow, (ROOT / "scripts/ci/package-pr-functional-runtime.sh").read_text()):
            self.assertIn("runtime-image-id", producer)


if __name__ == "__main__":
    unittest.main()
