from __future__ import annotations

import contextlib
import hashlib
import io
import json
import subprocess
import sys
import unittest

import test_release_assurance_advisory as release_fixture
import test_release_browser_evidence as browser_fixture

advisory = release_fixture.advisory
browser = browser_fixture.evidence


class BrowserAttachmentTests(unittest.TestCase):
    def setUp(self):
        self.release = release_fixture.ReleaseAssuranceAdvisoryTests()
        self.release.setUp()
        self.addCleanup(self.release.tearDown)
        self.native = browser_fixture.BrowserEvidenceTests()
        self.native.setUp()
        self.addCleanup(self.native.tearDown)
        self.native.context.update(startedAtUtc='2026-09-04T05:28:00Z',
                                   completedAtUtc='2026-09-04T05:29:00Z')
        self.native.native['created'] = '2026-09-04T05:28:30Z'
        self.native.args.now = self.release.args.now
        self.path = self.release.root / 'browser-tooling.json'

    def attach(self, report=None):
        self.report = self.native.report() if report is None else report
        self.release.write(self.path, self.report)
        self.release.args.browser_tooling_receipt = str(self.path)
        self.release.args.browser_tooling_run_identity = browser_fixture.RUN

    def collect(self):
        return advisory.collect(self.release.args)[0]

    def reject(self, mutate, code):
        # Every invalid control first establishes the exact valid attachment.
        self.attach()
        self.assertEqual('MATCHED', self.collect()['nativeTierBObservations']['browserTooling']['toolingOutcome'])
        mutate(self.report)
        self.release.write(self.path, self.report)
        with self.assertRaisesRegex(advisory.AdvisoryError, code):
            self.collect()

    def test_absent_attachment_preserves_existing_shape_and_source_scope(self):
        report = self.collect()
        self.assertNotIn('browserTooling', report['nativeTierBObservations'])
        self.assertNotIn('sec14_browser_evidence.py', report['verifierSourceBytesSha256'])
        self.assertEqual(4, report['missingAdvancedAdapterCount'])

    def test_positive_attachment_keeps_local_tooling_provenance_and_all_acceptance_holds(self):
        self.attach()
        report = self.collect()
        observation = report['nativeTierBObservations']['browserTooling']
        self.assertEqual('MATCHED', observation['candidateAndLocalRunConsistency'])
        self.assertEqual('TEST_OWNED_LOOPBACK_FIXTURE_ONLY', observation['scope'])
        self.assertEqual('DEVELOPMENT', observation['sourceStateObservation'])
        self.assertEqual(hashlib.sha256(self.path.read_bytes()).hexdigest(), report['inputSha256']['browserToolingReceipt'])
        self.assertEqual(self.report['fixtureCounters'], observation['fixtureCounters'])
        self.assertEqual(self.report['nativeRules'], observation['nativeRules'])
        for field in ('productSourceBinding', 'productImageBinding', 'scannerExecutionAuthentication',
                      'githubRunAuthentication', 'personalOwnerApproval', 'canonicalNormativeCoverage'):
            self.assertEqual('UNVERIFIED', observation[field])
        self.assertEqual('UNVERIFIED_RETAINED_HASHES_ONLY', observation['nativeBytesBinding'])
        self.assertIn('sec14_browser_evidence.py', report['verifierSourceBytesSha256'])
        self.assertEqual('MISSING', report['advancedAdapters']['browserAjaxZap'])
        self.assertEqual((0, 14, 4), (report['acceptanceQualifiedControlCount'],
                                     report['outstandingAcceptanceControlCount'], report['missingAdvancedAdapterCount']))
        self.assertEqual('BLOCKED', report['releaseAcceptance'])

    def test_partial_optional_context_is_rejected(self):
        self.attach()
        for name in ('browser_tooling_receipt', 'browser_tooling_run_identity'):
            original = getattr(self.release.args, name)
            setattr(self.release.args, name, None)
            with self.subTest(name=name), self.assertRaisesRegex(advisory.AdvisoryError, 'BROWSER_INPUT_INCOMPLETE'):
                self.collect()
            setattr(self.release.args, name, original)

    def test_wrong_candidate_or_local_run_is_rejected(self):
        for field, value in (('candidateSha', 'b' * 40), ('candidateSha', True),
                             ('runIdentity', 'local:' + '2' * 32)):
            with self.subTest(field=field, value=value):
                self.reject(lambda report: report.update({field: value}), 'BROWSER_IDENTITY_MISMATCH')

    def test_independent_run_must_be_a_bounded_local_identity(self):
        self.attach()
        for run in ('', 'local:' + 'a' * 31, 'local:' + 'A' * 32, release_fixture.RUN, 'x' * 10000):
            self.release.args.browser_tooling_run_identity = run
            with self.subTest(run=run[:32]), self.assertRaisesRegex(advisory.AdvisoryError, 'BROWSER_EXPECTED_CONTEXT_INVALID'):
                self.collect()
        self.release.args.browser_tooling_run_identity = browser_fixture.RUN
        for path in ('', 'x' * 4097, '\0'):
            self.release.args.browser_tooling_receipt = path
            with self.subTest(path=path[:32]), self.assertRaisesRegex(advisory.AdvisoryError, 'BROWSER_EXPECTED_CONTEXT_INVALID'):
                self.collect()

    def test_schema_unknown_fields_and_product_scope_are_rejected(self):
        for field, value in (('schema', 'unknown'), ('verifierVersion', '2'),
                             ('scope', 'PRODUCT_BROWSER'), ('mode', 'BLOCKING'),
                             ('extra', 'canary-credential')):
            with self.subTest(field=field):
                self.reject(lambda report: report.update({field: value}), 'BROWSER_SCHEMA_OR_SCOPE_INVALID')

    def test_receipt_cannot_forge_approval_product_or_native_authentication(self):
        for field in ('productSourceBinding', 'productImageBinding', 'scannerExecutionAuthentication',
                      'githubRunAuthentication', 'personalOwnerApproval', 'canonicalNormativeCoverage',
                      'rawArtifactUploadAllowed', 'releaseAcceptance', 'preAvaloniaVerdict'):
            with self.subTest(field=field):
                self.reject(lambda report: report.update({field: True}), 'BROWSER_AUTHORITY_OR_SANITIZER_INVALID')

    def test_missing_changed_or_extra_executed_source_hash_is_rejected(self):
        for source in browser.SOURCE_FILES:
            for mutation in ('missing', 'changed'):
                def mutate(report):
                    if mutation == 'missing':
                        report['sourceSha256'].pop(source)
                    else:
                        report['sourceSha256'][source] = 'f' * 64
                with self.subTest(source=source, mutation=mutation):
                    self.reject(mutate, 'BROWSER_SOURCE_BYTES_MISMATCH')
        self.reject(lambda report: report['sourceSha256'].update({'extra-source': 'a' * 64}),
                    'BROWSER_SOURCE_BYTES_MISMATCH')

    def test_missing_or_malformed_native_hashes_remain_unqualified(self):
        for mutation in ('missing', 'extra', 'invalid'):
            def mutate(report):
                if mutation == 'missing': report['inputSha256'].pop('context')
                elif mutation == 'extra': report['inputSha256']['ownerApproved'] = True
                else: report['inputSha256']['report'] = 'canary-credential'
            with self.subTest(mutation=mutation):
                self.reject(mutate, 'BROWSER_NATIVE_HASH_INVALID')

    def test_counter_types_bounds_and_unknown_fields_are_rejected(self):
        for key, value in (('root', True), ('authorized200', -1), ('other', 1000001), ('secret', 'canary-credential')):
            with self.subTest(key=key):
                self.reject(lambda report: report['fixtureCounters'].update({key: value}), 'BROWSER_COUNTER_INVALID')

    def test_missing_positive_ajax_or_unauthorized_response_cannot_keep_matched_credit(self):
        for key, value in (('authorized200', 0), ('ajaxDiscovery', 0), ('unauthorized200', 1)):
            with self.subTest(key=key):
                self.reject(lambda report: report['fixtureCounters'].update({key: value}), 'BROWSER_CONDITION_OR_OUTCOME_MISMATCH')

    def test_numeric_rules_cannot_carry_raw_or_unbounded_material(self):
        for rules in ({'canary-credential': {'riskCode': 1, 'instanceCount': 1}},
                      {'10038': {'riskCode': True, 'instanceCount': 1}},
                      {'10038': {'riskCode': 2, 'instanceCount': 10000000}},
                      {'10038': {'riskCode': 2, 'instanceCount': 1, 'body': 'canary-credential'}}):
            with self.subTest(rules=rules):
                self.reject(lambda report: report.update(nativeRules=rules), 'BROWSER_RULE_INVALID')

    def test_native_failed_tooling_receipt_is_preserved_and_returns_nonzero(self):
        self.native.context['processExit'] = 1
        self.attach()
        code, report = self.release.run_adapter()
        self.assertEqual(1, code)
        self.assertEqual('CONSISTENT', report['integrityStatus'])
        self.assertEqual('FAILED', report['nativeTierBObservations']['browserTooling']['toolingOutcome'])
        self.assertEqual(0, report['acceptanceQualifiedControlCount'])

    def test_stale_future_or_excessive_execution_window_is_rejected(self):
        for started, completed in (('2026-09-04T01:00:00Z', '2026-09-04T01:01:00Z'),
                                   ('2026-09-04T06:00:00Z', '2026-09-04T06:01:00Z'),
                                   ('2026-09-04T05:20:00Z', '2026-09-04T05:29:00Z'),
                                   ('2026-09-04Z', '2026-09-04T05:29:00Z')):
            with self.subTest(started=started):
                self.reject(lambda report: report.update(startedAtUtc=started, completedAtUtc=completed),
                            'BROWSER_EXECUTION_WINDOW_INVALID')

    def test_forged_conditions_cannot_reclassify_failure_or_disabled_verifier(self):
        for mutate in (lambda report: report['conditions'].update(scannerProcessSucceeded=False),
                       lambda report: report['conditions'].update(browserVerifierEnabled=False),
                       lambda report: report.update(toolingOutcome='PASS')):
            self.reject(mutate, 'BROWSER_CONDITION_OR_OUTCOME_MISMATCH')

    def test_error_and_missing_receipts_produce_sanitized_error_not_zero_findings(self):
        self.attach({'schema': browser.SCHEMA, 'integrityStatus': 'ERROR', 'raw': 'canary-credential'})
        report = self.release.assert_error('BROWSER_SCHEMA_OR_SCOPE_INVALID')
        self.assertNotIn('canary-credential', json.dumps(report))

    def test_missing_receipt_is_a_bounded_error(self):
        self.attach()
        self.path.unlink()
        self.release.assert_error('BROWSER_INPUT_INVALID')

    def test_duplicate_nonfinite_and_oversize_receipts_are_rejected(self):
        self.attach()
        for raw in (b'{"schema":1,"schema":2}', b'{"value":NaN}', b'x' * (browser.release.MAX_JSON_BYTES + 1)):
            self.path.write_bytes(raw)
            with self.subTest(raw=raw[:30]), self.assertRaisesRegex(advisory.AdvisoryError, 'BROWSER_INPUT_INVALID'):
                self.collect()

    def test_read_only_reverification_rejects_changed_receipt_and_preserves_original_output(self):
        self.attach()
        code, _ = self.release.run_adapter()
        self.assertEqual(0, code)
        output = self.release.out / 'release-assurance-advisory.json'
        original = output.read_bytes()
        self.release.args.verify_directory = str(self.release.out)
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(0, advisory.verify_record(self.release.args))
        self.report['fixtureCounters']['other'] += 1
        self.release.write(self.path, self.report)
        with self.assertRaisesRegex(advisory.AdvisoryError, 'RECORDED_ADVISORY_MISMATCH'):
            advisory.verify_record(self.release.args)
        self.assertEqual(original, output.read_bytes())

    def test_cli_accepts_the_optional_pair_without_launching_any_scanner(self):
        self.attach()
        command = [sys.executable, str(release_fixture.ROOT / 'scripts/ci/release_assurance_advisory.py'),
                   '--expected-repository-sha', release_fixture.SHA, '--expected-subject', release_fixture.SUBJECT,
                   '--expected-platform', 'linux/amd64', '--expected-run-identity', release_fixture.RUN,
                   '--sbom-directory', str(self.release.root), '--scan-directory', str(self.release.root),
                   '--policy', str(self.release.native.policy), '--out-directory', str(self.release.out),
                   '--now', self.release.args.now, '--browser-tooling-receipt', str(self.path),
                   '--browser-tooling-run-identity', browser_fixture.RUN]
        result = subprocess.run(command, text=True, capture_output=True, check=False)
        self.assertEqual(0, result.returncode, result.stderr)
        report = self.release.read(self.release.out / 'release-assurance-advisory.json')
        self.assertEqual('MATCHED', report['nativeTierBObservations']['browserTooling']['toolingOutcome'])


class BrowserAttachmentWorkflowTests(unittest.TestCase):
    def test_new_attachment_controls_use_the_existing_contract_job_and_both_triggers(self):
        source = release_fixture.ROOT / '.github/workflows/release-supply-chain-contract.yml'
        # Ruby's YAML 1.1 reader treats GitHub's unquoted `on` key as true.
        result = subprocess.run(['ruby', '-ryaml', '-rjson', '-e',
                                 'data = YAML.load_file(ARGV[0]); '
                                 'data["on"] = data.delete(true) if data.key?(true); '
                                 'puts JSON.generate(data)', str(source)],
                                capture_output=True, text=True, check=True)
        workflow = json.loads(result.stdout)
        for trigger in ('pull_request', 'push'):
            self.assertIn('tests/ci/test_release_browser_attachment.py', workflow['on'][trigger]['paths'])
        self.assertEqual({'sec11-contract'}, set(workflow['jobs']))
        job = workflow['jobs']['sec11-contract']
        self.assertEqual('sec11-release-supply-chain-contract', job['name'])
        scripts = '\n'.join(step.get('run', '') for step in job['steps'])
        self.assertIn('pattern="test_release*.py"', scripts)


if __name__ == '__main__':
    unittest.main()
