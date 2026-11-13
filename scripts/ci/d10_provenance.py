"""D10 v1: single-job, first-attempt, same-tree PR Shadow capture provenance."""
from datetime import datetime, timezone
import argparse
import base64
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import sys
import tempfile
import zipfile
import zlib

import sec_arch_reconcile as archive_binding
from sec_arch_github_provenance import collect, instant
from d10_transport import ArtifactGitHub, MAX_ZIP

REPOSITORY = 'NYGsatoshi/Coglatas'
REPOSITORY_ID = 1261244608
WORKFLOW = '.github/workflows/playwright-native-capture-shadow.yml'
PRODUCER_WORKFLOW = '.github/workflows/playwright-native-capture-producer.yml'
JOB = 'Shadow Playwright native capture / Native capture'
ARTIFACT = 'playwright-native-shadow-v1'
ROLE = 'PLAYWRIGHT_STATIC_SHADOW_V1'
FILES = {'source.json', 'applicability.json', 'native.json', 'capture-receipt.json'}
MAX_REPORT = 2 * 1024 * 1024
STATES = ('METADATA_VERIFIED', 'ARCHIVE_BYTES_VERIFIED', 'REPORT_IDENTITY_VERIFIED',
          'PRODUCER_AUTHENTICATED', 'CONTENT_RECONCILED', 'PROVENANCE_VERIFIED')
PINS = {'playwright_case_reconcile': '56bc4e341f580682e9441b4425723666b5dfe67e73fe250bb87df79b00d71b16',
        'playwright_applicability_reconcile': '5b75507ce54549131faeda6024c12f30a3c7e6e745fbb46ddb52f5b15a648dba'}
POLICY_FIELDS = {'schema', 'repository', 'repositoryId', 'prNumber', 'reviewedHeadSha', 'baseSha',
                 'testedMergeSha', 'sourceTreeSha', 'workflowId', 'workflowPath', 'workflowSourceSha256',
                 'producerWorkflowSourceSha256', 'runId', 'attempt', 'jobId', 'artifactId', 'sourceRosterSha256', 'applicabilitySha256'}


class Refused(ValueError):
    pass


def need(value, code):
    if not value: raise Refused(code)


def fields(value, expected, code):
    need(type(value) is dict and set(value) == set(expected), code)


def hex_value(value, size):
    return type(value) is str and re.fullmatch('[a-f0-9]{' + str(size) + '}', value) is not None


def int_equal(value, expected):
    return type(value) is int and value == expected


def document(raw, limit=MAX_REPORT):
    need(type(raw) is bytes and 0 < len(raw) <= limit, 'INPUT_INVALID')
    return json.loads(raw, object_pairs_hook=archive_binding.unique_object, parse_constant=archive_binding.reject_constant)


def policy_document(raw, expected_digest):
    need(hex_value(expected_digest, 64) and hashlib.sha256(raw).hexdigest() == expected_digest, 'POLICY_DIGEST_MISMATCH')
    policy = document(raw, 65536)
    fields(policy, POLICY_FIELDS, 'POLICY_INVALID')
    need(policy['schema'] == 'coglatas.d10-policy.v1' and policy['repository'] == REPOSITORY and
         type(policy['repositoryId']) is int and policy['repositoryId'] == REPOSITORY_ID and
         policy['workflowPath'] == WORKFLOW, 'POLICY_INVALID')
    for key in ['prNumber', 'workflowId', 'runId', 'jobId', 'artifactId']:
        need(type(policy[key]) is int and 0 < policy[key] <= 2**63 - 1, 'POLICY_INVALID')
    need(type(policy['attempt']) is int and policy['attempt'] == 1, 'ATTEMPT_UNSUPPORTED')
    for key in ['reviewedHeadSha', 'baseSha', 'testedMergeSha', 'sourceTreeSha']: need(hex_value(policy[key], 40), 'POLICY_INVALID')
    for key in ['workflowSourceSha256', 'producerWorkflowSourceSha256', 'sourceRosterSha256', 'applicabilitySha256']: need(hex_value(policy[key], 64), 'POLICY_INVALID')
    need(len({policy['reviewedHeadSha'], policy['testedMergeSha'], policy['baseSha']}) == 3, 'POLICY_INVALID')
    return policy


