import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, renameSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import test from 'node:test';
import * as yaml from 'js-yaml';
import { compareCodeUnits } from './test-support/code-unit-order.mjs';
import { collectSearchDrawerObservations } from '../../scripts/ci/collect-search-drawer-observations.mjs';
import { searchDrawerByteLimit, searchDrawerFilename, searchDrawerInputDirectory, searchDrawerOutputDirectory,
  observeSearchDrawerCase, validateSearchDrawerObservation, withSearchDrawerObservation } from './helpers/search-drawer-observation.mjs';

const event = () => ({ kind: 'KEYDOWN', key: 'ENTER', target: 'OPENER', targetNode: 4096,
  targetConnected: true, active: 'OPENER', activeNode: 4096, documentFocused: true,
  defaultPrevented: false, milliseconds: 1000000, hostNode: 4096, trackedHostNode: 4096,
  trackedHostConnected: true, openerNode: 4096, drawerNode: 0, panelExpanded: true,
  drawerOpen: false, parentLoading: false, parentDenied: false });
const observation = (attempt = 0, bodyOutcome = 'FAILED') => ({ schemaVersion: 1,
  scope: 'MOCKED_UI_SEARCH_LIFECYCLE_ONLY', profile: 'chromium-mobile', attempt, bodyOutcome,
  captureState: 'CAPTURED', classification: 'UNKNOWN', events: [event()], truncated: false });
const scratch = () => mkdtempSync(join(tmpdir(), 'search-drawer-control-'));
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
  mkdirSync(join(root, searchDrawerInputDirectory), { recursive: true });
  writeFileSync(join(root, searchDrawerInputDirectory, searchDrawerFilename(value.profile, value.attempt)), JSON.stringify(value));
}

test('closed lifecycle metadata excludes raw fields and fits 128 events within the 64 KiB budget', () => {
  const value = { ...observation(), events: Array.from({ length: 128 }, event) };
  assert.deepEqual(validateSearchDrawerObservation(value), value);
  assert.equal(validateSearchDrawerObservation({ ...observation(),
    events: [{ ...event(), kind: 'CAPTURE_MICROTASK' }] }).events[0].kind, 'CAPTURE_MICROTASK');
  assert.ok(Buffer.byteLength(JSON.stringify(value)) <= searchDrawerByteLimit);
  const mutations = [{ ...value, url: 'private-canary' }, { ...value, profile: 'private-canary' },
    { ...value, classification: 'CAUSE_PROVEN' }, { ...value, events: [...value.events, event()] },
    { ...value, events: [{ ...event(), html: 'private-canary' }] },
    { ...value, events: [{ ...event(), key: 'private-canary' }] },
    { ...value, events: [{ ...event(), documentFocused: 'private-canary' }] },
    { ...value, events: [{ ...event(), hostNode: -1 }] }, { ...value, events: [{ ...event(), hostNode: Infinity }] },
    { ...value, events: [{ ...event(), target: 'private-canary' }] },
    { ...value, events: [{ ...event(), activeNode: 4097 }] },
    { ...value, events: [{ ...event(), targetNode: 0.1 }] },
    { ...value, events: [{ ...event(), parentLoading: 'private-canary' }] },
    { ...value, events: [{ ...event(), drawerOpen: true }] },
    { ...value, events: [{ ...event(), kind: 'POST_KEYDOWN' }] },
    { ...value, attempt: 3 }];
  for (const changed of mutations) { assert.throws(() => validateSearchDrawerObservation(changed)); }
});

test('truncation, unavailable and empty observations cannot claim complete evidence or a cause', () => {
  for (const changed of [{ ...observation(), truncated: true },
    { ...observation(), captureState: 'UNAVAILABLE', events: [] }, { ...observation(), events: [] }]) {
    assert.throws(() => validateSearchDrawerObservation(changed));
    assert.equal(validateSearchDrawerObservation({ ...changed, classification: 'INCOMPLETE' }).classification, 'INCOMPLETE');
  }
  assert.equal(validateSearchDrawerObservation(observation(0, 'COMPLETED')).classification, 'UNKNOWN');
});

