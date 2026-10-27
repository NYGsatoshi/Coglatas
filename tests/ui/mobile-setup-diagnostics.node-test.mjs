import assert from 'node:assert/strict';
import test from 'node:test';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, rmSync, symlinkSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { mobileSetupDiagnostic, observeMobileOutput } from '../../scripts/ci/run-real-backend-mobile.mjs';

const secret = 'SYNTHETIC_PRIVATE_TOKEN_PATH_AND_MESSAGE';
const sha = 'a'.repeat(40);
const runner = fileURLToPath(new URL('../../scripts/ci/run-real-backend-mobile.mjs', import.meta.url));
const info = (changes = {}) => ({ uid: 1001, mode: 0o40700, isDirectory: () => true,
  isSymbolicLink: () => false, ...changes });
const io = (changes = {}) => ({ uid: () => 1001, lstatSync: () => info(), accessSync: () => {}, ...changes });
const state = () => ({ phase: 'OUTPUT_PREPARATION', sourceClaim: sha, runId: '123', runAttempt: '1',
  invocationValid: null, sourceMatched: null, sourceClean: null, harnessStartAttempted: false, harnessProcessStarted: false });

test('setup projection binds only approved identity and never grants runtime acceptance', () => {
  const record = mobileSetupDiagnostic(state(), io());
  assert.equal(record.sourceClaim, sha);
  assert.equal(record.phase, 'OUTPUT_PREPARATION');
  assert.equal(record.functionalAcceptance, 'NOT_EVALUATED');
  assert.equal(record.publicGo, false);
  assert.equal(record.harnessProcessStarted, false);
  assert.equal(record.harnessStartAttempted, false);
  assert.deepEqual(record.filesystem.artifactRoot, { state: 'OBSERVED', directory: true, symlink: false,
    ownedByRunner: true, writableByRunner: true, searchableByRunner: true, privateToRunner: true });
});

test('only finite phases and filesystem codes can cross the diagnostic boundary', () => {
  for (const phase of ['OUTPUT_PREPARATION', 'INVOCATION_BINDING', 'SOURCE_BINDING', 'HARNESS_PREPARATION', 'HARNESS_EXECUTION']) {
    assert.equal(mobileSetupDiagnostic({ ...state(), phase }, io()).phase, phase);
  }
  for (const code of ['EACCES', 'EPERM', 'ENOENT', 'ENOTDIR', 'ELOOP', 'ENOSPC', 'EROFS', 'EIO']) {
    assert.equal(mobileSetupDiagnostic({ ...state(), error: { code } }, io()).osCode, code);
  }
  const record = mobileSetupDiagnostic({ ...state(), phase: secret, error: { code: secret, message: secret, path: secret } }, io());
  assert.equal(record.phase, 'UNKNOWN');
  assert.equal(record.osCode, null);
  assert.equal(JSON.stringify(record).includes(secret), false);
});

test('invalid or wrong-type identity and unbounded status cannot be reflected', () => {
  for (const value of [secret, 123, {}, null, '0', '1'.repeat(50)]) {
    const record = mobileSetupDiagnostic({ ...state(), sourceClaim: value, runId: value,
      runAttempt: value }, io());
    assert.equal(record.sourceClaim, null);
    assert.equal(record.runId, null);
    assert.equal(record.runAttempt, null);
    assert.equal(record.harnessExitCode, null);
    assert.equal(JSON.stringify(record).includes(secret), false);
  }
  for (const value of [-1, 256, Infinity, NaN, secret, {}, '123']) {
    assert.equal(mobileSetupDiagnostic({ ...state(), harnessExitCode: value }, io()).harnessExitCode, null);
  }
});

test('permission failure is a closed code and booleans without a UID or raw path', () => {
  const denied = io({ lstatSync: () => info({ uid: 0, mode: 0o40755 }),
    accessSync: () => { throw Object.assign(new Error(secret), { code: 'EACCES', path: secret }); } });
  const record = mobileSetupDiagnostic({ ...state(), error: { code: 'EACCES', path: secret } }, denied);
  assert.equal(record.osCode, 'EACCES');
  for (const value of Object.values(record.filesystem)) {
    assert.equal(value.ownedByRunner, false);
    assert.equal(value.writableByRunner, false);
    assert.equal(value.searchableByRunner, false);
    assert.equal(value.privateToRunner, false);
  }
  const rendered = JSON.stringify(record);
  assert.equal(rendered.includes(secret), false);
  assert.equal(rendered.includes('"uid"'), false);
  assert.equal(rendered.includes('artifacts/mobile'), false);
});

test('missing or unavailable ancestors never cause inspection of an unverified child', () => {
  for (const code of ['ENOENT', 'EACCES', secret]) {
    const calls = [];
    const result = observeMobileOutput(io({ lstatSync: path => {
      calls.push(path); throw Object.assign(new Error(secret), { code });
    } }));
    assert.deepEqual(calls, ['artifacts']);
    assert.equal(result.artifactRoot.state, code === 'ENOENT' ? 'MISSING' : 'UNAVAILABLE');
    assert.equal(result.mobileOutput.state, 'NOT_INSPECTED');
    assert.equal(JSON.stringify(result).includes(secret), false);
  }
});

