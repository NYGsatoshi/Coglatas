"""Offline recovery eligibility and strict input regressions; no benchmark requests."""
import copy
import importlib.util
import json
import os
import sys
import tempfile
import unittest
import zipfile
from io import BytesIO
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts/performance'))
from common import PerformanceContractError
from diagnostic_json import decode_json
from diagnostic_recovery import declaration_digest, digest, load_json, validate


def fixture():
    rule = load_json(ROOT / 'performance/diagnostic-zero-measurement-recovery.json')
    # Synthetic immutable archive/log digests replace only external evidence in
    # this offline fixture. The CLI always reads the repository's real rule.
    log = b'announcement-diagnostic.py", line 60, in main\nfixture = json.loads(Path(\nJSONDecodeError: Unexpected UTF-8 BOM\n'
    execution = {'runId': 37695270091, 'attempt': 1, 'sha': rule['predecessor']['headSha']}
    files = {'diagnostic-execution.json': json.dumps(execution).encode(),
             'medium/fixture.json': b'\xef\xbb\xbf{"fixtureHash":"synthetic"}',
             'medium/environment.json': b'{}', 'medium/preflight.json': b'{}', 'medium/warmup.json': b'{}'}
    buffer = BytesIO()
    with zipfile.ZipFile(buffer, 'w') as archive:
        for name, data in files.items():
            archive.writestr(name, data)
    data = buffer.getvalue()
    rule['predecessor']['archiveFiles'] = {name: digest(value) for name, value in files.items()}
    rule['predecessor']['artifactSha256'] = digest(data)
    rule['predecessor']['failureLogSha256'] = digest(log)
    predecessor = rule['predecessor']
    run = {'id': predecessor['runId'], 'run_attempt': 1, 'head_sha': predecessor['headSha'],
           'head_branch': predecessor['branch'], 'workflow_id': predecessor['workflowId'],
           'event': 'workflow_dispatch', 'status': 'completed', 'conclusion': 'failure'}
    proof = {'predecessor': copy.deepcopy(predecessor), 'run': run, 'originalDispatchTotal': 1,
             'originalDispatchRuns': [copy.deepcopy(run)], 'artifacts': [{'id': predecessor['artifactId'],
             'expired': False, 'digest': 'sha256:' + digest(data)}],
             'replacementDispatchTotal': 0, 'replacementDispatchRuns': []}
    identity = 'perf05-announcement-diagnostic-recovery-unit-test'
    replacement = {'schemaVersion': 1, 'ruleId': rule['ruleId'],
        'purpose': 'DIAGNOSTIC_ONLY_NOT_ACCEPTANCE_NOT_BASELINE', 'diagnosticId': identity,
        'dispatchId': '12345678-1234-4123-8123-123456789abc', 'predecessorRun': '37695270091/1',
        'recoveryOf': predecessor['diagnosticId'], 'recoveryDepth': 1,
        'fixedCollectorCommit': rule['knownFixCommit'], 'workload': copy.deepcopy(rule['workload']),
        'collectorFiles': copy.deepcopy(rule['fixedCollectorFiles']), 'branch': 'diagnostics/' + identity,
        'declarationReference': 'https://github.com/NYGsatoshi/Coglatas/pull/1046#issuecomment-1234'}
    replacement['immutableDigest'] = declaration_digest(replacement)
    return rule, proof, replacement, data, log


