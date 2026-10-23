import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, writeFileSync, existsSync, mkdirSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';
import test from 'node:test';
import { spawnSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';
import { assertMobileProfile, mobileDeviceOptions, mobileOwners, mobileProject, validateMobileOwners } from './mobile/profile.mjs';
import MobileReporter from './mobile/reporter.mjs';
import { requiredOwners } from './fixtures/functional-evidence-reporter.mjs';
import { mobileRuntimeIdentity } from '../../scripts/ci/mobile-runtime-contract.mjs';
import { mobileOutcome, safeRunIdentity } from './mobile/outcome.mjs';

const records = () => Object.entries(mobileOwners).map(([id, steps]) => ({ id, project: mobileProject,
  backend: 'real', expectedStatus: 'passed', attempts: [{ status: 'passed', retry: 0, steps: [...steps] }] }));
const profile = () => ({ name: mobileProject, use: { browserName: 'chromium', ...mobileDeviceOptions(mobileProject) } });

test('mobile failure projection retains only approved owner, step, failure kind and source line', () => {
  const value = mobileOutcome({ annotations: [{ type: 'journey', description: 'FUNC-MSG-001' }] }, {
    status: 'failed', retry: 0, errors: [{ message: 'private-canary' }],
    steps: [{ category: 'test.step', title: 'FUNC-MSG-001 / MSG-THREAD private-canary', error: {},
      steps: [{ category: 'expect', title: 'private-canary', error: { matcherResult: { private: 'private-canary' } },
        location: { file: '/work/tests/functional/messaging/collaboration-owner-journey.spec.ts', line: 123 }, steps: [] }] }],
  });
  assert.equal(value.failedStepId, 'MSG-THREAD');
  assert.equal(value.failureKind, 'ASSERTION');
  assert.equal(value.sourceModule, 'COLLABORATION_OWNER');
  assert.equal(value.sourceLine, 123);
  assert.equal(JSON.stringify(value).includes('private-canary'), false);
  const absent = mobileOutcome({ annotations: [{ type: 'journey', description: 'private-canary' }] });
  assert.equal(absent.journeyId, 'UNKNOWN_OWNER');
  assert.equal(absent.status, 'NOT_EXECUTED');
  assert.equal(JSON.stringify(absent).includes('private-canary'), false);
  assert.equal(safeRunIdentity('private-canary'), 'UNAVAILABLE');
});

test('actual failed Playwright owner emits finite diagnostics without a PASS receipt or protected error text', () => {
  const directory = mkdtempSync(join(tmpdir(), 'coglatas-mobile-reporter-'));
  const root = process.cwd();
  try {
    mkdirSync(join(directory, 'artifacts/mobile'), { recursive: true });
    writeFileSync(join(directory, 'artifacts/mobile/source-sha'), 'a'.repeat(40) + '\n');
    const playwright = pathToFileURL(join(root, 'node_modules/@playwright/test/index.mjs')).href;
    const reporter = join(root, 'tests/functional/mobile/reporter.mjs');
    const config = pathToFileURL(join(root, 'playwright.mobile-real.config.ts')).href;
    const fixture = Object.entries(mobileOwners).map(([id, steps], index) =>
      `test(${JSON.stringify(id)}, { annotation: [{type:'journey',description:${JSON.stringify(id)}},{type:'backend',description:'real'}] }, async () => {` +
      steps.map((step, stepIndex) => `await test.step(${JSON.stringify(id + ' / ' + step + ' private-canary')}, async () => {` +
        (index === 0 && stepIndex === 0 ? `expect(1, 'private-canary').toBe(2);` : '') + '});').join('\n') + '});').join('\n');
    writeFileSync(join(directory, 'owner.spec.mjs'), `import {test,expect} from ${JSON.stringify(playwright)};\n${fixture}`);
    mkdirSync(join(directory, 'test-results'));
    writeFileSync(join(directory, 'test-results/host-sentinel'), 'host-owned');
    writeFileSync(join(directory, 'config.mjs'), `import mobile from ${JSON.stringify(config)}; export default { ...mobile, ...${JSON.stringify({
      testDir: directory, testMatch: 'owner.spec.mjs', workers: 1, retries: 0,
      ...(process.platform === 'win32' ? { outputDir: join(directory, 'runner-output') } : {}),
      reporter: [[reporter]], projects: [{ ...profile(), retries: 0 }],
    })}};`);
    const run = spawnSync(process.execPath, [join(root, 'node_modules/@playwright/test/cli.js'), 'test', '--config', join(directory, 'config.mjs')], {
      cwd: directory, encoding: 'utf8', env: { ...process.env, TARGET_SHA: 'a'.repeat(40), COGLATAS_REAL_BACKEND_MOBILE: '1' },
    });
    assert.equal(run.status, 1);
    assert.equal(readFileSync(join(directory, 'test-results/host-sentinel'), 'utf8'), 'host-owned');
    const output = run.stdout + run.stderr;
    assert.equal(output.includes('private-canary'), false);
    assert.match(output, /Mobile owner outcome:/);
    assert.equal(existsSync(join(directory, 'artifacts/mobile/owners.json')), false);
    const diagnostics = readFileSync(join(directory, 'artifacts/mobile/diagnostics.json'), 'utf8');
    assert.equal(diagnostics.includes('private-canary'), false);
    const parsed = JSON.parse(diagnostics);
    assert.equal(parsed.outcomes.length, 4);
    assert.equal(parsed.outcomes[0].failedStepId, 'MSG-01');
    assert.equal(parsed.outcomes[0].failureKind, 'ASSERTION');
    assert.equal(parsed.outcomes[0].status, 'failed');
    assert.equal(parsed.outcomes.slice(1).every((row) => row.status === 'passed'), true);
  } finally {
    assert.equal(dirname(resolve(directory)), resolve(tmpdir()));
    assert.match(basename(directory), /^coglatas-mobile-reporter-/);
    rmSync(directory, { recursive: true, force: true });
  }
});

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
