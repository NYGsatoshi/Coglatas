"""Bounded scalar sidecar projection; diagnostics never participate in comparator decisions."""
from __future__ import annotations

import hashlib
import json
import math
import re
import time
from pathlib import Path

from common import PerformanceContractError, load_json, write_json_atomic

RUNTIME_NUMBERS = ('processAllocatedBytes', 'availableWorkers', 'availableIoThreads', 'threadPoolQueueLength',
                   'processCpuTimeMs', 'availableProcessorCount', 'hostCpuTotalTicks', 'hostCpuIdleTicks',
                   'cgroupUsageMicroseconds', 'cgroupThrottledMicroseconds', 'cgroupThrottlePeriods',
                   'cgroupQuotaMicroseconds', 'cgroupPeriodMicroseconds')
CLIENT_NUMBERS = ('clientStartedUtcMilliseconds', 'clientFinishedUtcMilliseconds', 'requestElapsedMs',
                  'totalClientElapsedMs', 'blockedMs', 'connectingMs', 'sendingMs', 'waitingMs', 'receivingMs')


def read_client_events(path: Path, config, contract):
    if path.stat().st_size > 32 * 1024 * 1024:
        raise PerformanceContractError('private diagnostic stream exceeds its bounded size')
    rows = []
    with path.open(encoding='utf-8') as handle:
        for line in handle:
            if len(line) > 16384:
                raise PerformanceContractError('private diagnostic point exceeds its bounded size')
            point = json.loads(line)
            if point.get('type') != 'Point' or point.get('metric') != 'perf_diagnostic_request_elapsed_ms':
                continue
            data = point['data']
            tags = data['tags']
            row = {key: tags.get(key) for key in ('captureId', 'scenario', 'warmupIdentity', 'warmupCompletedUtc')}
            for key in ('sampleOrdinal', 'trialOrdinal'):
                if not re.fullmatch(r'[1-9][0-9]{0,2}', tags.get(key, '')):
                    raise PerformanceContractError('invalid numeric diagnostic identity')
                row[key] = int(tags[key])
            for key in CLIENT_NUMBERS:
                value = tags.get(key)
                if not isinstance(value, str) or not re.fullmatch(r'[0-9]+(?:\.[0-9]+)?(?:e[+-]?[0-9]+)?', value):
                    raise PerformanceContractError('invalid numeric diagnostic tag')
                row[key] = number(float(value))
            if row['requestElapsedMs'] != number(data.get('value')):
                raise PerformanceContractError('diagnostic metric value differs from its timing tag')
            rows.append(row)
            if len(rows) > len(contract['scenarios']) * contract['profile']['iterations']:
                raise PerformanceContractError('diagnostic stream has excess sample points')
    return project_clients(rows, config, contract, require_complete=False)


def number(value, *, nullable=False, minimum=0):
    if value is None and nullable:
        return None
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value < minimum:
        raise PerformanceContractError('invalid scalar diagnostic')
    return value


def counters(value, count):
    if not isinstance(value, list) or len(value) != count:
        raise PerformanceContractError('invalid bounded diagnostic counter array')
    return [number(item) for item in value]


def utc(value, *, nullable=False):
    if value is None and nullable:
        return None
    if not isinstance(value, str) or not re.fullmatch(r'\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d{1,7})?(?:Z|\+00:00)', value):
        raise PerformanceContractError('invalid diagnostic completion timestamp')
    return value


