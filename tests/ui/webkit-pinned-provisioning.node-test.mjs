import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdtemp, mkdir, readFile, writeFile, chmod, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';

const runner = path.resolve('scripts/ci/run-webkit-pinned.mjs');
const sha = 'a'.repeat(40);
const digest = 'sha256:06a9939e57531807f8d5fd76ce44b53165ffb7d7501d87ab10e285c20b1e971f';
const id = 'b'.repeat(64);
const fakeDocker = `#!/usr/bin/env node
const fs = require('fs');
const args = process.argv.slice(2);
fs.appendFileSync(process.env.COMMAND_LOG, JSON.stringify(args) + '\\n');
if (args[0] === process.env.FAIL_COMMAND) process.exit(7);
if (args[0] === 'image') console.log(JSON.stringify([{Os:'linux',Architecture:process.env.BAD_ARCH || 'amd64',Id:'sha256:test',RepoDigests:process.env.BAD_DIGEST ? [] : ['mcr.microsoft.com/playwright@${digest}']} ]));
if (args[0] === 'create') { fs.writeFileSync(process.env.CREATED, 'true'); console.log('${id}'); }
if (args[0] === 'ps' && fs.existsSync(process.env.CREATED)) {
  if (process.env.HOLD_CLEANUP) setTimeout(() => console.log('${id}'), 500);
  else console.log('${id}');
}
if (args[0] === 'inspect') console.log(JSON.stringify({Running:!!process.env.CONTAINER_RUNNING,Status:process.env.CONTAINER_RUNNING ? 'running' : 'exited',ExitCode:Number(process.env.CONTAINER_EXIT || '0')}));
if (args[0] === 'start' && process.env.HOLD_START) setInterval(() => {}, 1000);
`;

