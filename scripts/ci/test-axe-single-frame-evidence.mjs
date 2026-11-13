import assert from 'node:assert/strict';
import test from 'node:test';
import { verifyAxeSingleFrameEvidence } from './axe-single-frame-evidence.mjs';

const projects = ['chromium-desktop', 'chromium-mobile'];
const viewports = ['1536x1024', '1366x768', '768x1024', '390x844'];
function row(project, viewport, retry = 0, completed = 1, override = {}) {
  const fields = { spec: 'penpot-responsive', project, viewport, retry, completed,
    scans: 6, standard_ms: 6400, legacy_ms: 4900,
    frame_count: 1, parity: 'PASS', ...override };
  return 'CI Axe single-frame shadow: ' +
    Object.entries(fields).map(([key, value]) => key + '=' + value).join(' ');
}
const canonical = projects.flatMap(project => viewports.map(viewport => row(project, viewport)));
const parse = records => verifyAxeSingleFrameEvidence(records.join('\n'));

test('48 authoritative and 48 candidate checks reconcile across eight cases', () => {
  const actual = parse(canonical);
  assert.deepEqual(actual, {
    cases: 8, attempts: 8, scans: 48, standardMs: 51200, legacyMs: 39200
  });
});

test('Failed attempt and later successful retry do not fabricate extra coverage', () => {
  const prior = row('chromium-mobile', '390x844', 0, 0, {
    scans: 2, standard_ms: 1800, legacy_ms: 1400, parity: 'INCOMPLETE'
  });
  const next = row('chromium-mobile', '390x844', 1, 1);
  const rest = canonical.filter(x => !x.includes('project=chromium-mobile viewport=390x844'));
  const result = parse([...rest, prior, next]);
  assert.equal(result.cases, 8);
  assert.equal(result.attempts, 9);
  assert.equal(result.scans, 48);
});

test('No missing, duplicate or spoofed case can pass', () => {
  assert.throws(() => parse(canonical.slice(0, 7)));
  assert.throws(() => parse([...canonical, canonical[0]]));
  assert.throws(() => parse([...canonical.slice(0, 7), row('invalid-project', '390x844')]));
  assert.throws(() => parse([...canonical.slice(0, 7), row('chromium-mobile', '390x844', 3)]));
  assert.throws(() => parse([...canonical.slice(0, 7), row('chromium-mobile', '390x844', 0, 0, { scans: 0, parity: 'INCOMPLETE' })]));
});

test('Frame scope, rule disagreement, missing times and untrusted fields fail closed', () => {
  assert.throws(() => parse([...canonical.slice(0, 7), row('chromium-mobile', '390x844', 0, 1, { scans: 5 })]));
  assert.throws(() => parse([...canonical.slice(0, 7), row('chromium-mobile', '390x844', 0, 1, { frame_count: 2 })]));
  assert.throws(() => parse([...canonical.slice(0, 7), row('chromium-mobile', '390x844', 0, 1, { parity: 'INCOMPLETE' })]));
  assert.throws(() => parse([...canonical.slice(0, 7), row('chromium-mobile', '390x844', 0, 1, { standard_ms: 0 })]));
  assert.throws(() => parse([...canonical.slice(0, 7), canonical[7] + ' injection=secret']));
  assert.throws(() => parse([...canonical.slice(0, 7), canonical[7] + ' scans=6']));
  assert.throws(() => verifyAxeSingleFrameEvidence(''));
});
