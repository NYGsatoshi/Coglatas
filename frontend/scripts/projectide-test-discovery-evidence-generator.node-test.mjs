import test, { after } from 'node:test';
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
const temporaryRoot = mkdtempSync(join(tmpdir(), 'projectide-evidence-generator-'));
const isolatedFrontend = join(temporaryRoot, 'frontend');
const isolatedGenerator = join(isolatedFrontend, 'scripts/generate-projectide-test-discovery-evidence.mjs');
const output = join(isolatedFrontend, 'src/app/features/projectide/projectide-test-discovery.evidence.ts');
const profiles = readdirSync(originalAssets).filter(name => name.endsWith('.json')).sort();
const producer = join(temporaryRoot, 'producer');
mkdirSync(dirname(isolatedGenerator), { recursive: true }); mkdirSync(dirname(output), { recursive: true }); mkdirSync(producer);
copyFileSync(sourceGenerator, isolatedGenerator);
const restore = () => { for (const profile of profiles) copyFileSync(join(originalAssets, profile), join(producer, profile)); writeFileSync(output, existingEvidence); };
restore();
const run = (preload) => spawnSync(process.execPath, [...(preload ? ['--require', preload] : []), isolatedGenerator, '--producer-dir', producer], { encoding: 'utf8', maxBuffer: 1048576 });
const unchanged = () => { assert.ok(readFileSync(output).equals(existingEvidence), 'evidence bytes must remain exact'); assert.deepEqual(readdirSync(dirname(output)).filter(name => name.endsWith('.tmp')), []); };
const mutate = (name, change) => { const file = join(producer, name); const value = JSON.parse(readFileSync(file, 'utf8')); change(value); writeFileSync(file, JSON.stringify(value)); };
const failure = result => { assert.notEqual(result.status, 0, result.stdout); unchanged(); };
after(() => { const root = resolve(temporaryRoot); assert.equal(dirname(root), resolve(tmpdir())); assert.match(basename(root), /^projectide-evidence-generator-/); rmSync(root, { recursive: true, force: true }); });

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
