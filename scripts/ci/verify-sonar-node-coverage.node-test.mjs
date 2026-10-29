import assert from 'node:assert/strict';
import test from 'node:test';
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { validateNodeCoverage } from './verify-sonar-node-coverage.mjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const record = 'TN:\nSF:subject.mjs\nFN:1,subject\nBRDA:1,0,0,1\nBRDA:2,1,0,0\nBRF:2\nDA:1,1\nDA:2,0\nend_of_record\n';
const options = { root, tracked: new Set(['subject.mjs']), read: () => 'if (flag) {\n return false;\n}\n' };
await test('valid original lines preserve hit and uncovered branches without rewriting the report', () => {
  assert.deepEqual(validateNodeCoverage(record, options), { sourceFiles: 1, branches: 2, uncoveredBranches: 1 });
  assert.deepEqual(validateNodeCoverage(record.replace('2,1,0,0', '2,1,0,-'), options), { sourceFiles: 1, branches: 2, uncoveredBranches: 1 });
});
await test('undefined, negative, nonnumeric and out-of-bounds branch coordinates cannot be imported', () => {
  for (const value of ['undefined', '0', '-1', '4', '1.5']) assert.throws(() => validateNodeCoverage(record.replace('BRDA:1,', `BRDA:${value},`), options));
  assert.throws(() => validateNodeCoverage(record.replace('1,0,0,1', '1,0,0,-1'), options));
});
await test('line/function coordinates and branch totals must remain valid', () => {
  for (const [before, after] of [['DA:1,1', 'DA:4,1'], ['DA:1,1', 'DA:undefined,1'], ['FN:1,', 'FN:undefined,'], ['BRF:2', 'BRF:1']]) assert.throws(() => validateNodeCoverage(record.replace(before, after), options));
});
await test('missing, duplicate, outside-root and untracked sources cannot qualify', () => {
  for (const text of [record.replace('SF:subject.mjs\n', ''), record + record, record.replace('subject.mjs', '../outside.mjs'), record.replace('subject.mjs', 'untracked.mjs')]) assert.throws(() => validateNodeCoverage(text, options));
});
await test('tracked test bodies remain outside the operational coverage scope', () => {
  assert.throws(() => validateNodeCoverage(record.replace('subject.mjs', 'subject.node-test.mjs'), { ...options, tracked: new Set(['subject.node-test.mjs']) }));
});
await test('empty and truncated records cannot silently qualify', () => {
  for (const text of ['', record.replace('end_of_record\n', '')]) assert.throws(() => validateNodeCoverage(text, options));
});
await test('real source file and Git inventory are used by default', () => {
  const source = 'scripts/ci/verify-sonar-node-coverage.mjs';
  assert.deepEqual(validateNodeCoverage(record.replace('subject.mjs', source), { root }), { sourceFiles: 1, branches: 2, uncoveredBranches: 1 });
});
await test('native mapping repairs actual Playwright transforms while retaining every tracked source and uncovered control', { timeout: 90000 }, t => {
  const directory = realpathSync(mkdtempSync(join(tmpdir(), 'coglatas-node-map-control-'))), tempRoot = realpathSync(tmpdir());
  const assertOwned = () => { assert.ok(directory.startsWith(tempRoot + sep)); assert.ok(directory.slice(tempRoot.length + 1).startsWith('coglatas-node-map-control-')); };
  assertOwned(); t.after(() => { assertOwned(); rmSync(directory, { recursive: true, force: true }); });
  const environment = { ...process.env }; delete environment.NODE_V8_COVERAGE; delete environment.NODE_OPTIONS; delete environment.NODE_TEST_CONTEXT;
  const execute = mapped => {
    const output = join(directory, mapped ? 'mapped.lcov' : 'raw.lcov');
    const args = [...(mapped ? ['--enable-source-maps'] : []), '--test', '--experimental-test-coverage', '--test-coverage-exclude=**/*.node-test.mjs',
      '--test-coverage-exclude=' + join(tmpdir(), 'thread-secondary-reporter-*/**').replaceAll('\\', '/'),
      '--test-reporter=spec', '--test-reporter-destination=' + output + '.log', '--test-reporter=lcov', '--test-reporter-destination=' + output,
      'tests/functional/thread-probe-observation.node-test.mjs'];
    const result = spawnSync(process.execPath, args, { cwd: root, env: environment, encoding: 'utf8', timeout: 40000 });
    assert.equal(result.status, 0, 'The unchanged existing observer/reporter regressions must pass in both producer modes.');
    return readFileSync(output, 'utf8');
  };
  const raw = execute(false), mapped = execute(true);
  assert.match(raw, /BRDA:undefined,/);
  assert.throws(() => validateNodeCoverage(raw, { root }));
  const result = validateNodeCoverage(mapped, { root }); assert.ok(result.uncoveredBranches > 0);
  const paths = text => new Set(text.split(/\r?\n/).filter(row => row.startsWith('SF:')).map(row => row.slice(3)));
  const before = paths(raw), after = paths(mapped); for (const file of before) assert.ok(after.has(file), 'A measured repository source must not disappear.');
  for (const suffix of ['thread-probe-observation.mjs', 'mobile/outcome.mjs', 'mobile/reporter.mjs']) {
    const block = mapped.split('end_of_record').find(value => value.replaceAll('\\', '/').includes(suffix)); assert.ok(block);
    const hits = block.split(/\r?\n/).filter(row => row.startsWith('BRDA:')).map(row => Number(row.split(',')[3]));
    assert.ok(hits.some(value => value > 0)); assert.ok(hits.some(value => value === 0));
  }
});

await test('actual CLI accepts mapped source and rejects malformed or absent reports without echoing their contents', t => {
  const directory = realpathSync(mkdtempSync(join(tmpdir(), 'coglatas-node-map-cli-'))), tempRoot = realpathSync(tmpdir());
  const assertOwned = () => { assert.ok(directory.startsWith(tempRoot + sep)); assert.ok(directory.slice(tempRoot.length + 1).startsWith('coglatas-node-map-cli-')); };
  assertOwned(); t.after(() => { assertOwned(); rmSync(directory, { recursive: true, force: true }); });
  const report = join(directory, 'control.lcov'), command = join(root, 'scripts/ci/verify-sonar-node-coverage.mjs');
  writeFileSync(report, record.replace('subject.mjs', 'scripts/ci/verify-sonar-node-coverage.mjs'));
  const valid = spawnSync(process.execPath, [command, report], { cwd: root, encoding: 'utf8' });
  assert.equal(valid.status, 0); assert.match(valid.stdout, /"sourceFiles":1/);
  writeFileSync(report, 'SF:DO_NOT_ECHO_COVERAGE_BODY\nend_of_record\n');
  for (const args of [[command, report], [command]]) {
    const invalid = spawnSync(process.execPath, args, { cwd: root, encoding: 'utf8' });
    assert.equal(invalid.status, 1); assert.doesNotMatch(invalid.stdout + invalid.stderr, /DO_NOT_ECHO_COVERAGE_BODY/);
    assert.equal(invalid.stdout, ''); assert.match(invalid.stderr, /Native Node coverage must/);
  }
});
