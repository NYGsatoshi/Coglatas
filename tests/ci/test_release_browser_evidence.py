from __future__ import annotations

import argparse
import contextlib
import hashlib
import io
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts/ci'))
import sec14_browser_evidence as evidence

SHA = 'a' * 40
RUN = 'local:' + '1' * 32
NOW = '2026-10-10T01:30:00Z'


class BrowserEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.context = {
            'schema': 'sec14-browser-execution-v1', 'candidateSha': SHA, 'runIdentity': RUN,
            'scope': 'TEST_OWNED_LOOPBACK_FIXTURE_ONLY', 'mode': 'normal',
            'startedAtUtc': '2026-10-10T01:00:00Z', 'completedAtUtc': '2026-10-10T01:01:00Z',
            'sourceSha256': evidence.source_hashes(ROOT), 'scannerImage': evidence.IMAGE,
            'scannerVersion': '2.17.0', 'processExit': 0, 'sourceState': 'DEVELOPMENT',
            'network': 'none', 'platform': 'linux/amd64', 'nativeSha256': {},
        }
        self.native = {'@programName': 'ZAP', '@version': '2.17.0', 'created': '2026-10-10T01:00:40Z',
                       'site': [{'@name': evidence.ORIGIN, 'alerts': [{'pluginid': '10038', 'riskcode': '2', 'count': '2'}]}]}
        self.counters = {'schema': 'sec14-browser-fixture-counters-v1',
                         'scope': 'TEST_OWNED_LOOPBACK_FIXTURE_ONLY', 'weakened': False,
                         'counters': {'root': 2, 'ajaxDiscovery': 3, 'authorized200': 2,
                                      'crossScope403': 2, 'unauthorized200': 0, 'other': 0}}
        self.args = argparse.Namespace(expected_candidate_sha=SHA, expected_run_identity=RUN,
                                      raw_directory=str(self.root), output=str(self.root / 'safe.json'),
                                      verify_report=None, now=NOW)
        self.write_all()

    def tearDown(self):
        self.temporary.cleanup()

    def write_all(self):
        for name, document in (('browser-native-v1.json', self.native), ('browser-counters.json', self.counters)):
            (self.root / name).write_text(json.dumps(document), encoding='utf-8')
        self.context['nativeSha256'] = {key: hashlib.sha256((self.root / name).read_bytes()).hexdigest()
                                       for key, name in (('report', 'browser-native-v1.json'), ('counters', 'browser-counters.json'))}
        (self.root / 'context.json').write_text(json.dumps(self.context), encoding='utf-8')

    def report(self):
        self.write_all()
        return evidence.collect(self.args)

    def reject(self, code):
        self.write_all()
        with self.assertRaisesRegex(evidence.EvidenceError, '^' + code + '$'):
            evidence.collect(self.args)

    def run_command(self):
        with contextlib.redirect_stdout(io.StringIO()):
            return evidence.command(self.args)

    def test_positive_native_counters_and_medium_findings_preserve_unqualified_scope(self):
        report = self.report()
        self.assertEqual(report['toolingOutcome'], 'MATCHED')
        self.assertEqual(report['nativeRules']['10038'], {'riskCode': 2, 'instanceCount': 2})
        self.assertEqual(report['productImageBinding'], 'UNVERIFIED')
        self.assertEqual(report['canonicalNormativeCoverage'], 'UNVERIFIED')
        self.assertEqual(report['releaseAcceptance'], 'BLOCKED')
        self.assertFalse(report['rawArtifactUploadAllowed'])

    def test_wrong_candidate(self):
        self.args.expected_candidate_sha = 'b' * 40
        self.reject('CANDIDATE_MISMATCH')

    def test_wrong_run(self):
        self.args.expected_run_identity = 'local:' + '2' * 32
        self.reject('RUN_IDENTITY_MISMATCH')

    def test_product_or_owner_scope_cannot_be_self_declared(self):
        self.context['personalOwnerApproval'] = 'APPROVED'
        self.reject('EXECUTION_CONTEXT_SCHEMA_INVALID')

    def test_product_target_is_outside_this_fixture_adapter(self):
        self.context['scope'] = 'PRODUCT_VERIFIED'
        self.reject('FIXTURE_SCOPE_INVALID')

    def test_changed_fixture_source_bytes(self):
        self.context['sourceSha256'][evidence.SOURCE_FILES[0]] = '0' * 64
        self.reject('EXECUTED_SOURCE_BYTES_MISMATCH')

    def test_wrong_tool(self):
        self.context['scannerImage'] = evidence.IMAGE.replace('781a', 'ffff', 1)
        self.reject('SCANNER_IDENTITY_MISMATCH')

    def test_native_wrong_version(self):
        self.native['@version'] = '2.16.0'
        self.reject('NATIVE_SCANNER_VERSION_MISMATCH')

    def test_replay_native_report_into_current_execution(self):
        self.native['created'] = '2026-10-09T01:00:40Z'
        self.reject('NATIVE_REPORT_TIME_MISMATCH')

    def test_stale_execution(self):
        self.args.now = '2026-10-10T05:00:00Z'
        self.reject('STALE_OR_INVALID_EXECUTION_WINDOW')

    def test_future_execution(self):
        self.context['completedAtUtc'] = '2026-10-10T02:00:00Z'
        self.reject('STALE_OR_INVALID_EXECUTION_WINDOW')

    def test_report_hash_replay_detected(self):
        self.write_all()
        (self.root / 'browser-native-v1.json').write_text(json.dumps(self.native) + ' ', encoding='utf-8')
        with self.assertRaisesRegex(evidence.EvidenceError, 'EXECUTED_NATIVE_BYTES_MISMATCH'):
            evidence.collect(self.args)

    def test_scanner_nonzero_and_timeout_cannot_pass(self):
        for code in (1, 124, -9):
            with self.subTest(code=code):
                self.context['processExit'] = code
                report = self.report()
                self.assertEqual(report['toolingOutcome'], 'FAILED')
                self.assertFalse(report['conditions']['scannerProcessSucceeded'])

    def test_no_ajax_discovery_cannot_pass(self):
        self.counters['counters']['ajaxDiscovery'] = 0
        self.assertEqual(self.report()['toolingOutcome'], 'FAILED')

    def test_negative_without_authorized_positive_cannot_pass(self):
        self.counters['counters']['authorized200'] = 0
        report = self.report()
        self.assertEqual(report['toolingOutcome'], 'FAILED')
        self.assertFalse(report['conditions']['crossScopeDenialAfterPositive'])

    def test_unauthorized_fixture_response_detects_weakened_semantics(self):
        self.context['mode'] = 'weakened'
        self.counters['weakened'] = True
        self.counters['counters'].update(unauthorized200=2, crossScope403=0)
        report = self.report()
        self.assertEqual(report['toolingOutcome'], 'FAILED')
        self.assertFalse(report['conditions']['noUnauthorizedFixtureResponse'])

    def test_disabled_browser_cannot_qualify_preexisting_positive_counters(self):
        self.context['mode'] = 'disabled'
        report = self.report()
        self.assertEqual(report['toolingOutcome'], 'FAILED')
        self.assertFalse(report['conditions']['browserVerifierEnabled'])

    def test_missing_scanned_site_cannot_be_zero_findings(self):
        self.native['site'] = []
        self.reject('NATIVE_SCANNED_SITE_MISSING_OR_INVALID')

    def test_another_origin_is_rejected(self):
        self.native['site'][0]['@name'] = 'https://outside.invalid'
        self.reject('NATIVE_TARGET_MISMATCH')

    def test_high_native_rule_is_visible_failure(self):
        self.native['site'][0]['alerts'][0]['riskcode'] = '3'
        report = self.report()
        self.assertEqual(report['toolingOutcome'], 'FAILED')
        self.assertEqual(report['nativeRules']['10038']['riskCode'], 3)

    def test_sensitive_report_fields_are_never_retained(self):
        marker = 'PRIVATE_FIXTURE_CANARY_DO_NOT_RETAIN'
        self.native.update(requestBody=marker, cookie=marker, token=marker)
        self.native['site'][0]['alerts'][0].update(evidence=marker, name=marker, param=marker,
                                                   instances=[{'requestBody': marker, 'responseBody': marker}])
        self.assertNotIn(marker, evidence.rendered(self.report()).decode('utf-8'))

    def test_secret_in_rule_identity_cannot_enter_output(self):
        self.native['site'][0]['alerts'][0]['pluginid'] = 'PRIVATE_SECRET'
        self.reject('NATIVE_RULE_CLASSIFICATION_INVALID')

    def test_unknown_counter_fields_rejected(self):
        self.counters['counters']['protectedBody'] = 'private-canary'
        self.reject('FIXTURE_COUNTER_SCOPE_INVALID')

    def test_booleans_cannot_fake_positive_counts(self):
        self.counters['counters']['authorized200'] = True
        self.reject('FIXTURE_COUNTER_SCOPE_INVALID')

    def test_missing_input_creates_sanitized_error_receipt(self):
        (self.root / 'browser-counters.json').unlink()
        self.assertEqual(self.run_command(), 1)
        report = json.loads(Path(self.args.output).read_bytes())
        self.assertEqual(report['integrityStatus'], 'ERROR')
        self.assertEqual(report['candidateSha'], SHA)
        self.assertEqual(report['runIdentity'], RUN)
        self.assertFalse(report['rawArtifactUploadAllowed'])

    def test_invalid_expected_identity_is_not_reflected_into_error_receipt(self):
        self.args.expected_candidate_sha = 'PRIVATE_CANDIDATE_CANARY'
        self.args.expected_run_identity = 'PRIVATE_RUN_CANARY'
        self.assertEqual(self.run_command(), 1)
        report = Path(self.args.output).read_bytes()
        self.assertNotIn(b'PRIVATE_', report)
        parsed = json.loads(report)
        self.assertEqual(parsed['candidateSha'], 'UNVERIFIED')
        self.assertEqual(parsed['runIdentity'], 'UNVERIFIED')

    def test_exclusive_output_does_not_rewrite_original(self):
        self.run_command()
        original = Path(self.args.output).read_bytes()
        with self.assertRaises(FileExistsError):
            self.run_command()
        self.assertEqual(Path(self.args.output).read_bytes(), original)

    def test_read_only_consumer_positive_and_forged_approval_rejection(self):
        self.run_command()
        self.args.verify_report = self.args.output
        original = Path(self.args.output).read_bytes()
        self.assertEqual(self.run_command(), 0)
        self.assertEqual(Path(self.args.output).read_bytes(), original)
        forged = json.loads(original)
        forged['personalOwnerApproval'] = 'APPROVED'
        Path(self.args.output).write_text(json.dumps(forged), encoding='utf-8')
        with self.assertRaisesRegex(evidence.EvidenceError, 'RETAINED_REPORT_MISMATCH'):
            self.run_command()

    def test_json_overflow_duplicate_and_nonfinite_rejected(self):
        path = self.root / 'invalid.json'
        for raw in (b'{"x":1,"x":2}', b'{"x":NaN}', b'{"x":Infinity}',
                    b'{"x":' + b'[' * 129 + b'0' + b']' * 129 + b'}'):
            with self.subTest(raw=raw[:30]):
                path.write_bytes(raw)
                with self.assertRaisesRegex(evidence.EvidenceError, 'NATIVE_INPUT_MISSING_OR_INVALID'):
                    evidence.read(path)
        path.write_bytes(b'{"x":"' + b'a' * 100 + b'"}')
        with patch.object(evidence.release, 'MAX_JSON_BYTES', 32):
            with self.assertRaisesRegex(evidence.EvidenceError, 'NATIVE_INPUT_MISSING_OR_INVALID'):
                evidence.read(path)

    @unittest.skipUnless(hasattr(os, 'mkfifo'), 'POSIX FIFO control requires a POSIX runner')
    def test_nonregular_fifo_is_rejected_without_waiting_for_a_writer(self):
        path = self.root / 'fifo'
        os.mkfifo(path)
        with self.assertRaisesRegex(evidence.EvidenceError, 'NATIVE_INPUT_MISSING_OR_INVALID'):
            evidence.read(path)

    def test_source_read_limit_is_enforced_on_the_stream(self):
        with patch.object(evidence.release, 'MAX_JSON_BYTES', 32):
            with self.assertRaisesRegex(evidence.EvidenceError, 'SOURCE_INPUT_SIZE_LIMIT'):
                evidence.source_hashes(ROOT)


if __name__ == '__main__':
    unittest.main()
