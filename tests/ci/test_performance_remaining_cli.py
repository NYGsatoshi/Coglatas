#!/usr/bin/env python3
"""Offline boundary controls for the remaining Performance CLI entry points."""
import contextlib
import importlib.util
import io
import os
import sys
import tempfile
import unittest
import urllib.error
from pathlib import Path
from unittest.mock import Mock, call, patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts/performance'))
from common import PerformanceContractError
from local_support import LocalError


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'scripts/performance' / filename)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


preflight = load('remaining_preflight', 'preflight.py')
warmup = load('remaining_warmup', 'warmup.py')
github = load('remaining_github', 'local-github-preflight.py')
announcement = load('remaining_announcement', 'local-announcement-diagnostic.py')


class RemainingPerformanceCliTests(unittest.TestCase):
    def test_preflight_limits_response_reads_and_preserves_tenant_and_timeout(self):
        response = Mock(status=200)
        response.read.return_value = b'ready'
        manager = Mock()
        manager.__enter__ = Mock(return_value=response)
        manager.__exit__ = Mock(return_value=False)
        with patch.object(preflight.urllib.request, 'urlopen', return_value=manager) as opened:
            self.assertEqual((200, 'ready'), preflight.request_ok('http://127.0.0.1/health/ready', 'synthetic', 2.5))
        request = opened.call_args.args[0]
        self.assertEqual('synthetic', request.get_header('X-tenant-slug'))
        self.assertEqual({'timeout': 2.5}, opened.call_args.kwargs)
        response.read.assert_called_once_with(4096)

    def test_preflight_http_denial_retains_status_and_bounded_body(self):
        error = urllib.error.HTTPError('http://127.0.0.1/health/ready', 503, 'unavailable', {}, io.BytesIO(b'x' * 5000))
        with patch.object(preflight.urllib.request, 'urlopen', side_effect=error):
            self.assertEqual((503, 'x' * 4096), preflight.request_ok('http://127.0.0.1/health/ready', 'synthetic', 1))

    def test_preflight_rejection_writes_no_success_or_network_request(self):
        argv = ['preflight', '--base-url', 'invalid', '--profile', 'small', '--fixture-evidence', 'unused', '--output', 'unused-output']
        with patch.object(sys, 'argv', argv), patch.object(preflight, 'validate_target', side_effect=PerformanceContractError('denied')), \
                patch.object(preflight, 'load_profile') as profile, patch.object(preflight, 'request_ok') as requested, \
                patch.object(preflight, 'write_json_atomic') as written, contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(2, preflight.main())
        profile.assert_not_called()
        requested.assert_not_called()
        written.assert_not_called()

    def warmup_rejection(self, contract, expected_error, with_password=True):
        argv = ['warmup', '--base-url', 'http://127.0.0.1', '--profile', 'small', '--fixture-evidence', 'synthetic', '--output', 'unused-output']
        fixture = {'identities': {'tenantSlug': 'synthetic', 'operatorEmail': 'operator@example.invalid',
                                 'workspaceId': 'workspace', 'taskListProjectId': 'task-project',
                                 'ganttProjectId': 'gantt-project', 'kanbanProjectId': 'kanban-project'},
                   'fixtureHash': 'synthetic'}
        environment = {'COGLATAS_PERFORMANCE_PASSWORD': 'synthetic-unit-credential'} if with_password else {}
        errors = io.StringIO()
        with patch.object(sys, 'argv', argv), patch.dict(os.environ, environment, clear=True), \
                patch.object(warmup, 'validate_target', return_value='http://127.0.0.1'), \
                patch.object(warmup, 'load_json', side_effect=[fixture, contract]), \
                patch.object(warmup, 'validate_fixture_evidence', return_value=fixture), \
                patch.object(warmup.urllib.request, 'build_opener') as opened, \
                patch.object(warmup, 'write_json_atomic') as written, contextlib.redirect_stderr(errors):
            self.assertEqual(2, warmup.main())
        self.assertIn(expected_error, errors.getvalue())
        opened.assert_not_called()
        written.assert_not_called()

    def test_warmup_rejects_measurement_credit_or_invalid_contract_before_authentication(self):
        for value, expected in [(None, 'warm-up contract must explicitly set measured=false'),
                                ({}, 'warm-up contract must explicitly set measured=false'),
                                ({'measured': True}, 'warm-up contract must explicitly set measured=false'),
                                ({'measured': False, 'iterations': 0}, 'warm-up iterations must be a positive integer'),
                                ({'measured': False, 'iterations': 1, 'routes': []}, 'warm-up contract requires at least one route')]:
            with self.subTest(contract=value):
                self.warmup_rejection({'warmup': value}, expected)

    def test_warmup_requires_password_before_constructing_a_network_client(self):
        self.warmup_rejection({'warmup': {'measured': False, 'iterations': 1, 'routes': ['/health/ready']}},
                              'COGLATAS_PERFORMANCE_PASSWORD is required for warm-up authentication', with_password=False)

    def test_warmup_http_denial_is_consumed_and_reported_as_a_status(self):
        opener = Mock()
        opener.open.side_effect = urllib.error.HTTPError('http://127.0.0.1/route', 403, 'denied', {}, io.BytesIO(b'denied'))
        with patch.object(warmup.time, 'perf_counter', side_effect=[1, 1.25]):
            self.assertEqual((403, 250.0), warmup.open_request(opener, object(), 3))
        self.assertEqual({'timeout': 3}, opener.open.call_args.kwargs)

    def github_environment(self, output):
        return {'GITHUB_REPOSITORY': 'NYGsatoshi/Coglatas', 'GITHUB_REF': 'refs/heads/main',
                'SOURCE_SHA': '1' * 40, 'BASE_SHA': '2' * 40, 'EVIDENCE_COMMIT_SHA': '3' * 40,
                'PR_NUMBER': '7', 'GITHUB_OUTPUT': str(output)}

    def test_github_preflight_rejects_untrusted_workflow_before_api_or_git(self):
        for changed in [{'GITHUB_REF': 'refs/pull/7/merge'}, {'GITHUB_REPOSITORY': 'foreign/repository'}, {'SOURCE_SHA': 'not-a-sha'}]:
            with self.subTest(changed=changed), tempfile.TemporaryDirectory() as directory:
                output = Path(directory) / 'output'
                with patch.dict(os.environ, self.github_environment(output) | changed, clear=True), \
                        patch.object(github, 'get') as get, patch.object(github, 'invoke') as invoke:
                    with self.assertRaises(LocalError):
                        github.main()
                get.assert_not_called()
                invoke.assert_not_called()
                self.assertFalse(output.exists())

    def test_github_preflight_binds_live_pr_base_head_tree_and_evidence_before_output(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / 'output'
            environment = self.github_environment(output)
            replies = [{'commit': {'sha': '2' * 40}},
                       {'state': 'open', 'head': {'repo': {'full_name': 'NYGsatoshi/Coglatas'}, 'sha': '1' * 40}, 'base': {'sha': '2' * 40}},
                       {'status': 'ahead'}, {'commit': {'tree': {'sha': '4' * 40}}}, {}]
            with patch.dict(os.environ, environment, clear=True), patch.object(github, 'get', side_effect=replies) as get, \
                    patch.object(github, 'invoke', return_value='2' * 40) as invoke, contextlib.redirect_stdout(io.StringIO()):
                github.main()
            self.assertEqual([call('branches/main'), call('pulls/7'), call('compare/' + '2' * 40 + '...' + '1' * 40),
                              call('commits/' + '1' * 40), call('commits/' + '3' * 40)], get.call_args_list)
            invoke.assert_called_once_with(['git', 'rev-parse', 'HEAD'])
            self.assertEqual('tree_sha=' + '4' * 40 + '\ntrusted_sha=' + '2' * 40 + '\n', output.read_text())

    def test_github_preflight_rejects_live_identity_mismatches_before_source_or_output(self):
        upstream = {'repo': {'full_name': 'NYGsatoshi/Coglatas'}, 'sha': '1' * 40}
        cases = [('trusted-head', '5' * 40, {}, 'ahead', 'trusted-main', 1),
                 ('pr-head', '2' * 40, {'head': upstream | {'sha': '5' * 40}}, 'ahead', 'live-pr-head', 2),
                 ('pr-base', '2' * 40, {'base': {'sha': '5' * 40}}, 'ahead', 'live-pr-head', 2),
                 ('pr-closed', '2' * 40, {'state': 'closed'}, 'ahead', 'live-pr-head', 2),
                 ('pr-repository', '2' * 40, {'head': upstream | {'repo': {'full_name': 'foreign/repository'}}}, 'ahead', 'live-pr-head', 2),
                 ('ancestry', '2' * 40, {}, 'behind', 'base-ancestry', 3)]
        for name, trusted, changes, ancestry, stage, expected_calls in cases:
            with self.subTest(mismatch=name), tempfile.TemporaryDirectory() as directory:
                output = Path(directory) / 'output'
                info = {'state': 'open', 'head': upstream, 'base': {'sha': '2' * 40}} | changes
                replies = [{'commit': {'sha': '2' * 40}}, info, {'status': ancestry},
                           {'commit': {'tree': {'sha': '4' * 40}}}, {}]
                with patch.dict(os.environ, self.github_environment(output), clear=True), \
                        patch.object(github, 'get', side_effect=replies) as get, \
                        patch.object(github, 'invoke', return_value=trusted):
                    with self.assertRaises(LocalError) as rejected:
                        github.main()
                self.assertEqual('EVIDENCE_INVALID', rejected.exception.code)
                self.assertEqual(stage, rejected.exception.stage)
                self.assertEqual(expected_calls, get.call_count)
                self.assertFalse(output.exists())

    def test_announcement_diagnostic_rejects_nonlocal_or_ci_execution_before_outputs(self):
        for environment in [{}, {'COGLATAS_LOCAL_EVIDENCE_MODE': 'LOCAL_DIAGNOSTIC', 'GITHUB_ACTIONS': 'true'}]:
            with self.subTest(environment=environment), patch.dict(os.environ, environment, clear=True), \
                    patch.object(announcement, 'write_json_atomic') as written, patch.object(announcement, 'invoke') as invoked, \
                    patch.object(announcement.urllib.request, 'build_opener') as opened:
                with self.assertRaises(LocalError):
                    announcement.main()
                written.assert_not_called()
                invoked.assert_not_called()
                opened.assert_not_called()

    def test_announcement_diagnostic_never_overwrites_an_existing_prospective_manifest(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'prospective-manifest.json'
            original = b'{"synthetic":"retained"}'
            path.write_bytes(original)
            with patch.dict(os.environ, {'COGLATAS_LOCAL_EVIDENCE_MODE': 'LOCAL_DIAGNOSTIC', 'COGLATAS_LOCAL_ANNOUNCEMENT_OUTPUT': directory}, clear=True), \
                    patch.object(announcement, 'write_json_atomic') as written, patch.object(announcement, 'invoke') as invoked:
                with self.assertRaises(LocalError):
                    announcement.main()
            self.assertEqual(original, path.read_bytes())
            written.assert_not_called()
            invoked.assert_not_called()


if __name__ == '__main__':
    unittest.main()