def snapshot(api, p, now):
    prefix = 'repos/' + REPOSITORY
    pr = api(prefix + '/pulls/' + str(p['prNumber']))
    for side in ['head', 'base']:
        need(pr[side]['repo']['id'] == REPOSITORY_ID and pr[side]['repo']['full_name'] == REPOSITORY, 'REPOSITORY_MISMATCH')
    need(pr['state'] == 'open' and pr['head']['sha'] == p['reviewedHeadSha'] and
         pr['base']['ref'] == 'SecurityTestImplimation' and pr['base']['sha'] == p['baseSha'], 'PR_IDENTITY_MISMATCH')
    need(pr['merge_commit_sha'] == p['testedMergeSha'], 'TESTED_MERGE_MISMATCH')
    branch = api(prefix + '/branches/SecurityTestImplimation')
    need(branch['commit']['sha'] == p['baseSha'], 'PR_IDENTITY_MISMATCH')
    head = api(prefix + '/git/commits/' + p['reviewedHeadSha'])
    merge = api(prefix + '/git/commits/' + p['testedMergeSha'])
    need(head['sha'] == p['reviewedHeadSha'] and merge['sha'] == p['testedMergeSha'] and
         [row['sha'] for row in merge['parents']] == [p['baseSha'], p['reviewedHeadSha']], 'TESTED_MERGE_MISMATCH')
    need(head['tree']['sha'] == merge['tree']['sha'] == p['sourceTreeSha'], 'SOURCE_EQUIVALENCE_UNVERIFIED')
    run_path = prefix + '/actions/runs/' + str(p['runId'])
    run = api(run_path)
    for key in ['repository', 'head_repository']:
        need(run[key]['id'] == REPOSITORY_ID and run[key]['full_name'] == REPOSITORY, 'REPOSITORY_MISMATCH')
    need(type(run['id']) is int and run['id'] == p['runId'] and type(run['run_attempt']) is int and
         run['run_attempt'] == p['attempt'] and run['head_sha'] == p['reviewedHeadSha'] and
         run['head_branch'] == pr['head']['ref'] and run['event'] == 'pull_request' and
         run['status'] == 'completed' and run['conclusion'] == 'success', 'RUN_IDENTITY_MISMATCH')
    need(int_equal(run['workflow_id'], p['workflowId']) and run['path'] == WORKFLOW and
         type(run['check_suite_id']) is int and run['check_suite_id'] > 0, 'WORKFLOW_MISMATCH')
    # Run head_sha is the reviewed head, not the executed merge. Require an
    # independent server revision binding from the local reusable producer call.
    references = run['referenced_workflows']
    need(type(references) is list and len(references) == 1, 'TESTED_MERGE_UNAUTHENTICATED')
    fields(references[0], {'path', 'ref', 'sha'}, 'TESTED_MERGE_UNAUTHENTICATED')
    need(references[0] == {'path': REPOSITORY + '/' + PRODUCER_WORKFLOW + '@' + p['testedMergeSha'],
         'ref': 'refs/pull/' + str(p['prNumber']) + '/merge', 'sha': p['testedMergeSha']}, 'TESTED_MERGE_UNAUTHENTICATED')
    requests = run['pull_requests']
    need(type(requests) is list and len(requests) == 1 and int_equal(requests[0]['number'], p['prNumber']) and
         requests[0]['head']['sha'] == p['reviewedHeadSha'] and requests[0]['head']['ref'] == pr['head']['ref'] and
         requests[0]['base']['sha'] == p['baseSha'] and requests[0]['base']['ref'] == 'SecurityTestImplimation', 'RUN_IDENTITY_MISMATCH')
    need(instant(run['created_at']) <= instant(run['updated_at']) <= now, 'RUN_IDENTITY_MISMATCH')
    workflow = api(prefix + '/actions/workflows/' + str(p['workflowId']))
    need(int_equal(workflow['id'], p['workflowId']) and workflow['path'] == WORKFLOW and workflow['state'] == 'active', 'WORKFLOW_MISMATCH')
    for path, digest in [(WORKFLOW, p['workflowSourceSha256']), (PRODUCER_WORKFLOW, p['producerWorkflowSourceSha256'])]:
        source = api(prefix + '/contents/' + path + '?ref=' + p['testedMergeSha'])
        need(source['type'] == 'file' and source['encoding'] == 'base64' and type(source['size']) is int and
             0 < source['size'] <= MAX_REPORT, 'WORKFLOW_MISMATCH')
        raw = base64.b64decode(''.join(source['content'].split()), validate=True)
        need(len(raw) == source['size'] and hashlib.sha256(raw).hexdigest() == digest and
             hashlib.sha1(b'blob ' + str(len(raw)).encode() + b'\0' + raw).hexdigest() == source['sha'], 'WORKFLOW_MISMATCH')
    jobs = collect(api, run_path + '/attempts/1/jobs', 'jobs')
    need(len(jobs) == 1, 'PRODUCER_SCOPE_UNSUPPORTED')
    job = jobs[0]
    need(type(job['id']) is int and job['id'] == p['jobId'] and job['name'] == JOB and
         int_equal(job['run_id'], p['runId']) and type(job['run_attempt']) is int and job['run_attempt'] == 1 and
         job['head_sha'] == p['reviewedHeadSha'] and job['status'] == 'completed' and job['conclusion'] == 'success', 'PRODUCER_MISMATCH')
    need(instant(run['created_at']) <= instant(job['started_at']) <= instant(job['completed_at']) <= instant(run['updated_at']), 'PRODUCER_MISMATCH')
    check_path = prefix + '/check-runs/'
    check_url = job['check_run_url']
    need(type(check_url) is str and re.fullmatch('https://api.github.com/' + check_path + '[1-9][0-9]*', check_url), 'PRODUCER_MISMATCH')
    check = api(check_url.removeprefix('https://api.github.com/'))
    need(type(check['id']) is int and str(check['id']) == check_url.rsplit('/', 1)[1] and
         check['app']['id'] == 15368 and check['app']['slug'] == 'github-actions' and check['name'] == JOB and
         check['head_sha'] == p['reviewedHeadSha'] and check['status'] == 'completed' and check['conclusion'] == 'success'
         and int_equal(check['check_suite']['id'], run['check_suite_id']), 'PRODUCER_MISMATCH')
    artifacts = collect(api, run_path + '/artifacts', 'artifacts')
    need(all(type(row.get('id')) is int and row['id'] > 0 for row in artifacts), 'ARTIFACT_MISMATCH')
    matches = [row for row in artifacts if row.get('id') == p['artifactId']]
    need(len(matches) == 1, 'ARTIFACT_MISSING')
    artifact = matches[0]
    need(artifact['name'] == ARTIFACT and sum(row.get('name') == ARTIFACT for row in artifacts) == 1 and
         len({row['id'] for row in artifacts}) == len(artifacts), 'ARTIFACT_MISMATCH')
    direct = api(prefix + '/actions/artifacts/' + str(p['artifactId']))
    relevant = ['id', 'name', 'size_in_bytes', 'digest', 'expired', 'created_at', 'updated_at', 'expires_at', 'workflow_run']
    need({key: direct[key] for key in relevant} == {key: artifact[key] for key in relevant}, 'ARTIFACT_MISMATCH')
    origin = artifact['workflow_run']
    need(int_equal(origin['id'], p['runId']) and origin['repository_id'] == REPOSITORY_ID and origin['head_repository_id'] == REPOSITORY_ID and
         origin['head_sha'] == p['reviewedHeadSha'] and origin['head_branch'] == pr['head']['ref'], 'ARTIFACT_MISMATCH')
    need(artifact['expired'] is False and instant(artifact['created_at']) <= instant(artifact['updated_at']) <= now < instant(artifact['expires_at']), 'ARTIFACT_EXPIRED')
    need(instant(job['started_at']) <= instant(artifact['created_at']) <= instant(job['completed_at']), 'ARTIFACT_MISMATCH')
    need(type(artifact['size_in_bytes']) is int and 0 < artifact['size_in_bytes'] <= MAX_ZIP and
         type(artifact['digest']) is str and re.fullmatch('sha256:[a-f0-9]{64}', artifact['digest']), 'ARTIFACT_MISMATCH')
    return {'artifact': {key: artifact[key] for key in relevant}, 'jobId': job['id'],
            'checkRunId': check['id'], 'workflowDigest': p['workflowSourceSha256'],
            'producerWorkflowDigest': p['producerWorkflowSourceSha256'],
            'reviewedHead': p['reviewedHeadSha'], 'testedMerge': p['testedMergeSha'], 'tree': p['sourceTreeSha']}


