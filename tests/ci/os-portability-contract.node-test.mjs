import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
import * as yaml from 'js-yaml';
import test from 'node:test';

import {
  findCaseInsensitiveCollisions,
  loadOsPortabilityContract,
  npmVersionCommand,
  validateLinuxOwnedWorkflowText,
  validateOsPortabilityContract,
  validateRunnerRoutingRegistry,
  validateWorkflowText,
  verifyRepositoryOsPortability
} from '../../scripts/ci/os-portability-contract.mjs';
import {
  readTrxCounters,
  verifyTrxCounters,
  verifyTrxFile
} from '../../scripts/ci/verify-os-portability-results.mjs';

test('repository OS portability contract is complete and source-resolvable', async () => {
  const result = await verifyRepositoryOsPortability(process.cwd());

  assert.deepEqual(result.matrix, ['ubuntu-latest', 'windows-latest', 'macos-latest']);
  assert.equal(result.portableTestClasses, 5);
  assert.equal(result.minimumDotnetTests, 25);
  assert.ok(result.trackedPaths > 0);
  assert.equal(result.runtimeValidated, false);
});

test('contract rejects a missing OS and a browser execution Cartesian product', async () => {
  const source = await loadOsPortabilityContract();
  const missingWindows = structuredClone(source);
  missingWindows.matrix = ['ubuntu-latest', 'macos-latest'];
  assert.throws(() => validateOsPortabilityContract(missingWindows), /matrix must be exactly/u);

  const browserProduct = structuredClone(source);
  browserProduct.compatCritical.executeBrowserInMatrix = true;
  assert.throws(() => validateOsPortabilityContract(browserProduct), /must not execute browsers/u);
});

test('Linux database ownership accepts official tagged and digest-pinned PostgreSQL only', () => {
  const digest = 'a'.repeat(64);
  const workflow = (image, runner = 'ubuntu-24.04') => `jobs:\n  database:\n    runs-on: ${runner}\n    services:\n      postgres:\n        image: ${JSON.stringify(image)}\n`;
  for (const image of ['postgres:18.6', 'postgres:18.6-alpine', `postgres@sha256:${digest}`, `postgres:18.6@sha256:${digest}`]) {
    assert.doesNotThrow(() => validateLinuxOwnedWorkflowText(workflow(image)), image);
  }
  for (const image of ['postgres', 'postgres:', 'postgres@sha256:short', `postgres@sha256:${digest}extra`,
    `postgres@md5:${digest}`, 'postgres:18.6@sha256:short', 'postgres:18.6/other',
    'unrelated:18.6', 'private.example/postgres:18.6', `postgres-other@sha256:${digest}`]) {
    assert.throws(() => validateLinuxOwnedWorkflowText(workflow(image)), /Ubuntu job that directly owns/u, image);
  }
  for (const runner of ['ubuntu-latest', 'ubuntu-26.04', 'windows-latest']) {
    assert.throws(() => validateLinuxOwnedWorkflowText(workflow(`postgres@sha256:${digest}`, runner)),
      /Ubuntu job that directly owns/u, runner);
  }
  assert.throws(() => validateLinuxOwnedWorkflowText('jobs:\n  database:\n    runs-on: ubuntu-latest\n'),
    /Ubuntu job that directly owns/u);
});

test('CI PostgreSQL services use the reviewed immutable PostgreSQL 18.7 image', async () => {
  const qualifiedImage = 'image: postgres:18.7@sha256:64c8b0d31e4ab6ea665e8426ea406a94bf52de3c05b3a4a8bf713704eefa9328 # PostgreSQL 18.7';
  const paths = [
    '.github/workflows/ci.yml',
    '.github/workflows/main-validation.yml',
    '.github/workflows/sec17-deep.yml',
    '.github/workflows/sec17-file-policy.yml',
    '.github/workflows/sonarcloud-ci.yml',
  ];
  for (const path of paths) {
    const source = await readFile(path, 'utf8');
    const imageLines = source.split('\n')
      .map((line) => line.trim())
      .filter((line) => line.startsWith('image: postgres'));
    assert.deepEqual(imageLines, [qualifiedImage], `Unqualified PostgreSQL service image in ${path}`);
  }
});

