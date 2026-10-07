import copy
import importlib.util
import json
from pathlib import Path
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
        self.value = {'schemaVersion': 1, 'role': 'alpha-owner', 'scopeRuleId': '10062', 'instances': [
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


if __name__ == '__main__':
    unittest.main()
