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

    def run_python(self, args, cwd, env):
        result = subprocess.run([sys.executable, '-B', *args], cwd=cwd, env=env,
                                capture_output=True, text=True, encoding='utf-8', timeout=15)
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
        workflow = (ROOT / '.github/workflows/sonarcloud-ci.yml').read_text(encoding='utf-8')
        loop = workflow.split('for control in \\\n', 1)[1].split('; do', 1)[0]
        for name in nested:
            self.assertNotIn('scripts/ci/' + name + '.py', loop)
        for name in ('test_sec_arch_evidence', 'test_sec_arch_github_provenance'):
            self.assertEqual(loop.count('scripts/ci/' + name + '.py'), 1)

    def test_existing_final_and_producer_binding_suites_are_complete_and_selected_once(self):
        workflow = (ROOT / '.github/workflows/sonarcloud-ci.yml').read_text(encoding='utf-8')
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
        workflow = (ROOT / '.github/workflows/sonarcloud-ci.yml').read_text(encoding='utf-8')
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
        workflow = (ROOT / '.github/workflows/sonarcloud-ci.yml').read_text(encoding='utf-8')
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

    def test_subject_scope_branch_and_parallel_configuration_are_explicit(self):
        with mock.patch.dict(os.environ, {'SONAR_PYTHON_SOURCE_ROOT': ROOT.as_posix()}):
            config = coverage.Coverage(config_file=str(CONFIG))
        self.assertTrue(config.get_option('run:branch'))
        self.assertTrue(config.get_option('run:parallel'))
        self.assertEqual(set(config.get_option('run:include')), {
            ROOT.as_posix() + '/scripts/ci/verify-sonar-dotnet-coverage.py',
            ROOT.as_posix() + '/scripts/ci/required_check_contract.py',
            ROOT.as_posix() + '/scripts/ci/check-publication-readiness.py',
            ROOT.as_posix() + '/scripts/ci/check-coglatas-rename.py',
            ROOT.as_posix() + '/scripts/ci/github_action_pins.py',
            ROOT.as_posix() + '/scripts/ci/verify_sbom_vulnerability_gate.py',
            ROOT.as_posix() + '/scripts/ci/verify_sbom.py',
            ROOT.as_posix() + '/scripts/ci/verify_grype_sbom_source.py',
            ROOT.as_posix() + '/scripts/security/finding_policy.py',
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
        ('test_performance_runtime_compatibility.py', 10),
        ('test_performance_diagnostic_recovery.py', 10),
    )

    def test_existing_database_suites_have_complete_unique_discovery_and_one_selection(self):
        workflow = (ROOT / '.github/workflows/sonarcloud-ci.yml').read_text(encoding='utf-8')
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
                self.assertEqual(len(declared), count)
                self.assertEqual(len(selected), len(set(selected)))
                self.assertEqual(sorted(selected), sorted(declared))

    def test_selected_database_dispatch_executes_all_cases_without_main_or_test_body_coverage(self):
        workflow = (ROOT / '.github/workflows/sonarcloud-ci.yml').read_text(encoding='utf-8')
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
            self.assertEqual(len(executed), 99)
            shards = list(folder.glob('data.*'))
            self.assertEqual(len(shards), len(self.SUITES))
            for shard in shards:
                measured = coverage.CoverageData(basename=str(shard))
                measured.read()
                self.assertTrue(measured.has_arcs())
                self.assertEqual([Path(name).resolve() for name in measured.measured_files()], [(suite / 'subject.py').resolve()])


if __name__ == '__main__':
    unittest.main()
