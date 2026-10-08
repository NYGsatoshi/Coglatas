#!/usr/bin/env python3
"""One prospective 5 x 20 scalar-only diagnostic. Never invokes an acceptance comparator."""
from __future__ import annotations
import datetime
import hashlib
import http.cookiejar
import json
import os
import re
import subprocess
import time
import urllib.request
from pathlib import Path
from db_probe_import import login, request
from diagnostic_json import load_json

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'artifacts/performance/announcement-diagnostic'
GROUPS, REQUESTS = 5, 20

def save(name, value):
    (OUT / name).write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')

def read_numbers(path):
    try:
        return [int(x) for x in Path(path).read_text().split()]
    except (OSError, ValueError):
        return None

def container_snapshot(pid):
    result = {}
    try:
        raw = Path(f'/proc/{pid}/stat').read_text().rsplit(')', 1)[1].split()
        result = {'cpuUserTicks': int(raw[11]), 'cpuSystemTicks': int(raw[12]), 'rssPages': int(raw[21])}
        cgroup = Path(f'/proc/{pid}/cgroup').read_text().split('0::', 1)[1].strip()
        base = Path('/sys/fs/cgroup') / cgroup.lstrip('/')
        result['cpu'] = {line.split()[0]: int(line.split()[1]) for line in (base / 'cpu.stat').read_text().splitlines()}
        result['memoryBytes'] = int((base / 'memory.current').read_text())
        result['memoryLimit'] = (base / 'memory.max').read_text().strip()
        result['cpuQuota'] = (base / 'cpu.max').read_text().strip()
    except (OSError, ValueError, IndexError):
        result['unavailable'] = True
    return result

def host_snapshot():
    result = {'utc': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'monotonicNs': time.monotonic_ns()}
    try:
        result['cpuTicks'] = [int(x) for x in Path('/proc/stat').read_text().splitlines()[0].split()[1:]]
        result['cpuPressureMicroseconds'] = {line.split()[0]: int(re.search(r'total=(\d+)', line)[1]) for line in Path('/proc/pressure/cpu').read_text().splitlines()}
    except (OSError, ValueError):
        result['unavailable'] = True
    return result

