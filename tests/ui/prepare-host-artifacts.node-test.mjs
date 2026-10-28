import assert from 'node:assert/strict';
import { chmodSync, lstatSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { runInNewContext } from 'node:vm';
import test from 'node:test';
import { assertHostArtifactRoot } from './prepare-host-artifacts.mjs';

const helperUrl = new URL('./prepare-host-artifacts.mjs', import.meta.url);
const wrapperUrl = new URL('./run-real-backend-fci04.mjs', import.meta.url);
function withWorkspace(run) {
  const workspace = mkdtempSync(join(tmpdir(), 'coglatas-host-output-'));
  try { return run(workspace); }
  finally { rmSync(workspace, { recursive: true, force: true }); }
}
function prepare(workspace) {
  return spawnSync(process.execPath, ['--input-type=module', '-e',
    `import { prepareHostArtifactRoot } from ${JSON.stringify(helperUrl.href)}; try { prepareHostArtifactRoot(); } catch (error) { console.error(error.message); process.exitCode = 1; }`],
  { cwd: workspace, encoding: 'utf8' });
}
const fakeStat = (overrides = {}) => ({ uid: 42, mode: 0o40700,
  isSymbolicLink: () => false, isDirectory: () => true, ...overrides });

test('creates only the fixed private host-owned parent in a fresh checkout', () => withWorkspace((workspace) => {
  const result = prepare(workspace);
  assert.equal(result.status, 0);
  assert.deepEqual(readdirSync(workspace), ['artifacts']);
  const stat = lstatSync(join(workspace, 'artifacts'));
  assert.equal(stat.uid, process.getuid());
  assert.equal(stat.mode & 0o777, 0o700);
  assert.deepEqual(readdirSync(join(workspace, 'artifacts')), []);
}));
test('preserves an existing owned writable root and all its contents and permissions', () => withWorkspace((workspace) => {
  const root = join(workspace, 'artifacts');
  mkdirSync(root, { mode: 0o750 });
  writeFileSync(join(root, 'existing'), 'unchanged');
  assert.equal(prepare(workspace).status, 0);
  assert.equal(lstatSync(root).mode & 0o777, 0o750);
  assert.equal(readFileSync(join(root, 'existing'), 'utf8'), 'unchanged');
}));
test('rejects a symlink instead of adopting its target', () => withWorkspace((workspace) => {
  const target = join(workspace, 'target');
  mkdirSync(target);
  symlinkSync(target, join(workspace, 'artifacts'));
  assert.notEqual(prepare(workspace).status, 0);
  assert.equal(lstatSync(join(workspace, 'artifacts')).isSymbolicLink(), true);
  assert.deepEqual(readdirSync(target), []);
}));
test('rejects a regular file without deleting or replacing it', () => withWorkspace((workspace) => {
  writeFileSync(join(workspace, 'artifacts'), 'preserve');
  assert.notEqual(prepare(workspace).status, 0);
  assert.equal(readFileSync(join(workspace, 'artifacts'), 'utf8'), 'preserve');
}));
test('rejects unwritable owner mode without repairing permissions', () => withWorkspace((workspace) => {
  const root = join(workspace, 'artifacts');
  mkdirSync(root, { mode: 0o500 });
  try {
    assert.notEqual(prepare(workspace).status, 0);
    assert.equal(lstatSync(root).mode & 0o777, 0o500);
  } finally { chmodSync(root, 0o700); }
}));
test('rejects missing owner search permission even if write is set', () => {
  assert.throws(() => assertHostArtifactRoot(fakeStat({ mode: 0o40200 }), 42));
});
test('rejects a foreign UID even when the directory is world-writable', () => {
  assert.throws(() => assertHostArtifactRoot(fakeStat({ uid: 43, mode: 0o40777 }), 42));
});
test('requires a real runner identity and accepts a valid owner independently of group/world mode', () => {
  assert.throws(() => assertHostArtifactRoot(fakeStat(), null));
  assert.throws(() => assertHostArtifactRoot(fakeStat(), -1));
  assertHostArtifactRoot(fakeStat({ mode: 0o40755 }), 42);
});
test('failure emits a fixed diagnostic without workspace path or foreign UID', () => withWorkspace((workspace) => {
  writeFileSync(join(workspace, 'artifacts'), 'private marker');
  const result = prepare(workspace);
  assert.notEqual(result.status, 0);
  assert.equal(result.stderr, 'Host artifacts directory must be writable and owned by this runner.\n');
  assert.equal(result.stderr.includes(workspace), false);
  assert.equal(result.stderr.includes('private marker'), false);
}));
async function wrapperEvents(gates, failPreparation = false, events = []) {
  const processStub = { argv: ['node', fileURLToPath(wrapperUrl), ...gates], env: {} };
  const source = readFileSync(wrapperUrl, 'utf8')
    .replace(/^import[^\n]+;\n/u, '')
    .replace("await import('./run-real-backend-smoke-compose.mjs');", "await compose();");
  await runInNewContext(`(async () => { ${source} })()`, {
    process: processStub,
    prepareHostArtifactRoot: () => { events.push('host-preparation'); if (failPreparation) { throw new Error('refused'); } },
    compose: async () => { events.push('compose'); }
  });
  return { events, processStub };
}
test('actual canonical entry prepares the host before compose without changing fixed gates', async () => {
  const { events, processStub } = await wrapperEvents(['functional-fast', 'functional-full', 'functional-fast']);
  assert.deepEqual(events, ['host-preparation', 'compose']);
  assert.equal(processStub.env.COGLATAS_FCI04_GATES, 'functional-fast,functional-full');
  assert.equal(processStub.env.COGLATAS_FCI04_ONLY, '1');
  assert.equal(processStub.argv.length, 2);
});
test('invalid gate never prepares output or starts compose', async () => {
  const events = [];
  await assert.rejects(wrapperEvents(['unapproved'], false, events), /Usage:/u);
  assert.deepEqual(events, []);
});
test('host refusal stops the actual entry before compose', async () => {
  const events = [];
  await assert.rejects(wrapperEvents([], true, events), /refused/u);
  assert.deepEqual(events, ['host-preparation']);
});
