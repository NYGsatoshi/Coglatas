import assert from 'node:assert/strict';
import { appendFileSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const marker = 'CI Penpot phase profile:';
const projects = ['chromium-desktop', 'chromium-mobile'];
const viewports = ['1536x1024', '1366x768', '768x1024', '390x844'];
const phases = ['navigation', 'capture', 'screenshot', 'attachment', 'accessibility'];
const requiredCounts = { navigation: 17, capture: 26, screenshot: 26, attachment: 52, accessibility: 6 };
const numeric = ['retry', 'completed', 'total_ms', 'capture_inclusive_ms',
  ...phases.flatMap(phase => [phase + '_calls', phase + '_ms'])];
const allowed = new Set(['spec', 'project', 'viewport', ...numeric]);

function boundedInteger(value, max) {
  assert.match(value ?? '', /^(0|[1-9][0-9]{0,6})$/u, 'Invalid bounded integer');
  const number = Number(value);
  assert.ok(number <= max, 'Out of range integer');
  return number;
}

function parseRecord(line) {
  const fields = Object.create(null);
  const text = line.slice(line.indexOf(marker) + marker.length).trim();
  assert.ok(text.length > 0 && text.length <= 600, 'Malformed profile line');
  for (const piece of text.split(/\s+/u)) {
    const index = piece.indexOf('=');
    assert.ok(index > 0, 'Invalid key-value pair');
    const key = piece.slice(0, index);
    assert.ok(allowed.has(key) && !Object.hasOwn(fields, key), 'Unexpected or duplicate field');
    fields[key] = piece.slice(index + 1);
  }
  for (const key of allowed) assert.ok(Object.hasOwn(fields, key), 'Missing field');
  assert.equal(fields.spec, 'penpot-responsive', 'Wrong spec');
  assert.ok(projects.includes(fields.project), 'Wrong browser project');
  assert.ok(viewports.includes(fields.viewport), 'Wrong viewport');
  const numbers = Object.fromEntries(numeric.map(key => [
    key, boundedInteger(fields[key], key.endsWith('_calls') ? 100 : 600000)
  ]));
  assert.ok(numbers.retry <= 2, 'Unsupported retry');
  assert.ok(numbers.completed <= 1, 'Invalid completion state');
  for (const phase of phases) {
    const count = numbers[phase + '_calls'];
    if (numbers.completed === 1) {
      assert.equal(count, requiredCounts[phase], 'Lost full test coverage');
      assert.ok(numbers[phase + '_ms'] > 0, 'Missing phase duration');
    } else {
      assert.ok(count <= requiredCounts[phase], 'Excess calls in failed attempt');
    }
  }
  assert.ok(Math.abs(numbers.capture_inclusive_ms -
    numbers.capture_ms - numbers.screenshot_ms - numbers.attachment_ms) <= 4,
    'Nested phase measurement mismatch');
  assert.ok(numbers.total_ms > 0, 'Zero elapsed test duration');
  return { project: fields.project, viewport: fields.viewport, ...numbers };
}

export function verifyPenpotPhaseEvidence(log) {
  assert.equal(typeof log, 'string', 'Expected a Playwright log');
  const records = log.split(/\r?\n/u)
    .filter(line => line.includes(marker))
    .map(parseRecord);
  assert.ok(records.length >= 8 && records.length <= 24, 'Missing or excessive phase records');
  const complete = new Map();
  const seenAttempts = new Set();
  for (const record of records) {
    const key = record.project + '/' + record.viewport;
    const attempt = key + '/' + record.retry;
    assert.ok(!seenAttempts.has(attempt), 'Duplicate browser attempt');
    seenAttempts.add(attempt);
    if (record.completed === 1) {
      assert.ok(!complete.has(key), 'Duplicate successful viewport');
      complete.set(key, record);
    }
  }
  assert.equal(complete.size, projects.length * viewports.length, 'Missing completed viewport');
  const names = [...phases.map(phase => phase + '_ms'), 'capture_inclusive_ms', 'total_ms'];
  const totals = Object.fromEntries(names.map(key => [
    key, [...complete.values()].reduce((sum, record) => sum + record[key], 0)
  ]));
  return { cases: complete.size, attempts: records.length, totals };
}

const cli = process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);
if (cli) {
  try {
    assert.equal(process.argv.length, 3, 'Expected only the existing lane log');
    const result = verifyPenpotPhaseEvidence(readFileSync(process.argv[2], 'utf8'));
    const parts = Object.entries(result.totals).map(([key, value]) => key + '=' + value);
    console.log('CI Penpot verified phase summary: cases=' + result.cases +
      ' attempts=' + result.attempts + ' ' + parts.join(' '));
    if (process.env.GITHUB_ACTIONS === 'true' && process.env.GITHUB_STEP_SUMMARY) {
      const rows = Object.entries(result.totals).map(([key, value]) =>
        '| ' + key + ' | ' + value + ' |');
      appendFileSync(process.env.GITHUB_STEP_SUMMARY, [
        '### Penpot existing-browser phase measurement (advisory)',
        '',
        'Eight complete viewport/browser cases across ' + result.attempts + ' attempts.',
        '',
        '| Phase | Total milliseconds |', '| --- | ---: |', ...rows, '',
        'capture_inclusive_ms contains screenshot and attachment time; do not sum it with exclusive phases.',
        ''
      ].join('\n'));
    }
  } catch {
    // Do not publish raw browser logs, resource URLs, or private host paths.
    console.error('::error::Penpot phase profile missing, malformed or incomplete.');
    process.exitCode = 1;
  }
}
