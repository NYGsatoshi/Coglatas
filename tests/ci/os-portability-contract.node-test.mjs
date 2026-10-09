import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
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
  verifyTrxCounters
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
  const workflow = (image, runner = 'ubuntu-latest') => `jobs:\n  database:\n    runs-on: ${runner}\n    services:\n      postgres:\n        image: ${JSON.stringify(image)}\n`;
  for (const image of ['postgres:18.6', 'postgres:18.6-alpine', `postgres@sha256:${digest}`, `postgres:18.6@sha256:${digest}`]) {
    assert.doesNotThrow(() => validateLinuxOwnedWorkflowText(workflow(image)), image);
  }
  for (const image of ['postgres', 'postgres:', 'postgres@sha256:short', `postgres@sha256:${digest}extra`,
    `postgres@md5:${digest}`, 'postgres:18.6@sha256:short', 'postgres:18.6/other',
    'unrelated:18.6', 'private.example/postgres:18.6', `postgres-other@sha256:${digest}`]) {
    assert.throws(() => validateLinuxOwnedWorkflowText(workflow(image)), /Ubuntu job that directly owns/u, image);
  }
  assert.throws(() => validateLinuxOwnedWorkflowText(workflow(`postgres@sha256:${digest}`, 'windows-latest')),
    /Ubuntu job that directly owns/u);
  assert.throws(() => validateLinuxOwnedWorkflowText('jobs:\n  database:\n    runs-on: ubuntu-latest\n'),
    /Ubuntu job that directly owns/u);
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
