from __future__ import annotations

import copy
import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts/performance'))
import api_diagnostics
import api_k6


def clients(contract, config):
    rows = []
    for sample in range(contract['profile']['iterations']):
        for scenario in contract['scenarios']:
            rows.append({'captureId': config['capturePrefix'] + format(len(rows), '016x'),
                         'scenario': scenario['id'], 'sampleOrdinal': sample + 1,
                         'trialOrdinal': config['trialOrdinal'], 'warmupIdentity': config['capturePrefix'],
                         'warmupCompletedUtc': '2026-10-05T00:00:00.000Z',
                         **{key: 10 for key in api_diagnostics.CLIENT_NUMBERS}})
    return rows


def server(client):
    return {**{key: client[key] for key in ('captureId', 'scenario', 'sampleOrdinal', 'trialOrdinal', 'warmupIdentity')},
            'schemaVersion': 1, 'stateClass': 'completed', 'status': 200,
            'monotonicStartTicks': 100, 'monotonicEndTicks': 110, 'monotonicFrequency': 1000,
            'serverProcessingElapsedMs': 10, 'processCpuTimeDeltaMs': 1, 'allocatedBytesDelta': 10,
            'fixtureResetCompletedUtc': '2026-10-05T00:00:00+00:00', 'fixtureResetCompletedMonotonicTicks': 1,
            'database': {'commandCount': 1, 'failedCommandCount': 0, 'summedCommandDurationMs': 2,
                         'slowestCommandDurationsMs': [2], 'connectionOpenCount': 1, 'summedConnectionOpenDurationMs': 1},
            **{key: {**{name: 1 for name in api_diagnostics.RUNTIME_NUMBERS}, 'gcCollections': [0, 0, 0]}
               for key in ('runtimeBefore', 'runtimeAfter')},
            **{key: {'eventDispatches': 0, 'signalRSends': 0, 'allEfCommands': 1,
                     'workerStarts': [0, 0, 0, 0], 'activeWorkers': [0, 0, 0, 0]}
               for key in ('activityBefore', 'activityAfter')}}


