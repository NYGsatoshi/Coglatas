#!/usr/bin/env python3
"""Offline negative controls for live-API Qodana ZIP/attempt correlation."""
from __future__ import annotations

import copy
import hashlib
import io
import json
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'scripts' / 'quality'))
import verify_qodana_artifact_provenance as verifier

RUN_ID = 40649034671
SHA = 'cc83b0b6f2607e4705885ac6c1a0fed995dcfc0b'
START = '2026-10-28T07:21:00Z'
END = '2026-10-28T07:48:00Z'
CREATED = '2026-10-28T07:40:00Z'
CID = 12800071093
DID = 12799926762


def sarif(guid: str, result: str = 'example') -> dict:
    return {
        '$schema': 'https://schemastore.azurewebsites.net/schemas/json/sarif-2.1.0-rtm.6.json',
        'version': '2.1.0',
        'runs': [{
            'automationDetails': {
                'id': 'QDNETC/qodana/2026-10-28', 'guid': guid,
                'properties': {'jobUrl': f'https://github.com/NYGsatoshi/Coglatas/actions/runs/{RUN_ID}'},
            },
            'versionControlProvenance': [{
                'repositoryUri': 'https://github.com/NYGsatoshi/Coglatas.git',
                'revisionId': SHA, 'branch': 'main',
            }],
            'invocations': [{'executionSuccessful': True, 'exitCode': 0}],
            'tool': {'driver': {'name': 'QDNETC', 'version': '2026.2', 'rules': [{'id': 'R1'}]}},
            'results': [{'ruleId': 'R1', 'message': {'text': result}}],
            'artifacts': [{'location': {'uri': 'src/Example.cs'}}],
        }],
    }


def archived(document: dict, *, extra: dict[str, bytes] | None = None) -> bytes:
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, 'w', compression=zipfile.ZIP_DEFLATED) as z:
        z.writestr('qodana.sarif.json', json.dumps(document))
        for name, data in (extra or {}).items():
            z.writestr(name, data)
    return stream.getvalue()


def job(job_id: int, name: str) -> dict:
    return {
        'id': job_id, 'name': name, 'run_id': RUN_ID, 'run_attempt': 1,
        'head_sha': SHA, 'workflow_name': 'Main CI', 'status': 'completed',
        'conclusion': 'success', 'started_at': START, 'completed_at': END,
        'steps': [{'name': 'Upload Qodana inspection inventory', 'conclusion': 'success'}],
    }


def artifact(artifact_id: int, name: str, contents: bytes) -> dict:
    return {
        'id': artifact_id, 'name': name, 'expired': False,
        'size_in_bytes': len(contents), 'digest': 'sha256:' + hashlib.sha256(contents).hexdigest(),
        'created_at': CREATED, 'workflow_run': {
            'id': RUN_ID, 'repository_id': verifier.REPO_ID,
            'head_repository_id': verifier.REPO_ID, 'head_branch': 'main', 'head_sha': SHA,
        },
    }


class FakeAPI:
    def __init__(self, responses: dict[str, dict]):
        self.responses = responses
        self.calls: list[str] = []

    def __call__(self, path: str) -> dict:
        self.calls.append(path)
        return copy.deepcopy(self.responses[path])


