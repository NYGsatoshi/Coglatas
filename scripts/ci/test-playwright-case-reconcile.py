"""Positive and adversarial contents; synthetic fixtures never grant native acceptance."""
import copy
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import unittest

import playwright_case_reconcile as guard

SHA = 'a' * 40
RUN = 'local-' + 'c' * 32
IDENTITY = dict(source_sha=SHA, execution_kind='LOCAL', run_id=RUN, attempt=1)


def source_digest(source):
    payload = {key: source[key] for key in ['sourceSha', 'suite', 'tests']}
    source['rosterSha256'] = hashlib.sha256(json.dumps(payload, sort_keys=True, separators=(',', ':')).encode()).hexdigest()


def fixture():
    source = {'schema': 'coglatas.playwright-source-discovery.v1', 'suite': 'angular-static',
              'sourceSha': SHA, 'tests': [{'id': '1' * 64, 'status': 'passed'}, {'id': '2' * 64, 'status': 'skipped'}],
              'rosterSha256': '', 'artifactTrust': 'EXTERNAL_VERIFICATION_REQUIRED',
              'requiredCheckQualified': False, 'qualification': 'SOURCE_LIST_ONLY_NOT_EXECUTION_PROOF'}
    source_digest(source)
    execution = {'schema': 'coglatas.playwright-case-execution.v1', 'suite': 'angular-static',
                 'sourceSha': SHA, 'executionKind': 'LOCAL', 'runId': RUN, 'runAttempt': 1,
                 'producer': 'playwright-testcase-events-v1', 'status': 'passed', 'captureComplete': True,
                 'globalErrors': 0, 'captureErrors': [], 'tests': [
                     {'id': row['id'], 'listed': True, 'declaredExpectedStatus': row['status'],
                      'expectedStatus': row['status'], 'outcome': 'skipped' if row['status'] == 'skipped' else 'expected',
                      'attempts': [{'retry': 0, 'status': row['status']}]} for row in source['tests']]}
    return source, execution


def compare(source, execution, **identity):
    return guard.reconcile(json.dumps(source).encode(), json.dumps(execution).encode(), **(IDENTITY | identity))


