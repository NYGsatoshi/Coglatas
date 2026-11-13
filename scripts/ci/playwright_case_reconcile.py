"""Bounded, nonpublishing source/native case-content reconciliation; no D10 attestation."""
from __future__ import annotations

import hashlib
import json
import re
import sys

MAX_BYTES = 2 * 1024 * 1024
MAX_CASES = 5000
SHA = re.compile(r'[a-f0-9]{40}\Z')
CASE = re.compile(r'[a-f0-9]{64}\Z')
STATUSES = {'passed', 'failed', 'timedOut', 'skipped', 'interrupted'}
CAPTURE_ERRORS = {'DUPLICATE_BEGIN', 'INVALID_BEGIN', 'EVENT_PHASE', 'UNLISTED_CASE',
                  'DUPLICATE_RESULT', 'INVALID_RESULT', 'END_PHASE', 'INVALID_END', 'INVALID_CASE_END'}


class InvalidCaseEvidence(ValueError):
    """A fixed nonsecret refusal code, never input/report content."""


def require(condition, code):
    if not condition:
        raise InvalidCaseEvidence(code)


def fields(value, names, code):
    require(type(value) is dict and set(value) == set(names), code)


def unique_pairs(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, 'DUPLICATE_JSON_FIELD')
        result[key] = value
    return result


def reject_constant(_value):
    raise InvalidCaseEvidence('NONFINITE_JSON')


def parse_evidence(raw):
    require(type(raw) is bytes and 0 < len(raw) <= MAX_BYTES, 'INPUT_SIZE_OR_TYPE')
    try:
        return json.loads(raw.decode('utf-8'), object_pairs_hook=unique_pairs, parse_constant=reject_constant)
    except (UnicodeError, json.JSONDecodeError, RecursionError) as error:
        raise InvalidCaseEvidence('MALFORMED_JSON') from error


def source_inventory(source, source_sha):
    # The unchanged #1717 source-only schema, not a new source discovery implementation.
    fields(source, {'schema', 'suite', 'sourceSha', 'tests', 'rosterSha256', 'artifactTrust',
                    'requiredCheckQualified', 'qualification'}, 'SOURCE_SCHEMA')
    require(source['schema'] == 'coglatas.playwright-source-discovery.v1'
            and source['suite'] == 'angular-static' and source['sourceSha'] == source_sha
            and source['artifactTrust'] == 'EXTERNAL_VERIFICATION_REQUIRED'
            and source['requiredCheckQualified'] is False
            and source['qualification'] == 'SOURCE_LIST_ONLY_NOT_EXECUTION_PROOF', 'SOURCE_IDENTITY')
    rows = source['tests']
    require(type(rows) is list and 0 < len(rows) <= MAX_CASES, 'SOURCE_CASE_COUNT')
    expected = {}
    previous = ''
    for row in rows:
        fields(row, {'id', 'status'}, 'SOURCE_CASE_FIELDS')
        require(type(row['id']) is str and CASE.fullmatch(row['id']), 'SOURCE_CASE_ID')
        require(row['id'] not in expected, 'DUPLICATE_SOURCE_CASE_ID')
        require(row['id'] > previous, 'SOURCE_CASE_ORDER')
        require(type(row['status']) is str and row['status'] in {'passed', 'skipped'}, 'SOURCE_EXPECTED_STATUS')
        previous = row['id']
        expected[row['id']] = row['status']
    payload = {'sourceSha': source_sha, 'suite': 'angular-static', 'tests': rows}
    digest = hashlib.sha256(json.dumps(payload, sort_keys=True, separators=(',', ':'), ensure_ascii=True).encode()).hexdigest()
    require(source['rosterSha256'] == digest, 'SOURCE_DIGEST')
    return expected


