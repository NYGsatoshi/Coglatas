"""One producer per exact consumer candidate; supplemental evidence, never Required CI replacement."""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import platform
import re
import socket
import subprocess
import sys
import time
import urllib.request
sys.dont_write_bytecode = True
from projectide_core_isolated import inventory


def normalized(key, values):
    if key == 'vitestTests':
        values = [dict(item, file=item['file'].replace('\\', '/').split('/frontend/')[-1]) for item in values]
    return sorted(json.dumps(item, sort_keys=True) for item in values)


def require_inventory(key, actual, previous):
    names = normalized(key, actual)
    if not names or len(set(names)) != len(names) or not set(normalized(key, previous)).issubset(names):
        raise ValueError('Empty, duplicated or removed consumer tests: ' + key)


def verify(root: Path, receipt: Path):
    evidence = receipt.parent
    value = json.loads(receipt.read_text(encoding='utf-8'))
    baseline_path = Path(__file__).with_name('projectide-evidence-consumer-baseline.json')
    baseline = json.loads(baseline_path.read_text(encoding='utf-8'))
    producer = evidence / 'composer-source' / 'projectide_web_verify.py'
    spec = importlib.util.spec_from_file_location('pinned_consumer_inventory', producer)
    parser = importlib.util.module_from_spec(spec); spec.loader.exec_module(parser)
    value.update(os=platform.platform(), commands=[], status='VERIFYING', baselinePins=baseline['pins'],
                 verifierSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                 baselineSha256=hashlib.sha256(baseline_path.read_bytes()).hexdigest())
    environment = os.environ.copy()
    environment.update(PYTHONDONTWRITEBYTECODE='1', NG_BUILD_MAX_WORKERS='4', PLAYWRIGHT_SKIP_BUILD='1')
    def execute(name, command, cwd=root):
        with (evidence / (name + '.log')).open('w', encoding='utf-8') as output:
            result = subprocess.run(command, cwd=cwd, env=environment, stdout=output, stderr=subprocess.STDOUT)
        value['commands'].append(dict(name=name, command=command, exitCode=result.returncode))
        print(name, result.returncode, flush=True)
        if result.returncode:
            raise RuntimeError(name + ' failed; see retained log')
    try:
        parser.assert_candidate(root, value)
        value['node'] = subprocess.check_output(['node', '--version'], text=True).strip()
        value['sdk'] = subprocess.check_output(['dotnet', '--version'], cwd=root, text=True).strip()
        if value['sdk'] != json.loads((root / 'global.json').read_text())['sdk']['version']:
            raise ValueError('Incorrect SDK')
        execute('composition-controls', [sys.executable, '-m', 'unittest', 'discover', '-s', 'tests/ci', '-p', 'test_projectide*.py', '-v'])
        execute('fixture-export', ['dotnet', 'run', '--project', 'tools/ProjectIde.Preview', '-c', 'Release', '--', str(evidence / 'preview-reproduced.json')])
        if (evidence / 'preview-reproduced.json').read_bytes() != (root / 'frontend/public/assets/projectide/preview-v1.json').read_bytes():
            raise ValueError('Fixture differs from actual Core exporter')
        execute('dotnet-build', ['dotnet', 'build', 'Coglatas.slnx', '-c', 'Release', '--nologo', '-warnaserror', '--disable-build-servers', '-m:1'])
        for name, project, filter_value in [('projectide', 'tests/Coglatas.Tests/Coglatas.Tests.csproj', 'FullyQualifiedName~ProjectIde'),
                                             ('architecture', 'tests/Coglatas.Architecture.Tests/Coglatas.Architecture.Tests.csproj', None)]:
            command = ['dotnet', 'test', project, '-c', 'Release', '--no-build', '--disable-build-servers', '-m:1']
            selected = ['--filter', filter_value] if filter_value else []
            execute(name + '-discovery', command + selected + ['--list-tests'])
            discovered = sorted(line.strip() for line in (evidence / (name + '-discovery.log')).read_text(encoding='utf-8').splitlines() if line.startswith('    Coglatas.'))
            execute(name, command + selected + ['--logger', 'trx;LogFileName=' + name + '.trx', '--results-directory', str(evidence)])
            actual = inventory(evidence / (name + '.trx'))
            if discovered != actual:
                raise ValueError('Discovery/execution inventory mismatch: ' + name)
            value[name + 'Tests'] = actual
            if name == 'projectide':
                for family in ['RequirementEvidenceValidationTests', 'ResourceReferenceValidationTests', 'CompilerValueReferenceTests', 'SyntheticWorkbench']:
                    if not any(family in test for test in actual):
                        raise ValueError('Missing required test family: ' + family)
        frontend = root / 'frontend'
        cli = ['node', 'node_modules/@angular/cli/bin/ng.js']
        execute('typecheck', ['node', 'node_modules/typescript/bin/tsc', '--noEmit', '-p', 'tsconfig.app.json'], frontend)
        execute('frontend-architecture', ['node', 'scripts/check-architecture.mjs'], frontend)
        execute('route-inventory', ['node', 'scripts/check-av-mig-source.mjs'], frontend)
        execute('frontend-node-tests', ['node', '--test', '--test-reporter=tap', *[str(p.relative_to(frontend)) for p in sorted((frontend / 'scripts').glob('*.node-test.mjs'))]], frontend)
        node_log = (evidence / 'frontend-node-tests.log').read_text(encoding='utf-8')
        counts = {key: int(re.search(r'^# ' + key + r' (\d+)$', node_log, re.M)[1]) for key in ['tests', 'pass', 'fail', 'cancelled', 'skipped', 'todo']}
        if not counts['tests'] or counts['pass'] != counts['tests'] or any(counts[key] for key in ['fail', 'cancelled', 'skipped', 'todo']):
            raise ValueError('Incomplete Node tests')
        value['frontendNodeCounts'] = counts; value['frontendNodeTests'] = re.findall(r'^# Subtest: (.+)$', node_log, re.M)
        execute('angular-build', cli + ['build', '--configuration', 'production'], frontend)
        execute('sanitize-themes', ['node', 'scripts/sanitize-syncfusion-theme-css.mjs'], frontend)
        command = cli + ['test', '--watch=false', '--reporters=json', '--output-file=' + str(evidence / 'vitest.json')]
        for pattern in ['src/app/features/projectide/*.spec.ts', 'src/app/features/projects/project-detail*.spec.ts', 'src/app/features/projects/project-detail-page/*.spec.ts',
                        'src/app/features/projects/task-table/*.spec.ts', 'src/app/shared/ui/adapters/syncfusion/*.spec.ts', 'src/app/app.routes.spec.ts',
                        'src/app/core/feature-flags/frontend-feature-flags.service.spec.ts', 'src/app/core/auth/auth-session.guard.spec.ts', 'src/app/core/i18n/i18n.service.spec.ts']:
            command += ['--include', pattern]
        execute('vitest', command, frontend)
        tests = json.loads((evidence / 'vitest.json').read_text(encoding='utf-8'))
        value['vitestCounts'] = {key: tests[key] for key in ['numTotalTests', 'numPassedTests', 'numFailedTests', 'numPendingTests', 'numTodoTests']}
        value['vitestTests'] = parser.vitest_inventory(tests)
        with socket.socket() as probe:
            probe.bind(('127.0.0.1', 0)); port = probe.getsockname()[1]
        static_root = (frontend / 'dist/coglatas-web').resolve()
        expected = hashlib.sha256((static_root / 'index.html').read_bytes()).hexdigest()
        origin = 'http://127.0.0.1:' + str(port)
        environment.update(PLAYWRIGHT_PORT=str(port), PLAYWRIGHT_STATIC_ROOT=str(static_root), PLAYWRIGHT_BASE_URL=origin,
                           PLAYWRIGHT_JUNIT_OUTPUT_FILE=str(evidence / 'playwright.xml'))
        with (evidence / 'browser-server.log').open('w', encoding='utf-8') as server_log:
            server = subprocess.Popen(['node', 'tests/ui/serve-static.mjs', '--port', str(port)], cwd=root, env=environment, stdout=server_log, stderr=subprocess.STDOUT)
            try:
                for attempt in range(100):
                    if server.poll() is not None:
                        raise ValueError('Owned browser server failed to start')
                    try:
                        with urllib.request.urlopen(origin + '/app', timeout=1) as response:
                            actual = hashlib.sha256(response.read()).hexdigest()
                        break
                    except OSError:
                        time.sleep(.1)
                else:
                    raise ValueError('Owned browser server did not become ready')
                if actual != expected:
                    raise ValueError('Served index does not match exact candidate build')
                value['browserBinding'] = dict(port=port, root=str(static_root), servedIndexSha256=actual, expectedIndexSha256=expected, retries=0, forbidOnly=True)
                execute('playwright', ['node', 'node_modules/@playwright/test/cli.js', 'test', 'tests/ui/projectide-workbench.spec.ts', 'tests/ui/angular-smoke.spec.ts',
                    '--grep', 'ProjectIDE synthetic|canonical Project Kanban|maintained Project Task List|canonical Schedule tab|maintained read-only Schedule',
                    '--retries=0', '--forbid-only', '--reporter=list,junit', '--output=' + str(evidence / 'browser')])
                if server.poll() is not None:
                    raise ValueError('Owned browser server exited during verification')
                value['browserTests'] = parser.browser_inventory(evidence / 'playwright.xml')
            finally:
                server.terminate(); server.wait(timeout=10)
        for key in ['projectideTests', 'architectureTests', 'vitestTests', 'frontendNodeTests', 'browserTests']:
            require_inventory(key, value[key], baseline[key])
        parser.assert_candidate(root, value)
        value['status'] = 'LOCAL_VERIFIED' if platform.system() == 'Windows' else 'ISOLATED_CI_VERIFIED'
    except Exception as error:
        value.update(status='FAILED', failure=str(error)); raise
    finally:
        value['artifactHashes'] = {str(p.relative_to(evidence)).replace('\\', '/'): hashlib.sha256(p.read_bytes()).hexdigest()
                                   for p in sorted(evidence.rglob('*')) if p.is_file() and p != receipt}
        receipt.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    argument_parser = argparse.ArgumentParser(description=__doc__)
    argument_parser.add_argument('--worktree', type=Path, required=True); argument_parser.add_argument('--receipt', type=Path, required=True)
    args = argument_parser.parse_args(); verify(args.worktree.resolve(), args.receipt.resolve())
