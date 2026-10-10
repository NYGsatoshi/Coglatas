"""Observe original Main API scanner bytes without granting SEC-14 acceptance."""

from __future__ import annotations

import argparse
import base64
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import re
import sys

import release_assurance_advisory as advisory
import release_supply_chain as release
import sec_arch_github_provenance as provenance
import sec_arch_reconcile as binding

SCHEMA = 'coglatas-sec14-main-security-observation-v1'
ARTIFACT = 'main-security-evidence'
JOB = 'Main Test / Frontend / Security / Main Security'
UPLOAD = 'Upload security evidence'
PRODUCER_WORKFLOW = '.github/workflows/main-validation.yml'
PLAN = 'scripts/security/zap-automation.yaml'
POLICY = 'scripts/security/zap-policy.json'
MAX_NDJSON = 128 * 1024 * 1024
MAX_LINE = 8 * 1024 * 1024
EVENTS = {'Initialize', 'EngineStarted', 'EngineFinished', 'LoadingStarted', 'LoadingFinished',
          'PhaseStarted', 'PhaseFinished', 'ScenarioStarted', 'ScenarioFinished', 'SuiteStarted', 'SuiteFinished'}
RISKS = ('High', 'Medium', 'Low', 'Informational')
METHODS = ('get', 'post', 'put', 'patch', 'delete', 'head', 'options', 'trace')


class ObservationError(ValueError):
    """Only fixed diagnostics leave this adapter."""


def require(condition: bool, code: str) -> None:
    if not condition:
        raise ObservationError(code)


def number(value, positive=False) -> bool:
    return type(value) is int and (0 < value if positive else 0 <= value) and value < 2**31


def artifact_json(archive, name):
    raw = binding.read_member(archive, name, release.MAX_JSON_BYTES)
    return release.parse_json_bytes(raw), hashlib.sha256(raw).hexdigest()


def source_bytes(api, prefix, path, candidate):
    document = api(prefix + '/contents/' + path + '?ref=' + candidate)
    require(document.get('type') == 'file' and document.get('encoding') == 'base64'
            and number(document.get('size'), True) and document['size'] <= provenance.MAX_RESPONSE
            and isinstance(document.get('content'), str) and len(document['content']) <= 4 * 1024 * 1024,
            'IMMUTABLE_SOURCE_UNAVAILABLE')
    raw = base64.b64decode(''.join(document['content'].split()), validate=True)
    require(len(raw) == document['size'], 'IMMUTABLE_SOURCE_SIZE_MISMATCH')
    blob = hashlib.sha1(b'blob ' + str(len(raw)).encode('ascii') + b'\0' + raw).hexdigest()
    # SHA-1 checks the repository's Git object address; SHA-256 records source bytes.
    require(document.get('sha') == blob, 'IMMUTABLE_SOURCE_BLOB_MISMATCH')
    return raw


def upload_job(jobs, candidate, run_id, attempt, now):
    provenance.check_jobs(jobs, candidate, run_id, attempt)
    job = next(row for row in jobs if row['name'] == JOB)
    steps = job.get('steps')
    require(isinstance(steps, list) and len(steps) <= 100, 'UPLOAD_STEP_UNAVAILABLE')
    matches = [row for row in steps if isinstance(row, dict) and row.get('name') == UPLOAD]
    require(len(matches) == 1, 'UPLOAD_STEP_MISSING_OR_DUPLICATE')
    step = matches[0]
    require(step.get('status') == 'completed' and step.get('conclusion') == 'success'
            and number(step.get('number'), True), 'UPLOAD_STEP_DID_NOT_SUCCEED')
    begin, finish = provenance.instant(job['started_at']), provenance.instant(job['completed_at'])
    uploaded_start, uploaded_end = provenance.instant(step['started_at']), provenance.instant(step['completed_at'])
    require(begin <= uploaded_start <= uploaded_end <= finish <= now, 'UPLOAD_WINDOW_INVALID')
    return {'id': job['id'], 'name': JOB, 'start': begin, 'finish': finish,
            'uploadStart': uploaded_start, 'uploadEnd': uploaded_end}


