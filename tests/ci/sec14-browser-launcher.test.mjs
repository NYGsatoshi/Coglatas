import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const launcher = resolve(root, 'scripts/security/run-sec14-browser-fixture.mjs');
const candidate = 'a'.repeat(40);
const valid = ['--candidate-sha', candidate, '--report', 'artifacts/sec14-native/never-executed-cli-control.json'];

function reject(args, message, cwd = root) {
  // An empty PATH proves rejected input cannot reach Git, Docker or a scanner.
  const result = spawnSync(process.execPath, [launcher, ...args], {
    cwd, encoding: 'utf8', timeout: 10000, env: { ...process.env, PATH: '' },
  });
  assert.equal(result.error, undefined);
  assert.equal(result.status, 1);
  assert.match(result.stderr, message);
  assert.doesNotMatch(result.stdout, /MATCHED|PRODUCT_VERIFIED/);
}

test('unknown options reject before any executable is invoked', () => {
  reject([...valid, '--target', 'https://outside.invalid'], /Unknown fixture option/);
});

test('duplicate options reject before any executable is invoked', () => {
  reject([...valid, '--candidate-sha', candidate], /Duplicate fixture option/);
});

test('unbounded option input is rejected', () => {
  reject(['--candidate-sha', candidate, '--report', 'a'.repeat(4097)], /Fixture option invalid/);
});

test('a missing option value is rejected', () => {
  reject(['--candidate-sha'], /Fixture option invalid/);
});

test('non-exact or uppercase candidate identities are rejected', () => {
  for (const value of ['a'.repeat(39), 'A'.repeat(40), 'main']) {
    reject(['--candidate-sha', value, '--report', valid[3]], /Fixture requires exact candidate/);
  }
});

test('unknown fixture modes are rejected', () => {
  reject([...valid, '--case', 'product'], /Fixture requires exact candidate/);
});

test('artifact traversal and non-JSON output paths are rejected', () => {
  for (const value of ['artifacts/sec14-native/../escaped.json', 'artifacts/sec14-native/report.txt']) {
    reject(['--candidate-sha', candidate, '--report', value], /Fixture output must be a JSON artifact/);
  }
});

test('a caller outside the repository root is rejected', () => {
  reject(valid, /Run fixture from its repository root/, dirname(root));
});