def reports_from_zip(data, artifact):
    need(type(data) is bytes and 0 < len(data) <= MAX_ZIP and len(data) == artifact['size_in_bytes'], 'ARCHIVE_INVALID')
    digest = hashlib.sha256(data).hexdigest()
    need(artifact['digest'] == 'sha256:' + digest, 'ARCHIVE_DIGEST_MISMATCH')
    need(data[:4] == b'PK\x03\x04' and len(data) >= 22 and data[-22:-18] == b'PK\x05\x06' and data[-2:] == b'\0\0', 'ARCHIVE_INVALID')
    with tempfile.TemporaryDirectory(prefix='coglatas-d10-') as directory:
        path = Path(directory) / 'snapshot.zip'
        with path.open('xb') as output: output.write(data)
        with archive_binding.verified_zip(path, digest, max_archive=MAX_ZIP, max_expanded=4 * MAX_REPORT, max_members=4) as archive:
            need(set(archive.namelist()) == FILES, 'REPORT_MISSING')
            need(min(row.header_offset for row in archive.infolist()) == 0, 'ARCHIVE_INVALID')
            for row in archive.infolist():
                mode = row.external_attr >> 16
                need(not row.is_dir() and stat.S_IFMT(mode) in [0, stat.S_IFREG] and
                     row.compress_type in [zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED] and
                     0 < row.file_size <= MAX_REPORT and row.file_size <= 100 * max(1, row.compress_size), 'ARCHIVE_INVALID')
            raw = {name: archive_binding.read_member(archive, name, MAX_REPORT) for name in sorted(FILES)}
    return raw


