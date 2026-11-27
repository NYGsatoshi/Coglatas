"""Real subprocess controls for the isolated pinned coverage bootstrap."""
from pathlib import Path
import ast
import importlib
import importlib.util
import os
import runpy
import shlex
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import xml.etree.ElementTree as ET

import coverage

ROOT = Path(__file__).resolve().parents[2]
BOOTSTRAP = ROOT / 'scripts/ci/sonar-python-coverage-bootstrap'
CONFIG = ROOT / 'scripts/ci/sonar-python.coveragerc'


class PythonProcessCoverageTests(unittest.TestCase):
    def environment(self):
        env = os.environ.copy()
        for key in ('COVERAGE_PROCESS_START', 'COVERAGE_FILE', 'COVERAGE_RCFILE', 'PYTHONPATH'):
            env.pop(key, None)
        env['PYTHONDONTWRITEBYTECODE'] = '1'
        env['COVERAGE_CORE'] = 'pytrace'
        env['PYTHONPATH'] = str(BOOTSTRAP) + os.pathsep + str(Path(coverage.__file__).resolve().parent.parent)
        return env

    def run_python(self, args, cwd, env, *, timeout=15):
        result = subprocess.run([sys.executable, '-B', *args], cwd=cwd, env=env,
                                capture_output=True, text=True, encoding='utf-8', timeout=timeout)
        self.assertEqual(result.returncode, 0, 'Synthetic coverage control process failed.')
        return result

    def test_parent_and_cli_children_collect_both_branches_from_another_directory(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            elsewhere = folder / 'elsewhere'
            elsewhere.mkdir()
            subject = folder / 'subject.py'
            code = ('import sys\n'
                    'def parent():\n    return 7\n'
                    'def child(value):\n    if value == "allow":\n        return 0\n    return 3\n'
                    'if __name__ == "__main__":\n    sys.exit(child(sys.argv[1]))\n')
            subject.write_text(code, encoding='utf-8')
            config = folder / 'coverage.ini'
            config.write_text('[run]\nbranch = True\nparallel = True\ninclude = ' + subject.as_posix() + '\n', encoding='utf-8')
            driver = folder / 'driver.py'
            driver.write_text('import subprocess,sys\nimport subject\nassert subject.parent()==7\n'
                              'for value,expected in [("allow",0),("deny",3)]:\n'
                              '    result=subprocess.run([sys.executable,"-B",' + repr(str(subject)) + ',value],cwd=' + repr(str(elsewhere)) + ')\n'
                              '    assert result.returncode==expected\n', encoding='utf-8')
            data = folder / 'data'
            env = self.environment()
            env['COVERAGE_PROCESS_START'] = str(config)
            env['COVERAGE_FILE'] = str(data)
            self.run_python([str(driver)], folder, env)
            shards = list(folder.glob('data.*'))
            self.assertEqual(len(shards), 3, 'Parent and both real CLI children must each write a shard.')
            report_env = self.environment()
            report_env['COVERAGE_FILE'] = str(data)
            self.run_python(['-m', 'coverage', 'combine', '--rcfile=' + str(config)], folder, report_env)
            measured = coverage.CoverageData(basename=str(data))
            measured.read()
            self.assertTrue(measured.has_arcs())
            files = list(measured.measured_files())
            self.assertEqual(len(files), 1, 'Driver/bootstrap/test bodies are outside the measured subject.')
            self.assertEqual(Path(files[0]).resolve(), subject.resolve())
            lines = set(measured.lines(files[0]))
            for marker in ('    return 7', '        return 0', '    return 3'):
                self.assertIn(code.splitlines().index(marker) + 1, lines)

    def test_bootstrap_is_inert_without_explicit_process_opt_in(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            env = self.environment()
            env['COVERAGE_FILE'] = str(folder / 'data')
            result = self.run_python(['-c', 'print("INERT_CONTROL")'], folder, env)
            self.assertEqual(result.stdout.strip(), 'INERT_CONTROL')
            self.assertEqual(list(folder.glob('data*')), [])

    def test_accounting_hook_preserves_all_owned_and_nested_tests_without_duplicate_execution(self):
        loader = unittest.TestLoader()
        evidence = importlib.import_module('test_sec_arch_evidence')

        def cases(suite):
            for item in suite:
                if isinstance(item, unittest.TestSuite):
                    yield from cases(item)
                else:
                    yield item.id()

        aggregate = list(cases(loader.loadTestsFromModule(evidence)))
        expected = []
        for value in vars(evidence).values():
            if isinstance(value, type) and issubclass(value, unittest.TestCase) and value.__module__ == evidence.__name__:
                expected.extend(cases(loader.loadTestsFromTestCase(value)))
        nested = ('test_sec_arch_http_accounting', 'test_sec_arch_signalr_accounting', 'test_sec_arch_manual_replay')
        for name in nested:
            expected.extend(cases(loader.loadTestsFromModule(importlib.import_module(name))))
        self.assertGreater(len(expected), 0)
        self.assertEqual(len(expected), len(set(expected)))
        self.assertEqual(len(aggregate), len(set(aggregate)))
        self.assertEqual(sorted(expected), sorted(aggregate))
        workflow = (ROOT / 'scripts/ci/sonar-python-controls.sh').read_text(encoding='utf-8')
        loop = workflow.split('for control in \\\n', 1)[1].split('; do', 1)[0]
        for name in nested:
            self.assertNotIn('scripts/ci/' + name + '.py', loop)
        for name in ('test_sec_arch_evidence', 'test_sec_arch_github_provenance'):
            self.assertEqual(loop.count('scripts/ci/' + name + '.py'), 1)

    def test_existing_final_and_producer_binding_suites_are_complete_and_selected_once(self):
        workflow = (ROOT / 'scripts/ci/sonar-python-controls.sh').read_text(encoding='utf-8')
        loop = workflow.split('for control in \\\n', 1)[1].split('; do', 1)[0]
        paths = [line.strip().removesuffix('\\').strip() for line in loop.splitlines() if line.strip()]
        self.assertEqual(len(paths), len(set(paths)))
        loader = unittest.TestLoader()

        def cases(suite):
            for item in suite:
                if isinstance(item, unittest.TestSuite):
                    yield from cases(item)
                else:
                    yield item.id()

        for filename, count in (('test_sec_arch_reconcile.py', 25), ('test-mvp-a-final-checks.py', 10)):
            with self.subTest(filename=filename):
                self.assertEqual(paths.count('scripts/ci/' + filename), 1)
                name = 'canonical_existing_' + Path(filename).stem.replace('-', '_')
                spec = importlib.util.spec_from_file_location(name, ROOT / 'scripts/ci' / filename)
                self.assertIsNotNone(spec)
                self.assertIsNotNone(spec.loader)
                module = importlib.util.module_from_spec(spec)
                spec.loader.exec_module(module)
                self.assertFalse(hasattr(module, 'load_tests'), 'A new loader hook needs independent completeness review.')
                expected = []
                for value in vars(module).values():
                    if isinstance(value, type) and issubclass(value, unittest.TestCase) and value.__module__ == name:
                        expected.extend(cases(loader.loadTestsFromTestCase(value)))
                selected = list(cases(loader.loadTestsFromModule(module)))
                self.assertEqual(len(expected), count)
                self.assertEqual(len(selected), len(set(selected)))
                self.assertEqual(sorted(selected), sorted(expected))

    def test_existing_local_and_suspension_suites_are_complete_and_selected_once(self):
        workflow = (ROOT / 'scripts/ci/sonar-python-controls.sh').read_text(encoding='utf-8')
        loop = workflow.split('for control in \\\n', 1)[1].split('; do', 1)[0]
        loader = unittest.TestLoader()

        def cases(suite):
            for item in suite:
                if isinstance(item, unittest.TestSuite):
                    yield from cases(item)
                else:
                    yield item.id()

        for filename, count in (('test_performance_local.py', 26), ('test_performance_assurance.py', 9)):
            with self.subTest(filename=filename):
                self.assertEqual(loop.count('tests/ci/' + filename), 1)
                name = 'canonical_existing_' + Path(filename).stem
                spec = importlib.util.spec_from_file_location(name, ROOT / 'tests/ci' / filename)
                self.assertIsNotNone(spec)
                self.assertIsNotNone(spec.loader)
                module = importlib.util.module_from_spec(spec)
                with mock.patch.object(sys, 'path', [str(ROOT / 'tests/ci'), *sys.path]):
                    spec.loader.exec_module(module)
                self.assertFalse(hasattr(module, 'load_tests'), 'A new loader hook needs independent completeness review.')
                expected = []
                for value in vars(module).values():
                    if isinstance(value, type) and issubclass(value, unittest.TestCase) and value.__module__ == name:
                        expected.extend(cases(loader.loadTestsFromTestCase(value)))
                selected = list(cases(loader.loadTestsFromModule(module)))
                self.assertEqual(len(expected), count)
                self.assertEqual(len(selected), len(set(selected)))
                self.assertEqual(sorted(selected), sorted(expected))

    def test_local_suite_without_main_runs_all_cases_through_the_selected_producer_command(self):
        workflow = (ROOT / 'scripts/ci/sonar-python-controls.sh').read_text(encoding='utf-8')
        dispatch = workflow.split('; do\n', 1)[1].split('\n          done', 1)[0]
        discovery = dispatch.split('if [[ "$control" == "tests/ci/test_performance_local.py" ]]; then\n', 1)[1].split('            fi', 1)[0]
        self.assertEqual(discovery.count('python3 '), 1)
        self.assertIn('              continue\n', discovery)
        command = next(line.strip() for line in discovery.splitlines() if line.strip().startswith('python3 '))
        args = shlex.split(command.replace('${control##*/}', 'test_performance_local.py'))
        self.assertEqual(args[:5], ['python3', '-B', '-m', 'coverage', 'run'])
        self.assertEqual(args[5:], ['-m', 'unittest', 'discover', '-s', 'tests/ci', '-p', 'test_performance_local.py'])
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            suite = folder / 'tests/ci'
            suite.mkdir(parents=True)
            (suite / 'subject.py').write_text('from pathlib import Path\ndef record(value):\n    with Path("executed.txt").open("a") as output:\n        output.write(str(value) + "\\n")\n', encoding='utf-8')
            methods = ''.join('    def test_case_' + str(i) + '(self):\n        record(' + str(i) + ')\n' for i in range(26))
            (suite / 'test_performance_local.py').write_text('import unittest\nfrom subject import record\nclass SyntheticLocalTests(unittest.TestCase):\n' + methods, encoding='utf-8')
            config = folder / 'coverage.ini'
            config.write_text('[run]\nbranch = True\nparallel = True\ninclude = ' + (suite / 'subject.py').as_posix() + '\n', encoding='utf-8')
            env = self.environment()
            env['COVERAGE_RCFILE'] = str(config)
            env['COVERAGE_PROCESS_START'] = str(config)
            env['COVERAGE_FILE'] = str(folder / 'data')
            result = self.run_python(args[1:], folder, env)
            self.assertIn('Ran 26 tests', result.stderr)
            self.assertIn('OK', result.stderr)
            executed = (folder / 'executed.txt').read_text(encoding='utf-8').splitlines()
            self.assertEqual(sorted(map(int, executed)), list(range(26)))
            self.assertEqual(len(list(folder.glob('data.*'))), 1)

    def test_exact_realtime_candidate_source_uses_native_executed_coverage(self):
        tracked = (ROOT / 'scripts/security/sec17_realtime_finding_candidate.py').resolve()
        subject = (ROOT / 'tests/ci/test_sec17_realtime_finding_candidate.py').resolve()
        workflow = (ROOT / 'scripts/ci/sonar-python-controls.sh').read_text(encoding='utf-8')
        controls = workflow.split('for control in \\\n', 1)[1].split('; do', 1)[0]
        self.assertEqual(controls.count('tests/ci/test_sec17_realtime_finding_candidate.py'), 1)
        with tempfile.TemporaryDirectory() as directory:
            data_file = Path(directory) / 'native'
            env = self.environment()
            env['SONAR_PYTHON_SOURCE_ROOT'] = str(ROOT)
            env['COVERAGE_RCFILE'] = str(CONFIG)
            env['COVERAGE_FILE'] = str(data_file)
            executed = self.run_python(['-m', 'coverage', 'run', str(subject)], ROOT, env)
            self.assertIn('Ran 9 tests', executed.stderr)
            self.assertIn('OK', executed.stderr)
            self.run_python(['-m', 'coverage', 'combine'], ROOT, env)
            data = coverage.CoverageData(basename=str(data_file))
            data.read()
            measured = {Path(name).resolve(): name for name in data.measured_files()}
            self.assertIn(tracked, measured)
            self.assertNotIn(subject, measured)
            self.assertTrue(data.has_arcs())
            run_lines = set(data.lines(measured[tracked]) or ())
            self.assertGreater(len(run_lines), 0)
            report = coverage.Coverage(data_file=str(data_file), config_file=str(CONFIG))
            report.load()
            statements = set(report.analysis2(str(tracked))[1])
            self.assertTrue(statements)
            self.assertGreaterEqual(len(statements & run_lines) / len(statements), 0.80)

    def test_logical_main_push_admission_is_measured_by_existing_python_cohort(self):
        """The native logical suite must be selected; no coverage exclusion or bypass."""
        workflow = (ROOT / 'scripts/ci/sonar-python-controls.sh').read_text(encoding='utf-8')
        cohort = workflow.split('for control in \\\n', 1)[1].split('; do', 1)[0]
        self.assertEqual(cohort.count('tests/ci/test_synthetic_logical_recovery_ci.py'), 1)
        self.assertIn('python3 -B -m coverage run "$control"', workflow)
        subject = (ROOT / 'scripts/ci/run-synthetic-logical-recovery-ci.py').resolve()
        with mock.patch.dict(os.environ, {'SONAR_PYTHON_SOURCE_ROOT': ROOT.as_posix()}):
            config = coverage.Coverage(config_file=str(CONFIG))
        self.assertEqual(config.get_option('run:include').count(str(subject)), 1)
        cases = unittest.TestLoader().discover(str(ROOT / 'tests/ci'),
                                               pattern='test_synthetic_logical_recovery_ci.py')
        self.assertGreater(cases.countTestCases(), 10)

    def test_angular_proof_controls_report_native_subject_coverage(self):
        """Use the real repository configuration and regression suite, not a copied subject."""
        subject = (ROOT / 'scripts/ci/verify-sonar-angular-coverage.py').resolve()
        suite = ROOT / 'scripts/ci/test-sonar-angular-coverage.py'
        workflow = (ROOT / 'scripts/ci/sonar-python-controls.sh').read_text(encoding='utf-8')
        cohort = workflow.split('for control in \\\n', 1)[1].split('; do', 1)[0]
        self.assertEqual(cohort.count('scripts/ci/test-sonar-angular-coverage.py'), 1)
        with tempfile.TemporaryDirectory() as directory:
            data = Path(directory) / 'coverage'
            env = self.environment()
            env['SONAR_PYTHON_SOURCE_ROOT'] = ROOT.as_posix()
            env['COVERAGE_FILE'] = str(data)
            option = '--rcfile=' + str(CONFIG)
            self.run_python(['-m', 'coverage', 'run', option, str(suite)], ROOT, env)
            self.run_python(['-m', 'coverage', 'combine', option], ROOT, env)
            measured = coverage.CoverageData(basename=str(data))
            measured.read()
            self.assertTrue(measured.has_arcs())
            by_path = {Path(name).resolve(): name for name in measured.measured_files()}
            self.assertIn(subject, by_path)
            self.assertNotIn(suite.resolve(), by_path)
            lines = set(measured.lines(by_path[subject]) or [])
            source = subject.read_text(encoding='utf-8').splitlines()
            for marker in ('raise ValueError("Angular artifact identity or digest mismatch")',
                           'result = compare_results(inventories.pop("baseline"), inventories)'):
                matching = [i for i, line in enumerate(source, 1) if line.strip() == marker]
                self.assertEqual(len(matching), 1)
                self.assertIn(matching[0], lines)
            self.assertTrue(measured.arcs(by_path[subject]))

    def test_real_native_python_shard_collection_and_comparison(self):
        """Exercise the actual collector/comparator with real pinned native reports."""
        import contextlib
        import io
        import json
        import shutil

        subject_path = ROOT / 'scripts/ci/verify-sonar-python-sharding.py'
        spec = importlib.util.spec_from_file_location('native_shard_fixture', subject_path)
        self.assertIsNotNone(spec)
        self.assertIsNotNone(spec.loader)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        self.assertEqual(coverage.__version__, '7.17.0')
        tool = Path(coverage.__file__).resolve().parent.parent
        with tempfile.TemporaryDirectory() as directory:
            work = Path(directory).resolve()
            root = work / 'checkout'
            root.mkdir()
            control = 'scripts/ci/test-native-mini.py'
            fixture_subject = 'scripts/ci/fixture_subject.py'

            def write(relative, value):
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(value, encoding='utf-8')
                return path

            # These are isolated test fixtures, never the production cohort or
            # any production handoff. Native coverage.py executes the source.
            write(fixture_subject,
                  'def choose(flag):\n    if flag:\n        return 1\n    return 0\n')
            write(control,
                  'import unittest\nfrom fixture_subject import choose\n'
                  'class Fixture(unittest.TestCase):\n'
                  '    def test_false(self):\n        self.assertEqual(choose(False), 0)\n'
                  'if __name__ == "__main__":\n    unittest.main()\n')
            write(module.MUTATION_CONTROL,
                  'import json, subprocess, sys\nfrom pathlib import Path\n'
                  'from fixture_subject import choose\n'
                  'assert choose(True) == 1\n'
                  'result = subprocess.run([sys.executable, "-B", "-c", '
                  '"from fixture_subject import choose; raise SystemExit(0 if choose(True) == 1 else 1)"], '
                  'cwd=Path(__file__).parent, check=False)\n'
                  'report = Path("artifacts/av-mig/production-cli-mutations.json")\n'
                  'report.parent.mkdir(parents=True, exist_ok=True)\n'
                  'report.write_text(json.dumps([{"mutation": "fixture-true", '
                  '"expectedExit": 0, "actualExit": result.returncode, '
                  '"result": "PASS" if result.returncode == 0 else "FAIL"}]))\n'
                  'raise SystemExit(result.returncode)\n')
            write(module.CONFIG, '[run]\nbranch = True\nparallel = True\ninclude =\n    '
                  + (root / fixture_subject).as_posix() + '\n')
            for relative in (module.WORKFLOW, module.REQUIREMENTS, module.BOOTSTRAP):
                write(relative, (ROOT / relative).read_text(encoding='utf-8'))
            canonical = (ROOT / module.CONTROL_LOOP).read_text(encoding='utf-8')
            _, separator, body = canonical.partition('; do\n')
            self.assertEqual(separator, '; do\n')
            write(module.CONTROL_LOOP, '          for control in \\\n'
                  + '            ' + control + ' \\\n'
                  + '            ' + module.MUTATION_CONTROL + separator + body)
            for args in (['init', '--quiet'], ['add', '.'],
                         ['-c', 'user.name=Coverage fixture', '-c',
                          'user.email=coverage@example.test', '-c',
                          'commit.gpgsign=false', 'commit', '--quiet', '-m', 'Native coverage fixture']):
                subprocess.run(['/usr/bin/git', *args], cwd=root, check=True, capture_output=True)
            sha = subprocess.check_output(['/usr/bin/git', 'rev-parse', 'HEAD'],
                                          cwd=root, text=True).strip()
            env = {'PROOF_SOURCE_SHA': sha, 'GITHUB_RUN_ID': '100',
                   'GITHUB_RUN_ATTEMPT': '1', 'GITHUB_WORKSPACE': str(root)}
            archives = root / 'artifacts/sonar-python-shard-proof'
            archives.mkdir(parents=True)
            output = root / 'artifacts/sonar-python-shard-equivalence'
            with (mock.patch.object(module, 'ROOT', root),
                  mock.patch.object(module, 'SECOND_GROUP', {control}),
                  mock.patch.dict(os.environ, env),
                  contextlib.redirect_stdout(io.StringIO())):
                for lane in module.LANES:
                    runner = work / lane
                    runner.mkdir()
                    (runner / 'sonar-coverage-tools').symlink_to(tool, target_is_directory=True)
                    with mock.patch.dict(os.environ, {'RUNNER_TEMP': str(runner)}):
                        module.collect(lane)
                    shutil.move(str(root / 'artifacts/sonar-python-shard-lane'),
                                str(archives / ('sonar-python-shard-proof-' + lane + '-1')))
                    (root / module.MUTATION_REPORT).unlink(missing_ok=True)
                with mock.patch.dict(os.environ, {'RUNNER_TEMP': str(work / 'baseline')}):
                    module.compare()
                    result = json.loads((output / 'receipt.json').read_bytes())
                    self.assertEqual(result['sourceSha'], sha)
                    self.assertEqual(result['controls'], 2)
                    self.assertEqual(result['unittestCases'], 1)
                    self.assertEqual(result['mutationCases'], 1)
                    self.assertEqual(result['measuredFiles'], 1)
                    self.assertGreater(result['executedArcs'], 0)
                    self.assertEqual(set(module.native_points(
                        output / module.NATIVE_REPORTS[0], root, {fixture_subject})),
                        {fixture_subject})
                    with self.assertRaisesRegex(ValueError, 'unowned source'):
                        module.native_points(output / module.NATIVE_REPORTS[0], root, set())
                    shutil.rmtree(output)
                    baseline = archives / 'sonar-python-shard-proof-baseline-1'
                    shard = archives / 'sonar-python-shard-proof-shard-1-1'
                    receipt_path = baseline / 'receipt.json'
                    original_receipt = receipt_path.read_bytes()

                    def rejected(message):
                        try:
                            with self.assertRaisesRegex(ValueError, message):
                                module.compare()
                            self.assertFalse((output / 'receipt.json').exists())
                        finally:
                            if output.exists():
                                shutil.rmtree(output)

                    extra = archives / 'unexpected-attempt'
                    extra.mkdir()
                    try:
                        rejected('Missing, stale or unexpected Python lane artifact')
                    finally:
                        extra.rmdir()
                    for key, value, message in (
                        ('attempt', '2', 'Native Python artifact receipt or digest differs'),
                        ('controlSeconds', 0, 'Invalid native control timing'),
                        ('hashes', {}, 'Native Python artifact receipt or digest differs'),
                    ):
                        changed = json.loads(original_receipt)
                        changed[key] = value
                        try:
                            receipt_path.write_text(json.dumps(changed), encoding='utf-8')
                            rejected(message)
                        finally:
                            receipt_path.write_bytes(original_receipt)
                    # Re-bind real native bytes from a complementary shard to
                    # exercise semantic checks independently of manifest checks.
                    for name, message in (
                        (module.NATIVE_REPORTS[0],
                         'Combined Python native line/arc inventory differs from baseline'),
                        (module.NATIVE_REPORTS[1],
                         'Combined native Python JSON point/branch evidence differs'),
                        (module.NATIVE_REPORTS[2],
                         'Combined native Python XML differs outside timestamp'),
                    ):
                        target = baseline / name
                        original = target.read_bytes()
                        replacement = (shard / name).read_bytes()
                        self.assertNotEqual(original, replacement)
                        try:
                            target.write_bytes(replacement)
                            changed = json.loads(original_receipt)
                            changed['hashes'] = module.file_manifest(baseline)
                            receipt_path.write_text(json.dumps(changed), encoding='utf-8')
                            rejected(message)
                        finally:
                            target.write_bytes(original)
                            receipt_path.write_bytes(original_receipt)
                    ledger = baseline / 'production-cli-mutations.json'
                    original_ledger = ledger.read_bytes()
                    changed = json.loads(original_ledger)
                    changed[0]['mutation'] = 'different-fixture'
                    try:
                        ledger.write_text(json.dumps(changed), encoding='utf-8')
                        receipt = json.loads(original_receipt)
                        receipt['hashes'] = module.file_manifest(baseline)
                        receipt_path.write_text(json.dumps(receipt), encoding='utf-8')
                        rejected('Native mutation receipt differs from original ledger')
                    finally:
                        ledger.write_bytes(original_ledger)
                        receipt_path.write_bytes(original_receipt)
                    module.compare()
                    self.assertTrue((output / 'receipt.json').is_file())

    def test_compiler_and_sharding_proof_controls_report_native_subject_coverage(self):
        """Measure actual checked-in helpers; synthetic inputs are only regression fixtures."""
        cohort = (ROOT / 'scripts/ci/sonar-python-controls.sh').read_text(encoding='utf-8')
        cohort = cohort.split('for control in \\\n', 1)[1].split('; do', 1)[0]
        markers = {
            'dotnet-coverage': (
                'raise ValueError("Actual scanner pre/post-rebuild binaries or mappings differ")',
                'raise ValueError("Actual scanner executed binary changed")',
                'raise ValueError("Actual scanner native baseline report differs")',
                'raise ValueError("Native shard TRX counter differs")',
                'raise ValueError("Actual scanner baseline comparison counters differ")',
                'return dict(identity, **result, byteIdentical=True, mappingIdentical=True,',
            ),
            'python-sharding': (
                'raise ValueError("Missing or duplicated Python control execution")',
                'return value["files"]',
                'root.attrib.pop("timestamp", None)',
                'raise ValueError("Missing, stale or unexpected Python lane artifact")',
                'raise ValueError("Native Python artifact receipt or digest differs")',
                'raise ValueError("Invalid native control timing")',
                'raise ValueError("Native mutation receipt differs from original ledger")',
                'raise ValueError("Combined Python native line/arc inventory differs from baseline")',
                'raise ValueError("Combined native Python JSON point/branch evidence differs")',
                'raise ValueError("Combined native Python XML differs outside timestamp")',
                'raise ValueError("Native Python data references unowned source: " + name)',
                'result.update(expected, scope="PYTHON_NATIVE_SHARD_EQUIVALENCE_ONLY_NOT_SONARCLOUD",',
            ),
            'openapi-reproduction': (
                'raise ValueError("Invalid native build receipt")',
                'print(json.dumps(receipt, sort_keys=True))',
            ),
            'scanner-identity-trial': (
                'raise ValueError("Diagnostic producer receipt digest differs")',
                'raise ValueError("Diagnostic producer evidence differs")',
                'return record',
            ),
        }
        for stem, required in markers.items():
            with self.subTest(subject=stem), tempfile.TemporaryDirectory() as directory:
                subject = (ROOT / ('scripts/ci/verify-sonar-' + stem + '.py')).resolve()
                suite = ROOT / ('scripts/ci/test-sonar-' + stem + '.py')
                self.assertEqual(cohort.count(suite.relative_to(ROOT).as_posix()), 1)
                data = Path(directory) / 'coverage'
                env = self.environment()
                env['SONAR_PYTHON_SOURCE_ROOT'] = ROOT.as_posix()
                env['COVERAGE_FILE'] = str(data)
                option = '--rcfile=' + str(CONFIG)
                self.run_python(['-m', 'coverage', 'run', option, str(suite)], ROOT, env)
                if stem == 'python-sharding':
                    self.run_python(['-m', 'coverage', 'run', option, str(Path(__file__).resolve()),
                                     'PythonProcessCoverageTests.test_real_native_python_shard_collection_and_comparison'],
                                    ROOT, env, timeout=120)
                self.run_python(['-m', 'coverage', 'combine', option], ROOT, env)
                measured = coverage.CoverageData(basename=str(data))
                measured.read()
                self.assertTrue(measured.has_arcs())
                by_path = {Path(name).resolve(): name for name in measured.measured_files()}
                self.assertIn(subject, by_path)
                self.assertNotIn(suite.resolve(), by_path)
                lines = set(measured.lines(by_path[subject]) or [])
                source = subject.read_text(encoding='utf-8').splitlines()
                for marker in required:
                    matching = [i for i, line in enumerate(source, 1) if line.strip() == marker]
                    self.assertEqual(len(matching), 1, marker)
                    self.assertIn(matching[0], lines, marker)
                self.assertTrue(measured.arcs(by_path[subject]))

    def test_subject_scope_branch_and_parallel_configuration_are_explicit(self):
        with mock.patch.dict(os.environ, {'SONAR_PYTHON_SOURCE_ROOT': ROOT.as_posix()}):
            config = coverage.Coverage(config_file=str(CONFIG))
        self.assertTrue(config.get_option('run:branch'))
        self.assertTrue(config.get_option('run:parallel'))
        self.assertEqual(set(config.get_option('run:include')), {
            ROOT.as_posix() + '/scripts/ci/verify-sonar-dotnet-coverage.py',
            ROOT.as_posix() + '/scripts/ci/verify-sonar-angular-coverage.py',
            ROOT.as_posix() + '/scripts/ci/verify-sonar-python-sharding.py',
            ROOT.as_posix() + '/scripts/ci/verify-sonar-openapi-reproduction.py',
            ROOT.as_posix() + '/scripts/ci/verify-sonar-scanner-identity-trial.py',
            ROOT.as_posix() + '/scripts/ci/verify-sonar-windows-coverage.py',
            ROOT.as_posix() + '/scripts/ci/required_check_contract.py',
            ROOT.as_posix() + '/scripts/ci/check-publication-readiness.py',
            ROOT.as_posix() + '/scripts/ci/check-coglatas-rename.py',
            ROOT.as_posix() + '/scripts/ci/github_action_pins.py',
            ROOT.as_posix() + '/scripts/ci/verify_sbom_vulnerability_gate.py',
            ROOT.as_posix() + '/scripts/ci/verify_sbom.py',
            ROOT.as_posix() + '/scripts/ci/verify_grype_sbom_source.py',
            ROOT.as_posix() + '/scripts/security/finding_policy.py',
            ROOT.as_posix() + '/scripts/security/sec17_realtime_finding_candidate.py',
            ROOT.as_posix() + '/scripts/security/schemathesis_policy.py',
            ROOT.as_posix() + '/scripts/ci/run-sec04-deep-ci.py',
            ROOT.as_posix() + '/scripts/ci/sec17_deep_execution.py',
            ROOT.as_posix() + '/scripts/ci/sec17_file_policy_execution.py',
            ROOT.as_posix() + '/scripts/ci/sec_arch_evidence.py',
            ROOT.as_posix() + '/scripts/ci/runtime_image_identity.py',
            ROOT.as_posix() + '/scripts/ci/verify-acceptance-image-reuse.py',
            ROOT.as_posix() + '/scripts/ci/release_image_graph.py',
            ROOT.as_posix() + '/scripts/ci/release_supply_chain.py',
            ROOT.as_posix() + '/scripts/ci/release_assurance_advisory.py',
            ROOT.as_posix() + '/scripts/ci/sec14_main_security_evidence.py',
            ROOT.as_posix() + '/scripts/ci/sec14_browser_evidence.py',
            ROOT.as_posix() + '/scripts/ci/functional_evidence.py',
            ROOT.as_posix() + '/scripts/ci/sec_arch_github_provenance.py',
            ROOT.as_posix() + '/scripts/ci/sec_arch_reconcile.py',
            ROOT.as_posix() + '/scripts/ci/sec_arch_assembly_binding.py',
            ROOT.as_posix() + '/scripts/performance/db_campaign.py',
            ROOT.as_posix() + '/scripts/performance/compare.py',
            ROOT.as_posix() + '/scripts/ci/verify-performance-baseline-updates.py',
            ROOT.as_posix() + '/scripts/ci/verify-performance-contract.py',
            ROOT.as_posix() + '/scripts/ci/verify-performance-db-campaign-baselines.py',
            ROOT.as_posix() + '/deploy/sakura/validate-pilot-intake-policy.py',
            ROOT.as_posix() + '/scripts/ci/mbj02-trx-receipt.py',
            ROOT.as_posix() + '/scripts/ci/functional_diagnostics.py',
            ROOT.as_posix() + '/scripts/ci/verify-postgres-log-privacy.py',
            ROOT.as_posix() + '/scripts/ci/functional-setup-failure-probe.py',
            ROOT.as_posix() + '/scripts/security/process-zap-report.py',
            ROOT.as_posix() + '/scripts/performance/db-ci.py',
            ROOT.as_posix() + '/scripts/ci/thread_probe_diagnostics.py',
            ROOT.as_posix() + '/scripts/ci/sonar-python-coverage-bootstrap/sitecustomize.py',
            ROOT.as_posix() + '/scripts/ci/run-synthetic-logical-recovery-ci.py',
            ROOT.as_posix() + '/scripts/ci/specification_contract.py',
            ROOT.as_posix() + '/scripts/ci/specification_contract_check.py',
            ROOT.as_posix() + '/scripts/ci/sec_arch_http_accounting.py',
            ROOT.as_posix() + '/scripts/ci/sec_arch_signalr_accounting.py',
            ROOT.as_posix() + '/scripts/ci/sec_arch_worker_accounting.py',
            ROOT.as_posix() + '/scripts/ci/sec_arch_rls_matrix.py',
            ROOT.as_posix() + '/scripts/ci/sec_arch_rls_sources.py',
            ROOT.as_posix() + '/scripts/ci/sec_arch_http_theory_cases.py',
            ROOT.as_posix() + '/scripts/ci/sec_arch_evidence.py',
            ROOT.as_posix() + '/scripts/ci/sec_arch_manual_replay.py',
            ROOT.as_posix() + '/scripts/quality/reconcile_qodana.py',
            ROOT.as_posix() + '/scripts/quality/reviewed_qodana_correspondence.py',
            ROOT.as_posix() + '/scripts/ci/qodana-packets/verify.py',
            ROOT.as_posix() + '/scripts/ci/qodana-packets/r2_verify.py',
            ROOT.as_posix() + '/scripts/ci/nuget_dependency_snapshot.py',
            ROOT.as_posix() + '/scripts/ci/check-nuget-lock-policy.py',
            ROOT.as_posix() + '/scripts/ci/audit_sbom_component_inventory.py',
            ROOT.as_posix() + '/scripts/ci/verify-mvp-a-final-checks.py',
            ROOT.as_posix() + '/scripts/ci/validate-functional-quarantine.py',
            ROOT.as_posix() + '/scripts/ci/finalize-functional-lane.py',
            ROOT.as_posix() + '/scripts/performance/api_k6.py',
            ROOT.as_posix() + '/scripts/performance/api_diagnostics.py',
            ROOT.as_posix() + '/scripts/performance/common.py',
            ROOT.as_posix() + '/scripts/performance/environment_class.py',
            ROOT.as_posix() + '/scripts/performance/announcement-diagnostic.py',
            ROOT.as_posix() + '/scripts/performance/assurance_policy.py',
            ROOT.as_posix() + '/scripts/performance/collect-environment.py',
            ROOT.as_posix() + '/scripts/performance/db-compare.py',
            ROOT.as_posix() + '/scripts/performance/db-probe.py',
            ROOT.as_posix() + '/scripts/performance/db_class_baselines.py',
            ROOT.as_posix() + '/scripts/performance/db_gate.py',
            ROOT.as_posix() + '/scripts/performance/db_probe_import.py',
            ROOT.as_posix() + '/scripts/performance/diagnostic_json.py',
            ROOT.as_posix() + '/scripts/performance/diagnostic_recovery.py',
            ROOT.as_posix() + '/scripts/performance/local-announcement-diagnostic.py',
            ROOT.as_posix() + '/scripts/performance/local-fetch-evidence.py',
            ROOT.as_posix() + '/scripts/performance/local-github-preflight.py',
            ROOT.as_posix() + '/scripts/ci/verify_av_mig_contract_boundary.py',
            ROOT.as_posix() + '/scripts/ci/verify_av_mig_contract_boundary_base.py',
            ROOT.as_posix() + '/scripts/performance/assurance_policy.py',
            ROOT.as_posix() + '/scripts/performance/db_probe_import.py',
            ROOT.as_posix() + '/scripts/performance/local-runtime-preflight.py',
            ROOT.as_posix() + '/scripts/performance/local_api_samples.py',
            ROOT.as_posix() + '/scripts/performance/local_evidence.py',
            ROOT.as_posix() + '/scripts/performance/local_runner.py',
            ROOT.as_posix() + '/scripts/performance/local_support.py',
            ROOT.as_posix() + '/scripts/performance/performance-local.py',
            ROOT.as_posix() + '/scripts/performance/preflight.py',
            ROOT.as_posix() + '/scripts/performance/verify-samples.py',
            ROOT.as_posix() + '/scripts/performance/warmup.py',
            ROOT.as_posix() + '/scripts/performance/db-compare.py',
            ROOT.as_posix() + '/scripts/performance/db_class_baselines.py',
            ROOT.as_posix() + '/scripts/performance/collect-environment.py',
            ROOT.as_posix() + '/scripts/performance/diagnostic_recovery.py',
            ROOT.as_posix() + '/scripts/performance/announcement-diagnostic.py',
        })


    def test_execution_reader_controls_measure_exact_source_and_preserve_uncalled_cli(self):
        tracked = ROOT / 'scripts/ci/sec_arch_evidence.py'
        functions = {node.name: node for node in ast.parse(tracked.read_text(encoding='utf-8')).body
                     if isinstance(node, ast.FunctionDef)}
        covered = next(node.lineno for node in ast.walk(functions['read_execution_trx'])
                       if isinstance(node, ast.Return) and isinstance(node.value, ast.Name)
                       and node.value.id == 'data')
        container = next(node for node in functions['main'].body
                         if isinstance(node, ast.If) and isinstance(node.test, ast.Name)
                         and node.test.id == 'container')
        uncalled = container.body[0].lineno
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            env = self.environment()
            env.pop('SEC_ARCH_POSTGRES_CONTAINER', None)
            env['SONAR_PYTHON_SOURCE_ROOT'] = ROOT.as_posix()
            env['COVERAGE_PROCESS_START'] = str(CONFIG)
            env['COVERAGE_FILE'] = str(folder / 'data')
            self.run_python([str(ROOT / 'scripts/ci/test_sec_arch_evidence.py')], folder, env)
            env.pop('COVERAGE_PROCESS_START')
            self.run_python(['-m', 'coverage', 'combine', '--keep', '--rcfile=' + str(CONFIG)], folder, env)
            data = coverage.CoverageData(basename=str(folder / 'data'))
            data.read()
            sources = [name for name in data.measured_files() if Path(name).resolve() == tracked.resolve()]
            self.assertEqual(len(sources), 1)
            self.assertIn(covered, data.lines(sources[0]))
            self.assertNotIn(uncalled, data.lines(sources[0]))
            report = folder / 'coverage.xml'
            self.run_python(['-m', 'coverage', 'xml', '--rcfile=' + str(CONFIG), '-o', str(report)], folder, env)
            xml = ET.parse(report).getroot()
            roots = [Path(node.text or '.') for node in xml.findall('./sources/source')]
            classes = [node for node in xml.findall('.//class')
                       if tracked.resolve() in {(root / node.attrib['filename']).resolve() for root in roots}]
            self.assertEqual(len(classes), 1)
            hits = {int(node.attrib['number']): int(node.attrib['hits'])
                    for node in classes[0].findall('./lines/line')}
            self.assertGreater(hits[covered], 0)
            self.assertEqual(hits[uncalled], 0)

    def test_existing_receipt_cli_controls_measure_tracked_source_and_preserve_zero_branch(self):
        tracked = ROOT / 'scripts/ci/verify-acceptance-image-reuse.py'
        tree = ast.parse(tracked.read_text(encoding='utf-8'))
        verification = next(node for node in tree.body
                            if isinstance(node, ast.FunctionDef) and node.name == 'verify_receipts')
        covered_line = verification.body[0].lineno
        binding_line = next(node.lineno for node in ast.walk(tree)
                            if isinstance(node, ast.Assign)
                            and any(isinstance(target, ast.Name) and target.id == 'require_binding'
                                    for target in node.targets))
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            data = folder / 'data'
            env = self.environment()
            env['SONAR_PYTHON_SOURCE_ROOT'] = ROOT.as_posix()
            env['COVERAGE_PROCESS_START'] = str(CONFIG)
            env['COVERAGE_FILE'] = str(data)
            self.run_python([
                str(ROOT / 'scripts/ci/test-main-runtime-artifact.py'),
                'AcceptanceImageReuseTests.test_all_four_receipts_are_required_and_reset_does_not_touch_other_artifacts',
                'AcceptanceImageReuseTests.test_invalid_or_wrong_producer_receipts_fail_without_echoing_contents',
                'AcceptanceImageReuseTests.test_extra_entries_and_non_regular_receipts_are_not_qualified',
            ], folder, env)
            self.assertGreater(len(list(folder.glob('data.*'))), 1,
                               'The existing controls and real CLI children must write separate coverage shards.')
            env.pop('COVERAGE_PROCESS_START')
            self.run_python(['-m', 'coverage', 'combine', '--keep', '--rcfile=' + str(CONFIG)], folder, env)
            measured = coverage.CoverageData(basename=str(data))
            measured.read()
            self.assertTrue(measured.has_arcs())
            target_files = [name for name in measured.measured_files() if Path(name).resolve() == tracked.resolve()]
            self.assertEqual(len(target_files), 1, 'The report must identify the exact tracked operational CLI.')
            executed = set(measured.lines(target_files[0]))
            self.assertIn(covered_line, executed)
            self.assertNotIn(binding_line, executed,
                             'Same-engine controls must not invent execution of the distinct-engine binding branch.')
            report = folder / 'coverage.xml'
            self.run_python(['-m', 'coverage', 'xml', '--rcfile=' + str(CONFIG), '-o', str(report)], folder, env)
            document = ET.parse(report).getroot()
            sources = [Path(source.text or '.') for source in document.findall('./sources/source')]
            classes = [node for node in document.findall('.//class')
                       if tracked.resolve() in {(source / node.attrib['filename']).resolve() for source in sources}]
            self.assertEqual(len(classes), 1, 'Cobertura must retain the same exact tracked source identity.')
            hits = {int(line.attrib['number']): int(line.attrib['hits'])
                    for line in classes[0].findall('./lines/line')}
            self.assertGreater(hits.get(covered_line, 0), 0)
            self.assertIn(binding_line, hits)
            self.assertEqual(hits[binding_line], 0, 'Original zero rows must survive standard XML generation.')

    def test_existing_privacy_and_probe_suites_measure_exact_sources_and_preserve_uncalled_entries(self):
        subjects = [
            ('verify-postgres-log-privacy.py', 'test-postgres-log-privacy.py', 'validate_settings', 'main'),
            ('functional-setup-failure-probe.py', 'test-functional-setup-failure-probe.py', 'observe', '_run'),
        ]
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            data = folder / 'data'
            env = self.environment()
            env['SONAR_PYTHON_SOURCE_ROOT'] = ROOT.as_posix()
            env['COVERAGE_PROCESS_START'] = str(CONFIG)
            env['COVERAGE_FILE'] = str(data)
            expected = []
            for source, suite, positive_name, zero_name in subjects:
                tracked = ROOT / 'scripts/ci' / source
                functions = {node.name: node for node in ast.parse(tracked.read_text(encoding='utf-8')).body
                             if isinstance(node, ast.FunctionDef)}
                positive = functions[positive_name].body[0].lineno
                uncalled = functions[zero_name].body[0]
                if isinstance(uncalled, ast.Try):
                    uncalled = uncalled.body[0]
                expected.append((tracked.resolve(), positive, uncalled.lineno))
                self.run_python([str(ROOT / 'scripts/ci' / suite)], folder, env)
            self.assertEqual(len(list(folder.glob('data.*'))), 2,
                             'Each of the two existing fully mocked suites must write its own real shard.')
            env.pop('COVERAGE_PROCESS_START')
            self.run_python(['-m', 'coverage', 'combine', '--keep', '--rcfile=' + str(CONFIG)], folder, env)
            measured = coverage.CoverageData(basename=str(data))
            measured.read()
            self.assertTrue(measured.has_arcs())
            report = folder / 'coverage.xml'
            self.run_python(['-m', 'coverage', 'xml', '--rcfile=' + str(CONFIG), '-o', str(report)], folder, env)
            document = ET.parse(report).getroot()
            sources = [Path(source.text or '.') for source in document.findall('./sources/source')]
            for tracked, positive, zero in expected:
                with self.subTest(source=tracked.name):
                    files = [name for name in measured.measured_files() if Path(name).resolve() == tracked]
                    self.assertEqual(len(files), 1, 'CoverageData must identify the exact tracked operational source.')
                    executed = set(measured.lines(files[0]))
                    self.assertIn(positive, executed)
                    self.assertNotIn(zero, executed, 'Mocked controls must not invent CLI/default-run execution.')
                    classes = [node for node in document.findall('.//class')
                               if tracked in {(source / node.attrib['filename']).resolve() for source in sources}]
                    self.assertEqual(len(classes), 1, 'Standard XML must retain that same exact source identity.')
                    hits = {int(line.attrib['number']): int(line.attrib['hits'])
                            for line in classes[0].findall('./lines/line')}
                    self.assertGreater(hits.get(positive, 0), 0)
                    self.assertIn(zero, hits)
                    self.assertEqual(hits[zero], 0, 'Original uncalled-line zeros must survive standard XML generation.')

    def test_existing_zap_and_db_suites_measure_real_cli_children_and_preserve_authority_zeros(self):
        subjects = [
            ('scripts/security/process-zap-report.py', 'tests/ci/test_security_zap_attribution.py',
             19, 'load_attribution', 'load_forbidden_values'),
            ('scripts/performance/db-ci.py', 'tests/ci/test_performance_db_ci.py',
             26, 'gate_decision', 'resolve_build'),
        ]
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            data = folder / 'data'
            env = self.environment()
            env['SONAR_PYTHON_SOURCE_ROOT'] = ROOT.as_posix()
            env['COVERAGE_PROCESS_START'] = str(CONFIG)
            env['COVERAGE_FILE'] = str(data)
            expected = []
            for source, suite, cases, positive_name, zero_name in subjects:
                tracked = (ROOT / source).resolve()
                functions = {node.name: node for node in ast.parse(tracked.read_text(encoding='utf-8')).body
                             if isinstance(node, ast.FunctionDef)}
                if zero_name == 'load_forbidden_values':
                    zeros = [node.lineno for node in ast.walk(functions[zero_name])
                             if isinstance(node, ast.Return) and isinstance(node.value, ast.Tuple)
                             and len(node.value.elts) == 2 and isinstance(node.value.elts[0], ast.List)
                             and not node.value.elts[0].elts and isinstance(node.value.elts[1], ast.Constant)
                             and node.value.elts[1].value is True]
                else:
                    zeros = [node.lineno for node in ast.walk(functions[zero_name])
                             if isinstance(node, ast.With) and any(
                                 isinstance(item.context_expr, ast.Call)
                                 and isinstance(item.context_expr.func, ast.Attribute)
                                 and item.context_expr.func.attr == 'urlopen' for item in node.items)]
                self.assertEqual(len(zeros), 1, 'The existing uncalled authority branch must be unambiguous.')
                positive_body = functions[positive_name].body
                first = positive_body[0]
                if isinstance(first, ast.Expr) and isinstance(first.value, ast.Constant) and isinstance(first.value.value, str):
                    first = positive_body[1]
                expected.append((tracked, first.lineno,
                                 functions['main'].body[0].lineno, zeros[0]))
                result = self.run_python([str(ROOT / suite)], folder, env)
                self.assertIn(f'Ran {cases} tests', result.stderr)
            self.assertGreater(len(list(folder.glob('data.*'))), 2,
                               'The two existing suites and their actual CLI children must write separate shards.')
            env.pop('COVERAGE_PROCESS_START')
            self.run_python(['-m', 'coverage', 'combine', '--keep', '--rcfile=' + str(CONFIG)], folder, env)
            measured = coverage.CoverageData(basename=str(data))
            measured.read()
            self.assertTrue(measured.has_arcs())
            report = folder / 'coverage.xml'
            self.run_python(['-m', 'coverage', 'xml', '--rcfile=' + str(CONFIG), '-o', str(report)], folder, env)
            document = ET.parse(report).getroot()
            sources = [Path(source.text or '.') for source in document.findall('./sources/source')]
            for tracked, positive, cli, zero in expected:
                with self.subTest(source=tracked.name):
                    files = [name for name in measured.measured_files() if Path(name).resolve() == tracked]
                    self.assertEqual(len(files), 1, 'CoverageData must identify the exact tracked operational CLI.')
                    executed = set(measured.lines(files[0]))
                    self.assertIn(positive, executed)
                    self.assertIn(cli, executed, 'Child-process coverage must include the real CLI main body.')
                    self.assertNotIn(zero, executed, 'Synthetic controls must not invent unsanitized or network authority.')
                    classes = [node for node in document.findall('.//class')
                               if tracked in {(source / node.attrib['filename']).resolve() for source in sources}]
                    self.assertEqual(len(classes), 1, 'Standard XML must retain the exact same tracked source.')
                    rows = classes[0].findall('./lines/line')
                    hits = {int(line.attrib['number']): int(line.attrib['hits']) for line in rows}
                    self.assertEqual(len(rows), len(hits), 'Original XML must not contain duplicate source line rows.')
                    line_count = len(tracked.read_text(encoding='utf-8').splitlines())
                    self.assertTrue(all(0 < line <= line_count
                                        and count >= 0 for line, count in hits.items()))
                    self.assertGreater(hits.get(positive, 0), 0)
                    self.assertGreater(hits.get(cli, 0), 0)
                    self.assertIn(zero, hits)
                    self.assertEqual(hits[zero], 0, 'Uncalled authority zeros must survive original XML generation.')

    def test_same_suffix_fixture_copy_is_not_repository_source(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            clone = folder / 'scripts/ci/mbj02-trx-receipt.py'
            clone.parent.mkdir(parents=True)
            clone.write_text('print("SYNTHETIC_COPY_CONTROL")\n', encoding='utf-8')
            data = folder / 'data'
            env = self.environment()
            env['SONAR_PYTHON_SOURCE_ROOT'] = ROOT.as_posix()
            env['COVERAGE_PROCESS_START'] = str(CONFIG)
            env['COVERAGE_FILE'] = str(data)
            self.run_python([str(clone)], folder, env)
            self.assertEqual(len(list(folder.glob('data.*'))), 1)
            env.pop('COVERAGE_PROCESS_START')
            self.run_python(['-m', 'coverage', 'combine', '--rcfile=' + str(CONFIG)], folder, env)
            measured = coverage.CoverageData(basename=str(data))
            measured.read()
            self.assertEqual(list(measured.measured_files()), [], 'Temporary copies must not enter source-bound reports.')

    def test_disabled_bootstrap_does_not_dispatch_coverage_startup(self):
        startup = mock.Mock()
        with mock.patch.dict(os.environ), mock.patch.dict(sys.modules, {'coverage': startup}):
            os.environ.pop('COVERAGE_PROCESS_START', None)
            runpy.run_path(str(BOOTSTRAP / 'sitecustomize.py'))
        startup.process_startup.assert_not_called()

    def test_explicit_bootstrap_dispatches_startup_exactly_once_without_arguments(self):
        startup = mock.Mock()
        with mock.patch.dict(os.environ, {'COVERAGE_PROCESS_START': str(CONFIG)}), mock.patch.dict(sys.modules, {'coverage': startup}):
            runpy.run_path(str(BOOTSTRAP / 'sitecustomize.py'))
        startup.process_startup.assert_called_once_with()

    def test_missing_repository_root_fails_configuration(self):
        with mock.patch.dict(os.environ):
            os.environ.pop('SONAR_PYTHON_SOURCE_ROOT', None)
            with self.assertRaisesRegex(coverage.exceptions.CoverageException, 'SONAR_PYTHON_SOURCE_ROOT'):
                coverage.Coverage(config_file=str(CONFIG))


class ExistingDatabaseCoverageDiscoveryTests(unittest.TestCase):
    environment = PythonProcessCoverageTests.environment
    run_python = PythonProcessCoverageTests.run_python

    SUITES = (
        ('test_performance_db.py', 16),
        ('test_performance_db_candidate.py', 8),
        ('test_performance_db_assignment_transition.py', 39),
        ('test_performance_db_approval_history.py', 4),
        ('test_performance_environment_class.py', 9),
        ('test_performance_public_digest.py', 3),
        ('test_performance_runtime_compatibility.py', 11),
        ('test_performance_diagnostic_recovery.py', 10),
    )

    def test_existing_database_suites_have_complete_unique_discovery_and_one_selection(self):
        workflow = (ROOT / 'scripts/ci/sonar-python-controls.sh').read_text(encoding='utf-8')
        loop = workflow.split('for control in \\\n', 1)[1].split('; do', 1)[0]
        loader = unittest.TestLoader()

        def cases(suite):
            for item in suite:
                if isinstance(item, unittest.TestSuite):
                    yield from cases(item)
                else:
                    yield item.id()

        for filename, count in self.SUITES:
            with self.subTest(filename=filename):
                self.assertEqual(loop.count('tests/ci/' + filename), 1)
                name = 'canonical_database_' + Path(filename).stem
                spec = importlib.util.spec_from_file_location(name, ROOT / 'tests/ci' / filename)
                self.assertIsNotNone(spec)
                self.assertIsNotNone(spec.loader)
                module = importlib.util.module_from_spec(spec)
                with mock.patch.object(sys, 'path', [str(ROOT / 'tests/ci'), str(ROOT / 'scripts/performance'), *sys.path]):
                    spec.loader.exec_module(module)
                self.assertFalse(hasattr(module, 'load_tests'), 'A loader hook needs independent completeness review.')
                declared = []
                for value in vars(module).values():
                    if isinstance(value, type) and issubclass(value, unittest.TestCase) and value.__module__ == name:
                        declared.extend(cases(loader.loadTestsFromTestCase(value)))
                selected = list(cases(loader.loadTestsFromModule(module)))
                if filename == 'test_performance_runtime_compatibility.py':
                    # The new runtime-image APT integrity guard must stay in the measured cohort.
                    self.assertIn(
                        name + '.RuntimeCompatibilityTests.test_both_runtime_recipes_fail_closed_on_partial_ubuntu_package_indexes',
                        selected,
                    )
                self.assertEqual(len(declared), count)
                self.assertEqual(len(selected), len(set(selected)))
                self.assertEqual(sorted(selected), sorted(declared))

    def test_selected_database_dispatch_executes_all_cases_without_main_or_test_body_coverage(self):
        workflow = (ROOT / 'scripts/ci/sonar-python-controls.sh').read_text(encoding='utf-8')
        dispatch = workflow.split('; do\n', 1)[1].split('\n          done', 1)[0]
        discovery = dispatch.split('case "$control" in\n', 1)[1].split('            esac', 1)[0]
        self.assertEqual(discovery.count('python3 '), 1)
        self.assertIn('                continue\n', discovery)
        command = next(line.strip() for line in discovery.splitlines() if line.strip().startswith('python3 '))
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            suite = folder / 'tests/ci'
            suite.mkdir(parents=True)
            (suite / 'subject.py').write_text('from pathlib import Path\ndef record(value):\n    with Path("executed.txt").open("a") as output:\n        output.write(value + "\\n")\n', encoding='utf-8')
            config = folder / 'coverage.ini'
            config.write_text('[run]\nbranch = True\nparallel = True\ninclude = ' + (suite / 'subject.py').as_posix() + '\n', encoding='utf-8')
            env = self.environment()
            env['COVERAGE_RCFILE'] = str(config)
            env['COVERAGE_PROCESS_START'] = str(config)
            env['COVERAGE_FILE'] = str(folder / 'data')
            expected = []
            for filename, count in self.SUITES:
                self.assertEqual(discovery.count('tests/ci/' + filename), 1)
                methods = ''
                for index in range(count):
                    identity = filename + ':' + str(index)
                    expected.append(identity)
                    methods += '    def test_case_' + str(index) + '(self):\n        record(' + repr(identity) + ')\n'
                (suite / filename).write_text('import unittest\nfrom subject import record\nclass SyntheticDatabaseTests(unittest.TestCase):\n' + methods, encoding='utf-8')
                args = shlex.split(command.replace('${control##*/}', filename))
                self.assertEqual(args[:5], ['python3', '-B', '-m', 'coverage', 'run'])
                self.assertEqual(args[5:], ['-m', 'unittest', 'discover', '-s', 'tests/ci', '-p', filename])
                result = self.run_python(args[1:], folder, env)
                self.assertIn('Ran ' + str(count) + ' tests', result.stderr)
                self.assertIn('OK', result.stderr)
            executed = (folder / 'executed.txt').read_text(encoding='utf-8').splitlines()
            self.assertEqual(sorted(executed), sorted(expected))
            self.assertEqual(len(executed), 100)
            shards = list(folder.glob('data.*'))
            self.assertEqual(len(shards), len(self.SUITES))
            for shard in shards:
                measured = coverage.CoverageData(basename=str(shard))
                measured.read()
                self.assertTrue(measured.has_arcs())
                self.assertEqual([Path(name).resolve() for name in measured.measured_files()], [(suite / 'subject.py').resolve()])


if __name__ == '__main__':
    unittest.main()
