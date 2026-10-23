import copy
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
from types import SimpleNamespace
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location('zap_report_attribution', ROOT / 'scripts/security/process-zap-report.py')
REPORT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(REPORT)


class AttributionEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        root = Path(self.directory.name)
        self.args = SimpleNamespace(attribution_report=root / 'attribution.json', contract=root / 'contract.json', role='alpha-owner')
        self.args.contract.write_text(json.dumps({'components': {'schemas': {'File': {'properties': {'items': {}, 'id': {}}}}}}))
        self.value = {'schemaVersion': 2, 'role': 'alpha-owner', 'scopeRuleId': '10062', 'sqlResponses': [], 'instances': [
            {'alertId': 7, 'ruleId': '10062', 'risk': 'High', 'status': 'matched-json-property', 'locations': [
                {'schemaPath': ['items', '*', 'id'], 'valueCategory': 'uuid-substring', 'matchKind': 'substring'}]}]}
        self.alerts = [{'ruleId': '10062', 'risk': 'High', 'instanceCount': 1}]

    def load(self):
        self.args.attribution_report.write_text(json.dumps(self.value))
        return REPORT.load_attribution(self.args, self.alerts)

    def test_schema_location_retained_and_high_still_blocks(self):
        self.assertEqual(self.value['instances'], self.load()['instances'])
        with self.assertRaisesRegex(SystemExit, 'High'):
            REPORT.enforce_blocking_policy(0, {'High': 1})

    def test_arbitrary_response_property_and_raw_literals_rejected(self):
        self.value['instances'][0]['locations'][0]['schemaPath'] = ['private-user@example.invalid']
        with self.assertRaisesRegex(SystemExit, 'unsafe'):
            self.load()
        self.value['instances'][0]['locations'][0]['schemaPath'] = ['id']
        self.value['instances'][0]['evidence'] = 'protected literal'
        with self.assertRaisesRegex(SystemExit, 'unsafe'):
            self.load()

    def test_role_mismatch_and_missing_instance_rejected(self):
        self.value['role'] = 'beta-owner'
        with self.assertRaisesRegex(SystemExit, 'role/schema'):
            self.load()
        self.value['role'] = 'alpha-owner'
        self.value['instances'] = []
        with self.assertRaisesRegex(SystemExit, 'inventory'):
            self.load()

    def test_missing_sidecar_cannot_be_successful_capture(self):
        with self.assertRaisesRegex(SystemExit, 'missing or invalid'):
            REPORT.load_attribution(self.args, self.alerts)

    def test_unknown_attribution_remains_explicit_and_high_blocking(self):
        self.value['instances'][0].update(status='message-unavailable', locations=[])
        self.assertEqual('message-unavailable', self.load()['instances'][0]['status'])
        with self.assertRaises(SystemExit):
            REPORT.enforce_blocking_policy(1, {'High': 1})

    def test_clean_scan_requires_an_empty_complete_inventory(self):
        self.alerts = []
        self.value['instances'] = []
        self.assertEqual('captured', self.load()['status'])

    def test_query_bearing_trace_attribution_does_not_authorize_suppression(self):
        self.args.contract.write_text(json.dumps({'properties': {'traceId': {}}}))
        self.value['instances'][0]['locations'] = [
            {'schemaPath': ['traceId'], 'valueCategory': 'text', 'matchKind': 'substring'}]
        raw = [{'alerts': [{'pluginid': '10062', 'riskcode': '3', 'count': '1', 'instances': [
            {'uri': 'http://app:8080/api/admin/invites?pageSize=50&page=1', 'method': 'GET'}]}]}]
        risks, _, _, self.alerts = REPORT.reduce_alerts(raw, 'http://app:8080')
        self.assertEqual(['page', 'pageSize'], self.alerts[0]['instances'][0]['queryParameterNames'])
        self.assertEqual(['traceId'], self.load()['instances'][0]['locations'][0]['schemaPath'])
        with self.assertRaisesRegex(SystemExit, 'High'):
            REPORT.enforce_blocking_policy(0, risks)

    def test_other_high_rules_and_unrelated_pii_remain_blocking(self):
        for rule, uri in [('40012', '/api/admin/invites?page=1'), ('10062', '/api/tasks?page=1&pageSize=50')]:
            with self.subTest(rule=rule, uri=uri):
                risks, _, _, _ = REPORT.reduce_alerts([{'alerts': [{'pluginid': rule, 'riskcode': '3',
                    'instances': [{'uri': 'http://app:8080' + uri, 'method': 'GET'}]}]}], 'http://app:8080')
                with self.assertRaisesRegex(SystemExit, 'High'):
                    REPORT.enforce_blocking_policy(0, risks)

    def add_sql_response(self):
        self.alerts.append({'ruleId': '40018', 'risk': 'High', 'instanceCount': 1})
        self.value['sqlResponses'] = [{'alertId': 8, 'ruleId': '40018', 'risk': 'High', 'status': 'captured',
            'responseStatusCode': 404, 'mediaType': 'json', 'bodyClassification': 'endpoint-not-found-envelope'}]

    def test_finite_sql_response_retained_without_changing_pii_and_high_blocking(self):
        original_pii = copy.deepcopy(self.value['instances'])
        self.add_sql_response()
        result = self.load()
        self.assertEqual(original_pii, result['instances'])
        self.assertEqual(self.value['sqlResponses'], result['sqlResponses']['instances'])
        for exit_code in [0, 1, 124]:
            with self.assertRaisesRegex(SystemExit, 'High'):
                REPORT.enforce_blocking_policy(exit_code, {'High': 1})

    def test_sql_diagnostic_inventory_is_complete_bounded_and_rule_specific(self):
        self.add_sql_response()
        self.alerts[-1]['instanceCount'] = 2
        with self.assertRaisesRegex(SystemExit, 'inventory'):
            self.load()
        self.value['sqlResponses'].append(copy.deepcopy(self.value['sqlResponses'][0]))
        with self.assertRaisesRegex(SystemExit, 'identity'):
            self.load()
        self.value['sqlResponses'] = [dict(self.value['sqlResponses'][0], alertId=i) for i in range(501)]
        self.alerts[-1]['instanceCount'] = 501
        with self.assertRaisesRegex(SystemExit, 'inventory'):
            self.load()

    def test_hostile_sql_fields_and_classifications_are_rejected_without_echoing_values(self):
        self.add_sql_response()
        original = copy.deepcopy(self.value)
        attacks = {'attack': 'secret', 'body': 'secret', 'header': 'secret', 'evidence': 'secret',
                   'traceId': 'secret', 'scalarHash': 'secret', 'url': 'https://private.invalid/secret'}
        for key, value in attacks.items():
            with self.subTest(key=key):
                self.value = copy.deepcopy(original)
                self.value['sqlResponses'][0][key] = value
                with self.assertRaisesRegex(SystemExit, 'unsafe') as caught:
                    self.load()
                self.assertNotIn('secret', str(caught.exception))
        for key in ['status', 'mediaType', 'bodyClassification']:
            self.value = copy.deepcopy(original)
            self.value['sqlResponses'][0][key] = 'private-email@example.invalid'
            with self.assertRaisesRegex(SystemExit, 'unsafe'):
                self.load()

    def test_sql_status_types_identity_and_unavailable_consistency_are_closed(self):
        self.add_sql_response()
        original = copy.deepcopy(self.value)
        invalid = [('responseStatusCode', True), ('responseStatusCode', '404'), ('responseStatusCode', 600),
                   ('responseStatusCode', 99), ('responseStatusCode', None), ('alertId', True),
                   ('alertId', -1), ('alertId', 2147483648), ('ruleId', '10062'), ('risk', 'Medium'),
                   ('alertId', 7), ('mediaType', 'html')]
        for key, value in invalid:
            with self.subTest(key=key, value=value):
                self.value = copy.deepcopy(original)
                self.value['sqlResponses'][0][key] = value
                with self.assertRaises(SystemExit):
                    self.load()
        for status in ['message-unavailable', 'response-header-unavailable']:
            self.value = copy.deepcopy(original)
            self.value['sqlResponses'][0].update(status=status, responseStatusCode=None,
                mediaType='unavailable', bodyClassification='body-unavailable')
            self.assertEqual(status, self.load()['sqlResponses']['instances'][0]['status'])
            self.value['sqlResponses'][0]['responseStatusCode'] = 404
            with self.assertRaisesRegex(SystemExit, 'inconsistent'):
                self.load()

    def test_duplicate_json_fields_old_schema_and_oversized_sidecars_fail_closed(self):
        self.args.attribution_report.write_text('{"schemaVersion":2,"schemaVersion":2}')
        with self.assertRaisesRegex(SystemExit, 'duplicate'):
            REPORT.load_attribution(self.args, self.alerts)
        self.value['schemaVersion'] = 1
        with self.assertRaisesRegex(SystemExit, 'role/schema'):
            self.load()
        self.args.attribution_report.write_text('x' * (2 * 1024 * 1024 + 1))
        with self.assertRaisesRegex(SystemExit, 'size limit'):
            REPORT.load_attribution(self.args, self.alerts)

    def test_sql_high_leaves_only_sanitized_finite_evidence_and_remains_blocked(self):
        self.add_sql_response()
        self.value['instances'] = []
        self.alerts = self.alerts[-1:]
        self.args.attribution_report.write_text(json.dumps(self.value))
        root = self.args.contract.parent
        args = SimpleNamespace(**vars(self.args), scanner_version='synthetic', scanner_image='synthetic',
            scanner_exit=1, automation_plan=self.args.contract, policy=self.args.contract,
            addon_list_sha256='a' * 64, output=root / 'safe.json', metadata=root / 'metadata.json')
        evidence = REPORT.build_evidence(args, 'http://app:8080', {'High': 1}, {'40018': 1}, {'40018': 1},
            self.alerts, ['synthetic-secret', 'synthetic-private-trace'], False)
        REPORT.write_evidence(args, evidence, {'High': 1}, ['synthetic-secret', 'synthetic-private-trace'])
        with self.assertRaisesRegex(SystemExit, 'High findings are blocking'):
            REPORT.enforce_blocking_policy(args.scanner_exit, {'High': 1})
        retained = args.output.read_text(encoding='utf-8')
        self.assertNotIn('synthetic-secret', retained)
        self.assertNotIn('synthetic-private-trace', retained)
        self.assertEqual(1, json.loads(retained)['summary']['blockingHighAlerts'])
        self.assertEqual('blocked-high', json.loads(args.metadata.read_text(encoding='utf-8'))['status'])