async function fixture(t, extra = {}) {
  const root = await mkdtemp(path.join(os.tmpdir(), 'webkit-provisioning-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  await mkdir(path.join(root, 'bin'));
  await mkdir(path.join(root, 'artifacts/ci'), { recursive: true });
  await writeFile(path.join(root, 'artifacts/ci/frontend-build-sha'), `${sha}\n`);
  for (const [name, source] of [['docker', fakeDocker], ['git', `#!/usr/bin/env node\nconsole.log('${sha}');\n`]]) {
    await writeFile(path.join(root, 'bin', name), source);
    await chmod(path.join(root, 'bin', name), 0o755);
  }
  const log = path.join(root, 'commands.jsonl');
  const env = { ...process.env, PATH: `${path.join(root, 'bin')}${path.delimiter}${process.env.PATH}`,
    GITHUB_SHA: sha, COMMAND_LOG: log, CREATED: path.join(root, 'created'), ...extra };
  return { root, env, log };
}

function launch(f, mode) {
  const child = spawn(process.execPath, [runner, mode], { cwd: f.root, env: f.env, stdio: ['ignore', 'pipe', 'pipe'] });
  let output = '';
  child.stdout.on('data', (data) => { output += data; });
  child.stderr.on('data', (data) => { output += data; });
  return { child, done: new Promise((resolve, reject) => {
    child.on('error', reject);
    child.on('close', (code) => resolve({ code, output }));
  }) };
}

async function commands(f) {
  try { return (await readFile(f.log, 'utf8')).trim().split('\n').filter(Boolean).map((line) => JSON.parse(line)); }
  catch (error) { if (error.code === 'ENOENT') return []; throw error; }
}

// Node's test runner owns completion and reports callback failures.
void test('WebKit preparation explicitly acquires the immutable Linux image and fails closed', async (t) => {
  const ok = await fixture(t);
  const prepared = await launch(ok, 'prepare').done;
  assert.equal(prepared.code, 0, prepared.output);
  const calls = await commands(ok);
  assert.deepEqual(calls[0], ['pull', '--platform', 'linux/amd64', `mcr.microsoft.com/playwright:v1.64.0-noble@${digest}`]);
  const failed = await fixture(t, { FAIL_COMMAND: 'pull' });
  assert.notEqual((await launch(failed, 'prepare').done).code, 0);
  assert.equal((await commands(failed)).some((args) => args[0] === 'create'), false);
  const wrong = await fixture(t, { BAD_ARCH: 'arm64' });
  assert.notEqual((await launch(wrong, 'prepare').done).code, 0);
  const digestMismatch = await fixture(t, { BAD_DIGEST: '1' });
  assert.notEqual((await launch(digestMismatch, 'prepare').done).code, 0);
});

void test('WebKit execution preserves source binding, canonical selection and output ownership without pulling', async (t) => {
  const f = await fixture(t);
  const executed = await launch(f, 'run').done;
  assert.equal(executed.code, 0, executed.output);
  const calls = await commands(f);
  assert.equal(calls.some((args) => args[0] === 'pull'), false);
  const create = calls.find((args) => args[0] === 'create');
  assert.equal(create[create.indexOf('--pull') + 1], 'never');
  assert.equal(create[create.indexOf('--user') + 1], `${process.getuid()}:${process.getgid()}`);
  assert.ok(create.includes('PLAYWRIGHT_SKIP_BUILD=1'));
  assert.equal(create.at(-1), 'node scripts/ci/verify-webkit-pinned-runtime.mjs && npm run test:ui:compat-critical -- --profile browser-engine -- --project=webkit-desktop');
  assert.deepEqual(calls.find((args) => args[0] === 'rm'), ['rm', '--force', id]);
});

void test('WebKit refuses a mismatched producer before creating any container', async (t) => {
  const f = await fixture(t);
  await writeFile(path.join(f.root, 'artifacts/ci/frontend-build-sha'), 'c'.repeat(40));
  assert.notEqual((await launch(f, 'run').done).code, 0);
  assert.equal((await commands(f)).some((args) => args[0] === 'create'), false);
});

void test('WebKit propagates failed container execution and cannot pass a running container', async (t) => {
  const f = await fixture(t, { CONTAINER_EXIT: '19' });
  const failed = await launch(f, 'run').done;
  assert.equal(failed.code, 19, failed.output);
  assert.deepEqual((await commands(f)).find((args) => args[0] === 'rm'), ['rm', '--force', id]);
  const startFailed = await fixture(t, { FAIL_COMMAND: 'start' });
  assert.notEqual((await launch(startFailed, 'run').done).code, 0);
  const running = await fixture(t, { CONTAINER_RUNNING: '1' });
  assert.notEqual((await launch(running, 'run').done).code, 0);
  const missingImage = await fixture(t, { FAIL_COMMAND: 'image' });
  assert.notEqual((await launch(missingImage, 'run').done).code, 0);
  assert.equal((await commands(missingImage)).some((args) => args[0] === 'create'), false);
});

void test('WebKit cancellation fails and removes only the invocation-owned immutable container ID', async (t) => {
  const f = await fixture(t, { HOLD_START: '1' });
  const execution = launch(f, 'run');
  const deadline = Date.now() + 10000;
  while (!(await commands(f)).some((args) => args[0] === 'start')) {
    if (Date.now() > deadline) throw new Error('Fake Docker did not enter execution.');
    await new Promise((resolve) => setTimeout(resolve, 20));
  }
  execution.child.kill('SIGTERM');
  const result = await execution.done;
  assert.equal(result.code, 143, result.output);
  const calls = await commands(f);
  const select = calls.find((args) => args[0] === 'ps');
  assert.ok(select.some((arg) => arg.startsWith('label=coglatas.compat-webkit-owner=')));
  assert.ok(select.some((arg) => arg.startsWith('name=^/coglatas-compat-webkit-')));
  assert.deepEqual(calls.find((args) => args[0] === 'rm'), ['rm', '--force', id]);
});

void test('WebKit cancellation during cleanup preserves failure and completes owned removal', async (t) => {
  const f = await fixture(t, { HOLD_CLEANUP: '1' });
  const execution = launch(f, 'run');
  const deadline = Date.now() + 10000;
  while (!(await commands(f)).some((args) => args[0] === 'ps')) {
    if (Date.now() > deadline) throw new Error('Fake Docker did not enter cleanup.');
    await new Promise((resolve) => setTimeout(resolve, 20));
  }
  execution.child.kill('SIGTERM');
  const result = await execution.done;
  assert.equal(result.code, 143, result.output);
  assert.deepEqual((await commands(f)).find((args) => args[0] === 'rm'), ['rm', '--force', id]);
});