def select_artifact(rows, identity, candidate, run_id, repository_id, job, now):
    require(len({row.get('id') for row in rows}) == len(rows), 'ARTIFACT_IDENTITIES_DUPLICATED')
    matches = [row for row in rows if row.get('id') == identity and row.get('name') == ARTIFACT]
    require(len(matches) == 1 and sum(row.get('name') == ARTIFACT for row in rows) == 1,
            'ORIGINAL_ARTIFACT_MISSING_OR_REPLACED')
    row = matches[0]
    origin = row.get('workflow_run', {})
    require(row.get('expired') is False and type(row.get('size_in_bytes')) is int
            and 0 < row['size_in_bytes'] <= binding.MAX_ARCHIVE
            and isinstance(row.get('digest'), str) and re.fullmatch(r'sha256:[a-f0-9]{64}', row['digest'])
            and isinstance(origin, dict) and origin.get('id') == run_id and origin.get('repository_id') == repository_id
            and origin.get('head_repository_id') == repository_id and origin.get('head_sha') == candidate
            and origin.get('head_branch') == 'main', 'ARTIFACT_ORIGIN_DIGEST_OR_SIZE_INVALID')
    created, updated = provenance.instant(row['created_at']), provenance.instant(row['updated_at'])
    require(job['uploadStart'] <= created <= job['uploadEnd'] and created <= updated <= now
            and now < provenance.instant(row['expires_at']), 'ARTIFACT_ATTEMPT_WINDOW_OR_EXPIRY_INVALID')
    return {'id': identity, 'name': ARTIFACT, 'digest': row['digest'][7:], 'size': row['size_in_bytes'],
            'createdAtUtc': created.isoformat()}


def schemathesis_execution(archive, role, metadata, job):
    name = 'artifacts/security/schemathesis/' + role + '.ndjson'
    require(name in archive.namelist() and 0 < archive.getinfo(name).file_size <= MAX_NDJSON,
            'SCHEMATHESIS_NATIVE_REPORT_MISSING_OR_OVERSIZE')
    total, count, initialized, finished = 0, 0, 0, 0
    digest = hashlib.sha256()
    with archive.open(name) as stream:
        while raw := stream.readline(MAX_LINE + 1):
            total += len(raw)
            count += 1
            require(len(raw) <= MAX_LINE and total <= MAX_NDJSON and count <= 100000
                    and finished == 0, 'SCHEMATHESIS_NATIVE_STREAM_INVALID')
            digest.update(raw)
            event = release.parse_json_bytes(raw)
            require(len(event) == 1 and next(iter(event)) in EVENTS, 'SCHEMATHESIS_EVENT_SCHEMA_INVALID')
            if 'Initialize' in event:
                payload = event['Initialize']
                require(count == 1 and isinstance(payload, dict)
                        and payload.get('schemathesis_version') == metadata['schemathesis_version']
                        and payload.get('seed') == metadata['seed'], 'SCHEMATHESIS_INITIALIZE_MISMATCH')
                initialized += 1
            if 'EngineFinished' in event:
                payload = event['EngineFinished']
                require(isinstance(payload, dict) and payload.get('stop_reason') == 'completed'
                        and type(payload.get('timestamp')) in (int, float)
                        and math.isfinite(payload['timestamp'])
                        and type(payload.get('running_time')) in (int, float) and math.isfinite(payload['running_time'])
                        and 0 < payload['running_time'] <= (job['finish'] - job['start']).total_seconds()
                        and isinstance(payload.get('payload'), dict)
                        and payload['payload'].get('reauth_broke') is False,
                        'SCHEMATHESIS_ENGINE_DID_NOT_COMPLETE')
                require(job['start'].timestamp() <= payload['timestamp'] <= job['finish'].timestamp(),
                        'SCHEMATHESIS_EXECUTION_WINDOW_MISMATCH')
                finished += 1
    require(initialized == finished == 1, 'SCHEMATHESIS_NATIVE_EXECUTION_MISSING')
    return digest.hexdigest(), count


