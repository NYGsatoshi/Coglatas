import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, renameSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import test from 'node:test';
import * as yaml from 'js-yaml';
import { collectGroupKeyboardObservations } from '../../scripts/ci/collect-group-keyboard-observations.mjs';
import { groupKeyboardByteLimit, groupKeyboardV1ByteLimit, groupKeyboardFilename, groupKeyboardInputDirectory, groupKeyboardOutputDirectory,
  observeGroupKeyboardCase, validateGroupKeyboardObservation, withGroupKeyboardObservation } from './helpers/group-keyboard-observation.mjs';

const event = () => ({ kind: 'KEYDOWN', key: 'HOME', target: 'LIST', active: 'LIST', documentFocused: true,
  defaultPrevented: false, milliseconds: 1000000, listTop: 1000000, listMax: 1000000, asideTop: 1000000, asideMax: 1000000 });
const eventV2 = () => ({ ...event(), kind: 'CAPTURE_MICROTASK', listNode: 4096, listConnected: true,
  trackedListNode: 4096, trackedListConnected: true, targetNode: 4096, targetConnected: true,
  activeNode: 4096, activeConnected: true, trusted: true });
const observation = (attempt = 0, bodyOutcome = 'FAILED') => ({ schemaVersion: 1,
  scope: 'MOCKED_UI_KEYBOARD_OBSERVATION_ONLY', profile: 'chromium-mobile', attempt, bodyOutcome,
  captureState: 'CAPTURED', classification: 'UNKNOWN', events: [event()], truncated: false });
const scratch = () => mkdtempSync(join(tmpdir(), 'group-keyboard-control-'));
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
  mkdirSync(join(root, groupKeyboardInputDirectory), { recursive: true });
  writeFileSync(join(root, groupKeyboardInputDirectory, groupKeyboardFilename(value.profile, value.attempt)), JSON.stringify(value));
}

test('closed keyboard metadata excludes raw fields and fits 128 events within the 32 KiB budget', () => {
  const value = { ...observation(), events: Array.from({ length: 128 }, event) };
  assert.deepEqual(validateGroupKeyboardObservation(value), value);
  assert.ok(Buffer.byteLength(JSON.stringify(value)) <= groupKeyboardV1ByteLimit);
  const mutations = [{ ...value, url: 'private-canary' }, { ...value, profile: 'private-canary' },
    { ...value, classification: 'CAUSE_PROVEN' }, { ...value, events: [...value.events, event()] },
    { ...value, events: [{ ...event(), html: 'private-canary' }] },
    { ...value, events: [{ ...event(), key: 'private-canary' }] },
    { ...value, events: [{ ...event(), documentFocused: 'private-canary' }] },
    { ...value, events: [{ ...event(), listTop: -1 }] }, { ...value, events: [{ ...event(), listTop: Infinity }] },
    { ...value, attempt: 3 }];
  for (const changed of mutations) { assert.throws(() => validateGroupKeyboardObservation(changed)); }
});

test('truncation, unavailable and empty observations cannot claim complete evidence or a cause', () => {
  for (const changed of [{ ...observation(), truncated: true },
    { ...observation(), captureState: 'UNAVAILABLE', events: [] }, { ...observation(), events: [] }]) {
    assert.throws(() => validateGroupKeyboardObservation(changed));
    assert.equal(validateGroupKeyboardObservation({ ...changed, classification: 'INCOMPLETE' }).classification, 'INCOMPLETE');
  }
  assert.equal(validateGroupKeyboardObservation(observation(0, 'COMPLETED')).classification, 'UNKNOWN');
});

