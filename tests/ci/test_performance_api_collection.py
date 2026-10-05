from __future__ import annotations

import io
import json
import os
import subprocess
import sys
import tempfile
import unittest
from contextlib import ExitStack, redirect_stderr, redirect_stdout
from pathlib import Path
from unittest.mock import MagicMock, patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts/performance'))
import api_k6
from common import fixture_hash, load_profile
from test_performance_api import raw_result


class ApiCollectionFailureTests(unittest.TestCase):
    def collect(self, failure=None):
        contract = api_k6.load_contract()
        manifest, profile = load_profile('small')
        fixture = {
            'schemaVersion': 1, 'fixtureVersion': 1, 'seedManifestVersion': manifest['seedManifestVersion'],
            'profile': 'small', 'seed': profile['seed'], 'fixtureHash': fixture_hash('small', fixture_version=1),
            'migrationStatus': 'current', 'complete': True, 'cardinalities': profile['counts'],
            'focus': profile['focus'],
            'identities': {key: 'synthetic-protected-value' for key in
                           ('tenantSlug', 'operatorEmail', 'workspaceId', 'taskListProjectId', 'ganttProjectId', 'kanbanProjectId')},
        }
        with tempfile.TemporaryDirectory() as directory, ExitStack() as patches:
            root = Path(directory)
            fixture_path = root / 'fixture.json'
            fixture_path.write_text(json.dumps(fixture))
            environment_path = root / 'environment.json'
            environment_path.write_text(json.dumps({'commitSha': 'a' * 40}))
            output = root / 'output.json'
            environment = {
                'COGLATAS_PERFORMANCE_BASE_URL': 'http://127.0.0.1:18080',
                'COGLATAS_PERFORMANCE_FIXTURE_EVIDENCE': str(fixture_path),
                'COGLATAS_PERFORMANCE_ENVIRONMENT_EVIDENCE': str(environment_path),
                'COGLATAS_PERFORMANCE_COMPOSE_PROJECT': 'synthetic-trial',
                'COGLATAS_PERFORMANCE_DB_CAPTURE_ENABLED': 'false',
                'COGLATAS_PERFORMANCE_API_DIAGNOSTICS_ENABLED': 'false',
                'COGLATAS_PERFORMANCE_PASSWORD': 'synthetic-protected-password',
            }
            patches.enter_context(patch.dict(os.environ, environment))
            patches.enter_context(patch.object(os, 'getuid', return_value=1000, create=True))
            patches.enter_context(patch.object(os, 'getgid', return_value=1000, create=True))

            def docker(command, **kwargs):
                if command[-1] == 'version':
                    if failure == 'version-timeout':
                        raise subprocess.TimeoutExpired(command, 120, output=b'protected-output', stderr=b'protected-body')
                    return subprocess.CompletedProcess(command, 1 if failure == 'version' else 0,
                                                       f"k6 v{contract['k6Version']} (synthetic)".encode(), b'protected-version-output')
                self.assertEqual(180, kwargs['timeout'])
                self.assertEqual(subprocess.PIPE, kwargs['stdout'])
                self.assertEqual(subprocess.PIPE, kwargs['stderr'])
                if failure == 'process-timeout':
                    raise subprocess.TimeoutExpired(command, 180, output=b'protected-output', stderr=b'protected-body')
                mount = Path(command[command.index('-v') + 1].removesuffix(':/work'))
                if failure not in ('missing-summary', 'failed-missing-summary'):
                    raw = raw_result()
                    if failure == 'authentication':
                        raw['authFailures'] = 1
                    (mount / 'result.json').write_text('protected-invalid-json' if failure == 'invalid-summary' else json.dumps(raw))
                return subprocess.CompletedProcess(command, 1 if failure in ('process-exit', 'failed-missing-summary') else 0,
                                                   b'protected-output', b'protected-body')

            patches.enter_context(patch.object(api_k6.subprocess, 'run', side_effect=docker))
            response = MagicMock()
            response.__enter__.return_value.status = 503 if failure == 'health' else 200
            patches.enter_context(patch.object(api_k6.urllib.request, 'urlopen', return_value=response))
            patches.enter_context(patch.object(sys, 'argv', ['api_k6.py', 'collect', '--output', str(output)]))
            stdout, stderr = io.StringIO(), io.StringIO()
            with redirect_stdout(stdout), redirect_stderr(stderr):
                status = api_k6.main()
            document = json.loads(output.read_text()) if output.exists() else None
            return status, stdout.getvalue(), stderr.getvalue(), document

    def test_failed_collection_reports_only_a_fixed_stage_and_never_emits_measurements(self):
        cases = {
            'version': 'k6-version', 'version-timeout': 'k6-version', 'process-timeout': 'k6-process',
            'missing-summary': 'k6-summary', 'failed-missing-summary': 'k6-summary', 'invalid-summary': 'k6-summary',
            'process-exit': 'k6-exit', 'authentication': 'measurement-contract', 'health': 'post-collection-health',
        }
        for failure, stage in cases.items():
            with self.subTest(failure=failure):
                status, stdout, stderr, document = self.collect(failure)
                self.assertEqual(2, status)
                self.assertEqual('', stdout)
                self.assertIsNone(document)
                self.assertIn(f'collection failure stage: {stage}; protected output discarded', stderr)
                for private in ('protected-password', 'protected-value', 'protected-body', 'protected-output',
                                'protected-invalid-json', '127.0.0.1', 'Traceback', 'result.json'):
                    self.assertNotIn(private, stderr)

    def test_success_keeps_the_existing_normalized_document(self):
        status, stdout, stderr, document = self.collect()
        self.assertEqual((0, '', ''), (status, stdout, stderr))
        self.assertEqual(api_k6.normalize(raw_result(), api_k6.load_contract()), document['measurements'])
        self.assertEqual('a' * 40, document['headSha'])
        self.assertNotIn('protected', json.dumps(document))

    def test_untrusted_stage_names_cannot_enter_public_diagnostics(self):
        error = api_k6.ApiCollectionError('protected-cookie\nforged-stage')
        self.assertEqual('unknown', error.stage)
        self.assertNotIn('protected-cookie', str(error))


if __name__ == '__main__':
    unittest.main()