def zap_summary(document, metadata, source_digests):
    require(set(document) == {'schemaVersion', 'control', 'scanner', 'role', 'target', 'inputs',
                              'sanitization', 'summary', 'alerts', 'attribution'}
            and type(document['schemaVersion']) is int and document['schemaVersion'] == 1
            and document['control'] == 'SEC-06' and document['role'] == metadata['role'], 'ZAP_NATIVE_SCHEMA_INVALID')
    require(isinstance(document['scanner'], dict) and type(document['scanner'].get('exitCode')) is int
            and isinstance(document['sanitization'], dict) and number(document['sanitization'].get('forbiddenValueCount'), True)
            and document['sanitization'].get('unsanitizedAllowed') is False
            and document['scanner'] == {'name': 'OWASP ZAP', 'version': metadata['scannerVersion'],
                                    'image': metadata['scannerImage'], 'exitCode': 0}
            and document['target'] == {'origin': 'http://app:8080', 'isolated': True, 'externalNetworkAccess': False}
            and document['inputs'] == {key: metadata[key] for key in
                                      ('openApiSha256', 'automationPlanSha256', 'policySha256', 'addonListSha256')}
            and document['sanitization'] == {key: metadata[key] for key in ('forbiddenValueCount', 'unsanitizedAllowed')}
            and metadata['automationPlanSha256'] == source_digests[PLAN]
            and metadata['policySha256'] == source_digests[POLICY], 'ZAP_NATIVE_INPUT_OR_SCOPE_MISMATCH')
    alerts = document['alerts']
    require(isinstance(alerts, list) and len(alerts) <= 10000, 'ZAP_NATIVE_ALERT_SCOPE_INVALID')
    risks, rules, instances = Counter(), Counter(), Counter()
    for alert in alerts:
        require(isinstance(alert, dict) and set(alert) == {'confidence', 'cweId', 'instanceCount', 'instances',
                                                        'name', 'risk', 'ruleId', 'wascId'}
                and alert['risk'] in RISKS and isinstance(alert['ruleId'], str)
                and re.fullmatch(r'[0-9]{1,8}', alert['ruleId'])
                and number(alert['instanceCount'], True) and isinstance(alert['instances'], list)
                and len(alert['instances']) == alert['instanceCount'], 'ZAP_NATIVE_ALERT_SCHEMA_INVALID')
        risks[alert['risk']] += 1
        rules[alert['ruleId']] += 1
        instances[alert['ruleId']] += alert['instanceCount']
    expected = {'uniqueAlertsByRisk': {risk: risks[risk] for risk in RISKS},
                'uniqueAlertsByRule': dict(sorted(rules.items())), 'instancesByRule': dict(sorted(instances.items())),
                'blockingHighAlerts': risks['High']}
    summary = document['summary']
    require(isinstance(summary, dict) and set(summary) == set(expected)
            and number(summary['blockingHighAlerts'])
            and all(isinstance(summary[field], dict) and all(number(value) for value in summary[field].values())
                    for field in ('uniqueAlertsByRisk', 'uniqueAlertsByRule', 'instancesByRule'))
            and summary == expected and risks['High'] == 0
            and all(metadata[field] == risks[risk] for field, risk in
                    (('highAlerts', 'High'), ('mediumAlerts', 'Medium'), ('lowAlerts', 'Low'),
                     ('informationalAlerts', 'Informational'))), 'ZAP_NATIVE_SUMMARY_OR_FINDING_MISMATCH')
    attribution = document['attribution']
    require(isinstance(attribution, dict) and set(attribution) == {'status', 'scopeRuleId', 'instances'}
            and attribution['status'] == 'captured' and attribution['scopeRuleId'] == '10062'
            and isinstance(attribution['instances'], list), 'ZAP_ATTRIBUTION_UNAVAILABLE')
    return expected