class DiagnosticProjectionTests(unittest.TestCase):
    def setUp(self):
        self.contract = api_k6.load_contract()
        self.config = {'capturePrefix': '0123456789abcdef', 'trialOrdinal': 1}
        self.clients = clients(self.contract, self.config)

    def test_fixed_projection_preserves_order_and_original_latency(self):
        result = api_diagnostics.project_clients(self.clients, self.config, self.contract)
        self.assertEqual(len(self.clients), len(result))
        self.assertEqual(self.clients[-1]['captureId'], result[-1]['captureId'])
        self.assertEqual(10, result[-1]['requestElapsedMs'])

    def test_private_k6_points_cross_vu_summary_boundary_without_exporting_raw_tags(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'events.jsonl'
            points = [{'type': 'Point', 'metric': 'http_req_duration', 'data': {'tags': {'url': 'protected-url'}}}]
            for row in self.clients:
                points.append({'type': 'Point', 'metric': 'perf_diagnostic_request_elapsed_ms',
                               'data': {'value': row['requestElapsedMs'],
                                        'tags': {**{key: str(value) for key, value in row.items()}, 'cookie': 'protected-cookie'}}})
            path.write_text('\n'.join(json.dumps(point) for point in points) + '\n')
            result = api_diagnostics.read_client_events(path, self.config, self.contract)
            self.assertEqual(260, len(result))
            self.assertNotIn('protected', json.dumps(result))

    def test_private_k6_point_value_mismatch_and_duplicate_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'events.jsonl'
            client = self.clients[0]
            point = {'type': 'Point', 'metric': 'perf_diagnostic_request_elapsed_ms',
                     'data': {'value': 11, 'tags': {key: str(value) for key, value in client.items()}}}
            path.write_text(json.dumps(point) + '\n')
            with self.assertRaises(api_k6.PerformanceContractError):
                api_diagnostics.read_client_events(path, self.config, self.contract)
            point['data']['value'] = 10
            path.write_text((json.dumps(point) + '\n') * 2)
            with self.assertRaises(api_k6.PerformanceContractError):
                api_diagnostics.read_client_events(path, self.config, self.contract)

    def test_missing_duplicate_reordered_and_excess_samples_are_rejected(self):
        for rows in (self.clients[:-1], self.clients + [self.clients[0]],
                     self.clients[:1] + self.clients[:1] + self.clients[2:], list(reversed(self.clients))):
            with self.subTest(count=len(rows)):
                with self.assertRaises(api_k6.PerformanceContractError):
                    api_diagnostics.project_clients(rows, self.config, self.contract)

    def test_ordinal_and_capture_injection_are_rejected(self):
        for key, value in (('scenario', 'private-task-title'), ('captureId', '../escape'),
                           ('trialOrdinal', 2), ('warmupIdentity', 'private-cookie'),
                           ('warmupCompletedUtc', 'protected-body'), ('requestElapsedMs', float('nan'))):
            rows = copy.deepcopy(self.clients)
            rows[0][key] = value
            with self.subTest(key=key):
                with self.assertRaises(api_k6.PerformanceContractError):
                    api_diagnostics.project_clients(rows, self.config, self.contract)

    def test_unknown_and_protected_server_fields_are_never_copied(self):
        raw = server(self.clients[0])
        raw.update({'sql': "SELECT 'protected-token'", 'authorization': 'protected-token', 'body': 'protected-body'})
        raw['runtimeBefore']['path'] = 'protected-path'
        raw['unavailableReasons'] = {'jit': 'protected-title'}
        self.assertNotIn('protected', json.dumps(api_diagnostics.project_server(raw, self.clients[0])))
        for target in ('serverProcessingElapsedMs', 'allocatedBytesDelta'):
            raw[target] = 'protected-secret'
            with self.assertRaises(api_k6.PerformanceContractError): api_diagnostics.project_server(raw, self.clients[0])
            raw[target] = 1

    def test_wrong_identity_clock_and_unbounded_arrays_are_rejected(self):
        variants = []
        raw = server(self.clients[0]); raw['captureId'] = '0' * 32; variants.append(raw)
        raw = server(self.clients[0]); raw['monotonicEndTicks'] = 50; variants.append(raw)
        raw = server(self.clients[0]); raw['database']['slowestCommandDurationsMs'] = [1] * 6; variants.append(raw)
        raw = server(self.clients[0]); raw['activityBefore']['workerStarts'] = [1] * 100; variants.append(raw)
        for raw in variants:
            with self.assertRaises(api_k6.PerformanceContractError): api_diagnostics.project_server(raw, self.clients[0])

    def test_unsupported_kernel_counters_remain_explicitly_unavailable(self):
        raw = server(self.clients[0])
        for runtime in ('runtimeBefore', 'runtimeAfter'):
            for key in api_diagnostics.RUNTIME_NUMBERS:
                if key.startswith(('host', 'cgroup')): raw[runtime][key] = None
        result = api_diagnostics.project_server(raw, self.clients[0])
        self.assertIsNone(result['runtimeBefore']['hostCpuIdleTicks'])
        self.assertIn('hostOrCgroupNull', result['unavailableReasons'])

    def test_complete_capture_cohort_retains_all_samples_and_digests(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for client in self.clients:
                (root / (client['captureId'] + '.json')).write_text(json.dumps(server(client)))
            output = root / 'output.json'
            api_diagnostics.collect_sidecar({'diagnostics': self.clients}, self.config, self.contract, root,
                                            output, {'commitSha': 'a' * 40}, 'b' * 64, 'c' * 64)
            result = json.loads(output.read_text())
            self.assertTrue(result['complete'])
            self.assertEqual(260, len(result['samples']))
            self.assertEqual(64, len(result['samples'][0]['serverRawDigest']))
            self.assertEqual('investigation-only-not-gate-or-baseline-evidence', result['purpose'])

    def test_partial_capture_is_retained_before_fail_closed_and_never_replayed(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            client = self.clients[0]
            (root / (client['captureId'] + '.json')).write_text(json.dumps(server(client)))
            output = root / 'output.json'
            with self.assertRaises(api_k6.PerformanceContractError):
                api_diagnostics.collect_sidecar({'diagnostics': [client]}, self.config, self.contract, root,
                                                output, {'commitSha': 'a' * 40}, 'b' * 64, 'c' * 64)
            result = json.loads(output.read_text())
            self.assertFalse(result['complete'])
            self.assertEqual(1, len(result['samples']))
            self.assertEqual(2, len(list(root.glob('*.json'))))


if __name__ == '__main__':
    unittest.main()
