import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, writeFileSync, existsSync, mkdirSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';
import test from 'node:test';
import { assertMobileProfile, mobileDeviceOptions, mobileOwners, mobileProject, validateMobileOwners } from './mobile/profile.mjs';
import MobileReporter from './mobile/reporter.mjs';
import { requiredOwners } from './fixtures/functional-evidence-reporter.mjs';
import { mobileRuntimeIdentity } from '../../scripts/ci/mobile-runtime-contract.mjs';

const records = () => Object.entries(mobileOwners).map(([id, steps]) => ({ id, project: mobileProject,
  backend: 'real', expectedStatus: 'passed', attempts: [{ status: 'passed', retry: 0, steps: [...steps] }] }));
const profile = () => ({ name: mobileProject, use: { browserName: 'chromium', ...mobileDeviceOptions(mobileProject) } });

test('runtime binding rejects wrong checkout, dirty source, image substitution and unmatched restored producer', () => {
  const sha = 'a'.repeat(40), id = 'sha256:' + 'b'.repeat(64);
  const valid = () => ({ sha, stamp: sha, checkout: sha, dirty: '', reused: false,
    image: { Id: id, Config: { Labels: { 'org.opencontainers.image.revision': sha, private: 'private-canary' } } },
    running: { Image: id, State: { Running: true } }, producerSource: sha, producerId: id });
  assert.equal(mobileRuntimeIdentity(valid()).producer, 'fresh-isolated-build');
  assert.equal(mobileRuntimeIdentity({ ...valid(), reused: true }).producer, 'restored-main-artifact');
  assert.equal(JSON.stringify(mobileRuntimeIdentity(valid())).includes('private-canary'), false);
  for (const mutate of [
    (v) => { v.sha = ''; }, (v) => { v.stamp = 'c'.repeat(40); }, (v) => { v.checkout = 'c'.repeat(40); },
    (v) => { v.dirty = 'modified'; }, (v) => { v.image.Id = 'bad'; }, (v) => { v.running.Image = 'sha256:' + 'c'.repeat(64); },
    (v) => { v.running.State.Running = false; }, (v) => { v.image.Config.Labels['org.opencontainers.image.revision'] = ''; },
    (v) => { v.reused = true; v.producerSource = ''; }, (v) => { v.reused = true; v.producerId = ''; },
  ]) { const changed = valid(); mutate(changed); assert.throws(() => mobileRuntimeIdentity(changed)); }
});

test('four complete real owners are required once with all mandatory steps', () => {
  assert.equal(validateMobileOwners(records()).length, 4);
  for (const mutate of [
    (rows) => rows.pop(), (rows) => rows.push(rows[0]), (rows) => { rows[1] = rows[0]; },
    (rows) => { rows[0].backend = 'mock'; }, (rows) => { rows[0].project = 'functional-chromium'; },
    (rows) => { rows[0].expectedStatus = 'skipped'; }, (rows) => { rows[0].attempts = []; },
    (rows) => { rows[0].attempts.push(rows[0].attempts[0]); }, (rows) => { rows[0].attempts[0].status = 'skipped'; },
    (rows) => { rows[0].attempts[0].status = 'failed'; }, (rows) => { rows[0].attempts[0].retry = 1; },
  ]) { const changed = records(); mutate(changed); assert.throws(() => validateMobileOwners(changed)); }
  for (const [index, owner] of records().entries()) {
    for (const step of owner.attempts[0].steps) {
      const changed = records(); changed[index].attempts[0].steps = changed[index].attempts[0].steps.filter((id) => id !== step);
      assert.throws(() => validateMobileOwners(changed), `Missing ${step}`);
    }
  }
});

