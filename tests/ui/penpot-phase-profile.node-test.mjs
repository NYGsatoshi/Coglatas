import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import test from 'node:test';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const workflow = readFileSync(path.join(repo, '.github/workflows/ci.yml'), 'utf8');
const composePath = path.join(repo, 'infra/compose/dev/playwright.yml');
const spec = readFileSync(path.join(repo, 'tests/ui/penpot-responsive.spec.ts'), 'utf8');

test('Penpot profiling is exact-base, label-opted and otherwise disabled', () => {
  const line = workflow.split('\n').find(row =>
    row.trimStart().startsWith('COGLATAS_PENPOT_PHASE_PROFILE:'));
  assert.ok(line, 'the hosted frontend job must declare a profile flag');
  for (const guard of [
    "github.event_name == 'pull_request'",
    "github.base_ref == 'SecurityTestImplimation'",
    "contains(github.event.pull_request.labels.*.name, 'ci-penpot-phase-profile')",
    "&& '1' || '0'"
  ]) {
    assert.ok(line.includes(guard), 'Missing opt-in guard: ' + guard);
  }
  assert.match(spec, /process\.env\['COGLATAS_PENPOT_PHASE_PROFILE'\] === '1'/);
});

test('Every original Penpot navigation, capture and axe call remains timed', () => {
  const count = (pattern) => [...spec.matchAll(pattern)].length;
  assert.equal(count(/await profile\.measure\('navigation',/g), 9);
  assert.equal(count(/await capture\(page, testInfo, .+?, profile\);/g), 13);
  assert.equal(count(/await profile\.measure\('accessibility',/g), 3);
  assert.match(spec, /await profile\.measure\('screenshot',/);
  assert.match(spec, /await profile\.measure\('attachment',/);
  // Capture includes screenshot and attachment: output the exclusive layout
  // phase as capture_ms and retain a named inclusive diagnostic.
  assert.match(spec, /totals\.capture\.millis - totals\.screenshot\.millis - totals\.attachment\.millis/);
  assert.match(spec, /capture_inclusive_ms=/);
  assert.match(spec, /phase === 'capture' \? captureLayoutMillis : totals\[phase\]\.millis/);
  // A failed browser check must still publish a phase record without masking
  // its existing Playwright failure and its retry attempt.
  assert.match(spec, /finally\s*\{[\s\S]*?profile\.report\(testInfo\.retry, completed\);/);
  assert.equal([...spec.matchAll(/profile\.report\(testInfo\.retry, completed\)/g)].length, 1);
  assert.match(spec, /page\.screenshot\(\{ path: imagePath, fullPage: false, animations: 'disabled' \}\)/);
  assert.match(spec, /testInfo\.attach\(fileName, \{ path: imagePath, contentType: 'image\/png' \}\)/);
  assert.match(spec, /expect\(errors\)\.toEqual\(\[\]\)/);
  assert.match(spec, /expect\(commands\)\.toEqual\(\[\]\)/);
});

test('Actual Compose runner forwards opt-in and defaults off without new jobs', {
  skip: process.env.GITHUB_ACTIONS !== 'true'
}, () => {
  const readCompose = (value) => {
    const env = { ...process.env };
    delete env.COGLATAS_PENPOT_PHASE_PROFILE;
    if (value !== undefined) env.COGLATAS_PENPOT_PHASE_PROFILE = value;
    const result = spawnSync('docker', [
      'compose', '-f', composePath, 'config', '--format', 'json'
    ], { cwd: repo, env, encoding: 'utf8', timeout: 20000 });
    assert.equal(result.status, 0, (result.stderr || '').slice(0, 600));
    return JSON.parse(result.stdout).services['angular-playwright'];
  };
  const enabled = readCompose('1');
  const disabled = readCompose(undefined);
  const invalid = readCompose('other');
  assert.equal(enabled.environment.COGLATAS_PENPOT_PHASE_PROFILE, '1');
  assert.equal(disabled.environment.COGLATAS_PENPOT_PHASE_PROFILE, '0');
  // The spec only opts into profiling for an exact value of 1.
  assert.equal(invalid.environment.COGLATAS_PENPOT_PHASE_PROFILE, 'other');
  assert.deepEqual(Object.keys(enabled.environment).sort(), Object.keys(disabled.environment).sort());
  assert.equal(enabled.network_mode, disabled.network_mode);
});
