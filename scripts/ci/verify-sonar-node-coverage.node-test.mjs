import assert from 'node:assert/strict';
import test from 'node:test';
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve, sep } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { readOwnedRepositoryText, runCoverageCli, trustedGitExecutable, validateNodeCoverage } from './verify-sonar-node-coverage.mjs';

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
    assert.ok(hits.some(value => value > 0)); assert.ok(hits.includes(0));
  }
});


function ownedDirectory(t, prefix) {
  const temp = realpathSync(tmpdir()), directory = realpathSync(mkdtempSync(join(temp, prefix)));
  const assertOwned = () => { assert.ok(directory.startsWith(temp + sep)); assert.ok(directory.slice(temp.length + 1).startsWith(prefix)); };
  assertOwned(); t.after(() => { assertOwned(); rmSync(directory, { recursive: true, force: true }); });
  return directory;
}

await test('absolute Git selection is independent of PATH and rejects implicit executable configuration', () => {
  const absolute = process.platform === 'win32' ? 'D:/ApprovedTools/Git/cmd/git.exe' : '/opt/approved-git/bin/git';
  assert.equal(trustedGitExecutable(absolute), absolute);
  for (const value of ['', 'git', '../git', './git']) assert.throws(() => trustedGitExecutable(value));
  if (process.platform === 'win32') {
    for (const value of ['/ApprovedTools/git.exe', String.raw`\ApprovedTools\git.exe`]) assert.throws(() => trustedGitExecutable(value));
    const unc = String.raw`\\server\share\git.exe`; assert.equal(trustedGitExecutable(unc), unc);
  }
});

await test('regular repository reads reject traversal, directories and symlinked parents before content access', t => {
  const directory = ownedDirectory(t, 'coglatas-node-map-reader-'), outside = ownedDirectory(t, 'coglatas-node-map-outside-');
  writeFileSync(join(directory, 'normal.mjs'), 'ordinary source\n');
  writeFileSync(join(outside, 'private.mjs'), 'SYNTHETIC_OUTSIDE_CONTENT\n');
  assert.equal(readOwnedRepositoryText(directory, 'normal.mjs'), 'ordinary source\n');
  assert.throws(() => readOwnedRepositoryText(directory, outside));
  assert.throws(() => readOwnedRepositoryText(directory, '../outside'));
  assert.throws(() => readOwnedRepositoryText(directory, '.'));
  mkdirSync(join(directory, 'folder')); assert.throws(() => readOwnedRepositoryText(directory, 'folder'));
  symlinkSync(outside, join(directory, 'alias'), process.platform === 'win32' ? 'junction' : 'dir');
  assert.throws(() => readOwnedRepositoryText(directory, 'alias/private.mjs'));
  if (process.platform !== 'win32') {
    symlinkSync(join(outside, 'private.mjs'), join(directory, 'file-alias.mjs'));
    assert.throws(() => readOwnedRepositoryText(directory, 'file-alias.mjs'));
  }
});

await test('actual CLI boundary accepts only its producer report and preserves malformed/missing/private input denial', t => {
  const directory = ownedDirectory(t, 'coglatas-node-map-cli-');
  mkdirSync(join(directory, 'artifacts/sonar-node'), { recursive: true });
  writeFileSync(join(directory, '.gitignore'), 'artifacts/\n'); writeFileSync(join(directory, 'subject.mjs'), options.read());
  const git = args => execFileSync(trustedGitExecutable(), args, { cwd: directory, encoding: 'utf8' });
  git(['init', '--quiet']); git(['add', '.']);
  git(['-c', 'user.name=Synthetic Source Control', '-c', 'user.email=synthetic@example.test', 'commit', '--quiet', '-m', 'Synthetic coverage fixture']);
  const report = join(directory, 'artifacts/sonar-node/lcov.info'), logs = [];
  const invocation = { root: directory, args: ['artifacts/sonar-node/lcov.info'], info: value => logs.push(value), error: value => logs.push(value) };
  writeFileSync(report, record); assert.equal(runCoverageCli(invocation), 0); assert.match(logs.pop(), /"sourceFiles":1/);
  for (const args of [[], ['../outside'], [report], ['./artifacts/sonar-node/lcov.info'], ['artifacts/sonar-node/lcov.info', 'extra']]) {
    assert.equal(runCoverageCli({ ...invocation, args }), 1);
  }
  writeFileSync(report, 'SF:DO_NOT_ECHO_COVERAGE_BODY\nend_of_record\n');
  assert.equal(runCoverageCli(invocation), 1); assert.doesNotMatch(logs.join('\n'), /DO_NOT_ECHO_COVERAGE_BODY/);
  writeFileSync(report, record);
  const previousPath = process.env.PATH;
  const shadow = join(directory, 'artifacts');
  for (const name of ['git', 'git.exe', 'git.com']) writeFileSync(join(shadow, name), 'INERT_EXECUTABLE_CANDIDATE\n');
  try { process.env.PATH = shadow; assert.equal(runCoverageCli(invocation), 0); } finally { process.env.PATH = previousPath; }
});