class Sec16ReportBoundaryTests(unittest.TestCase):
    """Exercise existing SEC-06 processing, without implying SEC-08 completion."""

    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.raw = self.root / 'raw.json'
        self.output = self.root / 'safe.json'
        self.metadata = self.root / 'metadata.json'
        self.contract = self.root / 'contract.json'
        self.contract.write_text('{}', encoding='utf-8')
        self.secret = 'synthetic-sec16-post-body-token'
        self.value = {'site': [{'@name': 'http://app:8080', 'alerts': [{
            'pluginid': '10021', 'name': 'Synthetic File route finding',
            'riskcode': '2', 'confidence': '2', 'count': '2', 'instances': [
                {'uri': 'http://app:8080/api/file-download-grants/{grantId}/download?token=' + self.secret,
                 'method': 'POST', 'param': 'token', 'evidence': self.secret,
                 'request-header': 'Cookie: ' + self.secret, 'response-body': 'synthetic-private-file-body'},
                {'uri': 'http://app:8080/api/attachment-download-grants/{grantId}/download',
                 'method': 'POST', 'param': 'token', 'evidence': 'synthetic-private-storage-key'}]}]}]}

    def run_processor(self, scanner_exit=0, write_report=True):
        if write_report:
            self.raw.write_text(json.dumps(self.value), encoding='utf-8')
        environment = {**os.environ, REPORT.FORBIDDEN_VALUES_ENV: json.dumps([
            self.secret, 'synthetic-private-file-body', 'synthetic-private-storage-key'])}
        environment.pop(REPORT.ALLOW_UNSANITIZED_ENV, None)
        return subprocess.run([sys.executable, str(ROOT / 'scripts/security/process-zap-report.py'),
            '--raw-report', str(self.raw), '--output', str(self.output), '--metadata', str(self.metadata),
            '--role', 'alpha-member', '--target', 'http://app:8080', '--scanner-version', 'synthetic',
            '--scanner-image', 'synthetic', '--scanner-exit', str(scanner_exit),
            '--contract', str(self.contract), '--automation-plan', str(self.contract),
            '--policy', str(self.contract), '--addon-list-sha256', 'a' * 64],
            env=environment, capture_output=True, text=True, timeout=30, check=False)

    def assert_safe(self, result):
        retained = self.output.read_text(encoding='utf-8')
        metadata = self.metadata.read_text(encoding='utf-8')
        for marker in (self.secret, 'synthetic-private-file-body', 'synthetic-private-storage-key'):
            self.assertNotIn(marker, retained)
            self.assertNotIn(marker, metadata)
            self.assertNotIn(marker, result.stdout + result.stderr)
        return json.loads(retained)

    def assert_file_routes(self, evidence):
        instances = evidence['alerts'][0]['instances']
        self.assertEqual([
            '/api/file-download-grants/{grantId}/download',
            '/api/attachment-download-grants/{grantId}/download'], [item['path'] for item in instances])
        self.assertEqual(['POST', 'POST'], [item['method'] for item in instances])
        self.assertEqual(['token', 'token'], [item['parameter'] for item in instances])

    def test_file_route_high_blocks_after_retaining_only_safe_metadata(self):
        self.value['site'][0]['alerts'][0]['riskcode'] = '3'
        result = self.run_processor()
        self.assertNotEqual(0, result.returncode)
        self.assertIn('High-risk alert type(s) are blocking', result.stderr)
        evidence = self.assert_safe(result)
        self.assertEqual(1, evidence['summary']['blockingHighAlerts'])
        self.assert_file_routes(evidence)
        self.assertEqual('blocked-high', json.loads(self.metadata.read_text(encoding='utf-8'))['status'])

    def test_medium_is_visible_under_current_report_only_policy_not_a_sec08_ratchet(self):
        result = self.run_processor()
        self.assertEqual(0, result.returncode, result.stderr)
        evidence = self.assert_safe(result)
        self.assert_file_routes(evidence)
        self.assertEqual(1, evidence['summary']['uniqueAlertsByRisk']['Medium'])
        self.assertEqual(['token'], evidence['alerts'][0]['instances'][0]['queryParameterNames'])
        self.assertEqual('POST', evidence['alerts'][0]['instances'][0]['method'])
        self.assertEqual('passed', json.loads(self.metadata.read_text(encoding='utf-8'))['status'])

    def test_scanner_timeout_cannot_pass_as_zero_findings(self):
        self.value['site'][0]['alerts'] = []
        result = self.run_processor(scanner_exit=124)
        self.assertNotEqual(0, result.returncode)
        self.assertIn('ZAP failure/timeout cannot be green', result.stderr)
        self.assertEqual(0, self.assert_safe(result)['summary']['blockingHighAlerts'])
        self.assertEqual('scanner-failed', json.loads(self.metadata.read_text(encoding='utf-8'))['status'])

    def test_missing_report_cannot_pass_as_empty_file_findings(self):
        result = self.run_processor(write_report=False)
        self.assertNotEqual(0, result.returncode)
        self.assertIn('without a non-empty JSON report', result.stderr)
        self.assertFalse(self.output.exists())
        self.assertFalse(self.metadata.exists())

    def test_secret_bearing_retained_parameter_fails_before_publishing_evidence(self):
        self.value['site'][0]['alerts'][0]['instances'][0]['param'] = self.secret
        result = self.run_processor()
        self.assertNotEqual(0, result.returncode)
        self.assertIn('sanitized evidence still contains ephemeral authentication material', result.stderr)
        self.assertNotIn(self.secret, result.stdout + result.stderr)
        self.assertFalse(self.output.exists())
        self.assertFalse(self.metadata.exists())


if __name__ == '__main__':
    unittest.main()
