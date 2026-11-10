"""Isolated same-case positive and mutation tests for Architecture API witness."""
from __future__ import annotations

import copy
import json
from pathlib import Path
import tempfile
import unittest

import architecture_github_metadata as witness

BASE = '47dffab4ba29b451c56881c698a932ffeb97bc57'
HEAD = '88a7e54a021062ae0f7444ac8e47b1fc26cc3036'
MERGE = '1151920bea5c8280f86cad0336fbf111a0ba94f9'
TREE = '34876a9c6a6c9be1b661678b2b45765e417784f8'
DIGEST = '9d6c564e556d523fc4dbd9876f7e3da01aae8dfbd2fc38e35f0d9f9d943b9ac1'
RUN = 42846167969
JOB = 129934084581
ARTIFACT = 13777452374
PR = 1886
BRANCH = 'ci/architecture-archive-native-composition-20261111'
URL = 'https://api.github.com/repos/NYGsatoshi/Coglatas/check-runs/' + str(JOB)


def fixture() -> dict:
    """Create realistic, minimal read-only GitHub API projections."""
    repo = {'id': witness.REPOSITORY_ID}
    return {
        'pr': {'number': PR, 'state': 'open', 'merged': False,
               'head': {'sha': HEAD, 'ref': BRANCH, 'repo': repo},
               'base': {'sha': BASE, 'ref': witness.BASE_REF, 'repo': repo},
               'merge_commit_sha': MERGE},
        'base_branch': {'commit': {'sha': BASE}, 'protected': False},
        'head_commit': {'sha': HEAD, 'tree': {'sha': TREE}},
        'merge_commit': {'sha': MERGE, 'parents': [{'sha': BASE}, {'sha': HEAD}],
                         'tree': {'sha': TREE}},
        'run': {'id': RUN, 'head_sha': HEAD, 'head_branch': BRANCH,
                'event': 'pull_request', 'path': witness.WORKFLOW,
                'workflow_id': witness.WORKFLOW_ID, 'run_attempt': 1,
                'status': 'completed', 'conclusion': 'success',
                'check_suite_id': 116036818242, 'repository': repo,
                'head_repository': repo,
                'pull_requests': [{'number': PR, 'head': {'sha': HEAD},
                                   'base': {'sha': BASE, 'ref': witness.BASE_REF}}],
                'referenced_workflows': [],
                'created_at': '2026-11-10T15:36:48Z',
                'updated_at': '2026-11-10T15:38:17Z'},
        'jobs': [{'id': JOB, 'name': witness.JOB_NAME, 'run_id': RUN,
                  'run_attempt': 1, 'head_sha': HEAD, 'status': 'completed',
                  'conclusion': 'success', 'check_run_url': URL,
                  'started_at': '2026-11-10T15:36:51Z',
                  'completed_at': '2026-11-10T15:38:16Z'}],
        'check_run': {'id': JOB, 'name': witness.JOB_NAME, 'head_sha': HEAD,
                      'status': 'completed', 'conclusion': 'success',
                      'app': {'id': witness.ACTION_APP_ID, 'slug': 'github-actions'},
                      'check_suite': {'id': 116036818242}},
        'artifacts': [{'id': ARTIFACT,
                       'name': f'architecture-advisory-{RUN}-1',
                       'size_in_bytes': 1136, 'digest': 'sha256:' + DIGEST,
                       'expired': False,
                       'workflow_run': {'id': RUN, 'repository_id': witness.REPOSITORY_ID,
                                        'head_repository_id': witness.REPOSITORY_ID,
                                        'head_branch': BRANCH, 'head_sha': HEAD},
                       'created_at': '2026-11-10T15:38:13Z',
                       'updated_at': '2026-11-10T15:38:13Z'}],
    }


def verify(record: dict, **changes: object) -> dict:
    """Call the checker with externally fixed expected identities."""
    kwargs = {'pr_number': PR, 'head_sha': HEAD, 'base_sha': BASE, 'merge_sha': MERGE,
              'run_id': RUN, 'job_id': JOB, 'artifact_id': ARTIFACT, 'attempt': 1,
              'artifact_sha256': DIGEST, 'artifact_size': 1136}
    kwargs.update(changes)
    return witness.verify(record, **kwargs)


