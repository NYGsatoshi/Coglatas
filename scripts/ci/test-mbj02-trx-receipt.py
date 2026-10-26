#!/usr/bin/env python3
"""Positive and fail-closed controls for the fixed MBJ-02 public projection."""
import copy
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import sys
import time
import unittest
from unittest import mock
import uuid
import xml.etree.ElementTree as ET

sys.dont_write_bytecode = True
SPEC = importlib.util.spec_from_file_location('mbj02', Path(__file__).with_name('mbj02-trx-receipt.py'))
mbj = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(mbj)
ROOT = Path(__file__).resolve().parents[2]
CANARY = 'private-password-cookie-query-body-canary'


def fixture(required, assembly):
    def child(parent, tag, **attributes):
        return ET.SubElement(parent, mbj.NS + tag, attributes)
    root = ET.Element(mbj.NS + 'TestRun')
    results, definitions, entries = [child(root, tag) for tag in ['Results', 'TestDefinitions', 'TestEntries']]
    for name in required:
        tid, eid, lid = [str(uuid.uuid4()) for _ in range(3)]
        result = child(results, 'UnitTestResult', testName=name, testId=tid, executionId=eid,
                       testListId=lid, outcome='Passed', duration='00:00:00.1234567')
        child(child(result, 'Output'), 'StdOut').text = CANARY
        definition = child(definitions, 'UnitTest', name=name, id=tid, storage=str(assembly).lower())
        child(definition, 'Execution', id=eid)
        klass, method = name.rsplit('.', 1)
        child(definition, 'TestMethod', className=klass, name=method, codeBase=str(assembly))
        child(entries, 'TestEntry', testId=tid, executionId=eid, testListId=lid)
    summary = child(root, 'ResultSummary', outcome='Completed')
    child(summary, 'Counters', **{k: str(8 if k in ['total', 'passed', 'executed'] else 0) for k in mbj.COUNTERS})
    return root


def serialize(tree):
    # The pre-existing checker expects the VSTest default namespace, not a
    # prefixed serializer. This mirrors the real eight-fact producer format.
    ET.register_namespace('', mbj.NS[1:-1])
    ET.indent(tree)
    return ET.tostring(tree, encoding='utf-8', xml_declaration=True)


class ReceiptTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.ident = {'sourceSha': 'a' * 40, 'runId': '12345', 'runAttempt': 1}
        self.private = mbj.paths(self.root, self.ident)
        self.private.mkdir(parents=True)
        for relative in [mbj.MANIFEST, 'scripts/ci/verify-trx-results.sh', 'scripts/ci/mbj02-trx-receipt.py']:
            target = self.root / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(ROOT / relative, target)
        self.required = mbj.names(self.root)
        self.assembly = self.root / mbj.ASSEMBLY
        self.assembly.parent.mkdir(parents=True)
        self.assembly.write_bytes(b'synthetic-test-dll')
        self.tree = fixture(self.required, self.assembly)
        self.context = {**self.ident, 'startedNs': time.time_ns() - 1000000000}
        self.save('context.json', self.context)
        self.save('test-exit.json', {'exitCode': 0})
        for phase in ['before', 'after']:
            (self.private / f'postgres-{phase}.txt').write_text('180006\n')
        self.trx = self.private / mbj.TRX
        self.write_trx()

    def save(self, name, value):
        (self.private / name).write_text(json.dumps(value))

    def write_trx(self):
        self.trx.write_bytes(serialize(self.tree))

    def receipt(self, exit_code=0):
        return mbj.projection(self.root, self.private, self.ident, exit_code)

    def fail(self):
        self.assertEqual('FAIL', self.receipt()['status'])

    def test_positive_requires_all_eight_exact_results_and_real_checker(self):
        value = self.receipt()
        self.assertEqual('PASS', value['status'])
        self.assertEqual('VERIFIED', value['verification'])
        self.assertEqual(8, len(value['facts']))
        self.assertTrue(all(n['outcome'] == 'PASS' for n in value['facts']))
        self.assertEqual(180006, value['postgresServerVersionNum'])
        self.assertEqual(mbj.digest(self.assembly.read_bytes()), value['testAssemblySha256'])
        self.assertNotIn(CANARY, json.dumps(value))
        self.assertLess(len(json.dumps(value)), 16384)

    def test_exact_subset_superset_duplicate_and_renamed_tests_fail(self):
        original = copy.deepcopy(self.tree)
        results = mbj.one(self.tree, 'Results')
        for mode in ['missing', 'extra', 'duplicate', 'suffix', 'wrong-class']:
            with self.subTest(mode=mode):
                self.tree = copy.deepcopy(original)
                results = mbj.one(self.tree, 'Results')
                if mode == 'missing':
                    results.remove(results[0])
                elif mode == 'extra':
                    results.append(copy.deepcopy(results[0]))
                elif mode == 'duplicate':
                    results[1].attrib.update(results[0].attrib)
                else:
                    results[0].set('testName', results[0].get('testName') + ('Extra' if mode == 'suffix' else '.Other'))
                self.write_trx()
                self.fail()

    def test_definition_entry_and_id_linkage_fail_closed(self):
        original = copy.deepcopy(self.tree)
        for mode in ['definition-missing', 'definition-extra', 'definition-duplicate-id', 'execution-duplicate',
                     'entry-duplicate', 'entry-mismatch', 'result-execution', 'method-name', 'storage', 'codebase']:
            with self.subTest(mode=mode):
                self.tree = copy.deepcopy(original)
                defs = mbj.one(self.tree, 'TestDefinitions')
                entries = mbj.one(self.tree, 'TestEntries')
                if mode == 'definition-missing': defs.remove(defs[0])
                if mode == 'definition-extra': defs.append(copy.deepcopy(defs[0]))
                if mode == 'definition-duplicate-id': defs[1].set('id', defs[0].get('id'))
                if mode == 'execution-duplicate': mbj.one(defs[1], 'Execution').set('id', mbj.one(defs[0], 'Execution').get('id'))
                if mode == 'entry-duplicate': entries[1].attrib.update(entries[0].attrib)
                if mode == 'entry-mismatch': entries[0].set('testId', str(uuid.uuid4()))
                if mode == 'result-execution': mbj.one(self.tree, 'Results')[0].set('executionId', str(uuid.uuid4()))
                if mode == 'method-name': mbj.one(defs[0], 'TestMethod').set('name', 'Other')
                if mode == 'storage': defs[0].set('storage', '/private/other.dll')
                if mode == 'codebase': mbj.one(defs[0], 'TestMethod').set('codeBase', '/private/other.dll')
                self.write_trx()
                self.fail()

    def test_failure_and_skip_outcomes_cannot_inherit_green_counters(self):
        result = mbj.one(self.tree, 'Results')[0]
        for outcome in ['Failed', 'NotExecuted', 'Unknown']:
            with self.subTest(outcome=outcome):
                result.set('outcome', outcome)
                self.write_trx()
                self.fail()

    def test_real_failed_result_is_projected_without_error_body(self):
        mbj.one(self.tree, 'Results')[0].set('outcome', 'Failed')
        summary = mbj.one(self.tree, 'ResultSummary')
        summary.set('outcome', 'Failed')
        mbj.one(summary, 'Counters').attrib.update(passed='7', failed='1')
        self.write_trx()
        self.save('test-exit.json', {'exitCode': 1})
        value = self.receipt(1)
        self.assertEqual('TEST_FAILED', value['verification'])
        self.assertEqual(['FAIL'] + ['PASS'] * 7, [n['outcome'] for n in value['facts']])
        self.assertNotIn(CANARY, json.dumps(value))

    def test_valid_skipped_result_is_recorded_and_cannot_pass(self):
        mbj.one(self.tree, 'Results')[0].set('outcome', 'NotExecuted')
        mbj.one(mbj.one(self.tree, 'ResultSummary'), 'Counters').attrib.update(
            passed='7', executed='7', notExecuted='1')
        self.write_trx()
        value = self.receipt()
        self.assertEqual('TEST_FAILED', value['verification'])
        self.assertEqual('FAIL', value['status'])
        self.assertEqual('SKIP', value['facts'][0]['outcome'])

    def test_each_counter_and_summary_must_match_individual_results(self):
        original = copy.deepcopy(self.tree)
        for key in mbj.COUNTERS + ['other']:
            with self.subTest(counter=key):
                self.tree = copy.deepcopy(original)
                counters = mbj.one(mbj.one(self.tree, 'ResultSummary'), 'Counters')
                counters.set(key, '999')
                self.write_trx()
                self.fail()
        self.tree = original
        mbj.one(self.tree, 'ResultSummary').set('outcome', 'Failed')
        self.write_trx()
        self.fail()

    def test_malformed_xml_namespace_dtd_entity_and_size_fail_without_disclosure(self):
        for payload in [b'', b'<bad>', serialize(self.tree).replace(mbj.NS[1:-1].encode(), b'other'),
                        b'<!DOCTYPE TestRun [<!ENTITY x "' + CANARY.encode() + b'">]><TestRun/>',
                        b'x' * (2 * 1024 * 1024 + 1)]:
            with self.subTest(size=len(payload)):
                self.trx.write_bytes(payload)
                value = self.receipt()
                self.assertEqual('FAIL', value['status'])
                self.assertNotIn(CANARY, json.dumps(value))

    def test_utf16_dtd_and_entities_are_rejected_by_strict_parser_itself(self):
        text = serialize(self.tree).decode('utf-8')
        text = text.replace("encoding='utf-8'", "encoding='utf-16'")
        text = text.replace('?>', '?><!DOCTYPE TestRun [<!ENTITY unused "' + CANARY + '">]>', 1)
        for encoding in ['utf-16', 'utf-16-le', 'utf-16-be']:
            with self.subTest(encoding=encoding):
                payload = text.encode(encoding)
                with self.assertRaises((mbj.Invalid, UnicodeError)):
                    mbj.parse_trx(payload, self.required, [self.assembly])
                self.trx.write_bytes(payload)
                self.fail()

    def test_invalid_json_metadata_shapes_fail_closed(self):
        for filename in ['context.json', 'test-exit.json']:
            original = (self.private / filename).read_bytes()
            for value in [None, True, 1, 'private', [], [{}]]:
                with self.subTest(filename=filename, shape=type(value).__name__):
                    self.save(filename, value)
                    self.fail()
            (self.private / filename).write_bytes(original)

    def test_missing_trx_dll_postgres_and_state_fail(self):
        for path in [self.trx, self.assembly, self.private / 'postgres-after.txt', self.private / 'test-exit.json']:
            with self.subTest(file=path.name):
                contents = path.read_bytes()
                path.unlink()
                self.fail()
                path.write_bytes(contents)

    def test_nonzero_test_exit_never_uses_an_old_pass(self):
        self.save('test-exit.json', {'exitCode': 27})
        value = self.receipt(27)
        self.assertEqual('FAIL', value['status'])
        self.assertEqual(27, value['testExitCode'])

    def test_acceptance_failure_distinct_from_eight_fact_verification(self):
        value = self.receipt(19)
        self.assertEqual('VERIFIED', value['verification'])
        self.assertEqual('FAIL', value['status'])
        self.assertEqual(19, value['acceptanceExitCode'])

    def test_stale_wrong_source_run_attempt_and_boolean_context_fail(self):
        for key, value in [('sourceSha', 'b' * 40), ('runId', '678'), ('runAttempt', 2), ('runAttempt', True),
                           ('startedNs', time.time_ns() + 100000000000), ('extra', CANARY)]:
            with self.subTest(key=key):
                self.save('context.json', {**self.context, key: value})
                self.fail()
        self.save('context.json', self.context)
        os.utime(self.trx, ns=(1, 1))
        self.fail()

    def test_mismatched_invalid_or_private_postgres_value_fails(self):
        for value in ['170001', CANARY, '180006\n180006', 'true', '']:
            with self.subTest(value=value):
                (self.private / 'postgres-after.txt').write_text(value)
                self.fail()

    def test_symlink_raw_and_assembly_refused(self):
        for path in [self.trx, self.assembly]:
            with self.subTest(file=path.name):
                data = path.read_bytes()
                other = path.with_suffix('.other')
                other.write_bytes(data)
                path.unlink()
                path.symlink_to(other)
                self.fail()
                path.unlink()
                path.write_bytes(data)

    def publish(self, value, outcome='success'):
        self.save('receipt.json', value)
        with mock.patch.object(mbj, 'identity', return_value=self.ident), \
                mock.patch.object(mbj.subprocess, 'check_output', return_value=str(self.root)), \
                mock.patch('sys.argv', ['receipt', 'publish', '--acceptance-outcome', outcome]), \
                mock.patch.dict(os.environ, {'GITHUB_OUTPUT': str(self.root / 'output')}, clear=False):
            return mbj.main()

    def test_consumer_revalidates_raw_digests_and_dll_at_publication(self):
        value = self.receipt()
        self.assembly.write_bytes(b'modified-binary')
        self.assertEqual(1, self.publish(value))
        self.assertFalse((self.root / 'artifacts/mbj02-issue527/receipt.json').exists())

    def test_consumer_rejects_changed_trx_even_with_same_passed_rows(self):
        value = self.receipt()
        self.trx.write_bytes(self.trx.read_bytes() + b'\n')
        self.assertEqual(1, self.publish(value))

    def test_consumer_rejects_unknown_fields_wrong_types_and_failed_step_pass(self):
        value = self.receipt()
        for mutation in [{**value, 'secret': CANARY}, {**value, 'runAttempt': True},
                         {**value, 'acceptanceExitCode': -1}]:
            self.assertEqual(1, self.publish(mutation))
        self.assertEqual(1, self.publish(value, 'failure'))

    def test_consumer_publishes_only_closed_pass_receipt_once(self):
        value = self.receipt()
        self.assertEqual(0, self.publish(value))
        public = self.root / 'artifacts/mbj02-issue527/receipt.json'
        self.assertEqual(value, json.loads(public.read_text()))
        self.assertNotIn(CANARY, public.read_text())
        self.assertEqual('validated=true\n', (self.root / 'output').read_text())
        self.assertEqual(1, self.publish(value))

    def test_consumer_keeps_valid_failed_receipt_but_returns_failure(self):
        value = self.receipt(23)
        self.assertEqual(1, self.publish(value, 'failure'))
        self.assertEqual('validated=true\n', (self.root / 'output').read_text())
        self.assertEqual('FAIL', json.loads((self.root / 'artifacts/mbj02-issue527/receipt.json').read_text())['status'])

    def test_duplicate_json_keys_rejected(self):
        with self.assertRaises(mbj.Invalid):
            mbj.decode_json(b'{"runAttempt":1,"runAttempt":1}')

    def test_runner_preserves_primary_test_failure_and_keeps_raw_stdout_private(self):
        for proof_fails, state_collision, zero_control in [(False, False, False), (True, False, False),
                                                            (False, True, False), (False, False, True)]:
            with self.subTest(proof_fails=proof_fails, state_collision=state_collision, zero_control=zero_control):
                with tempfile.TemporaryDirectory() as temporary:
                    root = Path(temporary)
                    scripts = root / 'scripts/ci'
                    scripts.mkdir(parents=True)
                    for name in ['run-mbj02-invite-acceptance.sh', 'acceptance-image-reuse.sh',
                                 'mbj02-trx-receipt.py', 'mbj02-issue527-required-tests.txt', 'verify-trx-results.sh']:
                        shutil.copyfile(ROOT / 'scripts/ci' / name, scripts / name)
                    subprocess.run(['git', 'init', '-q', str(root)], check=True)
                    subprocess.run(['git', 'add', 'scripts'], cwd=root, check=True)
                    subprocess.run(['git', '-c', 'user.name=Evidence Control', '-c', 'user.email=evidence@example.test',
                                    'commit', '-qm', 'Synthetic runner control'], cwd=root, check=True)
                    executable = root / 'bin/docker'
                    executable.parent.mkdir()
                    executable.write_text('#!' + sys.executable + '\n' + r"""
import os,pathlib,sys
args=sys.argv[1:]
if 'inspect' in args:
    print('running healthy 0')
elif 'ps' in args:
    print('synthetic-container')
elif 'logs' in args:
    print('sanitized compose observation')
elif 'run' in args and 'migrate' in args:
    relative=next(a.split('=',1)[1] for a in args if a.startswith('COGLATAS_MBJ02_RESULTS_DIRECTORY='))
    if os.environ['STATE_COLLISION']=='true':
        (pathlib.Path(relative)/'test-exit.json').write_text('{"exitCode":0}')
    print(os.environ['CANARY'])
    sys.exit(27)
elif 'exec' in args and 'SHOW server_version_num' in args:
    counter=pathlib.Path('query-count')
    count=int(counter.read_text()) if counter.exists() else 0
    counter.write_text(str(count+1))
    if count and os.environ['PROOF_FAILS']=='true':
        sys.exit(7)
    print('180006')
""")
                    executable.chmod(0o755)
                    env = {k: v for k, v in os.environ.items() if k not in
                           ['TARGET_SHA', 'GITHUB_ACTIONS', 'GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT',
                            'COGLATAS_REUSE_PREBUILT_APP_IMAGE']}
                    env.update(PATH=str(executable.parent) + os.pathsep + env['PATH'],
                               CANARY=CANARY, PROOF_FAILS=str(proof_fails).lower(),
                               STATE_COLLISION=str(state_collision).lower())
                    run_path = 'scripts/ci/run-mbj02-invite-acceptance.sh'
                    if zero_control:
                        # Execute the actual production cleanup/trap registration,
                        # with a successful primary command and no test evidence.
                        prefix = (scripts / 'run-mbj02-invite-acceptance.sh').read_text().split('\nwait_healthy() {', 1)[0]
                        run_path = 'scripts/ci/zero-primary-control.sh'
                        (root / run_path).write_text(prefix + '\nMBJ02_PRIVATE_RESULTS="$(python3 scripts/ci/mbj02-trx-receipt.py begin)"\ntrue\n')
                    result = subprocess.run(['bash', run_path], cwd=root,
                                            env=env, text=True, capture_output=True)
                    self.assertEqual(1 if zero_control else 27, result.returncode, result.stderr)
                    self.assertNotIn(CANARY, result.stdout + result.stderr)
                    private = root / 'artifacts/mbj02-issue527-private/local-1'
                    if not zero_control:
                        self.assertIn(CANARY, (private / 'test.log').read_text())
                    value = json.loads((private / 'receipt.json').read_text())
                    self.assertEqual(0 if zero_control else 27, value['acceptanceExitCode'])
                    self.assertEqual('FAIL', value['status'])
                    for public in (root / 'test-results').glob('*'):
                        self.assertNotIn(CANARY, public.read_text())

    def test_workflow_uploads_only_validated_receipt_and_preserves_owner_selection(self):
        workflow = (ROOT / '.github/workflows/licensed-real-backend-acceptance.yml').read_text()
        self.assertIn("if: always() && steps.mbj02-receipt.outputs.validated == 'true'", workflow)
        self.assertIn('path: artifacts/mbj02-issue527/receipt.json', workflow)
        self.assertNotIn('path: artifacts/mbj02-issue527-private', workflow)
        runner = (ROOT / 'scripts/ci/run-mbj02-invite-acceptance.sh').read_text()
        self.assertIn('--filter "Scope=Issue527"', runner)
        self.assertNotIn('--results-directory test-results', runner)
        self.assertIn('return "$test_exit"', runner)


if __name__ == '__main__':
    unittest.main()