def native_observations(archive, source_digests, job):
    contract, contract_digest = artifact_json(archive, 'artifacts/openapi/coglatas-openapi.json')
    require(isinstance(contract.get('openapi'), str) and contract['openapi'].startswith('3.')
            and isinstance(contract.get('paths'), dict) and contract['paths'], 'OPENAPI_NATIVE_CONTRACT_INVALID')
    operations = {method.upper() + ' ' + path for path, item in contract['paths'].items()
                  if isinstance(item, dict) for method in METHODS if method in item}
    require(0 < len(operations) <= 10000, 'OPENAPI_NATIVE_OPERATION_SCOPE_INVALID')
    hashes = {'openApi': contract_digest}
    schemathesis, zap = [], []
    observed_operations = set()
    for role in advisory.SCHEMATHESIS_ROLES:
        metadata, digest = artifact_json(archive, 'artifacts/security/schemathesis/' + role + '.metadata.json')
        require(set(metadata) == advisory.SCHEMATHESIS_FIELDS and metadata['role'] == role
                and metadata['contract_sha256'] == contract_digest and metadata['lane'] == 'deep'
                and metadata['schemathesis_version'] == '4.25.2', 'SCHEMATHESIS_NATIVE_METADATA_MISMATCH')
        require(all(number(metadata[field], True) for field in ('request_count', 'operation_count', 'seed'))
                and all(type(metadata[field]) is int and metadata[field] == 0 for field in ('scanner_exit', 'network_errors')),
                'SCHEMATHESIS_NATIVE_EXECUTION_ERROR')
        selected = metadata['operations']
        require(isinstance(selected, list) and len(selected) == metadata['operation_count']
                and all(isinstance(item, str) for item in selected) and len(set(selected)) == len(selected)
                and set(selected) <= operations, 'SCHEMATHESIS_NATIVE_OPERATION_MISMATCH')
        native_digest, events = schemathesis_execution(archive, role, metadata, job)
        hashes['schemathesisMetadata:' + role], hashes['schemathesisNative:' + role] = digest, native_digest
        observed_operations.update(selected)
        schemathesis.append({'role': role, 'requestCount': metadata['request_count'], 'operationCount': len(selected),
                             'unobservedOpenApiOperationCount': len(operations - set(selected)), 'nativeEventCount': events,
                             'nativeEngineOutcome': 'COMPLETED', 'transportCountersAuthentication': 'PRODUCER_REPORTED'})
    for role in advisory.ZAP_ROLES:
        metadata, digest = artifact_json(archive, 'artifacts/security/zap/' + role + '.metadata.json')
        require(set(metadata) == advisory.ZAP_FIELDS and metadata['role'] == role and metadata['control'] == 'SEC-06'
                and metadata['status'] == 'passed' and metadata['openApiSha256'] == contract_digest
                and metadata['scannerVersion'] == '2.17.0' and metadata['scannerImage'] == advisory.ZAP_IMAGE
                and metadata['unsanitizedAllowed'] is False and number(metadata['forbiddenValueCount'], True),
                'ZAP_NATIVE_METADATA_MISMATCH')
        require(all(number(metadata[field]) for field in ('highAlerts', 'mediumAlerts', 'lowAlerts', 'informationalAlerts')),
                'ZAP_NATIVE_COUNT_INVALID')
        require(all(isinstance(metadata[field], str) and release.SHA256_RE.fullmatch(metadata[field])
                    for field in ('automationPlanSha256', 'policySha256', 'addonListSha256')), 'ZAP_NATIVE_HASH_INVALID')
        document, native_digest = artifact_json(archive, 'artifacts/security/zap/' + role + '.json')
        summary = zap_summary(document, metadata, source_digests)
        hashes['zapMetadata:' + role], hashes['zapNative:' + role] = digest, native_digest
        zap.append({'role': role, 'nativeOutcome': 'PASSED', 'observedAlertTypesByRisk': summary['uniqueAlertsByRisk'],
                    'planAndPolicySourceBytesBinding': 'MATCHED', 'requestCoverage': 'UNVERIFIED'})
    return {'openApiOperationCount': len(operations), 'schemathesis': schemathesis, 'zapApi': zap,
            'unobservedOpenApiOperationCount': len(operations - observed_operations), 'inputSha256': hashes}