test('workflow rejects ignored OS failures, services, shell overrides, and mutable action refs', async () => {
  const contract = await loadOsPortabilityContract();
  const workflow = await readFile(contract.workflow, 'utf8');
  const allowlist = JSON.parse(await readFile('governance/github-actions-allowlist.json', 'utf8'));

  const mutations = [
    ['continue-on-error', `${workflow}\ncontinue-on-error: true\n`],
    ['services', `${workflow}\nservices:\n  postgres:\n`],
    ['Workflow root must not declare shell', `${workflow}\nshell: bash\n`],
    ['bounded matrix runner routing', workflow.replace(/^\s*runs-on:.*matrix\.os.*$/mu, '    runs-on: ${{ matrix.os }}')],
    ['must use immutable actions/checkout@', workflow.replace(allowlist.actions['actions/checkout'].sha, 'v7')]
  ];
  for (const [message, candidate] of mutations) {
    assert.throws(() => validateWorkflowText(contract, candidate, allowlist), new RegExp(message, 'u'));
  }
});

test('runner registry binds the matrix to approved GitHub-hosted labels', async () => {
  const contract = await loadOsPortabilityContract();
  const registry = JSON.parse(await readFile('governance/workflow-trust-policy.json', 'utf8'));
  assert.doesNotThrow(() => validateRunnerRoutingRegistry(contract, registry));

  const missingWindows = structuredClone(registry);
  Reflect.set(missingWindows.runner_routing, 'github_hosted_labels', ['ubuntu-latest', 'macos-latest']);
  assert.throws(() => validateRunnerRoutingRegistry(contract, missingWindows), /every portability matrix label/u);

  const missingExpression = structuredClone(registry);
  Reflect.set(missingExpression.runner_routing, 'approved_dynamic_expressions', []);
  assert.throws(() => validateRunnerRoutingRegistry(contract, missingExpression), /bounded portability runner expression/u);
});

test('workflow keeps native Git tool-only changes in the automatic Main OS matrix', async () => {
  const contract = await loadOsPortabilityContract();
  const workflow = await readFile(contract.workflow, 'utf8');
  const allowlist = JSON.parse(await readFile('governance/github-actions-allowlist.json', 'utf8'));
  assert.doesNotThrow(() => validateWorkflowText(contract, workflow, allowlist));

  const omitted = workflow.replace(/^\s*- "tools\/Coglatas\.SecurityArchitecture\/\*\*"\r?\n/mu, '');
  assert.notEqual(omitted, workflow);
  assert.throws(() => validateWorkflowText(contract, omitted, allowlist), /Main push paths must include tools/u);
  const otherBranch = workflow.replace('branches: ["main"]', 'branches: ["development"]');
  assert.notEqual(otherBranch, workflow);
  assert.throws(() => validateWorkflowText(contract, otherBranch, allowlist), /Main push paths must include tools/u);
});

test('npm version probe uses the Windows command shim through cmd.exe only on Windows', () => {
  assert.deepEqual(npmVersionCommand('win32'), {
    command: 'cmd.exe',
    args: ['/d', '/s', '/c', 'npm --version']
  });
  assert.deepEqual(npmVersionCommand('linux'), { command: 'npm', args: ['--version'] });
  assert.deepEqual(npmVersionCommand('darwin'), { command: 'npm', args: ['--version'] });
});

test('case-insensitive path collisions fail even when exact paths differ', () => {
  assert.deepEqual(findCaseInsensitiveCollisions(['src/App.cs', 'src/app.cs', 'README.md']), [
    ['src/App.cs', 'src/app.cs']
  ]);
  assert.deepEqual(findCaseInsensitiveCollisions(['src/App.cs', 'README.md']), []);
});

test('portable TRX verifier rejects zero, skipped, and failed selections', () => {
  const passing = counters({ total: 25, executed: 25, passed: 25 });
  assert.deepEqual(verifyTrxCounters(passing, 25), passing);

  assert.throws(() => verifyTrxCounters(counters({}), 25), /below required minimum/u);
  assert.throws(
    () => verifyTrxCounters(counters({ total: 25, executed: 24, passed: 24, notExecuted: 1 }), 25),
    /notExecuted is non-zero/u
  );
  assert.throws(
    () => verifyTrxCounters(counters({ total: 25, executed: 25, passed: 24, failed: 1 }), 25),
    /failed is non-zero/u
  );
});

