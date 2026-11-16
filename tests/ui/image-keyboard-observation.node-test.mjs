import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, renameSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import test from 'node:test';
import * as yaml from 'js-yaml';
import { compareCodeUnits } from './test-support/code-unit-order.mjs';
import { collectImageKeyboardObservations } from '../../scripts/ci/collect-image-keyboard-observations.mjs';
import { imageKeyboardByteLimit, imageKeyboardFilename, imageKeyboardInputDirectory, imageKeyboardOutputDirectory,
  observeImageKeyboardScroll, validateImageKeyboardObservation, withImageKeyboardObservation } from './helpers/image-keyboard-observation.mjs';

const event = (kind = 'INITIAL') => ({ kind, key: null, defaultPrevented: null, trusted: null,
  viewportConnected: true, sameViewport: true, targetIsViewport: false, isFocused: true, focusWithin: true,
  documentFocused: true, imageComplete: true, imageConnected: true, overflowX: 'AUTO', overflowY: 'AUTO',
  milliseconds: 100, scrollLeft: 20, scrollTop: 20, scrollWidth: 2400, scrollHeight: 1200,
  clientWidth: 350, clientHeight: 384, naturalWidth: 2400, naturalHeight: 1200,
  displayedWidth: 2400, displayedHeight: 1200 });
const runIdentity = { id: '43692134220', attempt: '1', job: 'frontend-test' };
const observation = (attempt = 0, bodyOutcome = 'FAILED') => ({ schemaVersion: 1,
  scope: 'MOCKED_UI_IMAGE_SCROLL_ONLY', profile: 'chromium-mobile', width: 1536, image: 'WIDE', attempt, bodyOutcome,
  captureState: 'CAPTURED', classification: 'UNKNOWN', events: [event(), event('FINAL')], truncated: false });