def project_server(raw, client):
    for key in ('captureId', 'scenario', 'sampleOrdinal', 'trialOrdinal', 'warmupIdentity'):
        if raw.get(key) != client[key]:
            raise PerformanceContractError('diagnostic identity mismatch')
    if raw.get('schemaVersion') != 1 or raw.get('stateClass') not in ('completed', 'pipeline-failed'):
        raise PerformanceContractError('invalid diagnostic state class')
    result = {key: client[key] for key in ('captureId', 'scenario', 'sampleOrdinal', 'trialOrdinal', 'warmupIdentity')}
    for key in ('status', 'monotonicStartTicks', 'monotonicEndTicks', 'monotonicFrequency',
                'serverProcessingElapsedMs', 'processCpuTimeDeltaMs', 'allocatedBytesDelta'):
        result[key] = number(raw.get(key))
    if result['monotonicFrequency'] <= 0 or result['monotonicEndTicks'] < result['monotonicStartTicks']:
        raise PerformanceContractError('invalid monotonic diagnostic boundary')
    result['stateClass'] = raw['stateClass']
    result['fixtureResetCompletedUtc'] = utc(raw.get('fixtureResetCompletedUtc'))
    result['fixtureResetCompletedMonotonicTicks'] = number(raw.get('fixtureResetCompletedMonotonicTicks'))
    if result['fixtureResetCompletedMonotonicTicks'] > result['monotonicStartTicks']:
        raise PerformanceContractError('measurement precedes fixture reset completion')
    database = raw['database']
    result['database'] = {key: number(database.get(key)) for key in (
        'commandCount', 'failedCommandCount', 'summedCommandDurationMs', 'connectionOpenCount', 'summedConnectionOpenDurationMs')}
    slowest = database.get('slowestCommandDurationsMs')
    if not isinstance(slowest, list) or len(slowest) != min(5, database['commandCount']):
        raise PerformanceContractError('invalid slowest command bound')
    result['database']['slowestCommandDurationsMs'] = [number(value) for value in slowest]
    for key in ('runtimeBefore', 'runtimeAfter'):
        runtime = raw[key]
        result[key] = {name: number(runtime.get(name), nullable=name.startswith(('host', 'cgroup')),
                                    minimum=-1 if name == 'cgroupQuotaMicroseconds' else 0) for name in RUNTIME_NUMBERS}
        result[key]['gcCollections'] = counters(runtime.get('gcCollections'), 3)
    for key in ('activityBefore', 'activityAfter'):
        activity = raw[key]
        result[key] = {name: number(activity.get(name)) for name in ('eventDispatches', 'signalRSends', 'allEfCommands')}
        result[key]['workerStarts'] = counters(activity.get('workerStarts'), 4)
        result[key]['activeWorkers'] = counters(activity.get('activeWorkers'), 4)
    # Never copy arbitrary strings, error messages, request headers or unknown fields.
    result['unavailableReasons'] = {
        'clientMonotonicEndpoints': 'k6-exposes-duration-only-server-endpoints-recorded',
        'acquisitionOnly': 'logical-open-includes-pool-and-physical-setup',
        'allocationAttribution': 'process-wide-overlap-not-request-attribution',
        'dbBoundary': 'ef-command-execute-only-no-direct-command-or-reader-drain-attribution',
        'transactionSetup': 'included-in-server-boundary-not-isolated',
        'jit': 'not-isolated-by-scalar-observer',
        'workerAttribution': 'process-wide-overlap-counts-not-request-causality',
        'hostOrCgroupNull': 'unsupported-or-unreadable-fixed-kernel-counter'
    }
    return result


def project_clients(rows, config, contract, *, require_complete=True):
    if not isinstance(rows, list) or len(rows) > len(contract['scenarios']) * contract['profile']['iterations']:
        raise PerformanceContractError('diagnostic sample count exceeds the bounded profile')
    projected = []
    for index, row in enumerate(rows):
        ordinal = index // len(contract['scenarios']) + 1
        scenario = contract['scenarios'][index % len(contract['scenarios'])]['id']
        capture_id = config['capturePrefix'] + format(index, '016x')
        if (row.get('scenario') != scenario or row.get('sampleOrdinal') != ordinal or
                row.get('trialOrdinal') != config['trialOrdinal'] or row.get('captureId') != capture_id or
                row.get('warmupIdentity') != config['capturePrefix']):
            raise PerformanceContractError('missing, duplicated or reordered diagnostic sample')
        result = {'captureId': capture_id, 'scenario': scenario, 'sampleOrdinal': ordinal,
                  'trialOrdinal': config['trialOrdinal'], 'warmupIdentity': config['capturePrefix'],
                  'warmupCompletedUtc': utc(row.get('warmupCompletedUtc'))}
        result.update({key: number(row.get(key)) for key in CLIENT_NUMBERS})
        projected.append(result)
    if require_complete and len(rows) != len(contract['scenarios']) * contract['profile']['iterations']:
        raise PerformanceContractError('incomplete diagnostic sample group')
    return projected


def collect_sidecar(raw, config, contract, capture_directory: Path, output: Path, fingerprint: dict,
                    fixture_digest: str, warmup_digest: str):
    clients = project_clients(raw.get('diagnostics'), config, contract, require_complete=False)
    samples, missing = [], []
    deadline = time.monotonic() + 5
    for client in clients:
        path = capture_directory / (client['captureId'] + '.json')
        # OnCompleted persistence is outside the HTTP timing boundary. Wait only
        # for the already measured capture, never replay a measurement request.
        while not path.exists() and time.monotonic() < deadline:
            time.sleep(0.02)
        if not path.exists():
            missing.append(client['captureId'])
            samples.append({'client': client, 'server': None})
            continue
        samples.append({'client': client, 'server': project_server(load_json(path), client),
                        'serverRawDigest': hashlib.sha256(path.read_bytes()).hexdigest()})
    complete = not missing and len(clients) == len(contract['scenarios']) * contract['profile']['iterations']
    write_json_atomic(output, {
        'schemaVersion': 1, 'purpose': 'investigation-only-not-gate-or-baseline-evidence',
        'headSha': fingerprint['commitSha'], 'trialOrdinal': config['trialOrdinal'],
        'fixtureDigest': fixture_digest, 'warmupEvidenceDigest': warmup_digest,
        'diagnosticsImplementationDigest': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        'workerCounterOrder': ['outbox', 'notification-digest', 'scheduled-announcement', 'audit-export'],
        'rawClientSampleDigest': hashlib.sha256(json.dumps(clients, separators=(',', ':')).encode()).hexdigest(),
        'complete': complete, 'missingServerCaptures': missing, 'samples': samples,
    })
    if not complete:
        raise PerformanceContractError('incomplete diagnostics retained; no sample was replayed or deleted')
