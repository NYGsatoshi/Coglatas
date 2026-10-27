"""Synthetic controls for an unadopted proposal; no native execution claim."""

import copy
import hashlib
import json
from pathlib import Path
import sys
import unittest

SCRIPTS = Path(__file__).resolve().parents[2] / 'scripts' / 'security'
sys.path.insert(0, str(SCRIPTS))
try:
    import finding_policy as common
    import sec17_realtime_finding_candidate as candidate
finally:
    sys.path.pop(0)


class RealtimeFindingCandidateTests(unittest.TestCase):
    def input(self):
        return {'schemaVersion': 1, 'case': candidate.CASE, 'sourceSha': '1' * 40,
                'executionStatus': 'COMPLETE', 'positiveControls': 'CONFIRMED',
                'observations': [{'category': category, 'observation': 'EXCLUDED'}
                                 for category in candidate.CATEGORIES]}

    def test_complete_input_is_never_policy_acceptance_or_zero_finding_pass(self):
        result = candidate.inspect_candidate(self.input())
        self.assertEqual('CANDIDATE_NOT_ADOPTED', result['status'])
        self.assertEqual('DECLARED_COMPLETE_NO_OBSERVED_DELIVERY', result['executionState'])
        self.assertFalse(result['liveGateQualified'])
        self.assertFalse(result['producerVerified'])
        for field in ('severity', 'normalizedFinding', 'policyDecision'):
            self.assertIsNone(result[field])

    def test_finite_observed_delivery_proposes_only_a_scope_not_severity_or_finding(self):
        value = self.input()
        value['observations'][0]['observation'] = 'DELIVERED'
        result = candidate.inspect_candidate(value)
        self.assertEqual('DECLARED_COMPLETE_OBSERVED_DELIVERY', result['executionState'])
        self.assertEqual(1, len(result['proposals']))
        self.assertEqual({'category', 'proposedScope', 'proposedFingerprint'}, set(result['proposals'][0]))
        self.assertEqual({'kind', 'role', 'hub', 'eventType', 'resourceClass', 'phase', 'receiver'},
                         set(result['proposals'][0]['proposedScope']))
        self.assertIsNone(result['normalizedFinding'])
        self.assertIsNone(result['severity'])

    def test_missing_liveness_missing_oracle_and_native_failure_remain_execution_failure(self):
        mutations = [lambda v: v.update(positiveControls='UNCONFIRMED'),
                     lambda v: v['observations'].pop(),
                     lambda v: v.update(executionStatus='EXECUTION_FAILURE'),
                     lambda v: v.update(executionStatus='COVERAGE_INSUFFICIENT')]
        for mutate in mutations:
            with self.subTest(mutation=mutations.index(mutate)):
                value = self.input()
                mutate(value)
                result = candidate.inspect_candidate(value)
                self.assertEqual('EXECUTION_FAILURE', result['executionState'])
                self.assertIsNone(result['normalizedFinding'])
                self.assertIsNone(result['policyDecision'])

    def test_partial_delivery_observation_survives_later_execution_failure_without_promotion(self):
        value = self.input()
        value['observations'][0]['observation'] = 'DELIVERED'
        value['executionStatus'] = 'EXECUTION_FAILURE'
        result = candidate.inspect_candidate(value)
        self.assertEqual('EXECUTION_FAILURE', result['executionState'])
        self.assertEqual(1, len(result['proposals']))
        self.assertFalse(result['producerVerified'])
        self.assertIsNone(result['normalizedFinding'])

    def test_unknown_raw_fields_categories_and_duplicate_observations_are_rejected_safely(self):
        canary = 'private-input-canary'
        cases = []
        value = self.input()
        value[canary] = canary
        cases.append(value)
        value = self.input()
        value['observations'][0]['rawBody'] = canary
        cases.append(value)
        value = self.input()
        value['observations'][0]['category'] = canary
        cases.append(value)
        value = self.input()
        value['observations'][1] = copy.deepcopy(value['observations'][0])
        cases.append(value)
        for value in cases:
            with self.assertRaises(common.PolicyError) as caught:
                candidate.inspect_candidate(value)
            self.assertNotIn(canary, str(caught.exception))

    def test_non_scalar_execution_and_observation_values_have_fixed_rejections(self):
        for field in ('executionStatus', 'positiveControls'):
            value = self.input()
            value[field] = {'private-canary': []}
            with self.assertRaises(common.PolicyError) as caught:
                candidate.inspect_candidate(value)
            self.assertNotIn('private-canary', str(caught.exception))
        value = self.input()
        value['observations'][0]['observation'] = ['private-canary']
        with self.assertRaises(common.PolicyError):
            candidate.inspect_candidate(value)

    def test_candidate_identity_stable_across_source_and_order_but_distinct_for_each_scope(self):
        value = self.input()
        for row in value['observations']:
            row['observation'] = 'DELIVERED'
        first = candidate.inspect_candidate(value)['proposals']
        value['sourceSha'] = '2' * 40
        value['observations'].reverse()
        self.assertEqual(first, candidate.inspect_candidate(value)['proposals'])
        self.assertEqual(4, len({row['proposedFingerprint'] for row in first}))

    def test_candidate_fingerprint_is_not_existing_sec08_fingerprint(self):
        value = self.input()
        value['observations'][0]['observation'] = 'DELIVERED'
        row = candidate.inspect_candidate(value)['proposals'][0]
        identity = {'tool': 'security-integration', 'ruleId': 'SEC17.FilePolicy.DeliveryAfterPrivate',
                    'scope': row['proposedScope'], 'findingClass': 'authorization'}
        self.assertNotEqual(common.digest(identity, 'finding-fingerprint-v1'), row['proposedFingerprint'])

    def test_existing_http_v1_scope_fingerprint_and_normalization_are_unchanged(self):
        contract = {'paths': {'/api/files/{id}': {'get': {
            'parameters': [{'in': 'path', 'name': 'id'}]}}}}
        scope = {'role': 'alpha-restricted', 'method': 'GET', 'routeClass': '/api/files/{id}',
                 'parameterCategory': 'path', 'parameterName': 'id'}
        finding = {'ruleId': 'synthetic.authorization', 'severity': 'Informational',
                   'confidence': 'confirmed', 'findingClass': 'authorization', 'scope': scope}
        document = {'schemaVersion': 1, 'scanner': {'id': 'security-integration', 'version': '1.0.0'},
                    'sourceSha': '1' * 40, 'configSha256': 'a' * 64,
                    'execution': {'status': 'PASS_EXECUTED', 'exitCode': 0}, 'findings': [finding]}
        before = common.normalize_document(document, common.contract_operations(contract))
        candidate.inspect_candidate(self.input())
        after = common.normalize_document(document, common.contract_operations(contract))
        self.assertEqual(before, after)
        # Pin HTTP v1 identity bytes independently of common.canonical/digest.
        identity_bytes = (b'{"findingClass":"authorization","ruleId":"synthetic.authorization",'
                          b'"scope":{"method":"GET","parameterCategory":"path","parameterName":"id",'
                          b'"role":"alpha-restricted","routeClass":"/api/files/{id}"},'
                          b'"tool":"security-integration"}')
        expected = hashlib.sha256(b'finding-fingerprint-v1\x00' + identity_bytes).hexdigest()
        self.assertEqual(expected, after['findings'][0]['fingerprint'])
        policy = json.loads((SCRIPTS / 'finding-policy.json').read_text(encoding='utf8'))
        evaluated = common.evaluate(document, policy, common.contract_operations(contract), '2026-10-27T00:00:00Z')
        self.assertEqual('BLOCK_NON_SUPPRESSIBLE', evaluated['findings'][0]['suppressionMatch'])
        self.assertEqual('NOT_ACTIVATED', evaluated['activation'])
        self.assertFalse(evaluated['liveGateQualified'])
        self.assertEqual([], policy['exceptions'])


if __name__ == '__main__':
    unittest.main()
