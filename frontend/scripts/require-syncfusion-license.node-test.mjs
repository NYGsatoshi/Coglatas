import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { readdir, readFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const scriptPath = join(
  dirname(fileURLToPath(import.meta.url)),
  'require-syncfusion-license.mjs'
);
const repositoryRoot = resolve(dirname(scriptPath), '..', '..');

async function typescriptFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  return (await Promise.all(entries.map((entry) => {
    const path = join(directory, entry.name);
    return entry.isDirectory() ? typescriptFiles(path) : path.endsWith('.ts') ? [path] : [];
  }))).flat();
}

function runLicenseCheck(value, includeVariable = true) {
  const env = { ...process.env };
  delete env.SYNCFUSION_LICENSE;

  if (includeVariable) {
    env.SYNCFUSION_LICENSE = value;
  }

  return spawnSync(process.execPath, [scriptPath], {
    env,
    encoding: 'utf8'
  });
}

test('fails closed when SYNCFUSION_LICENSE is missing', () => {
  const result = runLicenseCheck('', false);

  assert.equal(result.status, 1);
  assert.match(result.stderr, /SYNCFUSION_LICENSE is not configured\./);
});

test('fails closed when SYNCFUSION_LICENSE is empty or whitespace-only', () => {
  for (const value of ['', '   ', '\t\n']) {
    const result = runLicenseCheck(value);

    assert.equal(result.status, 1);
    assert.match(result.stderr, /SYNCFUSION_LICENSE is not configured\./);
  }
});

test('accepts a non-empty license without printing it', () => {
  const license = 'test-license-value';
  const result = runLicenseCheck(license);

  assert.equal(result.status, 0);
  assert.equal(result.stdout, '');
  assert.equal(result.stderr, '');
  assert.equal(result.stdout.includes(license), false);
  assert.equal(result.stderr.includes(license), false);
});

test('keeps runtime license registration out of active and legacy browser sources', async () => {
  for (const sourceRoot of [
    join(repositoryRoot, 'frontend', 'src'),
    join(repositoryRoot, 'coglatas-frontend', 'src')
  ]) {
    for (const path of await typescriptFiles(sourceRoot)) {
      const source = await readFile(path, 'utf8');
      assert.doesNotMatch(source, /\bregisterLicense\s*\(/u, `${path} must not register a license in browser code`);
    }
  }
});