def validate_reports(raw, p):
    reports = {name: document(value) for name, value in raw.items()}
    receipt = reports['capture-receipt.json']
    fields(receipt, {'schema', 'repository', 'reviewedHeadSha', 'testedMergeSha', 'workflowId', 'runId',
                     'attempt', 'jobId', 'producerRole', 'reportDigests'}, 'REPORT_IDENTITY_MISMATCH')
    expected = {'schema': 'coglatas.playwright-capture-receipt.v1', 'repository': REPOSITORY,
                'reviewedHeadSha': p['reviewedHeadSha'], 'testedMergeSha': p['testedMergeSha'],
                'workflowId': p['workflowId'], 'runId': p['runId'], 'attempt': p['attempt'], 'jobId': p['jobId'], 'producerRole': ROLE}
    need(all(type(receipt[key]) is type(value) and receipt[key] == value for key, value in expected.items()), 'REPORT_IDENTITY_MISMATCH')
    digests = {name: hashlib.sha256(raw[name]).hexdigest() for name in sorted(FILES - {'capture-receipt.json'})}
    need(receipt['reportDigests'] == digests, 'REPORT_IDENTITY_MISMATCH')
    source, applicability, native = (reports[name] for name in ['source.json', 'applicability.json', 'native.json'])
    need(all(value['sourceSha'] == p['testedMergeSha'] and value['suite'] == 'angular-static' for value in [source, applicability, native])
         and source['rosterSha256'] == p['sourceRosterSha256'] and applicability['applicabilitySha256'] == p['applicabilitySha256']
         and native['executionKind'] == 'GITHUB_ACTIONS' and native['runId'] == str(p['runId']) and
         type(native['runAttempt']) is int and native['runAttempt'] == p['attempt'], 'REPORT_IDENTITY_MISMATCH')
    return digests


