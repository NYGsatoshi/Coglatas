#!/usr/bin/env python3
"""Reject unbound or modified prebuilt reference inputs before native probes."""
import importlib.util
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('runtime_probes', Path(__file__).with_name('test_av_mig_runtime_cli.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ReferenceBoundaryTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / 'src').mkdir()
        (self.root / 'src/fixture.cs').write_text('class Fixture {}\n')
        self.git('init', '-q', '-b', 'measurement')
        self.git('add', 'src/fixture.cs')
        self.git('-c', 'user.name=CI control', '-c', 'user.email=ci@example.invalid', 'commit', '-qm', 'fixture')
        self.sha = self.git('rev-parse', 'HEAD').strip()
        self.stamp = self.root / 'artifacts/ci/dotnet-build-sha'
        self.stamp.parent.mkdir(parents=True)
        self.stamp.write_text(self.sha + '\n')
        self.environment = {'COGLATAS_SECURITY_AVMIG_REUSE_REFERENCES': '1', 'GITHUB_ACTIONS': 'true', 'GITHUB_EVENT_NAME': 'pull_request', 'GITHUB_SHA': self.sha}
        root_patch = patch.object(module, 'ROOT', self.root)
        root_patch.start()
        self.addCleanup(root_patch.stop)

    def git(self, *args):
        return subprocess.check_output(['git', *args], cwd=self.root, text=True, stderr=subprocess.STDOUT)

    def run_guard(self, **changes):
        environment = dict(self.environment, **changes)
        with patch.dict(os.environ, environment):
            return module.use_prebuilt_references()

    def test_explicit_pr_mode_accepts_bound_pristine_inputs(self):
        self.assertTrue(self.run_guard())

    def test_default_mode_retains_full_baseline_without_a_binding(self):
        self.stamp.unlink()
        self.assertFalse(self.run_guard(COGLATAS_SECURITY_AVMIG_REUSE_REFERENCES='0'))

    def test_invalid_modes_and_non_pr_contexts_fail_closed(self):
        for change in [{'COGLATAS_SECURITY_AVMIG_REUSE_REFERENCES': 'true'}, {'GITHUB_ACTIONS': 'false'}, {'GITHUB_EVENT_NAME': 'push'}]:
            with self.subTest(change=change), self.assertRaises(RuntimeError):
                self.run_guard(**change)

    def test_missing_binding_cannot_enable_reuse(self):
        self.stamp.unlink()
        with self.assertRaises(FileNotFoundError):
            self.run_guard()

    def test_foreign_producer_or_analyzed_revision_is_rejected(self):
        with self.assertRaises(RuntimeError):
            self.run_guard(GITHUB_SHA='0' * 40)
        self.stamp.write_text('0' * 40)
        with self.assertRaises(RuntimeError):
            self.run_guard()

    def test_tracked_source_edit_after_stamping_is_rejected(self):
        (self.root / 'src/fixture.cs').write_text('class Changed {}\n')
        with self.assertRaises(RuntimeError):
            self.run_guard()

    def test_untracked_source_cannot_hide_behind_the_stamp(self):
        (self.root / 'src/extra.cs').write_text('class Extra {}\n')
        with self.assertRaises(RuntimeError):
            self.run_guard()


if __name__ == '__main__':
    unittest.main()