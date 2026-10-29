"""Real subprocess controls for the isolated pinned coverage bootstrap."""
from pathlib import Path
import os
import runpy
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

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

    def test_subject_scope_branch_and_parallel_configuration_are_explicit(self):
        with mock.patch.dict(os.environ, {'SONAR_PYTHON_SOURCE_ROOT': ROOT.as_posix()}):
            config = coverage.Coverage(config_file=str(CONFIG))
        self.assertTrue(config.get_option('run:branch'))
        self.assertTrue(config.get_option('run:parallel'))
        self.assertEqual(set(config.get_option('run:include')), {
            ROOT.as_posix() + '/scripts/ci/verify-sonar-dotnet-coverage.py',
            ROOT.as_posix() + '/scripts/security/finding_policy.py',
            ROOT.as_posix() + '/scripts/security/schemathesis_policy.py',
            ROOT.as_posix() + '/scripts/ci/run-sec04-deep-ci.py',
            ROOT.as_posix() + '/scripts/ci/mbj02-trx-receipt.py',
            ROOT.as_posix() + '/scripts/ci/functional_diagnostics.py',
            ROOT.as_posix() + '/scripts/ci/thread_probe_diagnostics.py',
            ROOT.as_posix() + '/scripts/ci/sonar-python-coverage-bootstrap/sitecustomize.py',
        })


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


if __name__ == '__main__':
    unittest.main()
