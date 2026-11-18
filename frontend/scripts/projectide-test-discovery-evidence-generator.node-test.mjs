import test, { after } from 'node:test';
import { createHash } from 'node:crypto';
import assert from 'node:assert/strict';
import { readFileSync, writeFileSync, copyFileSync, readdirSync, mkdirSync, mkdtempSync, rmSync } from 'node:fs';
import { resolve, join, dirname, basename } from 'node:path';
import { fileURLToPath } from 'node:url';
import { tmpdir } from 'node:os';
import { spawnSync } from 'node:child_process';

// Disposable DTO mutation/file-system controls only; no Core execution or pin-publication credit.
const frontend = fileURLToPath(new URL('../', import.meta.url));
const originalAssets = join(frontend, 'public/assets/projectide');
const sourceGenerator = join(frontend, 'scripts/generate-projectide-test-discovery-evidence.mjs');
const existingEvidence = readFileSync(join(frontend, 'src/app/features/projectide/projectide-test-discovery.evidence.ts'));
const profiles = readdirSync(originalAssets).filter(name => name.endsWith('.json')).sort();
const temporaryRoot = mkdtempSync(join(tmpdir(), 'projectide-evidence-generator-'));
const isolatedFrontend = join(temporaryRoot, 'frontend');
const isolatedGenerator = join(isolatedFrontend, 'scripts/generate-projectide-test-discovery-evidence.mjs');
const output = join(isolatedFrontend, 'src/app/features/projectide/projectide-test-discovery.evidence.ts');
const producer = join(temporaryRoot, 'producer');
const cleanup = () => { const root = resolve(temporaryRoot); assert.equal(dirname(root), resolve(tmpdir())); assert.match(basename(root), /^projectide-evidence-generator-/); rmSync(root, { recursive: true, force: true }); };
const restore = () => { for (const profile of profiles) copyFileSync(join(originalAssets, profile), join(producer, profile)); writeFileSync(output, existingEvidence); };
try {
  mkdirSync(dirname(isolatedGenerator), { recursive: true }); mkdirSync(dirname(output), { recursive: true }); mkdirSync(producer);
  copyFileSync(sourceGenerator, isolatedGenerator); restore();
} catch (error) {
  try { cleanup(); } catch (cleanupFailure) { error.cause = cleanupFailure; }
  throw error;
}
after(cleanup);
const run = (preload) => spawnSync(process.execPath, [...(preload ? ['--require', preload] : []), isolatedGenerator, '--producer-dir', producer], { encoding: 'utf8', maxBuffer: 1048576 });
const unchanged = () => { assert.ok(readFileSync(output).equals(existingEvidence), 'evidence bytes must remain exact'); assert.deepEqual(readdirSync(dirname(output)).filter(name => name.endsWith('.tmp')), []); };
const mutate = (name, change) => { const file = join(producer, name); const value = JSON.parse(readFileSync(file, 'utf8')); change(value); writeFileSync(file, JSON.stringify(value)); };
const failure = result => { assert.notEqual(result.status, 0, result.stdout); unchanged(); };

test('complete isolated sample DTOs preserve the exact existing evidence bytes', () => {
  restore(); const result = run(); assert.equal(result.status, 0, result.stderr); assert.ok(readFileSync(output).equals(existingEvidence), 'evidence bytes must remain exact'); unchanged();
});
test('a one-element UUID array is rejected before any evidence replacement', () => {
  restore(); mutate('execution-plan-v1.json', bundle => { const value = bundle.cases.find(c => c.key === 'tests-plan-budget-hypothesis'); const row = value.localProjectTestDiscovery.binding.retainedTests[0]; row.testId = [row.testId]; });
  const result = run(); failure(result); assert.match(result.stderr, /Unexpected retained identity/);
});
test('all expected filenames with empty case arrays cannot erase evidence', () => {
  restore(); for (const name of profiles) writeFileSync(join(producer, name), JSON.stringify({ cases: [] }));
  const result = run(); failure(result); assert.match(result.stderr, /Incomplete original Core case/);
});
test('one missing original case is refused even with every filename present', () => {
  restore(); mutate('test-discovery-v1.json', bundle => { bundle.cases = []; });
  const result = run(); failure(result); assert.match(result.stderr, /Incomplete original Core case/);
});
test('missing discovery metadata cannot pass by retaining the original case key', () => {
  restore(); mutate('test-discovery-v1.json', bundle => { delete bundle.cases[0].localProjectTestDiscovery; });
  const result = run(); failure(result); assert.match(result.stderr, /Incomplete original Core case/);
});
test('a partial temporary write failure preserves the previous evidence and cleans up', () => {
  restore(); const preload = join(temporaryRoot, 'fail-write.cjs');
  writeFileSync(preload, "const fs=require('node:fs'), original=fs.writeFileSync, output=" + JSON.stringify(output) + "; fs.writeFileSync=(file,data,options)=>{if(String(file)===output || String(file).endsWith('.tmp')){original(file,String(data).slice(0,17),options);throw Object.assign(new Error('injected temporary write failure'),{code:'ENOSPC'});}return original(file,data,options);};require('node:module').syncBuiltinESMExports();");
  const result = run(preload); failure(result); assert.match(result.stderr, /injected temporary write failure/);
});
test('a rename failure preserves the previous evidence and removes the temporary file', () => {
  restore(); const preload = join(temporaryRoot, 'fail-rename.cjs');
  writeFileSync(preload, "const fs=require('node:fs'); fs.renameSync=()=>{throw Object.assign(new Error('injected rename failure'),{code:'EACCES'});};require('node:module').syncBuiltinESMExports();");
  const result = run(preload); failure(result); assert.match(result.stderr, /injected rename failure/);
});