test('v2 retains 128 bounded structural events while v1 remains a strict separate contract', () => {
  const maximumEvent = () => ({ ...eventV2(), key: 'SHIFT_TAB', target: 'SUMMARY', active: 'SUMMARY',
    listConnected: false, trackedListConnected: false, targetConnected: false, activeConnected: false, trusted: false });
  const value = { ...observation(), schemaVersion: 2, profile: 'chromium-desktop', bodyOutcome: 'COMPLETED',
    events: Array.from({ length: 128 }, maximumEvent) };
  assert.deepEqual(validateGroupKeyboardObservation(value), value);
  assert.ok(Buffer.byteLength(JSON.stringify(value)) > groupKeyboardV1ByteLimit);
  assert.ok(Buffer.byteLength(JSON.stringify(value)) <= groupKeyboardByteLimit);
  for (const changed of [
    { ...value, schemaVersion: 1 },
    { ...value, schemaVersion: 3 },
    { ...value, events: [...value.events, eventV2()] },
    { ...value, events: [{ ...eventV2(), kind: 'POST_KEYDOWN' }] },
    { ...value, events: [{ ...eventV2(), listNode: 4097 }] },
    { ...value, events: [{ ...eventV2(), activeNode: 0.5 }] },
    { ...value, events: [{ ...eventV2(), targetNode: 0, targetConnected: true }] },
    { ...value, events: [{ ...eventV2(), trusted: 'private-canary' }] },
    { ...value, events: [{ ...eventV2(), style: 'private-canary' }] },
    { ...observation(), events: [{ ...event(), kind: 'SCROLL_END' }] },
  ]) { assert.throws(() => validateGroupKeyboardObservation(changed)); }
  for (const kind of ['SCROLL_END', 'LIFECYCLE', 'CAPTURE_MICROTASK']) {
    assert.equal(validateGroupKeyboardObservation({ ...value,
      events: [{ ...eventV2(), kind }] }).events[0].kind, kind);
  }
});

test('v2 never labels a missing roster, truncation or unavailable capture complete', () => {
  const value = { ...observation(), schemaVersion: 2, events: [eventV2()] };
  for (const changed of [{ ...value, truncated: true }, { ...value, events: [] },
    { ...value, captureState: 'UNAVAILABLE', events: [] },
    { ...value, events: [{ ...eventV2(), listNode: 0, listConnected: false }] }]) {
    assert.throws(() => validateGroupKeyboardObservation(changed));
    assert.equal(validateGroupKeyboardObservation({ ...changed, classification: 'INCOMPLETE' }).classification, 'INCOMPLETE');
  }
});

test('collector preserves strict legacy raw-byte bounds and independently binds mixed versions', () => {
  for (const oversizedLegacy of [false, true]) {
    const root = scratch();
    try {
      const sha = repository(root), legacy = { ...observation(), profile: 'chromium-desktop' };
      input(root, legacy);
      if (oversizedLegacy) {
        const file = join(root, groupKeyboardInputDirectory, groupKeyboardFilename(legacy.profile, legacy.attempt));
        writeFileSync(file, JSON.stringify(legacy) + ' '.repeat(groupKeyboardV1ByteLimit));
        assert.throws(() => collectGroupKeyboardObservations(root, sha));
        assert.equal(existsSync(join(root, groupKeyboardOutputDirectory)), false);
      } else {
        input(root, { ...observation(), schemaVersion: 2, events: [eventV2()] });
        assert.equal(collectGroupKeyboardObservations(root, sha), 2);
        const outputs = readdirSync(join(root, groupKeyboardOutputDirectory)).map((name) =>
          JSON.parse(readFileSync(join(root, groupKeyboardOutputDirectory, name), 'utf8')));
        assert.deepEqual(outputs.map((entry) => entry.observation.schemaVersion), [1, 2]);
        assert.ok(outputs.every((entry) => entry.sourceSha === sha && entry.producerSha === sha));
      }
    } finally { rmSync(root, { recursive: true, force: true }); }
  }
});

test('publication and capture exceptions preserve the identical mandatory primary and fail otherwise-successful bodies', async () => {
  const primary = new Error('private-primary'), secondary = new Error('private-secondary');
  let outcome;
  await assert.rejects(withGroupKeyboardObservation(async () => { throw primary; }, async (value) => {
    outcome = value; throw secondary;
  }), (error) => error === primary);
  assert.equal(outcome, 'FAILED');
  await assert.rejects(withGroupKeyboardObservation(async () => {}, async () => { throw secondary; }),
    (error) => error === secondary);
});

