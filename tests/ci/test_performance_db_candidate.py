from __future__ import annotations

import copy
import hashlib
import tempfile
import textwrap
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = (ROOT / '.github/workflows/performance-db-candidate.yml').read_text(encoding='utf-8')
HEAD = 'a' * 40
BASE = 'b' * 40
REPOSITORY = 'NYGsatoshi/Coglatas'
REF = 'refs/heads/ci/issue-606-db-regression-gate'


def workflow_functions(marker):
    """Execute the actual workflow's pure validators; never its network entrypoint."""
    source = textwrap.dedent(WORKFLOW.split(f'# {marker}-start\n', 1)[1].split(f'# {marker}-end', 1)[0])
    namespace = {'__name__': 'candidate_contract_tests'}
    exec(compile(source, 'performance-db-candidate.yml', 'exec'), namespace)
    return namespace


GUARD = workflow_functions('candidate-guard')['validate_candidate']
BINDING = workflow_functions('candidate-binding')


def environment():
    return {'GITHUB_EVENT_NAME': 'workflow_dispatch', 'GITHUB_REPOSITORY': REPOSITORY,
            'GITHUB_REF': REF, 'GITHUB_SHA': HEAD, 'EXPECTED_SHA': HEAD,
            'GITHUB_WORKFLOW_REF': REPOSITORY + '/.github/workflows/performance-db-candidate.yml@' + REF,
            'GITHUB_RUN_ID': '42', 'GITHUB_RUN_ATTEMPT': '1',
            'GUARD_RESULT': 'success', 'COLLECT_RESULT': 'success'}


def pull_request():
    return {'number': 1046, 'state': 'open', 'merged': False,
            'head': {'ref': REF.removeprefix('refs/heads/'), 'sha': HEAD,
                     'repo': {'full_name': REPOSITORY, 'fork': False}},
            'base': {'ref': 'main', 'sha': BASE, 'repo': {'full_name': REPOSITORY}}}


def main_ref():
    return {'ref': 'refs/heads/main', 'object': {'type': 'commit', 'sha': BASE}}