class DiagnosticInputTests(unittest.TestCase):
    def test_fixed_collector_bytes_match_versioned_rule(self):
        rule = load_json(ROOT / 'performance/diagnostic-zero-measurement-recovery.json')
        for name, expected in rule['fixedCollectorFiles'].items():
            with self.subTest(name=name):
                self.assertEqual(expected, digest((ROOT / name).read_bytes()))

    def test_before_fix_actual_bom_input_fails(self):
        data = b'\xef\xbb\xbf{"fixtureHash":"same-input"}'
        with self.assertRaises(json.JSONDecodeError):
            json.loads(data.decode('utf-8'))
        self.assertEqual({'fixtureHash': 'same-input'}, decode_json(data))

    def test_utf8_with_and_without_bom(self):
        for prefix in (b'', b'\xef\xbb\xbf'):
            with self.subTest(prefix=prefix):
                self.assertEqual({'value': 'fixture'}, decode_json(prefix + b'{"value":"fixture"}'))

    def test_corrupt_inputs_fail_closed(self):
        inputs = [b'', b'\xef\xbb\xbf', b'not json', b'{"a":', b'{"a":1', b'{"a":1}\n{"b":2}',
                  b'{"a":1,"a":2}', b'{"a":NaN}', b'{"a":Infinity}', b'{"a":\xff}',
                  b'\xff\xfe{\x00}\x00', b'\xfe\xff\x00{\x00}', b'\xef\xbb\xbf\xef\xbb\xbf{}',
                  b'[]', b'null', b'column,column\n1,2', b'{"a":1} trailing']
        for data in inputs:
            with self.subTest(data=data), self.assertRaises(PerformanceContractError):
                decode_json(data)

    def test_real_collector_declares_bounds_without_network(self):
        spec = importlib.util.spec_from_file_location('announcement_diagnostic', ROOT / 'scripts/performance/announcement-diagnostic.py')
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            path = directory / 'fixture.json'
            path.write_bytes(b'\xef\xbb\xbf{"fixtureHash":"same-input"}')
            environment = {'GITHUB_RUN_ATTEMPT': '1', 'GITHUB_RUN_ID': '123', 'GITHUB_SHA': 'b' * 40,
                'COGLATAS_PERFORMANCE_PROFILE': 'medium', 'COGLATAS_PERFORMANCE_DB_CAPTURE_ENABLED': 'true',
                'COGLATAS_PERFORMANCE_API_DIAGNOSTICS_ENABLED': 'true',
                'COGLATAS_PERFORMANCE_FIXTURE_EVIDENCE': str(path),
                'COGLATAS_PERFORMANCE_COMPOSE_PROJECT': 'offline-only', 'COGLATAS_PERFORMANCE_BASE_URL': 'http://127.0.0.1:18080'}
            with patch.dict(os.environ, environment), patch.object(module, 'OUT', directory / 'output'), \
                    patch.object(module.subprocess, 'check_output', return_value='123\n'), \
                    patch.object(module, 'login', side_effect=RuntimeError('stop before network')), \
                    patch.object(module, 'request') as request:
                with self.assertRaisesRegex(RuntimeError, 'stop before network'):
                    module.main()
                request.assert_not_called()
            manifest = load_json(directory / 'output/prospective-manifest.json')
            self.assertEqual((5, 20, 1, 5, 1, 0, False),
                (manifest['groups'], manifest['requestsPerGroup'], manifest['page'], manifest['pageSize'],
                 manifest['warmups'], manifest['retries'], manifest['earlyStop']))
            self.assertFalse((directory / 'output/client-samples.json').exists())