def pinned_content(raw, p, content_root):
    # Only two explicitly pinned verifier modules execute; report or API bytes never execute.
    saved = {name: sys.modules.get(name) for name in PINS}
    try:
        with tempfile.TemporaryDirectory(prefix='coglatas-d10-code-') as directory:
            for name, digest in PINS.items():
                source = read_regular(Path(content_root) / (name + '.py'), 1024 * 1024).replace(b'\r\n', b'\n')
                need(hashlib.sha256(source).hexdigest() == digest, 'DEPENDENCY_UNAVAILABLE')
                path = Path(directory) / (name + '.py')
                with path.open('xb') as output: output.write(source)
                spec = importlib.util.spec_from_file_location(name, path)
                module = importlib.util.module_from_spec(spec)
                sys.modules[name] = module
                spec.loader.exec_module(module)
            result = sys.modules['playwright_applicability_reconcile'].reconcile(raw['source.json'], raw['native.json'], raw['applicability.json'],
                source_sha=p['testedMergeSha'], execution_kind='GITHUB_ACTIONS', run_id=str(p['runId']), attempt=p['attempt'],
                applicability_sha256=p['applicabilitySha256'])
            need(result['decision'] == 'CONTENT_MATCH', 'CONTENT_RECONCILIATION_FAILED')
            return result['counts']
    finally:
        for name, value in saved.items():
            if value is None: sys.modules.pop(name, None)
            else: sys.modules[name] = value


def read_regular(path, limit):
    path = Path(path).absolute()
    for part in [path, *path.parents]:
        info = part.lstat()
        need(not stat.S_ISLNK(info.st_mode) and not getattr(info, 'st_file_attributes', 0) & 0x400, 'INPUT_INVALID')
    fd = os.open(path, os.O_RDONLY | getattr(os, 'O_BINARY', 0) | getattr(os, 'O_NOFOLLOW', 0))
    try:
        info = os.fstat(fd)
        need(stat.S_ISREG(info.st_mode) and 0 < info.st_size <= limit, 'INPUT_INVALID')
        with os.fdopen(fd, 'rb', closefd=False) as stream: raw = stream.read(limit + 1)
        need(len(raw) == info.st_size, 'INPUT_INVALID')
        return raw
    finally: os.close(fd)


def verify(api, download, policy_raw, expected_policy_digest, now, content_root):
    result = {'schema': 'coglatas.d10-verification.v1', 'decision': 'REJECTED',
              'states': {name: False for name in STATES}, 'ARTIFACT_PROVENANCE_VERIFIED': False,
              'REQUIRED_GATE_QUALIFIED': False, 'ownerApproval': 'NOT_ESTABLISHED'}
    try:
        p = policy_document(policy_raw, expected_policy_digest)
        need(isinstance(now, datetime) and now.tzinfo is not None, 'INPUT_INVALID')
        authority = snapshot(api, p, now)
        result['states']['METADATA_VERIFIED'] = True
        # Exactly one API-authenticated job and no prior attempt: no ambiguous uploader exists.
        result['states']['PRODUCER_AUTHENTICATED'] = True
        raw = reports_from_zip(download(p['artifactId']), authority['artifact'])
        result['states']['ARCHIVE_BYTES_VERIFIED'] = True
        digests = validate_reports(raw, p)
        result['states']['REPORT_IDENTITY_VERIFIED'] = True
        counts = pinned_content(raw, p, content_root)
        result['states']['CONTENT_RECONCILED'] = True
        try:
            after = snapshot(api, p, now)
        except (OSError, ValueError, TypeError, KeyError, RuntimeError, AttributeError):
            raise Refused('AUTHORITY_CHANGED') from None
        need(after == authority, 'AUTHORITY_CHANGED')
        result['states']['PROVENANCE_VERIFIED'] = True
        result.update(decision='VERIFIED', ARTIFACT_PROVENANCE_VERIFIED=True, repository=REPOSITORY,
                      reviewedHeadSha=p['reviewedHeadSha'], testedMergeSha=p['testedMergeSha'], sourceTreeSha=p['sourceTreeSha'],
                      workflowId=p['workflowId'], workflowPath=WORKFLOW, runId=p['runId'], attempt=p['attempt'],
                      producerWorkflowPath=PRODUCER_WORKFLOW,
                      producerJobId=p['jobId'], artifactId=p['artifactId'], archiveSha256=authority['artifact']['digest'][7:],
                      reportDigests=digests, counts=counts, policySha256=expected_policy_digest)
    except Refused as error:
        result['reason'] = str(error)
        if str(error) == 'AUTHORITY_CHANGED': result['states'] = {name: False for name in STATES}
    except (OSError, ValueError, TypeError, KeyError, RuntimeError, AttributeError, RecursionError, EOFError,
            zipfile.BadZipFile, zlib.error):
        result['reason'] = 'D10_INPUT_OR_AUTHORITY_UNAVAILABLE'
    return result


