import assert from 'node:assert/strict';
import { lstatSync, mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { test } from 'node:test';
import { actorFile, childCompletionObservation, measurementBranch, nativeSummaryObservation, trustedExecutorPaths, validateActorCoverage, validateMeasurementAdmission, validateNativeSummary } from './sonar-actor-measurement.mjs';
import { measurementFrontendOutput } from './sonar-actor-frontend-output.mjs';
import { validateAdmission } from '../../tests/ui/run-real-backend-actor-replacement.mjs';

const source = 'a'.repeat(40), tree = 'b'.repeat(40), repository = 'NYGsatoshi/Coglatas';
const owner = repository.split('/')[0];
const baseline = () => ({ repository, owner, actor: owner, triggeringActor: owner,
  eventName: 'push', source, tree, checkout: source, checkoutTree: tree, ref: `refs/heads/${measurementBranch}`,
  event: { repository: { full_name: repository }, sender: { login: owner }, after: source, ref: `refs/heads/${measurementBranch}`, deleted: false },
  runId: '1', attempt: '1', analysisKind: 'pull_request', prNumber: '2', prBranch: measurementBranch, prBase: 'main',
  imageName: `coglatas-sonar-actor-measurement:${source}` });

void test('measurement admission preserves actual push identity and exact closed router selection', () => {
  validateMeasurementAdmission(baseline());
  const main = baseline(); Object.assign(main, { ref: 'refs/heads/main', analysisKind: 'main', prNumber: '', prBranch: '', prBase: '' }); main.event.ref = main.ref;
  validateMeasurementAdmission(main);
  for (const [key, value] of [['eventName', 'pull_request'], ['repository', 'external/Coglatas'], ['owner', 'bot'], ['actor', 'bot'], ['triggeringActor', 'bot'],
    ['ref', 'refs/heads/SecurityTestImplimation'], ['source', 'c'.repeat(40)], ['checkout', 'c'.repeat(40)], ['checkoutTree', 'c'.repeat(40)],
    ['tree', 'bad'], ['analysisKind', 'main'], ['prNumber', '0'], ['prBranch', 'held-candidate'], ['prBase', 'other'], ['runId', '../1'],
    ['attempt', '0'], ['imageName', `coglatas-main-runtime:${source}`]]) assert.throws(() => validateMeasurementAdmission({ ...baseline(), [key]: value }), key);
  for (const event of [{ ...baseline().event, after: 'c'.repeat(40) }, { ...baseline().event, deleted: true },
    { ...baseline().event, ref: 'refs/heads/main' }, { ...baseline().event, sender: { login: 'bot' } }]) assert.throws(() => validateMeasurementAdmission({ ...baseline(), event }));
});

void test('canonical Main and PR admission both continue to reject the measurement namespace', () => {
  const shared = { source, head: source, sourceTree: tree, headTree: tree, checkout: source, artifactSource: source,
    dotnetSource: source, frontendSource: source, runId: '1', attempt: '1', imageName: baseline().imageName,
    producerImage: `sha256:${'c'.repeat(64)}`, engineImage: `sha256:${'c'.repeat(64)}` };
  assert.throws(() => validateAdmission({ ...shared, event: 'push', ref: 'refs/heads/main' }));
  assert.throws(() => validateAdmission({ ...shared, event: 'pull_request', artifactHead: source, parents: ['d'.repeat(40), source] }));
});

// These are parser controls, never measured execution or coverage evidence.
const code = readFileSync(resolve(actorFile), 'utf8');
const lineCount = code.split('\n').length - Number(code.endsWith('\n'));
const parserFixture = () => ['TN:', `SF:${actorFile}`, 'FN:18,check', 'FN:87,run', 'FNDA:0,check', 'FNDA:1,run', 'FNF:2', 'FNH:1',
  'BRDA:18,0,0,0', 'BRDA:87,1,0,1', 'BRF:2', 'BRH:1', ...Array.from({ length: lineCount }, (_, index) => `DA:${index + 1},${index === 17 ? 0 : 1}`),
  `LH:${lineCount - 1}`, `LF:${lineCount}`, 'end_of_record', ''].join('\n');

void test('strict actor LCOV rejects missing, duplicate, foreign, malformed, dropped-miss and fabricated-total rows', () => {
  const original = parserFixture();
  const finite = validateActorCoverage(original, code);
  assert.equal(finite.linesHit, lineCount - 1); assert.equal(finite.functionsHit, 1); assert.equal(finite.branchesHit, 1);
  for (const text of ['', original + original, original.replace(actorFile, '../outside.mjs'), original.replace('BRDA:18,0,0,0', 'BRDA:18,0,0,-1'),
    original.replace('FNDA:1,run', 'FNDA:0,run'), original.replace('FNH:1', 'FNH:2'), original.replace('DA:18,0\n', ''),
    original.replace('DA:18,0', 'DA:18,0\nDA:18,0'), original.replace('BRDA:18,0,0,0', 'BRDA:18,0,0,0\nBRDA:18,0,0,0'),
    original.replace('FN:18,check', 'FN:18,check\nFN:18,check'), original.replace('LF:', 'LEAK:'), original.replace('end_of_record', 'end_of_record\nprivate diagnostic'),
    original.replace('LH:', 'LH:9007199254740992\nLH:')]) assert.throws(() => validateActorCoverage(text, code));
  assert.throws(() => validateActorCoverage(original, code + '\n'));
});

void test('native summary requires one completed implicit case and rejects failed/cancelled/skipped/duplicate summaries', () => {
  const original = [['tests', 1], ['suites', 0], ['pass', 1], ['fail', 0], ['cancelled', 0], ['skipped', 0], ['todo', 0]].map(([name, count]) => `ℹ ${name} ${count}`).join('\n');
  validateNativeSummary(original);
  for (const text of ['', original + '\nℹ tests 1', original.replace('pass 1', 'pass 0'), original.replace('fail 0', 'fail 1'),
    original.replace('cancelled 0', 'cancelled 1'), original.replace('skipped 0', 'skipped 1'), original.replace('tests 1', 'tests 17')]) assert.throws(() => validateNativeSummary(text));
});

void test('actual native coverage directory named tests cannot masquerade as a second terminal summary', () => {
  const parent=realpathSync(tmpdir()), root=mkdtempSync(join(parent,'coglatas-summary-control-'));
  try {
    mkdirSync(join(root,'tests'));
    writeFileSync(join(root,'tests','control.mjs'),"console.log('synthetic summary fixture');\n");
    const env={...process.env};delete env.NODE_TEST_CONTEXT; // This owned fixture starts a distinct native test runner.
    const child=spawnSync(process.execPath,['--test','--experimental-test-coverage','--test-concurrency=1',
      '--test-coverage-include=tests/control.mjs','--test-reporter=spec','tests/control.mjs'],{cwd:root,env,encoding:'utf8',maxBuffer:1024*1024});
    assert.equal(child.status,0);assert.equal(child.signal,null);
    assert.match(child.stdout,/^ℹ tests +\| +\| +\| +\| *$/mu);
    validateNativeSummary(child.stdout);
    const directory=child.stdout.split(/\r?\n/).find(row=>/^ℹ tests +\| +\| +\| +\| *$/u.test(row));
    for(const invalid of [child.stdout+'\nℹ tests 1',child.stdout+'\nℹ tests bad',child.stdout+'\nℹ fail 0',
      child.stdout.replace('ℹ fail 0','ℹ fail 1'),child.stdout.replace('ℹ cancelled 0','ℹ cancelled 1'),
      child.stdout.replace('ℹ skipped 0','ℹ skipped 1'),child.stdout.replace(directory,directory+'\n'+directory),
      child.stdout.replace('ℹ start of coverage report','missing start marker'),
      child.stdout.replace('ℹ end of coverage report','missing end marker'),
      child.stdout.replace('ℹ end of coverage report','ℹ fail 1\nℹ end of coverage report')]) assert.throws(()=>validateNativeSummary(invalid));
  } finally {
    assert.equal(lstatSync(root).isSymbolicLink(),false);
    assert.equal(dirname(realpathSync(root)),parent);
    rmSync(root,{recursive:true,force:true});
  }
});

void test('summary observations retain only closed bounded counters and an ANSI-presence boolean', () => {
  const exact = 'ℹ tests 1\nℹ pass 1\nℹ fail 0';
  assert.deepEqual(nativeSummaryObservation(exact), { expectedRows: { tests: 1, suites: 0, pass: 1, fail: 1, cancelled: 0, skipped: 0, todo: 0 }, ansiEscapePresent: false });
  const observed = nativeSummaryObservation(exact + '\nℹ tests 1\nℹ tests 1\nprivate=canary\nℹ tests 99999999\n\u001b[32mℹ pass 1');
  assert.equal(observed.expectedRows.tests, 2);
  assert.equal(observed.ansiEscapePresent, true);
  assert.doesNotMatch(JSON.stringify(observed), /canary|99999999|private/);
  assert.throws(() => validateNativeSummary(exact));
});

void test('child completion observations cannot emit raw signals, arguments or diagnostics', () => {
  assert.deepEqual(childCompletionObservation({exitCode:0,signal:null}),{exitCode:0,signal:'NONE'});
  assert.deepEqual(childCompletionObservation({exitCode:null,signal:'SIGTERM'}),{exitCode:null,signal:'SIGTERM'});
  for(const value of [{exitCode:-1,signal:'private-canary'}, {exitCode:256,signal:'secret-token'},
    {exitCode:'password',signal:{toString(){throw new Error('private-canary');}}}]) {
    const observed=childCompletionObservation(value);
    assert.deepEqual(observed,{exitCode:null,signal:'OTHER'});
    assert.doesNotMatch(JSON.stringify(observed),/private|secret|password|token/);
  }
});

void test('measurement output follows actual Angular production configuration and canonical producer source together', () => {
  const angular = JSON.parse(readFileSync('frontend/angular.json', 'utf8'));
  const producer = readFileSync('.github/workflows/main-build-artifacts.yml', 'utf8');
  assert.equal(measurementFrontendOutput(angular, producer), 'frontend/dist/coglatas-web');
  for (const output of [{ base: 'dist/frontend', browser: 'browser' }, { base: '../outside', browser: '' },
    { base: 'dist/coglatas-web', browser: 'other' }, 'dist/coglatas-web']) {
    const changed = structuredClone(angular); changed.projects.frontend.architect.build.options.outputPath = output;
    assert.throws(() => measurementFrontendOutput(changed, producer));
  }
  const changed = structuredClone(angular); changed.projects.frontend.architect.build.configurations.production.outputPath = { base: 'dist/other', browser: '' };
  assert.throws(() => measurementFrontendOutput(changed, producer));
  assert.throws(() => measurementFrontendOutput(angular, producer.replaceAll('frontend/dist/coglatas-web', 'frontend/dist/other')));
  assert.throws(() => measurementFrontendOutput(angular, producer + '\n          cp -a frontend/dist/coglatas-web/. artifacts/main-runtime/frontend/\n'));
});

void test('hosted executor roots are closed and never accept arbitrary env or PATH executables', () => {
  const node = '/opt/hostedtoolcache/node/24.22.1/x64/bin/node';
  const paths = trustedExecutorPaths(node, '/usr/share/dotnet');
  assert.equal(paths.node, node);
  assert.equal(paths.npm, '/opt/hostedtoolcache/node/24.22.1/x64/bin/npm');
  assert.equal(paths.npmCli, '/opt/hostedtoolcache/node/24.22.1/x64/lib/node_modules/npm/bin/npm-cli.js');
  assert.equal(paths.dotnet, '/usr/share/dotnet/dotnet');
  assert.equal(paths.docker, '/usr/bin/docker'); assert.equal(paths.python, '/usr/bin/python3');
  for (const value of ['node', '/tmp/node', '/opt/hostedtoolcache/node/24.22.1/x64/bin/../node',
    '/opt/hostedtoolcache/node/26.0.0/x64/bin/node', '/opt/hostedtoolcache/node/24.22.1/arm64/bin/node']) assert.throws(() => trustedExecutorPaths(value, '/usr/share/dotnet'));
  for (const root of [undefined, 'dotnet', '/tmp/dotnet', '/usr/share/dotnet/../dotnet']) assert.throws(() => trustedExecutorPaths(node, root));
});