const scratch = () => mkdtempSync(join(tmpdir(), 'image-keyboard-control-'));
function repository(root) {
  const git = (...args) => execFileSync('git', args, { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  writeFileSync(join(root, '.gitignore'), 'artifacts/\nrunner-output/\ntest-results/\n');
  git('init', '--quiet'); git('add', '.'); git('-c', 'user.name=Control', '-c', 'user.email=control@example.test', 'commit', '--quiet', '-m', 'synthetic collector fixture');
  const sha = git('rev-parse', 'HEAD');
  mkdirSync(join(root, 'artifacts/ci'), { recursive: true });
  writeFileSync(join(root, 'artifacts/ci/frontend-build-sha'), sha + '\n');
  return sha;
}
function input(root, value) {
  mkdirSync(join(root, imageKeyboardInputDirectory), { recursive: true });
  writeFileSync(join(root, imageKeyboardInputDirectory, imageKeyboardFilename(value.profile, value.width, value.image, value.attempt)), JSON.stringify(value));
}

// Node's test runner owns completion and reports callback failures.
void test('closed image geometry rejects raw text, unknown identities and unbounded observations', () => {
  const value = { ...observation(), events: [event(), ...Array.from({ length: 62 }, () => event('SCROLL')), event('FINAL')] };
  assert.deepEqual(validateImageKeyboardObservation(value), value);
  assert.ok(Buffer.byteLength(JSON.stringify(value)) <= imageKeyboardByteLimit);
  const mutations = [{ ...value, url: 'private-canary' }, { ...value, profile: 'private-canary' },
    { ...value, image: 'private-canary' }, { ...value, width: 1000000 }, { ...value, attempt: 3 },
    { ...value, classification: 'CAUSE_PROVEN' }, { ...value, events: [...value.events, event()] },
    ...[{ html: 'private-canary' }, { key: 'private-canary' }, { kind: 'private-canary' },
      { documentFocused: 'private-canary' }, { scrollTop: -1 }, { scrollWidth: Infinity },
      { naturalHeight: 1000001 }, { scrollLeft: 0.5 }, { overflowX: 'private-canary' },
      { trusted: 'private-canary' }].map((change) => ({ ...observation(), events: [{ ...event(), ...change }, event('FINAL')] }))];
  for (const changed of mutations) { assert.throws(() => validateImageKeyboardObservation(changed)); }
});

void test('truncation, unavailable and empty observations cannot claim complete evidence or a cause', () => {
  for (const changed of [{ ...observation(), truncated: true },
    { ...observation(), captureState: 'UNAVAILABLE', events: [] }, { ...observation(), events: [] }]) {
    assert.throws(() => validateImageKeyboardObservation(changed));
    assert.equal(validateImageKeyboardObservation({ ...changed, classification: 'INCOMPLETE' }).classification, 'INCOMPLETE');
  }
  assert.equal(validateImageKeyboardObservation(observation(0, 'COMPLETED')).classification, 'UNKNOWN');
});

void test('publication and capture exceptions preserve the identical mandatory primary and fail otherwise-successful bodies', async () => {
  const primary = new Error('private-primary'), secondary = new Error('private-secondary');
  let outcome;
  await assert.rejects(withImageKeyboardObservation(async () => { throw primary; }, async (value) => {
    outcome = value; throw secondary;
  }), (error) => error === primary);
  assert.equal(outcome, 'FAILED');
  await assert.rejects(withImageKeyboardObservation(async () => {}, async () => { throw secondary; }),
    (error) => error === secondary);
});

void test('actual case helper publishes explicit unavailable capture before preserving the primary error', async () => {
  const root = scratch(), previous = process.cwd(), primary = new Error('private-primary');
  try {
    process.chdir(root);
    const page = { addInitScript: async () => {}, evaluate: async () => { throw new Error('private-capture'); } };
    await assert.rejects(observeImageKeyboardScroll(page, { project: { name: 'chromium-mobile' }, retry: 0 }, 1536, 'WIDE',
      async () => { throw primary; }), (error) => error === primary);
    const text = readFileSync(join(root, imageKeyboardInputDirectory, 'chromium-mobile-1536-wide-attempt-0.json'), 'utf8');
    const value = JSON.parse(text);
    assert.equal(value.bodyOutcome, 'FAILED'); assert.equal(value.captureState, 'UNAVAILABLE');
    assert.equal(value.classification, 'INCOMPLETE'); assert.equal(text.includes('private'), false);
  } finally { process.chdir(previous); rmSync(root, { recursive: true, force: true }); }
});

void test('actual Playwright first failure and later retry success retain separate immutable attempt files', () => {
  const root = scratch(), source = process.cwd();
  try {
    writeFileSync(join(root, 'package.json'), JSON.stringify({ type: 'module' }));
    writeFileSync(join(root, 'fixture.spec.mjs'), `import {test} from ${JSON.stringify(pathToFileURL(join(source, 'node_modules/@playwright/test/index.mjs')).href)};
      import {observeImageKeyboardScroll} from ${JSON.stringify(pathToFileURL(join(source, 'tests/ui/helpers/image-keyboard-observation.mjs')).href)};
      test('mechanical retry-retention control only', async ({},info)=>{let installed=false;
        await observeImageKeyboardScroll({evaluate:async()=>{if(!installed){installed=true;return true;}return (${JSON.stringify({ events: [event(), event('FINAL')], truncated: false })});}}, info, 1536, 'WIDE',
          async()=>{if(info.retry===0)throw new Error('private-primary');});
      });`);
    writeFileSync(join(root, 'quiet.mjs'), 'export default class { onEnd(){} }');
    writeFileSync(join(root, 'config.mjs'), `export default ${JSON.stringify({ build: { external: ['**/*.mjs'] }, testDir: root, testMatch: 'fixture.spec.mjs',
      outputDir: join(root, 'runner-output'), workers: 1, retries: 1, reporter: [[join(root, 'quiet.mjs')]],
      projects: [{ name: 'chromium-mobile' }],
    })};`);
    const sha = repository(root);
    const run = spawnSync(process.execPath, [join(source, 'node_modules/@playwright/test/cli.js'), 'test', '--config', join(root, 'config.mjs')],
      { cwd: root, encoding: 'utf8' });
    assert.equal(run.status, 0); assert.equal((run.stdout + run.stderr).includes('private'), false);
    const inputNames = readdirSync(join(root, imageKeyboardInputDirectory)).sort(compareCodeUnits);
    assert.deepEqual(inputNames, ['chromium-mobile-1536-wide-attempt-0.json', 'chromium-mobile-1536-wide-attempt-1.json']);
    assert.equal(collectImageKeyboardObservations(root, sha, runIdentity), 2);
    const output = inputNames.map((name) => JSON.parse(readFileSync(join(root, imageKeyboardOutputDirectory, name), 'utf8')));
    assert.deepEqual(output.map((value) => [value.observation.attempt, value.observation.bodyOutcome]), [[0, 'FAILED'], [1, 'COMPLETED']]);
    assert.equal(output.every((value) => value.sourceSha === sha && value.producerSha === sha && JSON.stringify(value.run) === JSON.stringify(runIdentity) && value.observation.classification === 'UNKNOWN'), true);
    assert.throws(() => collectImageKeyboardObservations(root, sha, runIdentity), 'old validated receipts are not overwritten');
  } finally { rmSync(root, { recursive: true, force: true }); }
});

void test('collector rejects raw data, identity mismatches, dirty source and stale output before any publication', () => {
  for (const mutation of ['raw', 'wrong-name', 'oversized', 'producer', 'source', 'dirty', 'stale']) {
    const root = scratch();
    try {
      const sha = repository(root); input(root, observation());
      const file = join(root, imageKeyboardInputDirectory, 'chromium-mobile-1536-wide-attempt-0.json');
      if (mutation === 'raw') { writeFileSync(file, JSON.stringify({ ...observation(), cookie: 'private-canary' })); }
      if (mutation === 'wrong-name') { writeFileSync(join(root, imageKeyboardInputDirectory, 'private-canary.json'), '{}'); }
      if (mutation === 'oversized') { writeFileSync(file, 'x'.repeat(imageKeyboardByteLimit + 1)); }
      if (mutation === 'producer') { writeFileSync(join(root, 'artifacts/ci/frontend-build-sha'), 'a'.repeat(40)); }
      if (mutation === 'dirty') { writeFileSync(join(root, '.gitignore'), 'private-canary'); }
      if (mutation === 'stale') { mkdirSync(join(root, imageKeyboardOutputDirectory)); }
      const run = spawnSync(process.execPath, [join(process.cwd(), 'scripts/ci/collect-image-keyboard-observations.mjs')], {
        cwd: root, encoding: 'utf8', env: { ...process.env, GITHUB_SHA: mutation === 'source' ? 'b'.repeat(40) : sha, GITHUB_RUN_ID: runIdentity.id, GITHUB_RUN_ATTEMPT: runIdentity.attempt, GITHUB_JOB: runIdentity.job },
      });
      assert.equal(run.status, 1); assert.equal((run.stdout + run.stderr).includes('private-canary'), false);
      assert.equal(existsSync(join(root, imageKeyboardOutputDirectory, 'chromium-mobile-1536-wide-attempt-0.json')), false);
    } finally { rmSync(root, { recursive: true, force: true }); }
  }
});

void test('collector rejects a special-file acquisition race and bounds same-inode growth without blocking or publishing', () => {
  for (const mutation of ['special-file', 'same-inode-growth']) {
    const root = scratch();
    try {
      const sha = repository(root); input(root, observation());
      const file = join(root, imageKeyboardInputDirectory, 'chromium-mobile-1536-wide-attempt-0.json');
      const collector = pathToFileURL(join(process.cwd(), 'scripts/ci/collect-image-keyboard-observations.mjs')).href;
      const script = `import assert from 'node:assert/strict'; import fs from 'node:fs';
        import {syncBuiltinESMExports} from 'node:module'; import {execFileSync} from 'node:child_process';
        const file=${JSON.stringify(file)}, mutation=${JSON.stringify(mutation)}, originalStat=fs.lstatSync,
          originalOpen=fs.openSync, originalRead=fs.readSync;
        let swapped=false, descriptor=null, readCalls=0, maximumRequested=0;
        fs.lstatSync=(path,...args)=>{
          const value=originalStat(path,...args);
          if(path===file && mutation==='special-file' && !swapped){swapped=true;fs.unlinkSync(file);
            if(process.platform==='win32'){fs.mkdirSync(file);}else{execFileSync('mkfifo',[file]);}}
          return value;
        };
        fs.openSync=(path,flags,...args)=>{if(path===file && process.platform!=='win32'){
          assert.equal((flags&fs.constants.O_NONBLOCK)!==0,true);}
          const fd=originalOpen(path,flags,...args);if(path===file)descriptor=fd;return fd;};
        fs.readSync=(fd,buffer,offset,length,position)=>{
          if(fd===descriptor && mutation==='same-inode-growth'){
            if(readCalls===0)fs.appendFileSync(file,'x'.repeat(${imageKeyboardByteLimit * 8}));
            readCalls++;maximumRequested=Math.max(maximumRequested,length);
          }
          return originalRead(fd,buffer,offset,length,position);
        };
        syncBuiltinESMExports();
        const {collectImageKeyboardObservations}=await import(${JSON.stringify(collector)});
        assert.throws(()=>collectImageKeyboardObservations(${JSON.stringify(root)},${JSON.stringify(sha)},${JSON.stringify(runIdentity)}));
        assert.equal(fs.existsSync(${JSON.stringify(join(root, imageKeyboardOutputDirectory))}),false);
        if(mutation==='special-file')assert.equal(swapped,true);
        else{assert.equal(readCalls,1);assert.equal(maximumRequested,${imageKeyboardByteLimit + 1});}
        process.stdout.write('bounded rejection verified\\n');`;
      const run = spawnSync(process.execPath, ['--input-type=module', '-e', script], { encoding: 'utf8', timeout: 2000 });
      assert.equal(run.error, undefined); assert.equal(run.status, 0);
      assert.equal(run.stdout, 'bounded rejection verified\n');
    } finally { rmSync(root, { recursive: true, force: true }); }
  }
});

void test('collector refuses a linked artifacts parent before publication', () => {
  const root = scratch();
  try {
    const sha = repository(root); input(root, observation());
    const relocated = join(root, 'relocated-artifacts');
    renameSync(join(root, 'artifacts'), relocated);
    symlinkSync(relocated, join(root, 'artifacts'), 'junction');
    assert.throws(() => collectImageKeyboardObservations(root, sha, runIdentity));
    assert.equal(existsSync(join(relocated, 'image-keyboard-observation-validated')), false);
  } finally { rmSync(root, { recursive: true, force: true }); }
});

void test('CI retains only validated finite lifecycle files after retry-success without expanding general browser publication', () => {
  const workflow = yaml.load(readFileSync('.github/workflows/ci.yml', 'utf8'));
  const steps = workflow.jobs['frontend-test'].steps;
  const collect = steps.find((step) => step.id === 'collect-image-keyboard-observations');
  const upload = steps.find((step) => step.with?.name === 'image-keyboard-observations-${{ github.run_id }}-${{ github.run_attempt }}');
  assert.equal(collect.if, "always() && needs.frontend-build.outputs.frontend_playwright == 'true'");
  assert.equal(collect.run, 'node scripts/ci/collect-image-keyboard-observations.mjs');
  assert.equal(upload.if, "always() && needs.frontend-build.outputs.frontend_playwright == 'true' && steps.collect-image-keyboard-observations.outcome == 'success'");
  assert.equal(upload.with.path, 'artifacts/image-keyboard-observation-validated/*.json');
  assert.equal(upload.with['retention-days'], 7);
  assert.equal(collect['continue-on-error'], undefined);
  assert.equal(collect['timeout-minutes'], 2);
  assert.equal(upload['timeout-minutes'], 2);
  assert.equal(upload['continue-on-error'], undefined);
  const general = steps.find((step) => step.with?.name === 'playwright-ui-test-artifacts');
  assert.equal(general.if, "failure() && needs.frontend-build.outputs.frontend_playwright == 'true'");
  assert.equal(general.with.path.trim(), 'playwright-report/\ntest-results/');
  assert.match(readFileSync('tests/ui/run-angular-playwright.mjs', 'utf8'),
    /'--test',[\s\S]*'tests\/ui\/image-keyboard-observation\.node-test\.mjs'/);
});

void test('run identity is exact, bounded and distinct from the Playwright retry identity', () => {
  for (const run of [null, { ...runIdentity, id: '' }, { ...runIdentity, id: '01' },
    { ...runIdentity, id: 43692134220 }, { ...runIdentity, id: '43692134220\n' },
    { ...runIdentity, attempt: 1 }, { ...runIdentity, attempt: '1\n' },
    { ...runIdentity, attempt: '0' }, { ...runIdentity, job: 'main-frontend-test' },
    { ...runIdentity, url: 'private-canary' }]) {
    const root = scratch();
    try {
      const sha = repository(root); input(root, observation());
      assert.throws(() => collectImageKeyboardObservations(root, sha, run));
      assert.equal(existsSync(join(root, imageKeyboardOutputDirectory)), false);
    } finally { rmSync(root, { recursive: true, force: true }); }
  }
});

void test('all native profiles, widths, images and retries have noncolliding closed filenames', () => {
  const names = new Set();
  for (const profile of ['chromium-desktop', 'chromium-mobile', 'firefox-desktop', 'webkit-desktop', 'webkit-mobile', 'narrow-320']) {
    for (const width of [320, 390, 768, 1366, 1536]) {
      for (const image of ['WIDE', 'PORTRAIT']) {
        for (const attempt of [0, 1, 2]) {
          const value = { ...observation(attempt), profile, width, image };
          validateImageKeyboardObservation(value);
          names.add(imageKeyboardFilename(profile, width, image, attempt));
        }
      }
    }
  }
  assert.equal(names.size, 180);
});