class GithubMetadataTests(unittest.TestCase):
    """Reject inconsistent identities without claiming API or producer trust."""

    def test_fixed_positive_is_only_metadata_content_match(self) -> None:
        """Positive consistency is not independently authenticated evidence."""
        got = verify(fixture())
        self.assertEqual(got['status'], 'SUPPLIED_GITHUB_METADATA_CONTENT_MATCH')
        self.assertEqual(got['artifactIdClaim'], ARTIFACT)
        self.assertFalse(got['baseProtectedObserved'])
        for field in ('gitHubResponseOriginAuthenticated', 'testedMergeExecutionAuthenticated',
                      'artifactBytesVerified', 'producerAuthenticated',
                      'artifactProvenanceVerified', 'requiredCheckQualified'):
            self.assertIs(got[field], False)
        self.assertNotIn('token', str(got).lower())

    def test_wrong_parent_order_and_tree_refuse(self) -> None:
        """Today's merge ref cannot replace two exact Git-parent/tree identities."""
        for field, value in [('parents', [{'sha': HEAD}, {'sha': BASE}]),
                             ('tree', {'sha': 'f' * 40})]:
            record = fixture()
            record['merge_commit'][field] = value
            with self.subTest(field=field), self.assertRaises(witness.MetadataRefused):
                verify(record)

    def test_foreign_head_and_base_refuse(self) -> None:
        """A mismatched PR, repository or current base is unqualified."""
        for key, value in [('head', {'sha': 'f' * 40, 'ref': BRANCH,
                                    'repo': {'id': witness.REPOSITORY_ID}}),
                           ('base', {'sha': BASE, 'ref': 'main',
                                     'repo': {'id': witness.REPOSITORY_ID}})]:
            record = fixture()
            record['pr'][key] = value
            with self.subTest(key=key), self.assertRaises(witness.MetadataRefused):
                verify(record)
        record = fixture()
        record['base_branch']['commit']['sha'] = 'b' * 40
        with self.assertRaises(witness.MetadataRefused):
            verify(record)

    def test_pr_run_association_missing_or_wrong_refuses(self) -> None:
        """An unrelated or missing PR association cannot borrow a green run."""
        for other in ([], [{'number': 22, 'head': {'sha': HEAD},
                            'base': {'sha': BASE, 'ref': witness.BASE_REF}}]):
            record = fixture()
            record['run']['pull_requests'] = other
            with self.subTest(n=len(other)), self.assertRaises(witness.MetadataRefused):
                verify(record)

    def test_run_context_and_rerun_are_rejected(self) -> None:
        """Only first-attempt successful PR job under the pinned workflow qualifies."""
        for key, value in [('run_attempt', 2), ('path', '.github/workflows/ci.yml'),
                           ('status', 'in_progress'), ('conclusion', 'failure')]:
            record = fixture()
            record['run'][key] = value
            with self.subTest(key=key), self.assertRaises(witness.MetadataRefused):
                verify(record)
        record = fixture()
        record['run']['referenced_workflows'] = [{'sha': MERGE}]
        with self.assertRaises(witness.MetadataRefused):
            verify(record)

    def test_fake_job_and_check_app_refuse(self) -> None:
        """Require exactly the original successful job and Actions check app."""
        record = fixture()
        record['jobs'].append(copy.deepcopy(record['jobs'][0]))
        with self.assertRaises(witness.MetadataRefused):
            verify(record)
        for where, key, value in [('job', 'head_sha', 'f' * 40),
                                  ('job', 'check_run_url', 'https://example.com'),
                                  ('check', 'id', 7), ('check', 'name', 'other')]:
            record = fixture()
            d = record['jobs'][0] if where == 'job' else record['check_run']
            d[key] = value
            with self.subTest(where=where, key=key), self.assertRaises(witness.MetadataRefused):
                verify(record)
        record = fixture()
        record['check_run']['app']['slug'] = 'untrusted'
        with self.assertRaises(witness.MetadataRefused):
            verify(record)

    def test_artifact_foreign_digests_and_names_refuse(self) -> None:
        """Artifact metadata must match the externally supplied digest and ID."""
        for key, value in [('id', 1), ('name', 'other'),
                           ('digest', 'sha256:' + 'f' * 64),
                           ('size_in_bytes', 1), ('expired', True)]:
            record = fixture()
            record['artifacts'][0][key] = value
            with self.subTest(key=key), self.assertRaises(witness.MetadataRefused):
                verify(record)
        record = fixture()
        record['artifacts'][0]['workflow_run']['head_sha'] = 'f' * 40
        with self.assertRaises(witness.MetadataRefused):
            verify(record)

    def test_artifact_count_and_reordered_time_refuse(self) -> None:
        """Duplicate/ambiguous artifacts and impossible job times cannot qualify."""
        record = fixture()
        record['artifacts'].append(copy.deepcopy(record['artifacts'][0]))
        with self.assertRaises(witness.MetadataRefused):
            verify(record)
        record = fixture()
        record['artifacts'][0]['updated_at'] = '2026-11-10T15:40:00Z'
        with self.assertRaises(witness.MetadataRefused):
            verify(record)

    def test_boolean_identifiers_or_malformed_operator_pins_refuse(self) -> None:
        """Reject Boolean IDs, stale external digest, invalid source SHA."""
        for key, value in [('pr_number', True), ('attempt', True),
                           ('artifact_sha256', 'a'), ('head_sha', HEAD[:-1]),
                           ('artifact_size', False)]:
            with self.subTest(key=key), self.assertRaises(witness.MetadataRefused):
                verify(fixture(), **{key: value})

    def test_duplicate_keys_and_malformed_snapshot_refuse(self) -> None:
        """Read only bounded JSON without duplicate keys or symlinked files."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = root / 'api.json'
            path.write_text(json.dumps(fixture()), encoding='utf-8')
            self.assertEqual(witness.read_snapshot(path), fixture())
            for raw in (b'{"pr":{},"pr":{}}', b'{"x":NaN}', b'\xff', b'{}',
                        b'x' * (witness.MAX_METADATA + 1)):
                path.write_bytes(raw)
                with self.subTest(length=len(raw)), self.assertRaises(witness.MetadataRefused):
                    witness.read_snapshot(path)
            path.write_text(json.dumps(fixture()), encoding='utf-8')
            link = root / 'api-link.json'
            try:
                link.symlink_to(path)
            except (OSError, NotImplementedError):
                self.skipTest('This filesystem does not permit symlinks')
            with self.assertRaises(witness.MetadataRefused):
                witness.read_snapshot(link)


if __name__ == '__main__':
    unittest.main()
