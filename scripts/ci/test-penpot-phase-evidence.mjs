import assert from 'node:assert/strict';
import test from 'node:test';
import { verifyPenpotPhaseEvidence } from './penpot-phase-evidence.mjs';

const projects = ['chromium-desktop', 'chromium-mobile'];
const viewports = ['1536x1024', '1366x768', '768x1024', '390x844'];
const metrics = {
  navigation_calls: 17, navigation_ms: 4000,
  capture_calls: 26, capture_ms: 1200,
  screenshot_calls: 26, screenshot_ms: 900,
  attachment_calls: 52, attachment_ms: 400,
  accessibility_calls: 6, accessibility_ms: 8000,
  capture_inclusive_ms: 2500, total_ms: 18000
};
function record(project, viewport, retry = 0, completed = 1, changes = {}) {
  const fields = { spec: 'penpot-responsive', project, viewport, retry, completed,
    ...metrics, ...changes };
  return 'CI Penpot phase profile: ' +
    Object.entries(fields).map(([key, value]) => key + '=' + value).join(' ');
}
const lines = projects.flatMap(project => viewports.map(viewport => record(project, viewport)));
const parse = values => verifyPenpotPhaseEvidence(values.join('\n'));

test('Eight complete existing cases have additive metrics with full call counts', () => {
  const result = parse(lines);
  assert.equal(result.cases, 8);
  assert.equal(result.attempts, 8);
  assert.equal(result.totals.navigation_ms, 32000);
  assert.equal(result.totals.capture_ms, 9600);
  assert.equal(result.totals.capture_inclusive_ms, 20000);
  assert.equal(result.totals.accessibility_ms, 64000);
});

test('Failed first attempt does not create a ninth successful case', () => {
  const prior = record('chromium-mobile', '390x844', 0, 0, {
    navigation_calls: 4, capture_calls: 3, screenshot_calls: 3,
    attachment_calls: 6, accessibility_calls: 0, accessibility_ms: 0,
    capture_ms: 160, screenshot_ms: 120, attachment_ms: 70,
    capture_inclusive_ms: 350, total_ms: 7000
  });
  const next = record('chromium-mobile', '390x844', 1, 1);
  const untouched = lines.filter(line =>
    !line.includes('project=chromium-mobile viewport=390x844'));
  const result = parse([...untouched, prior, next]);
  assert.equal(result.cases, 8);
  assert.equal(result.attempts, 9);
});

test('Missing cases, repeated successes and invalid retries fail closed', () => {
  assert.throws(() => parse(lines.slice(0, 7)));
  assert.throws(() => parse([...lines, lines[0]]));
  assert.throws(() => parse([...lines.slice(0, 7), record('chromium-wrong', '390x844')]));
  assert.throws(() => parse([...lines.slice(0, 7), record('chromium-mobile', '390x844', 3)]));
  assert.throws(() => parse([...lines.slice(0, 7), record('chromium-mobile', '390x844', 0, 0)]));
  assert.throws(() => verifyPenpotPhaseEvidence(''));
});

test('Lost operations, tampered nesting, unknown fields and invalid numerics fail closed', () => {
  assert.throws(() => parse([...lines.slice(0, 7),
    record('chromium-mobile', '390x844', 0, 1, { screenshot_calls: 25 })]));
  assert.throws(() => parse([...lines.slice(0, 7),
    record('chromium-mobile', '390x844', 0, 1, { capture_inclusive_ms: 999 })]));
  assert.throws(() => parse([...lines.slice(0, 7), lines[7] + ' unknown=secret']));
  assert.throws(() => parse([...lines.slice(0, 7), lines[7] + ' total_ms=1']));
  assert.throws(() => parse([...lines.slice(0, 7),
    record('chromium-mobile', '390x844', 0, 1, { total_ms: -1 })]));
});