test('actual case helper publishes explicit unavailable capture before preserving the primary error', async () => {
  const root = scratch(), previous = process.cwd(), primary = new Error('private-primary');
  try {
    process.chdir(root);
    const page = { addInitScript: async () => {}, evaluate: async () => { throw new Error('private-capture'); } };
    await assert.rejects(observeGroupKeyboardCase(page, { project: { name: 'chromium-mobile' }, retry: 0 },
      async () => { throw primary; }), (error) => error === primary);
    const text = readFileSync(join(root, groupKeyboardInputDirectory, 'chromium-mobile-attempt-0.json'), 'utf8');
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
      import {observeGroupKeyboardCase} from ${JSON.stringify(pathToFileURL(join(source, 'tests/ui/helpers/group-keyboard-observation.mjs')).href)};
      test('mechanical retry-retention control only', async ({},info)=>{
        await observeGroupKeyboardCase({addInitScript:async()=>{},evaluate:async()=>(${JSON.stringify({ events: [eventV2()], truncated: false })})}, info,
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
    const inputNames = readdirSync(join(root, groupKeyboardInputDirectory)).sort();
    assert.deepEqual(inputNames, ['chromium-mobile-attempt-0.json', 'chromium-mobile-attempt-1.json']);
    assert.equal(collectGroupKeyboardObservations(root, sha), 2);
    const output = inputNames.map((name) => JSON.parse(readFileSync(join(root, groupKeyboardOutputDirectory, name), 'utf8')));
    assert.deepEqual(output.map((value) => [value.observation.attempt, value.observation.bodyOutcome]), [[0, 'FAILED'], [1, 'COMPLETED']]);
    assert.equal(output.every((value) => value.sourceSha === sha && value.producerSha === sha && value.observation.classification === 'UNKNOWN'), true);
    assert.throws(() => collectGroupKeyboardObservations(root, sha), 'old validated receipts are not overwritten');
  } finally { rmSync(root, { recursive: true, force: true }); }
});

test('collector rejects raw data, identity mismatches, dirty source and stale output before any publication', () => {
  for (const mutation of ['raw', 'wrong-name', 'oversized', 'producer', 'source', 'dirty', 'stale']) {
    const root = scratch();
    try {
      const sha = repository(root); input(root, observation());
      const file = join(root, groupKeyboardInputDirectory, 'chromium-mobile-attempt-0.json');
      if (mutation === 'raw') { writeFileSync(file, JSON.stringify({ ...observation(), cookie: 'private-canary' })); }
      if (mutation === 'wrong-name') { writeFileSync(join(root, groupKeyboardInputDirectory, 'private-canary.json'), '{}'); }
      if (mutation === 'oversized') { writeFileSync(file, 'x'.repeat(groupKeyboardByteLimit + 1)); }
      if (mutation === 'producer') { writeFileSync(join(root, 'artifacts/ci/frontend-build-sha'), 'a'.repeat(40)); }
      if (mutation === 'dirty') { writeFileSync(join(root, '.gitignore'), 'private-canary'); }
      if (mutation === 'stale') { mkdirSync(join(root, groupKeyboardOutputDirectory)); }
      const run = spawnSync(process.execPath, [join(process.cwd(), 'scripts/ci/collect-group-keyboard-observations.mjs')], {
        cwd: root, encoding: 'utf8', env: { ...process.env, GITHUB_SHA: mutation === 'source' ? 'b'.repeat(40) : sha },
      });
      assert.equal(run.status, 1); assert.equal((run.stdout + run.stderr).includes('private-canary'), false);
      assert.equal(existsSync(join(root, groupKeyboardOutputDirectory, 'chromium-mobile-attempt-0.json')), false);
    } finally { rmSync(root, { recursive: true, force: true }); }
  }
});

test('collector rejects a special-file acquisition race and bounds same-inode growth without blocking or publishing', () => {
  for (const mutation of ['special-file', 'same-inode-growth']) {
    const root = scratch();
    try {
      const sha = repository(root); input(root, observation());
      const file = join(root, groupKeyboardInputDirectory, 'chromium-mobile-attempt-0.json');
      const collector = pathToFileURL(join(process.cwd(), 'scripts/ci/collect-group-keyboard-observations.mjs')).href;
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
            if(readCalls===0)fs.appendFileSync(file,'x'.repeat(${groupKeyboardByteLimit * 8}));
            readCalls++;maximumRequested=Math.max(maximumRequested,length);
          }
          return originalRead(fd,buffer,offset,length,position);
        };
        syncBuiltinESMExports();
        const {collectGroupKeyboardObservations}=await import(${JSON.stringify(collector)});
        assert.throws(()=>collectGroupKeyboardObservations(${JSON.stringify(root)},${JSON.stringify(sha)}));
        assert.equal(fs.existsSync(${JSON.stringify(join(root, groupKeyboardOutputDirectory))}),false);
        if(mutation==='special-file')assert.equal(swapped,true);
        else{assert.equal(readCalls,1);assert.equal(maximumRequested,${groupKeyboardByteLimit + 1});}
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
    assert.throws(() => collectGroupKeyboardObservations(root, sha));
    assert.equal(existsSync(join(relocated, 'group-keyboard-observation-validated')), false);
  } finally { rmSync(root, { recursive: true, force: true }); }
});

test('CI retains only validated finite files after retry-success without expanding general browser publication', () => {
  const workflow = yaml.load(readFileSync('.github/workflows/ci.yml', 'utf8'));
  const gate = workflow.jobs['frontend-test'];
  assert.equal(gate.name, 'frontend-test');
  assert.equal(gate.if, '${{ always() }}');
  assert.deepEqual(gate.needs, ['changes', 'frontend-build', 'frontend-checks', 'storybook-build']);
  assert.equal(gate.steps.at(-1).run, 'bash scripts/ci/check-frontend-aggregate.sh');
  const steps = workflow.jobs['frontend-checks'].steps;
  const collect = steps.find((step) => step.id === 'collect-group-keyboard-observations');
  const upload = steps.find((step) => step.with?.name === 'group-keyboard-observations');
  assert.equal(collect.if, "always() && needs.frontend-build.outputs.frontend_playwright == 'true'");
  assert.equal(collect.run, 'node scripts/ci/collect-group-keyboard-observations.mjs');
  assert.equal(upload.if, "always() && needs.frontend-build.outputs.frontend_playwright == 'true' && steps.collect-group-keyboard-observations.outcome == 'success'");
  assert.equal(upload.with.path, 'artifacts/group-keyboard-observation-validated/*.json');
  assert.equal(upload.with['retention-days'], 7);
  assert.equal(upload['continue-on-error'], undefined);
  const general = steps.find((step) => step.with?.name === 'playwright-ui-test-artifacts');
  assert.equal(general.if, "failure() && needs.frontend-build.outputs.frontend_playwright == 'true'");
  assert.equal(general.with.path.trim(), 'playwright-report/\ntest-results/');
  assert.match(readFileSync('tests/ui/run-angular-playwright.mjs', 'utf8'),
    /'--test',[\s\S]*'tests\/ui\/group-keyboard-observation\.node-test\.mjs'/);
});

test('Main retains only successfully validated keyboard JSON and rejects raw success publication', () => {
  const workflow = yaml.load(readFileSync('.github/workflows/main-validation.yml', 'utf8'));
  const original = workflow.jobs['main-frontend-test'].steps;
  const verify = (steps) => {
    const collect = steps.find((step) => step.id === 'collect-group-keyboard-observations');
    const upload = steps.find((step) => step.with?.name === 'main-group-keyboard-observations');
    assert.equal(collect.if, 'always()');
    assert.equal(collect.run, 'node scripts/ci/collect-group-keyboard-observations.mjs');
    assert.equal(collect['continue-on-error'], undefined);
    assert.equal(upload.if, "always() && steps.collect-group-keyboard-observations.outcome == 'success'");
    assert.equal(upload.with.path, 'artifacts/group-keyboard-observation-validated/*.json');
    assert.equal(upload.with['retention-days'], 7);
    assert.equal(upload.with['if-no-files-found'], 'ignore');
    assert.equal(upload['continue-on-error'], undefined);
    assert.equal(upload.uses, 'actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a');
    assert.ok(steps.indexOf(collect) < steps.indexOf(upload));
    assert.equal(steps.filter((step) => step.uses?.startsWith('actions/upload-artifact@')).length, 3);
    const search = steps.find((step) => step.with?.name === 'main-search-drawer-observations');
    assert.equal(search.if, "always() && steps.collect-search-drawer-observations.outcome == 'success'");
    assert.equal(search.with.path, 'artifacts/search-drawer-observation-validated/*.json');
    assert.equal(search['continue-on-error'], undefined);
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
    const collect = steps.find((step) => step.id === 'collect-group-keyboard-observations');
    const upload = steps.find((step) => step.with?.name === 'main-group-keyboard-observations');
    if (mutation === 'raw-success') { steps.find((step) => step.with?.name === 'main-frontend-browser-artifacts').if = 'always()'; }
    if (mutation === 'raw-path') { upload.with.path = 'test-results/'; }
    if (mutation === 'unvalidated') { upload.if = 'always()'; }
    if (mutation === 'failed-collector') { collect['continue-on-error'] = true; }
    if (mutation === 'pr-condition') { collect.if = "always() && needs.frontend-build.outputs.frontend_playwright == 'true'"; }
    if (mutation === 'extra-raw-upload') { steps.push({ uses: upload.uses, if: 'always()', with: { name: 'other', path: 'test-results/' } }); }
    assert.throws(() => verify(steps), `Main rejects ${mutation}`);
  }
});