test('a hash-valid plan from a different compiler input is refused before replacing evidence', () => {
  restore(); const other = JSON.parse(readFileSync(join(producer, 'preview-v1.json'), 'utf8')).cases.find(value => value.key === 'tests-pass').localProjectTestDiscovery.plan;
  mutate('test-discovery-v1.json', bundle => { bundle.cases[0].localProjectTestDiscovery.plan = other; });
  const result = run(); failure(result); assert.match(result.stderr, /Original Core plan belongs to another compiler input/);
});


test('an incomplete or foreign definition roster cannot replace pins for a Source-retained Test', () => {
  for (const mode of ['empty', 'identity', 'body']) {
    restore(); mutate('test-discovery-v1.json', bundle => {
      const definitions = bundle.cases[0].localProjectTestDiscovery.definitions;
      if (mode === 'empty') definitions.length = 0;
      if (mode === 'identity') definitions[0].testId = 'ffffffff-ffff-ffff-ffff-ffffffffffff';
      if (mode === 'body') { const declaration = JSON.parse(definitions[0].text); declaration.displayName = 'foreign definition'; definitions[0].text = JSON.stringify(declaration); }
    });
    const result = run(); failure(result); assert.match(result.stderr, /Original Core definition roster/);
  }
});

test('different original definition bytes cannot replace pins for an Unavailable inventory', () => {
  for (const change of [text => text + ' ', text => ' ' + text, text => text.replace('Input assertion', '\\u0049nput assertion')]) {
    restore(); mutate('test-discovery-v1.json', bundle => {
      const value = bundle.cases.find(item => item.key === 'tests-discovery-unavailable');
      value.localProjectTestDiscovery.definitions[0].text = change(value.localProjectTestDiscovery.definitions[0].text);
    });
    const result = run(); failure(result); assert.match(result.stderr, /Original Core definition roster/);
  }
});

test('retained plan declarations must match the original identity and status roster', () => {
  for (const mode of ['invented', 'omitted', 'status', 'digest', 'unexpected-field', 'order']) {
    restore(); const target = mode === 'invented' ? JSON.parse(readFileSync(join(producer, 'test-discovery-v1.json'), 'utf8')).cases.find(value => value.key === 'tests-discovery-unavailable') :
      JSON.parse(readFileSync(join(producer, 'preview-v1.json'), 'utf8')).cases.find(value => value.key === 'tests-many');
    for (const name of profiles) mutate(name, bundle => { for (const value of bundle.cases) {
      if (value.inputDigest.value !== target.inputDigest.value || !value.localProjectTestDiscovery) continue;
      const projection = value.localProjectTestDiscovery; const plan = JSON.parse(projection.plan.text);
      if (mode === 'invented') plan.tests = [{ declarationDigest: { algorithm: 'sha-256', value: createHash('sha256').update('coglatas.project-test-declaration/1\0' + projection.definitions[0].text).digest('hex') }, status: 'Decoded' }];
      if (mode === 'omitted') plan.tests.pop();
      if (mode === 'status') plan.tests[0].status = 'Invalid';
      if (mode === 'digest') plan.tests[0].declarationDigest.value = '0'.repeat(64);
      if (mode === 'unexpected-field') plan.tests[0].unexpected = null;
      if (mode === 'order') plan.tests.reverse();
      projection.plan.text = JSON.stringify(plan); projection.plan.digest = { algorithm: 'sha-256', value: createHash('sha256').update('coglatas.project-test-plan/1\0' + projection.plan.text).digest('hex') };
    } });
    const result = run(); failure(result); assert.match(result.stderr, /Original Core plan definition roster/);
  }
});