def resolve(api, candidate, run_id, attempt, artifact_id, archive_path, now):
    require(isinstance(candidate, str) and re.fullmatch(r'[a-f0-9]{40}', candidate)
            and all(type(value) is int and 0 < value < 10**20 for value in (run_id, attempt, artifact_id))
            and now.tzinfo is not None, 'EXPECTED_CONTEXT_INVALID')
    prefix = 'repos/' + provenance.REPOSITORY
    run_path = prefix + '/actions/runs/' + str(run_id)
    repository_id, workflow_id = provenance.check_run(api(run_path), candidate, run_id, attempt, now)
    workflow = api(prefix + '/actions/workflows/' + str(workflow_id))
    require(workflow.get('id') == workflow_id and workflow.get('path') == provenance.WORKFLOW, 'MAIN_WORKFLOW_MISMATCH')
    sources = {path: hashlib.sha256(source_bytes(api, prefix, path, candidate)).hexdigest()
               for path in (provenance.WORKFLOW, PRODUCER_WORKFLOW, PLAN, POLICY)}
    jobs_path = run_path + '/attempts/' + str(attempt) + '/jobs'
    job = upload_job(provenance.collect(api, jobs_path, 'jobs'), candidate, run_id, attempt, now)
    artifacts_path = run_path + '/artifacts'
    artifact = select_artifact(provenance.collect(api, artifacts_path, 'artifacts'), artifact_id,
                               candidate, run_id, repository_id, job, now)
    require(archive_path.is_file() and archive_path.stat().st_size == artifact['size'], 'ORIGINAL_ZIP_SIZE_MISMATCH')
    with binding.verified_zip(archive_path, artifact['digest']) as archive:
        observed = native_observations(archive, sources, job)
    require(provenance.check_run(api(run_path), candidate, run_id, attempt, now) == (repository_id, workflow_id)
            and upload_job(provenance.collect(api, jobs_path, 'jobs'), candidate, run_id, attempt, now) == job
            and select_artifact(provenance.collect(api, artifacts_path, 'artifacts'), artifact_id,
                                candidate, run_id, repository_id, job, now) == artifact, 'LIVE_AUTHORITY_CHANGED')
    return {'schema': SCHEMA, 'mode': 'ADVISORY', 'integrityStatus': 'MATCHED',
            'qualification': 'LIVE_GITHUB_MAIN_NATIVE_API_OBSERVATION', 'repository': provenance.REPOSITORY,
            'candidateSha': candidate, 'runId': str(run_id), 'runAttempt': str(attempt),
            'observedAtUtc': now.isoformat(), 'workflowSourceSha256': sources,
            'jobId': job['id'], 'artifact': artifact, 'nativeObservations': observed,
            'artifactAttemptBinding': 'UPLOAD_STEP_TIME_WINDOW_OBSERVED',
            'imageSubjectBinding': 'UNVERIFIED', 'applicationBuildBinding': 'UNVERIFIED',
            'cryptographicVerification': 'UNVERIFIED', 'executionAttestation': 'UNVERIFIED', 'personalOwnerApproval': 'UNVERIFIED',
            'acceptanceQualifiedControlCount': 0, 'outstandingAcceptanceControlCount': len(advisory.TIER_B),
            'releaseAcceptance': 'BLOCKED', 'preAvaloniaVerdict': 'BLOCKED',
            'limits': ['Live HTTPS metadata is a non-atomic server observation, not signed execution attestation.',
                       'The artifact API has no upload-attempt identity; the observed upload-step window is a consistency control.',
                       'Native metadata transport counters are producer-reported; scope gaps stay explicit.',
                       'The retained ZAP format does not establish actual request or browser/AJAX coverage.',
                       'API report bytes and policy sources do not establish release-image, loaded-build or canonical contract coverage.',
                       'No owner decision, exception, complete category acceptance or gate promotion is granted.']}


def bounded_id(value):
    if not re.fullmatch(r'[1-9][0-9]{0,19}', value):
        raise argparse.ArgumentTypeError('Unsupported numeric identity.')
    return int(value)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--candidate-sha', required=True)
    parser.add_argument('--run-id', required=True, type=bounded_id)
    parser.add_argument('--run-attempt', required=True, type=bounded_id)
    parser.add_argument('--artifact-id', required=True, type=bounded_id)
    parser.add_argument('--artifact', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    arguments = sys.argv[1:]
    flags = {'--candidate-sha', '--run-id', '--run-attempt', '--artifact-id', '--artifact', '--output'}
    if arguments not in (['-h'], ['--help']):
        if (len(arguments) != 2 * len(flags) or set(arguments[::2]) != flags
                or any(len(value) > 4096 or any(char in value for char in '\0\r\n') for value in arguments)):
            print('SEC-14 Main observation ERROR: command inputs are unsupported.')
            return 1
    args = parser.parse_args(arguments)
    try:
        # Reserve exclusive output before live I/O; historical receipts are never replaced.
        with args.output.open('x', encoding='utf-8', newline='\n') as output:
            try:
                report = resolve(provenance.LiveGitHub(os.environ.get('GH_TOKEN') or os.environ.get('GITHUB_TOKEN')),
                                 args.candidate_sha, args.run_id, args.run_attempt, args.artifact_id,
                                 args.artifact, datetime.now(timezone.utc))
                result = 0
            except (OSError, ValueError, TypeError, KeyError, RuntimeError):
                report = {'schema': SCHEMA, 'mode': 'ADVISORY', 'integrityStatus': 'ERROR',
                          'errorCode': 'LIVE_MAIN_NATIVE_INPUT_OR_AUTHORITY_INVALID',
                          'acceptanceQualifiedControlCount': 0, 'outstandingAcceptanceControlCount': len(advisory.TIER_B),
                          'releaseAcceptance': 'BLOCKED', 'preAvaloniaVerdict': 'BLOCKED',
                          'personalOwnerApproval': 'UNVERIFIED', 'executionAttestation': 'UNVERIFIED'}
                result = 1
            json.dump(report, output, indent=2)
            output.write('\n')
    except OSError:
        print('SEC-14 Main observation ERROR: output unavailable or already retained.')
        return 1
    print('SEC-14 Main native observation ' + report['integrityStatus'] + '; acceptance BLOCKED.')
    return result


if __name__ == '__main__':
    raise SystemExit(main())
