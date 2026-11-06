"""Composed-source content trial only; #1755 is a mandatory independent dependency."""
import hashlib
import json
import re
import sys


def reconcile(source_raw, native_raw, applicability_raw, *, source_sha, execution_kind, run_id,
              attempt, applicability_sha256):
    # Deliberately absent from #1717 alone. Never manufacture integration of #1755.
    from playwright_case_reconcile import reconcile as original, parse_evidence, require, fields
    prior = original(source_raw, native_raw, source_sha=source_sha, execution_kind=execution_kind,
                     run_id=run_id, attempt=attempt)
    source, sidecar = parse_evidence(source_raw), parse_evidence(applicability_raw)
    fields(sidecar, {'schema', 'sourceSha', 'suite', 'rosterSha256', 'anchorSha', 'sourceManifestSha256',
                     'tests', 'applicabilitySha256', 'artifactTrust', 'requiredCheckQualified', 'qualification'}, 'APPLICABILITY_FIELDS')
    require(sidecar['schema'] == 'coglatas.playwright-source-applicability.v1'
            and sidecar['sourceSha'] == source_sha and sidecar['suite'] == 'angular-static'
            and sidecar['rosterSha256'] == source['rosterSha256']
            and sidecar['anchorSha'] == '3865b09a2d3ea3cb82f450c6d19ec643ffbae01f'
            and sidecar['artifactTrust'] == 'EXTERNAL_VERIFICATION_REQUIRED'
            and sidecar['requiredCheckQualified'] is False
            and sidecar['qualification'] == 'AST_SOURCE_APPLICABILITY_ONLY_NOT_EXECUTION', 'APPLICABILITY_IDENTITY')
    hex64 = lambda value: type(value) is str and re.fullmatch('[a-f0-9]{64}', value) is not None
    require(hex64(applicability_sha256) and hex64(sidecar['sourceManifestSha256']), 'APPLICABILITY_DIGEST_TYPE')
    payload = {key: sidecar[key] for key in ['sourceSha', 'suite', 'rosterSha256', 'anchorSha', 'sourceManifestSha256', 'tests']}
    digest = hashlib.sha256(json.dumps(payload, separators=(',', ':'), ensure_ascii=False).encode()).hexdigest()
    require(digest == sidecar['applicabilitySha256'] == applicability_sha256, 'APPLICABILITY_DIGEST')
    rows = sidecar['tests']
    expected = {row['id']: row['status'] for row in source['tests']}
    require(type(rows) is list and len(rows) == len(expected), 'APPLICABILITY_COUNT')
    by_id, last = {}, ''
    for row in rows:
        fields(row, {'id', 'project', 'classification', 'bindingSha256'}, 'APPLICABILITY_CASE_FIELDS')
        require(hex64(row['id']) and row['id'] > last and row['id'] in expected, 'APPLICABILITY_CASE_ID')
        require(type(row['project']) is str and row['project'] in {'CHROMIUM_DESKTOP', 'CHROMIUM_MOBILE'}
                and type(row['classification']) is str and row['classification'] in {'RUN', 'DECLARATION_SKIP', 'CONDITIONAL_SKIP'}
                and hex64(row['bindingSha256']), 'APPLICABILITY_CASE_SCHEMA')
        require((row['classification'] == 'DECLARATION_SKIP') == (expected[row['id']] == 'skipped'), 'DECLARATION_CLASSIFICATION')
        by_id[row['id']] = row
        last = row['id']
    allowed_prior = {'EXPECTED_STATUS_MISMATCH', 'RESULT_OUTCOME_MISMATCH', 'UNAUTHORIZED_SKIP'}
    issues = set(prior['issues']) - allowed_prior
    declared = conditional = unauthorized = 0
    for case in prior['cases']:
        classification = by_id.get(case['id'], {}).get('classification')
        skipped = classification in {'DECLARATION_SKIP', 'CONDITIONAL_SKIP'}
        status = 'skipped' if skipped else 'passed'
        matches = (case['listed'] is True and
                   case['declaredExpectedStatus'] == ('skipped' if classification == 'DECLARATION_SKIP' else 'passed') and
                   case['expectedStatus'] == status and case['outcome'] == ('skipped' if skipped else 'expected') and
                   case['attempts'] == [{'retry': 0, 'status': status}])
        if not matches:
            issues.add('APPLICABILITY_NATIVE_MISMATCH')
            if any(result['status'] == 'skipped' for result in case['attempts']): unauthorized += 1
        elif classification == 'DECLARATION_SKIP': declared += 1
        elif classification == 'CONDITIONAL_SKIP': conditional += 1
    counts = dict(prior['counts'], declarationSkips=declared, conditionalSkips=conditional, unauthorizedSkips=unauthorized)
    return {'schema': 'coglatas.playwright-applicability-reconciliation.v1', 'sourceSha': source_sha,
            'executionKind': execution_kind, 'runId': run_id, 'runAttempt': attempt,
            'decision': 'REJECTED' if issues else 'CONTENT_MATCH', 'issues': sorted(issues), 'counts': counts,
            'originalReconciliation': prior, 'applicabilitySha256': digest,
            'states': {'SOURCE_DISCOVERED': True, 'SOURCE_APPLICABILITY_VERIFIED': True,
                       'NATIVE_EXECUTION_RECONCILED': not issues, 'ARTIFACT_PROVENANCE_VERIFIED': False,
                       'REQUIRED_GATE_QUALIFIED': False},
            'coverageState': 'COVERAGE_UNVERIFIED',
            'qualification': 'COMPOSED_CONTENT_TRIAL_NOT_ARTIFACT_OR_PROTECTED_GATE_ATTESTATION'}


def main():
    try:
        from playwright_case_reconcile import parse_evidence, fields, MAX_BYTES
        if len(sys.argv) != 1: raise ValueError()
        envelope = parse_evidence(sys.stdin.buffer.read(MAX_BYTES + 1))
        fields(envelope, {'source', 'native', 'applicability', 'expected'}, 'ENVELOPE')
        fields(envelope['expected'], {'source_sha', 'execution_kind', 'run_id', 'attempt', 'applicability_sha256'}, 'EXPECTED')
        result = reconcile(json.dumps(envelope['source']).encode(), json.dumps(envelope['native']).encode(),
                           json.dumps(envelope['applicability']).encode(), **envelope['expected'])
        print(json.dumps(result, sort_keys=True, separators=(',', ':')))
        return 0 if result['decision'] == 'CONTENT_MATCH' else 1
    except (ImportError, ValueError, TypeError, KeyError, RecursionError, OSError):
        print('APPLICABILITY_RECONCILIATION_REFUSED', file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
