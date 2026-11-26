import assert from 'node:assert/strict';
import { appendFileSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const marker = 'CI Axe single-frame shadow:';
const projects = ['chromium-desktop', 'chromium-mobile'];
const viewports = ['1536x1024', '1366x768', '768x1024', '390x844'];
const numeric = ['retry', 'completed', 'scans', 'standard_ms', 'legacy_ms', 'frame_count'];
const allowed = new Set(['spec', 'project', 'viewport', ...numeric, 'parity']);

function bounded(value, max) {
  assert.match(value ?? '', /^(0|[1-9][0-9]{0,6})$/u, 'Invalid bounded integer');
  const n = Number(value);
  assert.ok(n <= max, 'Out-of-bounds metric');
  return n;
}
function parseRecord(line) {
  const fragment = line.slice(line.indexOf(marker) + marker.length).trim();
  assert.ok(fragment.length > 0 && fragment.length <= 360, 'Unbounded profile record');
  const map = Object.create(null);
  for (const piece of fragment.split(/\s+/u)) {
    const index = piece.indexOf('=');
    assert.ok(index > 0, 'Malformed record field');
    const name = piece.slice(0, index);
    assert.ok(allowed.has(name) && !Object.hasOwn(map, name), 'Unknown or duplicate attribute');
    map[name] = piece.slice(index + 1);
  }
  for (const name of allowed) assert.ok(Object.hasOwn(map, name), 'Missing attribute');
  assert.equal(map.spec, 'penpot-responsive', 'Unexpected spec');
  assert.ok(projects.includes(map.project), 'Unknown project');
  assert.ok(viewports.includes(map.viewport), 'Unknown viewport');
  const values = Object.fromEntries(numeric.map(name => [name,
    bounded(map[name], name === 'retry' ? 2 : name === 'scans' ? 6 : name === 'frame_count' ? 8 : 600000)
  ]));
  assert.ok(values.completed <= 1, 'Invalid completion state');
  assert.ok(map.parity === 'PASS' || map.parity === 'INCOMPLETE', 'Unknown parity status');
  if (values.completed === 1) {
    assert.equal(values.scans, 6, 'Missing accessibility comparisons');
    assert.equal(values.frame_count, 1, 'Unexpected frame count');
    assert.equal(map.parity, 'PASS', 'Disagreement between analyzers');
    assert.ok(values.standard_ms > 0 && values.legacy_ms > 0, 'Missing analyzer durations');
  } else {
    assert.equal(map.parity, 'INCOMPLETE', 'Failed attempt claims parity');
  }
  return { project: map.project, viewport: map.viewport, ...values, parity: map.parity };
}

export function verifyAxeSingleFrameEvidence(log) {
  assert.equal(typeof log, 'string');
  const records = log.split(/\r?\n/u).filter(line => line.includes(marker)).map(parseRecord);
  assert.ok(records.length >= 8 && records.length <= 24, 'Missing or excessive scan records');
  const completed = new Map(), attempts = new Set();
  for (const record of records) {
    const id = record.project + '/' + record.viewport;
    const attempt = id + '/' + record.retry;
    assert.ok(!attempts.has(attempt), 'Repeated attempt');
    attempts.add(attempt);
    if (record.completed === 1) {
      assert.ok(!completed.has(id), 'Duplicate completed case');
      completed.set(id, record);
    }
  }
  assert.equal(completed.size, 8, 'Missing verified viewport');
  const full = [...completed.values()];
  const standardMs = full.reduce((a, x) => a + x.standard_ms, 0);
  const legacyMs = full.reduce((a, x) => a + x.legacy_ms, 0);
  const scans = full.reduce((a, x) => a + x.scans, 0);
  assert.equal(scans, 48, 'The original 48 accessibility checks must be retained');
  return { cases: completed.size, attempts: records.length, scans, standardMs, legacyMs };
}

const isCli = process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);
if (isCli) {
  try {
    assert.equal(process.argv.length, 3, 'Requires existing internal Playwright lane log only');
    const report = verifyAxeSingleFrameEvidence(readFileSync(process.argv[2], 'utf8'));
    console.log('CI Axe verified shadow summary: cases=' + report.cases +
      ' attempts=' + report.attempts + ' scans=' + report.scans +
      ' standard_ms=' + report.standardMs + ' legacy_ms=' + report.legacyMs);
    if (process.env.GITHUB_ACTIONS === 'true' && process.env.GITHUB_STEP_SUMMARY) {
      appendFileSync(process.env.GITHUB_STEP_SUMMARY, [
        '### Existing Penpot Axe analyzer parity (advisory)',
        '',
        'All 48 canonical scans still executed; legacy scans are additional.',
        '',
        '| Measurement | Value |', '| --- | ---: |',
        '| Complete viewport cases | ' + report.cases + ' |',
        '| Successful scan pairs | ' + report.scans + ' |',
        '| Canonical analyzer, milliseconds | ' + report.standardMs + ' |',
        '| Legacy single-frame analyzer, milliseconds | ' + report.legacyMs + ' |',
        '',
        'Do not interpret CI job wall clock as a direct analyzer benchmark.',
        ''
      ].join('\n'));
    }
  } catch {
    // Never echo raw browser logs, selectors, accessibility messages, or paths.
    console.error('::error::Axe shadow comparison missing, incomplete or inconsistent.');
    process.exitCode = 1;
  }
}