test('portable TRX parser requires the complete counter shape', () => {
  const complete = `<TestRun><ResultSummary><Counters ${Object.entries(counters({ total: 25, executed: 25, passed: 25 }))
    .map(([name, value]) => `${name}="${value}"`)
    .join(' ')} /></ResultSummary></TestRun>`;
  assert.equal(readTrxCounters(complete).total, 25);
  assert.throws(() => readTrxCounters('<TestRun />'), /Counters element is missing/u);
  assert.throws(() => readTrxCounters('<Counters total="25" />'), /attribute is missing/u);
});


test('OS contract refuses weakened boundaries, toolchains and source identities', async () => {
  const source = await loadOsPortabilityContract();
  const mutations = [
    ['schema', c => { c.schemaVersion = 2; }, /schemaVersion must be 1/u],
    ['issue', c => { c.issue = 0; }, /Issue #590/u],
    ['workflow escape', c => { c.workflow = '../escape.yml'; }, /normalized repository-relative path/u],
    ['duplicate matrix', c => { c.matrix = ['ubuntu-latest', 'ubuntu-latest']; }, /duplicates/u],
    ['node major', c => { c.toolchain.nodeMajor = 0; }, /positive integer/u],
    ['floating npm', c => { c.toolchain.npmVersion = 'latest'; }, /exact semantic version/u],
    ['global SDK path', c => { c.toolchain.dotnetGlobalJson = '/tmp/foreign.json'; }, /normalized repository-relative path/u],
    ['dependency roots', c => { c.dependencyRoots = ['frontend', '.']; }, /must be exactly/u],
    ['solution path', c => { c.dotnet.solution = '../outside.slnx'; }, /normalized repository-relative path/u],
    ['build mode', c => { c.dotnet.configuration = 'Debug'; }, /must be Release/u],
    ['test filter', c => { c.dotnet.testFilter = 'Other'; }, /Portability=CrossPlatform/u],
    ['test minimum', c => { c.dotnet.minimumTests = 0; }, /positive integer/u],
    ['test trait', c => { c.dotnet.trait.value = 'Other'; }, /must declare Portability=CrossPlatform/u],
    ['missing class roster', c => { c.dotnet.testClasses = []; }, /at least one class/u],
    ['class name', c => { c.dotnet.testClasses[0].className = '2Invalid'; }, /Invalid portable test class name/u],
    ['duplicate class', c => { c.dotnet.testClasses[1].className = c.dotnet.testClasses[0].className; }, /Duplicate portable test class entry/u],
    ['duplicate class file', c => { c.dotnet.testClasses[1].path = c.dotnet.testClasses[0].path; }, /Duplicate portable test class entry/u],
    ['inactive frontend', c => { c.frontend.workspace = 'inactive'; }, /active frontend directory/u],
    ['missing build', c => { c.frontend.buildScript = ''; }, /non-empty string/u],
    ['missing helpers', c => { c.frontend.helperScripts = []; }, /non-empty array/u],
    ['wrong compatibility profile', c => { c.compatCritical.profile = 'mobile'; }, /COMPAT-04 os-portability profile/u],
    ['browser execution', c => { c.boundaries.realBrowser.matrixMode = 'included'; }, /matrixMode must be excluded/u],
    ['DB execution', c => { c.boundaries.databaseIntegration.matrixMode = 'included'; }, /matrixMode must be excluded/u],
    ['Docker execution', c => { c.boundaries.docker.matrixMode = 'included'; }, /matrixMode must be excluded/u],
    ['DB owner', c => { c.boundaries.databaseIntegration.ownerWorkflow = 'other.yml'; }, /Database integration must remain owned/u],
    ['Docker owner', c => { c.boundaries.docker.ownerWorkflow = 'other.yml'; }, /Docker validation must remain owned/u],
    ['browser owner', c => { c.boundaries.realBrowser.ownerIssue = 0; }, /Issue #587/u],
    ['missing assumptions', c => { c.assumptions = []; }, /non-empty array/u],
    ['assumption bypass', c => { c.assumptions[0].matrixHandling = 'excluded'; }, /matrixHandling must be portable/u],
    ['assumption missing enforcement', c => { c.assumptions[0].enforcement = ''; }, /non-empty string/u],
    ['duplicate assumption', c => { c.assumptions[1].id = c.assumptions[0].id; }, /duplicates/u]
  ];
  for (const [label, mutate, expected] of mutations) {
    const candidate = structuredClone(source);
    mutate(candidate);
    assert.throws(() => validateOsPortabilityContract(candidate), expected, label);
  }
});

test('OS workflow rejects missing gates, mutable actions and unapproved execution', async () => {
  const contract = await loadOsPortabilityContract();
  const workflowText = await readFile(contract.workflow, 'utf8');
  const allowlist = JSON.parse(await readFile('governance/github-actions-allowlist.json', 'utf8'));
  const baseline = yaml.load(workflowText);
  const step = (c, id) => c.jobs.portability.steps.find(entry => entry.id === id);
  const mutations = [
    ['main branch', c => { c.on.push.branches = ['elsewhere']; }, /Main push paths/u],
    ['source boundary', c => { c.on.push.paths = ['src/**']; }, /Main push paths/u],
    ['extra job', c => { c.jobs.unreviewed = { 'runs-on': 'ubuntu-latest' }; }, /exactly one job/u],
    ['privilege expansion', c => { c.permissions.contents = 'write'; }, /permissions.contents must be read/u],
    ['root container', c => { c.container = 'ubuntu:latest'; }, /Workflow root must not declare container/u],
    ['matrix order', c => { c.jobs.portability.strategy.matrix.os.reverse(); }, /workflow matrix must be exactly/u],
    ['fail-fast', c => { c.jobs.portability.strategy['fail-fast'] = true; }, /matrix fail-fast must be false/u],
    ['parallelism', c => { c.jobs.portability.strategy['max-parallel'] = 8; }, /matrix max-parallel must be 3/u],
    ['runner', c => { c.jobs.portability['runs-on'] = 'ubuntu-latest'; }, /bounded matrix runner routing/u],
    ['job title', c => { c.jobs.portability.name = 'generic'; }, /OS-specific job identity/u],
    ['timeout', c => { c.jobs.portability['timeout-minutes'] = 61; }, /bounded timeout/u],
    ['services', c => { c.jobs.portability.services = {}; }, /jobs.portability must not declare services/u],
    ['missing steps', c => { c.jobs.portability.steps = []; }, /Missing required portability step id/u],
    ['duplicate step', c => { c.jobs.portability.steps.push({ ...step(c, 'checkout') }); }, /Duplicate portability step id/u],
    ['ignored failure', c => { step(c, 'dotnet_tests')['continue-on-error'] = true; }, /must not declare continue-on-error/u],
    ['shell override', c => { step(c, 'dotnet_tests').shell = 'bash'; }, /must not override shell/u],
    ['mutable checkout', c => { step(c, 'checkout').uses = 'actions/checkout@v7'; }, /must use immutable actions\/checkout/u],
    ['missing npm command', c => { step(c, 'npm_toolchain').run = 'npm --version'; }, /missing required command/u],
    ['persist credentials', c => { step(c, 'checkout').with['persist-credentials'] = true; }, /persist-credentials: false/u],
    ['dotnet SDK source', c => { step(c, 'setup_dotnet').with['global-json-file'] = 'other.json'; }, /setup-dotnet must use/u],
    ['node runtime', c => { step(c, 'setup_node').with['node-version'] = '20'; }, /setup-node must use Node/u],
    ['conditional evidence', c => { step(c, 'evidence').if = 'success()'; }, /Evidence creation must run/u],
    ['conditional upload', c => { step(c, 'upload_evidence').if = 'success()'; }, /Evidence upload must run/u],
    ['shared evidence name', c => { step(c, 'upload_evidence').with.name = 'generic'; }, /OS- and attempt-specific/u],
    ['ignored absent artifact', c => { step(c, 'upload_evidence').with['if-no-files-found'] = 'warn'; }, /must fail when no files/u],
    ['Docker command', c => { step(c, 'contract').run += '\ndocker compose up'; }, /Docker execution/u],
    ['browser command', c => { step(c, 'contract').run += '\nnpx playwright test'; }, /browser execution/u],
    ['hard-coded temp path', c => { step(c, 'contract').run += '\necho /tmp/result'; }, /hard-coded \/tmp path/u],
    ['OS-specific chmod', c => { step(c, 'contract').run += '\nchmod +x script.sh'; }, /GNU or executable-bit command/u]
  ];
  for (const [label, mutate, expected] of mutations) {
    const candidate = structuredClone(baseline);
    mutate(candidate);
    assert.throws(() => validateWorkflowText(contract, yaml.dump(candidate), allowlist), expected, label);
  }
});

test('original portable TRX admission rejects absent and invalid reports', async () => {
  // mkdtemp creates an owner-private fixture; avoid the publicly writable system temp root.
  const directory = await mkdtemp(path.join(os.homedir(), 'coglatas-portable-trx-'));
  const report = path.join(directory, 'portable.trx');
  try {
    await assert.rejects(() => verifyTrxFile(report, 25), /Unable to read portable .NET TRX/u);
    const passing = counters({ total: 25, executed: 25, passed: 25 });
    const attrs = Object.entries(passing).map(([name, value]) => name + '="' + value + '"').join(' ');
    await writeFile(report, '<TestRun><Counters ' + attrs + '/></TestRun>', 'utf8');
    assert.deepEqual(await verifyTrxFile(report, 25), passing);
    await writeFile(report, '<TestRun><Counters total="25" /></TestRun>', 'utf8');
    await assert.rejects(() => verifyTrxFile(report, 25), /attribute is missing/u);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test('portable TRX CLI rejects unsupported and incomplete options', () => {
  for (const args of [['--unknown'], ['--contract'], ['--trx']]) {
    const result = spawnSync(process.execPath,
      ['scripts/ci/verify-os-portability-results.mjs', ...args], { encoding: 'utf8' });
    assert.notEqual(result.status, 0, args.join(' '));
    assert.match(result.stderr, /Unknown argument|requires a value/u);
  }
});


test('portable TRX command executes the original minimum and rejects a failed attempt', async () => {
  const contract = await loadOsPortabilityContract();
  const minimum = contract.dotnet.minimumTests;
  // mkdtemp creates an owner-private fixture; avoid the publicly writable system temp root.
  const directory = await mkdtemp(path.join(os.homedir(), 'coglatas-trx-command-'));
  const report = path.join(directory, 'portable.trx');
  const run = () => spawnSync(process.execPath,
    ['scripts/ci/verify-os-portability-results.mjs', '--trx', report], { encoding: 'utf8' });
  const xml = countersValue => '<TestRun><Counters ' +
    Object.entries(countersValue).map(([name, value]) => name + '="' + value + '"').join(' ') +
    '/></TestRun>';
  try {
    await writeFile(report, xml(counters({ total: minimum, executed: minimum, passed: minimum })), 'utf8');
    const accepted = run();
    assert.equal(accepted.status, 0, accepted.stderr);
    assert.match(accepted.stdout, /Portable \.NET TRX passed/u);
    await writeFile(report, xml(counters({ total: minimum, executed: minimum, passed: minimum - 1, failed: 1 })), 'utf8');
    const rejected = run();
    assert.notEqual(rejected.status, 0);
    assert.match(rejected.stderr, /Portable \.NET TRX verification failed/u);
    assert.match(rejected.stderr, /failed is non-zero/u);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

function counters(overrides) {
  return {
    total: 0,
    executed: 0,
    passed: 0,
    failed: 0,
    error: 0,
    timeout: 0,
    aborted: 0,
    inconclusive: 0,
    notRunnable: 0,
    notExecuted: 0,
    disconnected: 0,
    warning: 0,
    completed: 0,
    inProgress: 0,
    pending: 0,
    passedButRunAborted: 0,
    ...overrides
  };
}