test('publication and capture exceptions preserve the identical mandatory primary and fail otherwise-successful bodies', async () => {
  const primary = new Error('private-primary'), secondary = new Error('private-secondary');
  let outcome;
  await assert.rejects(withSearchDrawerObservation(async () => { throw primary; }, async (value) => {
    outcome = value; throw secondary;
  }), (error) => error === primary);
  assert.equal(outcome, 'FAILED');
  await assert.rejects(withSearchDrawerObservation(async () => {}, async () => { throw secondary; }),
    (error) => error === secondary);
});

test('actual case helper publishes explicit unavailable capture before preserving the primary error', async () => {
  const root = scratch(), previous = process.cwd(), primary = new Error('private-primary');
  try {
    process.chdir(root);
    const page = { addInitScript: async () => {}, evaluate: async () => { throw new Error('private-capture'); } };
    await assert.rejects(observeSearchDrawerCase(page, { project: { name: 'chromium-mobile' }, retry: 0 },
      async () => { throw primary; }), (error) => error === primary);
    const text = readFileSync(join(root, searchDrawerInputDirectory, 'chromium-mobile-attempt-0.json'), 'utf8');
    const value = JSON.parse(text);
    assert.equal(value.bodyOutcome, 'FAILED'); assert.equal(value.captureState, 'UNAVAILABLE');
    assert.equal(value.classification, 'INCOMPLETE'); assert.equal(text.includes('private'), false);
  } finally { process.chdir(previous); rmSync(root, { recursive: true, force: true }); }
});

test('actual Playwright first failure and later retry success retain separate immutable attempt files', () => {
  const root = scratch(), source = process.cwd();
  try {
    writeFileSync(join(root, 'package.json'), JSON.stringify({ type: 'module' }));
    writeFileSync(join(root, 'fixture.spec.mjs'), `import {test} from ${JSON.stringify(pathToFileURL(join(source, 'node_modules/@playwright/test/index.mjs')).href)};
      import {observeSearchDrawerCase} from ${JSON.stringify(pathToFileURL(join(source, 'tests/ui/helpers/search-drawer-observation.mjs')).href)};
      test('mechanical retry-retention control only', async ({},info)=>{
        await observeSearchDrawerCase({addInitScript:async()=>{},evaluate:async()=>(${JSON.stringify({ events: [event()], truncated: false })})}, info,
          async()=>{if(info.retry===0)throw new Error('private-primary');});
      });`);
    writeFileSync(join(root, 'quiet.mjs'), 'export default class { onEnd(){} }');
    writeFileSync(join(root, 'config.mjs'), `export default ${JSON.stringify({ testDir: root, testMatch: 'fixture.spec.mjs',
      outputDir: join(root, 'runner-output'), workers: 1, retries: 1, reporter: [[join(root, 'quiet.mjs')]],
      projects: [{ name: 'chromium-mobile' }],
    })};`);
    const sha = repository(root);
    const run = spawnSync(process.execPath, [join(source, 'node_modules/@playwright/test/cli.js'), 'test', '--config', join(root, 'config.mjs')],
      { cwd: root, encoding: 'utf8' });
    assert.equal(run.status, 0); assert.equal((run.stdout + run.stderr).includes('private'), false);
    const inputNames = readdirSync(join(root, searchDrawerInputDirectory)).sort(compareCodeUnits);
    assert.deepEqual(inputNames, ['chromium-mobile-attempt-0.json', 'chromium-mobile-attempt-1.json']);
    assert.equal(collectSearchDrawerObservations(root, sha), 2);
    const output = inputNames.map((name) => JSON.parse(readFileSync(join(root, searchDrawerOutputDirectory, name), 'utf8')));
    assert.deepEqual(output.map((value) => [value.observation.attempt, value.observation.bodyOutcome]), [[0, 'FAILED'], [1, 'COMPLETED']]);
    assert.equal(output.every((value) => value.sourceSha === sha && value.producerSha === sha && value.observation.classification === 'UNKNOWN'), true);
    assert.throws(() => collectSearchDrawerObservations(root, sha), 'old validated receipts are not overwritten');
  } finally { rmSync(root, { recursive: true, force: true }); }
});

