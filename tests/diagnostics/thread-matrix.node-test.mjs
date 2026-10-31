import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdtempSync, writeFileSync, readFileSync, existsSync, rmSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { CASES, validateExecutedCases, validateMatrixObservation } from './thread-matrix-contract.mjs';

const phases = ['OBSERVER_REGISTERED', 'ACTION_STARTED', 'REQUEST_OBSERVED', 'ACTION_COMPLETED', 'WAITING_RESPONSE', 'RESPONSE_OBSERVED'];
const observations = () => [
  { caseId: 'TD-01', classification: 'ORDINARY_FRESH_DENIAL', phases: [...phases], backgroundStatus: null, freshStatus: 400,
    earlyRedaction: false, clickPerformed: true, oldResponseExcluded: null, uiCleared: true },
  { caseId: 'TD-02', classification: 'LEGITIMATE_EARLY_REDACTION', phases: [], backgroundStatus: 400, freshStatus: null,
    earlyRedaction: true, clickPerformed: false, oldResponseExcluded: null, uiCleared: true },
  { caseId: 'TD-03', classification: 'OLD_RESPONSE_EXCLUDED', phases: [...phases], backgroundStatus: 400, freshStatus: 400,
    earlyRedaction: false, clickPerformed: true, oldResponseExcluded: true, uiCleared: true },
];
const records = () => observations().map((observation) => ({ id: observation.caseId, expectedStatus: 'passed',
  attempts: [{ status: 'passed', retry: 0 }], observation }));

// Node's test runner owns completion and reports callback failures.
void test('all declared mocked mechanisms must execute once without missing, skipped, failed or retried cases', () => {
  assert.equal(validateExecutedCases(records(), 'matrix').length, CASES.length);
  for (const mutate of [(rows) => rows.pop(), (rows) => rows.push(rows[0]), (rows) => { rows[1] = rows[0]; },
    (rows) => { rows[0].expectedStatus = 'skipped'; }, (rows) => { rows[0].attempts[0].status = 'skipped'; },
    (rows) => { rows[0].attempts[0].status = 'failed'; }, (rows) => { rows[0].attempts[0].retry = 1; },
    (rows) => { rows[0].attempts = []; }, (rows) => { rows[0].attempts.push(rows[0].attempts[0]); },
  ]) { const rows = records(); mutate(rows); assert.throws(() => validateExecutedCases(rows, 'matrix')); }
});

void test('early redaction is never represented as a fresh activation or backend isolation proof', () => {
  for (const mutate of [(value) => { value.freshStatus = 400; }, (value) => { value.clickPerformed = true; },
    (value) => { value.classification = 'ORDINARY_FRESH_DENIAL'; }, (value) => { value.phases = [...phases]; },
  ]) { const value = observations()[1]; mutate(value); assert.throws(() => validateMatrixObservation(value)); }
});

void test('old-response exclusion is not applicable without an old response and a new activation boundary', () => {
  for (const index of [0, 1]) {
    assert.equal(validateMatrixObservation(observations()[index]).oldResponseExcluded, null);
    for (const invented of [true, false, undefined, 'NOT_APPLICABLE']) {
      const value = observations()[index]; value.oldResponseExcluded = invented;
      assert.throws(() => validateMatrixObservation(value));
    }
  }
  assert.equal(validateMatrixObservation(observations()[2]).oldResponseExcluded, true);
  for (const unproven of [false, null, undefined, 'true']) {
    const value = observations()[2]; value.oldResponseExcluded = unproven;
    assert.throws(() => validateMatrixObservation(value));
  }
});

void test('observations reject raw payloads, unbounded phases and false fresh responses', () => {
  for (const mutate of [(value) => { value.private = 'private-canary'; }, (value) => { value.phases.push('private-canary'); },
    (value) => { value.phases.pop(); }, (value) => { value.phases[0] = value.phases[1]; },
    (value) => { value.phases.reverse(); },
    (value) => { value.freshStatus = 200; }, (value) => { value.uiCleared = false; },
    (value) => { value.oldResponseExcluded = false; },
  ]) { const value = observations()[0]; mutate(value); assert.throws(() => validateMatrixObservation(value)); }
  for (const value of observations()) { assert.equal(JSON.stringify(validateMatrixObservation(value)).includes('private-canary'), false); }
});

void test('actual reporter rejects protected observations and a controlled raw assertion without publishing either', () => {
  const directory = mkdtempSync(join(tmpdir(), 'coglatas-thread-matrix-'));
  try {
    const root = process.cwd(), playwright = pathToFileURL(join(root, 'node_modules/@playwright/test/index.mjs')).href;
    const reporter = join(root, 'tests/diagnostics/thread-matrix-reporter.mjs');
    writeFileSync(join(directory, 'config.mjs'), `export default ${JSON.stringify({
      testDir: directory, testMatch: 'control.spec.mjs', workers: 1, retries: 0, maxFailures: 1,
      timeout: 120000, expect: { timeout: 15000 },
      reporter: [[reporter]], outputDir: join(directory, 'browser-output'), projects: [{ name: 'mocked-thread-matrix-320-touch', use: {
        browserName: 'chromium', viewport: { width: 320, height: 800 }, screen: { width: 320, height: 800 },
        hasTouch: true, isMobile: true, deviceScaleFactor: 1,
      } }],
    })};`);
    const cli = join(root, 'node_modules/@playwright/test/cli.js');
    for (const mode of ['valid', 'protected-observation', 'raw-failure']) {
      const fixture = observations().map((observation, index) => {
        const value = mode === 'protected-observation' && index === 0 ? { ...observation, private: 'private-canary' } : observation;
        return `test(${JSON.stringify(observation.caseId + ' private-canary')}, {annotation:{type:'investigationCase',description:${JSON.stringify(observation.caseId)}}}, async ({}, info) => {` +
          (mode === 'raw-failure' && index === 0 ? `expect(1,'private-canary').toBe(2);` : '') +
          `await info.attach('thread-matrix-observation-v1',{body:Buffer.from(${JSON.stringify(JSON.stringify(value))}),contentType:'application/json'});});`;
      }).join('\n');
      writeFileSync(join(directory, 'control.spec.mjs'), `import {test,expect} from ${JSON.stringify(playwright)};\n${fixture}`);
      const run = spawnSync(process.execPath, [cli, 'test', '--config', join(directory, 'config.mjs')], {
        cwd: directory, encoding: 'utf8', env: { ...process.env, THREAD_INVESTIGATION_MODE: 'matrix' },
      });
      assert.equal(run.status, mode === 'valid' ? 0 : 1);
      assert.equal((run.stdout + run.stderr).includes('private-canary'), false);
      const receipt = join(directory, 'artifacts/thread-investigation/matrix.json');
      assert.equal(existsSync(receipt), mode === 'valid');
      if (mode === 'valid') {
        const output = readFileSync(receipt, 'utf8');
        assert.equal(output.includes('private-canary'), false);
        assert.equal(JSON.parse(output).scope, 'MOCKED_TRANSPORT_UI_MECHANISM_ONLY');
      }
    }
  } finally {
    assert.equal(dirname(resolve(directory)), resolve(tmpdir()));
    assert.match(basename(directory), /^coglatas-thread-matrix-/);
    rmSync(directory, { recursive: true, force: true });
  }
});