test('mobile profile fails closed on engine, opt-in, geometry or touch mismatches', () => {
  assertMobileProfile('chromium', profile(), '1');
  for (const mutate of [
    (value) => { value.name = 'functional-chromium'; }, (value) => { value.use.browserName = 'firefox'; },
    (value) => { value.use.viewport.width = 393; }, (value) => { value.use.viewport.height = 400; },
    (value) => { value.use.screen.width = 393; }, (value) => { value.use.isMobile = false; },
    (value) => { value.use.hasTouch = false; }, (value) => { value.use.deviceScaleFactor = 2; },
  ]) { const changed = profile(); mutate(changed); assert.throws(() => assertMobileProfile('chromium', changed, '1')); }
  assert.throws(() => assertMobileProfile('webkit', profile(), '1'));
  assert.throws(() => assertMobileProfile('chromium', profile(), '0'));
});

test('secondary contexts inherit only fresh device options and canonical owner counts stay unchanged', () => {
  const options = mobileDeviceOptions(mobileProject);
  assert.deepEqual(Object.keys(options).sort(), ['deviceScaleFactor', 'hasTouch', 'isMobile', 'screen', 'viewport']);
  options.viewport.width = 999;
  assert.equal(mobileDeviceOptions(mobileProject).viewport.width, 320);
  assert.deepEqual(mobileDeviceOptions('functional-chromium'), {});
  const domains = ['core', 'files', 'collaboration', 'authz-negative'];
  assert.equal(domains.flatMap((domain) => requiredOwners(domain, 'functional-fast')).length, 6);
  assert.equal(domains.flatMap((domain) => requiredOwners(domain, 'functional-full')).length, 7);
});

test('reporter refuses a source mismatch and never publishes protected assertion or attachment data', () => {
  const previous = process.cwd(), sha = process.env.TARGET_SHA, enabled = process.env.COGLATAS_REAL_BACKEND_MOBILE;
  const directory = mkdtempSync(join(tmpdir(), 'coglatas-mobile-reporter-'));
  const errors = [], oldError = console.error, oldLog = console.log;
  try {
    process.chdir(directory); mkdirSync('artifacts/mobile', { recursive: true });
    process.env.TARGET_SHA = 'a'.repeat(40); process.env.COGLATAS_REAL_BACKEND_MOBILE = '1';
    console.error = (value) => errors.push(value); console.log = () => {};
    const tests = records().map((record) => ({
      annotations: [{ type: 'journey', description: record.id }, { type: 'backend', description: 'real' }],
      parent: { project: () => ({ name: mobileProject }) }, expectedStatus: 'passed',
      results: [{ status: 'passed', retry: 0, attachments: [{ body: 'private-canary' }],
        steps: record.attempts[0].steps.map((id) => ({ category: 'test.step', title: `owner / ${id} private-canary` })) }],
    }));
    const reporter = new MobileReporter();
    reporter.onBegin({ projects: [{ ...profile(), retries: 0 }], workers: 1 }, { allTests: () => tests });
    assert.equal(reporter.onEnd({ status: 'passed' }).status, 'failed');
    assert.equal(existsSync('artifacts/mobile/owners.json'), false);
    assert.equal(errors.join().includes('private-canary'), false);
    // The matching source receipt allows only the finite owner projection.
    writeFileSync('artifacts/mobile/source-sha', process.env.TARGET_SHA + '\n');
    assert.equal(reporter.onEnd({ status: 'passed' }), undefined);
    const output = readFileSync('artifacts/mobile/owners.json', 'utf8');
    assert.equal(output.includes('private-canary'), false);
    assert.equal(JSON.parse(output).journeys.length, 4);
  } finally {
    console.error = oldError; console.log = oldLog; process.chdir(previous);
    assert.equal(dirname(resolve(directory)), resolve(tmpdir()));
    assert.match(basename(directory), /^coglatas-mobile-reporter-/);
    rmSync(directory, { recursive: true, force: true });
    if (sha === undefined) { delete process.env.TARGET_SHA; } else { process.env.TARGET_SHA = sha; }
    if (enabled === undefined) { delete process.env.COGLATAS_REAL_BACKEND_MOBILE; } else { process.env.COGLATAS_REAL_BACKEND_MOBILE = enabled; }
  }
});