test('collector rejects raw data, identity mismatches, dirty source and stale output before any publication', () => {
  for (const mutation of ['raw', 'wrong-name', 'oversized', 'producer', 'source', 'dirty', 'stale']) {
    const root = scratch();
    try {
      const sha = repository(root); input(root, observation());
      const file = join(root, searchDrawerInputDirectory, 'chromium-mobile-attempt-0.json');
      if (mutation === 'raw') { writeFileSync(file, JSON.stringify({ ...observation(), cookie: 'private-canary' })); }
      if (mutation === 'wrong-name') { writeFileSync(join(root, searchDrawerInputDirectory, 'private-canary.json'), '{}'); }
      if (mutation === 'oversized') { writeFileSync(file, 'x'.repeat(searchDrawerByteLimit + 1)); }
      if (mutation === 'producer') { writeFileSync(join(root, 'artifacts/ci/frontend-build-sha'), 'a'.repeat(40)); }
      if (mutation === 'dirty') { writeFileSync(join(root, '.gitignore'), 'private-canary'); }
      if (mutation === 'stale') { mkdirSync(join(root, searchDrawerOutputDirectory)); }
      const run = spawnSync(process.execPath, [join(process.cwd(), 'scripts/ci/collect-search-drawer-observations.mjs')], {
        cwd: root, encoding: 'utf8', env: { ...process.env, GITHUB_SHA: mutation === 'source' ? 'b'.repeat(40) : sha },
      });
      assert.equal(run.status, 1); assert.equal((run.stdout + run.stderr).includes('private-canary'), false);
      assert.equal(existsSync(join(root, searchDrawerOutputDirectory, 'chromium-mobile-attempt-0.json')), false);
    } finally { rmSync(root, { recursive: true, force: true }); }
  }
});