class SafeParser(argparse.ArgumentParser):
    def error(self, message): raise Refused('ARGUMENTS_INVALID')


def audit_run(api, pr_number, reviewed_head, run_id):
    """Availability only. Artifact names never authenticate a producer."""
    need(type(pr_number) is int and pr_number > 0 and type(run_id) is int and run_id > 0 and
         hex_value(reviewed_head, 40), 'ARGUMENTS_INVALID')
    prefix = 'repos/' + REPOSITORY
    pr = api(prefix + '/pulls/' + str(pr_number))
    run = api(prefix + '/actions/runs/' + str(run_id))
    need(pr['head']['sha'] == reviewed_head and pr['base']['ref'] == 'SecurityTestImplimation' and
         run['head_sha'] == reviewed_head and run['repository']['id'] == REPOSITORY_ID and
         run['repository']['full_name'] == REPOSITORY, 'PR_IDENTITY_MISMATCH')
    artifacts = collect(api, prefix + '/actions/runs/' + str(run_id) + '/artifacts', 'artifacts')
    matches = [row for row in artifacts if row.get('name') == ARTIFACT]
    return {'schema': 'coglatas.d10-availability.v1', 'decision': 'UNVERIFIED', 'repository': REPOSITORY,
            'reviewedHeadSha': reviewed_head, 'runId': run_id, 'artifactCount': len(artifacts),
            'nativeCaptureCandidates': len(matches), 'availability': 'NATIVE_CAPTURE_ARTIFACT_ABSENT' if not matches else 'CANDIDATE_PRESENT_NOT_AUTHENTICATED',
            'states': {name: False for name in STATES}, 'ARTIFACT_PROVENANCE_VERIFIED': False,
            'REQUIRED_GATE_QUALIFIED': False, 'producerJobId': None, 'artifactId': None,
            'archiveSha256': None, 'reportDigests': None, 'sourceCases': None, 'nativeCases': None}


def main():
    try:
        parser = SafeParser(add_help=False)
        parser.add_argument('--policy')
        parser.add_argument('--policy-sha256')
        parser.add_argument('--content-root')
        parser.add_argument('--audit-pr')
        parser.add_argument('--audit-head')
        parser.add_argument('--audit-run')
        args = parser.parse_args()
        api = ArtifactGitHub(os.environ.get('GH_TOKEN') or os.environ.get('GITHUB_TOKEN'))
        audit = [args.audit_pr, args.audit_head, args.audit_run]
        ordinary = [args.policy, args.policy_sha256, args.content_root]
        if any(audit):
            need(all(audit) and not any(ordinary) and re.fullmatch('[1-9][0-9]{0,18}', args.audit_pr) and
                 re.fullmatch('[1-9][0-9]{0,18}', args.audit_run), 'ARGUMENTS_INVALID')
            result = audit_run(api, int(args.audit_pr), args.audit_head, int(args.audit_run))
        else:
            need(all(ordinary), 'ARGUMENTS_INVALID')
            result = verify(api, api.download, read_regular(Path(args.policy), 65536), args.policy_sha256,
                            datetime.now(timezone.utc), args.content_root)
    except (OSError, ValueError, TypeError, RuntimeError, KeyError, AttributeError):
        result = {'schema': 'coglatas.d10-verification.v1', 'decision': 'REJECTED', 'reason': 'D10_ARGUMENTS_OR_INPUT_REFUSED',
                  'states': {name: False for name in STATES}, 'ARTIFACT_PROVENANCE_VERIFIED': False, 'REQUIRED_GATE_QUALIFIED': False}
    print(json.dumps(result, sort_keys=True, separators=(',', ':')))
    return 0 if result['decision'] == 'VERIFIED' else 1


if __name__ == '__main__': raise SystemExit(main())
