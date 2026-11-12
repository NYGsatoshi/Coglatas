import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { copyFileSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const verifier = join(root, 'scripts/ci/verify-node-toolchain.mjs');
const fixturePaths = [
  'global.json',
  'Dockerfile',
  'infra/docker/frontend-dev.Dockerfile',
  'infra/docker/backend-dev.Dockerfile',
  'infra/docker/runtime-prebuilt.Dockerfile',
  'package.json',
  'frontend/package.json',
];
const workflowPath = '.github/workflows/node-bootstrap.yml';
const workflow = version => [
  'jobs:',
  '  preflight:',
  '    steps:',
  '      - uses: actions/setup-node@820762786026740c76f36085b0efc47a31fe5020',
  '        with:',
  '          node-version: "' + version + '"',
  '',
].join('\n');

await test('the checked-in Node 26 LTS toolchain guard executes and reports every pinned category', async () => {
  const output = [];
  const originalLog = console.log;
  const originalError = console.error;
  try {
    console.log = (...args) => output.push(args.map(String).join(' '));
    console.error = (...args) => output.push(args.map(String).join(' '));
    // Import the actual policy guard into this test process to measure
    // executable source under native V8 coverage, not subprocess-only claims.
    await import('./verify-node-toolchain.mjs');
  } finally {
    console.log = originalLog;
    console.error = originalError;
  }

  const report = output.join('\n');
  assert.equal(process.exitCode ?? 0, 0, report);
  assert.match(report, /Node toolchain policy passed\./);
  assert.match(report, /Dockerfile: Node 26 \(production frontend dependency stage\)/);
  assert.match(report, /infra\/docker\/frontend-dev\.Dockerfile: Node 26 \(frontend development container\)/);
  assert.match(report, /node-toolchain-preflight\.yml: setup-node 26/);
  assert.match(report, /frontend\/package\.json: npm@11\.17\.0/);
});

await test('the actual guard rejects mismatched Actions, unpinned Docker, and npm version drift', t => {
  const directory = mkdtempSync(join(tmpdir(), 'coglatas-node26-toolchain-'));
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  for (const path of fixturePaths) {
    const target = join(directory, path);
    mkdirSync(dirname(target), { recursive: true });
    copyFileSync(join(root, path), target);
  }
  const workflowTarget = join(directory, workflowPath);
  mkdirSync(dirname(workflowTarget), { recursive: true });
  writeFileSync(workflowTarget, workflow('26'));
  const environment = { ...process.env };
  delete environment.NODE_V8_COVERAGE;
  delete environment.NODE_OPTIONS;
  delete environment.NODE_TEST_CONTEXT;
  const run = () => spawnSync(process.execPath, [verifier], {
    cwd: directory, env: environment, encoding: 'utf8', timeout: 15000,
  });
  const accepted = run();
  assert.equal(accepted.status, 0, accepted.stderr);
  assert.match(accepted.stdout, /Node toolchain policy passed\./);

  writeFileSync(workflowTarget, workflow('24'));
  const wrongWorkflow = run();
  assert.equal(wrongWorkflow.status, 1);
  assert.match(wrongWorkflow.stderr, /configures setup-node 24/);
  writeFileSync(workflowTarget, workflow('26'));

  const dockerTarget = join(directory, 'Dockerfile');
  const dockerContent = readFileSync(dockerTarget, 'utf8');
  const unpinned = dockerContent.replace(/^FROM node:26@sha256:[a-f0-9]{64} AS frontend-deps/m, 'FROM node:26 AS frontend-deps');
  assert.notEqual(unpinned, dockerContent, 'The fixture must replace the real pinned Node base.');
  writeFileSync(dockerTarget, unpinned);
  const wrongDocker = run();
  assert.equal(wrongDocker.status, 1);
  assert.match(wrongDocker.stderr, /expected production frontend dependency stage Node base image/);
  writeFileSync(dockerTarget, dockerContent);

  const manifestTarget = join(directory, 'frontend/package.json');
  const manifest = JSON.parse(readFileSync(manifestTarget, 'utf8'));
  manifest.packageManager = 'npm@11.17.1';
  writeFileSync(manifestTarget, JSON.stringify(manifest));
  const wrongNpm = run();
  assert.equal(wrongNpm.status, 1);
  assert.match(wrongNpm.stderr, /frontend\/package\.json packageManager is npm@11\.17\.1/);
});
