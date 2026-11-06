"""Synthetic policy controls; run with the pinned #1755 adapter explicitly available."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import unittest

import playwright_applicability_reconcile as policy
import playwright_case_reconcile as original

def load_fixture():
    path = Path(original.__file__).with_name('test-playwright-case-reconcile.py')
    spec = importlib.util.spec_from_file_location('original_controls', path)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    source, native = module.fixture()
    payload = {'sourceSha': source['sourceSha'], 'suite': source['suite'], 'rosterSha256': source['rosterSha256'],
               'anchorSha': '3865b09a2d3ea3cb82f450c6d19ec643ffbae01f', 'sourceManifestSha256': 'f' * 64,
               'tests': [{'id': row['id'], 'project': 'CHROMIUM_DESKTOP',
                          'classification': 'RUN' if row['status'] == 'passed' else 'DECLARATION_SKIP',
                          'bindingSha256': 'b' * 64} for row in source['tests']]}
    payload['tests'][0]['classification'] = 'CONDITIONAL_SKIP'
    native['tests'][0].update(expectedStatus='skipped', outcome='skipped', attempts=[{'retry': 0, 'status': 'skipped'}])
    sidecar = {'schema': 'coglatas.playwright-source-applicability.v1', **payload,
               'applicabilitySha256': hashlib.sha256(json.dumps(payload, separators=(',', ':')).encode()).hexdigest(),
               'artifactTrust': 'EXTERNAL_VERIFICATION_REQUIRED', 'requiredCheckQualified': False,
               'qualification': 'AST_SOURCE_APPLICABILITY_ONLY_NOT_EXECUTION'}
    return source, native, sidecar, module.IDENTITY

def compare(source, native, sidecar, identity, expected_digest=None):
    return policy.reconcile(json.dumps(source).encode(), json.dumps(native).encode(), json.dumps(sidecar).encode(),
                            **identity, applicability_sha256=expected_digest or sidecar['applicabilitySha256'])

def seal(sidecar):
    payload = {key: sidecar[key] for key in ['sourceSha', 'suite', 'rosterSha256', 'anchorSha', 'sourceManifestSha256', 'tests']}
    sidecar['applicabilitySha256'] = hashlib.sha256(json.dumps(payload, separators=(',', ':')).encode()).hexdigest()

class PolicyControls(unittest.TestCase):
    def test_conditional_content_preserves_original_rejection_and_native_records(self):
        source, native, sidecar, identity = load_fixture()
        result = compare(source, native, sidecar, identity)
        self.assertEqual('CONTENT_MATCH', result['decision'])
        self.assertEqual('REJECTED', result['originalReconciliation']['decision'])
        self.assertEqual(1, result['counts']['conditionalSkips'])
        self.assertEqual(1, result['counts']['declarationSkips'])
        self.assertEqual(0, result['counts']['unauthorizedSkips'])
        self.assertEqual(native['tests'], result['originalReconciliation']['cases'])
        self.assertFalse(result['states']['ARTIFACT_PROVENANCE_VERIFIED'])
        self.assertFalse(result['states']['REQUIRED_GATE_QUALIFIED'])

    def test_unrelated_or_intended_execution_project_skip_and_missing_results_reject(self):
        for mode in ['run-skipped', 'conditional-passed', 'missing', 'duplicate', 'extra', 'retry', 'failed', 'interrupted']:
            source, native, sidecar, identity = load_fixture()
            if mode == 'run-skipped':
                sidecar['tests'][0]['classification'] = 'RUN'
                # A valid independently expected RUN sidecar never permits a runtime skip.
                seal(sidecar)
                result = compare(source, native, sidecar, identity)
                self.assertEqual('REJECTED', result['decision'])
                self.assertEqual(1, result['counts']['unauthorizedSkips'])
                continue
            if mode == 'conditional-passed': native['tests'][0].update(expectedStatus='passed', outcome='expected', attempts=[{'retry': 0, 'status': 'passed'}])
            if mode == 'missing': native['tests'][0]['attempts'] = []
            if mode == 'duplicate': native['tests'].append(copy.deepcopy(native['tests'][0]))
            if mode == 'extra': native['tests'][0]['id'] = 'c' * 64
            if mode == 'retry': native['tests'][0]['attempts'].append({'retry': 1, 'status': 'skipped'})
            if mode == 'failed': native['tests'][0]['attempts'][0]['status'] = 'failed'
            if mode == 'interrupted': native['status'] = 'interrupted'
            self.assertEqual('REJECTED', compare(source, native, sidecar, identity)['decision'])

    def test_sha_identity_and_independent_sidecar_digest_mismatch_refuse(self):
        for mode in ['sha', 'run', 'attempt', 'digest']:
            source, native, sidecar, identity = load_fixture()
            if mode == 'sha': sidecar['sourceSha'] = 'b' * 40
            if mode == 'run': native['runId'] = 'local-' + 'f' * 32
            if mode == 'attempt': native['runAttempt'] = 2
            with self.assertRaises(original.InvalidCaseEvidence):
                compare(source, native, sidecar, identity, 'e' * 64 if mode == 'digest' else None)

    def test_sidecar_missing_duplicated_malformed_unknown_and_incomplete_refuse(self):
        for mode in ['missing', 'duplicate', 'field', 'schema', 'project', 'binding', 'empty']:
            source, native, sidecar, identity = load_fixture()
            if mode == 'missing': sidecar['tests'].pop()
            if mode == 'duplicate': sidecar['tests'][1] = sidecar['tests'][0]
            if mode == 'field': sidecar['PRIVATE_BODY'] = 'PRIVATE_CANARY'
            if mode == 'schema': sidecar['schema'] = 'unsupported'
            if mode == 'project': sidecar['tests'][0]['project'] = 'UNKNOWN'
            if mode == 'binding': sidecar['tests'][0]['bindingSha256'] = None
            if mode == 'empty': sidecar['tests'] = []
            if mode in {'missing', 'duplicate', 'project', 'binding', 'empty'}: seal(sidecar)
            codes = {'missing': 'APPLICABILITY_COUNT', 'empty': 'APPLICABILITY_COUNT',
                     'duplicate': 'APPLICABILITY_CASE_ID', 'project': 'APPLICABILITY_CASE_SCHEMA',
                     'binding': 'APPLICABILITY_CASE_SCHEMA', 'field': 'APPLICABILITY_FIELDS',
                     'schema': 'APPLICABILITY_IDENTITY'}
            with self.assertRaisesRegex(original.InvalidCaseEvidence, '^' + codes[mode] + '$'):
                compare(source, native, sidecar, identity)

    def test_malformed_oversized_duplicate_json_and_empty_evidence_refuse(self):
        source, native, sidecar, identity = load_fixture()
        for raw in [b'', b'{', b'\xff', b'{"x":1,"x":1}', b'{"x":NaN}', b' ' * (original.MAX_BYTES + 1)]:
            with self.assertRaises(original.InvalidCaseEvidence):
                policy.reconcile(json.dumps(source).encode(), json.dumps(native).encode(), raw,
                                 **identity, applicability_sha256=sidecar['applicabilitySha256'])

if __name__ == '__main__': unittest.main()