class ProtectedCandidateDbTests(unittest.TestCase):
    def test_authoritative_current_head_produces_allowlisted_exact_execution_identity(self):
        identity = GUARD(pull_request(), environment(), main_ref())
        self.assertEqual(HEAD, identity['headSha'])
        self.assertEqual(BASE, identity['baseSha'])
        self.assertEqual(42, identity['workflowRunId'])
        self.assertEqual(1, identity['workflowRunAttempt'])
        self.assertEqual('production-candidate-build', identity['sourceKind'])
        self.assertIs(False, identity['mainArtifactReused'])
        self.assertNotIn('body', identity)
        BINDING['validate_identity'](identity, environment())

    def test_dispatch_rejects_stale_injected_foreign_and_unclassified_identities(self):
        changes = ({'EXPECTED_SHA': BASE}, {'EXPECTED_SHA': '$(echo unsafe)'}, {'GITHUB_SHA': 'a' * 7},
                   {'GITHUB_REPOSITORY': 'foreign/Coglatas'}, {'GITHUB_REF': 'refs/tags/test'},
                   {'GITHUB_REF': 'refs/heads/other'}, {'GITHUB_EVENT_NAME': 'pull_request'},
                   {'GITHUB_EVENT_NAME': 'pull_request_target'}, {'GITHUB_WORKFLOW_REF': 'foreign/workflow'},
                   {'GITHUB_RUN_ID': '0'}, {'GITHUB_RUN_ATTEMPT': '0'}, {'GITHUB_RUN_ATTEMPT': '2'})
        for change in changes:
            with self.subTest(change=change), self.assertRaises(ValueError):
                GUARD(pull_request(), environment() | change, main_ref())

    def test_authoritative_pr_must_be_open_same_repository_main_and_current_fixed_head(self):
        for path, value in ((('number',), 1053), (('state',), 'closed'), (('merged',), True),
                            (('base', 'ref'), 'other'), (('base', 'sha'), 'missing'),
                            (('base', 'repo', 'full_name'), 'foreign/Coglatas'),
                            (('head', 'sha'), BASE), (('head', 'ref'), 'other'),
                            (('head', 'repo', 'full_name'), 'fork/Coglatas'), (('head', 'repo', 'fork'), True)):
            pr = pull_request()
            current = pr
            for name in path[:-1]:
                current = current[name]
            current[path[-1]] = value
            with self.subTest(path=path), self.assertRaises(ValueError):
                GUARD(pr, environment(), main_ref())

    def test_live_main_ref_must_match_authoritative_pr_base_before_ancestor_check(self):
        for main in ({'ref': 'refs/heads/main', 'object': {'type': 'commit', 'sha': 'c' * 40}},
                     {'ref': 'refs/tags/main', 'object': {'type': 'commit', 'sha': BASE}},
                     {'ref': 'refs/heads/main', 'object': {'type': 'tag', 'sha': BASE}}, {}):
            with self.subTest(main=main), self.assertRaises(ValueError):
                GUARD(pull_request(), environment(), main)

    def test_guard_artifact_cannot_cross_sha_run_attempt_ref_or_runtime_source(self):
        identity = GUARD(pull_request(), environment(), main_ref())
        for field, value in (('headSha', BASE), ('expectedSha', BASE), ('workflowRunId', 43),
                             ('workflowRunAttempt', 2), ('repository', 'foreign/Coglatas'),
                             ('headRef', 'refs/heads/other'), ('pullRequestNumber', 1053),
                             ('sourceKind', 'main-build-artifacts'), ('mainArtifactReused', True),
                             ('decision', 'invalid'), ('baseSha', 'missing'), ('schemaVersion', True)):
            with self.subTest(field=field), self.assertRaises(ValueError):
                BINDING['validate_identity'](identity | {field: value}, environment())
        for change in ({'GUARD_RESULT': 'failure'}, {'COLLECT_RESULT': 'skipped'},
                       {'COLLECT_RESULT': 'cancelled'}, {'EXPECTED_SHA': BASE}, {'GITHUB_RUN_ATTEMPT': '2'}):
            with self.subTest(change=change), self.assertRaises(ValueError):
                BINDING['validate_identity'](identity, environment() | change)

    def test_raw_profile_stamp_binds_all_files_to_exact_execution(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            for name in ('db.json', 'environment.json', 'fixture.json'):
                (directory / name).write_text('{"synthetic":true}')
            hashes = {name: hashlib.sha256((directory / name).read_bytes()).hexdigest()
                      for name in ('db.json', 'environment.json', 'fixture.json')}
            stamp = {'schemaVersion': 1, 'headSha': HEAD, 'profile': 'small', 'workflowRunId': 42,
                     'workflowRunAttempt': 1, 'sourceKind': 'production-candidate-build',
                     'mainArtifactReused': False, 'rawFileSha256': hashes}
            validate = BINDING['validate_capture_identity']
            validate(stamp, environment(), 'small', directory)
            for field, value in (('headSha', BASE), ('profile', 'medium'), ('workflowRunId', 43),
                                 ('workflowRunAttempt', 2), ('sourceKind', 'main-build-artifacts'),
                                 ('mainArtifactReused', True), ('schemaVersion', True)):
                with self.subTest(field=field), self.assertRaises(ValueError):
                    validate(stamp | {field: value}, environment(), 'small', directory)
            altered = copy.deepcopy(stamp)
            altered['rawFileSha256']['db.json'] = '0' * 64
            with self.assertRaises(ValueError):
                validate(altered, environment(), 'small', directory)
            (directory / 'db.json').write_text('{"altered":true}')
            with self.assertRaises(ValueError):
                validate(stamp, environment(), 'small', directory)
            (directory / 'fixture.json').unlink()
            with self.assertRaises(OSError):
                validate(stamp, environment(), 'small', directory)

    def test_manual_guard_precedes_secrets_and_requires_literal_exact_checkout(self):
        triggers = WORKFLOW.split('permissions:', 1)[0]
        self.assertIn('workflow_dispatch:', triggers)
        self.assertNotIn('pull_request:', triggers)
        self.assertNotIn('workflow_call:', triggers)
        self.assertNotIn('push:', triggers)
        self.assertNotIn('schedule:', triggers)
        self.assertNotIn(': write', WORKFLOW)
        guard, collect, gate = (WORKFLOW.split('  guard:', 1)[1].split('  collect:', 1)[0],
                                WORKFLOW.split('  collect:', 1)[1].split('  gate:', 1)[0],
                                WORKFLOW.split('  gate:', 1)[1])
        self.assertNotIn('secrets.', guard + gate)
        self.assertNotIn('environment:', guard + gate)
        self.assertIn("if: needs.guard.result == 'success' && needs.guard.outputs.validated == 'true'", collect)
        self.assertIn('environment: syncfusion-licensed-build', collect)
        self.assertEqual(1, collect.count('secrets.SYNCFUSION_LICENSE'))
        self.assertLess(collect.index('Recheck authoritative head'), collect.index('secrets.SYNCFUSION_LICENSE'))
        self.assertIn("['git', 'merge-base', '--is-ancestor'", guard)
        self.assertEqual(3, WORKFLOW.count('ref: ${{ github.sha }}'))
        self.assertEqual(3, WORKFLOW.count('persist-credentials: false'))
        self.assertNotIn('ref: ${{ inputs.', WORKFLOW)
        self.assertNotIn('${{ inputs.expected_sha }}', ''.join(line for line in WORKFLOW.splitlines() if 'EXPECTED_SHA:' not in line))

    def test_production_pair_preserves_strict_comparator_and_attempt_bound_artifacts(self):
        self.assertIn('profile: [small, medium]', WORKFLOW)
        self.assertIn('fail-fast: false', WORKFLOW)
        self.assertIn('COGLATAS_PERFORMANCE_RUNTIME_MODE: production', WORKFLOW)
        self.assertIn('COGLATAS_REUSE_PREBUILT_APP_IMAGE: "false"', WORKFLOW)
        self.assertIn('COGLATAS_REUSE_PREBUILT_DOTNET_BUILD: "false"', WORKFLOW)
        self.assertIn('COGLATAS_PERFORMANCE_DB_CAPTURE_ENABLED: "true"', WORKFLOW)
        self.assertNotIn('resolve-build', WORKFLOW)
        self.assertNotIn('restore-main-build-artifacts', WORKFLOW)
        self.assertIn('--duration --baselines performance/baselines/db', WORKFLOW)
        self.assertIn('--expected-sha "$TARGET_SHA"', WORKFLOW)
        self.assertIn('--base-ref "$base_sha" --head-sha "$TARGET_SHA"', WORKFLOW)
        self.assertIn('pattern: perf05-candidate-${{ github.run_attempt }}-*', WORKFLOW)
        self.assertIn('name: perf05-candidate-${{ github.run_attempt }}-${{ matrix.profile }}', WORKFLOW)
        self.assertIn('Preserve exact run and attempt gate decision\n        if: always()', WORKFLOW)
        self.assertIn('Upload complete safe candidate measurements\n        if: always()', WORKFLOW)
        self.assertNotIn('continue-on-error', WORKFLOW)
        self.assertNotIn('retry', WORKFLOW.replace('without retries', ''))


if __name__ == '__main__':
    unittest.main()
