#!/usr/bin/env python3
"""Bound and sanitize native browser fixture observations, without product credit."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
import re
import stat
import sys
from pathlib import Path
from typing import Any

import release_supply_chain as release

SCHEMA = 'coglatas-sec14-browser-tooling-advisory-v1'
IMAGE = ('zaproxy/zap-stable:2.17.0@sha256:'
         '781a2bdaea47324e7bab583e2263f21d257b0aee61ed51521a5be45f5f5081ef')
SOURCE_FILES = ('scripts/security/sec14-browser-fixture.py',
                'scripts/security/sec14-browser-fixture.yaml',
                'scripts/security/sec14-browser-fixture-run.sh',
                'scripts/security/run-sec14-browser-fixture.mjs',
                'scripts/ci/sec14_browser_evidence.py')
CONTEXT_FIELDS = {'schema', 'candidateSha', 'runIdentity', 'scope', 'mode', 'startedAtUtc',
                  'completedAtUtc', 'sourceSha256', 'scannerImage', 'scannerVersion',
                  'processExit', 'sourceState', 'nativeSha256', 'network', 'platform'}
COUNTERS = {'root', 'ajaxDiscovery', 'authorized200', 'crossScope403', 'unauthorized200', 'other'}
ORIGIN = 'http://127.0.0.1:8123'


class EvidenceError(ValueError):
    pass


def require(condition: bool, code: str) -> None:
    if not condition:
        raise EvidenceError(code)


def bounded_regular_bytes(path: Path) -> bytes:
    descriptor = os.open(path, os.O_RDONLY | getattr(os, 'O_NONBLOCK', 0))
    with os.fdopen(descriptor, 'rb') as handle:
        if not stat.S_ISREG(os.fstat(handle.fileno()).st_mode):
            raise OSError('Regular evidence input required')
        return handle.read(release.MAX_JSON_BYTES + 1)


def read(path: Path) -> tuple[dict[str, Any], str]:
    try:
        raw = bounded_regular_bytes(path)
        return release.parse_json_bytes(raw), hashlib.sha256(raw).hexdigest()
    except (OSError, release.ReleaseEvidenceError):
        raise EvidenceError('NATIVE_INPUT_MISSING_OR_INVALID') from None


def source_hashes(root: Path) -> dict[str, str]:
    result = {}
    for name in SOURCE_FILES:
        try:
            raw = bounded_regular_bytes(root / name)
        except OSError:
            raise EvidenceError('SOURCE_INPUT_MISSING') from None
        require(0 < len(raw) <= release.MAX_JSON_BYTES, 'SOURCE_INPUT_SIZE_LIMIT')
        result[name] = hashlib.sha256(raw).hexdigest()
    return result


def timestamp(value: Any) -> datetime:
    require(isinstance(value, str) and len(value) <= 40 and value.endswith('Z'), 'EXECUTION_TIME_INVALID')
    try:
        result = datetime.fromisoformat(value.replace('Z', '+00:00'))
    except ValueError:
        raise EvidenceError('EXECUTION_TIME_INVALID') from None
    require(result.utcoffset().total_seconds() == 0, 'EXECUTION_TIME_INVALID')
    return result


def normalized_rules(native: dict[str, Any]) -> dict[str, dict[str, int]]:
    require(native.get('@version') == '2.17.0' and native.get('@programName') == 'ZAP',
            'NATIVE_SCANNER_VERSION_MISMATCH')
    sites = native.get('site')
    require(isinstance(sites, list) and len(sites) == 1 and isinstance(sites[0], dict),
            'NATIVE_SCANNED_SITE_MISSING_OR_INVALID')
    require(sites[0].get('@name') == ORIGIN, 'NATIVE_TARGET_MISMATCH')
    alerts = sites[0].get('alerts')
    require(isinstance(alerts, list) and len(alerts) <= 10000, 'NATIVE_ALERT_SCOPE_INVALID')
    counts: dict[str, dict[str, int]] = {}
    for alert in alerts:
        require(isinstance(alert, dict), 'NATIVE_ALERT_INVALID')
        rule = alert.get('pluginid')
        risk = alert.get('riskcode')
        require(isinstance(rule, str) and re.fullmatch(r'[0-9]{1,10}', rule) is not None
                and risk in ('0', '1', '2', '3'), 'NATIVE_RULE_CLASSIFICATION_INVALID')
        require(rule not in counts, 'NATIVE_DUPLICATE_RULE')
        count = alert.get('count')
        require(isinstance(count, str) and re.fullmatch(r'[0-9]{1,7}', count) is not None,
                'NATIVE_RULE_COUNT_INVALID')
        # Never retain names, evidence, URLs, headers, bodies, parameters or values.
        counts[rule] = {'riskCode': int(risk), 'instanceCount': int(count)}
    return dict(sorted(counts.items()))


def collect(args: argparse.Namespace) -> dict[str, Any]:
    require(isinstance(args.expected_candidate_sha, str)
            and release.GIT_SHA_RE.fullmatch(args.expected_candidate_sha) is not None, 'EXPECTED_CANDIDATE_INVALID')
    require(isinstance(args.expected_run_identity, str)
            and re.fullmatch(r'local:[0-9a-f]{32}', args.expected_run_identity) is not None,
            'EXPECTED_LOCAL_RUN_INVALID')
    raw_dir = Path(args.raw_directory)
    context, context_hash = read(raw_dir / 'context.json')
    require(set(context) == CONTEXT_FIELDS and context['schema'] == 'sec14-browser-execution-v1',
            'EXECUTION_CONTEXT_SCHEMA_INVALID')
    require(context['candidateSha'] == args.expected_candidate_sha, 'CANDIDATE_MISMATCH')
    require(context['runIdentity'] == args.expected_run_identity, 'RUN_IDENTITY_MISMATCH')
    require(context['scope'] == 'TEST_OWNED_LOOPBACK_FIXTURE_ONLY'
            and context['mode'] in ('normal', 'weakened', 'disabled')
            and context['network'] == 'none' and context['platform'] == 'linux/amd64',
            'FIXTURE_SCOPE_INVALID')
    require(context['sourceState'] in ('CLEAN', 'DEVELOPMENT'), 'SOURCE_STATE_INVALID')
    require(context['sourceSha256'] == source_hashes(Path(__file__).resolve().parents[2]),
            'EXECUTED_SOURCE_BYTES_MISMATCH')
    require(context['scannerImage'] == IMAGE and context['scannerVersion'] == '2.17.0',
            'SCANNER_IDENTITY_MISMATCH')
    require(type(context['processExit']) is int and -255 <= context['processExit'] <= 255,
            'SCANNER_EXIT_INVALID')
    started, completed = timestamp(context['startedAtUtc']), timestamp(context['completedAtUtc'])
    now = timestamp(args.now) if args.now else datetime.now(timezone.utc)
    require(started <= completed <= now and (now - completed).total_seconds() <= 7200
            and (completed - started).total_seconds() <= 360, 'STALE_OR_INVALID_EXECUTION_WINDOW')
    native, native_hash = read(raw_dir / 'browser-native-v1.json')
    counters, counter_hash = read(raw_dir / 'browser-counters.json')
    require(context['nativeSha256'] == {'report': native_hash, 'counters': counter_hash},
            'EXECUTED_NATIVE_BYTES_MISMATCH')
    generated = timestamp(native.get('created'))
    require(started <= generated <= completed, 'NATIVE_REPORT_TIME_MISMATCH')
    require(set(counters) == {'schema', 'scope', 'weakened', 'counters'}
            and counters['schema'] == 'sec14-browser-fixture-counters-v1'
            and counters['scope'] == 'TEST_OWNED_LOOPBACK_FIXTURE_ONLY'
            and type(counters['weakened']) is bool
            and counters['weakened'] == (context['mode'] == 'weakened'), 'FIXTURE_COUNTER_SCHEMA_INVALID')
    values = counters['counters']
    require(isinstance(values, dict) and set(values) == COUNTERS
            and all(type(value) is int and 0 <= value <= 1000000 for value in values.values()),
            'FIXTURE_COUNTER_SCOPE_INVALID')
    rules = normalized_rules(native)
    high = sum(1 for value in rules.values() if value['riskCode'] == 3)
    conditions = {
        'scannerProcessSucceeded': context['processExit'] == 0,
        'browserDiscoveredAjaxTarget': values['root'] > 0 and values['ajaxDiscovery'] > 0,
        'authorizedPositiveObserved': values['authorized200'] > 0,
        'crossScopeDenialAfterPositive': values['authorized200'] > 0 and values['crossScope403'] > 0,
        'noUnauthorizedFixtureResponse': values['unauthorized200'] == 0,
        'noHighScannerFinding': high == 0,
        'browserVerifierEnabled': context['mode'] != 'disabled',
    }
    return {
        'schema': SCHEMA, 'mode': 'ADVISORY', 'verifierVersion': '1',
        'adapter': 'browserAjaxZap', 'scope': 'TEST_OWNED_LOOPBACK_FIXTURE_ONLY',
        'integrityStatus': 'CONSISTENT', 'toolingOutcome': 'MATCHED' if all(conditions.values()) else 'FAILED',
        'candidateSha': args.expected_candidate_sha, 'runIdentity': args.expected_run_identity,
        'sourceStateObservation': context['sourceState'], 'sourceSha256': context['sourceSha256'],
        'scannerImage': IMAGE, 'scannerVersion': '2.17.0', 'scannerExit': context['processExit'],
        'startedAtUtc': context['startedAtUtc'], 'completedAtUtc': context['completedAtUtc'],
        'inputSha256': {'context': context_hash, 'report': native_hash, 'counters': counter_hash},
        'fixtureCounters': values, 'nativeRules': rules, 'conditions': conditions,
        'rawArtifactUploadAllowed': False, 'sanitizer': 'CLOSED_NUMERIC_RULE_AND_COUNTER_FIELDS',
        'productSourceBinding': 'UNVERIFIED', 'productImageBinding': 'UNVERIFIED',
        'scannerExecutionAuthentication': 'UNVERIFIED', 'githubRunAuthentication': 'UNVERIFIED',
        'personalOwnerApproval': 'UNVERIFIED', 'canonicalNormativeCoverage': 'UNVERIFIED',
        'releaseAcceptance': 'BLOCKED', 'preAvaloniaVerdict': 'BLOCKED',
    }


def rendered(report: dict[str, Any]) -> bytes:
    raw = (json.dumps(report, indent=2, sort_keys=True) + '\n').encode('utf-8')
    require(len(raw) <= release.MAX_JSON_BYTES, 'OUTPUT_SIZE_LIMIT')
    return raw


def command(args: argparse.Namespace) -> int:
    try:
        report = collect(args)
    except EvidenceError as error:
        report = {'schema': SCHEMA, 'mode': 'ADVISORY', 'integrityStatus': 'ERROR',
                  'errorCode': str(error), 'rawArtifactUploadAllowed': False,
                  'candidateSha': args.expected_candidate_sha if isinstance(args.expected_candidate_sha, str)
                  and release.GIT_SHA_RE.fullmatch(args.expected_candidate_sha) else 'UNVERIFIED',
                  'runIdentity': args.expected_run_identity if isinstance(args.expected_run_identity, str)
                  and re.fullmatch(r'local:[0-9a-f]{32}', args.expected_run_identity) else 'UNVERIFIED',
                  'scope': 'TEST_OWNED_LOOPBACK_FIXTURE_ONLY',
                  'scannerExecutionAuthentication': 'UNVERIFIED', 'personalOwnerApproval': 'UNVERIFIED',
                  'releaseAcceptance': 'BLOCKED', 'preAvaloniaVerdict': 'BLOCKED'}
    raw = rendered(report)
    if args.verify_report:
        recorded, recorded_hash = read(Path(args.verify_report))
        require(recorded == report and recorded_hash == hashlib.sha256(raw).hexdigest(),
                'RETAINED_REPORT_MISMATCH')
    else:
        with Path(args.output).open('xb') as handle:
            handle.write(raw)
    print('SEC-14 browser fixture evidence=' + report.get('toolingOutcome', 'ERROR') + ' product=UNVERIFIED')
    return 0 if report.get('toolingOutcome') == 'MATCHED' else 1


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--expected-candidate-sha', required=True)
    parser.add_argument('--expected-run-identity', required=True)
    parser.add_argument('--raw-directory', required=True)
    output = parser.add_mutually_exclusive_group(required=True)
    output.add_argument('--output')
    output.add_argument('--verify-report')
    parser.add_argument('--now', help=argparse.SUPPRESS)
    args = parser.parse_args()
    try:
        require(all(value is None or len(value) <= 4096 for value in vars(args).values()), 'CLI_INPUT_LIMIT')
        return command(args)
    except (EvidenceError, OSError):
        print('SEC-14 browser evidence input/output rejected', file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