class RecoveryEligibilityTests(unittest.TestCase):
    def test_single_replacement_is_eligible_before_dispatch_and_in_run(self):
        rule, proof, replacement, data, log = fixture()
        self.assertTrue(validate(rule, proof, replacement, data, log)['eligible'])
        current = {'id': 999, 'run_attempt': 1, 'head_branch': replacement['branch'], 'event': 'workflow_dispatch'}
        proof['replacementDispatchTotal'] = 1
        proof['replacementDispatchRuns'] = [current]
        self.assertTrue(validate(rule, proof, replacement, data, log, 'in-run', current)['eligible'])

    def test_all_changed_workload_dimensions_fail(self):
        mutations = {'scenario': 'task.list', 'productSourceSha': '0' * 40, 'sourceTree': '0' * 40,
                     'profile': 'small', 'fixtureHash': '0' * 64, 'groups': 4, 'requestsPerGroup': 21,
                     'measuredRequests': 101, 'order': 'REVERSED', 'page': 2, 'pageSize': 6,
                     'runner': 'ubuntu-latest', 'environmentClass': 'different', 'runtimeMode': 'development',
                     'setup': 'none', 'explicitWarmups': 0, 'collectionWindow': 'longer', 'earlyStop': True,
                     'retries': 1, 'cpuSelector': 'favorable', 'acceptanceCredit': True, 'baselineEnrollment': True,
                     'instrumentation': 'changed', 'baselineTree': '0' * 40}
        for field, value in mutations.items():
            with self.subTest(field=field):
                rule, proof, replacement, data, log = fixture()
                replacement['workload'][field] = value
                replacement['immutableDigest'] = declaration_digest(replacement)
                with self.assertRaises(PerformanceContractError):
                    validate(rule, proof, replacement, data, log)
        for name in ('performance/comparison-policy.json', 'performance/datasets.json',
                     'src/Coglatas.Web/Testing/AnnouncementDiagnosticsStartupFilter.cs'):
            with self.subTest(file=name):
                rule, proof, replacement, data, log = fixture()
                replacement['workload']['files'][name] = '0' * 64
                replacement['immutableDigest'] = declaration_digest(replacement)
                with self.assertRaises(PerformanceContractError):
                    validate(rule, proof, replacement, data, log)

    def test_zero_measurement_and_original_identity_negatives(self):
        changes = [('measurements', 1), ('validTimingSamples', 1), ('loopStarted', True),
                   ('runAttempt', 2), ('recoveryOf', 'previous-recovery')]
        for key, value in changes:
            with self.subTest(key=key):
                rule, proof, replacement, data, log = fixture()
                proof['predecessor'][key] = value
                with self.assertRaises(PerformanceContractError):
                    validate(rule, proof, replacement, data, log)
        for field, value in [('run_attempt', 2), ('conclusion', 'success'), ('status', 'in_progress'), ('event', 'push')]:
            with self.subTest(field=field):
                rule, proof, replacement, data, log = fixture()
                proof['run'][field] = value
                with self.assertRaises(PerformanceContractError):
                    validate(rule, proof, replacement, data, log)

    def test_missing_retained_evidence_and_first_dispatch_fail(self):
        for kind in ('archive', 'log', 'artifact', 'expired', 'extra-original', 'incomplete-inventory'):
            with self.subTest(kind=kind):
                rule, proof, replacement, data, log = fixture()
                if kind == 'archive': data = b''
                if kind == 'log': log = b''
                if kind == 'artifact': proof['artifacts'] = []
                if kind == 'expired': proof['artifacts'][0]['expired'] = True
                if kind == 'extra-original':
                    proof['originalDispatchRuns'].append(copy.deepcopy(proof['run']))
                    proof['originalDispatchTotal'] = 2
                if kind == 'incomplete-inventory': proof['originalDispatchTotal'] = 2
                with self.assertRaises(PerformanceContractError):
                    validate(rule, proof, replacement, data, log)

    def test_reused_identity_chain_and_second_recovery_fail(self):
        changes = [('diagnosticId', 'perf05-announcement-diagnostic-20261008'), ('dispatchId', '37695270091'),
                   ('recoveryDepth', 2), ('recoveryOf', 'perf05-announcement-diagnostic-recovery-earlier'),
                   ('predecessorRun', '999/1'), ('purpose', 'API_ACCEPTANCE'), ('collectorFiles', {})]
        for key, value in changes:
            with self.subTest(key=key):
                rule, proof, replacement, data, log = fixture()
                replacement[key] = value
                replacement['immutableDigest'] = declaration_digest(replacement)
                with self.assertRaises(PerformanceContractError):
                    validate(rule, proof, replacement, data, log)
        rule, proof, replacement, data, log = fixture()
        current = {'id': 999, 'run_attempt': 1, 'head_branch': replacement['branch'], 'event': 'workflow_dispatch'}
        proof['replacementDispatchTotal'] = 1
        proof['replacementDispatchRuns'] = [current]
        with self.assertRaises(PerformanceContractError):
            validate(rule, proof, replacement, data, log)
        for attempt, count, run_id in [(2, 1, 999), (1, 2, 999), (1, 1, 37695270091)]:
            with self.subTest(attempt=attempt, count=count, run_id=run_id):
                current = {'id': run_id, 'run_attempt': attempt, 'head_branch': replacement['branch'], 'event': 'workflow_dispatch'}
                proof['replacementDispatchTotal'] = count
                proof['replacementDispatchRuns'] = [current] * count
                with self.assertRaises(PerformanceContractError):
                    validate(rule, proof, replacement, data, log, 'in-run', current)


if __name__ == '__main__':
    unittest.main()