class CaseReconciliationTests(unittest.TestCase):
    def test_valid_contents_keep_qualification_boundaries_and_declared_skip(self):
        source, execution = fixture()
        result = compare(source, execution)
        self.assertEqual('CONTENT_MATCH', result['decision'])
        self.assertEqual(2, result['counts']['sourceCases'])
        self.assertEqual(2, result['counts']['resultCases'])
        self.assertEqual(1, result['counts']['executedCases'])
        self.assertEqual(1, result['counts']['skipped'])
        self.assertEqual(0, result['counts']['unauthorizedSkips'])
        self.assertTrue(result['states']['NATIVE_EXECUTION_RECONCILED'])
        self.assertFalse(result['states']['ARTIFACT_PROVENANCE_VERIFIED'])
        self.assertFalse(result['states']['REQUIRED_GATE_QUALIFIED'])
        self.assertEqual('COVERAGE_UNVERIFIED', result['coverageState'])

    def test_missing_duplicate_invalid_source_ids_and_wrong_digest_are_refused(self):
        for mode in ['missing', 'duplicate', 'invalid', 'digest', 'empty', 'order']:
            with self.subTest(mode=mode):
                source, execution = fixture()
                if mode == 'missing': del source['tests'][0]['id']
                if mode == 'duplicate': source['tests'].append(copy.deepcopy(source['tests'][0])); source_digest(source)
                if mode == 'invalid': source['tests'][0]['id'] = 'protected title'
                if mode == 'digest': source['rosterSha256'] = 'f' * 64
                if mode == 'empty': source['tests'] = []; source_digest(source)
                if mode == 'order': source['tests'].reverse(); source_digest(source)
                with self.assertRaises(guard.InvalidCaseEvidence): compare(source, execution)

    def test_extra_duplicate_missing_native_cases_preserve_nonzero_counts(self):
        for mode, field in [('extra', 'unexpected'), ('duplicate', 'duplicateNative'), ('missing', 'missing')]:
            with self.subTest(mode=mode):
                source, execution = fixture()
                if mode == 'extra':
                    row = copy.deepcopy(execution['tests'][0]); row['id'] = '3' * 64; execution['tests'].append(row)
                if mode == 'duplicate': execution['tests'].append(copy.deepcopy(execution['tests'][0]))
                if mode == 'missing': execution['tests'].pop()
                result = compare(source, execution)
                self.assertEqual('REJECTED', result['decision'])
                self.assertEqual(1, result['counts'][field])
                self.assertFalse(result['states']['NATIVE_EXECUTION_RECONCILED'])
                self.assertEqual(len(execution['tests']), len(result['cases']))

    def test_missing_result_partial_execution_and_incomplete_capture_are_rejected(self):
        for mode in ['no-result', 'partial', 'capture', 'global-error', 'list-only']:
            with self.subTest(mode=mode):
                source, execution = fixture()
                if mode in ['no-result', 'list-only']: execution['tests'][0]['attempts'] = []
                if mode == 'partial': execution['tests'] = execution['tests'][:1]
                if mode == 'capture': execution['captureComplete'] = False
                if mode == 'global-error': execution['globalErrors'] = 1
                result = compare(source, execution)
                self.assertEqual('REJECTED', result['decision'])

    def test_wrong_expected_status_and_unauthorized_runtime_skip_are_rejected(self):
        source, execution = fixture()
        row = execution['tests'][0]
        row.update(expectedStatus='skipped', outcome='skipped', attempts=[{'retry': 0, 'status': 'skipped'}])
        result = compare(source, execution)
        self.assertIn('EXPECTED_STATUS_MISMATCH', result['issues'])
        self.assertIn('UNAUTHORIZED_SKIP', result['issues'])
        self.assertEqual(1, result['counts']['unauthorizedSkips'])

    def test_failure_cannot_be_mislabeled_as_a_pass(self):
        source, execution = fixture()
        execution['tests'][0]['attempts'][0]['status'] = 'failed'
        # Contradictory native outcome/overall status must not override the failed attempt.
        result = compare(source, execution)
        self.assertEqual('REJECTED', result['decision'])
        self.assertEqual(1, result['counts']['failed'])
        self.assertEqual('failed', result['cases'][0]['attempts'][0]['status'])

    def test_retry_and_later_pass_never_erase_first_failure(self):
        for attempts in [[{'retry': 0, 'status': 'failed'}, {'retry': 1, 'status': 'passed'}],
                         [{'retry': 1, 'status': 'passed'}],
                         [{'retry': 0, 'status': 'passed'}, {'retry': 0, 'status': 'passed'}]]:
            source, execution = fixture()
            execution['tests'][0]['attempts'] = attempts
            result = compare(source, execution)
            self.assertEqual('REJECTED', result['decision'])
            self.assertEqual(1, result['counts']['retried'])
            self.assertEqual(attempts, result['cases'][0]['attempts'])

    def test_interrupted_cancelled_failed_and_timeout_runs_are_rejected(self):
        for status in ['interrupted', 'failed', 'timedout']:
            source, execution = fixture()
            execution['status'] = status
            result = compare(source, execution)
            self.assertEqual('REJECTED', result['decision'])
            self.assertIn('EXECUTION_NOT_PASSED', result['issues'])
        source, execution = fixture()
        execution['tests'][0]['attempts'][0]['status'] = 'interrupted'
        self.assertEqual(1, compare(source, execution)['counts']['interrupted'])
        execution['status'] = 'cancelled'
        with self.assertRaises(guard.InvalidCaseEvidence): compare(source, execution)

    def test_source_run_attempt_producer_and_schema_mismatches_are_refused(self):
        for field, value in [('sourceSha', 'b' * 40), ('runId', 'local-' + 'd' * 32),
                             ('runAttempt', 2), ('runAttempt', True), ('executionKind', 'GITHUB_ACTIONS'),
                             ('producer', 'another-producer'), ('schema', 'v999')]:
            with self.subTest(field=field):
                source, execution = fixture(); execution[field] = value
                with self.assertRaises(guard.InvalidCaseEvidence): compare(source, execution)
        source, execution = fixture(); source['sourceSha'] = 'b' * 40
        with self.assertRaises(guard.InvalidCaseEvidence): compare(source, execution)
        source, execution = fixture(); source['schema'] = 'unsupported'
        with self.assertRaises(guard.InvalidCaseEvidence): compare(source, execution)

    def test_malformed_oversized_duplicate_key_nonfinite_and_empty_inputs_are_refused(self):
        source, execution = fixture()
        for raw in [b'', b'{', b'\xff', b'{}' * (guard.MAX_BYTES // 2 + 1),
                    b'{"schema":1,"schema":1}', b'{"x":NaN}']:
            with self.subTest(size=len(raw)):
                with self.assertRaises(guard.InvalidCaseEvidence):
                    guard.reconcile(raw, json.dumps(execution).encode(), **IDENTITY)
                with self.assertRaises(guard.InvalidCaseEvidence):
                    guard.reconcile(json.dumps(source).encode(), raw, **IDENTITY)

    def test_deterministic_order_and_no_unapproved_fields(self):
        source, execution = fixture()
        result = compare(source, execution)
        execution['tests'].reverse()
        self.assertEqual(result, compare(source, execution))
        execution['privateBody'] = 'NEVER_PUBLISH_CANARY'
        with self.assertRaises(guard.InvalidCaseEvidence): compare(source, execution)

    def test_wrong_native_field_types_are_refused(self):
        for field in ['listed', 'declaredExpectedStatus', 'expectedStatus', 'outcome', 'attempts']:
            for value in [{}, [], 0, True]:
                source, execution = fixture()
                execution['tests'][0][field] = value
                # A true listed flag is already valid; an empty attempt list is a diagnostic rejection.
                if field == 'listed' and value is True: continue
                if field == 'attempts' and value == []:
                    self.assertEqual('REJECTED', compare(source, execution)['decision']); continue
                with self.assertRaises(guard.InvalidCaseEvidence): compare(source, execution)

    def test_real_cli_refuses_private_invalid_arguments_and_keeps_rejected_report(self):
        source, execution = fixture()
        envelope = {'source': source, 'execution': execution, 'expected': IDENTITY}
        script = str(Path(guard.__file__).resolve())
        run = subprocess.run([sys.executable, '-B', script], input=json.dumps(envelope).encode(), capture_output=True)
        self.assertEqual(0, run.returncode)
        self.assertEqual('CONTENT_MATCH', json.loads(run.stdout)['decision'])
        execution['tests'].pop()
        run = subprocess.run([sys.executable, '-B', script], input=json.dumps(envelope).encode(), capture_output=True)
        self.assertEqual(1, run.returncode)
        self.assertEqual('REJECTED', json.loads(run.stdout)['decision'])
        for args, raw in [(['NEVER_PUBLISH_CANARY'], b''), ([], b'NEVER_PUBLISH_CANARY')]:
            run = subprocess.run([sys.executable, '-B', script, *args], input=raw, capture_output=True)
            self.assertEqual(1, run.returncode)
            self.assertNotIn(b'NEVER_PUBLISH_CANARY', run.stdout + run.stderr)


if __name__ == '__main__':
    unittest.main()
