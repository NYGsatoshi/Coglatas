"""Synthetic API/archive controls only. No fixture establishes native artifact production."""
import base64
import copy
from datetime import datetime, timezone
import hashlib
import io
import json
import os
from pathlib import Path
import stat
import subprocess
import sys
import unittest
from unittest.mock import patch
import urllib.error
import zipfile

import d10_provenance as d10
import d10_transport as transport
import sec_arch_reconcile as shared

NOW = datetime(2026, 11, 6, 5, tzinfo=timezone.utc)
HEAD, BASE, MERGE, TREE = [value * 40 for value in 'abcd']
def encode(value): return json.dumps(value, separators=(',', ':')).encode()
def sha(value): return hashlib.sha256(value).hexdigest()


class SyntheticD10Controls(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.content_root = os.environ.get('D10_CONTENT_ROOT')
        if not cls.content_root: raise RuntimeError('D10_CONTENT_ROOT with both pinned modules is mandatory; no skip or substitute verifier')

    def setUp(self):
        self.source = {'schema': 'coglatas.playwright-source-discovery.v1', 'suite': 'angular-static', 'sourceSha': MERGE,
                       'tests': [{'id': str(i) * 64, 'status': 'skipped' if i == 2 else 'passed'} for i in [1, 2, 3]],
                       'rosterSha256': '', 'artifactTrust': 'EXTERNAL_VERIFICATION_REQUIRED', 'requiredCheckQualified': False,
                       'qualification': 'SOURCE_LIST_ONLY_NOT_EXECUTION_PROOF'}
        payload = {key: self.source[key] for key in ['sourceSha', 'suite', 'tests']}
        self.source['rosterSha256'] = sha(json.dumps(payload, sort_keys=True, separators=(',', ':')).encode())
        payload = {'sourceSha': MERGE, 'suite': 'angular-static', 'rosterSha256': self.source['rosterSha256'],
                   'anchorSha': '3865b09a2d3ea3cb82f450c6d19ec643ffbae01f', 'sourceManifestSha256': 'f' * 64,
                   'tests': [{'id': str(i) * 64, 'project': 'CHROMIUM_DESKTOP',
                              'classification': {1: 'RUN', 2: 'DECLARATION_SKIP', 3: 'CONDITIONAL_SKIP'}[i],
                              'bindingSha256': 'e' * 64} for i in [1, 2, 3]]}
        self.applicability = {'schema': 'coglatas.playwright-source-applicability.v1', **payload,
                              'applicabilitySha256': sha(encode(payload)), 'artifactTrust': 'EXTERNAL_VERIFICATION_REQUIRED',
                              'requiredCheckQualified': False, 'qualification': 'AST_SOURCE_APPLICABILITY_ONLY_NOT_EXECUTION'}
        self.native = {'schema': 'coglatas.playwright-case-execution.v1', 'suite': 'angular-static', 'sourceSha': MERGE,
                       'executionKind': 'GITHUB_ACTIONS', 'runId': '11', 'runAttempt': 1, 'producer': 'playwright-testcase-events-v1',
                       'status': 'passed', 'captureComplete': True, 'globalErrors': 0, 'captureErrors': [],
                       'tests': [{'id': str(i) * 64, 'listed': True, 'declaredExpectedStatus': 'skipped' if i == 2 else 'passed',
                                  'expectedStatus': 'passed' if i == 1 else 'skipped', 'outcome': 'expected' if i == 1 else 'skipped',
                                  'attempts': [{'retry': 0, 'status': 'passed' if i == 1 else 'skipped'}]} for i in [1, 2, 3]]}
        workflow_bytes = b'synthetic reviewed single-job workflow'
        self.policy = {'schema': 'coglatas.d10-policy.v1', 'repository': d10.REPOSITORY, 'repositoryId': d10.REPOSITORY_ID,
                       'prNumber': 99, 'reviewedHeadSha': HEAD, 'baseSha': BASE, 'testedMergeSha': MERGE, 'sourceTreeSha': TREE,
                       'workflowId': 21, 'workflowPath': d10.WORKFLOW, 'workflowSourceSha256': sha(workflow_bytes),
                       'runId': 11, 'attempt': 1, 'jobId': 31, 'artifactId': 41,
                       'sourceRosterSha256': self.source['rosterSha256'], 'applicabilitySha256': self.applicability['applicabilitySha256']}
        self.receipt = {'schema': 'coglatas.playwright-capture-receipt.v1', 'repository': d10.REPOSITORY,
                        'reviewedHeadSha': HEAD, 'testedMergeSha': MERGE, 'workflowId': 21, 'runId': 11,
                        'attempt': 1, 'jobId': 31, 'producerRole': d10.ROLE, 'reportDigests': {}}
        identity = {'id': d10.REPOSITORY_ID, 'full_name': d10.REPOSITORY}
        self.pr = {'state': 'open', 'head': {'sha': HEAD, 'ref': 'candidate', 'repo': identity},
                   'base': {'sha': BASE, 'ref': 'SecurityTestImplimation', 'repo': identity}, 'merge_commit_sha': MERGE}
        self.run = {'id': 11, 'run_attempt': 1, 'head_sha': HEAD, 'head_branch': 'candidate', 'event': 'pull_request',
                    'status': 'completed', 'conclusion': 'success', 'workflow_id': 21, 'path': d10.WORKFLOW,
                    'repository': identity, 'head_repository': identity, 'check_suite_id': 61,
                    'created_at': '2026-11-06T00:00:00Z', 'updated_at': '2026-11-06T04:00:00Z'}
        self.job = {'id': 31, 'name': d10.JOB, 'run_id': 11, 'run_attempt': 1, 'head_sha': HEAD,
                    'status': 'completed', 'conclusion': 'success', 'started_at': '2026-11-06T01:00:00Z',
                    'completed_at': '2026-11-06T03:00:00Z', 'check_run_url': 'https://api.github.com/repos/' + d10.REPOSITORY + '/check-runs/51'}
        self.check = {'id': 51, 'name': d10.JOB, 'app': {'id': 15368, 'slug': 'github-actions'},
                      'head_sha': HEAD, 'status': 'completed', 'conclusion': 'success', 'check_suite': {'id': 61}}
        self.workflow = {'id': 21, 'path': d10.WORKFLOW, 'state': 'active'}
        self.workflow_source = {'type': 'file', 'encoding': 'base64', 'size': len(workflow_bytes),
                                'content': base64.b64encode(workflow_bytes).decode(),
                                'sha': hashlib.sha1(b'blob ' + str(len(workflow_bytes)).encode() + b'\0' + workflow_bytes).hexdigest()}
        self.artifact = {'id': 41, 'name': d10.ARTIFACT, 'expired': False, 'size_in_bytes': 0, 'digest': '',
                         'created_at': '2026-11-06T02:00:00Z', 'updated_at': '2026-11-06T02:00:00Z',
                         'expires_at': '2026-11-07T00:00:00Z', 'workflow_run': {'id': 11, 'repository_id': d10.REPOSITORY_ID,
                         'head_repository_id': d10.REPOSITORY_ID, 'head_sha': HEAD, 'head_branch': 'candidate'}}
        self.jobs, self.artifacts = [self.job], [self.artifact]
        self.calls = []
        self.pack()

    def pack(self, raw_override=None, zip_mutation=None):
        raw = {'source.json': encode(self.source), 'applicability.json': encode(self.applicability), 'native.json': encode(self.native)}
        raw.update(raw_override or {})
        self.receipt['reportDigests'] = {name: sha(value) for name, value in raw.items()}
        raw['capture-receipt.json'] = encode(self.receipt)
        buffer = io.BytesIO()
        with zipfile.ZipFile(buffer, 'w', zipfile.ZIP_DEFLATED) as archive:
            for name, value in raw.items(): archive.writestr(name, value)
            if zip_mutation: zip_mutation(archive)
        self.data = buffer.getvalue()
        self.artifact.update(size_in_bytes=len(self.data), digest='sha256:' + sha(self.data))

    def api(self, path):
        self.calls.append(path)
        prefix = 'repos/' + d10.REPOSITORY
        if path == prefix + '/pulls/99': result = self.pr
        elif path == prefix + '/branches/SecurityTestImplimation': result = {'commit': {'sha': BASE}}
        elif path == prefix + '/git/commits/' + HEAD: result = {'sha': HEAD, 'tree': {'sha': TREE}, 'parents': []}
        elif path == prefix + '/git/commits/' + MERGE: result = {'sha': MERGE, 'tree': {'sha': getattr(self, 'merge_tree', TREE)}, 'parents': [{'sha': BASE}, {'sha': HEAD}]}
        elif path == prefix + '/actions/runs/11': result = self.run
        elif path == prefix + '/actions/workflows/21': result = self.workflow
        elif '/contents/' in path: result = self.workflow_source
        elif '/jobs?' in path: result = {'total_count': len(self.jobs), 'jobs': self.jobs}
        elif '/artifacts?' in path: result = {'total_count': len(self.artifacts), 'artifacts': self.artifacts}
        elif path == prefix + '/actions/artifacts/41': result = self.artifact
        elif path == prefix + '/check-runs/51': result = self.check
        else: raise RuntimeError('PRIVATE_API_CANARY')
        return copy.deepcopy(result)

    def verify(self, api=None, download=None, digest=None, content_root=None):
        raw = encode(self.policy)
        return d10.verify(api or self.api, download or (lambda identity: self.data), raw, digest or sha(raw), NOW,
                          content_root or self.content_root)

    def rejected(self, result):
        self.assertEqual('REJECTED', result['decision'])
        self.assertFalse(result['ARTIFACT_PROVENANCE_VERIFIED'])
        self.assertFalse(result['REQUIRED_GATE_QUALIFIED'])
        self.assertNotIn('PRIVATE_', json.dumps(result))

    def test_synthetic_complete_path_preserves_merge_head_and_all_independent_states(self):
        value = self.verify()
        self.assertEqual('VERIFIED', value['decision'], value)
        self.assertTrue(all(value['states'].values()))
        self.assertNotEqual(value['reviewedHeadSha'], value['testedMergeSha'])
        self.assertEqual(41, value['artifactId'])
        self.assertEqual(1, value['counts']['conditionalSkips'])
        self.assertEqual(1, value['counts']['declarationSkips'])
        self.assertEqual(1, value['counts']['executedCases'])
        self.assertFalse(value['REQUIRED_GATE_QUALIFIED'])
        self.assertEqual(self.verify(), value)

    def test_repository_pr_and_reviewed_source_identities_reject(self):
        for mode in ['repository', 'fork', 'head', 'base', 'merge', 'tree']:
            self.setUp()
            if mode == 'repository': self.run['repository'] = {'id': 7, 'full_name': 'other/repo'}
            if mode == 'fork': self.pr['head']['repo'] = {'id': 7, 'full_name': 'fork/repo'}
            if mode == 'head': self.pr['head']['sha'] = 'e' * 40
            if mode == 'base': self.pr['base']['ref'] = 'main'
            if mode == 'merge': self.pr['merge_commit_sha'] = 'e' * 40
            if mode == 'tree': self.merge_tree = 'e' * 40
            self.rejected(self.verify())
            if hasattr(self, 'merge_tree'): del self.merge_tree

    def test_workflow_id_path_source_and_enforcement_reject(self):
        for field, value in [('id', 22), ('path', '.github/workflows/spoof.yml'), ('state', 'disabled')]:
            self.setUp(); self.workflow[field] = value; self.rejected(self.verify())
        self.setUp(); self.workflow_source['content'] = base64.b64encode(b'PRIVATE_SPOOF').decode(); self.rejected(self.verify())

    def test_run_attempt_job_identity_role_completion_and_application_reject(self):
        mutations = [('run', 'id', 12), ('run', 'run_attempt', 2), ('run', 'head_sha', MERGE),
                     ('run', 'conclusion', 'cancelled'), ('job', 'id', 32), ('job', 'name', 'PRIVATE_SPOOF'),
                     ('job', 'run_attempt', 2), ('job', 'run_id', 12), ('job', 'conclusion', 'skipped'),
                     ('job', 'head_sha', MERGE), ('check', 'app', {'id': 1, 'slug': 'spoof'})]
        for item, field, value in mutations:
            self.setUp(); getattr(self, item)[field] = value; self.rejected(self.verify())
        self.setUp(); self.jobs = []; self.rejected(self.verify())
        self.setUp(); self.jobs.append(dict(self.job, id=32)); self.rejected(self.verify())

    def test_artifact_missing_expired_other_run_name_spoof_and_digest_reject(self):
        for mode in ['missing', 'expired', 'run', 'id', 'name', 'duplicate', 'digest', 'time', 'size']:
            self.setUp()
            if mode == 'missing': self.artifacts.clear()
            if mode == 'expired': self.artifact['expired'] = True
            if mode == 'run': self.artifact['workflow_run']['id'] = 12
            if mode == 'id': self.artifact['id'] = 42
            if mode == 'name': self.artifact['name'] = 'PRIVATE_SPOOF'
            if mode == 'duplicate': self.artifacts.append(dict(self.artifact, id=42))
            if mode == 'digest': self.artifact['digest'] = 'sha256:' + '0' * 64
            if mode == 'time': self.artifact['created_at'] = '2026-11-06T04:00:00Z'
            if mode == 'size': self.artifact['size_in_bytes'] = transport.MAX_ZIP + 1
            self.rejected(self.verify())

    def test_archive_corruption_oversize_unknown_missing_duplicate_and_symlink_reject(self):
        for mode in ['corrupt', 'oversize', 'unknown', 'missing', 'duplicate', 'symlink', 'trailing', 'bomb']:
            self.setUp()
            if mode == 'corrupt': self.data = b'PRIVATE_BROKEN_ZIP'
            if mode == 'oversize': self.data = b'x' * (transport.MAX_ZIP + 1)
            if mode == 'unknown': self.pack(zip_mutation=lambda archive: archive.writestr('PRIVATE_UNKNOWN', b'x'))
            if mode == 'duplicate': self.pack(zip_mutation=lambda archive: archive.writestr('source.json', b'{}'))
            if mode == 'symlink':
                def linked(archive):
                    info = zipfile.ZipInfo('PRIVATE_LINK'); info.external_attr = (stat.S_IFLNK | 0o777) << 16; archive.writestr(info, b'target')
                self.pack(zip_mutation=linked)
            if mode == 'missing':
                buffer = io.BytesIO()
                with zipfile.ZipFile(buffer, 'w') as archive: archive.writestr('native.json', b'{}')
                self.data = buffer.getvalue()
            if mode == 'trailing': self.data += b'PRIVATE_TRAILING'
            if mode == 'bomb': self.pack({'native.json': b'0' * (1024 * 1024)})
            if mode != 'oversize': self.artifact.update(size_in_bytes=len(self.data), digest='sha256:' + sha(self.data))
            self.rejected(self.verify())

    def test_self_declared_receipt_and_unknown_claims_cannot_authenticate(self):
        for field, value in [('reviewedHeadSha', MERGE), ('testedMergeSha', HEAD), ('jobId', 32), ('runId', 12),
                             ('attempt', True), ('producerRole', 'PRIVATE_OTHER'), ('PROVENANCE_VERIFIED', True)]:
            self.setUp(); self.receipt[field] = value; self.pack(); self.rejected(self.verify())
        self.setUp(); self.native['PRIVATE_BODY'] = 'PRIVATE_CANARY'; self.pack(); self.rejected(self.verify())
        self.setUp(); self.native['sourceSha'] = HEAD; self.pack(); self.rejected(self.verify())

    def test_wrong_merge_parents_and_missing_server_producer_identity_reject(self):
        def wrong_parent(path):
            value = self.api(path)
            if path.endswith('/git/commits/' + MERGE): value['parents'][1]['sha'] = 'e' * 40
            return value
        value = self.verify(api=wrong_parent)
        self.rejected(value); self.assertEqual('TESTED_MERGE_MISMATCH', value['reason'])
        self.job.pop('check_run_url'); self.rejected(self.verify())

    def test_incomplete_ids_and_unsuccessful_execution_never_reconcile(self):
        for mode in ['missing', 'extra', 'duplicate', 'failed', 'retry', 'interrupted', 'skip', 'empty', 'incomplete']:
            self.setUp()
            if mode == 'missing': self.native['tests'].pop()
            if mode == 'extra': self.native['tests'][0]['id'] = '9' * 64
            if mode == 'duplicate': self.native['tests'].append(copy.deepcopy(self.native['tests'][0]))
            if mode == 'failed': self.native['tests'][0]['attempts'][0]['status'] = 'failed'
            if mode == 'retry': self.native['tests'][0]['attempts'].append({'retry': 1, 'status': 'passed'})
            if mode == 'interrupted': self.native['status'] = 'interrupted'
            if mode == 'skip': self.native['tests'][0].update(expectedStatus='skipped', outcome='skipped', attempts=[{'retry': 0, 'status': 'skipped'}])
            if mode == 'empty': self.native['tests'][0]['attempts'] = []
            if mode == 'incomplete': self.native['captureComplete'] = False
            self.pack(); self.rejected(self.verify())

    def test_unavailable_partial_and_changed_api_response_fail_closed(self):
        def unavailable(path): raise RuntimeError('PRIVATE_TOKEN_CANARY')
        self.rejected(self.verify(api=unavailable))
        def incomplete(path):
            if '/jobs?' in path: return {'total_count': 2, 'jobs': [copy.deepcopy(self.job)]}
            return self.api(path)
        self.rejected(self.verify(api=incomplete))
        def changed(identity): self.run['run_attempt'] = 2; return self.data
        value = self.verify(download=changed)
        self.rejected(value); self.assertEqual('AUTHORITY_CHANGED', value['reason']); self.assertFalse(any(value['states'].values()))

    def test_policy_unknown_fields_boolean_ids_wrong_digest_and_missing_dependency_reject(self):
        for field, value in [('unexpected', True), ('attempt', 2), ('runId', True), ('repository', 'other/repo')]:
            self.setUp(); self.policy[field] = value; self.rejected(self.verify())
        self.setUp(); self.rejected(self.verify(digest='0' * 64))
        self.rejected(self.verify(content_root='/PRIVATE_MISSING'))

    def test_malformed_reports_and_duplicate_json_refuse_without_private_diagnostics(self):
        for raw in [b'', b'{', b'PRIVATE_CANARY', b'{"sourceSha":1,"sourceSha":1}', b'{"x":NaN}']:
            self.setUp(); self.pack({'native.json': raw}); self.rejected(self.verify())

    def test_internal_archive_corruption_and_exact_name_symlink_reject(self):
        raw = bytearray(self.data)
        name_size = int.from_bytes(raw[26:28], 'little')
        extra_size = int.from_bytes(raw[28:30], 'little')
        raw[30 + name_size + extra_size + 8] ^= 1
        self.data = bytes(raw)
        self.artifact.update(size_in_bytes=len(self.data), digest='sha256:' + sha(self.data))
        self.rejected(self.verify())
        self.setUp()
        with zipfile.ZipFile(io.BytesIO(self.data)) as archive: members = {name: archive.read(name) for name in archive.namelist()}
        output = io.BytesIO()
        with zipfile.ZipFile(output, 'w') as archive:
            for name, data in members.items():
                info = zipfile.ZipInfo(name)
                if name == 'native.json': info.external_attr = (stat.S_IFLNK | 0o777) << 16
                archive.writestr(info, data)
        self.data = output.getvalue()
        self.artifact.update(size_in_bytes=len(self.data), digest='sha256:' + sha(self.data))
        self.rejected(self.verify())

    def test_actual_cli_invalid_arguments_do_not_echo_private_input(self):
        result = subprocess.run([sys.executable, '-B', str(Path(d10.__file__)), 'PRIVATE_TOKEN_CANARY'], capture_output=True)
        self.assertEqual(1, result.returncode)
        self.assertNotIn(b'PRIVATE_', result.stdout + result.stderr)
        self.assertFalse(json.loads(result.stdout)['ARTIFACT_PROVENANCE_VERIFIED'])

    def test_availability_is_not_provenance_even_when_name_matches(self):
        report = d10.audit_run(self.api, 99, HEAD, 11)
        self.assertEqual('CANDIDATE_PRESENT_NOT_AUTHENTICATED', report['availability'])
        self.assertFalse(any(report['states'].values()))
        self.artifacts.clear()
        self.assertEqual('NATIVE_CAPTURE_ARTIFACT_ABSENT', d10.audit_run(self.api, 99, HEAD, 11)['availability'])


class TransportAndSharedProfileControls(unittest.TestCase):
    def test_storage_redirects_and_non_numeric_ids_fail_closed(self):
        for value in ['http://a.blob.core.windows.net/a', 'https://evil.example/a', 'https://u:p@a.blob.core.windows.net/a',
                      'https://a.blob.core.windows.net:444/a', 'https://a.blob.core.windows.net/a#private', 'PRIVATE_TOKEN']:
            with self.assertRaises(transport.DownloadRefused): transport.storage_location(value)
        with self.assertRaises(transport.DownloadRefused): transport.ArtifactGitHub('PRIVATE_TOKEN').download(True)

    def test_storage_request_never_receives_api_token_and_second_redirect_is_refused(self):
        calls = []
        class Response(io.BytesIO):
            status = 200
        class Opener:
            def open(self, request, timeout):
                calls.append(request)
                if len(calls) == 1:
                    raise urllib.error.HTTPError(request.full_url, 302, 'redirect', {'Location': 'https://a.blob.core.windows.net/artifact?sig=PRIVATE_SIGNED'}, None)
                return Response(b'bytes')
        with patch.object(transport.urllib.request, 'build_opener', return_value=Opener()):
            self.assertEqual(b'bytes', transport.ArtifactGitHub('PRIVATE_TOKEN').download(41))
        self.assertIn('Authorization', calls[0].headers)
        self.assertNotIn('Authorization', calls[1].headers)
        self.assertIsNone(transport.StopRedirect().redirect_request(None, None, 302, '', {}, 'https://evil.example/'))

    def test_shared_archive_profile_cannot_loosen_legacy_limits(self):
        for options in [{'max_archive': shared.MAX_ARCHIVE + 1}, {'max_expanded': shared.MAX_EXPANDED + 1},
                        {'max_members': 257}, {'max_members': True}]:
            with self.assertRaises(shared.ReconciliationError):
                with shared.verified_zip(Path('unused'), '0' * 64, **options): pass

if __name__ == '__main__': unittest.main()