def main():
    assert os.environ['GITHUB_RUN_ATTEMPT'] == '1'
    assert os.environ['COGLATAS_PERFORMANCE_PROFILE'] == 'medium'
    assert os.environ['COGLATAS_PERFORMANCE_DB_CAPTURE_ENABLED'] == 'true'
    assert os.environ['COGLATAS_PERFORMANCE_API_DIAGNOSTICS_ENABLED'] == 'true'
    OUT.mkdir(parents=True, exist_ok=True)
    assert not (OUT / 'prospective-manifest.json').exists()
    fixture = load_json(Path(os.environ['COGLATAS_PERFORMANCE_FIXTURE_EVIDENCE']))
    manifest = {'purpose': 'DIAGNOSTIC_ONLY_NOT_ACCEPTANCE_NOT_BASELINE',
        'sourceSha': os.environ['GITHUB_SHA'], 'productSourceSha': '40d0f55df7930a169ee7f80e4ebf2ee5ca06cd7d',
        'runId': int(os.environ['GITHUB_RUN_ID']), 'attempt': 1,
        'createdUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'groups': GROUPS, 'requestsPerGroup': REQUESTS, 'warmups': 1, 'earlyStop': False, 'retries': 0,
        'scenario': 'announcement.list', 'profile': 'medium', 'page': 1, 'pageSize': 5,
        'fixtureHash': fixture['fixtureHash'], 'retain': 'ALL_IN_ORIGINAL_ORDER',
        'sourceContracts': {p: hashlib.sha256((ROOT / p).read_bytes()).hexdigest() for p in ['performance/datasets.json', 'performance/db-scenarios.json']},
        'observerLimitations': ['observer-overhead-present', 'logical-open-includes-pool-and-physical-setup',
            'GC-allocation-CPU-counters-process-wide', 'result-filter-includes-serialization-and-response-writes',
            'PostgreSQL-PID-time-windows-not-query-text', 'clock-alignment-resolution-one-millisecond'],
        'statisticalAcceptanceChanged': False, 'acceptedAsGate': False}
    if os.environ.get('COGLATAS_DIAGNOSTIC_ID'):
        manifest['diagnosticId'] = os.environ['COGLATAS_DIAGNOSTIC_ID']
        manifest['recoveryRule'] = 'perf05-diagnostic-zero-measurement-recovery-v1'
        manifest['predecessorRun'] = '37695270091/1'
        manifest['predecessorMeasurements'] = 0
        manifest['declarationDigest'] = os.environ['COGLATAS_DIAGNOSTIC_DECLARATION_DIGEST']
    save('prospective-manifest.json', manifest)
    project = os.environ['COGLATAS_PERFORMANCE_COMPOSE_PROJECT']
    ids = {service: subprocess.check_output(['docker', 'ps', '-q', '--filter', f'label=com.docker.compose.project={project}', '--filter', f'label=com.docker.compose.service={service}'], text=True).strip() for service in ['app', 'postgres']}
    assert all(ids.values())
    pids = {service: int(subprocess.check_output(['docker', 'inspect', '--format', '{{.State.Pid}}', cid], text=True)) for service, cid in ids.items()}
    opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
    base = os.environ['COGLATAS_PERFORMANCE_BASE_URL']
    headers = login(opener, base, fixture)
    route = '/api/announcements?pageSize=5&page=1'
    warm_status, warm_body = request(opener, base, route, headers)
    save('warmup-status.json', {'status': warm_status, 'returnedCount': len(warm_body.get('items', [])) if isinstance(warm_body, dict) else None})
    samples = []
    for ordinal in range(1, GROUPS * REQUESTS + 1):
        before = {service: container_snapshot(pid) for service, pid in pids.items()}
        host_before = host_snapshot()
        started = time.perf_counter_ns()
        utc = datetime.datetime.now(datetime.timezone.utc).isoformat()
        status, count, failure = None, None, None
        try:
            status, body = request(opener, base, route, headers | {'X-Announcement-Ordinal': str(ordinal)})
            count = len(body.get('items', [])) if isinstance(body, dict) else None
            del body
        except Exception:
            failure = 'request-failed-no-retry'
        ended = time.perf_counter_ns()
        sample = {'ordinal': ordinal, 'group': (ordinal - 1) // REQUESTS + 1, 'sampleInGroup': (ordinal - 1) % REQUESTS + 1,
            'startUtc': utc, 'clientStartNs': started, 'clientEndNs': ended, 'wallTimeMs': (ended - started) / 1e6,
            'status': status, 'returnedCount': count, 'failure': failure,
            'containersBefore': before, 'containersAfter': {service: container_snapshot(pid) for service, pid in pids.items()},
            'hostBefore': host_before, 'hostAfter': host_snapshot()}
        samples.append(sample)
        save('client-samples.json', samples)
    # Flush the in-memory sidecar only after all 100 attempts, including any failures.
    request(opener, base, '/health/ready', headers | {'X-Announcement-Flush': 'complete-100'})
    sidecar = Path(os.environ['COGLATAS_PERFORMANCE_API_DIAGNOSTICS_PATH']) / 'announcement-samples.json'
    # Polling for the observer file is never a request retry or a benchmark repetition.
    for _ in range(100):
        if sidecar.exists(): break
        time.sleep(.05)
    if sidecar.exists(): (OUT / 'server-samples.json').write_bytes(sidecar.read_bytes())
    raw = subprocess.run(['docker', 'logs', ids['postgres']], capture_output=True, text=True, check=True)
    durations = []
    # Full-line allowlist: no other PostgreSQL log content is retained or printed.
    pattern = re.compile(r'^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} UTC) \[(\d+)\] LOG:  duration: ([0-9.]+) ms\s*$')
    for line in (raw.stdout + raw.stderr).splitlines():
        if match := pattern.fullmatch(line):
            durations.append({'ordinal': len(durations) + 1, 'endUtc': match[1], 'backendPid': int(match[2]), 'durationMs': float(match[3])})
    save('postgres-durations.json', durations)
    save('completion.json', {'attemptedRequests': len(samples), 'serverSamples': len(load_json(sidecar, list)) if sidecar.exists() else 0,
        'postgresDurationRecords': len(durations), 'retries': 0, 'acceptanceEvidence': False, 'baselineEvidence': False})
    assert len(samples) == 100 and sidecar.exists() and len(load_json(sidecar, list)) == 100
    assert all(s['status'] == 200 and s['returnedCount'] == 5 for s in samples)
    assert durations, 'PostgreSQL scalar duration records unavailable'

if __name__ == '__main__':
    main()