test('collector rejects a special-file acquisition race and bounds same-inode growth without blocking or publishing', () => {
  for (const mutation of ['special-file', 'same-inode-growth']) {
    const root = scratch();
    try {
      const sha = repository(root); input(root, observation());
      const file = join(root, searchDrawerInputDirectory, 'chromium-mobile-attempt-0.json');
      const collector = pathToFileURL(join(process.cwd(), 'scripts/ci/collect-search-drawer-observations.mjs')).href;
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
            if(readCalls===0)fs.appendFileSync(file,'x'.repeat(${searchDrawerByteLimit * 8}));
            readCalls++;maximumRequested=Math.max(maximumRequested,length);
          }
          return originalRead(fd,buffer,offset,length,position);
        };
        syncBuiltinESMExports();
        const {collectSearchDrawerObservations}=await import(${JSON.stringify(collector)});
        assert.throws(()=>collectSearchDrawerObservations(${JSON.stringify(root)},${JSON.stringify(sha)}));
        assert.equal(fs.existsSync(${JSON.stringify(join(root, searchDrawerOutputDirectory))}),false);
        if(mutation==='special-file')assert.equal(swapped,true);
        else{assert.equal(readCalls,1);assert.equal(maximumRequested,${searchDrawerByteLimit + 1});}
        process.stdout.write('bounded rejection verified\\n');`;
      const run = spawnSync(process.execPath, ['--input-type=module', '-e', script], { encoding: 'utf8', timeout: 2000 });
      assert.equal(run.error, undefined); assert.equal(run.status, 0);
      assert.equal(run.stdout, 'bounded rejection verified\n');
    } finally { rmSync(root, { recursive: true, force: true }); }
  }
});

test('collector refuses a linked artifacts parent before publication', () => {
  const root = scratch();
  try {
    const sha = repository(root); input(root, observation());
    const relocated = join(root, 'relocated-artifacts');
    renameSync(join(root, 'artifacts'), relocated);
    symlinkSync(relocated, join(root, 'artifacts'), 'junction');
    assert.throws(() => collectSearchDrawerObservations(root, sha));
    assert.equal(existsSync(join(relocated, 'search-drawer-observation-validated')), false);
  } finally { rmSync(root, { recursive: true, force: true }); }
});

test('CI retains only validated finite lifecycle files after retry-success without expanding general browser publication', () => {
  const workflow = yaml.load(readFileSync('.github/workflows/ci.yml', 'utf8'));
  const steps = workflow.jobs['frontend-test'].steps;
  const collect = steps.find((step) => step.id === 'collect-search-drawer-observations');
  const upload = steps.find((step) => step.with?.name === 'search-drawer-observations');
  assert.equal(collect.if, "always() && needs.frontend-build.outputs.frontend_playwright == 'true'");
  assert.equal(collect.run, 'node scripts/ci/collect-search-drawer-observations.mjs');
  assert.equal(upload.if, "always() && needs.frontend-build.outputs.frontend_playwright == 'true' && steps.collect-search-drawer-observations.outcome == 'success'");
  assert.equal(upload.with.path, 'artifacts/search-drawer-observation-validated/*.json');
  assert.equal(upload.with['retention-days'], 7);
  assert.equal(upload['continue-on-error'], undefined);
  const general = steps.find((step) => step.with?.name === 'playwright-ui-test-artifacts');
  assert.equal(general.if, "failure() && needs.frontend-build.outputs.frontend_playwright == 'true'");
  assert.equal(general.with.path.trim(), 'playwright-report/\ntest-results/');
  assert.match(readFileSync('tests/ui/run-angular-playwright.mjs', 'utf8'),
    /'--test',[\s\S]*'tests\/ui\/search-drawer-observation\.node-test\.mjs'/);
});

test('Main retains only successfully validated lifecycle JSON and rejects raw success publication', () => {
  const workflow = yaml.load(readFileSync('.github/workflows/main-validation.yml', 'utf8'));
  const original = workflow.jobs['main-frontend-test'].steps;
  const verify = (steps) => {
    const collect = steps.find((step) => step.id === 'collect-search-drawer-observations');
    const upload = steps.find((step) => step.with?.name === 'main-search-drawer-observations');
    assert.equal(collect.if, 'always()');
    assert.equal(collect.run, 'node scripts/ci/collect-search-drawer-observations.mjs');
    assert.equal(collect['continue-on-error'], undefined);
    assert.equal(upload.if, "always() && steps.collect-search-drawer-observations.outcome == 'success'");
    assert.equal(upload.with.path, 'artifacts/search-drawer-observation-validated/*.json');
    assert.equal(upload.with['retention-days'], 7);
    assert.equal(upload.with['if-no-files-found'], 'ignore');
    assert.equal(upload['continue-on-error'], undefined);
    assert.equal(upload.uses, 'actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a');
    assert.ok(steps.indexOf(collect) < steps.indexOf(upload));
    assert.equal(steps.filter((step) => step.uses?.startsWith('actions/upload-artifact@')).length, 3);
    const general = steps.find((step) => step.with?.name === 'main-frontend-browser-artifacts');
    assert.equal(general.if, 'failure()');
    assert.equal(general.with.path.trim(), 'playwright-report/\ntest-results/');
    const restore = steps.find((step) => step.name === 'Restore prebuilt frontend outputs');
    assert.equal(restore.run, 'bash scripts/ci/restore-main-frontend-build.sh "$RUNNER_TEMP/main-build-artifacts" "$GITHUB_SHA" "$GITHUB_WORKSPACE"');
    assert.equal(steps.some((step) => /needs\.frontend-build/.test(step.if ?? '')), false);
  };
  verify(original);
  for (const mutation of ['raw-success', 'raw-path', 'unvalidated', 'failed-collector', 'pr-condition', 'extra-raw-upload']) {
    const steps = structuredClone(original);
    const collect = steps.find((step) => step.id === 'collect-search-drawer-observations');
    const upload = steps.find((step) => step.with?.name === 'main-search-drawer-observations');
    if (mutation === 'raw-success') { steps.find((step) => step.with?.name === 'main-frontend-browser-artifacts').if = 'always()'; }
    if (mutation === 'raw-path') { upload.with.path = 'test-results/'; }
    if (mutation === 'unvalidated') { upload.if = 'always()'; }
    if (mutation === 'failed-collector') { collect['continue-on-error'] = true; }
    if (mutation === 'pr-condition') { collect.if = "always() && needs.frontend-build.outputs.frontend_playwright == 'true'"; }
    if (mutation === 'extra-raw-upload') { steps.push({ uses: upload.uses, if: 'always()', with: { name: 'other', path: 'test-results/' } }); }
    assert.throws(() => verify(steps), `Main rejects ${mutation}`);
  }
});
