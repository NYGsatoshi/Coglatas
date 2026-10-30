#!/usr/bin/env python3
"""Fail-closed runtime selection, artifact provenance and protected-gate controls."""
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
BASH = str(Path(shutil.which('git')).parents[1] / 'bin/bash.exe') if os.name == 'nt' else 'bash'


def bash_path(value):
    path = Path(value).as_posix()
    return '/' + path[0].lower() + path[2:] if os.name == 'nt' else path


def run_bash(script, env, cwd=ROOT, arguments=()):
    command = [BASH, bash_path(script), *arguments]
    if os.name == 'nt':
        command = [BASH, '-c', 'export PATH="/usr/bin:/bin:$PATH"; exec bash "$@"', 'runtime-test', bash_path(script), *arguments]
    return subprocess.run(command, cwd=cwd, env=env, capture_output=True, text=True, encoding="utf-8", errors="replace")


class SecurityRuntimeIsolationTests(unittest.TestCase):
    def setUp(self):
        base = Path(os.environ.get('RUNNER_TEMP', ROOT / 'artifacts/ci-test-tmp'))
        base.mkdir(parents=True, exist_ok=True)
        self.tmp = tempfile.TemporaryDirectory(prefix='security-isolation-', dir=base)
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)

    def gate(self, scope='true', **changes):
        env = dict(os.environ, DOTNET_BUILD_RESULT='success', SECURITY_PREPARE_RESULT='success', SECURITY_RUNTIME_REQUIRED=scope, SECURITY_CORE_RESULT='success' if scope == 'true' else 'skipped', SECURITY_ZAP_RESULT='success' if scope == 'true' else 'skipped')
        env.update(changes)
        return run_bash(ROOT / 'scripts/ci/check-security-runtime-aggregate.sh', env)

    def test_required_gate_accepts_only_exact_selected_and_unselected_success(self):
        self.assertEqual(0, self.gate().returncode)
        self.assertEqual(0, self.gate('false').returncode)
        for key in ['DOTNET_BUILD_RESULT', 'SECURITY_PREPARE_RESULT']:
            for state in ['failure', 'cancelled', 'skipped', '', 'unknown']:
                for scope in ['true', 'false']:
                    with self.subTest(key=key, state=state, scope=scope):
                        self.assertNotEqual(0, self.gate(scope, **{key: state}).returncode)
        for key in ['SECURITY_CORE_RESULT', 'SECURITY_ZAP_RESULT']:
            for scope in ['true', 'false']:
                for state in ['success', 'failure', 'cancelled', 'skipped', '', 'unknown']:
                    if state == ('success' if scope == 'true' else 'skipped'):
                        continue
                    with self.subTest(key=key, state=state, scope=scope):
                        self.assertNotEqual(0, self.gate(scope, **{key: state}).returncode)
        for scope in ['', 'TRUE', 'unknown']:
            self.assertNotEqual(0, self.gate(scope).returncode)

    def prepare_runtime(self):
        directory = self.root / 'scripts/ci'
        directory.mkdir(parents=True)
        for name in ['run-security-runtime-parallel.sh', 'parallel-lane-lib.sh']:
            (directory / name).write_text((ROOT / 'scripts/ci' / name).read_text(), newline='\n')
        (directory / 'run-security-runtime-smoke.sh').write_text('''#!/usr/bin/env bash
set -euo pipefail
kind="${COGLATAS_SECURITY_CI_PROJECT##*-}"
printf '%s\\t%s\\t%s\\n' "$kind" "$1" "${COGLATAS_SECURITY_RUNTIME_FAILED:-unset}" >> "$RUNTIME_CALLS"
if [[ "$kind" == "${FAIL_KIND:-none}" && "$1" == "${FAIL_STAGE:-none}" ]]; then exit 19; fi
''', newline='\n')
        self.calls = self.root / 'calls.tsv'
        return directory / 'run-security-runtime-parallel.sh'

    def invoke_mode(self, script, arguments, **changes):
        self.calls.write_text('')
        env = dict(os.environ, COGLATAS_SECURITY_CI_PASSWORD='unit-test-only', RUNNER_TEMP=bash_path(self.root), RUNTIME_CALLS=bash_path(self.calls), GITHUB_RUN_ID='unit', GITHUB_ACTIONS='false')
        env.update(changes)
        env.pop('GITHUB_STEP_SUMMARY', None)
        result = run_bash(script.as_posix(), env, self.root, arguments)
        rows = [line.split('\t') for line in self.calls.read_text().splitlines()]
        return result, rows

    def test_actual_default_and_explicit_modes_dispatch_every_unchanged_stage(self):
        script = self.prepare_runtime()
        for arguments, kinds in [((), ['core', 'schemathesis', 'zap']), (('all',), ['core', 'schemathesis', 'zap']), (('core-schemathesis',), ['core', 'schemathesis']), (('zap',), ['zap'])]:
            with self.subTest(arguments=arguments):
                result, rows = self.invoke_mode(script, arguments)
                self.assertEqual(0, result.returncode, result.stderr)
                expected = {('core', 'sec05'), ('core', 'aud02'), ('schemathesis', 'sec04'), ('zap', 'sec06')}
                expected = {row for row in expected if row[0] in kinds}
                expected.update((kind, stage) for kind in kinds for stage in ['prepare', 'sec03', 'cleanup'])
                self.assertEqual(expected, {(row[0], row[1]) for row in rows})
                self.assertEqual(len(expected), len(rows), 'No duplicate or omitted stage')

    def test_invalid_modes_or_extra_arguments_cannot_start_a_runtime(self):
        script = self.prepare_runtime()
        for arguments in [('',), ('core',), ('unknown',), ('zap; echo bypass',), ('zap', 'all')]:
            with self.subTest(arguments=arguments):
                result, rows = self.invoke_mode(script, arguments)
                self.assertNotEqual(0, result.returncode)
                self.assertEqual([], rows)

    def test_native_stage_failures_stay_failed_and_clean_up(self):
        script = self.prepare_runtime()
        for mode, kind, stage in [('core-schemathesis', 'core', 'sec05'), ('core-schemathesis', 'schemathesis', 'sec04'), ('zap', 'zap', 'sec06')]:
            with self.subTest(kind=kind):
                result, rows = self.invoke_mode(script, (mode,), FAIL_KIND=kind, FAIL_STAGE=stage)
                self.assertNotEqual(0, result.returncode)
                self.assertIn([kind, 'cleanup', 'true'], rows)

    def test_protected_gate_binds_actual_results_and_both_complete_runtime_modes(self):
        from required_check_contract import _jobs
        source = (ROOT / '.github/workflows/ci.yml').read_text()
        blocks = _jobs(source)
        def block(name):
            begin, end, _ = blocks[name]
            return '\n'.join(source.splitlines()[begin:end])
        gate = block('security-scan')
        for binding in [
            'DOTNET_BUILD_RESULT: ${{ needs.dotnet-build.result }}',
            'SECURITY_PREPARE_RESULT: ${{ needs.security-prepare.result }}',
            'SECURITY_RUNTIME_REQUIRED: ${{ needs.dotnet-build.outputs.security_compose }}',
            'SECURITY_CORE_RESULT: ${{ needs.security-runtime-core.result }}',
            'SECURITY_ZAP_RESULT: ${{ needs.security-runtime-zap.result }}',
            'run: bash scripts/ci/check-security-runtime-aggregate.sh',
        ]:
            self.assertIn(binding, gate)
        self.assertIn('run: bash scripts/ci/run-security-runtime-parallel.sh core-schemathesis', block('security-runtime-core'))
        self.assertIn('run: bash scripts/ci/run-security-runtime-parallel.sh zap', block('security-runtime-zap'))
    def test_actual_contract_restore_rejects_wrong_source_digest_and_filename(self):
        source = (ROOT / '.github/workflows/ci.yml').read_text()
        parts = source.split('      - name: Verify exact-source contract before runtime execution\n')[1:]
        self.assertEqual(2, len(parts))
        bodies = [part.split('        run: |\n', 1)[1].split('\n      - name:', 1)[0] for part in parts]
        self.assertEqual(bodies[0], bodies[1])
        script = self.root / 'restore.sh'
        script.write_text('\n'.join(line[10:] for line in bodies[0].splitlines()) + '\n', newline='\n')
        subprocess.run(['git', 'init', '-q'], cwd=self.root, check=True)
        (self.root / 'tracked').write_text('source')
        subprocess.run(['git', 'add', 'tracked'], cwd=self.root, check=True)
        subprocess.run(['git', '-c', 'user.name=CI Unit', '-c', 'user.email=ci-unit@example.test', 'commit', '-qm', 'source'], cwd=self.root, check=True)
        sha = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=self.root, text=True).strip()
        bundle = self.root / 'security-runtime-contract'
        bundle.mkdir()
        document = b'{"openapi":"3.1.0"}\n'
        digest = hashlib.sha256(document).hexdigest()
        env = dict(os.environ, RUNNER_TEMP=bash_path(self.root), GITHUB_SHA=sha)
        for mutation in ['valid', 'stamp', 'github-sha', 'digest', 'filename', 'extra-line', 'dirty-source']:
            (bundle / 'source-sha').write_text(sha + '\n', newline='\n')
            (bundle / 'coglatas-openapi.json').write_bytes(document)
            (bundle / 'openapi.sha256').write_text(digest + '  coglatas-openapi.json\n', newline='\n')
            (self.root / 'tracked').write_text('source')
            active = dict(env)
            if mutation == 'stamp': (bundle / 'source-sha').write_text('0' * 40 + '\n', newline='\n')
            if mutation == 'github-sha': active['GITHUB_SHA'] = '0' * 40
            if mutation == 'digest': (bundle / 'coglatas-openapi.json').write_bytes(b'changed')
            if mutation == 'filename': (bundle / 'openapi.sha256').write_text(digest + '  ../tracked\n', newline='\n')
            if mutation == 'extra-line':
                with (bundle / 'openapi.sha256').open('a') as output: output.write(digest + '  coglatas-openapi.json\n')
            if mutation == 'dirty-source': (self.root / 'tracked').write_text('changed')
            result = run_bash(script.as_posix(), active, self.root)
            with self.subTest(mutation=mutation):
                self.assertEqual(mutation == 'valid', result.returncode == 0, result.stderr)
        self.assertEqual(document, (self.root / 'artifacts/openapi/coglatas-openapi.json').read_bytes())


if __name__ == '__main__':
    unittest.main()