class QodanaArtifactCorrelationTests(unittest.TestCase):
    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.left = self.root / 'community.zip'
        self.right = self.root / 'cloud.zip'
        self.left.write_bytes(archived(sarif('11111111-1111-4111-8111-111111111111')))
        self.right.write_bytes(archived(sarif('22222222-2222-4222-8222-222222222222')))
        self.artifacts = [
            artifact(CID, 'qodana-full-inventory', self.left.read_bytes()),
            artifact(DID, 'qodana-cloud-full-inventory', self.right.read_bytes()),
        ]
        self.jobs = [job(123456, verifier.EXPECTED_NAMES['community'][1]),
                     job(123457, verifier.EXPECTED_NAMES['cloud'][1])]
        self.run = {
            'id': RUN_ID, 'run_attempt': 1, 'workflow_id': verifier.WORKFLOW_ID,
            'head_sha': SHA, 'head_branch': 'main', 'event': 'push',
            'status': 'completed', 'conclusion': 'failure',
            'repository': {'id': verifier.REPO_ID},
        }

    def api(self) -> FakeAPI:
        return FakeAPI({
            f'/actions/runs/{RUN_ID}': self.run,
            f'/actions/runs/{RUN_ID}/jobs?per_page=100&page=1': {
                'total_count': len(self.jobs), 'jobs': self.jobs,
            },
            f'/actions/runs/{RUN_ID}/artifacts?per_page=100&page=1': {
                'total_count': len(self.artifacts), 'artifacts': self.artifacts,
            },
        })

    def verify(self) -> dict:
        return verifier.verify(
            RUN_ID, 1, SHA, 'main', {'community': CID, 'cloud': DID},
            {'community': self.left, 'cloud': self.right}, get=self.api(),
        )

    def assert_reject(self) -> None:
        with self.assertRaises(verifier.ParityError):
            self.verify()

    def test_valid_metadata_and_sarif_only_correlate_attempt(self):
        result = self.verify()
        self.assertEqual(result['runAttempt'], 1)
        self.assertTrue(result['artifactDigestVerified'])
        self.assertTrue(result['runAttemptVerified'])
        self.assertEqual(result['status'], 'ATTEMPT_CORRELATED_NOT_PUBLISHER_ATTESTED')
        self.assertFalse(result['producerCryptographicallyAttested'])
        self.assertFalse(result['cloudReplayVerified'])
        self.assertFalse(result['qualifiedForGateReuse'])
        self.assertEqual(result['producerJobIds']['community'], 123456)

    def test_run_provenance_mutations_fail(self):
        cases = [
            ('id', 1), ('run_attempt', 2), ('workflow_id', 1),
            ('head_sha', 'd' * 40), ('head_branch', 'SecurityTestImplimation'),
            ('event', 'pull_request'), ('status', 'in_progress'),
        ]
        for key, val in cases:
            with self.subTest(key=key):
                old = self.run[key]
                self.run[key] = val
                self.assert_reject()
                self.run[key] = old
        self.run['repository']['id'] = 22
        self.assert_reject()

    def test_job_attempt_and_status_fail_closed(self):
        for key, bad in [('run_attempt', 2), ('run_id', 1), ('head_sha', '0'*40),
                         ('status', 'queued'), ('conclusion', 'failure'),
                         ('workflow_name', 'Other workflow')]:
            with self.subTest(field=key):
                old = self.jobs[0][key]
                self.jobs[0][key] = bad
                self.assert_reject()
                self.jobs[0][key] = old
        self.jobs[1]['steps'][0]['conclusion'] = 'skipped'
        self.assert_reject()

    def test_artifact_binding_and_digest_reject_mutations(self):
        for key, bad in [('name', 'untrusted'), ('expired', True),
                         ('digest', 'sha256:' + '0'*64), ('size_in_bytes', 4)]:
            with self.subTest(field=key):
                old = self.artifacts[0][key]
                self.artifacts[0][key] = bad
                self.assert_reject()
                self.artifacts[0][key] = old
        self.artifacts[1]['workflow_run']['head_sha'] = 'd'*40
        self.assert_reject()

    def test_artifact_stale_attempt_time_window_rejected(self):
        self.artifacts[0]['created_at'] = '2026-10-27T07:40:00Z'
        self.assert_reject()
        self.artifacts[0]['created_at'] = '2026-10-28T10:00:00Z'
        self.assert_reject()

    def test_corrupted_archive_and_bad_entries_rejected(self):
        self.left.write_bytes(self.left.read_bytes() + b'unexpected')
        self.assert_reject()
        for extra in ({'../escape': b'unsafe'}, {'evil\\path': b'unsafe'}):
            with self.subTest(extra=extra):
                doc = sarif('11111111-1111-4111-8111-111111111111')
                blob = archived(doc, extra=extra)
                self.left.write_bytes(blob)
                self.artifacts[0] = artifact(CID, 'qodana-full-inventory', blob)
                self.assert_reject()

    def test_mismatched_sarif_findings_are_rejected(self):
        blob = archived(sarif('22222222-2222-4222-8222-222222222222', 'changed'))
        self.right.write_bytes(blob)
        self.artifacts[1] = artifact(DID, 'qodana-cloud-full-inventory', blob)
        self.assert_reject()

    def test_missing_or_identical_archive_fails(self):
        self.right.unlink()
        self.assert_reject()
        self.right.write_bytes(self.left.read_bytes())
        self.assert_reject()

    def test_duplicate_or_missing_api_entries_rejected(self):
        self.artifacts.append(copy.deepcopy(self.artifacts[0]))
        self.assert_reject()
        self.artifacts.pop()
        self.artifacts.pop()
        self.assert_reject()

    def test_attempt_and_id_inputs_must_be_exact(self):
        for attempt, sha, branch in [(0, SHA, 'main'), (2, SHA, 'main'),
                                     (1, 'x'*40, 'main'), (1, SHA, 'feature')]:
            with self.subTest(attempt=attempt, branch=branch):
                with self.assertRaises(verifier.ParityError):
                    verifier.verify(RUN_ID, attempt, sha, branch,
                                    {'community': CID, 'cloud': DID},
                                    {'community': self.left, 'cloud': self.right}, get=self.api())

    def test_api_failure_and_bad_pagination_fail(self):
        with self.assertRaises(verifier.ParityError):
            verifier.verify(RUN_ID, 1, SHA, 'main',
                            {'community': CID, 'cloud': DID},
                            {'community': self.left, 'cloud': self.right}, get=lambda _: {})
        self.jobs.append(copy.deepcopy(self.jobs[0]))
        self.assert_reject()

    def test_missing_token_fails_before_any_http(self):
        with patch.dict('os.environ', {'GITHUB_TOKEN': ''}):
            with self.assertRaises(verifier.ParityError):
                verifier.live_get(f'/actions/runs/{RUN_ID}')
        with self.assertRaises(verifier.ParityError):
            verifier.live_get('https://evil.invalid/anything')


if __name__ == '__main__':
    unittest.main()