test('symlink or non-directory ancestors are never followed or accessed', () => {
  for (const changes of [{ isSymbolicLink: () => true }, { isDirectory: () => false }]) {
    const calls = [];
    const result = observeMobileOutput(io({ lstatSync: path => { calls.push(path); return info(changes); },
      accessSync: () => assert.fail('An unsafe ancestor was accessed.') }));
    assert.deepEqual(calls, ['artifacts']);
    assert.equal(result.mobileOutput.state, 'NOT_INSPECTED');
  }
});

test('actual filesystem symlink observation does not traverse its target', () => {
  const directory = mkdtempSync(join(tmpdir(), 'coglatas-mobile-diagnostic-'));
  const previous = process.cwd();
  try {
    mkdirSync(join(directory, 'outside'));
    symlinkSync(join(directory, 'outside'), join(directory, 'artifacts'), 'dir');
    process.chdir(directory);
    const result = observeMobileOutput();
    assert.equal(result.artifactRoot.symlink, true);
    assert.equal(result.mobileOutput.state, 'NOT_INSPECTED');
  } finally { process.chdir(previous); rmSync(directory, { recursive: true, force: true }); }
});

test('even an error-code getter cannot publish an arbitrary exception message', () => {
  const error = { get code() { throw new Error(secret); } };
  const record = mobileSetupDiagnostic({ ...state(), error }, io());
  assert.equal(record.osCode, null);
  assert.equal(JSON.stringify(record).includes(secret), false);
});

test('real CLI override still fails before source or harness execution with closed diagnostics', () => {
  const directory = mkdtempSync(join(tmpdir(), 'coglatas-mobile-invocation-'));
  try {
    const result = spawnSync(process.execPath, [runner, secret], { cwd: directory, encoding: 'utf8',
      env: { ...process.env, TARGET_SHA: sha, GITHUB_RUN_ID: '123', GITHUB_RUN_ATTEMPT: '1' } });
    assert.equal(result.status, 1);
    const lines = result.stdout.split('\n').filter(line => line.startsWith('MOBILE_SETUP_DIAGNOSTIC '));
    assert.equal(lines.length, 2);
    const final = JSON.parse(lines.at(-1).slice('MOBILE_SETUP_DIAGNOSTIC '.length));
    assert.equal(final.phase, 'INVOCATION_BINDING');
    assert.equal(final.invocationValid, false);
    assert.equal(final.sourceMatched, null);
    assert.equal(final.harnessStartAttempted, false);
    assert.equal(final.harnessProcessStarted, false);
    assert.equal(final.functionalAcceptance, 'NOT_EVALUATED');
    assert.equal(result.stdout.includes(secret) || result.stderr.includes(secret), false);
    assert.equal(result.stdout.includes(directory) || result.stderr.includes(directory), false);
  } finally { rmSync(directory, { recursive: true, force: true }); }
});

test('module import for controls never invokes the mobile runtime', () => {
  const directory = mkdtempSync(join(tmpdir(), 'coglatas-mobile-import-'));
  try {
    const result = spawnSync(process.execPath, ['--input-type=module', '--eval',
      'await import(' + JSON.stringify(new URL('../../scripts/ci/run-real-backend-mobile.mjs', import.meta.url).href) + ')'],
    { cwd: directory, encoding: 'utf8' });
    assert.equal(result.status, 0);
    assert.equal(result.stdout, '');
    assert.equal(result.stderr, '');
  } finally { rmSync(directory, { recursive: true, force: true }); }
});

test('real unavailable-source failure suppresses git stderr and retains only closed source-phase metadata', () => {
  const directory = mkdtempSync(join(tmpdir(), 'coglatas-mobile-source-'));
  try {
    const result = spawnSync(process.execPath, [runner], { cwd: directory, encoding: 'utf8',
      env: { ...process.env, TARGET_SHA: sha, GITHUB_RUN_ID: '123', GITHUB_RUN_ATTEMPT: '1' } });
    assert.equal(result.status, 1);
    const lines = result.stdout.split('\n').filter(line => line.startsWith('MOBILE_SETUP_DIAGNOSTIC '));
    assert.equal(lines.length, 2);
    const final = JSON.parse(lines.at(-1).slice('MOBILE_SETUP_DIAGNOSTIC '.length));
    assert.equal(final.phase, 'SOURCE_BINDING');
    assert.equal(final.sourceMatched, null);
    assert.equal(final.harnessStartAttempted, false);
    assert.equal(result.stderr.includes('fatal:'), false);
    assert.equal(result.stdout.includes(directory) || result.stderr.includes(directory), false);
  } finally { rmSync(directory, { recursive: true, force: true }); }
});