await test('script aliases cannot bypass CLI validation and canonical module root ignores caller cwd', t => {
  const directory = ownedDirectory(t, 'coglatas-node-map-alias-'), command = join(root, 'scripts/ci/verify-sonar-node-coverage.mjs');
  symlinkSync(join(root, 'scripts'), join(directory, 'scripts-alias'), process.platform === 'win32' ? 'junction' : 'dir');
  const alias = join(directory, 'scripts-alias/ci/verify-sonar-node-coverage.mjs');
  for (const candidate of [command, alias]) {
    const result = spawnSync(process.execPath, [candidate, '../DO_NOT_READ_OUTSIDE'], { cwd: directory, encoding: 'utf8' });
    assert.equal(result.status, 1); assert.equal(result.stdout, ''); assert.doesNotMatch(result.stderr, /DO_NOT_READ_OUTSIDE/);
    assert.match(result.stderr, /Native Node coverage must/);
  }
});

await test('native fixture filters omit only their external generated families and retain tracked uncovered source', { timeout: 15000 }, t => {
  const directory = ownedDirectory(t, 'coglatas-node-fixture-filter-');
  const temp = realpathSync(tmpdir()), repository = join(directory, 'repository');
  mkdirSync(repository);
  const nested = 'group-keyboard-control-tracked'; mkdirSync(join(repository, nested));
  const calledSource = 'export function called(flag) {\n  if (flag) return 1;\n  return 0;\n}\n';
  const uncoveredSource = 'export function uncalled() {\n  return 2;\n}\n';
  writeFileSync(join(repository, 'called.mjs'), calledSource);
  writeFileSync(join(repository, nested, 'uncovered.mjs'), uncoveredSource);
  const families = ['coglatas-thread-matrix-', 'group-keyboard-control-', 'search-drawer-control-'];
  const fixtures = families.map(prefix => ownedDirectory(t, prefix));
  const nearMatch = ownedDirectory(t, 'group-keyboard-controlx-');
  for (const fixture of [...fixtures, nearMatch]) writeFileSync(join(fixture, 'fixture.mjs'), 'export const fixture = 1;\n');
  const entry = join(repository, 'control.node-test.mjs');
  const imports = fixtures.map(fixture => `import ${JSON.stringify(pathToFileURL(join(fixture, 'fixture.mjs')).href)};`).join('\n');
  const body = `import assert from 'node:assert/strict';\nimport test from 'node:test';\nimport {called} from './called.mjs';\nimport './${nested}/uncovered.mjs';\n${imports}\nawait test('synthetic fixture selection', () => assert.equal(called(true), 1));\n`;
  writeFileSync(entry, body);
  const git = args => execFileSync(trustedGitExecutable(), args, { cwd: repository, encoding: 'utf8' });
  git(['init', '--quiet']); git(['add', '.']);
  const environment = { ...process.env }; delete environment.NODE_V8_COVERAGE; delete environment.NODE_OPTIONS; delete environment.NODE_TEST_CONTEXT;
  const filters = families.map(prefix => '--test-coverage-exclude=' + join(temp, prefix + '*/**').replaceAll('\\', '/'));
  const execute = (name, filter) => {
    const output = join(directory, name + '.lcov');
    const result = spawnSync(process.execPath, ['--enable-source-maps', '--test', '--experimental-test-coverage', '--test-coverage-exclude=**/*.node-test.mjs',
      ...(filter ? filters : []), '--test-reporter=lcov', '--test-reporter-destination=' + output, entry],
    { cwd: repository, env: environment, encoding: 'utf8', timeout: 8000 });
    assert.equal(result.status, 0, 'Synthetic native producer must complete without suppressing a test failure.');
    return readFileSync(output, 'utf8');
  };
  const raw = execute('unfiltered', false);
  assert.throws(() => validateNodeCoverage(raw, { root: repository }), { code: 'SOURCE' });
  const filtered = execute('external-families-only', true);
  const result = validateNodeCoverage(filtered, { root: repository });
  assert.equal(result.sourceFiles, 2); assert.ok(result.uncoveredBranches > 0);
  const portable = filtered.replaceAll('\\', '/');
  assert.match(portable, /SF:called\.mjs\r?\n/);
  assert.match(portable, /SF:group-keyboard-control-tracked\/uncovered\.mjs\r?\n/);
  const uncovered = portable.split('end_of_record').find(block => block.includes('SF:' + nested + '/uncovered.mjs'));
  assert.match(uncovered, /FNDA:0,uncalled/); assert.match(uncovered, /DA:2,0/);
  for (const prefix of families) assert.ok(raw.includes(prefix), 'External generated fixture must actually execute before filtering.');
  assert.equal(filtered.includes('../'), false);
  writeFileSync(entry, body + `import ${JSON.stringify(pathToFileURL(join(nearMatch, 'fixture.mjs')).href)};\n`);
  const unexpected = execute('near-match', true);
  assert.ok(unexpected.includes('group-keyboard-controlx-'));
  assert.throws(() => validateNodeCoverage(unexpected, { root: repository }), { code: 'SOURCE' });
});