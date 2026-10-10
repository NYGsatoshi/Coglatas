"""Synthetic Main authority and original-byte controls, never live acceptance."""

import argparse
import base64
from collections import Counter
from copy import deepcopy
from datetime import datetime, timezone
import hashlib
import io
import json
from pathlib import Path
import stat
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts' / 'ci'))
import sec14_main_security_evidence as observation
import sec_arch_github_provenance as provenance
import sec_arch_reconcile as binding
import release_supply_chain as release
import release_assurance_advisory as advisory

SHA = 'a' * 40
NOW = datetime(2026, 10, 10, tzinfo=timezone.utc)


def encoded(value):
    return json.dumps(value, separators=(',', ':')).encode()


class MainNativeSecurityEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.archive = self.root / 'original.zip'
        self.sources = {path: ('synthetic-source-' + path).encode() for path in
                        (provenance.WORKFLOW, observation.PRODUCER_WORKFLOW, observation.PLAN, observation.POLICY)}
        self.run = {'id': 10, 'run_attempt': 2, 'head_sha': SHA, 'head_branch': 'main', 'event': 'push',
                    'path': provenance.WORKFLOW, 'status': 'completed', 'conclusion': 'success', 'workflow_id': 20,
                    'repository': {'id': 30, 'full_name': provenance.REPOSITORY},
                    'head_repository': {'id': 30, 'full_name': provenance.REPOSITORY},
                    'created_at': '2026-10-09T00:00:00Z', 'updated_at': '2026-10-09T00:20:00Z'}
        self.jobs = [{'id': 40 + index, 'name': name, 'run_id': 10, 'run_attempt': 2, 'head_sha': SHA,
                      'status': 'completed', 'conclusion': 'success',
                      'started_at': '2026-10-09T00:00:00Z', 'completed_at': '2026-10-09T00:10:00Z',
                      'steps': [{'name': observation.UPLOAD, 'number': 9, 'status': 'completed', 'conclusion': 'success',
                                 'started_at': '2026-10-09T00:09:00Z', 'completed_at': '2026-10-09T00:09:59Z'}]}
                     for index, name in enumerate(provenance.REQUIRED_JOBS)]
        self.artifact = {'id': 50, 'name': observation.ARTIFACT, 'expired': False,
                         'created_at': '2026-10-09T00:09:30Z', 'updated_at': '2026-10-09T00:09:30Z',
                         'expires_at': '2026-10-11T00:00:00Z',
                         'workflow_run': {'id': 10, 'repository_id': 30, 'head_repository_id': 30,
                                          'head_sha': SHA, 'head_branch': 'main'}}
        self.members = self.fixture_members()
        self.write_archive()

    def fixture_members(self):
        contract = encoded({'openapi': '3.1.1', 'paths': {'/synthetic/read': {'get': {}}, '/synthetic/unobserved': {'get': {}}}})
        contract_digest = hashlib.sha256(contract).hexdigest()
        members = {'artifacts/openapi/coglatas-openapi.json': contract}
        for role in advisory.SCHEMATHESIS_ROLES:
            metadata = {'contract_sha256': contract_digest, 'lane': 'deep', 'network_errors': 0,
                        'operation_count': 1, 'operations': ['GET /synthetic/read'], 'request_count': 5,
                        'role': role, 'scanner_exit': 0, 'schemathesis_version': '4.25.2', 'seed': 25}
            finish = {'EngineFinished': {'stop_reason': 'completed', 'running_time': 10.0,
                       'timestamp': datetime(2026, 10, 9, 0, 5, tzinfo=timezone.utc).timestamp(),
                       'payload': {'reauth_broke': False}}}
            native = encoded({'Initialize': {'seed': 25, 'schemathesis_version': '4.25.2'}}) + b'\n' + encoded(finish) + b'\n'
            members['artifacts/security/schemathesis/' + role + '.metadata.json'] = encoded(metadata)
            members['artifacts/security/schemathesis/' + role + '.ndjson'] = native
        for role in advisory.ZAP_ROLES:
            metadata = {'control': 'SEC-06', 'role': role, 'status': 'passed', 'scannerVersion': '2.17.0',
                        'scannerImage': advisory.ZAP_IMAGE, 'openApiSha256': contract_digest,
                        'automationPlanSha256': hashlib.sha256(self.sources[observation.PLAN]).hexdigest(),
                        'policySha256': hashlib.sha256(self.sources[observation.POLICY]).hexdigest(),
                        'addonListSha256': 'b' * 64, 'forbiddenValueCount': 7, 'unsanitizedAllowed': False,
                        'highAlerts': 0, 'mediumAlerts': 0, 'lowAlerts': 0, 'informationalAlerts': 1}
            native = {'schemaVersion': 1, 'control': 'SEC-06', 'role': role,
                      'scanner': {'name': 'OWASP ZAP', 'version': '2.17.0', 'image': advisory.ZAP_IMAGE, 'exitCode': 0},
                      'target': {'origin': 'http://app:8080', 'isolated': True, 'externalNetworkAccess': False},
                      'inputs': {key: metadata[key] for key in ('openApiSha256', 'automationPlanSha256', 'policySha256', 'addonListSha256')},
                      'sanitization': {'forbiddenValueCount': 7, 'unsanitizedAllowed': False},
                      'summary': {'blockingHighAlerts': 0, 'uniqueAlertsByRisk': {'High': 0, 'Medium': 0, 'Low': 0, 'Informational': 1},
                                  'uniqueAlertsByRule': {'10024': 1}, 'instancesByRule': {'10024': 1}},
                      'alerts': [{'confidence': 'canary-credential-do-not-publish', 'cweId': '0', 'instanceCount': 1,
                                  'instances': [{'path': '/private-canary-do-not-publish'}], 'name': 'protected-canary-do-not-publish',
                                  'risk': 'Informational', 'ruleId': '10024', 'wascId': '0'}],
                      'attribution': {'status': 'captured', 'scopeRuleId': '10062', 'instances': []}}
            members['artifacts/security/zap/' + role + '.metadata.json'] = encoded(metadata)
            members['artifacts/security/zap/' + role + '.json'] = encoded(native)
        return members

    def write_archive(self):
        with zipfile.ZipFile(self.archive, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
            for name, value in self.members.items():
                archive.writestr(name, value)
        self.refresh_archive_identity()

    def refresh_archive_identity(self):
        raw = self.archive.read_bytes()
        self.artifact.update(digest='sha256:' + hashlib.sha256(raw).hexdigest(), size_in_bytes=len(raw))

    def mutate_member(self, name, mutate):
        value = json.loads(self.members[name])
        mutate(value)
        self.members[name] = encoded(value)
        self.write_archive()

    def api(self, path):
        if '/contents/' in path:
            name = path.split('/contents/', 1)[1].split('?ref=', 1)[0]
            raw = self.sources[name]
            return {'type': 'file', 'encoding': 'base64', 'size': len(raw), 'content': base64.b64encode(raw).decode(),
                    'sha': hashlib.sha1(b'blob ' + str(len(raw)).encode() + b'\0' + raw).hexdigest()}
        if '/workflows/' in path:
            return {'id': 20, 'path': provenance.WORKFLOW}
        if '/jobs?' in path:
            return {'total_count': len(self.jobs), 'jobs': deepcopy(self.jobs)}
        if '/artifacts?' in path:
            return {'total_count': 1, 'artifacts': [deepcopy(self.artifact)]}
        return deepcopy(self.run)

    def resolve(self, api=None, **kwargs):
        return observation.resolve(api or self.api, kwargs.get('candidate', SHA), kwargs.get('run_id', 10),
                                   kwargs.get('attempt', 2), kwargs.get('artifact_id', 50), self.archive, NOW)

    def test_live_snapshot_and_original_bytes_remain_advisory_and_keep_scope_gaps(self):
        result = self.resolve()
        self.assertEqual('MATCHED', result['integrityStatus'])
        self.assertEqual(5, len(result['nativeObservations']['schemathesis']))
        self.assertEqual(3, len(result['nativeObservations']['zapApi']))
        self.assertEqual(1, result['nativeObservations']['unobservedOpenApiOperationCount'])
        self.assertTrue(all(row['unobservedOpenApiOperationCount'] == 1 for row in result['nativeObservations']['schemathesis']))
        self.assertEqual('UNVERIFIED', result['imageSubjectBinding'])
        self.assertEqual('UNVERIFIED', result['applicationBuildBinding'])
        self.assertEqual('UNVERIFIED', result['personalOwnerApproval'])
        self.assertEqual(0, result['acceptanceQualifiedControlCount'])
        self.assertEqual(14, result['outstandingAcceptanceControlCount'])
        self.assertEqual('BLOCKED', result['releaseAcceptance'])
        rendered = json.dumps(result)
        for secret in ('canary-credential', '/private-canary', 'protected-canary', '/synthetic/read'):
            self.assertNotIn(secret, rendered)

    def test_independent_context_rejects_wrong_sha_run_attempt_id_and_boolean_identity(self):
        for arguments in ({'candidate': 'c' * 40}, {'run_id': 11}, {'attempt': 1}, {'artifact_id': 51},
                          {'run_id': True}, {'attempt': True}, {'artifact_id': True}, {'run_id': 10**20}):
            with self.subTest(arguments=arguments), self.assertRaises(ValueError): self.resolve(**arguments)

    def test_non_main_fork_failed_or_incomplete_run_cannot_qualify(self):
        for key, value in (('event', 'pull_request'), ('event', 'workflow_dispatch'), ('conclusion', 'failure'),
                           ('conclusion', 'cancelled'), ('status', 'in_progress'), ('head_branch', 'feature'),
                           ('path', '.github/workflows/other.yml'), ('head_repository', {'id': 31, 'full_name': 'fork/Coglatas'})):
            original = deepcopy(self.run)
            self.run[key] = value
            with self.subTest(key=key, value=value), self.assertRaises(ValueError): self.resolve()
            self.run = original

    def test_required_jobs_and_exact_upload_step_must_succeed_in_one_attempt(self):
        for mutation in ('missing-job', 'wrong-attempt', 'wrong-sha', 'failed', 'no-upload', 'duplicate-upload',
                         'upload-skipped', 'upload-failed', 'upload-reversed', 'upload-after-job'):
            original = deepcopy(self.jobs)
            job = self.jobs[-1]
            if mutation == 'missing-job': self.jobs.pop()
            if mutation == 'wrong-attempt': job['run_attempt'] = 1
            if mutation == 'wrong-sha': job['head_sha'] = 'b' * 40
            if mutation == 'failed': job['conclusion'] = 'failure'
            if mutation == 'no-upload': job['steps'] = []
            if mutation == 'duplicate-upload': job['steps'] *= 2
            if mutation == 'upload-skipped': job['steps'][0]['status'] = 'skipped'
            if mutation == 'upload-failed': job['steps'][0]['conclusion'] = 'failure'
            if mutation == 'upload-reversed': job['steps'][0]['completed_at'] = '2026-10-09T00:08:00Z'
            if mutation == 'upload-after-job': job['steps'][0]['completed_at'] = '2026-10-09T00:11:00Z'
            with self.subTest(mutation=mutation), self.assertRaises(ValueError): self.resolve()
            self.jobs = original

    def test_artifact_expiry_origin_digest_size_and_upload_window_fail_closed(self):
        for mutation in ('expired', 'expiry', 'wrong-id', 'wrong-name', 'wrong-sha', 'wrong-repository',
                         'wrong-run', 'wrong-branch', 'digest', 'size', 'pre-upload', 'post-upload', 'future-update'):
            original = deepcopy(self.artifact)
            if mutation == 'expired': self.artifact['expired'] = True
            if mutation == 'expiry': self.artifact['expires_at'] = '2026-10-09T00:00:00Z'
            if mutation == 'wrong-id': self.artifact['id'] = 51
            if mutation == 'wrong-name': self.artifact['name'] = 'self-declared-approval'
            if mutation == 'wrong-sha': self.artifact['workflow_run']['head_sha'] = 'b' * 40
            if mutation == 'wrong-repository': self.artifact['workflow_run']['head_repository_id'] = 31
            if mutation == 'wrong-run': self.artifact['workflow_run']['id'] = 11
            if mutation == 'wrong-branch': self.artifact['workflow_run']['head_branch'] = 'feature'
            if mutation == 'digest': self.artifact['digest'] = 'sha256:' + 'f' * 64
            if mutation == 'size': self.artifact['size_in_bytes'] += 1
            if mutation == 'pre-upload': self.artifact['created_at'] = '2026-10-09T00:08:00Z'
            if mutation == 'post-upload': self.artifact['created_at'] = '2026-10-09T00:10:01Z'
            if mutation == 'future-update': self.artifact['updated_at'] = '2026-10-11T00:00:00Z'
            with self.subTest(mutation=mutation), self.assertRaises(ValueError): self.resolve()
            self.artifact = original

    def test_missing_role_contract_or_native_report_is_not_zero_findings(self):
        for name in list(self.members):
            original = self.members.pop(name)
            self.write_archive()
            with self.subTest(member=name), self.assertRaises(ValueError): self.resolve()
            self.members[name] = original

    def test_metadata_cannot_claim_unexecuted_wrong_role_or_out_of_contract_operations(self):
        name = 'artifacts/security/schemathesis/anonymous.metadata.json'
        original = self.members[name]
        for key, value in (('lane', 'pr'), ('request_count', 0), ('operation_count', 0), ('network_errors', 1),
                           ('scanner_exit', 1), ('scanner_exit', False), ('seed', True), ('role', 'alpha-owner'),
                           ('schemathesis_version', '0'), ('contract_sha256', 'b' * 64),
                           ('operations', ['GET /invented-operation']), ('operations', ['GET /synthetic/read'] * 2),
                           ('ownerApproved', True)):
            self.members[name] = original
            self.mutate_member(name, lambda doc: doc.update({key: value}))
            with self.subTest(key=key, value=value), self.assertRaises(ValueError): self.resolve()
        self.members[name] = original

    def test_native_engine_completion_seed_clock_and_authentication_are_required(self):
        name = 'artifacts/security/schemathesis/anonymous.ndjson'
        original = self.members[name]
        initialize, finished = [json.loads(line) for line in original.splitlines()]
        mutations = [b'', encoded(initialize) + b'\n', encoded(finished) + b'\n', original + encoded(finished) + b'\n',
                     b'{"Initialize":{"seed":NaN}}\n', b'{"UnknownEvent":{}}\n', b'x' * (observation.MAX_LINE + 1)]
        wrong_seed = deepcopy(initialize); wrong_seed['Initialize']['seed'] = 26
        mutations.append(encoded(wrong_seed) + b'\n' + encoded(finished) + b'\n')
        for key, value in (('stop_reason', 'timeout'), ('timestamp', NOW.timestamp()), ('timestamp', True),
                           ('timestamp', float('inf')), ('timestamp', 1e100), ('running_time', 0), ('running_time', 601),
                           ('running_time', float('inf')), ('payload', {'reauth_broke': True})):
            changed = deepcopy(finished); changed['EngineFinished'][key] = value
            mutations.append(encoded(initialize) + b'\n' + encoded(changed) + b'\n')
        for index, value in enumerate(mutations):
            self.members[name] = value; self.write_archive()
            with self.subTest(index=index), self.assertRaises(ValueError): self.resolve()
        self.members[name] = original

    def test_zap_native_failure_high_summary_mismatch_or_wrong_scope_cannot_pass(self):
        name = 'artifacts/security/zap/alpha-owner.json'
        original = self.members[name]
        mutations = [lambda doc: doc['scanner'].update(exitCode=1), lambda doc: doc['target'].update(isolated=False),
                     lambda doc: doc['target'].update(externalNetworkAccess=True), lambda doc: doc['target'].update(origin='https://external.invalid'),
                     lambda doc: doc['inputs'].update(policySha256='c' * 64), lambda doc: doc['inputs'].update(addonListSha256='c' * 64),
                     lambda doc: doc['sanitization'].update(unsanitizedAllowed=True), lambda doc: doc.update(role='beta-owner'),
                     lambda doc: doc.update(ownerApproved=True), lambda doc: doc['summary']['uniqueAlertsByRisk'].update(Informational=0),
                     lambda doc: doc['summary'].update(blockingHighAlerts=False), lambda doc: doc['alerts'][0].update(risk='High'),
                     lambda doc: doc['alerts'][0].update(instanceCount=True), lambda doc: doc['alerts'][0].update(ruleId='private-value'),
                     lambda doc: doc['attribution'].update(status='unavailable')]
        for index, mutate in enumerate(mutations):
            self.members[name] = original; self.mutate_member(name, mutate)
            with self.subTest(index=index), self.assertRaises(ValueError): self.resolve()
        self.members[name] = original

    def test_zap_metadata_cannot_disable_sanitization_or_forge_counts_and_source_hashes(self):
        name = 'artifacts/security/zap/alpha-owner.metadata.json'
        original = self.members[name]
        for key, value in (('status', 'scanner-failed'), ('status', 'blocked-high'), ('highAlerts', 1), ('lowAlerts', True),
                           ('role', 'beta-owner'), ('forbiddenValueCount', 0), ('unsanitizedAllowed', True),
                           ('automationPlanSha256', 'c' * 64), ('policySha256', 'c' * 64), ('addonListSha256', 'forged'),
                           ('ownerApproved', True)):
            self.members[name] = original; self.mutate_member(name, lambda doc: doc.update({key: value}))
            with self.subTest(key=key, value=value), self.assertRaises(ValueError): self.resolve()
        self.members[name] = original

    def test_immutable_source_size_blob_and_plan_bytes_are_independently_checked(self):
        for mutation in ('size', 'blob', 'missing', 'plan'):
            def api(path):
                value = self.api(path)
                if '/contents/' in path:
                    if mutation == 'size': value['size'] += 1
                    if mutation == 'blob': value['sha'] = 'f' * 40
                    if mutation == 'missing': value['type'] = 'dir'
                    if mutation == 'plan' and observation.PLAN in path:
                        raw = b'changed immutable plan'
                        value.update(size=len(raw), content=base64.b64encode(raw).decode(),
                                     sha=hashlib.sha1(b'blob ' + str(len(raw)).encode() + b'\0' + raw).hexdigest())
                return value
            with self.subTest(mutation=mutation), self.assertRaises(ValueError): self.resolve(api)

    def test_live_run_job_or_artifact_change_after_byte_resolution_is_rejected(self):
        for mutation in ('run', 'job', 'artifact'):
            counts = Counter()
            def api(path):
                counts[path] += 1
                value = self.api(path)
                if counts[path] == 2:
                    if mutation == 'run' and path.endswith('/10'): value['run_attempt'] = 3
                    if mutation == 'job' and '/jobs?' in path: value['jobs'][-1]['id'] += 1
                    if mutation == 'artifact' and '/artifacts?' in path: value['artifacts'][0]['digest'] = 'sha256:' + 'f' * 64
                return value
            with self.subTest(mutation=mutation), self.assertRaises(ValueError): self.resolve(api)

    def test_zip_path_escape_duplicate_and_linked_members_are_rejected_without_extracting(self):
        for mutation in ('escape', 'duplicate', 'link'):
            self.write_archive()
            with zipfile.ZipFile(self.archive, 'a') as archive:
                if mutation == 'escape': archive.writestr('../private-input', b'canary')
                if mutation == 'duplicate':
                    import warnings
                    with warnings.catch_warnings():
                        warnings.simplefilter('ignore', UserWarning)
                        archive.writestr(next(iter(self.members)), b'{}')
                if mutation == 'link':
                    info = zipfile.ZipInfo('linked-input'); info.external_attr = (stat.S_IFLNK | 0o777) << 16
                    archive.writestr(info, b'/private-path')
            self.refresh_archive_identity()
            with self.subTest(mutation=mutation), self.assertRaises(ValueError): self.resolve()
            self.assertFalse((self.root / 'private-input').exists())

    def test_cli_requires_exclusive_output_and_sanitizes_failed_authority(self):
        output = self.root / 'new.json'
        args = ['observation', '--candidate-sha', SHA, '--run-id', '10', '--run-attempt', '2', '--artifact-id', '50',
                '--artifact', str(self.archive), '--output', str(output)]
        with patch('sys.argv', args), patch.object(provenance, 'LiveGitHub', return_value=self.api), patch.object(observation, 'datetime') as clock, patch('sys.stdout', new_callable=io.StringIO):
            clock.now.return_value = NOW
            clock.fromtimestamp.side_effect = datetime.fromtimestamp
            self.assertEqual(0, observation.main())
        original = output.read_bytes()
        with patch('sys.argv', args), patch('sys.stdout', new_callable=io.StringIO): self.assertEqual(1, observation.main())
        self.assertEqual(original, output.read_bytes())
        args[-1] = str(self.root / 'failure.json')
        def error(_path): raise ValueError('private-cookie-canary-do-not-publish')
        with patch('sys.argv', args), patch.object(provenance, 'LiveGitHub', return_value=error), patch('sys.stdout', new_callable=io.StringIO) as printed:
            self.assertEqual(1, observation.main())
        self.assertNotIn('private-cookie', printed.getvalue())
        self.assertNotIn('private-cookie', (self.root / 'failure.json').read_text())
        self.assertEqual('ERROR', json.loads((self.root / 'failure.json').read_bytes())['integrityStatus'])

    def test_cli_numeric_context_bounds_reject_noncanonical_values(self):
        for value in ('0', '-1', '01', '1.5', '1e9', 'true', '1' * 21, 'private-cookie'):
            with self.subTest(value=value), self.assertRaises(argparse.ArgumentTypeError): observation.bounded_id(value)
        self.assertEqual(1, observation.bounded_id('1'))

    def test_unknown_duplicate_or_oversized_cli_inputs_do_not_resolve_credentials_or_echo_values(self):
        for arguments in (['--owner-approved', 'private-cookie-canary'],
                          ['--run-id', '1', '--run-id', '2'], ['--artifact', 'private-cookie-canary' * 1000]):
            with patch('sys.argv', ['observation'] + arguments), patch.object(provenance, 'LiveGitHub') as client, patch('sys.stdout', new_callable=io.StringIO) as printed:
                self.assertEqual(1, observation.main())
            client.assert_not_called()
            self.assertNotIn('private-cookie', printed.getvalue())


if __name__ == '__main__':
    unittest.main()