def reconcile(source_raw, execution_raw, *, source_sha, execution_kind, run_id, attempt):
    """Compare contents only. Caller identity and hashes never authenticate a producer."""
    require(type(source_sha) is str and SHA.fullmatch(source_sha), 'EXPECTED_SOURCE_SHA')
    require(type(execution_kind) is str and execution_kind in {'LOCAL', 'GITHUB_ACTIONS'} and type(run_id) is str
            and re.fullmatch(r'local-[a-f0-9]{32}' if execution_kind == 'LOCAL' else r'[1-9][0-9]{0,18}', run_id),
            'EXPECTED_RUN_ID')
    require(type(attempt) is int and 1 <= attempt <= 1000000, 'EXPECTED_ATTEMPT')
    expected = source_inventory(parse_evidence(source_raw), source_sha)
    execution = parse_evidence(execution_raw)
    fields(execution, {'schema', 'suite', 'sourceSha', 'executionKind', 'runId', 'runAttempt', 'producer',
                       'status', 'captureComplete', 'globalErrors', 'captureErrors', 'tests'}, 'EXECUTION_SCHEMA')
    require(execution['schema'] == 'coglatas.playwright-case-execution.v1'
            and execution['suite'] == 'angular-static'
            and execution['sourceSha'] == source_sha, 'EXECUTION_SOURCE_IDENTITY')
    require(execution['executionKind'] == execution_kind and execution['runId'] == run_id
            and type(execution['runAttempt']) is int and execution['runAttempt'] == attempt,
            'EXECUTION_RUN_OR_ATTEMPT')
    require(execution['producer'] == 'playwright-testcase-events-v1', 'UNSUPPORTED_PRODUCER')
    require(type(execution['captureComplete']) is bool
            and type(execution['globalErrors']) is int and 0 <= execution['globalErrors'] <= 1000000,
            'EXECUTION_COMPLETION_FIELDS')
    errors = execution['captureErrors']
    require(type(errors) is list and len(errors) <= len(CAPTURE_ERRORS)
            and all(type(error) is str and error in CAPTURE_ERRORS for error in errors)
            and errors == sorted(set(errors)), 'CAPTURE_ERROR_SCHEMA')
    require(type(execution['status']) is str
            and execution['status'] in {'passed', 'failed', 'timedout', 'interrupted'}, 'EXECUTION_STATUS')
    rows = execution['tests']
    require(type(rows) is list and 0 < len(rows) <= MAX_CASES, 'NATIVE_CASE_COUNT')
    cases, duplicates = {}, set()
    issues = set()
    counts = dict(sourceCases=len(expected), nativeCases=len(rows), resultCases=0, executedCases=0,
                  missing=0, unexpected=0, duplicateSource=0, duplicateNative=0,
                  missingResults=0, skipped=0, unauthorizedSkips=0, failed=0, retried=0, interrupted=0)
    for row in rows:
        fields(row, {'id', 'listed', 'declaredExpectedStatus', 'expectedStatus', 'outcome', 'attempts'}, 'NATIVE_CASE_FIELDS')
        identity = row['id']
        require(type(identity) is str and CASE.fullmatch(identity), 'NATIVE_CASE_ID')
        if identity in cases:
            duplicates.add(identity)
        require(type(row['listed']) is bool and ((type(row['declaredExpectedStatus']) is str
                and row['declaredExpectedStatus'] in {'passed', 'skipped'})
                if row['listed'] else row['declaredExpectedStatus'] is None), 'NATIVE_DECLARATION')
        require((row['expectedStatus'] is None or (type(row['expectedStatus']) is str
                and row['expectedStatus'] in {'passed', 'skipped'}))
                and type(row['outcome']) is str
                and row['outcome'] in {'expected', 'unexpected', 'flaky', 'skipped'}, 'NATIVE_OUTCOME')
        attempts = row['attempts']
        require(type(attempts) is list and len(attempts) <= 10, 'NATIVE_ATTEMPTS')
        for result in attempts:
            fields(result, {'retry', 'status'}, 'NATIVE_RESULT_FIELDS')
            require(type(result['retry']) is int and 0 <= result['retry'] < 10
                    and type(result['status']) is str and result['status'] in STATUSES, 'NATIVE_RESULT')
        case_issues = set()
        if not row['listed']:
            case_issues.add('NOT_LISTED')
        if identity not in expected:
            case_issues.add('UNEXPECTED_CASE')
        elif row['declaredExpectedStatus'] != expected[identity] or row['expectedStatus'] != expected[identity]:
            case_issues.add('EXPECTED_STATUS_MISMATCH')
        if not attempts:
            case_issues.add('MISSING_RESULT'); counts['missingResults'] += 1
        else:
            counts['resultCases'] += 1
        if any(a['status'] != 'skipped' for a in attempts):
            counts['executedCases'] += 1
        if any(a['status'] == 'skipped' for a in attempts):
            counts['skipped'] += 1
            if expected.get(identity) != 'skipped':
                case_issues.add('UNAUTHORIZED_SKIP'); counts['unauthorizedSkips'] += 1
        if any(a['status'] in {'failed', 'timedOut'} for a in attempts):
            counts['failed'] += 1; case_issues.add('FAILED_CASE')
        if any(a['status'] == 'interrupted' for a in attempts):
            counts['interrupted'] += 1; case_issues.add('INTERRUPTED_CASE')
        if len(attempts) > 1 or any(a['retry'] != 0 for a in attempts):
            counts['retried'] += 1; case_issues.add('RETRIED_CASE')
        if [a['retry'] for a in attempts] != list(range(len(attempts))):
            case_issues.add('INCONSISTENT_RETRY_SEQUENCE')
        if len(attempts) == 1 and (attempts[0]['status'] != expected.get(identity)
                or row['outcome'] != ('skipped' if attempts[0]['status'] == 'skipped' else 'expected')):
            case_issues.add('RESULT_OUTCOME_MISMATCH')
        issues.update(case_issues)
        # Keep every valid input attempt, including duplicate-case records, below.
        cases[identity] = True
    missing, unexpected = sorted(set(expected) - set(cases)), sorted(set(cases) - set(expected))
    counts.update(missing=len(missing), unexpected=len(unexpected), duplicateNative=len(duplicates))
    if missing: issues.add('MISSING_CASE')
    if unexpected: issues.add('UNEXPECTED_CASE')
    if duplicates: issues.add('DUPLICATE_NATIVE_CASE')
    if execution['status'] != 'passed': issues.add('EXECUTION_NOT_PASSED')
    if not execution['captureComplete'] or errors or execution['globalErrors']:
        issues.add('INCOMPLETE_CAPTURE')
    return {
        'schema': 'coglatas.playwright-case-reconciliation.v1', 'suite': 'angular-static',
        'sourceSha': source_sha, 'executionKind': execution_kind, 'runId': run_id, 'runAttempt': attempt,
        'decision': 'REJECTED' if issues else 'CONTENT_MATCH', 'issues': sorted(issues), 'counts': counts,
        'missingIds': missing, 'unexpectedIds': unexpected, 'duplicateNativeIds': sorted(duplicates),
        'nativeStatus': execution['status'], 'globalErrors': execution['globalErrors'], 'captureErrors': errors,
        'cases': sorted(rows, key=lambda row: (row['id'], json.dumps(row, sort_keys=True))),
        'states': {'SOURCE_DISCOVERED': True, 'NATIVE_EXECUTION_RECONCILED': not issues,
                   'ARTIFACT_PROVENANCE_VERIFIED': False, 'REQUIRED_GATE_QUALIFIED': False},
        'coverageState': 'COVERAGE_UNVERIFIED',
        'qualification': 'OFFLINE_CONTENT_COMPARISON_NOT_PRODUCER_OR_EXECUTION_ATTESTATION',
    }


def main():
    # Deliberately byte-stream based: no untrusted filesystem traversal or output publication.
    # One bounded JSON envelope on stdin, one sanitized deterministic result on stdout.
    try:
        require(len(sys.argv) == 1, 'INVALID_ARGUMENTS')
        data = parse_evidence(sys.stdin.buffer.read(MAX_BYTES + 1))
        fields(data, {'source', 'execution', 'expected'}, 'INPUT_ENVELOPE')
        fields(data['expected'], {'source_sha', 'execution_kind', 'run_id', 'attempt'}, 'EXPECTED_FIELDS')
        result = reconcile(json.dumps(data['source']).encode(), json.dumps(data['execution']).encode(), **data['expected'])
        print(json.dumps(result, sort_keys=True, separators=(',', ':'), ensure_ascii=True))
        return 0 if result['decision'] == 'CONTENT_MATCH' else 1
    except (InvalidCaseEvidence, ValueError, TypeError, KeyError, RecursionError, OSError):
        print('CASE_RECONCILIATION_REFUSED', file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
