import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { test } from 'node:test';
import { actorFile, measurementBranch, validateActorCoverage, validateMeasurementAdmission, validateNativeSummary } from './sonar-actor-measurement.mjs';
import { validateAdmission } from '../../tests/ui/run-real-backend-actor-replacement.mjs';

const source = 'a'.repeat(40), tree = 'b'.repeat(40);
const baseline = () => ({ repository: 'NYGsatoshi/Coglatas', owner: 'NYGsatoshi', actor: 'NYGsatoshi', triggeringActor: 'NYGsatoshi',
  eventName: 'push', source, tree, checkout: source, checkoutTree: tree, ref: `refs/heads/${measurementBranch}`,
  event: { repository: { full_name: 'NYGsatoshi/Coglatas' }, sender: { login: 'NYGsatoshi' }, after: source, ref: `refs/heads/${measurementBranch}`, deleted: false },
  runId: '1', attempt: '1', analysisKind: 'pull_request', prNumber: '2', prBranch: measurementBranch, prBase: 'main',
  imageName: `coglatas-sonar-actor-measurement:${source}` });

void test('measurement admission preserves actual push identity and exact closed router selection', () => {
  validateMeasurementAdmission(baseline());
  const main = baseline(); Object.assign(main, { ref: 'refs/heads/main', analysisKind: 'main', prNumber: '', prBranch: '', prBase: '' }); main.event.ref = main.ref;
  validateMeasurementAdmission(main);
  for (const [key, value] of [['eventName', 'pull_request'], ['repository', 'external/Coglatas'], ['actor', 'bot'], ['triggeringActor', 'bot'],
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
