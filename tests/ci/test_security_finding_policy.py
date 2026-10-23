"""Synthetic mechanics only: no entry below accepts an actual scanner finding."""

import copy
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / 'scripts/security/finding_policy.py'
SPEC = importlib.util.spec_from_file_location('finding_policy', SCRIPT)
POLICY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(POLICY)
NOW = '2026-10-23T12:00:00Z'


class FindingPolicyTests(unittest.TestCase):
    def setUp(self):
        self.contract = {'openapi': '3.1.0', 'paths': {
            '/api/files/{id}': {'get': {'parameters': [
                {'in': 'path', 'name': 'id'}, {'in': 'query', 'name': 'workspaceId'},
                {'in': 'query', 'name': 'versionId'}]}},
            '/api/files/shared': {'get': {}},
            '/api/files/{id}/sharing': {'post': {}}}}
        self.operations = POLICY.contract_operations(self.contract)
        self.policy = {'schemaVersion': 1, 'activation': 'NOT_ACTIVATED',
                       'initialInventory': 'UNREVIEWED', 'exceptions': []}
        self.scope = {'role': 'alpha-owner', 'method': 'GET', 'routeClass': '/api/files/{id}',
                      'parameterCategory': 'query', 'parameterName': 'workspaceId'}
        self.finding = {'ruleId': '10062', 'severity': 'Medium', 'confidence': 'medium',
                        'findingClass': 'generic', 'scope': self.scope}
        self.report = {'schemaVersion': 1, 'scanner': {'id': 'zap-api', 'version': '2.17.0'},
                       'sourceSha': '4' * 40, 'configSha256': 'a' * 64,
                       'execution': {'status': 'PASS_EXECUTED', 'exitCode': 0}, 'findings': [self.finding]}

    def result(self):
        return POLICY.evaluate(self.report, self.policy, self.operations, NOW)

    def exception(self):
        return {'id': 'synthetic-only', 'tool': 'zap-api', 'ruleId': '10062', 'severity': 'Medium',
                'findingClass': 'generic', 'scope': copy.deepcopy(self.scope),
                'fingerprint': POLICY.normalize_document(self.report, self.operations)['findings'][0]['fingerprint'],
                'rationale': 'Synthetic matcher control; accepts no real finding.', 'owner': 'synthetic-owner',
                'createdAtUtc': '2026-10-20T00:00:00Z', 'expiresAtUtc': '2026-10-24T00:00:00Z',
                'trackingIssue': 'https://github.com/NYGsatoshi/Coglatas/issues/582',
                'approval': {'approvedBy': 'synthetic-reviewer', 'approvedAtUtc': '2026-10-21T00:00:00Z',
                             'reference': 'https://github.com/NYGsatoshi/Coglatas/pull/1'}}

    def native(self):
        return {'schemaVersion': 1, 'control': 'SEC-06', 'role': 'alpha-owner',
                'scanner': {'name': 'OWASP ZAP', 'version': '2.17.0',
                            'image': 'zaproxy/zap-stable:2.17.0@sha256:' + 'b' * 64, 'exitCode': 0},
                'target': {'origin': 'http://app:8080', 'isolated': True, 'externalNetworkAccess': False},
                'inputs': {key: 'a' * 64 for key in
                           ('openApiSha256', 'automationPlanSha256', 'policySha256', 'addonListSha256')},
                'sanitization': {'forbiddenValueCount': 1, 'unsanitizedAllowed': False},
                'summary': {'uniqueAlertsByRisk': {'High': 0, 'Medium': 1, 'Low': 0, 'Informational': 0},
                            'uniqueAlertsByRule': {'10062': 1}, 'instancesByRule': {'10062': 1}, 'blockingHighAlerts': 0},
                'alerts': [{'ruleId': '10062', 'name': 'Discarded secret-bearing scanner label', 'risk': 'Medium',
                            'confidence': '2', 'cweId': '', 'wascId': '', 'instanceCount': 1,
                            'instances': [{'method': 'GET', 'path': '/api/files/volatile-id',
                                           'parameter': 'workspaceId', 'queryParameterNames': ['workspaceId']}]}],
                'attribution': {'status': 'not-captured'}}

    def test_repository_policy_has_no_real_entries_and_is_not_activated(self):
        actual, _ = POLICY.read_json(ROOT / 'scripts/security/finding-policy.json')
        self.assertEqual(self.policy, actual)
        self.assertEqual([], POLICY.validate_policy(actual, self.operations, NOW))

    def test_new_medium_blocks_without_any_entry(self):
        result = self.result()
        self.assertEqual('BLOCK', result['decision'])
        self.assertEqual('BLOCK_UNAPPROVED_MEDIUM', result['findings'][0]['suppressionMatch'])
        self.assertFalse(result['approvalVerified'])
        self.assertEqual('NOT_ACTIVATED', result['activation'])
        self.assertFalse(result['liveGateQualified'])
        self.assertEqual('OFFLINE_DECLARED_METADATA_ONLY', result['evaluationScope'])

    def test_exact_declared_medium_is_visible_without_fabricating_approval(self):
        self.policy['exceptions'] = [self.exception()]
        result = self.result()
        self.assertEqual('PASS_OFFLINE_EVALUATION_ONLY', result['decision'])
        self.assertEqual('MATCHED_DECLARED_EXCEPTION', result['findings'][0]['suppressionMatch'])
        self.assertEqual('synthetic-only', result['findings'][0]['exceptionId'])
        self.assertFalse(result['approvalVerified'])

    def test_expiry_at_evaluation_instant_reopens_block(self):
        entry = self.exception()
        entry['expiresAtUtc'] = NOW
        self.policy['exceptions'] = [entry]
        self.assertEqual('BLOCK_EXPIRED_EXCEPTION', self.result()['findings'][0]['suppressionMatch'])

    def test_high_and_critical_cannot_use_matching_medium_exception(self):
        self.policy['exceptions'] = [self.exception()]
        for risk in ('High', 'Critical'):
            with self.subTest(risk=risk):
                self.finding['severity'] = risk
                self.assertEqual('BLOCK_NON_SUPPRESSIBLE', self.result()['findings'][0]['suppressionMatch'])
                self.policy['exceptions'][0]['severity'] = risk
                with self.assertRaises(POLICY.PolicyError):
                    self.result()
                self.policy['exceptions'][0]['severity'] = 'Medium'

    def test_all_mandatory_control_classes_block_even_info(self):
        for finding_class in POLICY.CLASSES - {'generic'}:
            with self.subTest(finding_class=finding_class):
                self.finding.update(findingClass=finding_class, severity='Informational')
                self.assertEqual('BLOCK_NON_SUPPRESSIBLE', self.result()['findings'][0]['suppressionMatch'])

    def test_wildcard_regex_and_cross_route_exceptions_fail_validation(self):
        for path in ('*', '/api/*', '/api/files/.*', '^/api/files$', '/api/files/{other}', '/api/files/{id}?x=1'):
            with self.subTest(path=path):
                entry = self.exception()
                entry['scope']['routeClass'] = path
                self.policy['exceptions'] = [entry]
                with self.assertRaises(POLICY.PolicyError):
                    self.result()

    def test_required_exception_fields_and_approval_cannot_be_omitted(self):
        for key in POLICY.EXCEPTION_FIELDS:
            with self.subTest(key=key):
                entry = self.exception()
                del entry[key]
                self.policy['exceptions'] = [entry]
                with self.assertRaises(POLICY.PolicyError):
                    self.result()

    def test_blank_owner_rationale_bad_dates_and_external_links_fail(self):
        for key, value in [('owner', ''), ('rationale', ' '), ('expiresAtUtc', None),
                           ('createdAtUtc', '2027-01-01T00:00:00Z'), ('expiresAtUtc', '2026-10-19T00:00:00Z'),
                           ('trackingIssue', 'https://other.invalid/issues/1'), ('fingerprint', 'a' * 64)]:
            with self.subTest(key=key, value=value):
                entry = self.exception()
                entry[key] = value
                self.policy['exceptions'] = [entry]
                with self.assertRaises(POLICY.PolicyError):
                    self.result()

    def test_rule_route_method_parameter_role_and_tool_mismatches_never_suppress(self):
        changes = [lambda f: f.update(ruleId='10063'),
                   lambda f: f['scope'].update(routeClass='/api/files/shared', parameterCategory='none', parameterName=None),
                   lambda f: f['scope'].update(method='POST', routeClass='/api/files/{id}/sharing', parameterCategory='none', parameterName=None),
                   lambda f: f['scope'].update(parameterName='versionId'),
                   lambda f: f['scope'].update(role='beta-owner')]
        for change in changes:
            with self.subTest(change=change):
                report = copy.deepcopy(self.report)
                policy = copy.deepcopy(self.policy)
                policy['exceptions'] = [self.exception()]
                change(report['findings'][0])
                result = POLICY.evaluate(report, policy, self.operations, NOW)
                self.assertEqual('BLOCK_UNAPPROVED_MEDIUM', result['findings'][0]['suppressionMatch'])
        self.policy['exceptions'] = [self.exception()]
        self.report['scanner']['id'] = 'zap-browser'
        self.assertEqual('BLOCK_UNAPPROVED_MEDIUM', self.result()['findings'][0]['suppressionMatch'])

    def test_duplicate_ids_or_matching_scopes_and_unknown_policy_fields_fail(self):
        entry = self.exception()
        for entries in ([entry, copy.deepcopy(entry)], [entry, dict(entry, id='other')]):
            self.policy['exceptions'] = entries
            with self.assertRaises(POLICY.PolicyError):
                self.result()
        self.policy['exceptions'] = []
        self.policy['ignoreAllMedium'] = True
        with self.assertRaises(POLICY.PolicyError):
            self.result()

    def test_no_live_activation_or_inventory_approval_can_be_inferred(self):
        for key, value in [('activation', 'ENFORCE'), ('initialInventory', 'APPROVED')]:
            with self.subTest(key=key):
                policy = dict(self.policy, **{key: value})
                with self.assertRaises(POLICY.PolicyError):
                    POLICY.evaluate(self.report, policy, self.operations, NOW)

    def test_execution_failure_is_not_zero_finding_pass(self):
        self.report['findings'] = []
        for status in POLICY.EXECUTION - {'PASS_EXECUTED'}:
            with self.subTest(status=status):
                self.report['execution'] = {'status': status, 'exitCode': 0}
                self.assertEqual('BLOCK', self.result()['decision'])
        self.report['execution'] = {'status': 'PASS_EXECUTED', 'exitCode': 124}
        with self.assertRaises(POLICY.PolicyError):
            self.result()

    def test_low_info_remain_visible_and_clean_report_can_pass_offline(self):
        for severity in ('Low', 'Informational'):
            self.finding['severity'] = severity
            self.assertEqual('REPORT_ONLY', self.result()['findings'][0]['suppressionMatch'])
        self.report['findings'] = []
        self.assertEqual('PASS_OFFLINE_EVALUATION_ONLY', self.result()['decision'])

    def test_fingerprint_stable_across_run_source_version_order_and_instance_ids(self):
        native = self.native()
        a = POLICY.adapt_zap(native, self.operations, '1' * 40, 'a' * 64)
        native['alerts'][0]['instances'][0]['path'] = '/api/files/a-different-instance'
        native['scanner']['version'] = '2.18.0'
        b = POLICY.adapt_zap(native, self.operations, '2' * 40, 'a' * 64)
        left = POLICY.normalize_document(a, self.operations)['findings'][0]
        right = POLICY.normalize_document(b, self.operations)['findings'][0]
        self.assertEqual(left['fingerprint'], right['fingerprint'])
        self.assertEqual(left['evidenceHash'], right['evidenceHash'])
        self.assertNotIn('volatile', json.dumps(left))
        self.assertNotIn('secret-bearing', json.dumps(left))

    def test_distinct_parameter_scope_changes_fingerprint_and_duplicate_order_is_stable(self):
        first = POLICY.normalize_document(self.report, self.operations)
        other = copy.deepcopy(self.finding)
        other['scope']['parameterName'] = 'versionId'
        self.report['findings'] += [other, copy.deepcopy(self.finding)]
        a = POLICY.normalize_document(self.report, self.operations)
        self.report['findings'].reverse()
        self.assertEqual(a, POLICY.normalize_document(self.report, self.operations))
        self.assertEqual(2, len(a['findings']))
        self.assertEqual(1, sum(f['fingerprint'] == first['findings'][0]['fingerprint'] for f in a['findings']))

    def test_zap_literal_route_wins_and_unknown_or_ambiguous_scope_fails(self):
        instance = {'method': 'GET', 'parameter': '', 'path': '/api/files/shared'}
        self.assertEqual('/api/files/shared', POLICY.route_scope(instance, 'alpha-owner', self.operations)['routeClass'])
        for change in ({'path': '/outside'}, {'parameter': 'raw-secret'}, {'path': '/api/files/%2Fsecret'}, {'body': 'raw'}):
            with self.subTest(change=change):
                with self.assertRaises(POLICY.PolicyError):
                    POLICY.route_scope(dict(instance, **change), 'alpha-owner', self.operations)

    def test_zap_native_summary_contract_and_complete_instance_inventory_required(self):
        native = self.native()
        self.assertEqual('Medium', POLICY.adapt_zap(native, self.operations, '1' * 40, 'a' * 64)['findings'][0]['severity'])
        for change in (lambda n: n['alerts'][0].update(instanceCount=26),
                       lambda n: n['summary'].update(blockingHighAlerts=1),
                       lambda n: n['sanitization'].update(unsanitizedAllowed=True),
                       lambda n: n['inputs'].update(openApiSha256='b' * 64),
                       lambda n: n['target'].update(origin='https://public.invalid')):
            with self.subTest(change=change):
                value = copy.deepcopy(native)
                change(value)
                with self.assertRaises(POLICY.PolicyError):
                    POLICY.adapt_zap(value, self.operations, '1' * 40, 'a' * 64)

    def test_unknown_report_fields_severity_execution_and_scope_fail_closed(self):
        for key, value in [('severity', 'Warning'), ('findingClass', 'unknown'), ('confidence', 'arbitrary')]:
            report = copy.deepcopy(self.report)
            report['findings'][0][key] = value
            with self.assertRaises(POLICY.PolicyError):
                POLICY.evaluate(report, self.policy, self.operations, NOW)
        self.report['findings'][0]['rawBody'] = 'sensitive'
        with self.assertRaises(POLICY.PolicyError):
            self.result()

    def cli(self, report=None, extra=None, secrets=None, timeout=15):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        root = Path(directory.name)
        for name, value in [('input', report if report is not None else self.report), ('policy', self.policy), ('contract', self.contract)]:
            (root / (name + '.json')).write_text(json.dumps(value), encoding='utf8')
        output = root / 'out.json'
        args = [sys.executable, '-B', str(SCRIPT), '--input', str(root/'input.json'), '--policy', str(root/'policy.json'),
                '--contract', str(root/'contract.json'), '--evaluated-at', NOW, '--output', str(output)]
        if extra:
            args += extra(root, output)
        env = os.environ.copy()
        env.pop(POLICY.FORBIDDEN_ENV, None)
        if secrets is not None:
            env[POLICY.FORBIDDEN_ENV] = json.dumps(secrets)
        result = subprocess.run(args, capture_output=True, text=True, env=env, timeout=timeout)
        return result, output

    def test_actual_cli_clean_positive_and_medium_negative(self):
        result, output = self.cli(secrets=['private-fixture-secret'])
        self.assertEqual(1, result.returncode)
        self.assertEqual('BLOCK', json.loads(output.read_text())['decision'])
        self.report['findings'] = []
        result, output = self.cli(secrets=['private-fixture-secret'])
        self.assertEqual(0, result.returncode)
        self.assertEqual('NOT_ACTIVATED', json.loads(output.read_text())['activation'])

    def test_actual_cli_missing_empty_malformed_duplicate_and_oversize_report_fail_without_receipt(self):
        mutations = [lambda root, out: (root/'input.json').unlink(),
                     lambda root, out: (root/'input.json').write_bytes(b''),
                     lambda root, out: (root/'input.json').write_bytes(b'{'),
                     lambda root, out: (root/'input.json').write_bytes(b'{"a":1,"a":2}'),
                     lambda root, out: (root/'input.json').write_bytes(b' ' * (POLICY.LIMIT + 1))]
        for mutate in mutations:
            with self.subTest(mutate=mutate):
                def extra(root, out):
                    mutate(root, out)
                    return []
                result, output = self.cli(extra=extra, secrets=['private-fixture-secret'])
                self.assertEqual(2, result.returncode)
                self.assertFalse(output.exists())
                self.assertNotIn('Traceback', result.stderr)

    def test_actual_cli_secret_safe_report_and_no_unsanitized_opt_out(self):
        self.report['findings'] = []
        result, output = self.cli()
        self.assertEqual(2, result.returncode)
        self.assertFalse(output.exists())
        self.report['findings'] = [self.finding]
        result, output = self.cli(secrets=['workspaceId'])
        self.assertEqual(2, result.returncode)
        self.assertFalse(output.exists())
        self.assertNotIn('workspaceId', result.stdout + result.stderr)
        POLICY.ensure_safe('{"bounded":"safe"}', ['private-fixture-secret'])
        for text in ('secret token', 'secret%20token'):
            with self.assertRaises(POLICY.PolicyError):
                POLICY.ensure_safe(text, ['secret token'])

    def test_actual_cli_does_not_overwrite_a_retained_receipt(self):
        def existing(root, output):
            output.write_text('retained failure', encoding='utf8')
            return []
        result, output = self.cli(extra=existing, secrets=['private-fixture-secret'])
        self.assertEqual(2, result.returncode)
        self.assertEqual('retained failure', output.read_text())

    def test_actual_cli_protected_unknown_fields_never_reach_receipt_or_errors(self):
        for key in ('rawBody', 'authorization', 'cookie', 'signedUrl', 'storageKey'):
            with self.subTest(key=key):
                report = copy.deepcopy(self.report)
                report['findings'][0][key] = 'protected-unknown-field-canary'
                result, output = self.cli(report=report, secrets=['different-authentication-canary'])
                self.assertEqual(2, result.returncode)
                self.assertFalse(output.exists())
                self.assertNotIn('protected-unknown-field-canary', result.stdout + result.stderr)

    def test_actual_cli_invalid_arguments_and_mixed_help_never_echo_protected_values(self):
        canary = 'protected-argument-canary'
        for arguments in (['--unexpected', canary], ['--'+canary], ['--contract'],
                          ['--evaluated-at', canary], ['--help', '--unexpected', canary]):
            with self.subTest(argumentKind=arguments[0]):
                result, output = self.cli(extra=lambda root, out: arguments, secrets=[canary])
                self.assertEqual(2, result.returncode)
                self.assertFalse(output.exists())
                self.assertNotIn(canary, result.stdout + result.stderr)
                self.assertNotIn('Traceback', result.stderr)
                self.assertEqual('SEC-08 offline evaluation rejected: INVALID_OR_UNSAFE_INPUT; NOT_ACTIVATED\n', result.stderr)
        result = subprocess.run([sys.executable, '-B', str(SCRIPT), '--help'], capture_output=True,
                                text=True, timeout=15)
        self.assertEqual(0, result.returncode)
        self.assertEqual('', result.stderr)
        self.assertIn('finding-policy', result.stdout)
        self.assertNotIn(str(SCRIPT), result.stdout)

    def test_actual_cli_nonregular_inputs_rejected_before_open_including_posix_fifo(self):
        for option in ('--input', '--contract', '--policy'):
            def directory(root, output):
                return [option, str(root)]
            result, output = self.cli(extra=directory, secrets=['nonregular-input-canary'], timeout=3)
            self.assertEqual(2, result.returncode)
            self.assertFalse(output.exists())
            if os.name == 'posix':
                def fifo(root, output):
                    path = root/'nonregular-input-canary'
                    os.mkfifo(path)
                    return [option, str(path)]
                result, output = self.cli(extra=fifo, secrets=['nonregular-input-canary'], timeout=3)
                self.assertEqual(2, result.returncode)
                self.assertFalse(output.exists())
                self.assertNotIn('nonregular-input-canary', result.stdout + result.stderr)
                self.assertNotIn('Traceback', result.stderr)

    def test_actual_cli_unpaired_sanitizer_surrogates_fail_with_finite_error(self):
        for value in ('protected-surrogate-canary-\ud800', '\udfff'):
            result, output = self.cli(secrets=[value])
            self.assertEqual(2, result.returncode)
            self.assertFalse(output.exists())
            self.assertNotIn('protected-surrogate-canary', result.stdout + result.stderr)
            self.assertNotIn('Traceback', result.stderr)
            self.assertEqual('SEC-08 offline evaluation rejected: INVALID_OR_UNSAFE_INPUT; NOT_ACTIVATED\n', result.stderr)

    def test_acquisition_rejects_changed_regular_file_and_posix_fifo_race(self):
        with tempfile.TemporaryDirectory() as name:
            root = Path(name)
            path, other = root/'input.json', root/'other.json'
            path.write_text('{}', encoding='utf8')
            other.write_text('{}', encoding='utf8')
            real_open = os.open
            with mock.patch.object(POLICY.os, 'open', side_effect=lambda target, flags: real_open(other, flags)):
                with self.assertRaises(POLICY.PolicyError):
                    POLICY.read_json(path)
            if os.name == 'posix':
                def swapped_fifo(target, flags):
                    self.assertTrue(flags & os.O_NONBLOCK)
                    self.assertTrue(flags & os.O_NOFOLLOW)
                    path.unlink()
                    os.mkfifo(path)
                    return real_open(target, flags)
                with mock.patch.object(POLICY.os, 'open', side_effect=swapped_fifo):
                    with self.assertRaises(POLICY.PolicyError):
                        POLICY.read_json(path)

    def test_actual_zap_cli_projects_only_owned_metadata_and_preserves_failure(self):
        with tempfile.TemporaryDirectory() as name:
            root = Path(name)
            contract_path = root/'contract.json'
            contract_path.write_text(json.dumps(self.contract), encoding='utf8')
            contract_sha = POLICY.read_json(contract_path)[1]
            native = self.native()
            native['inputs']['openApiSha256'] = contract_sha
            native['alerts'][0]['risk'] = 'High'
            native['summary']['uniqueAlertsByRisk'].update(High=1, Medium=0)
            native['summary']['blockingHighAlerts'] = 1
            for filename, value in [('native', native), ('policy', self.policy)]:
                (root/(filename+'.json')).write_text(json.dumps(value), encoding='utf8')
            output = root/'sidecar.json'
            env = os.environ.copy()
            env[POLICY.FORBIDDEN_ENV] = json.dumps(['secret-bearing scanner label', 'volatile-id'])
            result = subprocess.run([sys.executable, '-B', str(SCRIPT), '--zap-report', str(root/'native.json'),
                '--source-sha', '4'*40, '--contract', str(contract_path), '--policy', str(root/'policy.json'),
                '--evaluated-at', NOW, '--output', str(output)], capture_output=True, text=True, env=env, timeout=15)
            self.assertEqual(1, result.returncode)
            evidence = json.loads(output.read_text())
            self.assertEqual('BLOCK_NON_SUPPRESSIBLE', evidence['findings'][0]['suppressionMatch'])
            self.assertEqual('/api/files/{id}', evidence['findings'][0]['scope']['routeClass'])
            self.assertNotIn('volatile-id', output.read_text())
            self.assertNotIn('secret-bearing', output.read_text())

    def test_native_boolean_counters_or_fake_isolation_cannot_pass(self):
        for change in (lambda n: n['summary']['uniqueAlertsByRisk'].update(Medium=True),
                       lambda n: n['summary'].update(blockingHighAlerts=False),
                       lambda n: n['target'].update(isolated=1)):
            native = self.native()
            change(native)
            with self.assertRaises(POLICY.PolicyError):
                POLICY.adapt_zap(native, self.operations, '1'*40, 'a'*64)

    def test_unclassified_body_parameters_and_ambiguous_route_never_gain_exception_scope(self):
        instance = {'method': 'GET', 'path': '/api/files/id', 'parameter': 'bodySecret'}
        with self.assertRaises(POLICY.PolicyError):
            POLICY.route_scope(instance, 'alpha-owner', self.operations)
        self.operations[('GET', '/api/files/{other}')] = self.operations[('GET', '/api/files/{id}')]
        instance['parameter'] = ''
        with self.assertRaises(POLICY.PolicyError):
            POLICY.route_scope(instance, 'alpha-owner', self.operations)

    def test_future_or_missing_approval_metadata_and_inventory_shortcuts_fail(self):
        for approval in ({}, {'approvedBy': '', 'approvedAtUtc': NOW, 'reference': 'https://github.com/NYGsatoshi/Coglatas/pull/1'},
                         {'approvedBy': 'synthetic', 'approvedAtUtc': '2026-10-24T00:00:00Z',
                          'reference': 'https://github.com/NYGsatoshi/Coglatas/pull/1'}):
            entry = self.exception()
            entry['approval'] = approval
            self.policy['exceptions'] = [entry]
            with self.assertRaises(POLICY.PolicyError):
                self.result()


if __name__ == '__main__':
    unittest.main()
