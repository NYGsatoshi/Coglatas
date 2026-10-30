#!/usr/bin/env python3
"""Exercise actual gate exits and reject disconnected/filtered prerequisites."""
import copy
import os
from pathlib import Path
import shutil
import subprocess
import unittest
import required_check_contract as required

ROOT = Path(__file__).resolve().parents[2]
GATE = ROOT / 'scripts/ci/check-frontend-aggregate.sh'
BASH = str(Path(shutil.which('git')).parents[1] / 'bin/bash.exe') if os.name == 'nt' else 'bash'


class FrontendAggregateTests(unittest.TestCase):
    def invoke(self, scope='true', story='success', **changes):
        env = dict(os.environ, CHANGES_RESULT='success', FRONTEND_BUILD_RESULT='success', FRONTEND_CHECKS_RESULT='success', FRONTEND_STORYBOOK=scope, STORYBOOK_RESULT=story)
        env.update(changes)
        return subprocess.run([BASH, GATE.as_posix()], cwd=ROOT, env=env, capture_output=True, text=True)

    def test_success_requires_exact_routed_storybook_state(self):
        self.assertEqual(0, self.invoke().returncode)
        self.assertEqual(0, self.invoke('false', 'skipped').returncode)

    def test_every_failed_cancelled_skipped_missing_or_unknown_required_input_rejects(self):
        for key in ['CHANGES_RESULT', 'FRONTEND_BUILD_RESULT', 'FRONTEND_CHECKS_RESULT']:
            for state in ['failure', 'cancelled', 'skipped', '', 'unknown', 'success ']:
                for scope, story in [('true', 'success'), ('false', 'skipped')]:
                    with self.subTest(key=key, state=state, scope=scope):
                        self.assertNotEqual(0, self.invoke(scope, story, **{key: state}).returncode)

    def test_storybook_failure_or_unexpected_exemption_cannot_pass(self):
        for scope in ['true', 'false']:
            for state in ['success', 'failure', 'cancelled', 'skipped', '', 'unknown']:
                if (scope, state) in [('true', 'success'), ('false', 'skipped')]:
                    continue
                with self.subTest(scope=scope, state=state):
                    self.assertNotEqual(0, self.invoke(scope, state).returncode)
        for scope in ['', 'unknown', 'TRUE', 'false ']:
            with self.subTest(scope=scope):
                self.assertNotEqual(0, self.invoke(scope, 'skipped').returncode)

    def test_live_gate_keeps_all_required_sources_and_helper_binding(self):
        source = (ROOT / '.github/workflows/ci.yml').read_text()
        registry = copy.deepcopy(required.load_required_check_registry())
        registry['checks'] = [c for c in registry['checks'] if c['context'] == 'frontend-test']
        self.assertEqual([], required.required_check_errors('.github/workflows/ci.yml', source, registry))
        blocks = required._jobs(source)
        begin, end, _ = blocks['frontend-test']
        gate = '\n'.join(source.splitlines()[begin:end])
        for binding in [
            'CHANGES_RESULT: ${{ needs.changes.result }}',
            'FRONTEND_BUILD_RESULT: ${{ needs.frontend-build.result }}',
            'FRONTEND_CHECKS_RESULT: ${{ needs.frontend-checks.result }}',
            'FRONTEND_STORYBOOK: ${{ needs.changes.outputs.frontend_storybook }}',
            'STORYBOOK_RESULT: ${{ needs.storybook-build.result }}',
            'run: bash scripts/ci/check-frontend-aggregate.sh',
        ]:
            self.assertIn(binding, gate)
        for change in [
            source.replace('needs: [changes, frontend-build, frontend-checks, storybook-build]', 'needs: [changes, frontend-build, frontend-checks]'),
            source.replace("if: needs.changes.outputs.frontend_storybook == 'true'", 'if: true'),
            source.replace('  storybook-build:\n', '  missing-storybook-build:\n'),
        ]:
            with self.subTest(change=change[:40]):
                self.assertTrue(required.required_check_errors('.github/workflows/ci.yml', change, registry))


if __name__ == '__main__':
    unittest.main()